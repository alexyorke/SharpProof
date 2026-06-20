using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Analyzer.Engine;

namespace PurelySharp.Analyzer
{
    internal static class ExceptionFlowQuery
    {
        private const string UnknownExceptionType = "unknown";

        private static readonly SymbolDisplayFormat ExceptionTypeDisplayFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        internal static MethodExceptionQueryResult AnalyzeMethod(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            IMethodSymbol methodSymbol,
            ExceptionSummaryCatalog exceptionSummaryCatalog)
        {
            var visitedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)
            {
                methodSymbol.OriginalDefinition
            };

            return AnalyzeMethod(
                methodNode,
                semanticModel,
                cancellationToken,
                methodSymbol,
                exceptionSummaryCatalog,
                visitedMethods);
        }

        private static MethodExceptionQueryResult AnalyzeMethod(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            IMethodSymbol methodSymbol,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            HashSet<IMethodSymbol> visitedMethods)
        {
            var siteEntries = CollectUncaughtExceptionSiteEntries(
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    methodSymbol,
                    exceptionSummaryCatalog,
                    visitedMethods)
                .ToImmutableArray();

            var exceptionEvidence = new ExceptionEvidenceSet();
            foreach (var siteEntry in siteEntries)
            {
                exceptionEvidence.Add(siteEntry.Exception);
            }

            return new MethodExceptionQueryResult(exceptionEvidence, siteEntries);
        }

