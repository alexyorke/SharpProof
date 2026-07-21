namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer {
    private static ImmutableArray<ExceptionContract> CollectExceptionContracts(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken) {
        var builder = ImmutableArray.CreateBuilder<ExceptionContract>();
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(methodSymbol, "DoesNotThrowAttribute")) {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Add(new ExceptionContract(
                ExceptionContractKind.DoesNotThrow,
                ImmutableArray<ITypeSymbol>.Empty,
                "[DoesNotThrow]",
                GetAttributeLocation(attribute, cancellationToken),
                ImmutableArray<InvalidExceptionContractArgument>.Empty));
        }

        var exceptionBase = semanticModel.Compilation.GetTypeByMetadataName("System.Exception");
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(methodSymbol, "AllowedExceptionsAttribute")) {
            cancellationToken.ThrowIfCancellationRequested();
            var allowedTypes = CollectAllowedExceptionTypes(
                attribute,
                exceptionBase,
                cancellationToken,
                out var invalidArguments);
            builder.Add(new ExceptionContract(
                ExceptionContractKind.AllowedExceptions,
                allowedTypes,
                "[AllowedExceptions]",
                GetAttributeLocation(attribute, cancellationToken),
                invalidArguments));
        }

        return builder.ToImmutable();
    }

    private static void AnalyzeExceptionContracts(
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        ImmutableArray<ExceptionContract> contracts,
        ImmutableArray<ExceptionFactView> facts) {
        if (contracts.Length == 0) return;

        var validContracts = ReportAndFilterInvalidExceptionContracts(
            contracts,
            context);
        if (validContracts.Length == 0) return;

        var effectiveContracts = CreateEffectiveExceptionContracts(validContracts);
        foreach (var contract in effectiveContracts)
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

    private static ImmutableArray<ExceptionContract> ReportAndFilterInvalidExceptionContracts(
        ImmutableArray<ExceptionContract> contracts,
        MethodBodyAnalysisContext context) {
        var validContracts = ImmutableArray.CreateBuilder<ExceptionContract>(contracts.Length);
        foreach (var contract in contracts) {
            if (contract.InvalidArguments.IsDefaultOrEmpty) {
                validContracts.Add(contract);
                continue;
            }

            foreach (var invalidArgument in contract.InvalidArguments) {
                var diagnostic = InvalidContractArgumentDiagnostics.Create(
                    contract.AttributeDisplay,
                    invalidArgument.Argument,
                    invalidArgument.Reason,
                    invalidArgument.Location ?? contract.Location ??
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node));
                context.ReportDiagnostic(diagnostic);
            }
        }

        return validContracts.ToImmutable();
    }

    private static ImmutableArray<EffectiveExceptionContract> CreateEffectiveExceptionContracts(
        ImmutableArray<ExceptionContract> validContracts) {
        var builder = ImmutableArray.CreateBuilder<EffectiveExceptionContract>();
        var doesNotThrow = validContracts
            .Where(static contract => contract.Kind == ExceptionContractKind.DoesNotThrow)
            .ToArray();
        if (doesNotThrow.Length > 0)
            builder.Add(new EffectiveExceptionContract(
                ExceptionContractKind.DoesNotThrow,
                ImmutableArray<ITypeSymbol>.Empty,
                "[DoesNotThrow]",
                doesNotThrow[0].Location));

        var allowedExceptions = validContracts
            .Where(static contract => contract.Kind == ExceptionContractKind.AllowedExceptions)
            .ToArray();
        if (allowedExceptions.Length > 0) {
            var allowedTypes = ImmutableArray.CreateBuilder<ITypeSymbol>();
            foreach (var contract in allowedExceptions)
                foreach (var allowedType in contract.AllowedTypes)
                    if (!ContainsSymbol(allowedTypes, allowedType))
                        allowedTypes.Add(allowedType);

            builder.Add(new EffectiveExceptionContract(
                ExceptionContractKind.AllowedExceptions,
                allowedTypes.ToImmutable(),
                "[AllowedExceptions]",
                allowedExceptions[0].Location));
        }

        return builder.ToImmutable();
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
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Kind != TypedConstantKind.Array) {
            invalidBuilder.Add(new InvalidExceptionContractArgument(
                AnalyzerSyntaxHelpers.GetAttributeArgumentListText(attribute, cancellationToken),
                "expected exception type list",
                GetAttributeLocation(attribute, cancellationToken)));
            invalidArguments = invalidBuilder.ToImmutable();
            return ImmutableArray<ITypeSymbol>.Empty;
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

            if (!TypeHierarchyEnumeration.IsSameOrDerivedFrom(
                    allowedType,
                    exceptionBase,
                    TypeIdentityPolicy.ExactOrOriginalDefinition)) {
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

    private static bool IsAllowedByExceptionContract(
        EffectiveExceptionContract contract,
        ExceptionFactView exception) {
        if (contract.Kind == ExceptionContractKind.DoesNotThrow) return false;

        if (exception.Type == null) return false;

        foreach (var allowedType in contract.AllowedTypes)
            if (TypeHierarchyEnumeration.IsSameOrDerivedFrom(
                    exception.Type,
                    allowedType,
                    TypeIdentityPolicy.ExactOrOriginalDefinition))
                return true;

        return false;
    }

    private static bool ContainsSymbol(
        IEnumerable<ITypeSymbol> symbols,
        ITypeSymbol candidate) {
        return symbols.Any(symbol =>
            SymbolEq.AreEqual(symbol.OriginalDefinition, candidate.OriginalDefinition));
    }

    private static string FormatType(ITypeSymbol type) {
        var display = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return string.IsNullOrWhiteSpace(display)
            ? type.Name
            : display;
    }

    private static Location? GetAttributeLocation(
        AttributeData attribute,
        CancellationToken cancellationToken) {
        return attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
    }

    private static IEnumerable<Location>? AdditionalLocations(Location? location) =>
        location == null ? null : [location];

    enum ExceptionContractKind {
        DoesNotThrow,
        AllowedExceptions
    }

    readonly record struct ExceptionContract(
        ExceptionContractKind Kind,
        ImmutableArray<ITypeSymbol> AllowedTypes,
        string AttributeDisplay,
        Location? Location,
        ImmutableArray<InvalidExceptionContractArgument> InvalidArguments);

    readonly record struct InvalidExceptionContractArgument(
        string Argument,
        string Reason,
        Location? Location);

    readonly record struct EffectiveExceptionContract(
        ExceptionContractKind Kind,
        ImmutableArray<ITypeSymbol> AllowedTypes,
        string AttributeDisplay,
        Location? Location);
}
