using System.Collections.Immutable;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

// Display projections consume validated evidence; they never alter its policy or outcome.
internal static partial class LauncherPresentation
{
    internal static string HumanSummary(WorkerVerifyResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var claims = response.ClaimResults;
        var assumptions = response.CallableResults.SelectMany(static result =>
                result is null ? throw new ArgumentNullException(nameof(result)) : result.Assumptions)
            .Where(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
                assumption.Kind is WorkerAssumptionKind.UserAssume or WorkerAssumptionKind.TrustedBoundary)
            .GroupBy(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
                (assumption.Kind, assumption.Id)).ToImmutableArray();
        var used = response.CallableResults.SelectMany(static result =>
                result is null ? throw new ArgumentNullException(nameof(result)) : result.Assumptions)
            .Concat(claims.SelectMany(static result => result is null ? throw new ArgumentNullException(nameof(result)) : result.Assumptions))
            .Where(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Used)
            .Select(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
                (assumption.Kind, assumption.Id)).ToImmutableHashSet();
        var proven = claims.Count(static result => result is null ? throw new ArgumentNullException(nameof(result)) :
            result.Outcome == WorkerClaimOutcome.Proven);
        var refuted = claims.Count(static result => result is null ? throw new ArgumentNullException(nameof(result)) :
            result.Outcome == WorkerClaimOutcome.Refuted);
        var unknown = claims.Count(static result => result is null ? throw new ArgumentNullException(nameof(result)) :
            result.Outcome == WorkerClaimOutcome.Unknown);
        var incomplete = response.CallableResults.Count(static result => result is null ? throw new ArgumentNullException(nameof(result)) :
            result.Coverage == WorkerCallableCoverage.Incomplete);
        var vacuous = claims.Count(static result => result is null ? throw new ArgumentNullException(nameof(result)) :
            result.Vacuity != WorkerVacuityKind.None);
        var usedCount = assumptions.Count(group => group is null ? throw new ArgumentNullException(nameof(group)) : used.Contains(group.Key));
        var selectedCount = response.Manifest.Callables.Length;
        var claimCount = response.Manifest.Claims.Length;
        return FormattableString.Invariant($"SharpProof results: run={response.RunStatus} ({response.FailureReason}); ") +
            FormattableString.Invariant($"selected callables={selectedCount}, claims={claimCount}; ") +
            FormattableString.Invariant($"Proven={proven}, Refuted={refuted}, Unknown={unknown}; ") +
            FormattableString.Invariant($"incomplete callables={incomplete}, vacuous claims={vacuous}; ") +
            FormattableString.Invariant($"declared user/trusted assumptions={assumptions.Length}, used={usedCount}; ") +
            FormattableString.Invariant($"elapsed={response.Summary.ElapsedMilliseconds} ms, cache={response.Summary.CacheStatus}.");
    }

    private const string UnknownHelp = " Help: " + ReasonExplanation.Documentation;

    internal static string UnknownGuidance(WorkerClaimReason reason)
    {
        return Advice(ReasonCatalog.Explain(reason));
    }

    internal static string IncompleteGuidance(WorkerCallableCoverageReason reason)
    {
        return Advice(ReasonCatalog.Explain(reason));
    }

    private static string Advice(ReasonExplanation explanation)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        return explanation.Description + " " + explanation.NextAction + UnknownHelp;
    }

    internal static string SourcePrefix(WorkerSourceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return string.IsNullOrWhiteSpace(location.Path) ? "SharpProof" : location.Path +
            FormattableString.Invariant($"({location.Line},{location.Column})");
    }
}
