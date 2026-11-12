namespace SharpProof.Compiler.Semantics;

// Why initialization the runtime may run on entry to a callable is not known
// to be free of observable effects.
internal enum EntryInitializationGap
{
    None,
    // The declaring type or an enclosing type has a static initializer or a
    // static constructor outside the admitted class (see
    // InitializationEffect).
    TypeInitializer,
    // The callable's assembly declares a module initializer.
    ModuleInitializer
}

// What a type initializer may do, ordered from least to most. The CLR runs a
// type initializer at a time the program does not control: before the first
// access to a static member, or, for a beforefieldinit type, at any earlier
// point. No code can observe a type's statics before its initializer has run,
// except code that the initializer itself reaches through a cycle. An
// initializer that only fills in the type's own readonly statics with values
// it makes from nothing therefore has no effect a body can observe. It still
// runs code and may allocate, so only claims that ignore allocation admit it.
internal enum InitializationEffect
{
    // Stores constants, or nothing, into the type's own readonly statics.
    None,
    // Also constructs fresh arrays, delegates and objects through admitted
    // constructors, and reads statics of types whose own initializers are
    // admitted. It writes only the type's own readonly statics and the fields
    // of the values it constructs, calls no other code, and cannot throw
    // except by running out of memory.
    FreshValues,
    // Anything else.
    Effectful
}

// Entry initialization shared by the compiler collector, which decides
// whether a body's effects are complete at entry, and the analyzer, which
// names the gap when they are not.
internal static class EntryInitialization
{
    private sealed class ModuleInitializerSet(ImmutableArray<IMethodSymbol> methods)
    {
        internal ImmutableArray<IMethodSymbol> Methods { get; } = methods.IsDefault
            ? throw new ArgumentException("Array must be initialized.", nameof(methods)) : methods;
    }

    private static readonly ConditionalWeakTable<Compilation, ModuleInitializerSet> ModuleInitializerSets = new();

