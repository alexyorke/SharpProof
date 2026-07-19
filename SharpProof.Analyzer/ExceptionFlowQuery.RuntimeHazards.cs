namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowEngine
{
    private readonly record struct ProvenRuntimeHazardSite(
        SymbolicRuntimeHazard Hazard,
        SyntaxNode Site,
        string Category,
        string Source,
        int FlowOrder,
        int InputOrder,
        bool BeforeCallees);

    private sealed record HazardProjection(
        string Source,
        int FlowOrder,
        string? Category = null,
        bool BeforeCallees = false);

    internal static ExceptionFlowResult AnalyzeHazards(
        SymbolicMethodAnalysisInput input,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis) =>
        new(ImmutableArray<ExceptionFlowSite>.Empty,
            QueryRuntimeHazards(input.Declaration, input.SemanticModel, cancellationToken, smtAnalysis));

    private static ImmutableArray<SymbolicRuntimeHazard> QueryRuntimeHazards(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis) =>
        new SymbolicRuntimeHazardQueryService().QueryNodeRuntimeHazards(
                methodNode,
                semanticModel,
                smtAnalysis,
                cancellationToken,
                new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true))
            .Hazards
            .ToImmutableArray();

    private static ImmutableArray<ProvenRuntimeHazardSite> ProjectProvenRuntimeHazardSites(
        SyntaxNode methodNode,
        IEnumerable<SymbolicRuntimeHazard> hazards) =>
        hazards.Select((hazard, index) => TryProjectHazard(methodNode, hazard, index))
            .Where(static site => site.HasValue)
            .Select(static site => site!.Value)
            .OrderBy(static site => site.FlowOrder)
            .ThenBy(static site => site.InputOrder)
            .ToImmutableArray();

    private static ProvenRuntimeHazardSite? TryProjectHazard(
        SyntaxNode methodNode,
        SymbolicRuntimeHazard hazard,
        int inputOrder)
    {
        if (hazard.Status != SymbolicRuntimeHazardStatus.Proven) return null;
        var site = ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard);
        var projection = MapHazard(hazard, site);
        return projection == null
            ? null
            : new ProvenRuntimeHazardSite(
                hazard,
                site,
                projection.Category ?? hazard.Category,
                projection.Source,
                projection.FlowOrder,
                inputOrder,
                projection.BeforeCallees);
    }

    private static HazardProjection? MapHazard(SymbolicRuntimeHazard hazard, SyntaxNode site) => hazard.Kind switch
    {
        SymbolicRuntimeHazardKind.DirectThrow or SymbolicRuntimeHazardKind.Rethrow =>
            new(ExceptionSources.Throw, 0, BeforeCallees: true),
        SymbolicRuntimeHazardKind.DivideByZero =>
            new(ExceptionSources.BinaryOperator, 10, ExceptionCategories.DefiniteDivideByZero),
        SymbolicRuntimeHazardKind.CheckedIntegralOverflow => new(
            site is CastExpressionSyntax ? ExceptionSources.CheckedConversion : ExceptionSources.CheckedOperator,
            20,
            ExceptionCategories.DefiniteCheckedIntegralOverflow),
        SymbolicRuntimeHazardKind.NegativeArrayLength => new(ExceptionSources.ArrayLength, 30),
        SymbolicRuntimeHazardKind.NegativeStackAllocLength => new(ExceptionSources.StackAllocLength, 40),
        SymbolicRuntimeHazardKind.NullDereference when IsAnalyzerCategory(hazard.Category) =>
            new(AnalyzerSource(hazard.Category), 180),
        SymbolicRuntimeHazardKind.NullDereference when site is
            MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or
            InvocationExpressionSyntax or AwaitExpressionSyntax => new(
                site is AwaitExpressionSyntax ? ExceptionSources.AwaitExpression : ExceptionSources.NullReceiver,
                50,
                site is AwaitExpressionSyntax
                    ? ExceptionCategories.DefiniteAwaitNull
                    : ExceptionCategories.DefiniteNullDereference),
        SymbolicRuntimeHazardKind.ArgumentNull
            when hazard.Category == ExceptionCategories.DefiniteLockNull => new(ExceptionSources.LockReceiver, 60),
        SymbolicRuntimeHazardKind.DynamicNullBinding => new(DynamicSource(hazard.Category), 70),
        SymbolicRuntimeHazardKind.NullableValueWithoutValue => new(ExceptionSources.NullableValue, 80),
        SymbolicRuntimeHazardKind.UnboxNull => new(ExceptionSources.Cast, 90),
        SymbolicRuntimeHazardKind.InvalidCast => new(ExceptionSources.Cast, 100),
        SymbolicRuntimeHazardKind.ArrayTypeMismatch => new(ExceptionSources.ArrayStore, 110),
        SymbolicRuntimeHazardKind.IndexOutOfRange
            when hazard.Category == ExceptionCategories.DefiniteIndexOutOfRange => new(ExceptionSources.ArrayIndex, 120),
        SymbolicRuntimeHazardKind.IndexOutOfRange
            when hazard.Category == ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange =>
            new(ExceptionSources.ArrayGetValue, 130),
        SymbolicRuntimeHazardKind.ArgumentOutOfRange when
            hazard.Category == ExceptionCategories.DefiniteRangeOutOfRange ||
            hazard.Category == ExceptionCategories.DefiniteSliceOutOfRange => new(
                site is InvocationExpressionSyntax ? ExceptionSources.SpanSlice : ExceptionSources.RangeSlice,
                140,
                ExceptionCategories.DefiniteRangeOutOfRange),
        SymbolicRuntimeHazardKind.ArgumentOutOfRange
            when hazard.Category == ExceptionCategories.DefiniteCountIndexOutOfRange => new(ExceptionSources.CountIndex, 150),
        SymbolicRuntimeHazardKind.SwitchExpressionNoMatch => new(ExceptionSources.SwitchExpression, 160),
        SymbolicRuntimeHazardKind.InvalidCollectionCardinality => new(ExceptionSources.CollectionOperation, 170),
        SymbolicRuntimeHazardKind.IndexOutOfRange when IsAnalyzerCategory(hazard.Category) =>
            new(AnalyzerSource(hazard.Category), 180),
        _ => null
    };

    private static bool IsAnalyzerCategory(string category) =>
        category == ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange ||
        category == ExceptionCategories.DefiniteWithNull ||
        category == ExceptionCategories.DefiniteDeconstructionNull;

    private static string DynamicSource(string category) =>
        category == SymbolicDynamicNullBindingFacts.MemberCategory
            ? SymbolicDynamicNullBindingFacts.MemberSource
            : category == SymbolicDynamicNullBindingFacts.IndexCategory
                ? SymbolicDynamicNullBindingFacts.IndexSource
                : SymbolicDynamicNullBindingFacts.InvocationSource;

    private static string AnalyzerSource(string category) =>
        category == ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange
            ? ExceptionSources.ArrayGetValue
            : category == ExceptionCategories.DefiniteWithNull
                ? ExceptionSources.WithExpression
                : category == ExceptionCategories.DefiniteDeconstructionNull
                    ? ExceptionSources.DeconstructionReceiver
                    : ExceptionSources.NullReceiver;
}
