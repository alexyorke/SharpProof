using System.Numerics;

namespace SharpProof.Compiler.Semantics;

// This is a closed structural proof system. Never descend through an unknown
// operation: doing so could omit a call, an allocation, or a nonterminating path.
internal sealed partial class ResourceBoundsAnalysis
{
    internal sealed record Bound
    {
        internal ResourceExpression Expression { get; }
        internal string Text => Expression.ToDisplayString();
        internal BigInteger? Constant => Expression.Constant;
        internal BigInteger? Maximum => Expression.Maximum;
        internal Bound(ResourceExpression expression)
        {
            Expression = ArgumentNullGuard.NotNull(expression, nameof(expression));
        }
        internal static readonly Bound Zero = Number(0);
        internal static readonly Bound One = Number(1);
        internal static Bound Number(BigInteger value)
        {
            return new(ResourceExpression.Literal(value));
        }
        internal static Bound Parameter(IParameterSymbol parameter, ResourceInputKind kind)
        {
            parameter = ArgumentNullGuard.NotNull(parameter, nameof(parameter));
            var maximum = kind == ResourceInputKind.NonNegativeScalar && parameter.Type.SpecialType == SpecialType.System_Int64
                ? new BigInteger(long.MaxValue) : new BigInteger(int.MaxValue);
            return new(ResourceExpression.InputSize(new ResourceInput(parameter.Ordinal, kind, parameter.Name, maximum)));
        }
        internal static Bound Add(Bound left, Bound right)
        {
            left = ArgumentNullGuard.NotNull(left, nameof(left));
            right = ArgumentNullGuard.NotNull(right, nameof(right));
            return new(ResourceExpression.Add(left.Expression, right.Expression));
        }
        internal static Bound Multiply(Bound left, Bound right)
        {
            left = ArgumentNullGuard.NotNull(left, nameof(left));
            right = ArgumentNullGuard.NotNull(right, nameof(right));
            return new(ResourceExpression.Multiply(left.Expression, right.Expression));
        }
        internal static Bound Max(Bound left, Bound right)
        {
            left = ArgumentNullGuard.NotNull(left, nameof(left));
            right = ArgumentNullGuard.NotNull(right, nameof(right));
            return new(ResourceExpression.Max(left.Expression, right.Expression));
        }
    }

    internal sealed record Cost
    {
        internal Bound LoopIterations { get; }
        internal Bound Arrays { get; }
        internal Bound Elements { get; }
        internal ResourceProgram Program { get; }
        internal Cost(Bound loopIterations, Bound arrays, Bound elements, ResourceProgram program)
        {
            LoopIterations = ArgumentNullGuard.NotNull(loopIterations, nameof(loopIterations));
            Arrays = ArgumentNullGuard.NotNull(arrays, nameof(arrays));
            Elements = ArgumentNullGuard.NotNull(elements, nameof(elements));
            Program = ArgumentNullGuard.NotNull(program, nameof(program));
        }
        internal static readonly Cost Zero = new(Bound.Zero, Bound.Zero, Bound.Zero, ResourceProgram.Zero);
        internal Cost Add(Cost other)
        {
            other = ArgumentNullGuard.NotNull(other, nameof(other));
            return new(Bound.Add(LoopIterations, other.LoopIterations),
                Bound.Add(Arrays, other.Arrays), Bound.Add(Elements, other.Elements), ResourceProgram.Sequence(Program, other.Program));
        }
        internal Cost Max(Cost other)
        {
            other = ArgumentNullGuard.NotNull(other, nameof(other));
            return new(Bound.Max(LoopIterations, other.LoopIterations),
                Bound.Max(Arrays, other.Arrays), Bound.Max(Elements, other.Elements), ResourceProgram.Choice(Program, other.Program));
        }
        internal Cost Scale(Bound count, ResourceLoopRank rank, ResourceRepeatKind kind)
        {
            count = ArgumentNullGuard.NotNull(count, nameof(count));
            rank = ArgumentNullGuard.NotNull(rank, nameof(rank));
            var entries = kind == ResourceRepeatKind.Loop ? Bound.Add(Bound.One, LoopIterations) : LoopIterations;
            return new(Bound.Multiply(count, entries),
                Bound.Multiply(count, Arrays), Bound.Multiply(count, Elements), ResourceProgram.Repeat(rank, kind, Program));
        }
    }

    internal sealed record Result(Cost? Bounds, string? UnknownReason)
    {
        internal bool Proven => Bounds is not null;
        internal ImmutableArray<string> Assumptions { get; init; } = [];
    }

