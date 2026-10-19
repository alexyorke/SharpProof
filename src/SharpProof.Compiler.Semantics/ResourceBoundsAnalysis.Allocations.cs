namespace SharpProof.Compiler.Semantics;

internal sealed partial class ResourceBoundsAnalysis
{
    private Cost DelegateCreation(IDelegateCreationOperation creation, int depth)
    {
        creation = ArgumentNullGuard.NotNull(creation, nameof(creation));
        if (!CSharpOperationSemantics.IsExplicitDelegateCreation(creation))
        { return Unknown("UnsupportedDelegateAllocation"); }
        var target = (IMethodReferenceOperation)creation.Target;
        var receiver = target.Instance is { } instance ? Visit(instance, depth + 1) : Cost.Zero;
        if (CSharpOperationSemantics.DelegateValueIsErased(creation, _compilation))
        { return receiver; }
        if (!target.Method.ContainingType.StaticConstructors.IsEmpty)
        { return Unknown("DelegateTypeInitializationUnknown"); }
        if (target.Method.IsExtern || target.Method.DeclaringSyntaxReferences.IsEmpty || _compilation is null ||
            target.Method.DeclaringSyntaxReferences.Any(reference => reference is null ? throw new ArgumentNullException(nameof(reference)) :
                !_compilation.ContainsSyntaxTree(reference.SyntaxTree)))
        { return Unknown("DelegateTargetBindingUnknown"); }
        return receiver.Add(new(Bound.Zero, Bound.Zero, Bound.Zero, ResourceProgram.Managed(ResourceAllocationShape.Delegate())));
    }

