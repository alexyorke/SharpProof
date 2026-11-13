namespace SharpProof.Worker.Launcher;

internal static partial class LauncherPresentation
{
    internal static int ExitCode(
        SharpProof.Worker.Protocol.WorkerRunStatus status
    )
    {
        return status switch
        {
            SharpProof.Worker.Protocol.WorkerRunStatus.TimedOut => 124,
            SharpProof.Worker.Protocol.WorkerRunStatus.Canceled => 4,
            _ => 3
        };
    }

    internal static string ClaimKind(
        SharpProof.Worker.Protocol.WorkerClaimManifestEntry claim
    )
    {
        if (claim.Kind == SharpProof.Worker.Protocol.WorkerClaimKind.Effect &&
            claim.EffectContractKind == SharpProof.Worker.Protocol.WorkerEffectContractKind.ResourceUpperBound)
        {
            return claim.ResourceUpperBound is { } resource ? "resource:" + resource.Metric :
                throw new ArgumentException("A resource claim requires its constraint.", nameof(claim));
        }
        return claim.Kind switch
        {
            SharpProof.Worker.Protocol.WorkerClaimKind.Postcondition => "Postcondition",
            SharpProof.Worker.Protocol.WorkerClaimKind.Effect => "effect:" + EffectKind(claim.EffectContractKind),
            SharpProof.Worker.Protocol.WorkerClaimKind.CallPrecondition => "CallPrecondition",
            _ => throw new ArgumentOutOfRangeException(nameof(claim))
        };
    }

    // "postcondition of Counter.Next", "call precondition in Counter.Broken".
    // The short source name is used when the compiler recorded one; the
    // documentation ID otherwise.
    internal static string RefutedSubject(
        SharpProof.Worker.Protocol.WorkerClaimManifestEntry claim,
        SharpProof.Worker.Protocol.WorkerClaimResult result
    )
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        var callable = result.Presentation is { Name.Length: > 0 } presentation ? presentation.Name : claim.CallableId;
        return claim.Kind switch
        {
            SharpProof.Worker.Protocol.WorkerClaimKind.Postcondition => "postcondition of " + callable,
            SharpProof.Worker.Protocol.WorkerClaimKind.CallPrecondition => "call precondition in " + callable,
            _ => ClaimKind(claim) + " claim for " + callable
        };
    }

    // Names the callee clause a call-precondition claim checks; the claim's
    // location is the call.
    internal static string CallPreconditionDetail(
        SharpProof.Worker.Protocol.WorkerClaimManifestEntry claim
    )
    {
        ArgumentNullException.ThrowIfNull(claim);
        return claim.CallPrecondition is not { } call ? string.Empty
            : FormattableString.Invariant($" Call to {SharpProof.Worker.Protocol.WorkerCallPreconditionTarget.StableCallee(call.Callee)}, clause {call.ClauseOrdinal} {call.DescribeClause(SourcePrefix)}.");
    }

    private static string EffectKind(
        SharpProof.Worker.Protocol.WorkerEffectContractKind kind
    )
    {
        return kind switch
        {
            SharpProof.Worker.Protocol.WorkerEffectContractKind.EnforcePure => "EnforcePure",
            SharpProof.Worker.Protocol.WorkerEffectContractKind.ZeroAllocations => "ZeroAllocations",
            SharpProof.Worker.Protocol.WorkerEffectContractKind.AllowedCapabilities => "AllowedCapabilities",
            SharpProof.Worker.Protocol.WorkerEffectContractKind.DoesNotThrow => "DoesNotThrow",
            SharpProof.Worker.Protocol.WorkerEffectContractKind.AllowedExceptions => "AllowedExceptions",
            SharpProof.Worker.Protocol.WorkerEffectContractKind.EffectContract => "EffectContract",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    internal static string Level(
        object policy,
        string advisory
    )
    {
        advisory = RepositoryParameterGuard.NotNull(advisory, nameof(advisory));
        return policy switch
        {
            SharpProof.Worker.Protocol.WorkerVerifyPolicy.Advisory or SharpProof.Worker.Protocol.WorkerAssumptionPolicy.Allow => advisory,
            SharpProof.Worker.Protocol.WorkerVerifyPolicy.WarnOnUnknown or SharpProof.Worker.Protocol.WorkerAssumptionPolicy.Warn => "warning",
            SharpProof.Worker.Protocol.WorkerVerifyPolicy.RequireProven or SharpProof.Worker.Protocol.WorkerAssumptionPolicy.Error => "error",
            _ => throw new InvalidOperationException("The policy was not validated.")
        };
    }

    // A callable whose only gaps are timed-out call-precondition claims reports
    // as warn-on-unknown would, so require-proven does not fail on machine load.
    internal static SharpProof.Worker.Protocol.WorkerVerifyPolicy TimingOnlyPolicy(
        SharpProof.Worker.Protocol.WorkerVerifyPolicy policy
    )
    {
        return policy == SharpProof.Worker.Protocol.WorkerVerifyPolicy.RequireProven
            ? SharpProof.Worker.Protocol.WorkerVerifyPolicy.WarnOnUnknown : policy;
    }

    internal const string TimingOnlyNote =
        " Only call-precondition claims ran out of time here, and those do not fail require-proven; their calls count as unchecked.";

    internal static SharpProof.Worker.Protocol.WorkerVerifyPolicy ParseVerifyPolicy(
        string value
    )
    {
        return value.ToUpperInvariant() switch
        {
            "ADVISORY" => SharpProof.Worker.Protocol.WorkerVerifyPolicy.Advisory,
            "WARN-ON-UNKNOWN" => SharpProof.Worker.Protocol.WorkerVerifyPolicy.WarnOnUnknown,
            "REQUIRE-PROVEN" => SharpProof.Worker.Protocol.WorkerVerifyPolicy.RequireProven,
            _ => throw new ArgumentException("The verifier policy is invalid.", nameof(value))
        };
    }

    internal static SharpProof.Worker.Protocol.WorkerAssumptionPolicy ParseAssumptionPolicy(
        string value
    )
    {
        return value.ToUpperInvariant() switch
        {
            "ALLOW" => SharpProof.Worker.Protocol.WorkerAssumptionPolicy.Allow,
            "WARN" => SharpProof.Worker.Protocol.WorkerAssumptionPolicy.Warn,
            "ERROR" => SharpProof.Worker.Protocol.WorkerAssumptionPolicy.Error,
            _ => throw new ArgumentException("The assumption policy is invalid.", nameof(value))
        };
    }
}
