using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SharpProof.Worker.Protocol;

internal sealed class WorkerProtocolValidationResult
{
    internal WorkerProtocolValidationResult(IEnumerable<WorkerProtocolError> errors)
    {
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        Errors = [.. errors];
    }
    public ImmutableArray<WorkerProtocolError> Errors { get; }
    public bool IsValid => Errors.IsDefaultOrEmpty;
}

internal readonly struct WorkerProtocolRule<T>(string code, Func<T, bool> isValid)
{
    internal readonly string Code = RepositoryParameterGuard.NotNull(code, nameof(code));
    internal readonly Func<T, bool> IsValid = RepositoryParameterGuard.NotNull(isValid, nameof(isValid));
}

internal static class WorkerProtocolMetadata
{

    private static readonly ImmutableHashSet<Enum> s_knownValues = [
        WorkerFeatureSet.Unspecified, WorkerFeatureSet.Effects, WorkerFeatureSet.Contracts, WorkerFeatureSet.All,
        WorkerVerifyPolicy.Unspecified, WorkerVerifyPolicy.Advisory, WorkerVerifyPolicy.WarnOnUnknown, WorkerVerifyPolicy.RequireProven,
        WorkerAssumptionPolicy.Unspecified, WorkerAssumptionPolicy.Allow, WorkerAssumptionPolicy.Warn, WorkerAssumptionPolicy.Error,
        WorkerSelectedFeature.Unspecified, WorkerSelectedFeature.Effects, WorkerSelectedFeature.Contracts,
        WorkerSelectionReason.Unspecified, WorkerSelectionReason.ExplicitAnnotation, WorkerSelectionReason.DiscoveredPostcondition, WorkerSelectionReason.DiscoveredCallPrecondition,
        WorkerClaimKind.Unspecified, WorkerClaimKind.Postcondition, WorkerClaimKind.Effect, WorkerClaimKind.CallPrecondition,
        WorkerClaimEvidence.Unspecified, WorkerClaimEvidence.DirectClause, WorkerClaimEvidence.CompanionClause,
        WorkerClaimEvidence.ReturnAttribute, WorkerClaimEvidence.Attribute, WorkerClaimEvidence.CallSite,
        WorkerEffectContractKind.Unspecified, WorkerEffectContractKind.EnforcePure, WorkerEffectContractKind.ZeroAllocations,
        WorkerEffectContractKind.AllowedCapabilities, WorkerEffectContractKind.DoesNotThrow, WorkerEffectContractKind.AllowedExceptions,
        WorkerEffectContractKind.EffectContract, WorkerEffectContractKind.ResourceUpperBound,
        WorkerResourceMetric.Unspecified, WorkerResourceMetric.LoopBodyEntries, WorkerResourceMetric.ArrayAllocations, WorkerResourceMetric.AllocatedElements, WorkerResourceMetric.AllocatedBytes, WorkerResourceMetric.PeakAuxiliaryBytes, WorkerResourceMetric.RetainedBytes, WorkerResourceMetric.SourceActivationDepth, WorkerResourceMetric.SourceStackBytes,
        WorkerRunStatus.Unspecified, WorkerRunStatus.Complete, WorkerRunStatus.TimedOut, WorkerRunStatus.Canceled, WorkerRunStatus.Failed,
        WorkerCallableCoverage.Unspecified, WorkerCallableCoverage.Complete, WorkerCallableCoverage.Incomplete,
        WorkerClaimOutcome.Unspecified, WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted, WorkerClaimOutcome.Unknown,
        WorkerClaimReason.Unspecified, WorkerClaimReason.None, WorkerClaimReason.UnsupportedCallable,
        WorkerClaimReason.UnsupportedContract, WorkerClaimReason.UnsupportedBody, WorkerClaimReason.UnsupportedExpression,
        WorkerClaimReason.DeepPostcondition, WorkerClaimReason.MissingReturnValue, WorkerClaimReason.ResourceLimit,
        WorkerClaimReason.MethodTimeout, WorkerClaimReason.ProjectTimeout, WorkerClaimReason.Canceled,
        WorkerClaimReason.BackendUnavailable, WorkerClaimReason.InfrastructureFailure, WorkerClaimReason.MalformedBackendResult,
        WorkerClaimReason.CounterexampleReplayFailed, WorkerClaimReason.PostconditionMayBeUndefined,
        WorkerClaimReason.CounterexampleNotReplayable, WorkerClaimReason.EffectSummaryIncomplete,
        WorkerClaimReason.EffectContractNotEstablished, WorkerClaimReason.SolverIncomplete,
        WorkerEffectEvidenceCertainty.Unspecified, WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary,
        WorkerEffectEvidenceCertainty.CompleteMayEffectSummary, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary,
        WorkerEffectEvidenceCertainty.DefiniteViolation, WorkerEffectEvidenceCertainty.Unavailable,
        WorkerEffectEvidenceCertainty.VacuousEntry,
        WorkerAssumptionKind.Unspecified, WorkerAssumptionKind.Precondition, WorkerAssumptionKind.UserAssume,
        WorkerAssumptionKind.TrustedBoundary, WorkerAssumptionKind.ApiSpecification, WorkerAssumptionKind.SourceDomain,
        WorkerAssumptionKind.NormalCompletion,
    ];
    internal static bool IsKnown<T>(T value) where T : struct, Enum
    {
        return s_knownValues.Contains((Enum)(object)value);
    }
    internal static string? GetManifestName(Enum value)
    {
        return value switch
        {
            WorkerSelectedFeature.Unspecified => nameof(WorkerSelectedFeature.Unspecified),
            WorkerSelectedFeature.Effects => nameof(WorkerSelectedFeature.Effects),
            WorkerSelectedFeature.Contracts => nameof(WorkerSelectedFeature.Contracts),
            WorkerSelectionReason.Unspecified => nameof(WorkerSelectionReason.Unspecified),
            WorkerSelectionReason.ExplicitAnnotation => nameof(WorkerSelectionReason.ExplicitAnnotation),
            WorkerSelectionReason.DiscoveredPostcondition => nameof(WorkerSelectionReason.DiscoveredPostcondition),
            WorkerSelectionReason.DiscoveredCallPrecondition => nameof(WorkerSelectionReason.DiscoveredCallPrecondition),
            _ => null
        };
    }
    internal static int GetAssumptionOrder(WorkerAssumptionKind value)
    {
        return value switch
        {
            WorkerAssumptionKind.Unspecified => 0,
            WorkerAssumptionKind.Precondition => 1,
            WorkerAssumptionKind.UserAssume => 2,
            WorkerAssumptionKind.TrustedBoundary => 3,
            WorkerAssumptionKind.ApiSpecification => 4,
            WorkerAssumptionKind.SourceDomain => 5,
            WorkerAssumptionKind.NormalCompletion => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "The manifest contains an unknown enum value.")
        };
    }
    internal static bool HasOnlyKnownFlags(WorkerEffectSet value)
    {
        return (value & ~WorkerEffectSet.AllKnown) == 0;
    }
    internal static bool HasOnlyKnownFlags(WorkerEffectCapabilitySet value)
    {
        return (value & ~WorkerEffectCapabilitySet.AllKnown) == 0;
    }

    internal static bool MatchesClaimShape(WorkerClaimKind kind, WorkerClaimEvidence evidence, WorkerEffectContractKind effectKind)
    {
        return (kind, evidence, effectKind) is
                (WorkerClaimKind.Postcondition, WorkerClaimEvidence.DirectClause, WorkerEffectContractKind.Unspecified)
                or (WorkerClaimKind.Postcondition, WorkerClaimEvidence.CompanionClause, WorkerEffectContractKind.Unspecified)
                or (WorkerClaimKind.Postcondition, WorkerClaimEvidence.ReturnAttribute, WorkerEffectContractKind.Unspecified)
                or (WorkerClaimKind.CallPrecondition, WorkerClaimEvidence.CallSite, WorkerEffectContractKind.Unspecified)
                or (WorkerClaimKind.Effect, WorkerClaimEvidence.Attribute, WorkerEffectContractKind.EnforcePure)
                or (WorkerClaimKind.Effect, WorkerClaimEvidence.Attribute, WorkerEffectContractKind.ZeroAllocations)
                or (WorkerClaimKind.Effect, WorkerClaimEvidence.Attribute, WorkerEffectContractKind.AllowedCapabilities)
                or (WorkerClaimKind.Effect, WorkerClaimEvidence.Attribute, WorkerEffectContractKind.DoesNotThrow)
                or (WorkerClaimKind.Effect, WorkerClaimEvidence.Attribute, WorkerEffectContractKind.AllowedExceptions)
                or (WorkerClaimKind.Effect, WorkerClaimEvidence.Attribute, WorkerEffectContractKind.EffectContract)
                or (WorkerClaimKind.Effect, WorkerClaimEvidence.Attribute, WorkerEffectContractKind.ResourceUpperBound);
    }
    internal static bool MatchesCallableCoverage(WorkerCallableCoverage coverage, WorkerCallableCoverageReason reason)
    {
        return coverage switch
        {
            WorkerCallableCoverage.Complete => reason is WorkerCallableCoverageReason.None,
            WorkerCallableCoverage.Incomplete => reason is WorkerCallableCoverageReason.UnsupportedCallable
                or WorkerCallableCoverageReason.UnsupportedContract or WorkerCallableCoverageReason.SemanticUnknown
                or WorkerCallableCoverageReason.MissingClaimResult or WorkerCallableCoverageReason.MethodTimeout
                or WorkerCallableCoverageReason.ProjectTimeout or WorkerCallableCoverageReason.Canceled
                or WorkerCallableCoverageReason.InfrastructureFailure,
            _ => false
        };
    }
    internal static bool MatchesClaimOutcome(WorkerClaimOutcome outcome, WorkerClaimReason reason)
    {
        return outcome switch
        {
            WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted => reason is WorkerClaimReason.None,
            WorkerClaimOutcome.Unknown => reason is WorkerClaimReason.UnsupportedCallable or WorkerClaimReason.UnsupportedContract
                or WorkerClaimReason.UnsupportedBody or WorkerClaimReason.UnsupportedExpression or WorkerClaimReason.DeepPostcondition
                or WorkerClaimReason.MissingReturnValue or WorkerClaimReason.ResourceLimit or WorkerClaimReason.MethodTimeout
                or WorkerClaimReason.ProjectTimeout or WorkerClaimReason.Canceled or WorkerClaimReason.BackendUnavailable
                or WorkerClaimReason.InfrastructureFailure or WorkerClaimReason.MalformedBackendResult
                or WorkerClaimReason.CounterexampleReplayFailed or WorkerClaimReason.PostconditionMayBeUndefined
                or WorkerClaimReason.CounterexampleNotReplayable or WorkerClaimReason.EffectSummaryIncomplete
                or WorkerClaimReason.EffectContractNotEstablished or WorkerClaimReason.SolverIncomplete,
            _ => false
        };
    }
    internal static bool MatchesClaimKindOutcome(WorkerClaimKind kind, WorkerClaimOutcome outcome, WorkerClaimReason reason)
    {
        return (kind, outcome, reason) is
                (_, WorkerClaimOutcome.Proven, WorkerClaimReason.None) or (_, WorkerClaimOutcome.Refuted, WorkerClaimReason.None)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedCallable)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedContract)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedBody)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedExpression)
                or (WorkerClaimKind.Postcondition, WorkerClaimOutcome.Unknown, WorkerClaimReason.DeepPostcondition)
                or (WorkerClaimKind.Postcondition, WorkerClaimOutcome.Unknown, WorkerClaimReason.MissingReturnValue)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.ResourceLimit)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.MethodTimeout)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.ProjectTimeout)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.Canceled)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.BackendUnavailable)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.InfrastructureFailure)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.MalformedBackendResult)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.CounterexampleReplayFailed)
                or (WorkerClaimKind.Postcondition, WorkerClaimOutcome.Unknown, WorkerClaimReason.PostconditionMayBeUndefined)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.CounterexampleNotReplayable)
                or (WorkerClaimKind.Effect, WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectSummaryIncomplete)
                or (WorkerClaimKind.Effect, WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectContractNotEstablished)
                or (_, WorkerClaimOutcome.Unknown, WorkerClaimReason.SolverIncomplete);
    }
    internal static bool MatchesEffectCertainty(WorkerClaimOutcome outcome, WorkerClaimReason reason, WorkerEffectEvidenceCertainty certainty)
    {
        return (outcome, reason, certainty) is
                (WorkerClaimOutcome.Proven, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.CompleteMayEffectSummary)
                or (WorkerClaimOutcome.Proven, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)
                or (WorkerClaimOutcome.Proven, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.VacuousEntry)
                or (WorkerClaimOutcome.Refuted, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.DefiniteViolation)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedContract, WorkerEffectEvidenceCertainty.Unavailable)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.CounterexampleNotReplayable, WorkerEffectEvidenceCertainty.Unavailable)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectSummaryIncomplete, WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectSummaryIncomplete, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectContractNotEstablished, WorkerEffectEvidenceCertainty.CompleteMayEffectSummary)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectContractNotEstablished, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.ResourceLimit, WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.ResourceLimit, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedBody, WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedBody, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)
                or (WorkerClaimOutcome.Unknown, _, WorkerEffectEvidenceCertainty.Unavailable);
    }
    internal static bool MatchesEffectEvidenceTuple(WorkerClaimOutcome outcome, WorkerClaimReason reason, WorkerEffectEvidenceCertainty certainty, WorkerVacuityKind vacuity, bool hasProofCore, bool hasTrustedProvenance, bool trustedProvenanceUsed)
    {
        return (outcome, reason, certainty, vacuity, hasProofCore, hasTrustedProvenance, trustedProvenanceUsed) is
                (WorkerClaimOutcome.Proven, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.CompleteMayEffectSummary, WorkerVacuityKind.None, _, _, _)
                or (WorkerClaimOutcome.Proven, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary, WorkerVacuityKind.None, _, true, true)
                or (WorkerClaimOutcome.Proven, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.VacuousEntry, WorkerVacuityKind.ContradictoryPreconditions, true, _, _)
                or (WorkerClaimOutcome.Refuted, WorkerClaimReason.None, WorkerEffectEvidenceCertainty.DefiniteViolation, WorkerVacuityKind.None, false, _, _)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectSummaryIncomplete, WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary, WorkerVacuityKind.None, false, _, _)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectSummaryIncomplete, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary, WorkerVacuityKind.None, false, true, true)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectContractNotEstablished, WorkerEffectEvidenceCertainty.CompleteMayEffectSummary, WorkerVacuityKind.None, false, _, _)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.EffectContractNotEstablished, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary, WorkerVacuityKind.None, false, true, true)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.ResourceLimit, WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary, WorkerVacuityKind.None, false, _, _)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.ResourceLimit, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary, WorkerVacuityKind.None, false, true, true)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedBody, WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary, WorkerVacuityKind.None, false, _, _)
                or (WorkerClaimOutcome.Unknown, WorkerClaimReason.UnsupportedBody, WorkerEffectEvidenceCertainty.TrustedCompleteBoundary, WorkerVacuityKind.None, false, true, true)
                or (WorkerClaimOutcome.Unknown, _, WorkerEffectEvidenceCertainty.Unavailable, WorkerVacuityKind.None, false, _, _);
    }
    internal static bool MatchesVacuity(WorkerClaimKind kind, WorkerClaimOutcome outcome, WorkerVacuityKind vacuity)
    {
        return (kind, outcome, vacuity) is
                (_, _, WorkerVacuityKind.None)
                or (WorkerClaimKind.Postcondition, WorkerClaimOutcome.Proven, WorkerVacuityKind.ContradictoryPreconditions)
                or (WorkerClaimKind.Postcondition, WorkerClaimOutcome.Proven, WorkerVacuityKind.NoModeledNormalReturn)
                or (WorkerClaimKind.CallPrecondition, WorkerClaimOutcome.Proven, WorkerVacuityKind.ContradictoryPreconditions)
                or (WorkerClaimKind.Effect, WorkerClaimOutcome.Proven, WorkerVacuityKind.ContradictoryPreconditions);
    }
    internal static bool MatchesRunFailure(WorkerRunStatus status, WorkerRunFailureReason reason)
    {
        return status switch
        {
            WorkerRunStatus.Complete or WorkerRunStatus.TimedOut or WorkerRunStatus.Canceled => reason is WorkerRunFailureReason.None,
            WorkerRunStatus.Failed => reason is WorkerRunFailureReason.InvalidRequest or WorkerRunFailureReason.InputUnavailable
                or WorkerRunFailureReason.CompilationFailure or WorkerRunFailureReason.CompilerManifestMismatch
                or WorkerRunFailureReason.BackendUnavailable or WorkerRunFailureReason.InfrastructureFailure
                or WorkerRunFailureReason.MalformedResult or WorkerRunFailureReason.CounterexampleReplayFailed
                or WorkerRunFailureReason.ContainmentFailure,
            _ => false
        };
    }
    internal static bool MatchesCacheState(WorkerCacheStatus status, bool hit)
    {
        return (status, hit) is
                (WorkerCacheStatus.Disabled, false) or (WorkerCacheStatus.Miss, false) or (WorkerCacheStatus.Hit, true)
                or (WorkerCacheStatus.Written, false) or (WorkerCacheStatus.Rejected, false) or (WorkerCacheStatus.Unavailable, false);
    }
    internal static bool MatchesClaimPayload(WorkerClaimOutcome outcome, bool hasProofCore, bool hasModel)
    {
        return (outcome, hasProofCore, hasModel) is
                (WorkerClaimOutcome.Proven, _, false) or (WorkerClaimOutcome.Refuted, false, _) or (WorkerClaimOutcome.Unknown, false, false);
    }
    internal static bool MatchesAssumptionKind(WorkerAssumptionKind kind)
    {
        return (kind) is
                (WorkerAssumptionKind.Precondition) or (WorkerAssumptionKind.UserAssume) or (WorkerAssumptionKind.TrustedBoundary);
    }

    internal static readonly ImmutableArray<WorkerProtocolRule<WorkerVerifyRequest>> RequestRules = [
        new("protocol.unsupported", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.ProtocolVersion == WorkerProtocolVersions.Current),
        new("project.compiler_manifest", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ =>
            (value.CompilerManifest != null
            && !string.IsNullOrWhiteSpace(value.CompilerManifest.Path)
            && WorkerProtocolJson.IsSha256(value.CompilerManifest.Sha256))),
        new("cache.null", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.Cache != null),
        new("cache.maximum_bytes", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ =>
            (!(value.Cache != null)
            || (value.Cache.MaximumBytes is >= 1 and <= WorkerCacheOptions.DefaultMaximumBytes))),
        new("policy.verify", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.IsDefined(value.VerifyPolicy, WorkerVerifyPolicy.Unspecified)),
        new("policy.assumption", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.IsDefined(value.AssumptionPolicy, WorkerAssumptionPolicy.Unspecified)),
    ];
    internal static readonly ImmutableArray<WorkerProtocolRule<WorkerBudgets>> BudgetsRules = [
        new("rlimit", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.QueryRlimit > 0),
        new("method_rlimit", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.MethodRlimit > 0),
        new("rlimit_order", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.QueryRlimit <= value.MethodRlimit),
        new("method_wall", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.MethodWallTimeMilliseconds > 0),
        new("project_wall", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.ProjectWallTimeMilliseconds > 0),
        new("parallelism", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.MaxParallelism is >= 1 and <= WorkerBudgets.MaximumParallelism),
        new("expression_depth", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.MaximumExpressionDepth is >= 1 and <= WorkerBudgets.MaximumAllowedExpressionDepth),
        new("wall_order", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.MethodWallTimeMilliseconds <= value.ProjectWallTimeMilliseconds),
    ];
    internal static bool IsSourceLocationValid(WorkerSourceLocation value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return (!string.IsNullOrWhiteSpace(value.Path)
            && value.Start >= 0
            && value.Length >= 0
            && value.Line > 0
            && value.Column > 0);
    }
    internal static readonly ImmutableArray<WorkerProtocolRule<WorkerCallableManifestEntry>> ManifestCallableRules = [
        new("selected_features", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ =>
            WorkerProtocolJson.AreDefinedUnique(value.SelectedFeatures, WorkerSelectedFeature.Unspecified, true)),
        new("selection_reasons", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ =>
            WorkerProtocolJson.AreDefinedUnique(value.SelectionReasons, WorkerSelectionReason.Unspecified, true)),
        new("callable_claim_ids", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.AreDistinctNonblank(value.ClaimIds)),
        new("callable_assumptions", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.AreValidAssumptions(value.Assumptions)),
        new("callable_assumption_usage", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => (WorkerProtocolJson.EmptyIfDefault(value.Assumptions)).All(static item => item != null && !item.Used)),
    ];
    internal static bool ResourceConstraintsEqual(WorkerResourceConstraint? left, WorkerResourceConstraint? right)
    {
        return ReferenceEquals(left, right) || left is not null && right is not null &&
            left.Metric == right.Metric && left.CostModel == right.CostModel && left.ExpressionModel == right.ExpressionModel &&
            left.UpperBound is not null && right.UpperBound is not null && left.UpperBound.SequenceEqual(right.UpperBound);
    }
    internal static bool HasValidResourceConstraint(WorkerEffectContractKind kind, WorkerResourceConstraint? constraint)
    {
        if (kind != WorkerEffectContractKind.ResourceUpperBound)
        { return constraint is null; }
        return constraint is not null && WorkerProtocolJson.IsDefined(constraint.Metric, WorkerResourceMetric.Unspecified) &&
            constraint.CostModel == (constraint.Metric == WorkerResourceMetric.AllocatedBytes ? WorkerResourceModels.AllocationCostModel : constraint.Metric is WorkerResourceMetric.PeakAuxiliaryBytes or WorkerResourceMetric.RetainedBytes ? WorkerResourceModels.LifetimeCostModel : constraint.Metric is WorkerResourceMetric.SourceActivationDepth or WorkerResourceMetric.SourceStackBytes ? WorkerResourceModels.StackCostModel : WorkerResourceModels.CostModel) && constraint.ExpressionModel == WorkerResourceModels.Expression &&
            constraint.UpperBound is { Length: > 0 and <= WorkerResourceModels.MaximumExpressionBytes };
    }
    internal static bool HasValidCallPrecondition(WorkerClaimKind kind, WorkerCallPreconditionTarget? target)
    {
        if (kind != WorkerClaimKind.CallPrecondition)
        { return target is null; }
        return target is not null && !string.IsNullOrWhiteSpace(target.Callee) && target.ClauseOrdinal >= 0 &&
            (target.ExportedBy is null || !string.IsNullOrWhiteSpace(target.ExportedBy) && target.ExportedBy.Length <= 1024 &&
                !target.CallerDeclared && target.ClauseLocation is not null) &&
            (target.ClauseLocation is { } location
                ? target.MetadataClause is null && IsSourceLocationValid(location)
                : target.MetadataClause is { } metadata && !target.CallerDeclared && IsMetadataClauseValid(metadata));
    }
    internal static bool IsMetadataClauseValid(WorkerMetadataClauseLocation metadata)
    {
        metadata = RepositoryParameterGuard.NotNull(metadata, nameof(metadata));
        return !string.IsNullOrWhiteSpace(metadata.Assembly) && metadata.Assembly.Length <= 4096 &&
            !string.IsNullOrWhiteSpace(metadata.Member) && metadata.Member.Length <= 4096 &&
            metadata.Parameter is >= 0 and < 128 &&
            !string.IsNullOrWhiteSpace(metadata.Attribute) && metadata.Attribute.Length <= 256;
    }
    internal static readonly ImmutableArray<WorkerProtocolRule<WorkerClaimManifestEntry>> ManifestClaimRules = [
        new("claim_resource_constraint", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => HasValidResourceConstraint(value.EffectContractKind, value.ResourceUpperBound)),
        new("claim_call_precondition", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => HasValidCallPrecondition(value.Kind, value.CallPrecondition)),
        new("claim_ordinal", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.Ordinal >= 0),
        new("claim_kind", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.IsDefined(value.Kind, WorkerClaimKind.Unspecified)),
        new("claim_evidence", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.IsDefined(value.Evidence, WorkerClaimEvidence.Unspecified)),
        new("claim_shape", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolMetadata.MatchesClaimShape(value.Kind, value.Evidence, value.EffectContractKind)),
    ];
    internal static bool IsAssumptionValid(WorkerAssumptionEvidence value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return (!string.IsNullOrWhiteSpace(value.Id)
            && WorkerProtocolMetadata.MatchesAssumptionKind(value.Kind));
    }
    internal static readonly ImmutableArray<WorkerProtocolRule<WorkerCallableResult>> CallableResultRules = [
        new("response.callable_coverage", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.IsDefined(value.Coverage, WorkerCallableCoverage.Unspecified)),
        new("response.callable_reason", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolMetadata.MatchesCallableCoverage(value.Coverage, value.Reason)),
        new("response.callable_assumptions", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.AreValidAssumptions(value.Assumptions)),
        new("response.callable_assumption_usage", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => (WorkerProtocolJson.EmptyIfDefault(value.Assumptions)).All(static item => item != null && !item.Used)),
    ];
    internal static readonly ImmutableArray<WorkerProtocolRule<WorkerClaimResult>> ClaimResultRules = [
        new("response.claim_outcome", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.IsDefined(value.Outcome, WorkerClaimOutcome.Unspecified)),
        new("response.claim_reason", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolMetadata.MatchesClaimOutcome(value.Outcome, value.Reason)),
        new("response.proof_core", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.AreDistinctNonblank(value.ProofCore)),
        new("response.model", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.AreValidModel(value.Model)),
        new("response.assumptions", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolJson.AreValidAssumptions(value.Assumptions)),
        new("response.vacuity_evidence", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ =>
            (!(value.Vacuity != WorkerVacuityKind.None)
            || (!value.ProofCore.IsDefaultOrEmpty))),
        new("response.claim_payload", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ =>
            WorkerProtocolMetadata.MatchesClaimPayload(value.Outcome, !value.ProofCore.IsDefaultOrEmpty, !value.Model.IsDefaultOrEmpty)),
    ];
    internal static bool IsEffectWitnessValid(WorkerEffectViolationWitness value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return (!string.IsNullOrWhiteSpace(value.Kind)
            && !string.IsNullOrWhiteSpace(value.Detail)
            && WorkerProtocolMetadata.HasOnlyKnownFlags(value.Effects)
            && WorkerProtocolMetadata.HasOnlyKnownFlags(value.Capabilities)
            && (value.Effects != WorkerEffectSet.None
            || value.Capabilities != WorkerEffectCapabilitySet.None)
            && (!(!value.ExactExceptionTypeHierarchy.IsDefaultOrEmpty)
            || ((value.Effects & WorkerEffectSet.Throws) != 0))
            && WorkerProtocolJson.AreDistinctNonblank(value.ExactExceptionTypeHierarchy));
    }
    internal static bool IsVersionsValid(WorkerVersionSummary value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return (value.ProtocolVersion == WorkerProtocolVersions.Current
            && value.ManifestSchemaVersion == WorkerManifestVersions.Current
            && value.CacheSchemaVersion == WorkerCacheVersions.Current
            && !string.IsNullOrWhiteSpace(value.WorkerVersion)
            && !string.IsNullOrWhiteSpace(value.ApiSpecVersion)
            && WorkerProtocolJson.IsSha256(value.WorkerBinarySha256)
            && WorkerProtocolJson.IsSha256(value.ApiSpecContentSha256));
    }
    internal static readonly ImmutableArray<WorkerProtocolRule<WorkerVerificationSummary>> SummaryRules = [
        new("summary.cache", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => WorkerProtocolMetadata.MatchesCacheState(value.CacheStatus, value.CacheHit)),
        new("summary.versions", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ =>
            (value.Versions != null
            && WorkerProtocolMetadata.IsVersionsValid(value.Versions))),
        new("summary.elapsed", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => value.ElapsedMilliseconds >= 0),
        new("summary.costs", static value /* SPINT001: all values valid - Validator.Rules rejects null before any predicate runs */ => IsCostReportValid(value.Costs)),
    ];
    internal static bool IsCostReportValid(WorkerCostReport? /* SPINT001: all values valid - absent costs are valid */ value)
    {
        if (value == null)
        {
            return true;
        }
        if (value.Callables.IsDefault)
        {
            return false;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cost in value.Callables)
        {
            if (cost == null || string.IsNullOrWhiteSpace(cost.CallableId) || !seen.Add(cost.CallableId) ||
                cost.ConsumedRlimit < 0 || cost.ElapsedMilliseconds < 0)
            {
                return false;
            }
        }
        return true;
    }
    internal static bool IsProtocolErrorValid(WorkerProtocolError value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return (!string.IsNullOrWhiteSpace(value.Code)
            && WorkerProtocolJson.IsSingleLineText(value.Code)
            && !string.IsNullOrWhiteSpace(value.Message)
            && WorkerProtocolJson.IsSingleLineText(value.Message));
    }
}
