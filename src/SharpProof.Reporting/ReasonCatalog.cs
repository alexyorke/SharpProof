using SharpProof.Worker.Protocol;

namespace SharpProof.Reporting;

internal sealed class ReasonExplanation
{
    internal ReasonExplanation(string category, string description, string nextAction)
    {
        global::SharpProof.Internal.InputValidation.Require(category, nameof(category));
        global::SharpProof.Internal.InputValidation.Require(description, nameof(description));
        global::SharpProof.Internal.InputValidation.Require(nextAction, nameof(nextAction));
        Category = category;
        Description = description;
        NextAction = nextAction;
    }

    internal string Category { get; }
    internal string Description { get; }
    internal string NextAction { get; }

    internal const string Documentation = "https://github.com/alexyorke/SharpProof/blob/master/docs/unknown-reasons.md";
}

internal static class ReasonCatalog
{
    internal static ReasonExplanation Explain(WorkerClaimReason reason)
    {
        return reason switch
        {
            WorkerClaimReason.UnsupportedCallable => Model("This callable's shape is outside the verifier's supported subset."),
            WorkerClaimReason.UnsupportedContract => Model("The contract could not be bound or modeled."),
            WorkerClaimReason.UnsupportedBody => Model("The method body contains control flow or operations the verifier cannot model."),
            WorkerClaimReason.UnsupportedExpression => Model("An expression has no supported interpretation."),
            WorkerClaimReason.DeepPostcondition => new("Model limit", "The postcondition exceeds the construction-depth limit.", "Simplify the postcondition or inspect SharpProofVerifyMaximumExpressionDepth; a larger solver timeout will not change expression admission."),
            WorkerClaimReason.MissingReturnValue => Model("The verifier could not supply a required normal return value."),
            WorkerClaimReason.ResourceLimit => new("Resource limit", "The bounded analysis exhausted its resource allowance.", "Inspect query and method resource usage before adjusting SharpProofVerifyQueryRlimit or SharpProofVerifyMethodRlimit."),
            WorkerClaimReason.MethodTimeout => new("Resource limit", "This method reached its wall-clock limit.", "Reproduce with the same inputs, then inspect SharpProofVerifyMethodWallTimeMilliseconds and container contention."),
            WorkerClaimReason.ProjectTimeout => new("Resource limit", "The project reached its wall-clock limit.", "Inspect the slow methods and SharpProofVerifyProjectWallTimeMilliseconds; retain the incomplete coverage report."),
            WorkerClaimReason.Canceled => new("Cancellation", "The verification was canceled.", "Run verification again when the build can complete."),
            WorkerClaimReason.BackendUnavailable => Infrastructure("The native solver backend was unavailable.", "Run sharpproof doctor --verification container and check the canonical image and matching Verifier package."),
            WorkerClaimReason.InfrastructureFailure => Infrastructure("Verification failed outside the modeled program.", "Inspect the run errors and retained request, result, and compiler manifest before retrying."),
            WorkerClaimReason.MalformedBackendResult => Infrastructure("Solver evidence failed validation.", "Preserve the evidence and report a reproducible issue; do not treat the candidate result as a proof."),
            WorkerClaimReason.CounterexampleReplayFailed => Replay("The candidate violation failed replay validation."),
            WorkerClaimReason.CounterexampleNotReplayable => Replay("The candidate model cannot be supplied as executable inputs."),
            WorkerClaimReason.PostconditionMayBeUndefined => new("Contract definedness", "Evaluating the postcondition may throw or be undefined.", "Inspect null access, indexing, arithmetic, and calls inside the postcondition under the actual preconditions."),
            WorkerClaimReason.EffectSummaryIncomplete => Model("The method or a dependency has incomplete effect evidence."),
            WorkerClaimReason.EffectContractNotEstablished => Model("The available evidence does not establish the declared effect contract."),
            WorkerClaimReason.SolverIncomplete => new("Solver limit", "The solver could not decide the obligation.", "Inspect the recorded solver reason and simplify a reproducer; increase budgets only when exhaustion is recorded."),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Only emitted Unknown reasons have explanations.")
        };
    }

    internal static ReasonExplanation Explain(WorkerCallableCoverageReason reason)
    {
        return reason switch
        {
            WorkerCallableCoverageReason.UnsupportedCallable => Explain(WorkerClaimReason.UnsupportedCallable),
            WorkerCallableCoverageReason.UnsupportedContract => Explain(WorkerClaimReason.UnsupportedContract),
            WorkerCallableCoverageReason.SemanticUnknown => new("Incomplete coverage", "At least one selected claim remains Unknown.", "Inspect this callable's individual claim reasons and the coverage guidance."),
            WorkerCallableCoverageReason.MissingClaimResult => Infrastructure("Expected claim evidence is missing.", "Inspect the run errors and retained evidence; missing evidence does not establish a claim."),
            WorkerCallableCoverageReason.MethodTimeout => Explain(WorkerClaimReason.MethodTimeout),
            WorkerCallableCoverageReason.ProjectTimeout => Explain(WorkerClaimReason.ProjectTimeout),
            WorkerCallableCoverageReason.Canceled => Explain(WorkerClaimReason.Canceled),
            WorkerCallableCoverageReason.InfrastructureFailure => Explain(WorkerClaimReason.InfrastructureFailure),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Only incomplete callable reasons have explanations.")
        };
    }

    private static ReasonExplanation Model(string description)
    {
        global::SharpProof.Internal.InputValidation.Require(description, nameof(description));
        return new("Model limit", description, "Inspect the reported source site and language reference. Isolate the unsupported operation; increasing a timeout cannot add missing semantics.");
    }

    private static ReasonExplanation Replay(string description)
    {
        global::SharpProof.Internal.InputValidation.Require(description, nameof(description));
        return new("Replay limit", description, "Inspect the model and replay evidence. Keep the result Unknown; only a validated executable witness establishes a violation.");
    }

    private static ReasonExplanation Infrastructure(string description, string action)
    {
        global::SharpProof.Internal.InputValidation.Require(description, nameof(description));
        global::SharpProof.Internal.InputValidation.Require(action, nameof(action));
        return new("Infrastructure", description, action);
    }
}
