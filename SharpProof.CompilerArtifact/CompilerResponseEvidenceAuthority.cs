using System.Collections.Immutable;
using System.Globalization;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

/// <summary>
/// Binds the free-form evidence rows in a worker response to the lowered
/// compiler artifact.  The protocol validator remains deliberately unaware of
/// compiler IR; this adapter is the single artifact-aware mapping used by both
/// the worker and the launcher.
/// </summary>
internal sealed class CompilerResponseEvidenceAuthority :
    IWorkerResponseEvidenceAuthority
{
    private readonly ImmutableArray<CompilerCallablePreparation> _targets;
    private readonly Func<CompilerCallablePreparation, IrCallInstruction,
        IrValue?, ImmutableArray<IrValue>, IrValue?>? _replayCallHost;

    private sealed class AssumptionShape
    {
        internal AssumptionShape(
            ImmutableArray<(string Id, WorkerAssumptionKind Kind)> byId,
            ImmutableArray<(string Id, WorkerAssumptionKind Kind)> canonical)
        {
            ById = byId;
            Canonical = canonical;
        }

        internal ImmutableArray<(string Id, WorkerAssumptionKind Kind)> ById { get; }

        internal ImmutableArray<(string Id, WorkerAssumptionKind Kind)> Canonical { get; }
    }

    private sealed class TargetProofLabels
    {
        internal TargetProofLabels(CompilerCallablePreparation target)
        {
            All = new HashSet<string>(StringComparer.Ordinal);
            Entry = new HashSet<string>(StringComparer.Ordinal);
            RequiresAssumptionIds = new Dictionary<string, string>(StringComparer.Ordinal);
            AssumeAssumptionIds = new Dictionary<string, string>(StringComparer.Ordinal);
            ClauseLabels = CompilerResponseEvidenceAuthority.ClauseLabels(target);
            foreach (var (label, clause) in ClauseLabels)
            {
                All.Add(label);
                if (clause.Kind == CompilerContractKind.Requires &&
                    clause.Condition is not IrBooleanTerm { Value: true })
                {
                    Entry.Add(label);
                }

                if (clause.AssumptionId != null)
                {
                    if (clause.Kind == CompilerContractKind.Requires)
                    {
                        RequiresAssumptionIds.Add(label, clause.AssumptionId);
                    }
                    else if (clause.Kind == CompilerContractKind.Assume)
                    {
                        AssumeAssumptionIds.Add(label, clause.AssumptionId);
                    }
                }
            }

            foreach (var variable in target.Variables)
            {
                if (variable.SourceIntegerInterval.HasValue)
                {
                    All.Add(DomainLabel(variable));
                }

                if ((variable.Role is CompilerVariableRole.Receiver or
                    CompilerVariableRole.Parameter) &&
                    variable.SourceIntegerInterval.HasValue)
                {
                    Entry.Add(DomainLabel(variable));
                }
            }

            if (target.Body is not { } body)
            {
                return;
            }

            foreach (var spec in body.SpecCalls.Values)
            {
                if (!string.IsNullOrWhiteSpace(spec.WitnessIdentifier))
                {
                    All.Add("spec:" + spec.WitnessIdentifier);
                }
            }

            foreach (var summary in body.SummaryCalls.Values)
            {
                var prefix = CompilerSpecificationPackAuthorityValidation
                    .GetSummaryPrefix(summary.Origin);
                if (prefix != null)
                {
                    All.Add(SummaryLabel(summary));
                }
            }

            All.Add("body:normal-completion");
        }

        internal HashSet<string> All { get; }

        internal HashSet<string> Entry { get; }

        internal Dictionary<string, string> RequiresAssumptionIds { get; }

        internal Dictionary<string, string> AssumeAssumptionIds { get; }

        internal (string Label, CompilerPreparedClause Clause)[] ClauseLabels { get; }
    }

    private sealed class TargetClaimIndex
    {
        private readonly Dictionary<string?, CompilerEffectClaimArtifact> _effects =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string?, CompilerPreparedClause> _postconditions =
            new(StringComparer.Ordinal);

        internal TargetClaimIndex(CompilerCallablePreparation target)
        {
            var hasLiteralFalsePrecondition = false;
            foreach (var effect in target.EffectClaims)
            {
                if (!_effects.ContainsKey(effect.ClaimId))
                {
                    _effects.Add(effect.ClaimId, effect);
                }
            }

            foreach (var clause in target.Clauses)
            {
                hasLiteralFalsePrecondition |=
                    clause.Kind == CompilerContractKind.Requires &&
                    clause.Condition is IrBooleanTerm { Value: false };
                if (clause.Kind == CompilerContractKind.Ensures &&
                    !_postconditions.ContainsKey(clause.ClaimId))
                {
                    _postconditions.Add(clause.ClaimId, clause);
                }
            }

            HasLiteralFalsePrecondition = hasLiteralFalsePrecondition;
            ProofLabels = new TargetProofLabels(target);
        }

        internal bool HasLiteralFalsePrecondition { get; }

        internal TargetProofLabels ProofLabels { get; }

        internal CompilerEffectClaimArtifact? FindEffect(string? claimId)
        {
            return _effects.TryGetValue(claimId, out var effect)
                ? effect
                : null;
        }

        internal CompilerPreparedClause? FindPostcondition(string? claimId)
        {
            return _postconditions.TryGetValue(claimId, out var clause)
                ? clause
                : null;
        }
    }

    internal CompilerResponseEvidenceAuthority(
        ImmutableArray<CompilerCallablePreparation> targets,
        Func<CompilerCallablePreparation, IrCallInstruction, IrValue?,
            ImmutableArray<IrValue>, IrValue?>? replayCallHost = null)
    {
        if (targets.IsDefault || targets.Any(static target => target == null))
        {
            throw new ArgumentException(
                "Compiler response authority targets are incomplete.",
                nameof(targets));
        }

        _targets = targets;
        _replayCallHost = replayCallHost;
    }

    public IEnumerable<string> Validate(WorkerVerifyResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(response, nameof(response));

        var errors = new HashSet<string>(StringComparer.Ordinal);
        var claims = (response.ClaimResults ?? [])
            .Where(static claim => claim != null)
            .ToDictionary(static claim => claim.ClaimId, StringComparer.Ordinal);
        var callables = (response.CallableResults ?? [])
            .Where(static callable => callable != null)
            .ToDictionary(static callable => callable.CallableId, StringComparer.Ordinal);

        foreach (var target in _targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!callables.TryGetValue(target.Entry.CallableId, out var callable))
            {
                continue;
            }

            var claimIndex = new TargetClaimIndex(target);
            var assumptionShape = CreateAssumptionShape(target.Entry.Assumptions);
            ValidateCallableAssumptions(callable, assumptionShape, errors);
            foreach (var claimId in target.Entry.ClaimIds)
            {
                if (claims.TryGetValue(claimId, out var claim))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateClaim(
                        target,
                        claimIndex,
                        claim,
                        assumptionShape,
                        errors,
                        cancellationToken);
                }
            }
        }

        return errors.OrderBy(static code => code, StringComparer.Ordinal);
    }

    private static void ValidateCallableAssumptions(
        WorkerCallableResult result,
        AssumptionShape assumptionShape,
        HashSet<string> errors)
    {
        ValidateAssumptionShape(
            result.Assumptions,
            assumptionShape,
            [],
            errors);
    }

    private void ValidateClaim(
        CompilerCallablePreparation target,
        TargetClaimIndex claimIndex,
        WorkerClaimResult result,
        AssumptionShape assumptionShape,
        HashSet<string> errors,
        CancellationToken cancellationToken)
    {
        if (!target.IsSuccess)
        {
            ValidateFailedTargetClaim(
                target, claimIndex, result, assumptionShape, errors);
            return;
        }

        var effect = claimIndex.FindEffect(result.ClaimId);
        var postcondition = claimIndex.FindPostcondition(result.ClaimId);
        if (effect == null && postcondition == null ||
            effect != null && postcondition != null)
        {
            errors.Add("response.evidence_authority");
            return;
        }

        var expectedUsed = new HashSet<string>(StringComparer.Ordinal);
        if (result.Vacuity == WorkerVacuityKind.ContradictoryPreconditions)
        {
            if (!HasAdmissibleEntryCore(claimIndex.ProofLabels, result.ProofCore))
            {
                errors.Add("response.vacuity_authority");
            }

            expectedUsed.UnionWith(
                AssumptionIdsForCore(
                    claimIndex.ProofLabels, result.ProofCore, requiresOnly: true));
        }
        else if (postcondition != null &&
                 result.Outcome == WorkerClaimOutcome.Proven)
        {
            expectedUsed.UnionWith(
                AssumptionIdsForCore(
                    claimIndex.ProofLabels, result.ProofCore, requiresOnly: false));
        }

        if (effect != null &&
            result.Outcome == WorkerClaimOutcome.Proven &&
            result.EffectCertainty ==
                WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)
        {
            expectedUsed.UnionWith(target.Entry.Assumptions
                .Where(static assumption =>
                    assumption.Kind == WorkerAssumptionKind.TrustedBoundary)
                .Select(static assumption => assumption.Id));
        }

        ValidateAssumptionShape(
            result.Assumptions,
            assumptionShape,
            expectedUsed,
            errors);

        if (effect != null)
        {
            ValidateEffectClaim(claimIndex, effect, result, errors);
        }
        else
        {
            ValidatePostconditionClaim(
                target,
                claimIndex,
                result,
                errors,
                claimIndex.HasLiteralFalsePrecondition,
                cancellationToken);
        }
    }

    private static void ValidateFailedTargetClaim(
        CompilerCallablePreparation target,
        TargetClaimIndex claimIndex,
        WorkerClaimResult result,
        AssumptionShape assumptionShape,
        HashSet<string> errors)
    {
        var effect = claimIndex.FindEffect(result.ClaimId);
        if (effect != null &&
            target.FailureReason != WorkerClaimReason.UnsupportedCallable)
        {
            ValidateFailedTargetEffectClaim(
                target,
                claimIndex,
                effect,
                result,
                assumptionShape,
                errors);
            return;
        }

        var expectedCertainty = effect != null
            ? WorkerEffectEvidenceCertainty.Unavailable
            : WorkerEffectEvidenceCertainty.Unspecified;

        if (result.Outcome != WorkerClaimOutcome.Unknown ||
            result.Reason != target.FailureReason ||
            result.EffectCertainty != expectedCertainty ||
            result.Vacuity != WorkerVacuityKind.None ||
            result.ProofCore is not { Length: 0 } ||
            result.Model is not { Length: 0 } ||
            result.EffectWitness != null)
        {
            errors.Add("response.evidence_authority");
        }

        ValidateAssumptionShape(
            result.Assumptions,
            assumptionShape,
            [],
            errors);
    }

    private static void ValidateFailedTargetEffectClaim(
        CompilerCallablePreparation target,
        TargetClaimIndex claimIndex,
        CompilerEffectClaimArtifact evidence,
        WorkerClaimResult result,
        AssumptionShape assumptionShape,
        HashSet<string> errors)
    {
        var replayFailed = evidence.Outcome == WorkerClaimOutcome.Refuted &&
            result.Outcome == WorkerClaimOutcome.Unknown &&
            result.Reason == WorkerClaimReason.CounterexampleReplayFailed &&
            result.EffectCertainty ==
                WorkerEffectEvidenceCertainty.Unavailable;
        var matchesCompilerEvidence =
            result.Outcome == evidence.Outcome &&
            result.Reason == evidence.Reason &&
            result.EffectCertainty == evidence.Certainty;
        if (!replayFailed && !matchesCompilerEvidence)
        {
            errors.Add("response.evidence_authority");
        }

        IEnumerable<string> expectedUsed =
            result.Outcome == WorkerClaimOutcome.Proven &&
            result.EffectCertainty ==
                WorkerEffectEvidenceCertainty.TrustedCompleteBoundary
                    ? target.Entry.Assumptions
                        .Where(static assumption =>
                            assumption.Kind ==
                                WorkerAssumptionKind.TrustedBoundary)
                        .Select(static assumption => assumption.Id)
                    : [];
        ValidateAssumptionShape(
            result.Assumptions,
            assumptionShape,
            expectedUsed,
            errors);
        ValidateEffectClaim(claimIndex, evidence, result, errors);
    }

    private static void ValidateAssumptionShape(
        WorkerAssumptionEvidence[]? actual,
        AssumptionShape expected,
        IEnumerable<string> expectedUsed,
        HashSet<string> errors)
    {
        if (!HasValidAssumptionShape(actual, expected))
        {
            errors.Add("response.assumption_usage_authority");
            return;
        }

        var used = new HashSet<string>(expectedUsed, StringComparer.Ordinal);
        foreach (var assumption in actual ?? [])
        {
            if (assumption.Used != used.Contains(assumption.Id))
            {
                errors.Add("response.assumption_usage_authority");
            }
        }
    }

    private static AssumptionShape CreateAssumptionShape(
        WorkerAssumptionEvidence[]? expected)
    {
        var declarations = (expected ?? [])
            .Where(static value => value != null)
            .Select(static value => (value.Id, value.Kind))
            .ToArray();
        return new AssumptionShape(
            [.. declarations.OrderBy(
                static value => value.Id,
                StringComparer.Ordinal)],
            [.. declarations.OrderBy(
                    static value => WorkerProtocolMetadata.GetAssumptionOrder(
                        value.Kind))
                .ThenBy(static value => value.Id, StringComparer.Ordinal)]);
    }

    private static bool HasValidAssumptionShape(
        WorkerAssumptionEvidence[]? actual,
        AssumptionShape expected)
    {
        var declarations = (actual ?? [])
            .Where(static value => value != null)
            .Select(static value => (value.Id, value.Kind))
            .ToArray();
        return declarations.SequenceEqual(expected.ById) &&
            declarations.SequenceEqual(expected.Canonical);
    }

    private static void ValidateEffectClaim(
        TargetClaimIndex claimIndex,
        CompilerEffectClaimArtifact evidence,
        WorkerClaimResult result,
        HashSet<string> errors)
    {
        if (!IsCanonicalProofCore(result.ProofCore) ||
            !IsCanonicalModel(result.Model))
        {
            errors.Add("response.evidence_order");
        }

        if (result.Vacuity == WorkerVacuityKind.ContradictoryPreconditions)
        {
            ValidateProofCore(claimIndex.ProofLabels, result, errors, entryOnly: true);
            return;
        }

        if (result.Outcome == WorkerClaimOutcome.Refuted)
        {
            if (result.Model is { Length: > 0 } ||
                result.ProofCore is { Length: > 0 } ||
                !CompilerEffectAuthority.WitnessesEqual(
                    result.EffectWitness,
                    evidence.Witness) ||
                !CompilerEffectViolationAuthority.IsViolation(
                    evidence,
                    result.EffectWitness))
            {
                errors.Add("response.effect_witness_authority");
            }

            return;
        }

        if (result.Outcome == WorkerClaimOutcome.Proven)
        {
            var expected = "compiler-effect:" + evidence.EvidenceSha256;
            if (result.ProofCore is not { Length: 1 } ||
                result.ProofCore[0] != expected)
            {
                errors.Add("response.proof_core_authority");
            }

            return;
        }

        if (result.ProofCore is { Length: > 0 } ||
            result.Model is { Length: > 0 } ||
            result.EffectWitness != null)
        {
            errors.Add("response.evidence_authority");
        }
    }

    private void ValidatePostconditionClaim(
        CompilerCallablePreparation target,
        TargetClaimIndex claimIndex,
        WorkerClaimResult result,
        HashSet<string> errors,
        bool hasLiteralFalsePrecondition,
        CancellationToken cancellationToken)
    {
        if (!IsCanonicalProofCore(result.ProofCore) ||
            !IsCanonicalModel(result.Model))
        {
            errors.Add("response.evidence_order");
        }

        if (result.Outcome == WorkerClaimOutcome.Refuted)
        {
            if (result.ProofCore is { Length: > 0 } ||
                result.Vacuity != WorkerVacuityKind.None ||
                !TryReplayPostcondition(target, result, out _, cancellationToken))
            {
                errors.Add("response.model_authority");
            }

            return;
        }

        if (result.Outcome == WorkerClaimOutcome.Proven)
        {
            ValidateProofCore(
                claimIndex.ProofLabels,
                result,
                errors,
                entryOnly: result.Vacuity ==
                    WorkerVacuityKind.ContradictoryPreconditions);
            if (result.Model is { Length: > 0 })
            {
                errors.Add("response.model_authority");
            }

            if (result.Vacuity == WorkerVacuityKind.NoModeledNormalReturn &&
                !(result.ProofCore ?? []).Contains(
                    "body:normal-completion", StringComparer.Ordinal))
            {
                errors.Add("response.vacuity_authority");
            }

            if (hasLiteralFalsePrecondition &&
                result.Vacuity != WorkerVacuityKind.ContradictoryPreconditions)
            {
                errors.Add("response.vacuity_authority");
            }

            return;
        }

        if (result.ProofCore is { Length: > 0 } ||
            result.Model is { Length: > 0 })
        {
            errors.Add("response.evidence_authority");
        }
    }

    private static void ValidateProofCore(
        TargetProofLabels labels,
        WorkerClaimResult result,
        HashSet<string> errors,
        bool entryOnly)
    {
        var allowed = entryOnly ? labels.Entry : labels.All;
        if ((result.ProofCore ?? []).Any(label => !allowed.Contains(label)))
        {
            errors.Add("response.proof_core_authority");
        }
    }

    private static bool HasAdmissibleEntryCore(
        TargetProofLabels labels,
        string[]? proofCore)
    {
        if (!IsCanonicalProofCore(proofCore) || proofCore is not { Length: > 0 })
        {
            return false;
        }

        return proofCore.All(labels.Entry.Contains) &&
            proofCore.Any(static label =>
                label.StartsWith("requires:", StringComparison.Ordinal) ||
                label.StartsWith("domain:", StringComparison.Ordinal));
    }

    private static IEnumerable<string> AssumptionIdsForCore(
        TargetProofLabels labels,
        IEnumerable<string>? proofCore,
        bool requiresOnly)
    {
        var ids = requiresOnly
            ? labels.RequiresAssumptionIds
            : labels.AssumeAssumptionIds;
        return (proofCore ?? [])
            .Where(ids.ContainsKey)
            .Select(label => ids[label]);
    }

    private static (string Label, CompilerPreparedClause Clause)[] ClauseLabels(
        CompilerCallablePreparation target)
    {
        var ordinal = 0;
        var labels = new List<(string, CompilerPreparedClause)>();
        foreach (var clause in target.Clauses)
        {
            if (clause.Kind == CompilerContractKind.Ensures)
            {
                continue;
            }

            var prefix = clause.Kind switch
            {
                CompilerContractKind.Requires => "requires",
                CompilerContractKind.Assume => "assume",
                _ => string.Empty
            };
            if (prefix.Length != 0)
            {
                labels.Add((prefix + ":" + ordinal.ToString(
                    CultureInfo.InvariantCulture), clause));
            }

            ordinal++;
        }

        return labels.ToArray();
    }

    private static string DomainLabel(CompilerCanonicalVariable variable)
    {
        return variable.Role switch
        {
            CompilerVariableRole.Receiver => "domain:receiver",
            CompilerVariableRole.Parameter => "domain:parameter:" +
                variable.Ordinal.ToString(CultureInfo.InvariantCulture),
            CompilerVariableRole.Result => "domain:result",
            _ => string.Empty
        };
    }

    private static string SummaryLabel(CompilerPreparedSummaryCall summary)
    {
        var prefix = CompilerSpecificationPackAuthorityValidation
            .GetSummaryPrefix(summary.Origin);
        if (prefix == null)
        {
            return string.Empty;
        }

        var summaryEvidence = summary.Origin ==
                CompilerSummaryOrigin.SpecificationPack
            ? prefix + ":" + summary.EvidenceIdentity
            : prefix;
        return summaryEvidence + ":" + summary.CallIdentity +
            CompilerDependencyEvidenceFormatter.Format(
                summary.DependencyEvidence,
                throwOnUnsupportedOrigin: false);
    }

    private static bool IsCanonicalProofCore(string[]? values)
    {
        if (values == null)
        {
            return false;
        }

        for (var index = 1; index < values.Length; index++)
        {
            if (StringComparer.Ordinal.Compare(
                    values[index - 1],
                    values[index]) > 0)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsCanonicalModel(WorkerModelValue[]? values)
    {
        if (values == null)
        {
            return false;
        }

        for (var index = 1; index < values.Length; index++)
        {
            var previous = values[index - 1];
            var current = values[index];
            var comparison = StringComparer.Ordinal.Compare(
                previous?.Variable ?? string.Empty,
                current?.Variable ?? string.Empty);
            if (comparison == 0)
            {
                comparison = StringComparer.Ordinal.Compare(
                    previous?.Kind ?? string.Empty,
                    current?.Kind ?? string.Empty);
            }
            if (comparison == 0)
            {
                comparison = StringComparer.Ordinal.Compare(
                    previous?.Value ?? string.Empty,
                    current?.Value ?? string.Empty);
            }
            if (comparison > 0)
            {
                return false;
            }
        }
        return true;
    }

    private bool TryReplayPostcondition(
        CompilerCallablePreparation target,
        WorkerClaimResult result,
        out ImmutableDictionary<IrVarId, IrValue> model,
        CancellationToken cancellationToken = default)
    {
        var variables = target.Variables.ToDictionary(
            static variable => variable.ModelLabel,
            StringComparer.Ordinal);
        var requiredInputs = target.Variables.Where(variable =>
                variable.Role is (CompilerVariableRole.Receiver or
                    CompilerVariableRole.Parameter) &&
                (target.Factory.GetVariableInfo(variable.Variable).Type ==
                    target.Factory.BooleanType ||
                 target.Factory.GetVariableInfo(variable.Variable).Type ==
                    target.Factory.IntegerType))
            .Select(static variable => variable.Variable)
            .ToArray();
        if (!CompilerModelValues.TryCreateModel(
                target.Factory,
                variables,
                requiredInputs,
                result.Model,
                requireInputRole: true,
                out model) ||
            !CompilerModelValues.EntryAssumptionsHold(
                target,
                model,
                cancellationToken) ||
            target.Body == null)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ensures = target.Clauses.Where(static clause =>
            clause.Kind == CompilerContractKind.Ensures).ToArray();
        var ordinal = Array.FindIndex(
            ensures,
            clause => clause.ClaimId == result.ClaimId);
        if (ordinal < 0)
        {
            return false;
        }

        try
        {
            return CompilerCallablePostconditionReplay.Replay(
                target,
                model,
                ensures[ordinal].Condition,
                rejectUnexpectedReturnValue: false,
                cancellationToken,
                _replayCallHost == null
                    ? null
                    : (call, receiver, arguments) => _replayCallHost(
                        target, call, receiver, arguments)) ==
                CompilerCallableReplayStatus.Refuted;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
            KeyNotFoundException)
        {
            return false;
        }
    }

}

internal enum CompilerCallableReplayStatus
{
    Refuted,
    PostconditionUndefined,
    UnsupportedRegisteredCall,
    Failed
}

internal static class CompilerCallablePostconditionReplay
{
    internal static CompilerCallableReplayStatus Replay(
        CompilerCallablePreparation target,
        ImmutableDictionary<IrVarId, IrValue> model,
        IrTerm postcondition,
        bool rejectUnexpectedReturnValue,
        CancellationToken cancellationToken,
        Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>,
            IrValue?>? callHost = null)
    {
        if (target.Body is not { } body)
        {
            return CompilerCallableReplayStatus.Failed;
        }

        var factory = target.Factory;
        var final = model.ToBuilder();
        var results = target.Variables.Where(static variable =>
            variable.Role == CompilerVariableRole.Result).ToArray();
        if (body.Kind == CompilerPreparedBodyKind.Program)
        {
            if (body.Program is not { } program ||
                !ReferenceEquals(program.Factory, factory))
            {
                return CompilerCallableReplayStatus.Failed;
            }

            var initial = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
            foreach (var binding in body.ParameterBindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!model.TryGetValue(binding.Value, out var value))
                {
                    return CompilerCallableReplayStatus.Failed;
                }

                initial[binding.Key] = value;
            }

            var maximumSteps = program.Blocks.Sum(
                static block => (long)block.Instructions.Length);
            if (maximumSteps is < 1 or > CompilerPreparedBody.MaximumInstructions)
            {
                return CompilerCallableReplayStatus.Failed;
            }

            var execution = new IrProgramInterpreter(factory).Execute(
                program,
                initial.ToImmutable(),
                (int)maximumSteps,
                callHost,
                cancellationToken);
            if (execution.Status != IrProgramExecutionStatus.Returned)
            {
                return execution is
                {
                    Status: IrProgramExecutionStatus.Unsupported,
                    Instruction: IrCallInstruction call
                } && (body.SpecCalls.ContainsKey(call.Id) ||
                      body.SummaryCalls.ContainsKey(call.Id))
                    ? CompilerCallableReplayStatus.UnsupportedRegisteredCall
                    : CompilerCallableReplayStatus.Failed;
            }

            foreach (var binding in body.ParameterBindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!execution.Values.TryGetValue(binding.Key, out var value))
                {
                    return CompilerCallableReplayStatus.Failed;
                }

                final[binding.Value] = value;
            }

            if (results.Length > 1 ||
                results.Length == 0 && rejectUnexpectedReturnValue &&
                execution.ReturnValue != null ||
                results.Length == 1 &&
                (execution.ReturnValue == null ||
                 execution.ReturnValue.Type != factory.GetVariableInfo(
                     results[0].Variable).Type))
            {
                return CompilerCallableReplayStatus.Failed;
            }

            if (results.Length == 1)
            {
                final[results[0].Variable] = execution.ReturnValue!;
            }
        }
        else if (body.Kind != CompilerPreparedBodyKind.Trivial ||
                 body.Program != null ||
                 !body.ParameterBindings.IsEmpty ||
                 !body.SpecCalls.IsEmpty ||
                 !body.SummaryCalls.IsEmpty ||
                 results.Length != 0)
        {
            return CompilerCallableReplayStatus.Failed;
        }

        foreach (var variable in target.Variables.Where(static variable =>
                     variable.Role == CompilerVariableRole.PreState))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!variable.CurrentStateVariable.HasValue ||
                !model.TryGetValue(variable.CurrentStateVariable.Value, out var value) ||
                value.Type != factory.GetVariableInfo(variable.Variable).Type)
            {
                return CompilerCallableReplayStatus.Failed;
            }

            final[variable.Variable] = value;
        }

        foreach (var variable in target.Variables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (final.TryGetValue(variable.Variable, out var value) &&
                !CompilerSourceIntegerDomain.Contains(
                    variable.SourceIntegerInterval,
                    value))
            {
                return CompilerCallableReplayStatus.Failed;
            }
        }

        var evaluated = new IrInterpreter(factory).Evaluate(
            postcondition,
            final,
            cancellationToken);
        if (evaluated.Status == IrEvaluationStatus.Exception)
        {
            return CompilerCallableReplayStatus.PostconditionUndefined;
        }

        return evaluated.Status == IrEvaluationStatus.Value &&
               evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: false }
            ? CompilerCallableReplayStatus.Refuted
            : CompilerCallableReplayStatus.Failed;
    }
}