    private Cost ObjectCreation(IObjectCreationOperation creation, int depth)
    {
        creation = ArgumentNullGuard.NotNull(creation, nameof(creation));
        if (CSharpOperationSemantics.IsRepeatedCharacterStringCreation(creation))
        {
            var stringArguments = Sequence(creation.Arguments.Select(static argument => argument is null ? throw new ArgumentNullException(nameof(argument)) : argument.Value), depth + 1);
            var count = creation.Arguments.Single(static argument => argument is null ? throw new ArgumentNullException(nameof(argument)) : argument.Parameter!.Ordinal == 1).Value;
            var size = Size(count);
            if (size is null)
            { return Unknown("UnsupportedStringAllocationSize"); }
            var allocation = count.ConstantValue is { HasValue: true, Value: int constant } && constant <= 0
                ? ResourceProgram.Zero
                : ResourceProgram.Choice(ResourceProgram.Zero, ResourceProgram.Managed(ResourceAllocationShape.String(size.Expression)));
            return stringArguments.Add(new(Bound.Zero, Bound.Zero, Bound.Zero, allocation));
        }
        if (CSharpOperationSemantics.IsCoreObjectCreation(creation))
        { return new(Bound.Zero, Bound.Zero, Bound.Zero, ResourceProgram.Managed(ResourceAllocationShape.Object([]))); }
        if (_compilation is null || creation.Type is not INamedTypeSymbol { TypeKind: TypeKind.Class } type ||
            type.IsRecord || type.IsAnonymousType || type.IsGenericType || type.IsComImport || type.BaseType?.SpecialType != SpecialType.System_Object ||
            type.DeclaringSyntaxReferences.IsEmpty || creation.Constructor is not { } constructor ||
            constructor.Parameters.Any(static parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) :
                parameter.RefKind != RefKind.None || !SupportedType(parameter.Type)))
        { return Unknown("UnsupportedManagedConstructor"); }
        if (!type.StaticConstructors.IsEmpty)
        { return Unknown("ConstructorTypeInitializationUnknown"); }
        if (_activeMethods.Contains(constructor))
        { return Unknown("RecursiveConstructorCostUnknown"); }
        if (type.GetAttributes().Any(static attribute => attribute is null ? throw new ArgumentNullException(nameof(attribute)) :
            attribute.AttributeClass is
            {
                MetadataName: "StructLayoutAttribute", ContainingType: null,
                ContainingNamespace:
                {
                    Name: "InteropServices", ContainingNamespace:
                    {
                        Name: "Runtime",
                        ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                    }
                }
            }))
        { return Unknown("UnsupportedManagedLayout"); }
        var compilation = ArgumentNullGuard.NotNull(_compilation, nameof(_compilation));
        var members = type.GetMembers();
        _remaining -= members.Length;
        if (_remaining < 0)
        { return Unknown("AnalysisBudgetExceeded"); }
        var fields = ImmutableArray.CreateBuilder<ResourceStorageKind>();
        var initializers = new List<IOperation>();
        foreach (var member in members)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (member is IEventSymbol or IMethodSymbol { MethodKind: MethodKind.Destructor })
            { return Unknown("UnsupportedManagedLayout"); }
            if (member is IFieldSymbol { IsStatic: false, IsConst: false } field)
            {
                var storage = FieldStorage(field.Type);
                if (field.IsFixedSizeBuffer || storage == ResourceStorageKind.Unspecified)
                { return Unknown("UnsupportedManagedFieldStorage"); }
                fields.Add(storage);
                foreach (var reference in field.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax(_cancellation) is VariableDeclaratorSyntax { Initializer.Value: { } value })
                    { AddInitializer(value); }
                }
            }
            if (member is IPropertySymbol { IsStatic: false } property)
            {
                if (property.IsIndexer || property.DeclaringSyntaxReferences.Length != 1 ||
                    property.DeclaringSyntaxReferences[0].GetSyntax(_cancellation) is not PropertyDeclarationSyntax declaration ||
                    declaration.ExpressionBody is not null || declaration.Modifiers.Any(SyntaxKind.PartialKeyword) ||
                    declaration.AccessorList is not { } accessors || accessors.Accessors.Any(static accessor => accessor is null ? throw new ArgumentNullException(nameof(accessor)) :
                        accessor.Body is not null || accessor.ExpressionBody is not null))
                { return Unknown("UnsupportedManagedLayout"); }
                if (!members.OfType<IFieldSymbol>().Any(field => field is null ? throw new ArgumentNullException(nameof(field)) :
                    SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property)))
                {
                    var storage = FieldStorage(property.Type);
                    if (storage == ResourceStorageKind.Unspecified)
                    { return Unknown("UnsupportedManagedFieldStorage"); }
                    fields.Add(storage);
                }
                if (declaration.Initializer?.Value is { } value)
                { AddInitializer(value); }
            }
        }
        var arguments = Cost.Zero;
        foreach (var argument in creation.Arguments)
        {
            arguments = arguments.Add(Visit(argument.Value, depth + 1));
            if (argument.Parameter is not { } parameter)
            { return Unknown("UnboundConstructorArgument"); }
            _scalarArguments.Add(parameter, Size(argument.Value));
            _arrayArguments.Add(parameter, ArraySize(argument.Value));
        }
        _activeMethods.Add(constructor);
        try
        {
            var allocation = new Cost(Bound.Zero, Bound.Zero, Bound.Zero, ResourceProgram.Managed(ResourceAllocationShape.Object(fields.ToImmutable())));
            var effects = Sequence(initializers, depth + 1);
            if (!constructor.IsImplicitlyDeclared)
            {
                if (constructor.DeclaringSyntaxReferences.Length != 1 ||
                    constructor.DeclaringSyntaxReferences[0].GetSyntax(_cancellation) is not ConstructorDeclarationSyntax declaration ||
                    declaration.Initializer is { } initializer &&
                        (!initializer.IsKind(SyntaxKind.BaseConstructorInitializer) || initializer.ArgumentList.Arguments.Count != 0))
                { return Unknown("UnsupportedManagedConstructor"); }
                var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree);
                var body = declaration.Body is { } block ? model.GetOperation(block, _cancellation) :
                    declaration.ExpressionBody is { } arrow ? model.GetOperation(arrow.Expression, _cancellation) : null;
                if (body is null)
                { return Unknown("MissingConstructorBody"); }
                effects = effects.Add(CallableBody(constructor, body, depth + 1));
            }
            if (creation.Initializer is { } objectInitializer)
            { effects = effects.Add(Sequence(objectInitializer.Initializers, depth + 1)); }
            return arguments.Add(allocation).Add(effects);
        }
        finally
        {
            _activeMethods.Remove(constructor);
            foreach (var parameter in constructor.Parameters)
            {
                _scalarArguments.Remove(parameter);
                _arrayArguments.Remove(parameter);
            }
        }

        void AddInitializer(ExpressionSyntax value)
        {
            value = ArgumentNullGuard.NotNull(value, nameof(value));
            var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, value.SyntaxTree);
            if (model.GetOperation(value, _cancellation) is { } operation)
            { initializers.Add(operation); }
            else
            { _unknown ??= "MissingFieldInitializer"; }
        }
    }

    private static ResourceStorageKind FieldStorage(ITypeSymbol type)
    {
        type = ArgumentNullGuard.NotNull(type, nameof(type));
        return Primitive(type) ? Storage(type) : type.IsReferenceType ? ResourceStorageKind.Reference : ResourceStorageKind.Unspecified;
    }
}
