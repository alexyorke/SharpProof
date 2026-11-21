using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Frontend;

// What a type initializer may do, ordered from least to most. The CLR runs a
// type initializer at a time the program does not control: before the first
// access to a static member, or, for a beforefieldinit type, at any earlier
// point. No code can observe a type's statics before its initializer has run,
// except code that the initializer itself reaches through a cycle. An
// initializer that only fills in the type's own statics with values it makes
// from nothing therefore has no effect a body can observe. It still runs code
// and may allocate, so only claims that ignore allocation admit it.
internal enum InitializationEffect
{
    // Stores constants, or nothing, into the type's own readonly statics.
    None,
    // Also constructs fresh arrays, delegates and objects through admitted
    // constructors, including a chain of admitted source base constructors,
    // reads statics of types whose own initializers are admitted, or stores
    // admitted values into the type's own mutable statics. It writes only the
    // type's own statics and the fields of the values it constructs, and
    // cannot throw except by running out of memory. It calls no other code
    // but the framework collection constructors and Add methods that
    // CSharpOperationSemantics.IsCallbackFreeCollectionCreation admits, the
    // framework values CSharpOperationSemantics.FrameworkInitializerValue
    // admits and the runtime type objects `typeof` yields, or,
    // when ClosedInitializerEvaluator ran it to completion, only code that
    // reads nothing but constants and the values it made.
    FreshValues,
    // Anything else.
    Effectful
}

// Classifies the initialization the runtime may run on entry to a type's
// members. Shared by the frontend, which inlines source callees, and by
// the compiler collector and analyzer, which decide entry completeness.
internal static class TypeInitialization
{
    private const int MaximumWork = 4096;

    private sealed class Results
    {
        internal ConcurrentDictionary<INamedTypeSymbol, InitializationEffect> Types { get; } = new(SymbolEqualityComparer.Default);
        internal ConcurrentDictionary<IFieldSymbol, InitializedStatic?> Statics { get; } = new(SymbolEqualityComparer.Default);
        internal ConcurrentDictionary<IFieldSymbol, bool> ImmutableStatics { get; } = new(SymbolEqualityComparer.Default);
    }

    // Evaluating an initializer can be costly, and every callable of an
    // assembly asks about the same few types.
    private static readonly ConditionalWeakTable<Compilation, Results> Cache = new();

    // The declaring type and, conservatively, every enclosing type. The CLR
    // does not run an enclosing type's initializer for a nested type's
    // member, but nothing here relies on that.
    internal static InitializationEffect Classify(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        var results = Cache.GetValue(compilation, static _ => new()).Types;
        if (results.TryGetValue(type, out var known))
        { return known; }
        var classifier = new TypeInitializerClassifier(compilation, cancellationToken);
        var effect = InitializationEffect.None;
        for (var current = type; current != null && effect != InitializationEffect.Effectful; current = current.ContainingType)
        { effect = Max(effect, classifier.Classify(current)); }
        // A cancelled classification throws before it reaches here, so only
        // complete results are kept.
        return results.GetOrAdd(type, effect);
    }

    // The value a static holds once its type's initialization, run by
    // ClosedInitializerEvaluator, has finished; null when it does not finish
    // or the value is not one InitializedValue describes.
    internal static InitializedStatic? InitializedStatic(CSharpCompilation compilation, IFieldSymbol field,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        field = RepositoryParameterGuard.NotNull(field, nameof(field)).OriginalDefinition;
        if (!TypeInitializerClassifier.IsInitializedOnly(field))
        { return null; }
        var results = Cache.GetValue(compilation, static _ => new()).Statics;
        if (results.TryGetValue(field, out var known))
        { return known; }
        var value = new ClosedInitializerEvaluator(compilation, cancellationToken).ReadInitializedStatic(field);
        return results.GetOrAdd(field, value);
    }

    // A readonly static that only its own initializer stores, and that holds
    // an immutable object, including a sealed source object constructed
    // entirely from immutable inputs and stored in private readonly state.
    // Once the type's initializer has run, every read yields that same object, so a
    // read of it is not an ambient read. Whether the initializer itself is
    // admitted is for the caller to check.
    internal static bool HoldsImmutableValue(CSharpCompilation compilation, IFieldSymbol field,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        field = RepositoryParameterGuard.NotNull(field, nameof(field)).OriginalDefinition;
        if (!TypeInitializerClassifier.IsInitializedOnly(field) || !field.Type.IsReferenceType || field.Type is IArrayTypeSymbol)
        { return false; }
        var results = Cache.GetValue(compilation, static _ => new()).ImmutableStatics;
        if (results.TryGetValue(field, out var known))
        { return known; }
        var immutable = CheckImmutableValue(compilation, field, cancellationToken);
        return results.GetOrAdd(field, immutable);
    }

