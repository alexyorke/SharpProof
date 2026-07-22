namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer {
    private static ImmutableArray<EffectiveExceptionContract> CollectExceptionContracts(
        MethodBodyAnalysisContext context,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var builder = ImmutableArray.CreateBuilder<EffectiveExceptionContract>(2);
        var doesNotThrow = attributePolicy.GetAcceptedAttributes(context.MethodSymbol, "DoesNotThrowAttribute").FirstOrDefault();
        if (doesNotThrow != null)
            builder.Add(new EffectiveExceptionContract(
                ExceptionContractKind.DoesNotThrow,
                [],
                "[DoesNotThrow]",
                GetAttributeLocation(doesNotThrow, context.CancellationToken)));

        var allAllowedTypes = ImmutableArray.CreateBuilder<ITypeSymbol>();
        Location? allowedLocation = null;
        var hasValidAllowedContract = false;
        var exceptionBase = context.SemanticModel.Compilation.GetTypeByMetadataName("System.Exception");
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(context.MethodSymbol, "AllowedExceptionsAttribute")) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var attributeTypes = CollectAllowedExceptionTypes(attribute, exceptionBase, context.CancellationToken,
                out var invalidArguments);
            if (!invalidArguments.IsDefaultOrEmpty) {
                foreach (var invalidArgument in invalidArguments)
                    context.ReportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                        "[AllowedExceptions]",
                        invalidArgument.Argument,
                        invalidArgument.Reason,
                        invalidArgument.Location ?? GetAttributeLocation(attribute, context.CancellationToken) ??
                        AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node)));
                continue;
            }
            hasValidAllowedContract = true;
            allowedLocation ??= GetAttributeLocation(attribute, context.CancellationToken);
            foreach (var allowedType in attributeTypes)
                if (!ContainsSymbol(allAllowedTypes, allowedType))
                    allAllowedTypes.Add(allowedType);
        }
        if (hasValidAllowedContract)
            builder.Add(new EffectiveExceptionContract(
                ExceptionContractKind.AllowedExceptions,
                allAllowedTypes.ToImmutable(),
                "[AllowedExceptions]",
                allowedLocation));

        return builder.ToImmutable();
    }
    private static void AnalyzeExceptionContracts(
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        ImmutableArray<EffectiveExceptionContract> contracts,
        ImmutableArray<ExceptionFactView> facts) {
        foreach (var contract in contracts)
            AnalyzeExceptionContract(context, methodSymbol, contract, facts);
    }
    private static void AnalyzeExceptionContract(
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        EffectiveExceptionContract contract,
        ImmutableArray<ExceptionFactView> siteEntries) {
        foreach (var siteGroup in siteEntries.GroupBy(static entry => entry.Site.Span)) {
            var firstEntry = siteGroup.First();
            var disallowedSites = siteGroup.Where(site => !IsAllowedByExceptionContract(contract, site)).ToArray();
            if (disallowedSites.Length == 0) continue;

            var siteLocation = GetExceptionSiteLocation(firstEntry.Site);
            var operationDisplay = firstEntry.Site.ToString();
            var exceptionList = string.Join(", ", disallowedSites
                .Select(static site => site.ExceptionType)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static type => type, StringComparer.Ordinal));
            context.ReportDiagnostic(Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("ExceptionContractViolationRule"),
                siteLocation,
                AdditionalLocations(contract.Location),
                methodSymbol.Name,
                contract.AttributeDisplay,
                operationDisplay,
                exceptionList));
        }
    }
    private static ImmutableArray<ITypeSymbol> CollectAllowedExceptionTypes(
        AttributeData attribute,
        INamedTypeSymbol? exceptionBase,
        CancellationToken cancellationToken,
        out ImmutableArray<InvalidExceptionContractArgument> invalidArguments) {
        var invalidBuilder = ImmutableArray.CreateBuilder<InvalidExceptionContractArgument>();
        if (exceptionBase == null) {
            invalidBuilder.Add(new InvalidExceptionContractArgument(
                AnalyzerSyntaxHelpers.GetAttributeArgumentListText(attribute, cancellationToken),
                "could not resolve System.Exception",
                GetAttributeLocation(attribute, cancellationToken)));
            invalidArguments = invalidBuilder.ToImmutable();
            return [];
        }
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Kind != TypedConstantKind.Array) {
            invalidBuilder.Add(new InvalidExceptionContractArgument(
                AnalyzerSyntaxHelpers.GetAttributeArgumentListText(attribute, cancellationToken),
                "expected exception type list",
                GetAttributeLocation(attribute, cancellationToken)));
            invalidArguments = invalidBuilder.ToImmutable();
            return [];
        }
        var values = attribute.ConstructorArguments[0].Values;
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>(values.Length);
        for (var index = 0; index < values.Length; index++) {
            var value = values[index];
            if (value.Kind != TypedConstantKind.Type ||
                value.Value is not ITypeSymbol allowedType) {
                invalidBuilder.Add(CreateInvalidAllowedExceptionArgument(
                    attribute,
                    index,
                    value,
                    "expected System.Type arguments",
                    cancellationToken));
                continue;
            }
            if (!TypeHierarchyEnumeration.IsSameOrDerivedFrom(allowedType, exceptionBase, TypeIdentityPolicy.ExactOrOriginalDefinition)) {
                invalidBuilder.Add(CreateInvalidAllowedExceptionArgument(
                    attribute,
                    index,
                    value,
                    "type '" + FormatType(allowedType) + "' must derive from System.Exception",
                    cancellationToken));
                continue;
            }
            if (!ContainsSymbol(builder, allowedType)) builder.Add(allowedType);
        }
        invalidArguments = invalidBuilder.ToImmutable();
        return builder.ToImmutable();
    }
    private static InvalidExceptionContractArgument CreateInvalidAllowedExceptionArgument(
        AttributeData attribute,
        int index,
        TypedConstant value,
        string reason,
        CancellationToken cancellationToken) {
        AttributeArgumentSyntax? argumentSyntax = null;
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax attributeSyntax) {
            var arguments = attributeSyntax.ArgumentList?.Arguments;
            if (arguments is { Count: > 0 }) {
                if (arguments.Value.Count == attribute.ConstructorArguments[0].Values.Length &&
                    index < arguments.Value.Count)
                    argumentSyntax = arguments.Value[index];
                else
                    argumentSyntax = arguments.Value
                        .SelectMany(static argument => argument.DescendantNodesAndSelf().OfType<TypeOfExpressionSyntax>())
                        .ElementAtOrDefault(index)
                        ?.FirstAncestorOrSelf<AttributeArgumentSyntax>();
            }
        }
        var argumentText = argumentSyntax?.ToString() ??
                           (value.Value is ITypeSymbol type ? "typeof(" + FormatType(type) + ")" : value.ToString());
        return new InvalidExceptionContractArgument(argumentText, reason, argumentSyntax?.GetLocation());
    }
    private static bool IsAllowedByExceptionContract(EffectiveExceptionContract contract, ExceptionFactView exception) {
        if (contract.Kind == ExceptionContractKind.DoesNotThrow) return false;

        if (exception.Type == null) return false;

        foreach (var allowedType in contract.AllowedTypes)
            if (TypeHierarchyEnumeration.IsSameOrDerivedFrom(exception.Type, allowedType, TypeIdentityPolicy.ExactOrOriginalDefinition))
                return true;

        return false;
    }
    private static bool ContainsSymbol(IEnumerable<ITypeSymbol> symbols, ITypeSymbol candidate) => symbols.Any(symbol
        => SymbolEq.AreEqual(symbol.OriginalDefinition, candidate.OriginalDefinition));
    private static string FormatType(ITypeSymbol type) {
        var display = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return string.IsNullOrWhiteSpace(display)
            ? type.Name
            : display;
    }
    private static Location? GetAttributeLocation(AttributeData attribute, CancellationToken cancellationToken)
        => attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
    private static IEnumerable<Location>? AdditionalLocations(Location? location) =>
        location == null ? null : [location];

    enum ExceptionContractKind {
        DoesNotThrow,
        AllowedExceptions
    }
    readonly record struct InvalidExceptionContractArgument(string Argument, string Reason, Location? Location);

    readonly record struct EffectiveExceptionContract(
        ExceptionContractKind Kind,
        ImmutableArray<ITypeSymbol> AllowedTypes,
        string AttributeDisplay,
        Location? Location);
}