        private static IEnumerable<UncaughtExceptionSiteEntry> CollectUncaughtExceptionSiteEntries(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            IMethodSymbol methodSymbol,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            HashSet<IMethodSymbol> visitedMethods)
        {
            foreach (var throwNode in ExceptionFlowAnalyzer.GetThrowNodes(methodNode))
            {
                if (IsInStaticallyUnreachableBranch(throwNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (ExceptionFlowAnalyzer.IsShadowedByDefinitelyThrowingFinally(throwNode))
                {
                    continue;
                }

                var exceptionType = ExceptionFlowAnalyzer.GetThrownExceptionType(throwNode, semanticModel, cancellationToken);
                if (IsCaughtWithinMethod(throwNode, exceptionType, methodNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                yield return new UncaughtExceptionSiteEntry(
                    throwNode,
                    methodSymbol,
                    new ExceptionCandidate(
                        exceptionType,
                        exceptionType?.ToDisplayString(ExceptionTypeDisplayFormat) ?? UnknownExceptionType,
                        IsRethrow(throwNode) ? "rethrow" : "direct_throw",
                        "throw"));
            }

            foreach (var calleeCallSite in ExceptionFlowAnalyzer.GetCalleeCallSites(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(calleeCallSite.CallSite, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (ExceptionFlowAnalyzer.IsShadowedByDefinitelyThrowingFinally(calleeCallSite.CallSite))
                {
                    continue;
                }

                var calleeDisplay = calleeCallSite.Method.OriginalDefinition.ToDisplayString();
                foreach (var exception in CollectCalleeExceptions(
                             calleeCallSite.Method,
                             semanticModel.Compilation,
                             cancellationToken,
                             exceptionSummaryCatalog,
                             visitedMethods))
                {
                    if (IsCaughtWithinMethod(calleeCallSite.CallSite, exception.Type, methodNode, semanticModel, cancellationToken))
                    {
                        continue;
                    }

                    yield return new UncaughtExceptionSiteEntry(calleeCallSite.CallSite, calleeCallSite.Method, exception, calleeDisplay);
                }
            }

            foreach (var divideByZeroNode in ExceptionFlowAnalyzer.GetDefiniteDivideByZeroNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(divideByZeroNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (ExceptionFlowAnalyzer.IsShadowedByDefinitelyThrowingFinally(divideByZeroNode))
                {
                    continue;
                }

                var exceptionType = semanticModel.Compilation.GetTypeByMetadataName("System.DivideByZeroException");
                if (IsCaughtWithinMethod(divideByZeroNode, exceptionType, methodNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                yield return new UncaughtExceptionSiteEntry(
                    divideByZeroNode,
                    methodSymbol,
                    new ExceptionCandidate(
                        exceptionType,
                        "System.DivideByZeroException",
                        "definite_divide_by_zero",
                        "binary_operator"));
            }

            foreach (var nullDereferenceNode in ExceptionFlowAnalyzer.GetDefiniteNullDereferenceNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(nullDereferenceNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (ExceptionFlowAnalyzer.IsShadowedByDefinitelyThrowingFinally(nullDereferenceNode))
                {
                    continue;
                }

                var exceptionType = semanticModel.Compilation.GetTypeByMetadataName("System.NullReferenceException");
                if (IsCaughtWithinMethod(nullDereferenceNode, exceptionType, methodNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                yield return new UncaughtExceptionSiteEntry(
                    nullDereferenceNode,
                    methodSymbol,
                    new ExceptionCandidate(
                        exceptionType,
                        "System.NullReferenceException",
                        "definite_null_dereference",
                        "null_receiver"));
            }
        }

        private static IEnumerable<ExceptionCandidate> CollectSourceCalleeExceptions(
            IMethodSymbol invokedMethod,
            Compilation compilation,
            System.Threading.CancellationToken cancellationToken,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            HashSet<IMethodSymbol> visitedMethods)
        {
            var originalDefinition = invokedMethod.OriginalDefinition;
            if (!visitedMethods.Add(originalDefinition))
            {
                return Enumerable.Empty<ExceptionCandidate>();
            }

            try
            {
                var syntaxReference = invokedMethod.DeclaringSyntaxReferences.FirstOrDefault()
                    ?? originalDefinition.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxReference == null)
                {
                    return Enumerable.Empty<ExceptionCandidate>();
                }

                var syntax = syntaxReference.GetSyntax(cancellationToken);
                var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
                var result = AnalyzeMethod(
                    syntax,
                    semanticModel,
                    cancellationToken,
                    invokedMethod,
                    exceptionSummaryCatalog,
                    visitedMethods);

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
                            "source_callee",
                            source,
                            CreateDerivedDiagnosticEdges(
                                entry.ExceptionType,
                                "source_callee",
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
            System.Threading.CancellationToken cancellationToken,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            HashSet<IMethodSymbol> visitedMethods)
        {
            foreach (var exception in CollectSourceCalleeExceptions(invokedMethod, compilation, cancellationToken, exceptionSummaryCatalog, visitedMethods))
            {
                yield return exception;
            }

            if (!exceptionSummaryCatalog.TryGetExceptionInfos(invokedMethod, compilation, out var summaryExceptions))
            {
                yield break;
            }

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
                            .Where(edge => string.Equals(edge.SourcePath, source, StringComparison.Ordinal))
                            .Select(edge => new ExceptionEdgeDiagnosticEntry(
                                summaryException.ExceptionType,
                                "effect_summary",
                                edge.SourcePath ?? source,
                                edge.CalleeExactSymbolKey,
                                edge.Depth ?? 0))
                            .ToImmutableArray();
                    var derivedEdges = CreateDerivedDiagnosticEdges(
                        summaryException.ExceptionType,
                        "effect_summary",
                        source,
                        CreateSummaryCalleeChain(source, fallbackSource));
                    matchingEdges = MergeDiagnosticEdges(matchingEdges, derivedEdges);

                    yield return new ExceptionCandidate(
                        TryResolveExceptionType(compilation, summaryException.ExceptionType),
                        summaryException.ExceptionType,
                        "effect_summary",
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
            {
                invokedChain = ImmutableArray.Create(invokedMethodDisplay);
            }

            if (nestedChain.IsDefaultOrEmpty)
            {
                return invokedChain;
            }

            if (!invokedChain.IsDefaultOrEmpty &&
                string.Equals(nestedChain[0], invokedChain[invokedChain.Length - 1], StringComparison.Ordinal))
            {
                var deduplicatedBuilder = ImmutableArray.CreateBuilder<string>(invokedChain.Length + nestedChain.Length - 1);
                deduplicatedBuilder.AddRange(invokedChain);
                for (var index = 1; index < nestedChain.Length; index++)
                {
                    deduplicatedBuilder.Add(nestedChain[index]);
                }

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
            {
                return methodSymbol.ToDisplayString();
            }

            var containingMethod = methodSymbol.ContainingSymbol as IMethodSymbol;
            var containingDisplay = containingMethod?.OriginalDefinition.ToDisplayString();
            var nestedDisplay = CreateNestedCallableDisplay(methodSymbol);
            if (string.IsNullOrWhiteSpace(containingDisplay) || string.IsNullOrWhiteSpace(nestedDisplay))
            {
                return methodSymbol.ToDisplayString();
            }

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
            {
                return methodSymbol.ToDisplayString();
            }

            return containingType + "." + methodName + "(" + parameterList + ")";
        }

        private static ImmutableArray<string> CreateSummaryCalleeChain(string source, string fallbackSource)
        {
            if (string.IsNullOrWhiteSpace(source) || string.Equals(source, fallbackSource, StringComparison.Ordinal))
            {
                return ImmutableArray<string>.Empty;
            }

            return ParseCalleeChainFromSource(source);
        }

        private static (string? Category, string Source) SplitQualifiedSource(string qualifiedSource)
        {
            if (string.IsNullOrWhiteSpace(qualifiedSource))
            {
                return (null, string.Empty);
            }

            var separatorIndex = qualifiedSource.IndexOf(':');
            if (separatorIndex <= 0)
            {
                return (null, qualifiedSource);
            }

            var category = qualifiedSource.Substring(0, separatorIndex);
            if (!IsKnownExceptionCategory(category))
            {
                return (null, qualifiedSource);
            }

            return (category, qualifiedSource.Substring(separatorIndex + 1));
        }

        private static ImmutableArray<string> ParseCalleeChainFromSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return ImmutableArray<string>.Empty;
            }

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
            {
                return ImmutableArray<ExceptionEdgeDiagnosticEntry>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<ExceptionEdgeDiagnosticEntry>();
            if (calleeChain.Length == 1)
            {
                builder.Add(new ExceptionEdgeDiagnosticEntry(
                    exceptionType,
                    category,
                    sourcePath,
                    calleeChain[0],
                    1));
                return builder.ToImmutable();
            }

            for (var index = 1; index < calleeChain.Length; index++)
            {
                var callee = calleeChain[index];
                if (string.IsNullOrWhiteSpace(callee))
                {
                    continue;
                }

                builder.Add(new ExceptionEdgeDiagnosticEntry(
                    exceptionType,
                    category,
                    sourcePath,
                    callee,
                    index));
            }

            return builder.ToImmutable();
        }

        private static ImmutableArray<ExceptionEdgeDiagnosticEntry> MergeDiagnosticEdges(
            ImmutableArray<ExceptionEdgeDiagnosticEntry> first,
            ImmutableArray<ExceptionEdgeDiagnosticEntry> second)
        {
            if (first.IsDefaultOrEmpty)
            {
                return second.IsDefault ? ImmutableArray<ExceptionEdgeDiagnosticEntry>.Empty : second;
            }

            if (second.IsDefaultOrEmpty)
            {
                return first;
            }

            var merged = new SortedDictionary<string, ExceptionEdgeDiagnosticEntry>(StringComparer.Ordinal);
            foreach (var edge in first)
            {
                merged[edge.CreateKey()] = edge;
            }

            foreach (var edge in second)
            {
                merged[edge.CreateKey()] = edge;
            }

            return merged.Values.ToImmutableArray();
        }

        private static bool IsKnownExceptionCategory(string category)
        {
            return string.Equals(category, "direct_throw", StringComparison.Ordinal) ||
                string.Equals(category, "rethrow", StringComparison.Ordinal) ||
                string.Equals(category, "source_callee", StringComparison.Ordinal) ||
                string.Equals(category, "effect_summary", StringComparison.Ordinal) ||
                string.Equals(category, "definite_divide_by_zero", StringComparison.Ordinal) ||
                string.Equals(category, "definite_null_dereference", StringComparison.Ordinal);
        }

        private static ITypeSymbol? TryResolveExceptionType(Compilation compilation, string displayName)
        {
            return displayName == UnknownExceptionType
                ? null
                : compilation.GetTypeByMetadataName(displayName);
        }

        private static bool IsCaughtWithinMethod(
            SyntaxNode throwNode,
            ITypeSymbol? exceptionType,
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var tryStatement in throwNode.Ancestors().OfType<TryStatementSyntax>())
            {
                if (!tryStatement.Span.Contains(throwNode.SpanStart))
                {
                    continue;
                }

                if (!tryStatement.Block.Span.Contains(throwNode.SpanStart))
                {
                    continue;
                }

                if (tryStatement.Catches.Any(catchClause => CatchesException(catchClause, exceptionType, semanticModel, cancellationToken)))
                {
                    return true;
                }

                if (ReferenceEquals(tryStatement, methodNode))
                {
                    break;
                }
            }

            return false;
        }

        private static bool IsInStaticallyUnreachableBranch(
            SyntaxNode node,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return ExecutionVisibility.IsInStaticallyUnreachableBranch(node, semanticModel, cancellationToken);
        }

        private static bool CatchesException(
            CatchClauseSyntax catchClause,
            ITypeSymbol? exceptionType,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (catchClause.Filter != null)
            {
                if (ExecutionVisibility.IsConditionAlwaysFalse(catchClause.Filter.FilterExpression, semanticModel, cancellationToken))
                {
                    return false;
                }

                if (!ExecutionVisibility.IsConditionAlwaysTrue(catchClause.Filter.FilterExpression, semanticModel, cancellationToken))
                {
                    return false;
                }
            }

            if (catchClause.Declaration == null)
            {
                return true;
            }

            if (exceptionType == null)
            {
                return false;
            }

            var catchType = semanticModel.GetTypeInfo(catchClause.Declaration.Type, cancellationToken).Type;
            return catchType != null && IsSameOrDerivedFrom(exceptionType, catchType);
        }

        private static bool IsSameOrDerivedFrom(ITypeSymbol exceptionType, ITypeSymbol catchType)
        {
            for (var current = exceptionType; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, catchType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRethrow(SyntaxNode throwNode)
        {
            return throwNode is ThrowStatementSyntax statement && statement.Expression == null;
        }

        internal sealed class MethodExceptionQueryResult
        {
            public MethodExceptionQueryResult(
                ExceptionEvidenceSet exceptionEvidence,
                ImmutableArray<UncaughtExceptionSiteEntry> siteEntries)
            {
                ExceptionEvidence = exceptionEvidence;
                SiteEntries = siteEntries;
            }

            public ExceptionEvidenceSet ExceptionEvidence { get; }

            public ImmutableArray<UncaughtExceptionSiteEntry> SiteEntries { get; }
        }

        internal sealed class ExceptionCandidate
        {
            public ExceptionCandidate(
                ITypeSymbol? type,
                string displayName,
                string category,
                string source,
                ImmutableArray<ExceptionEdgeDiagnosticEntry> edges = default)
            {
                Type = type;
                DisplayName = displayName;
                Category = category;
                Source = source;
                Edges = edges.IsDefault ? ImmutableArray<ExceptionEdgeDiagnosticEntry>.Empty : edges;
            }

            public ITypeSymbol? Type { get; }

            public string DisplayName { get; }

            public string Category { get; }

            public string Source { get; }

            public ImmutableArray<ExceptionEdgeDiagnosticEntry> Edges { get; }
        }

        internal sealed class UncaughtExceptionSiteEntry
        {
            public UncaughtExceptionSiteEntry(
                SyntaxNode site,
                IMethodSymbol method,
                ExceptionCandidate exception,
                string? exceptionSymbol = null)
            {
                Site = site;
                Method = method;
                Exception = exception;
                ExceptionSymbol = exceptionSymbol;
            }

            public SyntaxNode Site { get; }

            public IMethodSymbol Method { get; }

            public ExceptionCandidate Exception { get; }

            public string? ExceptionSymbol { get; }
        }

        internal sealed class ExceptionEvidenceEntry
        {
            public ExceptionEvidenceEntry(
                string exceptionType,
                string[] categories,
                string[] sources,
                ExceptionEdgeDiagnosticEntry[] edges)
            {
                ExceptionType = exceptionType;
                Categories = categories;
                Sources = sources;
                Edges = edges;
            }

            public string ExceptionType { get; }

            public string[] Categories { get; }

            public string[] Sources { get; }

            public ExceptionEdgeDiagnosticEntry[] Edges { get; }
        }

        internal sealed class ExceptionEvidenceSet
        {
            private readonly Dictionary<string, SortedSet<string>> _categoriesByType =
                new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            private readonly Dictionary<string, SortedSet<string>> _sourcesByType =
                new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            private readonly Dictionary<string, SortedDictionary<string, ExceptionEdgeDiagnosticEntry>> _edgesByType =
                new Dictionary<string, SortedDictionary<string, ExceptionEdgeDiagnosticEntry>>(StringComparer.Ordinal);

            public int Count => _categoriesByType.Count;

            public string[] Types => _categoriesByType.Keys.OrderBy(type => type, StringComparer.Ordinal).ToArray();

            public void Add(ExceptionCandidate candidate)
            {
                var exceptionType = candidate.DisplayName;
                var category = candidate.Category;
                var source = candidate.Source;
                if (!_categoriesByType.TryGetValue(exceptionType, out var categories))
                {
                    categories = new SortedSet<string>(StringComparer.Ordinal);
                    _categoriesByType.Add(exceptionType, categories);
                }

                categories.Add(category);

                if (!_sourcesByType.TryGetValue(exceptionType, out var sources))
                {
                    sources = new SortedSet<string>(StringComparer.Ordinal);
                    _sourcesByType.Add(exceptionType, sources);
                }

                sources.Add(category + ":" + source);

                if (!candidate.Edges.IsDefaultOrEmpty)
                {
                    if (!_edgesByType.TryGetValue(exceptionType, out var edges))
                    {
                        edges = new SortedDictionary<string, ExceptionEdgeDiagnosticEntry>(StringComparer.Ordinal);
                        _edgesByType.Add(exceptionType, edges);
                    }

                    foreach (var edge in candidate.Edges)
                    {
                        edges[edge.CreateKey()] = edge;
                    }
                }
            }

            public ExceptionEvidenceEntry[] EnumerateEntries()
            {
                return _categoriesByType.Keys
                    .OrderBy(type => type, StringComparer.Ordinal)
                    .Select(type => new ExceptionEvidenceEntry(
                        type,
                        _categoriesByType.TryGetValue(type, out var categories)
                            ? categories.ToArray()
                            : Array.Empty<string>(),
                        _sourcesByType.TryGetValue(type, out var sources)
                            ? sources.ToArray()
                            : Array.Empty<string>(),
                        _edgesByType.TryGetValue(type, out var edges)
                            ? edges.Values.ToArray()
                            : Array.Empty<ExceptionEdgeDiagnosticEntry>()))
                    .ToArray();
            }

            public string FormatCategories()
            {
                return string.Join(
                    ";",
                    _categoriesByType.Values
                        .SelectMany(categories => categories)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(category => category, StringComparer.Ordinal));
            }

            public string FormatSources()
            {
                return string.Join(
                    ";",
                    _sourcesByType
                        .OrderBy(item => item.Key, StringComparer.Ordinal)
                        .SelectMany(item => item.Value.Select(source => item.Key + "=" + source)));
            }

            public string? FormatEdges()
            {
                var edges = _edgesByType
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .SelectMany(item => item.Value.Values)
                    .ToArray();
                if (edges.Length == 0)
                {
                    return null;
                }

                return JsonSerializer.Serialize(edges);
            }
        }

        internal sealed class ExceptionEdgeDiagnosticEntry
        {
            public ExceptionEdgeDiagnosticEntry(
                string exceptionType,
                string category,
                string sourcePath,
                string? calleeExactSymbolKey,
                int depth)
            {
                ExceptionType = exceptionType;
                Category = category;
                SourcePath = sourcePath;
                CalleeExactSymbolKey = calleeExactSymbolKey;
                Depth = depth;
            }

            public string ExceptionType { get; }

            public string Category { get; }

            public string SourcePath { get; }

            public string? CalleeExactSymbolKey { get; }

            public int Depth { get; }

            public string CreateKey()
            {
                return ExceptionType + "|" +
                    Category + "|" +
                    SourcePath + "|" +
                    (CalleeExactSymbolKey ?? string.Empty) + "|" +
                    Depth.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
