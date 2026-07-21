namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer {
    public static void AnalyzeSymbolForExceptions(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var mode = context.Configuration.RuntimeHazardMode;
        var reportSummaries = context.Configuration.ReportExceptions ||
                              (mode & RuntimeHazardMode.Summaries) != 0;
        var reportSites = context.Configuration.CheckedExceptions ||
                          (mode & RuntimeHazardMode.Sites) != 0;
        var reportUnknowns = (mode & RuntimeHazardMode.Unknowns) != 0;
        var contracts = CollectExceptionContracts(
            context.MethodSymbol,
            context.SemanticModel,
            attributePolicy,
            context.CancellationToken);
        if (!reportSummaries && !reportSites && !reportUnknowns && contracts.IsDefaultOrEmpty) return;

        var effects = context.State.GetMethodEffects(context.CancellationToken);
        var facts = ProjectEffectFacts(context, effects.ExceptionFacts).ToBuilder();
        if (reportUnknowns)
            foreach (var fact in facts.Where(static fact =>
                         fact.Source == MethodExceptionSource.RuntimeHazard &&
                         fact.Escape == SharpProofVerdict.Unknown))
                ReportUnknownHazard(context, fact, baseline);

        var proven = facts.Where(static fact => fact.Escape == SharpProofVerdict.Proven).ToImmutableArray();
        AnalyzeExceptionContracts(context, context.MethodSymbol, contracts, proven, baseline);
        if (reportSites) ReportSites(context, proven, baseline);
        if (reportSummaries) ReportSummary(context, proven, baseline);
    }

    private static ImmutableArray<ExceptionFactView> ProjectEffectFacts(
        MethodBodyAnalysisContext context,
        ImmutableArray<MethodExceptionFact> facts) => facts
        .Where(static fact => fact.Escape != SharpProofVerdict.Disproven)
        .Select(fact => new ExceptionFactView(
            FindSite(context.Node, fact.SpanStart, fact.SpanStart + fact.SpanLength),
            fact.ExceptionType,
            ResolveExceptionType(context.SemanticModel.Compilation, fact.ExceptionType),
            fact.Source,
            fact.Reason,
            fact.Escape,
            fact.IsTransitive,
            fact.Operation,
            fact.Kind))
        .ToImmutableArray();

    private static void ReportSites(
        MethodBodyAnalysisContext context,
        ImmutableArray<ExceptionFactView> facts,
        DiagnosticBaseline baseline) {
        foreach (var group in facts.GroupBy(static fact => fact.Site.Span)) {
            var first = group.First();
            var types = group.Select(static fact => fact.ExceptionType)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static type => type, StringComparer.Ordinal)
                .ToArray();
            var location = GetExceptionSiteLocation(first.Site);
            if (location == null) continue;
            var properties = CreateExceptionProperties(group);
            properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
                properties,
                context.MethodSymbol,
                context.Node.SyntaxTree,
                first.Reason,
                null,
                CreateExceptionEvidenceKey("site:" + first.Site.SpanStart, group),
                location,
                "runtime hazards",
                "hazard");
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(baseline, Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("UncaughtExceptionSiteRule"),
                location,
                null,
                properties,
                first.Site.ToString(),
                string.Join(", ", types)), context.ReportDiagnostic);
        }
    }

    private static void ReportSummary(
        MethodBodyAnalysisContext context,
        ImmutableArray<ExceptionFactView> facts,
        DiagnosticBaseline baseline) {
        if (facts.IsDefaultOrEmpty) return;
        var location = GetIdentifierLocation(context.Node);
        if (location == null) return;
        var types = facts.Select(static fact => fact.ExceptionType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static type => type, StringComparer.Ordinal)
            .ToArray();
        var properties = CreateExceptionProperties(facts);
        properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            context.MethodSymbol,
            context.Node.SyntaxTree,
            "ExceptionSummary",
            null,
            CreateExceptionEvidenceKey("summary", facts),
            location,
            "runtime hazards",
            "may_throw");
        AnalyzerDiagnosticReporter.ReportIfNotSuppressed(baseline, Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("ExceptionSummaryRule"),
            location,
            null,
            properties,
            context.MethodSymbol.Name,
            string.Join(", ", types)), context.ReportDiagnostic);
    }

    private static void ReportUnknownHazard(
        MethodBodyAnalysisContext context,
        ExceptionFactView hazard,
        DiagnosticBaseline baseline) {
        var site = hazard.Site;
        var location = GetExceptionSiteLocation(site);
        if (location == null) return;
        var properties = UnknownReasonDiagnosticProperties.Add(
            ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticPropertyNames.ExceptionTypesProperty, hazard.ExceptionType)
                .Add(DiagnosticPropertyNames.ExceptionCategoriesProperty, hazard.Reason)
                .Add("sharpproof.runtime_hazard.kind", hazard.Kind)
                .Add("sharpproof.runtime_hazard.status", hazard.Escape.ToString()),
            SymbolicUnknownReasonTaxonomy.ForRuntimeHazard(
                SymbolicRuntimeHazardStatus.Unknown,
                hazard.Reason,
                SymbolicUnknownReason.Unknown));
        properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            context.MethodSymbol,
            context.Node.SyntaxTree,
            ((SyntaxKind)site.RawKind).ToString(),
            null,
            $"hazard:{site.SpanStart}:{hazard.Reason}",
            location,
            "runtime hazard candidate",
            hazard.Escape.ToString(),
            "SP-RUNTIME-HAZARD-UNKNOWN");
        AnalyzerDiagnosticReporter.ReportIfNotSuppressed(baseline, Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("UnknownRuntimeHazardRule"),
            location,
            null,
            properties,
            hazard.Kind,
            hazard.SourceDetail,
            hazard.Reason), context.ReportDiagnostic);
    }

    private static ImmutableDictionary<string, string?> CreateExceptionProperties(
        IEnumerable<ExceptionFactView> facts) => ImmutableDictionary<string, string?>.Empty
        .Add(DiagnosticPropertyNames.ExceptionTypesProperty, string.Join(";", facts
            .Select(static fact => fact.ExceptionType).Distinct(StringComparer.Ordinal)))
        .Add(DiagnosticPropertyNames.ExceptionCategoriesProperty, string.Join(";", facts
            .Select(static fact => fact.Reason).Distinct(StringComparer.Ordinal)))
        .Add(DiagnosticPropertyNames.ExceptionSourcesProperty, string.Join(";", facts
            .GroupBy(static fact => fact.ExceptionType, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group
                .Select(static fact => fact.Reason + ":" + fact.SourceDetail)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .Select(value => group.Key + "=" + value))));

    private static string CreateExceptionEvidenceKey(string scope, IEnumerable<ExceptionFactView> facts) =>
        scope + "|" + string.Join(";", facts
            .Select(static fact => fact.ExceptionType + ":" + fact.Source + ":" + fact.Reason)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal));

    private static SyntaxNode FindSite(SyntaxNode method, int start, int end) =>
        method.DescendantNodesAndSelf().FirstOrDefault(node => node.SpanStart == start && node.Span.End == end) ??
        method.DescendantNodesAndSelf().Where(node => node.Span.Contains(start))
            .OrderBy(static node => node.Span.Length).FirstOrDefault() ?? method;

    private static ITypeSymbol? ResolveExceptionType(Compilation compilation, string name) =>
        compilation.GetTypeByMetadataName(name.Replace("global::", string.Empty));

    private static Location? GetIdentifierLocation(SyntaxNode node) => node switch {
        MethodDeclarationSyntax method => method.Identifier.GetLocation(),
        ConstructorDeclarationSyntax constructor => constructor.Identifier.GetLocation(),
        LocalFunctionStatementSyntax local => local.Identifier.GetLocation(),
        AccessorDeclarationSyntax accessor => accessor.Keyword.GetLocation(),
        _ => node.GetLocation()
    };

    private static Location? GetExceptionSiteLocation(SyntaxNode node) => node.GetLocation();

    internal readonly record struct ExceptionFactView(
        SyntaxNode Site,
        string ExceptionType,
        ITypeSymbol? Type,
        MethodExceptionSource Source,
        string Reason,
        SharpProofVerdict Escape,
        bool IsTransitive,
        string SourceDetail,
        string Kind);

}
