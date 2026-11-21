using System.Numerics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Frontend;

// Runs a type's static initialization at analysis time, when it is a closed
// computation: its static field and property initializers, in declaration
// order, then its static constructor, reading only constants, the statics it
// is setting, and the readonly statics of other types that are themselves
// closed. Such an initializer computes the same values on every run, so
// running it here decides whether it ever throws. When it finishes, it has no
// effect a body can observe, and its classification is None, or FreshValues
// when it allocated.
//
// Each operation is run with the exact C# semantics of its operand types, or
// the evaluation gives up and the type stays Effectful. Integer arithmetic is
// exact, including checked overflow, division by zero and the overflow of
// MinValue / -1. BigInteger arithmetic runs on the host's BigInteger. Floating
// point, decimal and 128-bit values are only stored: a conversion into one
// that cannot throw makes an opaque value, and any use of an opaque value
// other than storing it gives up, so no branch ever depends on rounding. The
// exceptions are floating-point arithmetic, conversions between floating
// types, unchecked conversions to integral types and a few Math functions of
// a double: none of them throws, and each yields another opaque value.
//
// Effects are bounded the same way the structural rules bound them: a store
// must go to a local, to the initializing type's own initialized-only static,
// or into a struct, object or array the current initialization made. Reading
// an array element or a mutable field of an object another initialization
// made gives up, because other code may have changed it since. Exception
// regions, throws, virtual and interface calls, generic code, boxing, strings
// other than constants, and every library call other than the BigInteger
// operators named below give up.
// A static's value as a closed initialization leaves it: a Boolean, an
// integral or enum value, or null as Scalar, or a struct's fields.
internal sealed class InitializedValue(ITypeSymbol type, object? scalar,
    ImmutableArray<(IFieldSymbol Field, InitializedValue Value)>? fields)
{
    internal ITypeSymbol Type { get; } = RepositoryParameterGuard.NotNull(type, nameof(type));
    internal object? Scalar { get; } = scalar;
    // Null for a scalar or null value.
    internal ImmutableArray<(IFieldSymbol Field, InitializedValue Value)>? Fields { get; } = fields;
}

// Every method the initialization ran may observe the static before it is
// set, so only code outside Runs may read it as Value.
internal sealed class InitializedStatic(InitializedValue value, ImmutableHashSet<IMethodSymbol> runs)
{
    internal InitializedValue Value { get; } = RepositoryParameterGuard.NotNull(value, nameof(value));
    internal ImmutableHashSet<IMethodSymbol> Runs { get; } = RepositoryParameterGuard.NotNull(runs, nameof(runs));
}

internal sealed class ClosedInitializerEvaluator(CSharpCompilation compilation, CancellationToken cancellationToken)
{
    private const int MaximumSteps = 1 << 17;
    private const int MaximumDepth = 64;
    private const int MaximumArrayLength = 1 << 16;
    private static readonly BigInteger MaximumDecimal = new(decimal.MaxValue);
    private static readonly BigInteger MaximumInt128 = (BigInteger.One << 127) - 1;
    private static readonly BigInteger MaximumUInt128 = (BigInteger.One << 128) - 1;

    private readonly CSharpCompilation _compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
    private readonly CSharpInvocationEmissionPolicy _emission = new(compilation);
    private readonly Dictionary<INamedTypeSymbol, Run?> _types = new(SymbolEqualityComparer.Default);
    private readonly List<Run> _active = [];
    private int _steps = MaximumSteps;
    private int _depth;
    private int _runs;

    // A value no operation may inspect: a floating-point, decimal or
    // 128-bit number. Equality is by reference, as storage only needs it.
    private sealed class Opaque(ITypeSymbol type)
    {
        internal ITypeSymbol Type { get; } = RepositoryParameterGuard.NotNull(type, nameof(type));
    }

    private sealed class StructValue(INamedTypeSymbol type, Dictionary<IFieldSymbol, object?> fields)
    {
        internal INamedTypeSymbol Type { get; } = RepositoryParameterGuard.NotNull(type, nameof(type));
        internal Dictionary<IFieldSymbol, object?> Fields { get; } = RepositoryParameterGuard.NotNull(fields, nameof(fields));

        internal StructValue Copy()
        {
            var fields = new Dictionary<IFieldSymbol, object?>(SymbolEqualityComparer.Default);
            foreach (var pair in Fields)
            { fields[pair.Key] = pair.Value is StructValue nested ? nested.Copy() : pair.Value; }
            return new(Type, fields);
        }
    }

    // A class instance or array, made by the run with the given id. Only that
    // run may change it.
    private sealed class ObjectValue(INamedTypeSymbol type, int run)
    {
        internal INamedTypeSymbol Type { get; } = RepositoryParameterGuard.NotNull(type, nameof(type));
        internal int Run { get; } = run;
        internal Dictionary<IFieldSymbol, object?> Fields { get; } = new(SymbolEqualityComparer.Default);
    }

    private sealed class ArrayValue(ITypeSymbol elementType, object?[] items, int run)
    {
        internal ITypeSymbol ElementType { get; } = RepositoryParameterGuard.NotNull(elementType, nameof(elementType));
        internal object?[] Items { get; } = RepositoryParameterGuard.NotNull(items, nameof(items));
        internal int Run { get; } = run;
    }

    private sealed class Run(INamedTypeSymbol type, int id)
    {
        internal INamedTypeSymbol Type { get; } = RepositoryParameterGuard.NotNull(type, nameof(type));
        internal int Id { get; } = id;
        internal Dictionary<IFieldSymbol, object?> Statics { get; } = new(SymbolEqualityComparer.Default);
        internal bool Allocates { get; set; }
        // Every source method and constructor the initialization ran,
        // including those of the initializations it started.
        internal HashSet<IMethodSymbol> Runs { get; } = new(SymbolEqualityComparer.Default);
    }

    private sealed class Frame(IMethodSymbol? method, object? instance)
    {
        internal IMethodSymbol? Method { get; } = method;
        internal object? Instance { get; } = instance;
        internal Dictionary<ISymbol, object?> Variables { get; } = new(SymbolEqualityComparer.Default);
        internal Dictionary<CaptureId, object?> Captures { get; } = [];
    }