    private static bool CheckImmutableValue(CSharpCompilation compilation, IFieldSymbol field,
        CancellationToken cancellationToken)
    {
        var declared = field.AssociatedSymbol is IPropertySymbol property ? (ISymbol)property : field;
        if (declared.DeclaringSyntaxReferences.Length != 1 ||
            Initializer(compilation, declared.DeclaringSyntaxReferences[0], cancellationToken) is not { } initializer ||
            !IsImmutableValue(compilation, initializer.Value, field, cancellationToken))
        { return false; }
        // C# lets a static constructor and the type's own static initializers
        // store into a readonly static, or take a writable reference to it.
        foreach (var member in field.ContainingType.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!member.IsStatic || member is IFieldSymbol { IsConst: true } ||
                member is IMethodSymbol { MethodKind: not MethodKind.StaticConstructor })
            { continue; }
            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                var operation = member is IMethodSymbol
                    ? Host.CompilationModelProvider.GetSemanticModel(compilation, reference.SyntaxTree)
                        .GetOperation(reference.GetSyntax(cancellationToken), cancellationToken)
                    : Initializer(compilation, reference, cancellationToken);
                if (operation != null && operation.Descendants().Any(child => MayStore(child, field, declared)))
                { return false; }
            }
        }
        return true;
    }

    private static ISymbolInitializerOperation? Initializer(CSharpCompilation compilation, SyntaxReference reference,
        CancellationToken cancellationToken)
    {
        var clause = reference.GetSyntax(cancellationToken) switch
        {
            VariableDeclaratorSyntax variable => variable.Initializer,
            PropertyDeclarationSyntax property => property.Initializer,
            _ => null
        };
        return clause == null ? null
            : Host.CompilationModelProvider.GetSemanticModel(compilation, reference.SyntaxTree)
                .GetOperation(clause, cancellationToken) as ISymbolInitializerOperation;
    }

    private static bool IsImmutableValue(CSharpCompilation compilation, IOperation value, IFieldSymbol field,
        CancellationToken cancellationToken)
    {
        if (value.ConstantValue.HasValue)
        { return value.ConstantValue.Value is null or string; }
        switch (value)
        {
            case IConversionOperation { OperatorMethod: null, IsTryCast: false } conversion:
                var converted = conversion.GetConversion();
                if (converted.IsUserDefined)
                { return false; }
                if (converted.IsBoxing)
                {
                    return conversion.Operand.ConstantValue.HasValue && conversion.Operand.Type is { } operandType &&
                        (CSharpOperationSemantics.IsScalar(operandType) || operandType.TypeKind == TypeKind.Enum ||
                            operandType.SpecialType is SpecialType.System_Char or SpecialType.System_Single or SpecialType.System_Double);
                }
                return converted.IsImplicit && (converted.IsIdentity || converted.IsReference ||
                    converted.IsObjectCreation && conversion.Operand is IObjectCreationOperation &&
                    SymbolEqualityComparer.Default.Equals(conversion.Type, conversion.Operand.Type)) &&
                    IsImmutableValue(compilation, conversion.Operand, field, cancellationToken);
            case IObjectCreationOperation creation:
                return CSharpOperationSemantics.IsCoreObjectCreation(creation) ||
                    new TypeInitializerClassifier(compilation, cancellationToken).IsImmutableConstruction(creation, field);
            default:
                return false;
        }
    }

    // A reference to the static, or to its auto-property, other than a plain
    // read of its value.
    private static bool MayStore(IOperation operation, IFieldSymbol field, ISymbol declared)
    {
        var named = operation switch
        {
            IFieldReferenceOperation reference => SymbolEqualityComparer.Default.Equals(reference.Field.OriginalDefinition, field),
            IPropertyReferenceOperation reference => SymbolEqualityComparer.Default.Equals(reference.Property.OriginalDefinition, declared),
            _ => false
        };
        if (!named)
        { return false; }
        return operation.Parent switch
        {
            IAssignmentOperation assignment => assignment.Target == operation,
            IIncrementOrDecrementOperation or IAddressOfOperation or ITupleOperation or IDeconstructionAssignmentOperation => true,
            IArgumentOperation argument => argument.Parameter is not { RefKind: RefKind.None },
            _ => operation.Syntax.Parent is RefExpressionSyntax
        };
    }

    private static InitializationEffect Max(InitializationEffect left, InitializationEffect right)
    { return left >= right ? left : right; }

    // Admits the narrow initializer class described on InitializationEffect.
    // Every shape not named here is Effectful. A type reached again while its
    // own initializer is being classified is a cycle, and so is Effectful:
    // the second initializer would observe the first one's statics before
    // they are set.
    private sealed class TypeInitializerClassifier(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        // An array larger than this is not a fresh value here: a large
        // enough constant length fails with OutOfMemoryException.
        private const long MaximumArrayLength = 1 << 16;

        private readonly CSharpCompilation _compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        private readonly ClosedInitializerEvaluator _evaluator = new(compilation, cancellationToken);
        private readonly Dictionary<INamedTypeSymbol, InitializationEffect> _types = new(SymbolEqualityComparer.Default);
        private readonly HashSet<INamedTypeSymbol> _activeTypes = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> _activeConstructors = new(SymbolEqualityComparer.Default);
        // Base constructors being classified. Their stores run on an object
        // of a more derived class, where a virtual property may be overridden.
        private int _baseConstructors;
        private int _remaining = MaximumWork;

        private bool Spend()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return --_remaining >= 0;
        }

        // A result is the same in every visiting order: a type that reaches an
        // active type lies on a cycle through it, and is Effectful from any
        // starting point.
        internal InitializationEffect Classify(INamedTypeSymbol type)
        {
            type = RepositoryParameterGuard.NotNull(type, nameof(type)).OriginalDefinition;
            if (!Spend())
            { return InitializationEffect.Effectful; }
            if (_types.TryGetValue(type, out var known))
            { return known; }
            if (!IsSource(type) || !_activeTypes.Add(type))
            { return InitializationEffect.Effectful; }
            var effect = InitializationEffect.Effectful;
            try
            {
                effect = ClassifyMembers(type);
                // A shape the rules above do not admit may still be a closed
                // computation that provably finishes without effects.
                if (effect == InitializationEffect.Effectful)
                { effect = _evaluator.Classify(type); }
            }
            finally
            {
                _activeTypes.Remove(type);
                _types[type] = effect;
            }
            return effect;
        }

        private bool IsSource(INamedTypeSymbol type)
        {
            return !type.DeclaringSyntaxReferences.IsEmpty &&
                type.DeclaringSyntaxReferences.All(reference => _compilation.ContainsSyntaxTree(reference.SyntaxTree));
        }

        private InitializationEffect ClassifyMembers(INamedTypeSymbol owner)
        {
            // Work is spent per declaration and per value, not per member, so
            // a type with many uninitialized statics or constants stays
            // within budget.
            var effect = InitializationEffect.None;
            foreach (var member in owner.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                effect = Max(effect, member switch
                {
                    IMethodSymbol { MethodKind: MethodKind.StaticConstructor, IsImplicitlyDeclared: false } constructor =>
                        StaticConstructor(constructor, owner),
                    IFieldSymbol { IsStatic: true, IsConst: false } field =>
                        StaticInitializers(field, owner, IsStaticTarget(field, owner), IsMutableStaticTarget(field, owner)),
                    IPropertySymbol { IsStatic: true } property =>
                        StaticInitializers(property, owner, IsStaticTarget(property, owner), IsMutableStaticTarget(property, owner)),
                    // A field-like event's backing field is mutable state.
                    IEventSymbol { IsStatic: true } staticEvent => StaticInitializers(staticEvent, owner, admitted: false, mutable: false),
                    _ => InitializationEffect.None
                });
                if (effect == InitializationEffect.Effectful)
                { return effect; }
            }
            return effect;
        }

        // A static of owner that its initializer may store into: a readonly
        // one, or a mutable one (below).
        private bool IsStaticTarget(ISymbol member, INamedTypeSymbol owner)
        {
            return SymbolEqualityComparer.Default.Equals(member.ContainingType, owner) && member switch
            {
                IFieldSymbol field => IsInitializedOnly(field) || IsMutableStaticTarget(field, owner),
                IPropertySymbol { IsStatic: true, SetMethod: null } property => IsAutoProperty(property),
                IPropertySymbol property => IsMutableStaticTarget(property, owner),
                _ => false
            };
        }

        // A process-wide mutable static of owner, or a static auto-property
        // with a setter. No code reads it before the initializer that stores
        // into it has finished, so that store is no more observable than one
        // into a readonly static; later reads of it are ambient reads, since
        // other code may store into it. A [ThreadStatic] field starts zeroed
        // on every thread but the one that runs the initializer.
        private bool IsMutableStaticTarget(ISymbol member, INamedTypeSymbol owner)
        {
            return SymbolEqualityComparer.Default.Equals(member.ContainingType, owner) && member switch
            {
                IFieldSymbol { IsStatic: true, IsConst: false, IsReadOnly: false, AssociatedSymbol: null } field =>
                    !field.GetAttributes().Any(static attribute => attribute.AttributeClass is
                    {
                        Name: "ThreadStaticAttribute",
                        ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                    }),
                IPropertySymbol { IsStatic: true, SetMethod: not null } property => IsAutoProperty(property) &&
                    !property.GetAttributes().Any(static attribute => attribute.AttributeClass is { Name: "ThreadStaticAttribute" }),
                _ => false
            };
        }

        // A static only its type initializer writes, shared by all threads.
        internal static bool IsInitializedOnly(IFieldSymbol field)
        {
            field = RepositoryParameterGuard.NotNull(field, nameof(field));
            return field is { IsStatic: true, IsConst: false, IsReadOnly: true, IsVolatile: false } &&
                !field.GetAttributes().Any(static attribute => attribute.AttributeClass is
                {
                    Name: "ThreadStaticAttribute",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                });
        }

        // An auto-property's accessors read and write its backing field and
        // nothing else.
        private bool IsAutoProperty(IPropertySymbol property)
        {
            return !property.IsAbstract && !property.IsExtern && !property.IsIndexer && property.DeclaringSyntaxReferences.Length == 1 &&
                property.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is PropertyDeclarationSyntax
                {
                    ExpressionBody: null, AccessorList: { } accessors
                } && accessors.Accessors.All(static accessor => accessor.Body == null && accessor.ExpressionBody == null);
        }

        // A store into a mutable static belongs to the fresh-values class
        // even when the value is a constant, so claims that count every write
        // of static state, such as effect contracts, still see an effect.
        private InitializationEffect StaticInitializers(ISymbol member, INamedTypeSymbol owner, bool admitted, bool mutable)
        {
            var effect = InitializationEffect.None;
            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                if (!Spend())
                { return InitializationEffect.Effectful; }
                var clause = reference.GetSyntax(cancellationToken) switch
                {
                    VariableDeclaratorSyntax variable => variable.Initializer,
                    PropertyDeclarationSyntax property => property.Initializer,
                    _ => null
                };
                if (clause == null)
                { continue; }
                if (!admitted)
                { return InitializationEffect.Effectful; }
                var model = Host.CompilationModelProvider.GetSemanticModel(_compilation, reference.SyntaxTree);
                effect = Max(effect, model.GetOperation(clause, cancellationToken) is ISymbolInitializerOperation initializer
                    ? Value(initializer.Value, owner, constructor: null) : InitializationEffect.Effectful);
                if (mutable)
                { effect = Max(effect, InitializationEffect.FreshValues); }
                if (effect == InitializationEffect.Effectful)
                { return effect; }
            }
            return effect;
        }

        // A static constructor whose body only stores admitted values into
        // the type's own readonly statics.
        private InitializationEffect StaticConstructor(IMethodSymbol constructor, INamedTypeSymbol owner)
        {
            return Body(constructor) is { Initializer: null } body
                ? Statements(body, owner, constructor: null) : InitializationEffect.Effectful;
        }

        private IConstructorBodyOperation? Body(IMethodSymbol constructor)
        {
            if (constructor.IsExtern || constructor.DeclaringSyntaxReferences.Length != 1)
            { return null; }
            var reference = constructor.DeclaringSyntaxReferences[0];
            return reference.GetSyntax(cancellationToken) is ConstructorDeclarationSyntax declaration &&
                Host.CompilationModelProvider.GetSemanticModel(_compilation, reference.SyntaxTree)
                    .GetOperation(declaration, cancellationToken) is IConstructorBodyOperation { Locals.IsEmpty: true } body
                ? body : null;
        }

        private InitializationEffect Statements(IConstructorBodyOperation body, INamedTypeSymbol owner, IMethodSymbol? constructor,
            bool immutableValues = false)
        {
            var effect = InitializationEffect.None;
            foreach (var block in new[] { body.BlockBody, body.ExpressionBody })
            {
                if (block != null)
                { effect = Max(effect, Statement(block, owner, constructor, immutableValues)); }
            }
            return effect;
        }

        private InitializationEffect Statement(IOperation statement, INamedTypeSymbol owner, IMethodSymbol? constructor,
            bool immutableValues)
        {
            if (!Spend())
            { return InitializationEffect.Effectful; }
            switch (statement)
            {
                case IEmptyOperation or IReturnOperation { ReturnedValue: null }:
                    return InitializationEffect.None;
                case IBlockOperation { Locals.IsEmpty: true } block:
                    var effect = InitializationEffect.None;
                    foreach (var child in block.Operations)
                    {
                        effect = Max(effect, Statement(child, owner, constructor, immutableValues));
                        if (effect == InitializationEffect.Effectful)
                        { break; }
                    }
                    return effect;
                case IExpressionStatementOperation { Operation: ISimpleAssignmentOperation { IsRef: false } assignment }
                    when constructor == null ? IsStaticStore(assignment.Target, owner) : IsInstanceStore(assignment.Target, constructor.ContainingType):
                    var stored = Value(assignment.Value, owner, constructor, immutableValues);
                    return constructor == null && IsMutableStore(assignment.Target, owner) ? Max(stored, InitializationEffect.FreshValues) : stored;
                default:
                    return InitializationEffect.Effectful;
            }
        }

        private bool IsMutableStore(IOperation target, INamedTypeSymbol owner)
        {
            return target switch
            {
                IFieldReferenceOperation { Instance: null } field => IsMutableStaticTarget(field.Field, owner),
                IPropertyReferenceOperation { Instance: null, Arguments.IsEmpty: true } property => IsMutableStaticTarget(property.Property, owner),
                _ => false
            };
        }

        private bool IsStaticStore(IOperation target, INamedTypeSymbol owner)
        {
            return target switch
            {
                IFieldReferenceOperation { Instance: null } field => IsStaticTarget(field.Field, owner),
                IPropertyReferenceOperation { Instance: null, Arguments.IsEmpty: true } property => IsStaticTarget(property.Property, owner),
                _ => false
            };
        }

        // A store into a field or auto-property of the instance under
        // construction.
        private bool IsInstanceStore(IOperation target, INamedTypeSymbol type)
        {
            return target switch
            {
                IFieldReferenceOperation
                {
                    Field.IsStatic: false, Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
                } field => SymbolEqualityComparer.Default.Equals(field.Field.ContainingType, type),
                IPropertyReferenceOperation
                {
                    Property.IsStatic: false, Arguments.IsEmpty: true,
                    Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
                } property => SymbolEqualityComparer.Default.Equals(property.Property.ContainingType, type) &&
                    IsAutoProperty(property.Property) &&
                    (_baseConstructors == 0 || property.Property is { IsVirtual: false, IsAbstract: false, IsOverride: false }),
                _ => false
            };
        }

        // A value stored by an initializer of owner's statics, or, with a
        // constructor, by that instance constructor. A constructor may read
        // its own parameters, but no statics and no lambdas.
        private InitializationEffect Value(IOperation? value, INamedTypeSymbol owner, IMethodSymbol? constructor,
            bool immutableValues = false)
        {
            if (value == null || !Spend())
            { return InitializationEffect.Effectful; }
            if (value.ConstantValue.HasValue)
            {
                // The compiler builds a decimal constant with a decimal
                // constructor, or reads one of Decimal's own constants, which
                // its framework initializer sets.
                return IsConstant(value.Type, value.ConstantValue.Value) ? InitializationEffect.None
                    : value.ConstantValue.Value is decimal ? InitializationEffect.FreshValues : InitializationEffect.Effectful;
            }
            // The empty string runs no initializer; the other framework values
            // only make fresh objects.
            if (CSharpOperationSemantics.FrameworkInitializerValue(value) is { } framework &&
                (framework == InitializationEffect.None || !immutableValues))
            { return framework; }
            switch (value)
            {
                case IParenthesizedOperation parenthesized:
                    return Value(parenthesized.Operand, owner, constructor, immutableValues);
                case IConversionOperation { OperatorMethod: null, IsTryCast: false } conversion:
                    // Implicit identity, reference and numeric conversions run
                    // no code and cannot throw; boxing allocates. A
                    // target-typed `new(...)` converts the object it creates
                    // to the type it creates.
                    var converted = conversion.GetConversion();
                    // A default literal zeroes the target even when Roslyn
                    // represents it as a conversion to a type parameter.
                    if (converted.IsDefaultLiteral && conversion.Operand is IDefaultValueOperation)
                    { return InitializationEffect.None; }
                    return !converted.IsImplicit || converted.IsUserDefined ? InitializationEffect.Effectful
                        : converted.IsIdentity || converted.IsReference || converted.IsNumeric ||
                            converted.IsCollectionExpression && conversion.Operand is ICollectionExpressionOperation ||
                            converted.IsObjectCreation && conversion.Operand is IObjectCreationOperation &&
                            SymbolEqualityComparer.Default.Equals(conversion.Type, conversion.Operand.Type)
                            ? Value(conversion.Operand, owner, constructor, immutableValues)
                        : converted.IsBoxing && !immutableValues ? Max(InitializationEffect.FreshValues, Value(conversion.Operand, owner, constructor))
                        : InitializationEffect.Effectful;
                case IDefaultValueOperation:
                    return InitializationEffect.None;
                case IParameterReferenceOperation { Parameter: { RefKind: RefKind.None } parameter } when constructor != null &&
                    SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, constructor):
                    return InitializationEffect.None;
                case IArrayCreationOperation array when !immutableValues:
                    return Array(array, owner, constructor);
                case ICollectionExpressionOperation { Type: IArrayTypeSymbol { Rank: 1 }, ConstructMethod: null } collection
                    when !immutableValues && !collection.Elements.IsEmpty && collection.Elements.Length <= MaximumArrayLength:
                    return Elements(collection.Elements, owner, constructor);
                // An empty array collection expression is `Array.Empty<T>()`.
                case ICollectionExpressionOperation { Type: IArrayTypeSymbol { Rank: 1 }, ConstructMethod: null, Elements.IsEmpty: true }
                    when !immutableValues:
                    return InitializationEffect.FreshValues;
                // `typeof` loads a type handle and returns the runtime type
                // object, which the runtime makes once per type; it runs no
                // type initializer. A type of this assembly or of the core
                // library always loads.
                case ITypeOfOperation typeOf when !immutableValues && IsLoadedType(typeOf.TypeOperand):
                    return InitializationEffect.FreshValues;
                case IObjectCreationOperation creation when !immutableValues:
                    return Creation(creation, owner, constructor);
                // `Array.Empty<T>()` returns one cached zero-length array per
                // T; the first call may allocate it. Making a T[] runs no code
                // of T, and a zero-length array has nothing to change.
                case IInvocationOperation
                {
                    Instance: null, Arguments.IsEmpty: true,
                    TargetMethod: { Name: "Empty", IsGenericMethod: true, Parameters.IsEmpty: true, ContainingType.SpecialType: SpecialType.System_Array }
                } when !immutableValues:
                    return InitializationEffect.FreshValues;
                case IDelegateCreationOperation creation when constructor == null:
                    return creation.Target is IAnonymousFunctionOperation or IMethodReferenceOperation
                    {
                        Instance: null, Method: { IsStatic: true, IsAbstract: false, IsVirtual: false, MethodKind: MethodKind.Ordinary }
                    } ? InitializationEffect.FreshValues : InitializationEffect.Effectful;
                case IFieldReferenceOperation { Field.IsStatic: true, Instance: null } field when constructor == null && !immutableValues:
                    // Only a readonly, non-volatile, process-wide static holds
                    // nothing but what its initializer stored; reading any
                    // other static is an ambient read. Reading a static of the
                    // type being initialized starts nothing; any other type's
                    // initializer runs first.
                    return !IsInitializedOnly(field.Field) ? InitializationEffect.Effectful
                        : SymbolEqualityComparer.Default.Equals(field.Field.ContainingType, owner)
                        ? InitializationEffect.None : Classify(field.Field.ContainingType);
                // A get-only static auto-property's getter only reads its
                // readonly backing field, so the read is that field's.
                case IPropertyReferenceOperation { Instance: null, Arguments.IsEmpty: true, Property: { IsStatic: true, SetMethod: null } property }
                    when constructor == null && !immutableValues && AutoPropertyBackingField(property) is { } backing:
                    return !IsInitializedOnly(backing) ? InitializationEffect.Effectful
                        : SymbolEqualityComparer.Default.Equals(property.ContainingType.OriginalDefinition, owner)
                        ? InitializationEffect.None : Classify(property.ContainingType);
                default:
                    return InitializationEffect.Effectful;
            }
        }

        private IFieldSymbol? AutoPropertyBackingField(IPropertySymbol property)
        {
            var definition = property.OriginalDefinition;
            if (definition.ContainingType is not { TypeKind: TypeKind.Class or TypeKind.Struct } type || !IsSource(type) ||
                definition.IsVirtual || definition.IsOverride || !IsAutoProperty(definition))
            { return null; }
            return type.GetMembers().OfType<IFieldSymbol>()
                .FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, definition));
        }

        // A type every part of which is declared in this assembly or in the
        // core library, so loading it cannot fail.
        private bool IsLoadedType(ITypeSymbol type)
        {
            switch (type)
            {
                case IArrayTypeSymbol array:
                    return IsLoadedType(array.ElementType);
                case INamedTypeSymbol { TypeKind: not TypeKind.Error } named:
                    var assembly = named.OriginalDefinition.ContainingAssembly;
                    if (!SymbolEqualityComparer.Default.Equals(assembly, _compilation.Assembly) &&
                        !SymbolEqualityComparer.Default.Equals(assembly, _compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly))
                    { return false; }
                    return (named.ContainingType == null || IsLoadedType(named.ContainingType)) &&
                        (named.IsUnboundGenericType || named.TypeArguments.All(IsLoadedType));
                default:
                    return false;
            }
        }

        private static bool IsConstant(ITypeSymbol? type, object? value)
        {
            if (value == null)
            { return type == null || type.IsReferenceType; }
            return type != null && (CSharpOperationSemantics.IsScalar(type) || type.TypeKind == TypeKind.Enum ||
                type.SpecialType is SpecialType.System_Char or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_String);
        }

        private InitializationEffect Array(IArrayCreationOperation array, INamedTypeSymbol owner, IMethodSymbol? constructor)
        {
            if (array.DimensionSizes.Length != 1 || array.DimensionSizes[0].ConstantValue is not { HasValue: true, Value: { } size } ||
                size is not (int or long or uint or ulong or short or ushort or byte or sbyte) ||
                Convert.ToDecimal(size, CultureInfo.InvariantCulture) is var length && (length < 0 || length > MaximumArrayLength))
            { return InitializationEffect.Effectful; }
            return array.Initializer == null ? InitializationEffect.FreshValues
                : Elements(array.Initializer.ElementValues, owner, constructor);
        }

        private InitializationEffect Elements(ImmutableArray<IOperation> elements, INamedTypeSymbol owner, IMethodSymbol? constructor)
        {
            RepositoryParameterGuard.ThrowIfInvalid(elements.IsDefault, nameof(elements));
            var effect = InitializationEffect.FreshValues;
            foreach (var element in elements)
            {
                // A nested initializer belongs to a multidimensional array, and
                // a spread enumerates a collection.
                effect = element is IArrayInitializerOperation or ISpreadOperation
                    ? InitializationEffect.Effectful : Max(effect, Value(element, owner, constructor));
                if (effect == InitializationEffect.Effectful)
                { break; }
            }
            return effect;
        }

        // The cache read can omit ambient state only if every reachable value
        // is immutable. The stricter constructor pass reuses the initializer
        // rules, admitting only copies of checked arguments, constants,
        // defaults and deferred delegate arguments.
        internal bool IsImmutableConstruction(IObjectCreationOperation creation, IFieldSymbol cache)
        {
            creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
            cache = RepositoryParameterGuard.NotNull(cache, nameof(cache));
            if (creation.Initializer != null || creation.Constructor is not { } created ||
                created.ContainingType is not { IsSealed: true, TypeKind: TypeKind.Class, ContainingType: null } type ||
                cache is not { DeclaredAccessibility: Accessibility.Private, ContainingType.ContainingType: null } ||
                CSharpOperationSemantics.HasSynchronizedEntry(created))
            { return false; }

            var definition = type.OriginalDefinition;
            var owner = cache.ContainingType;
            if (definition.GetMembers().OfType<IFieldSymbol>().Any(static field => !field.IsStatic &&
                (field is not { IsReadOnly: true, DeclaredAccessibility: Accessibility.Private, RefKind: RefKind.None } ||
                    field.AssociatedSymbol is IPropertySymbol { SetMethod: not null } ||
                    !CSharpOperationSemantics.HasIndependentFieldStorage(field))))
            { return false; }
            foreach (var argument in creation.Arguments)
            {
                if (Argument(argument, owner, constructor: null, immutableValues: true) == InitializationEffect.Effectful)
                { return false; }
            }
            return Constructor(created.OriginalDefinition, owner, immutableValues: true) != InitializationEffect.Effectful &&
                HasNoReferenceAliases(definition) &&
                (SymbolEqualityComparer.Default.Equals(definition, owner) || HasNoReferenceAliases(owner));
        }

        // Private readonly storage is only writable during construction in
        // ordinary C#. Ref aliases, including implicit `in` arguments and
        // reduced extension receivers, leave that boundary.
        private bool HasNoReferenceAliases(INamedTypeSymbol type)
        {
            if (type.StaticConstructors.Any(CSharpOperationSemantics.HasSynchronizedEntry))
            { return false; }
            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                var model = Host.CompilationModelProvider.GetSemanticModel(_compilation, reference.SyntaxTree);
                foreach (var node in reference.GetSyntax(cancellationToken).DescendantNodes())
                {
                    if (!Spend() || node is RefExpressionSyntax or RefTypeSyntax or FixedStatementSyntax or UnsafeStatementSyntax or
                        PointerTypeSyntax or FunctionPointerTypeSyntax or MakeRefExpressionSyntax or RefValueExpressionSyntax or
                        ThisExpressionSyntax { Parent: not MemberAccessExpressionSyntax } ||
                        node is MemberDeclarationSyntax member && member.Modifiers.Any(SyntaxKind.ExternKeyword) ||
                        node.IsKind(SyntaxKind.AddressOfExpression))
                    { return false; }
                    if (node is ArgumentSyntax argument && (argument.RefKindKeyword.RawKind != 0 ||
                        model.GetOperation(argument, cancellationToken) is IArgumentOperation { Parameter.RefKind: not RefKind.None }))
                    { return false; }
                    if (node is not ExpressionSyntax expression)
                    { continue; }
                    var symbol = model.GetSymbolInfo(expression, cancellationToken).Symbol;
                    if (HasReferenceSignature(model.GetConversion(expression, cancellationToken).MethodSymbol) ||
                        UsesUntrackedStorage(symbol) ||
                        expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } &&
                            symbol is IMethodSymbol { ReducedFrom: not null })
                    { return false; }
                }
            }
            return true;
        }

        private static bool HasReferenceSignature(ISymbol? symbol)
        {
            return symbol is IPropertySymbol { RefKind: not RefKind.None } ||
                symbol is IMethodSymbol method && (method.ReducedFrom ?? method) is var target &&
                    (target.RefKind != RefKind.None || target.Parameters.Any(static parameter => parameter.RefKind != RefKind.None));
        }

        private static bool UsesUntrackedStorage(ISymbol? symbol)
        {
            return HasReferenceSignature(symbol) ||
                symbol is IMethodSymbol
                {
                    ContainingNamespace: { Name: "Reflection", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
                };
        }

        // `new object()`, or a source class or struct whose constructor only
        // stores admitted values into the new instance's own fields.
        // Constructing an instance may run its type's initializer, so that
        // must be admitted too, unless it is the one being classified.
        private InitializationEffect Creation(IObjectCreationOperation creation, INamedTypeSymbol owner, IMethodSymbol? constructor)
        {
            if (CSharpOperationSemantics.IsCoreObjectCreation(creation))
            { return InitializationEffect.FreshValues; }
            // A framework collection whose constructor and Add calls run no
            // user code. The collection is mutable, so a read of a static
            // that holds it stays an ambient read in a body.
            if (CSharpOperationSemantics.IsCallbackFreeCollectionCreation(creation, out var stored))
            {
                var collected = InitializationEffect.FreshValues;
                foreach (var value in stored)
                {
                    collected = Max(collected, Value(value, owner, constructor));
                    if (collected == InitializationEffect.Effectful)
                    { break; }
                }
                return collected;
            }
            if (creation.Initializer != null || creation.Constructor is not { } created)
            { return InitializationEffect.Effectful; }
            var type = created.ContainingType;
            var effect = SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, owner) && constructor == null
                ? InitializationEffect.FreshValues : Max(InitializationEffect.FreshValues, Classify(type));
            foreach (var argument in creation.Arguments)
            {
                if (effect == InitializationEffect.Effectful)
                { return effect; }
                effect = Max(effect, Argument(argument, owner, constructor));
            }
            return effect == InitializationEffect.Effectful ? effect : Max(effect, Constructor(created.OriginalDefinition, owner));
        }

        private InitializationEffect Argument(IArgumentOperation argument, INamedTypeSymbol owner, IMethodSymbol? constructor,
            bool immutableValues = false)
        {
            return argument.ArgumentKind is ArgumentKind.Explicit or ArgumentKind.DefaultValue &&
                argument.Parameter is { RefKind: RefKind.None }
                ? Value(argument.Value, owner, constructor, immutableValues) : InitializationEffect.Effectful;
        }

        // A constructor of a class, or with asBase the constructor of a source
        // base class that a derived constructor chains to, run on a new
        // instance created in owner's initialization.
        private InitializationEffect Constructor(IMethodSymbol constructor, INamedTypeSymbol owner, bool immutableValues = false,
            bool asBase = false)
        {
            var type = constructor.ContainingType;
            // A source base class runs its own constructor chain on the new
            // object first. The immutable-value pass checks only the created
            // type's own fields, so it admits no inherited ones.
            var sourceBase = type.TypeKind == TypeKind.Class && type.BaseType?.SpecialType != SpecialType.System_Object;
            if (!IsSource(type) || type.IsAbstract && !asBase || type.IsStatic || type.IsRecord ||
                type.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
                sourceBase && (immutableValues || !CSharpOperationSemantics.HasSourceBaseChain(type, cancellationToken)) ||
                // The finalizer of every constructed instance runs later.
                type.GetMembers().Any(static member => member is IMethodSymbol { MethodKind: MethodKind.Destructor }) ||
                !_activeConstructors.Add(constructor))
            { return InitializationEffect.Effectful; }
            if (asBase)
            { _baseConstructors++; }
            try
            {
                var effect = InstanceInitializers(type, constructor, immutableValues);
                if (effect == InitializationEffect.Effectful)
                { return effect; }
                if (constructor.IsImplicitlyDeclared)
                {
                    return !sourceBase ? effect
                        : CSharpOperationSemantics.TryImplicitBaseConstructor(type, cancellationToken, out var implicitBase) && implicitBase != null
                        ? Max(effect, BaseConstructor(implicitBase, type, owner, [], constructor))
                        : InitializationEffect.Effectful;
                }
                if (constructor.DeclaringSyntaxReferences.Length != 1 ||
                    constructor.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not ConstructorDeclarationSyntax declaration ||
                    declaration.Initializer is { } chained && (!chained.IsKind(SyntaxKind.BaseConstructorInitializer) ||
                        !sourceBase && chained.ArgumentList.Arguments.Count != 0) ||
                    Body(constructor) is not { } body)
                { return InitializationEffect.Effectful; }
                if (!sourceBase)
                {
                    return body.Initializer is not (null or IExpressionStatementOperation
                    {
                        Operation: IInvocationOperation
                        {
                            Arguments.IsEmpty: true, TargetMethod: { MethodKind: MethodKind.Constructor, ContainingType.SpecialType: SpecialType.System_Object }
                        }
                    })
                        ? InitializationEffect.Effectful : Max(effect, Statements(body, type, constructor, immutableValues));
                }
                // The base constructor call, explicit or implicit, on the
                // object under construction.
                if (body.Initializer is not IExpressionStatementOperation
                    {
                        Operation: IInvocationOperation
                        {
                            Instance: IInstanceReferenceOperation, TargetMethod: { MethodKind: MethodKind.Constructor } chainedBase
                        } call
                    } ||
                    !SymbolEqualityComparer.Default.Equals(chainedBase.ContainingType.OriginalDefinition, type.BaseType!.OriginalDefinition))
                { return InitializationEffect.Effectful; }
                effect = Max(effect, BaseConstructor(chainedBase, type, owner, call.Arguments, constructor));
                return effect == InitializationEffect.Effectful ? effect : Max(effect, Statements(body, type, constructor, immutableValues));
            }
            finally
            {
                if (asBase)
                { _baseConstructors--; }
                _activeConstructors.Remove(constructor);
            }
        }

        // Running a base constructor may run its class's type initializer,
        // unless that class is the one being initialized. Its arguments may
        // read the derived constructor's parameters.
        private InitializationEffect BaseConstructor(IMethodSymbol baseConstructor, INamedTypeSymbol derived, INamedTypeSymbol owner,
            ImmutableArray<IArgumentOperation> arguments, IMethodSymbol constructor)
        {
            var baseType = baseConstructor.ContainingType.OriginalDefinition;
            var effect = SymbolEqualityComparer.Default.Equals(baseType, owner) ? InitializationEffect.None : Classify(baseType);
            foreach (var argument in arguments)
            {
                if (effect == InitializationEffect.Effectful)
                { return effect; }
                effect = Max(effect, Argument(argument, derived, constructor));
            }
            return effect == InitializationEffect.Effectful ? effect
                : Max(effect, Constructor(baseConstructor.OriginalDefinition, owner, asBase: true));
        }

        // Instance field and auto-property initializers run in every
        // constructor that does not chain to another of the same type.
        private InitializationEffect InstanceInitializers(INamedTypeSymbol type, IMethodSymbol constructor, bool immutableValues)
        {
            var effect = InitializationEffect.None;
            foreach (var member in type.GetMembers())
            {
                if (!Spend())
                { return InitializationEffect.Effectful; }
                if (member.IsStatic || member is not (IFieldSymbol { IsConst: false } or IPropertySymbol or IEventSymbol))
                { continue; }
                foreach (var reference in member.DeclaringSyntaxReferences)
                {
                    var clause = reference.GetSyntax(cancellationToken) switch
                    {
                        VariableDeclaratorSyntax variable => variable.Initializer,
                        PropertyDeclarationSyntax property => property.Initializer,
                        _ => null
                    };
                    if (clause == null)
                    { continue; }
                    // Initializers cannot read the constructor's parameters.
                    effect = member is IEventSymbol ? InitializationEffect.Effectful
                        : Max(effect, Host.CompilationModelProvider.GetSemanticModel(_compilation, reference.SyntaxTree)
                            .GetOperation(clause, cancellationToken) is ISymbolInitializerOperation initializer
                            ? Value(initializer.Value, type, constructor, immutableValues) : InitializationEffect.Effectful);
                    if (effect == InitializationEffect.Effectful)
                    { return effect; }
                }
            }
            return effect;
        }
    }
}
