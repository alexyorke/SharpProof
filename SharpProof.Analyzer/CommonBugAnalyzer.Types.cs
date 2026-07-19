namespace SharpProof.Analyzer;

internal static partial class CommonBugAnalyzer
{
    private static void AnalyzeNamedTypeCore(
        SymbolAnalysisContext context,
        AnalyzerSession session,
        INamedTypeSymbol type)
    {
        if (type.DeclaringSyntaxReferences.IsDefaultOrEmpty) return;

        AnalyzeMutableStruct(context, session, type);
        AnalyzeOwnedDisposableFields(context, session, type);
        AnalyzeIneffectiveRequiredAttributes(context, session, type);
    }

    private static void AnalyzeMutableStruct(
        SymbolAnalysisContext context,
        AnalyzerSession session,
        INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Struct || type.IsReadOnly || !HasMutableInstanceState(type)) return;

        var location = GetTypeIdentifierLocation(type, context.CancellationToken);
        if (location == null) return;

        Report(
            context,
            session,
            AnalyzerDiagnosticCatalog.Get("MutableStructRule"),
            type,
            location,
            "mutable_struct",
            type.Name);
    }

    private static bool HasMutableInstanceState(INamedTypeSymbol type)
    {
        return type.GetMembers().Any(member => member switch
        {
            IFieldSymbol { IsStatic: false, IsConst: false, IsReadOnly: false, IsImplicitlyDeclared: false } => true,
            IPropertySymbol { IsStatic: false, SetMethod: { IsInitOnly: false } } => true,
            _ => false
        });
    }

    private static void AnalyzeOwnedDisposableFields(
        SymbolAnalysisContext context,
        AnalyzerSession session,
        INamedTypeSymbol type)
    {
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (field.IsStatic || field.IsConst || field.IsImplicitlyDeclared ||
                field.DeclaringSyntaxReferences.IsDefaultOrEmpty ||
                !TryGetRequiredDisposalInterface(field.Type, out var disposalInterface) ||
                ImplementsInterface(type, disposalInterface) ||
                !IsDefinitelyOwnedField(field, type, context.Compilation, context.CancellationToken))
                continue;

            var location = GetFieldIdentifierLocation(field, context.CancellationToken);
            if (location == null) continue;

            Report(
                context,
                session,
                AnalyzerDiagnosticCatalog.Get("OwnedDisposableFieldRule"),
                field,
                location,
                "owned_disposable_field",
                type.Name,
                field.Name,
                disposalInterface);
        }
    }

    private static bool IsDefinitelyOwnedField(
        IFieldSymbol field,
        INamedTypeSymbol containingType,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var syntaxReference in field.DeclaringSyntaxReferences)
            if (syntaxReference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                {
                    Initializer.Value: { } initializer
                })
            {
                var semanticModel = compilation.GetSemanticModel(initializer.SyntaxTree);
                if (Unwrap(semanticModel.GetOperation(initializer, cancellationToken)) is IObjectCreationOperation)
                    return true;
            }

        foreach (var constructor in containingType.InstanceConstructors)
            foreach (var syntaxReference in constructor.DeclaringSyntaxReferences)
            {
                var declaration = syntaxReference.GetSyntax(cancellationToken);
                var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
                foreach (var assignment in declaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    if (semanticModel.GetOperation(assignment, cancellationToken) is ISimpleAssignmentOperation
                        {
                            Target: IFieldReferenceOperation target,
                            Value: { } value
                        } &&
                        SymbolEq.AreEqual(target.Field, field) &&
                        Unwrap(value) is IObjectCreationOperation)
                        return true;
            }

        return false;
    }

    private static bool TryGetRequiredDisposalInterface(ITypeSymbol type, out string interfaceName)
    {
        if (ImplementsInterface(type, "System.IDisposable"))
        {
            interfaceName = "System.IDisposable";
            return true;
        }

        if (ImplementsInterface(type, "System.IAsyncDisposable"))
        {
            interfaceName = "System.IAsyncDisposable";
            return true;
        }

        interfaceName = string.Empty;
        return false;
    }

    private static bool ImplementsInterface(ITypeSymbol type, string metadataName)
    {
        return type is INamedTypeSymbol namedType && namedType.AllInterfaces.Any(candidate =>
            string.Equals(candidate.ToDisplayString(), metadataName, StringComparison.Ordinal));
    }

    private static void AnalyzeIneffectiveRequiredAttributes(
        SymbolAnalysisContext context,
        AnalyzerSession session,
        INamedTypeSymbol type)
    {
        var requiredAttributeType = context.Compilation.GetTypeByMetadataName(
            "System.ComponentModel.DataAnnotations.RequiredAttribute");
        foreach (var member in type.GetMembers())
        {
            ITypeSymbol? memberType = member switch
            {
                IFieldSymbol { IsStatic: false } field => field.Type,
                IPropertySymbol { IsStatic: false, IsIndexer: false } property => property.Type,
                _ => null
            };
            if (memberType?.IsValueType != true || IsNullableValueType(memberType)) continue;

            var required = FindAttribute(member, requiredAttributeType);
            var location = required?.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation();
            if (location == null) continue;

            Report(
                context,
                session,
                AnalyzerDiagnosticCatalog.Get("IneffectiveRequiredAttributeRule"),
                member,
                location,
                "ineffective_required_attribute",
                member.Name,
                memberType.ToDisplayString());
        }
    }

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static Location? GetTypeIdentifierLocation(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        var syntax = type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        return syntax switch
        {
            TypeDeclarationSyntax declaration => declaration.Identifier.GetLocation(),
            _ => type.Locations.FirstOrDefault(static location => location.IsInSource)
        };
    }

    private static Location? GetFieldIdentifierLocation(
        IFieldSymbol field,
        CancellationToken cancellationToken)
    {
        var syntax = field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        return syntax is VariableDeclaratorSyntax variable
            ? variable.Identifier.GetLocation()
            : field.Locations.FirstOrDefault(static location => location.IsInSource);
    }
}
