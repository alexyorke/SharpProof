namespace SharpProof.Symbolic;

internal sealed class SymbolicQueryContext(
    SymbolicSourceInput source,
    SharpProofTarget target,
    SymbolicQueryOptions? options = null)
{
    public SymbolicSourceInput Source { get; } = source ?? throw new ArgumentNullException(nameof(source));
    public SharpProofTarget Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
    public SymbolicQueryOptions Options { get; } = options ?? SymbolicQueryOptions.Default;
}

internal sealed class SymbolicQueryOptions(
    SharpProofAnalysisBudget analysisLimits,
    IEnumerable<MetadataReference>? references = null,
    SmtAnalysisService? smtAnalysis = null,
    IEnumerable<string>? impliedConditions = null,
    bool includeExpressionProgramPoints = false,
    bool includeCurrentStatementCompletionFacts = false,
    SymbolicSourceQueryFilter? filter = null)
{
    public static readonly SymbolicQueryOptions Default = new(SharpProofAnalysisBudget.Default);

    public SymbolicQueryOptions(
        IEnumerable<MetadataReference>? references = null,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicSourceQueryFilter? filter = null)
        : this(
            SharpProofAnalysisBudget.Default,
            references,
            smtAnalysis,
            impliedConditions,
            includeExpressionProgramPoints,
            includeCurrentStatementCompletionFacts,
            filter)
    {
    }

    public SharpProofAnalysisBudget AnalysisLimits { get; } =
        analysisLimits ?? throw new ArgumentNullException(nameof(analysisLimits));

    public SymbolicQueryOptions WithAnalysisLimits(SharpProofAnalysisBudget analysisLimits) => new(
        analysisLimits, References, SmtAnalysis, ImpliedConditions,
        IncludeExpressionProgramPoints, IncludeCurrentStatementCompletionFacts, Filter);

    public ImmutableArray<MetadataReference> References { get; } =
        SymbolicQueryOptionHelpers.NormalizeReferences(references, nameof(references));
    public SmtAnalysisService? SmtAnalysis { get; } = smtAnalysis;
    public ImmutableArray<string> ImpliedConditions { get; } = impliedConditions?
        .Where(static condition => !string.IsNullOrWhiteSpace(condition))
        .Select(static condition => condition.Trim())
        .ToImmutableArray() ?? ImmutableArray<string>.Empty;
    public bool IncludeExpressionProgramPoints { get; } = includeExpressionProgramPoints;
    public bool IncludeCurrentStatementCompletionFacts { get; } = includeCurrentStatementCompletionFacts;
    public SymbolicSourceQueryFilter? Filter { get; } = filter;
}

internal static class SymbolicQueryOptionHelpers
{
    public static ImmutableArray<MetadataReference> NormalizeReferences(
        IEnumerable<MetadataReference>? references,
        string parameterName)
    {
        if (references == null) return ImmutableArray<MetadataReference>.Empty;

        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var reference in references)
        {
            if (reference == null)
                throw new ArgumentException("References cannot contain null entries.", parameterName);

            builder.Add(reference);
        }

        return builder.ToImmutable();
    }
}