    // A gap for a claim that counts allocation, or, with admitFreshValues,
    // for a claim that ignores it.
    internal static EntryInitializationGap FindEffectGap(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken, bool admitFreshValues = false)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        var effect = ClassifyTypeInitialization(compilation, type, cancellationToken);
        return effect == InitializationEffect.Effectful || effect == InitializationEffect.FreshValues && !admitFreshValues
            ? EntryInitializationGap.TypeInitializer
            : !ModuleInitializers(compilation, cancellationToken).IsEmpty ? EntryInitializationGap.ModuleInitializer
            : EntryInitializationGap.None;
    }

    // All initialization the runtime may run on entry to a member of the
    // type: the type's, its enclosing types' and the module's.
    internal static InitializationEffect ClassifyEntry(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        var effect = ClassifyTypeInitialization(compilation, type, cancellationToken);
        return effect != InitializationEffect.Effectful && !ModuleInitializers(compilation, cancellationToken).IsEmpty
            ? InitializationEffect.Effectful : effect;
    }

    internal static bool HasNoEffectTypeInitialization(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return ClassifyTypeInitialization(compilation, type, cancellationToken) == InitializationEffect.None;
    }

    // The declaring type and, conservatively, every enclosing type. The CLR
    // does not run an enclosing type's initializer for a nested type's
    // member, but nothing here relies on that.
    internal static InitializationEffect ClassifyTypeInitialization(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        var classifier = new TypeInitializerClassifier(compilation, cancellationToken);
        var effect = InitializationEffect.None;
        for (var current = type; current != null && effect != InitializationEffect.Effectful; current = current.ContainingType)
        { effect = Max(effect, classifier.Classify(current)); }
        return effect;
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
        private readonly Dictionary<INamedTypeSymbol, InitializationEffect> _types = new(SymbolEqualityComparer.Default);
        private readonly HashSet<INamedTypeSymbol> _activeTypes = new(SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> _activeConstructors = new(SymbolEqualityComparer.Default);
        private int _remaining = TotalBodyLowering.MaximumWork;

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
            { effect = ClassifyMembers(type); }
            finally
            {
                _activeTypes.Remove(type);
                _types[type] = effect;
            }
            return effect;
        }

        private bool IsSource(INamedTypeSymbol type)
        {
            type = RepositoryParameterGuard.NotNull(type, nameof(type));
            return !type.DeclaringSyntaxReferences.IsEmpty &&
                type.DeclaringSyntaxReferences.All(reference => _compilation.ContainsSyntaxTree(reference.SyntaxTree));
        }

        private InitializationEffect ClassifyMembers(INamedTypeSymbol owner)
        {
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
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
                    IFieldSymbol { IsStatic: true, IsConst: false } field => StaticInitializers(field, owner, IsStaticTarget(field, owner)),
                    IPropertySymbol { IsStatic: true } property => StaticInitializers(property, owner, IsStaticTarget(property, owner)),
                    // A field-like event's backing field is mutable state.
                    IEventSymbol { IsStatic: true } staticEvent => StaticInitializers(staticEvent, owner, admitted: false),
                    _ => InitializationEffect.None
                });
                if (effect == InitializationEffect.Effectful)
                { return effect; }
            }
            return effect;
        }

        // Writing a mutable static is an entry effect: the existing policy is
        // that only readonly state is filled in unobservably.
        private bool IsStaticTarget(ISymbol member, INamedTypeSymbol owner)
        {
            member = RepositoryParameterGuard.NotNull(member, nameof(member));
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
            return SymbolEqualityComparer.Default.Equals(member.ContainingType, owner) && member switch
            {
                IFieldSymbol { IsStatic: true, IsConst: false, IsReadOnly: true, IsVolatile: false } field =>
                    !field.GetAttributes().Any(static attribute => attribute.AttributeClass is
                    {
                        Name: "ThreadStaticAttribute",
                        ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                    }),
                IPropertySymbol { IsStatic: true, SetMethod: null } property => IsAutoProperty(property),
                _ => false
            };
        }

        // An auto-property's accessors read and write its backing field and
        // nothing else.
        private bool IsAutoProperty(IPropertySymbol property)
        {
            property = RepositoryParameterGuard.NotNull(property, nameof(property));
            return !property.IsAbstract && !property.IsExtern && !property.IsIndexer && property.DeclaringSyntaxReferences.Length == 1 &&
                property.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is PropertyDeclarationSyntax
                {
                    ExpressionBody: null, AccessorList: { } accessors
                } && accessors.Accessors.All(static accessor => accessor.Body == null && accessor.ExpressionBody == null);
        }

        private InitializationEffect StaticInitializers(ISymbol member, INamedTypeSymbol owner, bool admitted)
        {
            member = RepositoryParameterGuard.NotNull(member, nameof(member));
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
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
                var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(_compilation, reference.SyntaxTree);
                effect = Max(effect, model.GetOperation(clause, cancellationToken) is ISymbolInitializerOperation initializer
                    ? Value(initializer.Value, owner, constructor: null) : InitializationEffect.Effectful);
                if (effect == InitializationEffect.Effectful)
                { return effect; }
            }
            return effect;
        }

        // A static constructor whose body only stores admitted values into
        // the type's own readonly statics.
        private InitializationEffect StaticConstructor(IMethodSymbol constructor, INamedTypeSymbol owner)
        {
            constructor = RepositoryParameterGuard.NotNull(constructor, nameof(constructor));
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
            return Body(constructor) is { Initializer: null } body
                ? Statements(body, owner, constructor: null) : InitializationEffect.Effectful;
        }

        private IConstructorBodyOperation? Body(IMethodSymbol constructor)
        {
            constructor = RepositoryParameterGuard.NotNull(constructor, nameof(constructor));
            if (constructor.IsExtern || constructor.DeclaringSyntaxReferences.Length != 1)
            { return null; }
            var reference = constructor.DeclaringSyntaxReferences[0];
            return reference.GetSyntax(cancellationToken) is ConstructorDeclarationSyntax declaration &&
                Frontend.Host.CompilationModelProvider.GetSemanticModel(_compilation, reference.SyntaxTree)
                    .GetOperation(declaration, cancellationToken) is IConstructorBodyOperation { Locals.IsEmpty: true } body
                ? body : null;
        }

        private InitializationEffect Statements(IConstructorBodyOperation body, INamedTypeSymbol owner, IMethodSymbol? constructor)
        {
            body = RepositoryParameterGuard.NotNull(body, nameof(body));
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
            var effect = InitializationEffect.None;
            foreach (var block in new[] { body.BlockBody, body.ExpressionBody })
            {
                if (block != null)
                { effect = Max(effect, Statement(block, owner, constructor)); }
            }
            return effect;
        }

        private InitializationEffect Statement(IOperation statement, INamedTypeSymbol owner, IMethodSymbol? constructor)
        {
            statement = RepositoryParameterGuard.NotNull(statement, nameof(statement));
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
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
                        effect = Max(effect, Statement(child, owner, constructor));
                        if (effect == InitializationEffect.Effectful)
                        { break; }
                    }
                    return effect;
                case IExpressionStatementOperation { Operation: ISimpleAssignmentOperation { IsRef: false } assignment }
                    when constructor == null ? IsStaticStore(assignment.Target, owner) : IsInstanceStore(assignment.Target, constructor.ContainingType):
                    return Value(assignment.Value, owner, constructor);
                default:
                    return InitializationEffect.Effectful;
            }
        }

        private bool IsStaticStore(IOperation target, INamedTypeSymbol owner)
        {
            target = RepositoryParameterGuard.NotNull(target, nameof(target));
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
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
            target = RepositoryParameterGuard.NotNull(target, nameof(target));
            type = RepositoryParameterGuard.NotNull(type, nameof(type));
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
                    IsAutoProperty(property.Property),
                _ => false
            };
        }

        // A value stored by an initializer of owner's statics, or, with a
        // constructor, by that instance constructor. A constructor may read
        // its own parameters, but no statics and no lambdas.
        private InitializationEffect Value(IOperation? value, INamedTypeSymbol owner, IMethodSymbol? constructor)
        {
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
            if (value == null || !Spend())
            { return InitializationEffect.Effectful; }
            if (value.ConstantValue.HasValue)
            { return IsConstant(value.Type, value.ConstantValue.Value) ? InitializationEffect.None : InitializationEffect.Effectful; }
            switch (value)
            {
                case IParenthesizedOperation parenthesized:
                    return Value(parenthesized.Operand, owner, constructor);
                case IConversionOperation { OperatorMethod: null, IsTryCast: false } conversion:
                    // Implicit identity, reference and numeric conversions run
                    // no code and cannot throw; boxing allocates.
                    var converted = conversion.GetConversion();
                    return !converted.IsImplicit || converted.IsUserDefined ? InitializationEffect.Effectful
                        : converted.IsIdentity || converted.IsReference || converted.IsNumeric ||
                            converted.IsCollectionExpression && conversion.Operand is ICollectionExpressionOperation
                            ? Value(conversion.Operand, owner, constructor)
                        : converted.IsBoxing ? Max(InitializationEffect.FreshValues, Value(conversion.Operand, owner, constructor))
                        : InitializationEffect.Effectful;
                case IDefaultValueOperation:
                    return InitializationEffect.None;
                case IParameterReferenceOperation { Parameter: { RefKind: RefKind.None } parameter } when constructor != null &&
                    SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, constructor):
                    return InitializationEffect.None;
                case IArrayCreationOperation array:
                    return Array(array, owner, constructor);
                case ICollectionExpressionOperation { Type: IArrayTypeSymbol { Rank: 1 }, ConstructMethod: null } collection
                    when !collection.Elements.IsEmpty && collection.Elements.Length <= MaximumArrayLength:
                    return Elements(collection.Elements, owner, constructor);
                case IObjectCreationOperation creation:
                    return Creation(creation, owner, constructor);
                case IDelegateCreationOperation creation when constructor == null:
                    return creation.Target is IAnonymousFunctionOperation or IMethodReferenceOperation
                    {
                        Instance: null, Method: { IsStatic: true, IsAbstract: false, IsVirtual: false, MethodKind: MethodKind.Ordinary }
                    } ? InitializationEffect.FreshValues : InitializationEffect.Effectful;
                case IFieldReferenceOperation { Field.IsStatic: true, Instance: null } field when constructor == null:
                    // Reading a static of the type being initialized starts
                    // nothing; any other type's initializer runs first.
                    return SymbolEqualityComparer.Default.Equals(field.Field.ContainingType, owner)
                        ? InitializationEffect.None : Classify(field.Field.ContainingType);
                default:
                    return InitializationEffect.Effectful;
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
            array = RepositoryParameterGuard.NotNull(array, nameof(array));
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
            if (array.DimensionSizes.Length != 1 || array.DimensionSizes[0].ConstantValue is not { HasValue: true, Value: { } size } ||
                size is not (int or long or uint or ulong or short or ushort or byte or sbyte) ||
                Convert.ToDecimal(size, CultureInfo.InvariantCulture) is var length && (length < 0 || length > MaximumArrayLength))
            { return InitializationEffect.Effectful; }
            return array.Initializer == null ? InitializationEffect.FreshValues
                : Elements(array.Initializer.ElementValues, owner, constructor);
        }

        private InitializationEffect Elements(ImmutableArray<IOperation> elements, INamedTypeSymbol owner, IMethodSymbol? constructor)
        {
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
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

        // `new object()`, or a source class or struct whose constructor only
        // stores admitted values into the new instance's own fields.
        // Constructing an instance may run its type's initializer, so that
        // must be admitted too, unless it is the one being classified.
        private InitializationEffect Creation(IObjectCreationOperation creation, INamedTypeSymbol owner, IMethodSymbol? constructor)
        {
            creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
            owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
            if (CSharpOperationSemantics.IsCoreObjectCreation(creation))
            { return InitializationEffect.FreshValues; }
            if (creation.Initializer != null || creation.Constructor is not { } created)
            { return InitializationEffect.Effectful; }
            var type = created.ContainingType;
            var effect = SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, owner) && constructor == null
                ? InitializationEffect.FreshValues : Max(InitializationEffect.FreshValues, Classify(type));
            foreach (var argument in creation.Arguments)
            {
                if (effect == InitializationEffect.Effectful)
                { return effect; }
                effect = argument.ArgumentKind is ArgumentKind.Explicit or ArgumentKind.DefaultValue &&
                    argument.Parameter is { RefKind: RefKind.None }
                    ? Max(effect, Value(argument.Value, owner, constructor)) : InitializationEffect.Effectful;
            }
            return effect == InitializationEffect.Effectful ? effect : Max(effect, Constructor(created.OriginalDefinition));
        }

        private InitializationEffect Constructor(IMethodSymbol constructor)
        {
            constructor = RepositoryParameterGuard.NotNull(constructor, nameof(constructor));
            var type = constructor.ContainingType;
            if (!IsSource(type) || type.IsAbstract || type.IsStatic || type.IsRecord || type.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
                type.TypeKind == TypeKind.Class && type.BaseType?.SpecialType != SpecialType.System_Object ||
                // The finalizer of every constructed instance runs later.
                type.GetMembers().Any(static member => member is IMethodSymbol { MethodKind: MethodKind.Destructor }) ||
                !_activeConstructors.Add(constructor))
            { return InitializationEffect.Effectful; }
            try
            {
                var effect = InstanceInitializers(type, constructor);
                if (constructor.IsImplicitlyDeclared || effect == InitializationEffect.Effectful)
                { return effect; }
                if (constructor.DeclaringSyntaxReferences.Length != 1 ||
                    constructor.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not ConstructorDeclarationSyntax declaration ||
                    declaration.Initializer is { } chained && (!chained.IsKind(SyntaxKind.BaseConstructorInitializer) ||
                        chained.ArgumentList.Arguments.Count != 0) ||
                    Body(constructor) is not { } body ||
                    body.Initializer is not (null or IExpressionStatementOperation
                    {
                        Operation: IInvocationOperation
                        {
                            Arguments.IsEmpty: true, TargetMethod: { MethodKind: MethodKind.Constructor, ContainingType.SpecialType: SpecialType.System_Object }
                        }
                    }))
                { return InitializationEffect.Effectful; }
                return Max(effect, Statements(body, type, constructor));
            }
            finally
            { _activeConstructors.Remove(constructor); }
        }

        // Instance field and auto-property initializers run in every
        // constructor that does not chain to another of the same type.
        private InitializationEffect InstanceInitializers(INamedTypeSymbol type, IMethodSymbol constructor)
        {
            type = RepositoryParameterGuard.NotNull(type, nameof(type));
            constructor = RepositoryParameterGuard.NotNull(constructor, nameof(constructor));
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
                        : Max(effect, Frontend.Host.CompilationModelProvider.GetSemanticModel(_compilation, reference.SyntaxTree)
                            .GetOperation(clause, cancellationToken) is ISymbolInitializerOperation initializer
                            ? Value(initializer.Value, type, constructor) : InitializationEffect.Effectful);
                    if (effect == InitializationEffect.Effectful)
                    { return effect; }
                }
            }
            return effect;
        }
    }

    // Every method of the assembly marked [ModuleInitializer]: ordinary
    // methods and also accessors, operators, conversions, destructors and
    // the accessors a `method:` target reaches, so the walk visits every
    // method member of every type rather than declarations of one syntax
    // kind. It is exact whatever the assembly's size, and runs once per
    // compilation.
    internal static ImmutableArray<IMethodSymbol> ModuleInitializers(CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        if (ModuleInitializerSets.TryGetValue(compilation, out var known))
        { return known.Methods; }
        var methods = ImmutableArray.CreateBuilder<IMethodSymbol>();
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(compilation.Assembly.GlobalNamespace);
        while (pending.Count != 0)
        {
            foreach (var member in pending.Pop().GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member is INamespaceOrTypeSymbol nested)
                { pending.Push(nested); }
                else if (member is IMethodSymbol method && method.GetAttributes().Any(IsModuleInitializerAttribute))
                { methods.Add(method); }
            }
        }
        return ModuleInitializerSets.GetValue(compilation, _ => new(methods.ToImmutable())).Methods;
    }

    private static bool IsModuleInitializerAttribute(AttributeData attribute)
    {
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        return attribute.AttributeClass is
        {
            Name: "ModuleInitializerAttribute",
            ContainingNamespace:
            {
                Name: "CompilerServices",
                ContainingNamespace:
                {
                    Name: "Runtime",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                }
            }
        };
    }
}