    private readonly CancellationToken _cancellation;
    private readonly Dictionary<ISymbol, Bound> _counters = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, Bound> _localProgress = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, BigInteger> _localInitialConstants = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, Bound?> _scalarArguments = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, Bound?> _arrayArguments = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> _activeMethods = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> _assumptions = new(StringComparer.Ordinal);
    private ResolvedApiSpecTable? _resourceSpecifications;
    private CSharpInvocationEmissionPolicy? _resourceEmission;
    private readonly Compilation? _compilation;
    private string? _unknown;
    private readonly Dictionary<IMethodSymbol, RootParameterClosurePlan> _rootClosures = new(SymbolEqualityComparer.Default);
    private int _remaining = 4096;

    private ResourceBoundsAnalysis(Compilation? compilation, CancellationToken cancellation)
    {
        _cancellation = cancellation;
        _compilation = compilation;
    }

    internal static Result Analyze(IMethodSymbol method, IOperation? body, CancellationToken cancellation)
    {
        ArgumentNullGuard.NotNull(method, nameof(method));
        cancellation.ThrowIfCancellationRequested();
        if (method.IsAsync || method.IsVirtual || method.IsAbstract || method.IsExtern ||
            method.ReturnsByRef || method.ReturnsByRefReadonly ||
            (!SupportedType(method.ReturnType) && !method.ReturnsVoid) ||
            method.Parameters.Any(parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) :
                parameter.RefKind != RefKind.None || string.IsNullOrEmpty(parameter.Name) || !SupportedType(parameter.Type)))
        {
            return new(null, "UnsupportedCallable");
        }
        if (body is null)
        {
            return new(null, "MissingBody");
        }
        var analysis = new ResourceBoundsAnalysis(body.SemanticModel?.Compilation, cancellation);
        analysis._activeMethods.Add(method);
        try
        {
            var cost = analysis.CallableBody(method, body, 0);
            if (cost.LoopIterations.Text.Length + cost.Arrays.Text.Length + cost.Elements.Text.Length > 16384)
            { analysis._unknown ??= "AnalysisBudgetExceeded"; }
            return analysis._unknown is { } reason ? new(null, reason) : new(cost, null)
            {
                Assumptions = [.. analysis._assumptions.OrderBy(static value => value is null ? throw new ArgumentNullException(nameof(value)) : value, StringComparer.Ordinal)]
            };
        }
        catch (ResourceExpressionLimitException)
        {
            return new(null, "AnalysisBudgetExceeded");
        }
    }

    private static bool Primitive(ITypeSymbol? type)
    {
        return type?.SpecialType is
        SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or
        SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char or
        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or
        SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double;
    }

    private static ResourceStorageKind Storage(ITypeSymbol type)
    {
        type = ArgumentNullGuard.NotNull(type, nameof(type));
        return type.SpecialType switch
        {
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => ResourceStorageKind.Byte,
            SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char => ResourceStorageKind.Word16,
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => ResourceStorageKind.Word32,
            SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => ResourceStorageKind.Word64,
            _ => throw new InvalidOperationException("Unsupported primitive array storage.")
        };
    }

    private static bool SupportedType(ITypeSymbol type)
    {
        type = ArgumentNullGuard.NotNull(type, nameof(type));
        return Primitive(type) || type.IsReferenceType;
    }

    private Cost Unknown(string reason)
    {
        reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
        _unknown ??= reason;
        return Cost.Zero;
    }

    private Cost Visit(IOperation operation, int depth)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        _cancellation.ThrowIfCancellationRequested();
        if (_unknown is not null)
        { return Cost.Zero; }
        if (--_remaining < 0 || depth > 128)
        { return Unknown("AnalysisBudgetExceeded"); }
        Cost result;
        switch (operation)
        {
            case IBlockOperation block:
                result = Sequence(block.Operations, depth);
                break;
            case IVariableDeclarationGroupOperation group:
                result = Sequence(group.Declarations, depth);
                break;
            case IVariableDeclarationOperation declaration:
                result = Sequence(declaration.Declarators, depth);
                break;
            case IVariableDeclaratorOperation variable:
                _localProgress.Remove(variable.Symbol);
                _localInitialConstants.Remove(variable.Symbol);
                if (variable.Symbol.Type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 &&
                    variable.Initializer is { } progressInitializer)
                {
                    var constant = IntegerConstant(progressInitializer.Value);
                    if (constant is { } exact)
                    { _localInitialConstants[variable.Symbol] = exact; }
                    var progress = constant is { } integer ? Bound.Number(BigInteger.Max(0, integer)) : Size(progressInitializer.Value);
                    if (progress is not null)
                    { _localProgress[variable.Symbol] = progress; }
                }
                result = variable.Symbol.RefKind != RefKind.None || !SupportedType(variable.Symbol.Type)
                    ? Unknown("UnsupportedLocal")
                    : variable.Initializer is { } variableInitializer ? Visit(variableInitializer, depth + 1) : Cost.Zero;
                break;
            case IVariableInitializerOperation initializer:
                result = Visit(initializer.Value, depth + 1);
                break;
            case IExpressionStatementOperation expression:
                result = Visit(expression.Operation, depth + 1);
                break;
            case IReturnOperation returned:
                result = returned.ReturnedValue is { } value ? Visit(value, depth + 1) : Cost.Zero;
                break;
            case IForLoopOperation loop:
                result = For(loop, depth);
                break;
            case IWhileLoopOperation loop:
                result = While(loop, depth);
                break;
            case IInvocationOperation invocation:
                result = Call(invocation, depth);
                break;
            case IConditionalOperation conditional when !conditional.IsRef:
                result = Visit(conditional.Condition, depth + 1).Add(
                    Visit(conditional.WhenTrue, depth + 1).Max(
                        conditional.WhenFalse is { } alternative ? Visit(alternative, depth + 1) : Cost.Zero));
                break;
            case IDelegateCreationOperation delegateCreation:
                result = DelegateCreation(delegateCreation, depth);
                break;
            case IObjectCreationOperation creation:
                result = ObjectCreation(creation, depth);
                break;
            case IFieldReferenceOperation field when !field.Field.IsStatic:
                result = field.Instance is { } receiver ? Visit(receiver, depth + 1) : Unknown("MissingFieldReceiver");
                break;
            case IInstanceReferenceOperation:
            case ILiteralOperation { ConstantValue.HasValue: true, ConstantValue.Value: null }:
            case IDefaultValueOperation { Type.IsReferenceType: true }:
                result = Cost.Zero;
                break;
            case IArrayCreationOperation array when array.Type is IArrayTypeSymbol type &&
                type is { Rank: 1, IsSZArray: true } && Primitive(type.ElementType) && array.DimensionSizes.Length == 1 && array.Initializer is null:
                var size = Size(array.DimensionSizes[0]);
                result = size is null ? Unknown("UnsupportedAllocationSize") : new(Bound.Zero, Bound.One, size, ResourceProgram.TypedArray(ResourceAllocationShape.Array(Storage(type.ElementType), size.Expression)));
                break;
            case ISimpleAssignmentOperation { IsRef: false, Target: IDiscardOperation } discarded:
                result = Visit(discarded.Value, depth + 1);
                break;
            case ISimpleAssignmentOperation assignment when !assignment.IsRef:
                result = Writable(assignment.Target)
                    ? Visit(assignment.Target, depth + 1).Add(Visit(assignment.Value, depth + 1))
                    : Unknown("UnstableInputOrCounter");
                break;
            case ICompoundAssignmentOperation assignment when assignment.OperatorMethod is null &&
                Primitive(assignment.Type) && !assignment.InConversion.IsUserDefined && !assignment.OutConversion.IsUserDefined:
                result = Writable(assignment.Target)
                    ? Visit(assignment.Target, depth + 1).Add(Visit(assignment.Value, depth + 1))
                    : Unknown("UnstableInputOrCounter");
                break;
            case IIncrementOrDecrementOperation increment when increment.OperatorMethod is null && Primitive(increment.Type):
                result = Writable(increment.Target) ? Visit(increment.Target, depth + 1) : Unknown("UnstableInputOrCounter");
                break;
            case IBinaryOperation binary when binary.OperatorMethod is null && Primitive(binary.Type):
                result = Visit(binary.LeftOperand, depth + 1).Add(Visit(binary.RightOperand, depth + 1));
                break;
            case IUnaryOperation unary when unary.OperatorMethod is null && Primitive(unary.Type):
                result = Visit(unary.Operand, depth + 1);
                break;
            case IConversionOperation boxing when CSharpOperationSemantics.IsScalarBoxing(boxing):
                result = Visit(boxing.Operand, depth + 1).Add(new(Bound.Zero, Bound.Zero, Bound.Zero,
                    ResourceProgram.Managed(ResourceAllocationShape.Box(Storage(boxing.Operand.Type!)))));
                break;
            case IConversionOperation conversion when !conversion.Conversion.IsUserDefined &&
                (conversion.Conversion.IsIdentity || conversion.Conversion.IsNumeric || conversion.Conversion.IsReference || conversion.Operand.ConstantValue is { HasValue: true, Value: null }) &&
                conversion.Type is { } converted && SupportedType(converted):
                result = Visit(conversion.Operand, depth + 1);
                break;
            case IArrayElementReferenceOperation element when Primitive(element.Type):
                result = Visit(element.ArrayReference, depth + 1).Add(Sequence(element.Indices, depth));
                break;
            case IPropertyReferenceOperation property when IsArrayLength(property):
                result = Visit(property.Instance!, depth + 1);
                break;
            case IPropertyReferenceOperation property when !property.Property.IsStatic && property.Instance != null && property.Arguments.IsEmpty &&
                CSharpOperationSemantics.GetterField(property.Property) is { AssociatedSymbol: IPropertySymbol }:
                result = Visit(property.Instance, depth + 1);
                break;
            case ILocalReferenceOperation local when SupportedType(local.Local.Type):
            case IParameterReferenceOperation parameter when SupportedType(parameter.Parameter.Type):
            case ILiteralOperation literal when literal.Type is { } literalType && SupportedType(literalType):
            case IEmptyOperation:
                result = Cost.Zero;
                break;
            default:
                result = Unknown("UnsupportedOperation:" + operation.Kind);
                break;
        }
        return result.LoopIterations.Text.Length + result.Arrays.Text.Length + result.Elements.Text.Length > 16384
            ? Unknown("AnalysisBudgetExceeded") : result;
    }

    private Cost Sequence(IEnumerable<IOperation> operations, int depth)
    {
        operations = ArgumentNullGuard.NotNull(operations, nameof(operations));
        var result = Cost.Zero;
        foreach (var operation in operations)
        {
            result = result.Add(Visit(operation, depth + 1));
            if (_unknown is not null)
            { break; }
            if (result.LoopIterations.Text.Length + result.Arrays.Text.Length + result.Elements.Text.Length > 16384)
            { return Unknown("AnalysisBudgetExceeded"); }
        }
        return result;
    }

    private bool Writable(IOperation target)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        if (target is ILocalReferenceOperation written)
        {
            _localProgress.Remove(written.Local);
            _localInitialConstants.Remove(written.Local);
        }
        return target switch
        {
            ILocalReferenceOperation local => local.Local.RefKind == RefKind.None && !_counters.ContainsKey(local.Local),
            IArrayElementReferenceOperation element => Primitive(element.Type),
            IFieldReferenceOperation field => !field.Field.IsStatic,
            _ => false
        };
    }

    private static bool IsArrayLength(IPropertyReferenceOperation property)
    {
        property = ArgumentNullGuard.NotNull(property, nameof(property));
        return property.Property.Name == "Length" && property.Property.ContainingType.SpecialType == SpecialType.System_Array &&
            property.Instance?.Type is IArrayTypeSymbol { Rank: 1, IsSZArray: true };
    }

    private Bound? Size(IOperation operation)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        return operation switch
        {
            ILiteralOperation { ConstantValue: { HasValue: true, Value: int value } } => Bound.Number(Math.Max(0, value)),
            ILiteralOperation { ConstantValue: { HasValue: true, Value: long value } } => Bound.Number(Math.Max(0, value)),
            IUnaryOperation
            {
                OperatorKind: UnaryOperatorKind.Minus, OperatorMethod: null,
                Operand: ILiteralOperation, ConstantValue: { HasValue: true, Value: int value }
            } => Bound.Number(Math.Max(0, value)),
            IParameterReferenceOperation parameter when _scalarArguments.TryGetValue(parameter.Parameter, out var argument) => argument,
            IParameterReferenceOperation parameter when parameter.Parameter.Type.SpecialType == SpecialType.System_Int32 =>
                Bound.Parameter(parameter.Parameter, ResourceInputKind.NonNegativeScalar),
            IParameterReferenceOperation parameter when parameter.Parameter.Type.SpecialType == SpecialType.System_Int64 =>
                Bound.Parameter(parameter.Parameter, ResourceInputKind.NonNegativeScalar),
            IPropertyReferenceOperation property when IsArrayLength(property) => ArraySize(property.Instance!),
            ILocalReferenceOperation local when _counters.TryGetValue(local.Local, out var bound) => bound,
            IConversionOperation conversion when conversion.Conversion.IsNumeric && !conversion.Conversion.IsUserDefined &&
                conversion.Type?.SpecialType == SpecialType.System_Int64 &&
                conversion.Operand.Type?.SpecialType == SpecialType.System_Int32 => Size(conversion.Operand),
            _ => null
        };
    }
}