    internal InitializationEffect Classify(INamedTypeSymbol type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type)).OriginalDefinition;
        return Initialize(type) is { } run
            ? run.Allocates ? InitializationEffect.FreshValues : InitializationEffect.None
            : InitializationEffect.Effectful;
    }

    // The value a finished initialization leaves in one of its type's
    // statics, when it is a Boolean, an integral or enum value, null, or a
    // struct made of such values; and every method the initialization ran.
    internal InitializedStatic? ReadInitializedStatic(IFieldSymbol field)
    {
        field = RepositoryParameterGuard.NotNull(field, nameof(field)).OriginalDefinition;
        if (!field.IsStatic || field.IsConst || Initialize(field.ContainingType) is not { } run)
        { return null; }
        if (!run.Statics.TryGetValue(field, out var value))
        {
            value = DefaultOf(field.Type, out var ok);
            if (!ok)
            { return null; }
        }
        return Describe(field.Type, value, 0) is { } described ? new(described, run.Runs.ToImmutableHashSet<IMethodSymbol>(SymbolEqualityComparer.Default)) : null;
    }

    private static InitializedValue? Describe(ITypeSymbol type, object? value, int depth)
    {
        switch (value)
        {
            case null:
                return type.IsReferenceType ? new(type, null, null) : null;
            case bool:
                return type.SpecialType == SpecialType.System_Boolean ? new(type, value, null) : null;
            case StructValue structure when depth < MaximumDepth &&
                SymbolEqualityComparer.Default.Equals(structure.Type, type.OriginalDefinition):
                var fields = ImmutableArray.CreateBuilder<(IFieldSymbol, InitializedValue)>(structure.Fields.Count);
                foreach (var pair in structure.Fields)
                {
                    if (Describe(pair.Key.Type, pair.Value, depth + 1) is not { } described)
                    { return null; }
                    fields.Add((pair.Key, described));
                }
                return new(type, null, fields.MoveToImmutable());
            default:
                return IsIntegral(type) && IsIntegralValue(value) ? new(type, value, null) : null;
        }
    }

    private bool Step()
    {
        cancellationToken.ThrowIfCancellationRequested();
        return --_steps >= 0;
    }

    private Run Current => _active[_active.Count - 1];

    // The finished initialization of a type, running it first if needed. A
    // type reached while its own initialization is running lies on a cycle.
    private Run? Initialize(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (_types.TryGetValue(type, out var known))
        {
            // The type's initializer may run as part of the current one.
            if (known != null && _active.Count != 0)
            {
                Current.Allocates |= known.Allocates;
                Current.Runs.UnionWith(known.Runs);
            }
            return known;
        }
        if (_active.Any(run => SymbolEqualityComparer.Default.Equals(run.Type, type)) || !Step() ||
            type.IsGenericType || type.ContainingType is { IsGenericType: true } || !IsSource(type) ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        { return null; }
        var run = new Run(type, ++_runs);
        _active.Add(run);
        var finished = false;
        try
        { finished = RunInitializers(run); }
        finally
        { _active.RemoveAt(_active.Count - 1); }
        if (finished && _active.Count != 0)
        {
            Current.Allocates |= run.Allocates;
            Current.Runs.UnionWith(run.Runs);
        }
        _types[type] = finished ? run : null;
        return finished ? run : null;
    }

    private bool IsSource(INamedTypeSymbol type)
    {
        return !type.DeclaringSyntaxReferences.IsEmpty &&
            type.DeclaringSyntaxReferences.All(reference => _compilation.ContainsSyntaxTree(reference.SyntaxTree));
    }

    private bool RunInitializers(Run run)
    {
        var type = run.Type;
        var initializers = new List<SyntaxNode>();
        IMethodSymbol? staticConstructor = null;
        foreach (var member in type.GetMembers())
        {
            if (!Step())
            { return false; }
            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.StaticConstructor, IsImplicitlyDeclared: false } constructor:
                    staticConstructor = constructor;
                    continue;
                case IFieldSymbol { IsStatic: true, IsConst: false }:
                    break;
                case IEventSymbol { IsStatic: true }:
                    return false;
                case IPropertySymbol { IsStatic: true }:
                    break;
                default:
                    continue;
            }
            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                var clause = reference.GetSyntax(cancellationToken) switch
                {
                    VariableDeclaratorSyntax variable => (SyntaxNode?)variable.Initializer,
                    PropertyDeclarationSyntax property => property.Initializer,
                    _ => null
                };
                // A backing field and its property share a declaration.
                if (clause != null && !initializers.Contains(clause))
                { initializers.Add(clause); }
            }
        }
        // Initializers run in textual order, which is undefined across the
        // files of a partial type.
        if (initializers.Select(static clause => clause.SyntaxTree).Distinct().Count() > 1)
        { return false; }
        foreach (var clause in initializers.OrderBy(static clause => clause.SpanStart))
        {
            if (Graph(clause) is not { } graph || !Execute(graph, new Frame(null, null), out _))
            { return false; }
        }
        if (staticConstructor == null)
        { return true; }
        if (staticConstructor.DeclaringSyntaxReferences.Length != 1 ||
            Graph(staticConstructor.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken)) is not { } body)
        { return false; }
        return Execute(body, new Frame(staticConstructor, null), out _);
    }

    private ControlFlowGraph? Graph(SyntaxNode node)
    {
        try
        {
            return ControlFlowGraph.Create(node, Host.CompilationModelProvider.GetSemanticModel(_compilation, node.SyntaxTree), cancellationToken);
        }
        catch (ArgumentException)
        { return null; }
    }

    // Runs a graph to its exit. Any exception region, throw or rethrow gives up.
    private bool Execute(ControlFlowGraph graph, Frame frame, out object? result)
    {
        result = null;
        if (HasExceptionRegion(graph.Root))
        { return false; }
        var block = graph.Blocks[0];
        while (true)
        {
            if (!Step())
            { return false; }
            foreach (var operation in block.Operations)
            {
                if (!Statement(operation, frame))
                { return false; }
            }
            if (block.Kind == BasicBlockKind.Exit)
            { return true; }
            ControlFlowBranch? branch;
            if (block.ConditionalSuccessor is { } conditional && block.BranchValue != null)
            {
                if (!Evaluate(block.BranchValue, frame, out var condition) || condition is not bool taken)
                { return false; }
                branch = taken == (block.ConditionKind == ControlFlowConditionKind.WhenTrue) ? conditional : block.FallThroughSuccessor;
            }
            else
            {
                branch = block.FallThroughSuccessor;
                if (branch?.Semantics == ControlFlowBranchSemantics.Return && block.BranchValue != null)
                {
                    if (!Evaluate(block.BranchValue, frame, out result))
                    { return false; }
                    result = Copy(result);
                }
            }
            if (branch is not { Semantics: ControlFlowBranchSemantics.Regular or ControlFlowBranchSemantics.Return, Destination: { } next })
            { return false; }
            block = next;
        }
    }

    private static bool HasExceptionRegion(ControlFlowRegion region)
    {
        return region.Kind is not (ControlFlowRegionKind.Root or ControlFlowRegionKind.LocalLifetime) ||
            region.NestedRegions.Any(HasExceptionRegion);
    }

    private bool Statement(IOperation operation, Frame frame)
    {
        switch (operation)
        {
            case IFlowCaptureOperation capture:
                if (!Evaluate(capture.Value, frame, out var captured))
                { return false; }
                frame.Captures[capture.Id] = captured;
                return true;
            // A block lists each statement's operation, sometimes wrapped. A
            // call the compiler omits, with its arguments, never runs.
            case IExpressionStatementOperation statement:
                return _emission.IsElided(statement) || Evaluate(statement.Operation, frame, out _);
            default:
                return _emission.IsElided(operation) || Evaluate(operation, frame, out _);
        }
    }

    private bool Evaluate(IOperation operation, Frame frame, out object? value)
    {
        value = null;
        if (!Step())
        { return false; }
        if (operation.ConstantValue.HasValue)
        { return Constant(operation.Type, operation.ConstantValue.Value, out value); }
        switch (operation)
        {
            case IFlowCaptureReferenceOperation reference:
                return frame.Captures.TryGetValue(reference.Id, out value);
            case IIsNullOperation test:
                if (!Evaluate(test.Operand, frame, out var tested) || tested is StructValue or Opaque || test.Operand.Type is { IsValueType: true })
                { return false; }
                value = tested == null;
                return true;
            case ILocalReferenceOperation { Local.RefKind: RefKind.None } local:
                return frame.Variables.TryGetValue(local.Local, out value);
            case IParameterReferenceOperation parameter:
                return frame.Variables.TryGetValue(parameter.Parameter, out value);
            case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }:
                value = frame.Instance;
                return value != null;
            case IFieldReferenceOperation field:
                return ReadField(field, frame, out value);
            case IPropertyReferenceOperation property:
                return ReadProperty(property, frame, out value);
            case IDefaultValueOperation defaultValue:
                value = DefaultOf(defaultValue.Type, out var ok);
                return ok;
            case IConversionOperation conversion:
                return Convert(conversion, frame, out value);
            case IBinaryOperation binary:
                return Binary(binary, frame, out value);
            case IUnaryOperation unary:
                return Unary(unary, frame, out value);
            case IInvocationOperation invocation:
                return Invoke(invocation, frame, out value);
            case IObjectCreationOperation creation:
                return Create(creation, frame, out value);
            case IArrayCreationOperation array:
                return CreateArray(array, frame, out value);
            case IArrayElementReferenceOperation element:
                if (!Element(element, frame, out var items, out var index) || items.Run != Current.Id)
                { return false; }
                value = Copy(items.Items[index]);
                return true;
            case ISimpleAssignmentOperation { IsRef: false } assignment:
                // C# resolves the target's array and index before the value.
                return Target(assignment.Target, frame, out var assigned, out var position) &&
                    Evaluate(assignment.Value, frame, out value) && StoreAt(assignment.Target, assigned, position, Copy(value), frame);
            case ICompoundAssignmentOperation compound:
                return Compound(compound, frame, out value);
            case IIncrementOrDecrementOperation increment:
                return Increment(increment, frame, out value);
            default:
                return false;
        }
    }

    private static object? Copy(object? value)
    { return value is StructValue structure ? structure.Copy() : value; }

    // ---- Values ----

    private static bool Constant(ITypeSymbol? type, object? constant, out object? value)
    {
        value = null;
        if (constant == null)
        { return type is null or { IsReferenceType: true }; }
        if (type == null)
        { return false; }
        var special = Underlying(type).SpecialType;
        switch (special)
        {
            case SpecialType.System_Boolean or SpecialType.System_String:
                value = constant;
                return true;
            case SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal:
                value = new Opaque(type);
                return true;
            default:
                if (!IsIntegral(type) || !IsIntegralValue(constant))
                { return false; }
                value = constant;
                return true;
        }
    }

    private static bool IsIntegralValue(object value)
    { return value is sbyte or byte or short or ushort or int or uint or long or ulong or char; }

    private static ITypeSymbol Underlying(ITypeSymbol type)
    { return type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying } ? underlying : type; }

    private static bool IsIntegral(ITypeSymbol type)
    {
        return Underlying(type).SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or
            SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or
            SpecialType.System_UInt64 or SpecialType.System_Char;
    }

    private static (int Bits, bool Signed) Width(ITypeSymbol type)
    {
        return Underlying(type).SpecialType switch
        {
            SpecialType.System_SByte => (8, true),
            SpecialType.System_Byte => (8, false),
            SpecialType.System_Int16 => (16, true),
            SpecialType.System_UInt16 or SpecialType.System_Char => (16, false),
            SpecialType.System_Int32 => (32, true),
            SpecialType.System_UInt32 => (32, false),
            SpecialType.System_Int64 => (64, true),
            _ => (64, false)
        };
    }

    private static BigInteger ToBig(object value)
    {
        return value switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => v,
            char v => v,
            _ => throw new ArgumentException("Not an integral value.", nameof(value))
        };
    }

    // The integral value of a type from an exact result: wrapped when
    // unchecked, or no value when checked and out of range.
    private static bool FromBig(BigInteger exact, ITypeSymbol type, bool isChecked, out object? value)
    {
        value = null;
        var (bits, signed) = Width(type);
        var modulus = BigInteger.One << bits;
        var minimum = signed ? -(modulus >> 1) : BigInteger.Zero;
        var maximum = signed ? (modulus >> 1) - 1 : modulus - 1;
        if (exact < minimum || exact > maximum)
        {
            if (isChecked)
            { return false; }
            exact = BigInteger.Remainder(exact - minimum, modulus);
            if (exact.Sign < 0)
            { exact += modulus; }
            exact += minimum;
        }
        value = Underlying(type).SpecialType switch
        {
            SpecialType.System_SByte => (sbyte)exact,
            SpecialType.System_Byte => (byte)exact,
            SpecialType.System_Int16 => (short)exact,
            SpecialType.System_UInt16 => (ushort)exact,
            SpecialType.System_Char => (char)(ushort)exact,
            SpecialType.System_Int32 => (int)exact,
            SpecialType.System_UInt32 => (uint)exact,
            SpecialType.System_Int64 => (long)exact,
            _ => (object)(ulong)exact
        };
        return true;
    }

    private object? DefaultOf(ITypeSymbol? type, out bool ok)
    {
        ok = true;
        if (type == null || type.IsReferenceType)
        { return null; }
        if (IsIntegral(type))
        {
            FromBig(BigInteger.Zero, type, isChecked: false, out var zero);
            return zero;
        }
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return false;
            case SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal:
                return new Opaque(type);
        }
        if (IsBigInteger(type))
        { return BigInteger.Zero; }
        if (IsWideInteger(type))
        { return new Opaque(type); }
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsGenericType: false } structure && IsSource(structure) &&
            !structure.IsRefLikeType && _depth < MaximumDepth)
        {
            _depth++;
            try
            {
                var fields = new Dictionary<IFieldSymbol, object?>(SymbolEqualityComparer.Default);
                foreach (var field in structure.GetMembers().OfType<IFieldSymbol>())
                {
                    if (field.IsStatic || field.IsConst)
                    { continue; }
                    // Overlapping fields share storage that named slots
                    // cannot model.
                    if (field.IsFixedSizeBuffer || !CSharpOperationSemantics.HasIndependentFieldStorage(field))
                    { ok = false; return null; }
                    fields[field] = DefaultOf(field.Type, out ok);
                    if (!ok)
                    { return null; }
                }
                return new StructValue(structure, fields);
            }
            finally
            { _depth--; }
        }
        ok = false;
        return null;
    }

    private bool IsLibrary(ITypeSymbol type)
    { return !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _compilation.Assembly); }

    private bool IsBigInteger(ITypeSymbol type)
    {
        return type is INamedTypeSymbol
        {
            Name: "BigInteger", Arity: 0,
            ContainingNamespace: { Name: "Numerics", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
        } && IsLibrary(type);
    }

    private bool IsWideInteger(ITypeSymbol type)
    {
        return type is INamedTypeSymbol
        {
            Name: "Int128" or "UInt128", Arity: 0, ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
        } && IsLibrary(type);
    }

    // ---- Conversions ----

    private bool Convert(IConversionOperation conversion, Frame frame, out object? value)
    {
        value = null;
        if (conversion.IsTryCast || conversion.Type is not { } target || !Evaluate(conversion.Operand, frame, out var operand) ||
            conversion.Operand.Type is not { } source)
        { return false; }
        var isChecked = CSharpOperationSemantics.CheckedContext(conversion, _compilation.Options.CheckOverflow) ?? false;
        if (conversion.OperatorMethod is { } method)
        { return UserConversion(method, operand, source, target, out value); }
        var kind = conversion.GetConversion();
        if (kind.IsIdentity)
        {
            value = operand;
            return true;
        }
        // `default` typed by its target.
        if (kind.IsDefaultLiteral)
        {
            value = DefaultOf(target, out var ok);
            return ok;
        }
        if (kind.IsReference && kind.IsImplicit && operand is null or ObjectValue or ArrayValue or string)
        {
            value = operand;
            return true;
        }
        if (!(kind.IsNumeric || kind.IsEnumeration || kind.IsConstantExpression))
        { return false; }
        if (IsIntegral(target) && operand != null && IsIntegralValue(operand))
        { return FromBig(ToBig(operand), target, isChecked, out value); }
        // Integral to floating point or decimal never throws.
        if (target.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal &&
            operand != null && IsIntegralValue(operand))
        {
            value = new Opaque(target);
            return true;
        }
        // A floating-point value converts to another floating-point type by
        // rounding, and to an integral type, unchecked, to an unspecified
        // value; neither throws. Only a checked conversion to an integral
        // type, and any conversion to decimal, can throw.
        if (kind.IsNumeric && operand is Opaque && IsFloating(source) &&
            (IsFloating(target) || IsIntegral(target) && !isChecked && target.TypeKind != TypeKind.Enum))
        {
            value = new Opaque(target);
            return true;
        }
        return false;
    }

    private static bool IsFloating(ITypeSymbol type)
    { return type.SpecialType is SpecialType.System_Single or SpecialType.System_Double; }

    private bool UserConversion(IMethodSymbol method, object? operand, ITypeSymbol source, ITypeSymbol target, out object? value)
    {
        value = null;
        if (!IsLibrary(method.ContainingType))
        { return Call(method, null, [operand], out value); }
        if (operand == null)
        { return false; }
        // Integral into BigInteger, decimal or a 128-bit integer is exact.
        if (IsIntegralValue(operand) && (IsBigInteger(target) || IsWideInteger(target) || target.SpecialType == SpecialType.System_Decimal))
        {
            Current.Allocates |= IsBigInteger(target);
            value = IsBigInteger(target) ? ToBig(operand) : new Opaque(target);
            return true;
        }
        if (operand is not BigInteger big || !IsBigInteger(source))
        { return false; }
        if (IsIntegral(target))
        { return FromBig(big, target, isChecked: true, out value); }
        switch (target.SpecialType)
        {
            // Out of range rounds to an infinity; it never throws.
            case SpecialType.System_Single or SpecialType.System_Double:
                value = new Opaque(target);
                return true;
            case SpecialType.System_Decimal:
                if (BigInteger.Abs(big) > MaximumDecimal)
                { return false; }
                value = new Opaque(target);
                return true;
        }
        if (IsWideInteger(target) && (target.Name == "Int128" ? big >= -MaximumInt128 - 1 && big <= MaximumInt128 : big.Sign >= 0 && big <= MaximumUInt128))
        {
            value = new Opaque(target);
            return true;
        }
        return false;
    }

    // ---- Operators ----

    private bool Binary(IBinaryOperation binary, Frame frame, out object? value)
    {
        value = null;
        if (binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
        { return false; }
        if (!Evaluate(binary.LeftOperand, frame, out var left) || !Evaluate(binary.RightOperand, frame, out var right))
        { return false; }
        if (binary.OperatorMethod is { } method)
        {
            if (!IsLibrary(method.ContainingType))
            { return Call(method, null, [left, right], out value); }
            if (left is not BigInteger l || right is not BigInteger r || !BigBinary(binary.OperatorKind, l, r, out value))
            { return false; }
            // A BigInteger result past the inline range is heap-backed.
            Current.Allocates |= value is BigInteger;
            return true;
        }
        if (binary.Type is not { } type)
        { return false; }
        // Floating-point arithmetic never throws: it rounds, overflows to an
        // infinity or yields NaN. Its result can only be stored.
        if (IsFloating(type) && left is Opaque && right is Opaque && binary.LeftOperand.Type is { } leftType && IsFloating(leftType) &&
            binary.RightOperand.Type is { } rightType && IsFloating(rightType) &&
            binary.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply or
                BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder)
        {
            value = new Opaque(type);
            return true;
        }
        if (left is bool a && right is bool b)
        {
            value = binary.OperatorKind switch
            {
                BinaryOperatorKind.And => a & b,
                BinaryOperatorKind.Or => a | b,
                BinaryOperatorKind.ExclusiveOr or BinaryOperatorKind.NotEquals => a ^ b,
                BinaryOperatorKind.Equals => a == b,
                _ => null
            };
            return value != null;
        }
        if (binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals &&
            binary.LeftOperand.Type is { IsReferenceType: true } && binary.RightOperand.Type is { IsReferenceType: true } &&
            left is null or ObjectValue or ArrayValue && right is null or ObjectValue or ArrayValue)
        {
            value = ReferenceEquals(left, right) == (binary.OperatorKind == BinaryOperatorKind.Equals);
            return true;
        }
        if (left == null || right == null || !IsIntegralValue(left) || !IsIntegralValue(right) ||
            binary.LeftOperand.Type is not { } operandType || !IsIntegral(operandType))
        { return false; }
        var x = ToBig(left);
        var y = ToBig(right);
        switch (binary.OperatorKind)
        {
            case BinaryOperatorKind.Equals:
                value = x == y;
                return true;
            case BinaryOperatorKind.NotEquals:
                value = x != y;
                return true;
            case BinaryOperatorKind.LessThan:
                value = x < y;
                return true;
            case BinaryOperatorKind.LessThanOrEqual:
                value = x <= y;
                return true;
            case BinaryOperatorKind.GreaterThan:
                value = x > y;
                return true;
            case BinaryOperatorKind.GreaterThanOrEqual:
                value = x >= y;
                return true;
        }
        if (!IsIntegral(type) || !Arithmetic(binary.OperatorKind, x, y, type, out var exact))
        { return false; }
        return FromBig(exact, type, binary.IsChecked && CanOverflow(binary.OperatorKind), out value);
    }

    // Only these operators throw OverflowException in a checked context;
    // the others wrap, and division's one overflow always throws.
    private static bool CanOverflow(BinaryOperatorKind kind)
    { return kind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply; }

    // The exact result of an integral operator on values of the given type,
    // or none when the runtime would throw.
    private static bool Arithmetic(BinaryOperatorKind kind, BigInteger x, BigInteger y, ITypeSymbol type, out BigInteger result)
    {
        result = BigInteger.Zero;
        var (bits, signed) = Width(type);
        switch (kind)
        {
            case BinaryOperatorKind.Add:
                result = x + y;
                return true;
            case BinaryOperatorKind.Subtract:
                result = x - y;
                return true;
            case BinaryOperatorKind.Multiply:
                result = x * y;
                return true;
            case BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder:
                // MinValue / -1 and MinValue % -1 throw whatever the context.
                if (y.IsZero || signed && y == BigInteger.MinusOne && x == -(BigInteger.One << (bits - 1)))
                { return false; }
                result = kind == BinaryOperatorKind.Divide ? BigInteger.Divide(x, y) : BigInteger.Remainder(x, y);
                return true;
            case BinaryOperatorKind.And:
                result = x & y;
                return true;
            case BinaryOperatorKind.Or:
                result = x | y;
                return true;
            case BinaryOperatorKind.ExclusiveOr:
                result = x ^ y;
                return true;
            case BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift:
                var count = (int)(y & (bits - 1));
                // A shift never overflows; the caller wraps the result.
                if (kind == BinaryOperatorKind.LeftShift)
                {
                    result = x << count;
                    return true;
                }
                if (kind == BinaryOperatorKind.UnsignedRightShift && x.Sign < 0)
                { x += BigInteger.One << bits; }
                result = x >> count;
                return true;
            default:
                return false;
        }
    }

    // Operands past this many bytes give up before the host arithmetic runs:
    // repeated squaring would otherwise exhaust memory within a few steps.
    private const int MaximumBigIntegerBytes = 1 << 10;

    private static bool BigBinary(BinaryOperatorKind kind, BigInteger x, BigInteger y, out object? value)
    {
        value = null;
        if (x.ToByteArray().Length > MaximumBigIntegerBytes || y.ToByteArray().Length > MaximumBigIntegerBytes)
        { return false; }
        value = kind switch
        {
            BinaryOperatorKind.Add => x + y,
            BinaryOperatorKind.Subtract => x - y,
            BinaryOperatorKind.Multiply => x * y,
            BinaryOperatorKind.Divide when !y.IsZero => BigInteger.Divide(x, y),
            BinaryOperatorKind.Remainder when !y.IsZero => BigInteger.Remainder(x, y),
            BinaryOperatorKind.Equals => x == y,
            BinaryOperatorKind.NotEquals => x != y,
            BinaryOperatorKind.LessThan => x < y,
            BinaryOperatorKind.LessThanOrEqual => x <= y,
            BinaryOperatorKind.GreaterThan => x > y,
            BinaryOperatorKind.GreaterThanOrEqual => x >= y,
            _ => null
        };
        return value != null;
    }

    private bool Unary(IUnaryOperation unary, Frame frame, out object? value)
    {
        value = null;
        if (!Evaluate(unary.Operand, frame, out var operand))
        { return false; }
        if (unary.OperatorMethod is { } method)
        {
            if (!IsLibrary(method.ContainingType))
            { return Call(method, null, [operand], out value); }
            if (operand is not BigInteger big)
            { return false; }
            Current.Allocates = true;
            value = unary.OperatorKind switch
            {
                UnaryOperatorKind.Minus => -big,
                UnaryOperatorKind.Plus => big,
                _ => null
            };
            return value != null;
        }
        if (operand is bool flag)
        {
            value = unary.OperatorKind == UnaryOperatorKind.Not ? !flag : null;
            return value != null;
        }
        if (operand is Opaque && unary.Type is { } floating && IsFloating(floating) && unary.Operand.Type is { } operandType &&
            IsFloating(operandType) && unary.OperatorKind is UnaryOperatorKind.Plus or UnaryOperatorKind.Minus)
        {
            value = new Opaque(floating);
            return true;
        }
        if (operand == null || !IsIntegralValue(operand) || unary.Type is not { } type || !IsIntegral(type))
        { return false; }
        var x = ToBig(operand);
        return unary.OperatorKind switch
        {
            UnaryOperatorKind.Plus => FromBig(x, type, isChecked: false, out value),
            UnaryOperatorKind.Minus => FromBig(-x, type, unary.IsChecked, out value),
            UnaryOperatorKind.BitwiseNegation => FromBig(-x - 1, type, isChecked: false, out value),
            _ => false
        };
    }

    private bool Compound(ICompoundAssignmentOperation compound, Frame frame, out object? value)
    {
        value = null;
        if (compound.OperatorMethod != null || compound.InConversion.Exists && !compound.InConversion.IsIdentity ||
            compound.OutConversion.Exists && !compound.OutConversion.IsIdentity || compound.Target.Type is not { } type ||
            !IsIntegral(type) || !Target(compound.Target, frame, out var array, out var position) ||
            !ReadAt(compound.Target, array, position, frame, out var current) || !Evaluate(compound.Value, frame, out var operand) ||
            current == null || operand == null || !IsIntegralValue(current) || !IsIntegralValue(operand) ||
            !Arithmetic(compound.OperatorKind, ToBig(current), ToBig(operand), type, out var exact) ||
            !FromBig(exact, type, compound.IsChecked && CanOverflow(compound.OperatorKind), out value))
        { return false; }
        return StoreAt(compound.Target, array, position, value, frame);
    }

    private bool Increment(IIncrementOrDecrementOperation increment, Frame frame, out object? value)
    {
        value = null;
        if (increment.OperatorMethod != null || increment.Target.Type is not { } type || !IsIntegral(type) ||
            !Target(increment.Target, frame, out var array, out var position) ||
            !ReadAt(increment.Target, array, position, frame, out var current) || current == null || !IsIntegralValue(current))
        { return false; }
        var delta = increment.Kind == OperationKind.Increment ? BigInteger.One : BigInteger.MinusOne;
        if (!FromBig(ToBig(current) + delta, type, increment.IsChecked, out var next) ||
            !StoreAt(increment.Target, array, position, next, frame))
        { return false; }
        value = increment.IsPostfix ? current : next;
        return true;
    }

    // ---- Storage ----

    // The run whose statics hold a field's value, initializing its type first.
    // Only a static its type initializer alone writes holds nothing but what
    // that initializer stored; any other static is ambient state, even the
    // initializing type's own.
    private Run? StaticOwner(IFieldSymbol field)
    {
        if (!IsInitializedOnly(field))
        { return null; }
        var type = field.ContainingType.OriginalDefinition;
        return SymbolEqualityComparer.Default.Equals(Current.Type, type) ? Current : Initialize(type);
    }

    // A static not yet stored holds its type's default value.
    private bool ReadStatic(IFieldSymbol field, out object? value)
    {
        value = null;
        if (StaticOwner(field) is not { } owner)
        { return false; }
        if (owner.Statics.TryGetValue(field, out value))
        {
            value = Copy(value);
            return true;
        }
        value = DefaultOf(field.Type, out var ok);
        return ok;
    }

    private bool ReadField(IFieldReferenceOperation reference, Frame frame, out object? value)
    {
        value = null;
        var field = reference.Field.OriginalDefinition;
        if (field.HasConstantValue)
        { return Constant(field.Type, field.ConstantValue, out value); }
        if (field.IsVolatile || field.IsFixedSizeBuffer)
        { return false; }
        if (field.IsStatic)
        { return ReadStatic(field, out value); }
        if (reference.Instance == null || !Evaluate(reference.Instance, frame, out var instance))
        { return false; }
        switch (instance)
        {
            case StructValue structure when structure.Fields.TryGetValue(field, out value):
                value = Copy(value);
                return true;
            // A readonly field of any object is fixed once its constructor
            // returns; another field is fixed only while its maker runs.
            case ObjectValue target when (field.IsReadOnly || target.Run == Current.Id) && target.Fields.TryGetValue(field, out value):
                value = Copy(value);
                return true;
            default:
                return false;
        }
    }

    private bool ReadProperty(IPropertyReferenceOperation reference, Frame frame, out object? value)
    {
        value = null;
        var property = reference.Property;
        if (!reference.Arguments.IsEmpty)
        { return false; }
        if (IsLibrary(property.ContainingType))
        {
            // An array's length is fixed when it is made.
            if (property is { IsStatic: false, Name: "Length", ContainingType.SpecialType: SpecialType.System_Array })
            {
                if (reference.Instance == null || !Evaluate(reference.Instance, frame, out var array) || array is not ArrayValue items)
                { return false; }
                value = items.Items.Length;
                return true;
            }
            if (!property.IsStatic || !IsBigInteger(property.ContainingType))
            { return false; }
            value = property.Name switch
            {
                "Zero" => BigInteger.Zero,
                "One" => BigInteger.One,
                "MinusOne" => BigInteger.MinusOne,
                _ => null
            };
            return value != null;
        }
        if (BackingField(property) is { } backing)
        {
            if (property.IsStatic)
            { return ReadStatic(backing, out value); }
            if (reference.Instance == null || !Evaluate(reference.Instance, frame, out var instance))
            { return false; }
            // Reading a struct's property calls its getter, which runs the
            // struct's type initializer. A mutable property of an object
            // another run made may have changed since.
            if (instance is StructValue structure && Initialized(structure.Type) && structure.Fields.TryGetValue(backing, out value) ||
                instance is ObjectValue target && (backing.IsReadOnly || target.Run == Current.Id) && target.Fields.TryGetValue(backing, out value))
            {
                value = Copy(value);
                return true;
            }
            return false;
        }
        if (property.GetMethod is not { } getter)
        { return false; }
        object? receiver = null;
        if (!property.IsStatic && (reference.Instance == null || !Evaluate(reference.Instance, frame, out receiver)))
        { return false; }
        return Call(getter, receiver, [], out value);
    }

    // The backing field of an auto-property, whose accessors only read and
    // write it.
    private IFieldSymbol? BackingField(IPropertySymbol property)
    {
        if (property.IsAbstract || property.IsVirtual || property.IsOverride || property.IsExtern || property.IsIndexer ||
            property.DeclaringSyntaxReferences.Length != 1 ||
            property.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not PropertyDeclarationSyntax
            {
                ExpressionBody: null, AccessorList: { } accessors
            } || accessors.Accessors.Any(static accessor => accessor.Body != null || accessor.ExpressionBody != null))
        { return null; }
        return property.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property))?.OriginalDefinition;
    }

    // A static only its type initializer writes, shared by all threads.
    private static bool IsInitializedOnly(IFieldSymbol field)
    {
        return field is { IsStatic: true, IsConst: false, IsReadOnly: true, IsVolatile: false } &&
            !field.GetAttributes().Any(static attribute => attribute.AttributeClass is
            {
                Name: "ThreadStaticAttribute",
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            });
    }

    private bool Store(IOperation target, object? value, Frame frame)
    {
        switch (target)
        {
            case ILocalReferenceOperation { Local.RefKind: RefKind.None } local:
                frame.Variables[local.Local] = value;
                return true;
            case IParameterReferenceOperation { Parameter.RefKind: RefKind.None } parameter:
                frame.Variables[parameter.Parameter] = value;
                return true;
            case IFieldReferenceOperation field:
                return StoreField(field.Field.OriginalDefinition, field.Instance, value, frame);
            case IPropertyReferenceOperation { Arguments.IsEmpty: true } property when BackingField(property.Property) is { } backing:
                return StoreField(backing, property.Instance, value, frame);
            case IArrayElementReferenceOperation element:
                return Element(element, frame, out var array, out var index) && StoreAt(target, array, index, value, frame);
            default:
                return false;
        }
    }

    // An assignment's array element, resolved once, before the value is
    // evaluated; null for any other target, whose operands have no effects.
    private bool Target(IOperation target, Frame frame, out ArrayValue? array, out int index)
    {
        array = null;
        index = 0;
        if (target is not IArrayElementReferenceOperation element)
        { return true; }
        if (!Element(element, frame, out var items, out index))
        { return false; }
        array = items;
        return true;
    }

    private bool ReadAt(IOperation target, ArrayValue? array, int index, Frame frame, out object? value)
    {
        value = null;
        if (array == null)
        { return Evaluate(target, frame, out value); }
        if (array.Run != Current.Id)
        { return false; }
        value = Copy(array.Items[index]);
        return true;
    }

    private bool StoreAt(IOperation target, ArrayValue? array, int index, object? value, Frame frame)
    {
        if (array == null)
        { return Store(target, value, frame); }
        if (array.Run != Current.Id ||
            // A covariant array store checks the element's runtime type: a
            // sealed element type takes null or exactly that type.
            array.ElementType.IsReferenceType && array.ElementType.SpecialType != SpecialType.System_String &&
            !(array.ElementType is INamedTypeSymbol { IsSealed: true } sealedType && (value is null ||
                value is ObjectValue stored && SymbolEqualityComparer.Default.Equals(stored.Type.OriginalDefinition, sealedType.OriginalDefinition))))
        { return false; }
        array.Items[index] = value;
        return true;
    }

    private bool StoreField(IFieldSymbol field, IOperation? instance, object? value, Frame frame)
    {
        if (field.IsVolatile || field.IsConst || field.IsFixedSizeBuffer)
        { return false; }
        if (field.IsStatic)
        {
            // Only the initialization of the field's own type may store it,
            // and only into a static nothing else writes.
            if (!SymbolEqualityComparer.Default.Equals(field.ContainingType.OriginalDefinition, Current.Type) ||
                !(IsInitializedOnly(field) || field.AssociatedSymbol is IPropertySymbol { SetMethod: null }))
            { return false; }
            Current.Statics[field] = value;
            return true;
        }
        if (instance is not IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
        { return false; }
        switch (frame.Instance)
        {
            // A struct's `this` is the frame's own copy: a constructor fills
            // in the value it returns.
            case StructValue structure when frame.Method is { MethodKind: MethodKind.Constructor }:
                structure.Fields[field] = value;
                return true;
            case ObjectValue target when target.Run == Current.Id:
                target.Fields[field] = value;
                return true;
            default:
                return false;
        }
    }

    private bool Element(IArrayElementReferenceOperation element, Frame frame, out ArrayValue array, out int index)
    {
        array = null!;
        index = 0;
        if (element.Indices.Length != 1 || !Evaluate(element.ArrayReference, frame, out var reference) ||
            reference is not ArrayValue items || !Evaluate(element.Indices[0], frame, out var position) ||
            position == null || !IsIntegralValue(position))
        { return false; }
        var exact = ToBig(position);
        if (exact.Sign < 0 || exact >= items.Items.Length)
        { return false; }
        array = items;
        index = (int)exact;
        return true;
    }

    // ---- Calls and construction ----

    private bool Invoke(IInvocationOperation invocation, Frame frame, out object? value)
    {
        value = null;
        var method = invocation.TargetMethod;
        object? receiver = null;
        if (invocation.Instance != null)
        {
            // A constructor initializer runs the chained constructor on the
            // value under construction.
            if (method.MethodKind == MethodKind.Constructor &&
                invocation.Instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
            {
                if (method.ContainingType.SpecialType == SpecialType.System_Object)
                { return invocation.Arguments.IsEmpty; }
                return frame.Method is { MethodKind: MethodKind.Constructor } current &&
                    SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, current.ContainingType.OriginalDefinition) &&
                    Arguments(invocation.Arguments, frame, out var chained) && Construct(method, frame.Instance, chained);
            }
            if (!Evaluate(invocation.Instance, frame, out receiver))
            { return false; }
        }
        if (!Arguments(invocation.Arguments, frame, out var arguments))
        { return false; }
        // Rounding, the absolute value and the square root of a double never
        // throw and read no state; the result can only be stored.
        if (IsFloatingMath(method) && arguments.Length == 1 && arguments[0] is Opaque)
        {
            value = new Opaque(method.ReturnType);
            return true;
        }
        return Call(method, receiver, arguments, out value);
    }

    private bool IsFloatingMath(IMethodSymbol method)
    {
        return method is
        {
            IsStatic: true, Name: "Ceiling" or "Floor" or "Truncate" or "Round" or "Abs" or "Sqrt",
            ReturnType.SpecialType: SpecialType.System_Double, Parameters.Length: 1,
            ContainingType: { Name: "Math", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
        } && method.Parameters[0] is { Type.SpecialType: SpecialType.System_Double, RefKind: RefKind.None } && IsLibrary(method.ContainingType);
    }

    private bool Arguments(ImmutableArray<IArgumentOperation> arguments, Frame frame, out object?[] values)
    {
        values = new object?[arguments.Length];
        foreach (var argument in arguments)
        {
            if (argument.Parameter is not { RefKind: RefKind.None } parameter ||
                argument.ArgumentKind is not (ArgumentKind.Explicit or ArgumentKind.DefaultValue) ||
                parameter.Ordinal >= values.Length || !Evaluate(argument.Value, frame, out var value))
            { return false; }
            values[parameter.Ordinal] = Copy(value);
        }
        return true;
    }

    // Runs a nonvirtual source method of a closed, nongeneric type.
    private bool Call(IMethodSymbol method, object? receiver, object?[] arguments, out object? value)
    {
        value = null;
        method = method.OriginalDefinition;
        if (method.IsGenericMethod || method.ContainingType.IsGenericType || method.ContainingType.ContainingType is { IsGenericType: true } ||
            method.IsAbstract || method.IsExtern || method.IsVirtual || method.IsOverride || method.IsAsync ||
            method.MethodKind is not (MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.UserDefinedOperator or MethodKind.Conversion) ||
            method.Parameters.Length != arguments.Length || method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            method.ReturnsByRef || method.ReturnsByRefReadonly || !IsSource(method.ContainingType) ||
            method.IsStatic != (receiver == null) || method.DeclaringSyntaxReferences.Length != 1 ||
            CSharpOperationSemantics.HasSynchronizedEntry(method))
        { return false; }
        // A call into another type may run its initializer first.
        if (method.IsStatic && !SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, Current.Type) &&
            Initialize(method.ContainingType) == null)
        { return false; }
        // An instance method of a struct gets a copy of its receiver; a
        // mutating one could change the caller's variable, which this does
        // not model.
        if (receiver is StructValue structure)
        {
            // Calling a value type's instance method runs its type
            // initializer (ECMA-335 II.10.5.3.1); its instances may exist
            // before that, as default values.
            if (!(structure.Type.IsReadOnly || method.IsReadOnly) || !Initialized(structure.Type))
            { return false; }
            receiver = structure.Copy();
        }
        else if (receiver != null && receiver is not ObjectValue)
        { return false; }
        var declaration = method.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
        if (declaration.DescendantNodes().Any(static node => node is YieldStatementSyntax) ||
            Graph(declaration) is not { } graph)
        { return false; }
        Current.Runs.Add(method);
        var frame = new Frame(method, receiver);
        for (var index = 0; index < arguments.Length; index++)
        { frame.Variables[method.Parameters[index]] = arguments[index]; }
        if (_depth >= MaximumDepth)
        { return false; }
        _depth++;
        try
        { return Execute(graph, frame, out value); }
        finally
        { _depth--; }
    }

    // A type whose initializer has finished, or is running.
    private bool Initialized(INamedTypeSymbol type)
    {
        return _active.Any(run => SymbolEqualityComparer.Default.Equals(run.Type, type.OriginalDefinition)) ||
            Initialize(type) != null;
    }

    private bool Create(IObjectCreationOperation creation, Frame frame, out object? value)
    {
        value = null;
        if (creation.Initializer != null || creation.Constructor is not { } constructor || creation.Type is not INamedTypeSymbol type ||
            !Arguments(creation.Arguments, frame, out var arguments))
        { return false; }
        if (CSharpOperationSemantics.IsCoreObjectCreation(creation))
        {
            Current.Allocates = true;
            value = new ObjectValue(type, Current.Id);
            return true;
        }
        if (type.IsGenericType || !IsSource(type) || type.IsAbstract || type.IsRecord ||
            type.TypeKind == TypeKind.Class && type.BaseType?.SpecialType != SpecialType.System_Object ||
            // The finalizer of every constructed instance runs later.
            type.GetMembers().Any(static member => member is IMethodSymbol { MethodKind: MethodKind.Destructor }) ||
            !SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, Current.Type) && Initialize(type) == null)
        { return false; }
        object? instance;
        if (type.TypeKind == TypeKind.Struct)
        {
            instance = DefaultOf(type, out var ok);
            if (!ok)
            { return false; }
            // `new S()` without a declared parameterless constructor is
            // default(S).
            if (constructor.IsImplicitlyDeclared)
            {
                value = instance;
                return arguments.Length == 0 && !HasInstanceInitializers(type);
            }
        }
        else if (type.TypeKind == TypeKind.Class)
        {
            Current.Allocates = true;
            var target = new ObjectValue(type, Current.Id);
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>().Where(static field => !field.IsStatic && !field.IsConst))
            {
                if (field.IsFixedSizeBuffer || !CSharpOperationSemantics.HasIndependentFieldStorage(field))
                { return false; }
                target.Fields[field] = DefaultOf(field.Type, out var ok);
                if (!ok)
                { return false; }
            }
            instance = target;
        }
        else
        { return false; }
        if (!Construct(constructor, instance, arguments))
        { return false; }
        value = instance;
        return true;
    }

    private bool HasInstanceInitializers(INamedTypeSymbol type)
    {
        return type.GetMembers().Any(member => !member.IsStatic && member.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax { Initializer: not null } or
                PropertyDeclarationSyntax { Initializer: not null }));
    }

    // Runs an instance constructor on a value under construction: the type's
    // instance initializers, unless it chains to another of its own
    // constructors, then its body, which begins with the chained call.
    private bool Construct(IMethodSymbol constructor, object? instance, object?[] arguments)
    {
        constructor = constructor.OriginalDefinition;
        if (instance is not (StructValue or ObjectValue) || constructor.IsExtern || constructor.IsStatic ||
            constructor.Parameters.Length != arguments.Length || constructor.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            _depth >= MaximumDepth)
        { return false; }
        var type = constructor.ContainingType;
        Current.Runs.Add(constructor);
        if (constructor.IsImplicitlyDeclared)
        { return arguments.Length == 0 && RunInstanceInitializers(type, instance); }
        if (constructor.DeclaringSyntaxReferences.Length != 1 ||
            constructor.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not ConstructorDeclarationSyntax declaration ||
            Graph(declaration) is not { } graph)
        { return false; }
        var chainsToThis = declaration.Initializer?.IsKind(SyntaxKind.ThisConstructorInitializer) == true;
        if (!chainsToThis && !RunInstanceInitializers(type, instance))
        { return false; }
        var frame = new Frame(constructor, instance);
        for (var index = 0; index < arguments.Length; index++)
        { frame.Variables[constructor.Parameters[index]] = arguments[index]; }
        _depth++;
        try
        { return Execute(graph, frame, out _); }
        finally
        { _depth--; }
    }

    private bool RunInstanceInitializers(INamedTypeSymbol type, object? instance)
    {
        var clauses = new List<SyntaxNode>();
        foreach (var member in type.GetMembers())
        {
            if (member.IsStatic)
            { continue; }
            if (member is IEventSymbol)
            { return false; }
            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                var clause = reference.GetSyntax(cancellationToken) switch
                {
                    VariableDeclaratorSyntax variable => (SyntaxNode?)variable.Initializer,
                    PropertyDeclarationSyntax property => property.Initializer,
                    _ => null
                };
                if (clause != null && !clauses.Contains(clause))
                { clauses.Add(clause); }
            }
        }
        if (clauses.Select(static clause => clause.SyntaxTree).Distinct().Count() > 1)
        { return false; }
        foreach (var clause in clauses.OrderBy(static clause => clause.SpanStart))
        {
            if (Graph(clause) is not { } graph || !Execute(graph, new Frame(type.InstanceConstructors.FirstOrDefault(), instance), out _))
            { return false; }
        }
        return true;
    }

    private bool CreateArray(IArrayCreationOperation array, Frame frame, out object? value)
    {
        value = null;
        if (array.Type is not IArrayTypeSymbol { Rank: 1 } type || array.DimensionSizes.Length != 1 ||
            !Evaluate(array.DimensionSizes[0], frame, out var size) || size == null || !IsIntegralValue(size))
        { return false; }
        var length = ToBig(size);
        if (length.Sign < 0 || length > MaximumArrayLength)
        { return false; }
        var items = new object?[(int)length];
        for (var index = 0; index < items.Length; index++)
        {
            if (!Step())
            { return false; }
            items[index] = DefaultOf(type.ElementType, out var ok);
            if (!ok)
            { return false; }
        }
        if (array.Initializer is { } initializer)
        {
            if (initializer.ElementValues.Length != items.Length)
            { return false; }
            for (var index = 0; index < items.Length; index++)
            {
                if (initializer.ElementValues[index] is IArrayInitializerOperation ||
                    !Evaluate(initializer.ElementValues[index], frame, out var element))
                { return false; }
                items[index] = Copy(element);
            }
        }
        Current.Allocates = true;
        value = new ArrayValue(type.ElementType, items, Current.Id);
        return true;
    }
}
