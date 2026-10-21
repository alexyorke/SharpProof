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

    private const string UnknownHelp = " Help: https://github.com/alexyorke/SharpProof/blob/master/docs/unknown-reasons.md";

    internal static string UnknownGuidance(WorkerClaimReason reason)
    {
        return UnknownAdvice(reason) + UnknownHelp;
    }

    private static string UnknownAdvice(WorkerClaimReason reason)
    {
        return reason switch
        {
            WorkerClaimReason.UnsupportedCallable =>
                "Check this callable's shape against the coverage and limits guide. More time does not add " +
                "language support.",
            WorkerClaimReason.UnsupportedContract =>
                "Check contract placement, binding, and supported forms in the first-contract tutorial and " +
                "compiler diagnostics. More time does not repair an unsupported contract.",
            WorkerClaimReason.UnsupportedBody =>
                "Reduce the callable to identify unsupported control flow or operations; compare the coverage " +
                "and limits guide. More time does not add body support.",
            WorkerClaimReason.UnsupportedExpression =>
                "Inspect the claim and its referenced expressions against the coverage and limits guide. More " +
                "time does not add expression support.",
            WorkerClaimReason.DeepPostcondition =>
                "Simplify the postcondition's expression structure while preserving its meaning; inspect the " +
                "analysis limits guide for construction limits.",
            WorkerClaimReason.MissingReturnValue =>
                "Check normal-return paths and the supported return-value model. A postcondition cannot be " +
                "decided without an admitted result value.",
            WorkerClaimReason.ResourceLimit =>
                "Inspect the effective budgets in the result and the analysis limits guide. Reproduce with the " +
                "same inputs before raising only the exhausted limit.",
            WorkerClaimReason.MethodTimeout =>
                "Inspect the method budget in the result and the analysis limits guide. A larger method budget " +
                "may help; the project budget must also allow it.",
            WorkerClaimReason.ProjectTimeout =>
                "Inspect the project budget and selected callable coverage. Retry with a larger project budget " +
                "or a smaller selected scope; retain visibility of unchecked callables.",
            WorkerClaimReason.Canceled => "The run was interrupted. Rerun without cancellation; this result does not establish the claim.",
            WorkerClaimReason.BackendUnavailable =>
                "Inspect run errors, supported host/runtime requirements, and native solver availability. " +
                "Raising analysis budgets will not repair backend loading.",
            WorkerClaimReason.InfrastructureFailure =>
                "Inspect the run errors and host/runtime configuration. Preserve the request, result, and " +
                "compiler manifest for a reproducible report; review them for private source and paths before " +
                "sharing.",
            WorkerClaimReason.MalformedBackendResult =>
                "The backend evidence failed validation. Preserve a minimal reproduction and version information " +
                "for a bug report; do not accept or edit the result into a proof.",
            WorkerClaimReason.CounterexampleReplayFailed =>
                "The candidate failed replay validation and is not a confirmed violation. Inspect replay " +
                "evidence and report a minimal reproduction; do not treat the candidate as Refuted.",
            WorkerClaimReason.PostconditionMayBeUndefined =>
                "Check nullable dereferences, indexing, division, and other operations that can throw while " +
                "evaluating the postcondition. Make the expression defined without weakening the obligation.",
            WorkerClaimReason.CounterexampleNotReplayable =>
                "The candidate is not an executable counterexample. Inspect modeling limits and replay evidence; " +
                "preserve the Unknown outcome until supported evidence decides it.",
            WorkerClaimReason.EffectSummaryIncomplete =>
                "Inspect transitive calls and supported effect coverage. Partial may-effect information cannot " +
                "establish the contract; more time cannot supply missing semantic support.",
            WorkerClaimReason.EffectContractNotEstablished =>
                "Inspect the effect contract, transitive calls, and declared external evidence. Do not add trust " +
                "or assumptions merely to obtain a passing result.",
            WorkerClaimReason.SolverIncomplete =>
                "The solver could not decide the obligation. Inspect solver evidence and the analysis limits " +
                "guide; a larger timeout is not guaranteed to resolve incompleteness.",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Expected a typed Unknown reason.")
        };
    }

    internal static string IncompleteGuidance(WorkerCallableCoverageReason reason)
    {
        return IncompleteAdvice(reason) + UnknownHelp;
    }

    private static string IncompleteAdvice(WorkerCallableCoverageReason reason)
    {
        return reason switch
        {
            WorkerCallableCoverageReason.UnsupportedCallable => UnknownAdvice(WorkerClaimReason.UnsupportedCallable),
            WorkerCallableCoverageReason.UnsupportedContract => UnknownAdvice(WorkerClaimReason.UnsupportedContract),
            WorkerCallableCoverageReason.SemanticUnknown =>
                "Inspect this callable's individual claim reasons and the Unknown reason guide. Complete " +
                "execution does not mean all claims were proven.",
            WorkerCallableCoverageReason.MissingClaimResult =>
                "Expected claim evidence is missing. Inspect run errors and report a minimal reproduction; " +
                "absence of a result is not a proof.",
            WorkerCallableCoverageReason.MethodTimeout => UnknownAdvice(WorkerClaimReason.MethodTimeout),
            WorkerCallableCoverageReason.ProjectTimeout => UnknownAdvice(WorkerClaimReason.ProjectTimeout),
            WorkerCallableCoverageReason.Canceled => UnknownAdvice(WorkerClaimReason.Canceled),
            WorkerCallableCoverageReason.InfrastructureFailure => UnknownAdvice(WorkerClaimReason.InfrastructureFailure),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Expected an incomplete coverage reason.")
        };
    }

    internal static string SourcePrefix(WorkerSourceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return string.IsNullOrWhiteSpace(location.Path) ? "SharpProof" : location.Path +
            FormattableString.Invariant($"({location.Line},{location.Column})");
    }
}
