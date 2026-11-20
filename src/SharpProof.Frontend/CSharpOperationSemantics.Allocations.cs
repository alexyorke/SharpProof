namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsRepeatedCharacterStringCreation(IObjectCreationOperation creation)
    {
        creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
        return creation is
        {
            Type.SpecialType: SpecialType.System_String, Initializer: null,
            Constructor: { DeclaringSyntaxReferences.IsEmpty: true, Parameters.Length: 2 }, Arguments.Length: 2
        } &&
            creation.Constructor.Parameters[0] is { Type.SpecialType: SpecialType.System_Char, RefKind: RefKind.None } &&
            creation.Constructor.Parameters[1] is { Type.SpecialType: SpecialType.System_Int32, RefKind: RefKind.None } &&
            creation.Arguments.Single(argument => argument.Parameter?.Ordinal == 0)
                .Value.ConstantValue is { HasValue: true, Value: char character } && !char.IsSurrogate(character);
    }

    internal static bool IsSupportedArrayCreation(IArrayCreationOperation creation)
    {
        creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
        return creation is { Type: IArrayTypeSymbol { IsSZArray: true }, DimensionSizes.Length: 1 } &&
            creation.DimensionSizes[0].Type?.SpecialType == SpecialType.System_Int32 && IsReferenceDomain(creation.Type) &&
            (creation.Initializer == null ||
                creation.DimensionSizes[0].ConstantValue is { HasValue: true, Value: int length } &&
                length == creation.Initializer.ElementValues.Length &&
                creation.Initializer.ElementValues.All(IsConstantArrayElement));
    }

    // A direct scalar local read cannot mutate state or fault. Keep this
    // separate from atomic constant initializers: its value must be stored
    // after allocation, at the point where C# evaluates that element.
    internal static bool IsSupportedLocalArrayInitializer(IArrayCreationOperation creation, IMethodSymbol owner)
    {
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        if (creation.Type is not IArrayTypeSymbol { IsSZArray: true } array ||
            creation.DimensionSizes.Length != 1 || creation.Initializer is not { } initializer ||
            !IsScalar(array.ElementType) || !IsReferenceDomain(creation.Type) ||
            creation.DimensionSizes[0].Type?.SpecialType != SpecialType.System_Int32 ||
            creation.DimensionSizes[0].ConstantValue is not { HasValue: true, Value: int length } ||
            length != initializer.ElementValues.Length)
        { return false; }
        var hasLocal = false;
        foreach (var element in initializer.ElementValues)
        {
            if (SymbolEqualityComparer.Default.Equals(element.Type, array.ElementType))
            {
                if (IsConstantArrayElement(element))
                { continue; }
                if (element is ILocalReferenceOperation { Local.RefKind: RefKind.None } local &&
                    SymbolEqualityComparer.Default.Equals(local.Local.ContainingSymbol, owner))
                {
                    hasLocal = true;
                    continue;
                }
            }
            return false;
        }
        return hasLocal;
    }

    // An empty collection expression `[]` whose target is a class with a
    // parameterless constructor, such as List<T>, HashSet<T> or
    // Dictionary<TKey, TValue>, runs that constructor and adds nothing: it
    // is `new T()`. Its identity conversion to the same type is the
    // expression. The constructor, or null for any other operation.
    internal static IMethodSymbol? EmptyCollectionConstructor(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        if (operation is IConversionOperation { OperatorMethod: null, IsTryCast: false, Operand: ICollectionExpressionOperation converted } conversion &&
            !conversion.Conversion.IsUserDefined && SymbolEqualityComparer.Default.Equals(conversion.Type, converted.Type))
        { operation = converted; }
        return operation is ICollectionExpressionOperation
        {
            Elements.IsEmpty: true, Type: INamedTypeSymbol { TypeKind: TypeKind.Class } type,
            ConstructMethod: IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false, Parameters.IsEmpty: true } constructor
        } && SymbolEqualityComparer.Default.Equals(constructor.ContainingType, type) ? constructor : null;
    }

    // The constructor that `new T(...)` without an initializer, or an empty
    // `[]`, runs; null for any other operation.
    internal static IMethodSymbol? EmptyCreationConstructor(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IObjectCreationOperation { Initializer: null, Constructor: { } constructor }
            ? constructor : EmptyCollectionConstructor(operation);
    }

    internal static bool IsSupportedConstantArrayCollection(ICollectionExpressionOperation collection)
    {
        collection = RepositoryParameterGuard.NotNull(collection, nameof(collection));
        return collection.Type is IArrayTypeSymbol { IsSZArray: true } && collection.ConstructMethod == null &&
            !collection.Elements.IsEmpty && IsReferenceDomain(collection.Type) &&
            collection.Elements.All(IsConstantArrayElement);
    }

    private static bool IsConstantArrayElement(IOperation element)
    {
        return element is not ISpreadOperation && element.ConstantValue.HasValue &&
            (IsScalar(element.Type) || element.ConstantValue.Value == null && (element.Type == null || IsReferenceDomain(element.Type)) ||
                element.Type?.SpecialType == SpecialType.System_String && element.ConstantValue.Value is string text &&
                Utf16WellFormedness.IsWellFormed(text));
    }

    internal static bool IsStringConcatenation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null, IsLifted: false,
            Type.SpecialType: SpecialType.System_String,
            LeftOperand.Type.SpecialType: SpecialType.System_String,
            RightOperand.Type.SpecialType: SpecialType.System_String
        };
    }

    // Whether the compiler may flatten this expression into an enclosing `+`
    // or `+=` string concatenation. Parentheses, casts and `!` do not stop it.
    internal static bool IsConcatenationOperand(SyntaxNode syntax)
    {
        syntax = RepositoryParameterGuard.NotNull(syntax, nameof(syntax));
        var parent = syntax.Parent;
        while (parent is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax or
            Microsoft.CodeAnalysis.CSharp.Syntax.CastExpressionSyntax ||
            parent?.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression) == true)
        { parent = parent!.Parent; }
        return parent?.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddExpression) == true ||
            parent?.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AddAssignmentExpression) == true;
    }

    // An explicit identity cast to string emits no code, so the compiler
    // flattens a concatenation beneath it into the enclosing chain.
    internal static IOperation? IdentityCastConcatenation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var current = operation;
        while (current is IConversionOperation
            {
                OperatorMethod: null, IsTryCast: false, Conversion.IsIdentity: true,
                Type.SpecialType: SpecialType.System_String
            } conversion)
        { current = conversion.Operand; }
        return !ReferenceEquals(current, operation) && IsStringConcatenation(current) && !current.ConstantValue.HasValue
            ? current : null;
    }

    internal static IrTerm StringConcatenationAllocates(IrFactory factory, ImmutableArray<IrTerm> operands)
    {

        RepositoryParameterGuard.RequireInitialized(operands.IsDefault, nameof(operands), "Array must be initialized.");
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        IrTerm seen = factory.Boolean(false);
        IrTerm allocates = factory.Boolean(false);
        foreach (var operand in operands)
        {
            // Total Length(null) is zero, matching String.Concat's treatment.
            var nonempty = factory.Binary(IrBinaryOperator.GreaterThan, factory.Length(operand),
                factory.Integer(factory.IntegerType, 0));
            allocates = factory.Binary(IrBinaryOperator.OrElse, allocates,
                factory.Binary(IrBinaryOperator.AndAlso, seen, nonempty));
            seen = factory.Binary(IrBinaryOperator.OrElse, seen, nonempty);
        }
        return allocates;
    }

    // A class constructor that runs exactly its own body: the class derives
    // from object, has no instance member initializers or primary
    // constructor, and the constructor chains to no other constructor.
    internal static bool IsPlainConstructor(IMethodSymbol constructor, CancellationToken cancellationToken)
    {
        constructor = RepositoryParameterGuard.NotNull(constructor, nameof(constructor));
        return IsDirectObjectConstructor(constructor, false, cancellationToken) && !ChainsToThis(constructor, cancellationToken);
    }

    internal static bool IsDirectObjectConstructor(IMethodSymbol constructor, bool includeInitializers, CancellationToken cancellationToken)
    {
        if (constructor is not { MethodKind: MethodKind.Constructor, IsStatic: false } ||
            constructor.ContainingType is not { TypeKind: TypeKind.Class, IsRecord: false } and not { TypeKind: TypeKind.Struct } ||
            constructor.ContainingType is { TypeKind: TypeKind.Struct } && !IsModeledStruct(constructor.ContainingType))
        { return false; }
        var type = constructor.ContainingType;
        // A class whose base is a source class runs that class's constructor
        // first. Only a caller that runs instance initializers itself can run
        // the chain in C#'s order, so only it admits one.
        var sourceBase = type.TypeKind == TypeKind.Class && type.BaseType?.SpecialType != SpecialType.System_Object;
        if (sourceBase && (!includeInitializers || !HasSourceBaseChain(type, cancellationToken)))
        { return false; }
        // `: this(arguments)` runs another constructor of the same type on the
        // same object first, and `: base(arguments)` one of a source base
        // class; the body graph holds that call, which the lowerer inlines on
        // the receiver.
        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax syntax ||
                syntax.Initializer is { } initializer &&
                !initializer.ThisOrBaseKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThisKeyword) &&
                initializer.ArgumentList.Arguments.Count != 0 && !sourceBase)
            { return false; }
        }
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax declaration ||
                declaration.ParameterList != null || !includeInitializers && declaration.Members.Any(static member => member switch
                {
                    Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax field => !IsStatic(field.Modifiers) &&
                        field.Declaration.Variables.Any(static variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Initializer != null)),
                    Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax property => !IsStatic(property.Modifiers) &&
                        property.Initializer != null,
                    Microsoft.CodeAnalysis.CSharp.Syntax.EventFieldDeclarationSyntax eventField => !IsStatic(eventField.Modifiers) &&
                        eventField.Declaration.Variables.Any(static variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Initializer != null)),
                    _ => false
                }))
            { return false; }
        }
        return true;

        static bool IsStatic(SyntaxTokenList modifiers)
        { return modifiers.Any(static modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)); }
    }

    // Every base class of a class down to object is a class declared in the
    // same assembly, without a primary constructor or a finalizer and not a record. A
    // metadata base other than object runs a constructor whose body is not
    // available here.
    internal static bool HasSourceBaseChain(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        if (HasFinalizer(type))
        { return false; }
        var depth = 0;
        for (var ancestor = type.BaseType; ancestor is not { SpecialType: SpecialType.System_Object }; ancestor = ancestor.BaseType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ancestor is not { TypeKind: TypeKind.Class, IsRecord: false } || ++depth > 16 ||
                ancestor.DeclaringSyntaxReferences.IsEmpty ||
                !SymbolEqualityComparer.Default.Equals(ancestor.ContainingAssembly, type.ContainingAssembly) ||
                HasFinalizer(ancestor))
            { return false; }
            foreach (var reference in ancestor.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is not Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax { ParameterList: null })
                { return false; }
            }
        }
        return true;

        static bool HasFinalizer(INamedTypeSymbol candidate)
        { return candidate.GetMembers().Any(static member => member is IMethodSymbol { MethodKind: MethodKind.Destructor }); }
    }

    // The base constructor an implicitly declared constructor of a class
    // runs: `base()` binds the base class's parameterless constructor when no
    // other one can be called without arguments. Null when the base is
    // object, whose constructor does nothing; false when the base is not a
    // source class or the call is not that one.
    internal static bool TryImplicitBaseConstructor(INamedTypeSymbol type, CancellationToken cancellationToken, out IMethodSymbol? constructor)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        constructor = null;
        if (type.TypeKind != TypeKind.Class || type.BaseType is not { } baseType)
        { return false; }
        if (baseType.SpecialType == SpecialType.System_Object)
        { return true; }
        if (!HasSourceBaseChain(type, cancellationToken))
        { return false; }
        foreach (var candidate in baseType.InstanceConstructors)
        {
            if (candidate.Parameters.IsEmpty)
            {
                if (constructor != null || candidate.DeclaredAccessibility == Accessibility.Private)
                { return false; }
                constructor = candidate;
            }
            else if (candidate.Parameters.All(static parameter => parameter.IsOptional || parameter.IsParams))
            { return false; }
        }
        return constructor != null;
    }

    // A constructor that can run on an object of a more derived class, or a
    // constructor it chains to with `: this(arguments)`, dispatches a virtual
    // member of `this`. Such a call runs the most derived override, which
    // the declaration does not show. A `base.` access does not dispatch.
    internal static bool ConstructorDispatchesOnThis(IMethodSymbol constructor, Compilation? compilation, CancellationToken cancellationToken)
    {
        constructor = RepositoryParameterGuard.NotNull(constructor, nameof(constructor));
        for (var depth = 0; depth < 16; depth++)
        {
            if (constructor.IsImplicitlyDeclared)
            { return false; }
            if (compilation == null || constructor.DeclaringSyntaxReferences.Length != 1 ||
                constructor.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not { } syntax ||
                !compilation.ContainsSyntaxTree(syntax.SyntaxTree) ||
                Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, syntax.SyntaxTree).GetOperation(syntax, cancellationToken) is not { } body)
            { return true; }
            IMethodSymbol? chained = null;
            foreach (var operation in body.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (operation)
                {
                    case IInvocationOperation { Instance: IInstanceReferenceOperation, TargetMethod.MethodKind: MethodKind.Constructor } call:
                        if (SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, constructor.ContainingType))
                        { chained = call.TargetMethod; }
                        break;
                    case IInvocationOperation { Instance: IInstanceReferenceOperation, IsVirtual: true }:
                    case IMethodReferenceOperation { Instance: IInstanceReferenceOperation, IsVirtual: true }:
                        return true;
                    case IPropertyReferenceOperation { Instance: IInstanceReferenceOperation receiver, Property: var property }
                        when (property.IsVirtual || property.IsAbstract || property.IsOverride) && !IsBaseAccess(receiver):
                        return true;
                    case IEventReferenceOperation { Instance: IInstanceReferenceOperation receiver, Event: var accessed }
                        when (accessed.IsVirtual || accessed.IsAbstract || accessed.IsOverride) && !IsBaseAccess(receiver):
                        return true;
                }
            }
            if (chained == null)
            { return false; }
            constructor = chained;
        }
        return true;

        static bool IsBaseAccess(IOperation receiver)
        { return receiver.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.BaseExpressionSyntax; }
    }

    // A constructor declared `: this(arguments)` that runs another declared
    // constructor. Instance field and property initializers run in that one,
    // not in this one. A struct's `: this()` to its implicit parameterless
    // constructor only zeroes the value, so this constructor still runs the
    // initializers after it.
    internal static bool ChainsToThis(IMethodSymbol constructor, CancellationToken cancellationToken)
    {
        constructor = RepositoryParameterGuard.NotNull(constructor, nameof(constructor));
        return constructor.MethodKind == MethodKind.Constructor && constructor.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax { Initializer: { } initializer } &&
            initializer.ThisOrBaseKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThisKeyword) &&
            !(constructor.ContainingType.TypeKind == TypeKind.Struct && initializer.ArgumentList.Arguments.Count == 0 &&
                constructor.ContainingType.InstanceConstructors.All(static other => !other.Parameters.IsEmpty || other.IsImplicitlyDeclared)));
    }

    // The call a `: this(arguments)` initializer makes: another constructor
    // of the type under construction, on the object under construction; or
    // the call a `: base(arguments)` initializer, written or implicit, makes
    // to a constructor of a source base class. C# has no other way to call a
    // constructor on `this`.
    internal static bool IsChainedConstructorCall(IInvocationOperation invocation, IMethodSymbol owner)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        return invocation is
        {
            TargetMethod.MethodKind: MethodKind.Constructor,
            Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
        } && owner.MethodKind == MethodKind.Constructor &&
            (SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType, owner.ContainingType) ||
                IsSourceBaseConstructorCall(invocation.TargetMethod, owner.ContainingType));
    }

    // A constructor of the direct base class of a class, declared in the
    // same assembly.
    internal static bool IsSourceBaseConstructorCall(IMethodSymbol target, INamedTypeSymbol? type)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        return type is { TypeKind: TypeKind.Class, BaseType: { SpecialType: not SpecialType.System_Object } baseType } &&
            target.MethodKind == MethodKind.Constructor && !target.IsStatic &&
            SymbolEqualityComparer.Default.Equals(target.ContainingType, baseType) &&
            SymbolEqualityComparer.Default.Equals(baseType.ContainingAssembly, type.ContainingAssembly) &&
            !baseType.DeclaringSyntaxReferences.IsEmpty;
    }

    internal static bool IsObjectConstructorCall(IOperation? operation)
    {
        return operation is IInvocationOperation
        {
            TargetMethod: { MethodKind: MethodKind.Constructor, ContainingType.SpecialType: SpecialType.System_Object },
            Arguments.Length: 0
        };
    }

    internal static bool IsCoreObjectCreation(IObjectCreationOperation creation)
    {
        creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
        return creation is
        {
            Type.SpecialType: SpecialType.System_Object,
            Constructor.Parameters.Length: 0, Arguments.Length: 0, Initializer: null
        } &&
            creation.Constructor.DeclaringSyntaxReferences.Length == 0;
    }

    internal static bool IsExplicitDelegateCreation(IDelegateCreationOperation creation)
    {
        // Explicit construction creates a fresh delegate. Method-group and
        // lambda conversions can reuse compiler-generated cached instances.
        creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
        return !creation.IsImplicit && creation.Type?.TypeKind == TypeKind.Delegate &&
            creation.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.ObjectCreationExpressionSyntax &&
            creation.Target is IMethodReferenceOperation
            {
                Method: { MethodKind: MethodKind.Ordinary, Arity: 0, ContainingType.Arity: 0 }
            } target &&
            (target is { Instance: null, Method.IsStatic: true } ||
                target is { Instance.Type.IsReferenceType: true, Method: { IsStatic: false, IsVirtual: false, IsOverride: false, IsAbstract: false } });
    }

    internal static TotalScalarRule DelegateReceiver(IrFactory factory, IrTerm receiver, IrTerm? mayCheckNull = null)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        receiver = RepositoryParameterGuard.NotNull(receiver, nameof(receiver));
        var isNull = factory.Binary(IrBinaryOperator.Equal, receiver, factory.Null(receiver.Type));
        var fails = mayCheckNull == null ? isNull : factory.Binary(IrBinaryOperator.AndAlso, isNull, mayCheckNull);
        return new(receiver,
            [new(IrExceptionKind.Argument, fails)],
            FrontendSubsetClassification.Exact);
    }

    internal static bool DelegateValueIsErased(IDelegateCreationOperation creation, Compilation? compilation)
    {
        creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
        if (compilation == null)
        { return false; }
        var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, creation.Syntax.SyntaxTree);
        SyntaxNode value = creation.Syntax;
        while (value.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax parenthesized)
        { value = parenthesized; }
        ILocalSymbol? local = null;
        if (value.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.AssignmentExpressionSyntax assignment &&
            assignment.Right == value && assignment.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionStatementSyntax &&
            model.GetOperation(assignment) is ISimpleAssignmentOperation { IsRef: false } store)
        {
            // An explicit discard omits construction in Debug and Release.
            if (store.Target is IDiscardOperation)
            { return true; }
            local = (store.Target as ILocalReferenceOperation)?.Local;
        }
        else if (value.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.EqualsValueClauseSyntax { Parent: Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax declaration })
        { local = model.GetDeclaredSymbol(declaration) as ILocalSymbol; }

        // Release can discard an unobserved local's standalone stores. A read,
        // ref use, capture or assignment whose value escapes keeps construction.
        // Debug retains user locals even when they are never read.
        if (model.Compilation.Options.OptimizationLevel != OptimizationLevel.Release ||
            local is not { RefKind: RefKind.None, ContainingSymbol: IMethodSymbol owner } ||
            owner.DeclaringSyntaxReferences.Length != 1 ||
            model.GetOperation(owner.DeclaringSyntaxReferences[0].GetSyntax()) is not { } body)
        { return false; }
        foreach (var reference in body.Descendants().OfType<ILocalReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(reference.Local, local))
            { continue; }
            if (!(reference.Parent is ISimpleAssignmentOperation { IsRef: false } write &&
                ReferenceEquals(write.Target, reference) &&
                write.Syntax.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionStatementSyntax))
            { return false; }
            // Even a write-only capture becomes an observable closure-field store.
            for (var ancestor = reference.Parent; ancestor != null && ancestor != body; ancestor = ancestor.Parent)
            {
                if (ancestor is IAnonymousFunctionOperation or ILocalFunctionOperation)
                { return false; }
            }
        }
        return true;
    }

    internal static bool DelegateValueEscapesDirectly(IDelegateCreationOperation creation)
    {
        creation = RepositoryParameterGuard.NotNull(creation, nameof(creation));
        return creation.Syntax.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax or
            Microsoft.CodeAnalysis.CSharp.Syntax.ArrowExpressionClauseSyntax;
    }

    internal static bool IsScalarBoxing(IConversionOperation conversion)
    {
        conversion = RepositoryParameterGuard.NotNull(conversion, nameof(conversion));
        return conversion.Type?.SpecialType == SpecialType.System_Object && IsScalar(conversion.Operand.Type) && !IsEnum(conversion.Operand.Type) &&
            conversion.OperatorMethod == null && conversion.Conversion.Exists && !conversion.IsTryCast;
    }
}
