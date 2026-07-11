using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowQuery
{
    private static IEnumerable<ExceptionCandidate> CollectSourceCalleeExceptions(
        IMethodSymbol invokedMethod,
        Compilation compilation,
        CancellationToken cancellationToken,
        ExceptionSummaryCatalog exceptionSummaryCatalog,
        HashSet<IMethodSymbol> visitedMethods,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var originalDefinition = invokedMethod.OriginalDefinition;
        if (!visitedMethods.Add(originalDefinition)) return Enumerable.Empty<ExceptionCandidate>();

        try
        {
            var syntaxReference = invokedMethod.DeclaringSyntaxReferences.FirstOrDefault()
                                  ?? originalDefinition.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference == null) return Enumerable.Empty<ExceptionCandidate>();

            var syntax = syntaxReference.GetSyntax(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
            var result = AnalyzeMethod(
                syntax,
                semanticModel,
                cancellationToken,
                invokedMethod,
                exceptionSummaryCatalog,
                visitedMethods,
                smtAnalysis,
                attributePolicy);

            var invokedMethodDisplay = GetExceptionSourceMethodDisplay(invokedMethod.OriginalDefinition);
            return result.ExceptionEvidence.EnumerateEntries()
                .SelectMany(entry =>
                {
                    var chainedSources = entry.Sources.Length == 0
                        ? new[] { invokedMethodDisplay }
                        : entry.Sources.Select(source => invokedMethodDisplay + " -> " + source);
                    return chainedSources.Select(source => new ExceptionCandidate(
                        TryResolveExceptionType(compilation, entry.ExceptionType),
                        entry.ExceptionType,
                        ExceptionCategories.SourceCallee,
                        source,
                        CreateDerivedDiagnosticEdges(
                            entry.ExceptionType,
                            ExceptionCategories.SourceCallee,
                            source,
                            CreatePrefixedCalleeChain(invokedMethodDisplay, source))));
                })
                .ToArray();
        }
        finally
        {
            visitedMethods.Remove(originalDefinition);
        }
    }

    private static IEnumerable<ExceptionCandidate> CollectCalleeExceptions(
        IMethodSymbol invokedMethod,
        Compilation compilation,
        CancellationToken cancellationToken,
        ExceptionSummaryCatalog exceptionSummaryCatalog,
        HashSet<IMethodSymbol> visitedMethods,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        foreach (var exception in CollectSourceCalleeExceptions(invokedMethod, compilation, cancellationToken,
                     exceptionSummaryCatalog, visitedMethods, smtAnalysis, attributePolicy)) yield return exception;

        if (!exceptionSummaryCatalog.TryGetExceptionInfos(invokedMethod, compilation, out var summaryExceptions))
            yield break;

        var fallbackSource = invokedMethod.OriginalDefinition.ToDisplayString();
        foreach (var summaryException in summaryExceptions)
        {
            var sources = summaryException.Sources.IsDefaultOrEmpty
                ? ImmutableArray.Create(fallbackSource)
                : summaryException.Sources;
            foreach (var source in sources)
            {
                var matchingEdges = summaryException.Edges.IsDefaultOrEmpty
                    ? ImmutableArray<ExceptionEdgeDiagnosticEntry>.Empty
                    : summaryException.Edges
                        .Where(edge => edge.SourcePath == null ||
                                       string.Equals(edge.SourcePath, source, StringComparison.Ordinal))
                        .Select(edge => new ExceptionEdgeDiagnosticEntry(
                            summaryException.ExceptionType,
                            ExceptionCategories.EffectSummary,
                            edge.SourcePath,
                            edge.CallChain.Select(static identity => identity.ToCanonicalKey()),
                            edge.CalleeIdentity?.ToCanonicalKey(),
                            edge.Depth ?? 0))
                        .ToImmutableArray();

                yield return new ExceptionCandidate(
                    TryResolveExceptionType(compilation, summaryException.ExceptionType),
                    summaryException.ExceptionType,
                    ExceptionCategories.EffectSummary,
                    source,
                    matchingEdges);
            }
        }
    }

    private static ImmutableArray<string> CreatePrefixedCalleeChain(string invokedMethodDisplay, string qualifiedSource)
    {
        var (_, source) = SplitQualifiedSource(qualifiedSource);
        var nestedChain = ParseCalleeChainFromSource(source);
        var invokedChain = ParseCalleeChainFromSource(invokedMethodDisplay);
        if (invokedChain.IsDefaultOrEmpty && !string.IsNullOrWhiteSpace(invokedMethodDisplay))
            invokedChain = ImmutableArray.Create(invokedMethodDisplay);

        if (nestedChain.IsDefaultOrEmpty) return invokedChain;

        if (!invokedChain.IsDefaultOrEmpty &&
            string.Equals(nestedChain[0], invokedChain[invokedChain.Length - 1], StringComparison.Ordinal))
        {
            var deduplicatedBuilder =
                ImmutableArray.CreateBuilder<string>(invokedChain.Length + nestedChain.Length - 1);
            deduplicatedBuilder.AddRange(invokedChain);
            for (var index = 1; index < nestedChain.Length; index++) deduplicatedBuilder.Add(nestedChain[index]);

            return deduplicatedBuilder.ToImmutable();
        }

        var builder = ImmutableArray.CreateBuilder<string>(invokedChain.Length + nestedChain.Length);
        builder.AddRange(invokedChain);
        builder.AddRange(nestedChain);
        return builder.ToImmutable();
    }

    private static string GetExceptionSourceMethodDisplay(IMethodSymbol methodSymbol)
    {
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

    private static string CreateNestedCallableDisplay(IMethodSymbol methodSymbol)
    {
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

    private static ImmutableArray<string> CreateSummaryCalleeChain(string source, string fallbackSource)
    {
        if (string.IsNullOrWhiteSpace(source) || string.Equals(source, fallbackSource, StringComparison.Ordinal))
            return ImmutableArray<string>.Empty;

        return ParseCalleeChainFromSource(source);
    }

    private static (string? Category, string Source) SplitQualifiedSource(string qualifiedSource)
    {
        if (string.IsNullOrWhiteSpace(qualifiedSource)) return (null, string.Empty);

        var separatorIndex = qualifiedSource.IndexOf(':');
        if (separatorIndex <= 0) return (null, qualifiedSource);

        var category = qualifiedSource.Substring(0, separatorIndex);
        if (!IsKnownExceptionCategory(category)) return (null, qualifiedSource);

        return (category, qualifiedSource.Substring(separatorIndex + 1));
    }

    private static ImmutableArray<string> ParseCalleeChainFromSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return ImmutableArray<string>.Empty;

        var segments = source
            .Split(new[] { " -> " }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeCalleeSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment) && LooksLikeSymbolSegment(segment))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        return segments;
    }

    private static string NormalizeCalleeSegment(string segment)
    {
        var trimmed = segment.Trim();
        var (_, source) = SplitQualifiedSource(trimmed);
        return source.Trim();
    }

    private static bool LooksLikeSymbolSegment(string segment)
    {
        return segment.Contains(".", StringComparison.Ordinal) ||
               segment.Contains("(", StringComparison.Ordinal);
    }

    private static ImmutableArray<ExceptionEdgeDiagnosticEntry> CreateDerivedDiagnosticEdges(
        string exceptionType,
        string category,
        string sourcePath,
        ImmutableArray<string> calleeChain)
    {
        if (calleeChain.IsDefaultOrEmpty || string.IsNullOrWhiteSpace(sourcePath))
            return ImmutableArray<ExceptionEdgeDiagnosticEntry>.Empty;

        var builder = ImmutableArray.CreateBuilder<ExceptionEdgeDiagnosticEntry>();
        if (calleeChain.Length == 1)
        {
            builder.Add(new ExceptionEdgeDiagnosticEntry(
                exceptionType,
                category,
                null,
                calleeChain,
                null,
                1));
            return builder.ToImmutable();
        }

        for (var index = 1; index < calleeChain.Length; index++)
        {
            var callee = calleeChain[index];
            if (string.IsNullOrWhiteSpace(callee)) continue;

            builder.Add(new ExceptionEdgeDiagnosticEntry(
                exceptionType,
                category,
                null,
                calleeChain,
                null,
                index));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<ExceptionEdgeDiagnosticEntry> MergeDiagnosticEdges(
        ImmutableArray<ExceptionEdgeDiagnosticEntry> first,
        ImmutableArray<ExceptionEdgeDiagnosticEntry> second)
    {
        if (first.IsDefaultOrEmpty)
            return second.IsDefault ? ImmutableArray<ExceptionEdgeDiagnosticEntry>.Empty : second;

        if (second.IsDefaultOrEmpty) return first;

        var merged = new SortedDictionary<string, ExceptionEdgeDiagnosticEntry>(StringComparer.Ordinal);
        foreach (var edge in first) merged[edge.CreateKey()] = edge;

        foreach (var edge in second) merged[edge.CreateKey()] = edge;

        return merged.Values.ToImmutableArray();
    }

    private static bool IsKnownExceptionCategory(string category)
    {
        return SymbolicRuntimeExceptionFacts.IsKnownEvidenceCategory(category) ||
               SymbolicDynamicNullBindingFacts.IsDynamicNullBindingCategory(category);
    }

    private static ITypeSymbol? TryResolveExceptionType(Compilation compilation, string displayName)
    {
        return displayName == ExceptionTypes.Unknown
            ? null
            : compilation.GetTypeByMetadataName(displayName);
    }
}
