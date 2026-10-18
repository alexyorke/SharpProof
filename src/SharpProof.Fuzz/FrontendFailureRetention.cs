namespace SharpProof.Fuzz;

public sealed record RetainedFrontendFailure(
    GeneratedCSharpCase OriginalCase,
    GeneratedCSharpCase MinimizedCase,
    FrontendDifferentialResult OriginalResult,
    FrontendDifferentialResult MinimizedResult,
    FuzzFailure Failure)
{
    public GeneratedCSharpCase OriginalCase { get; init; } = OriginalCase ?? throw new System.ArgumentNullException(nameof(OriginalCase));
    public GeneratedCSharpCase MinimizedCase { get; init; } = MinimizedCase ?? throw new System.ArgumentNullException(nameof(MinimizedCase));
    public FrontendDifferentialResult OriginalResult { get; init; } = OriginalResult ?? throw new System.ArgumentNullException(nameof(OriginalResult));
    public FrontendDifferentialResult MinimizedResult { get; init; } = MinimizedResult ?? throw new System.ArgumentNullException(nameof(MinimizedResult));
    public FuzzFailure Failure { get; init; } = Failure ?? throw new System.ArgumentNullException(nameof(Failure));
}

public static class FrontendFailureRetention
{
    public static RetainedFrontendFailure Retain(
        GeneratedCSharpCase original,
        FrontendDifferentialResult originalResult,
        int caseIndex,
        int caseSeed,
        CancellationToken cancellationToken)
    {
        if (original == null)
        {
            throw new ArgumentNullException(nameof(original));
        }
        if (originalResult == null)
        {
            throw new ArgumentNullException(nameof(originalResult));
        }
        if (originalResult.Status == FuzzOracleStatus.Agreement)
        {
            throw new ArgumentException("Only a frontend failure can be retained.", nameof(originalResult));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var minimized = FuzzRunner.IsCompilationFailure(originalResult)
            ? original
            : CSharpStructuralShrinker.Minimize(
                original,
                candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (FuzzRunner.IsSemanticMismatch(
                    FrontendDifferentialOracle.Compare(candidate, cancellationToken))),
                cancellationToken);
        var minimizedResult = FrontendDifferentialOracle.Compare(
            minimized, cancellationToken);
        var failure = new FuzzFailure(
            caseIndex,
            caseSeed,
            "frontend",
            original.Source,
            minimized.Source,
            minimizedResult.Detail);
        return new(original, minimized, originalResult, minimizedResult, failure);
    }
}
