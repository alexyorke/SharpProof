using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicQueryContext
{
    public SymbolicQueryContext(
        SymbolicSourceInput source,
        SharpProofTarget target,
        SymbolicQueryOptions? options = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Options = options ?? SymbolicQueryOptions.Default;
    }

    public SymbolicSourceInput Source { get; }

    public SharpProofTarget Target { get; }

    public SymbolicQueryOptions Options { get; }
}

internal sealed class SymbolicQueryOptions
{
    public static readonly SymbolicQueryOptions Default = new();

    public SymbolicQueryOptions(
        IEnumerable<MetadataReference>? references = null,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicSourceQueryFilter? filter = null)
        : this(
            SymbolicAnalysisLimits.Default,
            references,
            smtAnalysis,
            impliedConditions,
            includeExpressionProgramPoints,
            includeCurrentStatementCompletionFacts,
            filter)
    {
    }

    private SymbolicQueryOptions(
        SymbolicAnalysisLimits analysisLimits,
        IEnumerable<MetadataReference>? references = null,
        SmtAnalysisService? smtAnalysis = null,
        IEnumerable<string>? impliedConditions = null,
        bool includeExpressionProgramPoints = false,
        bool includeCurrentStatementCompletionFacts = false,
        SymbolicSourceQueryFilter? filter = null)
    {
        AnalysisLimits = analysisLimits ?? throw new ArgumentNullException(nameof(analysisLimits));
        References = SymbolicQueryOptionHelpers.NormalizeReferences(references, nameof(references));
        SmtAnalysis = smtAnalysis;
        ImpliedConditions = impliedConditions?
            .Where(static condition => !string.IsNullOrWhiteSpace(condition))
            .Select(static condition => condition.Trim())
            .ToImmutableArray() ?? ImmutableArray<string>.Empty;
        IncludeExpressionProgramPoints = includeExpressionProgramPoints;
        IncludeCurrentStatementCompletionFacts = includeCurrentStatementCompletionFacts;
        Filter = filter;
    }

    public SymbolicAnalysisLimits AnalysisLimits { get; }

    public SymbolicQueryOptions WithAnalysisLimits(SymbolicAnalysisLimits analysisLimits)
    {
        return new SymbolicQueryOptions(
            analysisLimits,
            References,
            SmtAnalysis,
            ImpliedConditions,
            IncludeExpressionProgramPoints,
            IncludeCurrentStatementCompletionFacts,
            Filter);
    }

    public ImmutableArray<MetadataReference> References { get; }

    public SmtAnalysisService? SmtAnalysis { get; }

    public ImmutableArray<string> ImpliedConditions { get; }

    public bool IncludeExpressionProgramPoints { get; }

    public bool IncludeCurrentStatementCompletionFacts { get; }

    public SymbolicSourceQueryFilter? Filter { get; }
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
