namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowEngine {
    private static IEnumerable<ExceptionFlowSite> CollectSourceCalleeExceptionSites(
        ExceptionFlowAnalyzer.MethodCallCandidate call,
        Compilation compilation,
        CancellationToken cancellationToken,
        HashSet<IMethodSymbol> visitedMethods,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var invokedMethod = call.Method;
        var originalDefinition = invokedMethod.OriginalDefinition;
        if (!visitedMethods.Add(originalDefinition)) return Enumerable.Empty<ExceptionFlowSite>();

        try {
            var syntaxReference = invokedMethod.DeclaringSyntaxReferences.FirstOrDefault()
                                  ?? originalDefinition.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference == null) return Enumerable.Empty<ExceptionFlowSite>();

            var syntax = syntaxReference.GetSyntax(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
            var result = AnalyzeMethod(
                invokedMethod,
                syntax,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                attributePolicy,
                visitedMethods);

            var invokedMethodDisplay = GetExceptionSourceMethodDisplay(invokedMethod.OriginalDefinition);
            var symbol = invokedMethod.OriginalDefinition.ToDisplayString();
            return result.Sites
                .GroupBy(static site => site.ExceptionType, StringComparer.Ordinal)
                .SelectMany(group => group
                    .Select(static site => site.Category + ":" + site.Source)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static source => source, StringComparer.Ordinal)
                    .Select(source => {
                    var chainedSource = invokedMethodDisplay + " -> " + source;
                    return new ExceptionFlowSite(
                        call.CallSite,
                        invokedMethod,
                        TryResolveExceptionType(compilation, group.Key),
                        group.Key,
                        ExceptionCategories.SourceCallee,
                        chainedSource,
                        symbol,
                        CreateDerivedDiagnosticEdges(
                            group.Key,
                            ExceptionCategories.SourceCallee,
                            chainedSource,
                            CreatePrefixedCalleeChain(invokedMethodDisplay, chainedSource)));
                }))
                .ToArray();
        }
        finally {
            visitedMethods.Remove(originalDefinition);
        }
    }

    private static IEnumerable<ExceptionFlowSite> CollectCalleeExceptionSites(
        ExceptionFlowAnalyzer.MethodCallCandidate call,
        Compilation compilation,
        CancellationToken cancellationToken,
        HashSet<IMethodSymbol> visitedMethods,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var invokedMethod = call.Method;
        foreach (var exception in CollectSourceCalleeExceptionSites(
                     call,
                     compilation,
                     cancellationToken,
                     visitedMethods,
                     smtAnalysis,
                     attributePolicy))
            yield return exception;
    }

    private static ImmutableArray<string> CreatePrefixedCalleeChain(string invokedMethodDisplay, string qualifiedSource) {
        var (_, source) = SplitQualifiedSource(qualifiedSource);
        var nestedChain = ParseCalleeChainFromSource(source);
        var invokedChain = ParseCalleeChainFromSource(invokedMethodDisplay);
        if (invokedChain.IsDefaultOrEmpty && !string.IsNullOrWhiteSpace(invokedMethodDisplay))
            invokedChain = ImmutableArray.Create(invokedMethodDisplay);

        if (nestedChain.IsDefaultOrEmpty) return invokedChain;
        var skip = !invokedChain.IsDefaultOrEmpty &&
                   nestedChain[0] == invokedChain[invokedChain.Length - 1]
            ? 1
            : 0;
        return invokedChain.Concat(nestedChain.Skip(skip)).ToImmutableArray();
    }

    private static string GetExceptionSourceMethodDisplay(IMethodSymbol methodSymbol) {
        if (methodSymbol.MethodKind != MethodKind.LocalFunction &&
            methodSymbol.MethodKind != MethodKind.AnonymousFunction)
            return methodSymbol.ToDisplayString();

        var containingMethod = methodSymbol.ContainingSymbol as IMethodSymbol;
        var containingDisplay = containingMethod?.OriginalDefinition.ToDisplayString();
        var nestedDisplay = CreateNestedCallableDisplay(methodSymbol);
        if (string.IsNullOrWhiteSpace(containingDisplay) || string.IsNullOrWhiteSpace(nestedDisplay))
            return methodSymbol.ToDisplayString();

        return containingDisplay + " -> " + nestedDisplay;
    }

    private static string CreateNestedCallableDisplay(IMethodSymbol methodSymbol) {
        var containingType = methodSymbol.ContainingType?.ToDisplayString();
        var methodName = methodSymbol.MethodKind == MethodKind.Constructor
            ? ".ctor"
            : string.IsNullOrWhiteSpace(methodSymbol.MetadataName)
                ? methodSymbol.Name
                : methodSymbol.MetadataName;
        var parameterList = string.Join(
            ", ",
            methodSymbol.Parameters.Select(parameter => parameter.Type.ToDisplayString()));
        if (string.IsNullOrWhiteSpace(containingType) || string.IsNullOrWhiteSpace(methodName))
            return methodSymbol.ToDisplayString();

        return containingType + "." + methodName + "(" + parameterList + ")";
    }

    private static (string? Category, string Source) SplitQualifiedSource(string qualifiedSource) {
        if (string.IsNullOrWhiteSpace(qualifiedSource)) return (null, string.Empty);

        var separatorIndex = qualifiedSource.IndexOf(':');
        if (separatorIndex <= 0) return (null, qualifiedSource);

        var category = qualifiedSource.Substring(0, separatorIndex);
        if (!IsKnownExceptionCategory(category)) return (null, qualifiedSource);

        return (category, qualifiedSource.Substring(separatorIndex + 1));
    }

    private static ImmutableArray<string> ParseCalleeChainFromSource(string source) =>
        string.IsNullOrWhiteSpace(source)
            ? ImmutableArray<string>.Empty
            : source
            .Split(new[] { " -> " }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeCalleeSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment) && LooksLikeSymbolSegment(segment))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();

    private static string NormalizeCalleeSegment(string segment) => SplitQualifiedSource(segment.Trim()).Source.Trim();

    private static bool LooksLikeSymbolSegment(string segment) =>
        segment == "lambda expression" || segment.Contains('.') || segment.Contains('(');

    private static ImmutableArray<ExceptionFlowEdge> CreateDerivedDiagnosticEdges(
        string exceptionType,
        string category,
        string sourcePath,
        ImmutableArray<string> calleeChain) {
        if (calleeChain.IsDefaultOrEmpty || string.IsNullOrWhiteSpace(sourcePath)) return [];
        return Enumerable.Range(1, Math.Max(1, calleeChain.Length - 1))
            .Select(depth => new ExceptionFlowEdge(
                exceptionType,
                category,
                null,
                calleeChain.ToArray(),
                null,
                depth))
            .ToImmutableArray();
    }

    private static bool IsKnownExceptionCategory(string category) =>
        SymbolicRuntimeExceptionFacts.IsKnownEvidenceCategory(category) ||
        SymbolicDynamicNullBindingFacts.IsDynamicNullBindingCategory(category);

    private static ITypeSymbol? TryResolveExceptionType(Compilation compilation, string displayName) =>
        displayName == ExceptionTypes.Unknown
            ? null
            : compilation.GetTypeByMetadataName(displayName);
}
