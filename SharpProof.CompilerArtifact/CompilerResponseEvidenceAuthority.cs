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

    internal CompilerResponseEvidenceAuthority(
        ImmutableArray<CompilerCallablePreparation> targets)
    {
        if (targets.IsDefault || targets.Any(static target => target == null))
        {
            throw new ArgumentException(
                "Compiler response authority targets are incomplete.",
                nameof(targets));
        }

        _targets = targets;
    }

    public IEnumerable<string> Validate(WorkerVerifyResponse response)
    {
        if (response == null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        var errors = new HashSet<string>(StringComparer.Ordinal);
        var claimGroups = (response.ClaimResults ?? [])
            .Where(static claim => claim != null)
            .GroupBy(static claim => claim.ClaimId, StringComparer.Ordinal)
            .ToArray();
        var claims = claimGroups.ToDictionary(
            static group => group.Key,
            static group => group.First(),
            StringComparer.Ordinal);
        var callableGroups = (response.CallableResults ?? [])
            .Where(static callable => callable != null)
            .GroupBy(static callable => callable.CallableId, StringComparer.Ordinal)
            .ToArray();
        var callables = callableGroups.ToDictionary(
            static group => group.Key,
            static group => group.First(),
            StringComparer.Ordinal);
        if (claimGroups.Any(static group => group.Count() > 1) ||
            callableGroups.Any(static group => group.Count() > 1))
        {
            errors.Add("response.evidence_authority");
        }

        foreach (var target in _targets)
        {
            if (!callables.TryGetValue(target.Entry.CallableId, out var callable))
            {
                continue;
            }

            ValidateCallableAssumptions(target, callable, errors);
            foreach (var claimId in target.Entry.ClaimIds)
            {
                if (claims.TryGetValue(claimId, out var claim))
                {
                    ValidateClaim(target, claim, errors);
                }
            }
        }

        return errors.OrderBy(static code => code, StringComparer.Ordinal);
    }

    private static void ValidateCallableAssumptions(
        CompilerCallablePreparation target,
        WorkerCallableResult result,
        HashSet<string> errors)
    {
        ValidateAssumptionShape(
            result.Assumptions,
            target.Entry.Assumptions,
            [],
            errors);
    }

    private static void ValidateClaim(
        CompilerCallablePreparation target,
        WorkerClaimResult result,
        HashSet<string> errors)
    {
        if (!target.IsSuccess)
        {
            ValidateFailedTargetClaim(target, result, errors);
            return;
        }

        var effect = target.EffectClaims.FirstOrDefault(
            evidence => evidence.ClaimId == result.ClaimId);
        var postcondition = target.Clauses.FirstOrDefault(
            clause => clause.Kind == CompilerContractKind.Ensures &&
                      clause.ClaimId == result.ClaimId);
        if (effect == null && postcondition == null ||
            effect != null && postcondition != null)
        {
            errors.Add("response.evidence_authority");
            return;
        }

        var expectedUsed = new HashSet<string>(StringComparer.Ordinal);
        if (result.Vacuity == WorkerVacuityKind.ContradictoryPreconditions)
        {
            if (!HasAdmissibleEntryCore(target, result.ProofCore))
            {
                errors.Add("response.vacuity_authority");
            }

            expectedUsed.UnionWith(
                AssumptionIdsForCore(target, result.ProofCore, requiresOnly: true));
        }
        else if (postcondition != null &&
                 result.Outcome == WorkerClaimOutcome.Proven)
        {
            expectedUsed.UnionWith(
                AssumptionIdsForCore(target, result.ProofCore, requiresOnly: false));
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
            target.Entry.Assumptions,
            expectedUsed,
            errors);

        if (effect != null)
        {
            ValidateEffectClaim(target, effect, result, errors);
        }
        else
        {
            ValidatePostconditionClaim(target, result, errors);
        }
    }

    private static void ValidateFailedTargetClaim(
        CompilerCallablePreparation target,
        WorkerClaimResult result,
        HashSet<string> errors)
    {
        var isEffect = target.EffectClaims.Any(
            evidence => evidence.ClaimId == result.ClaimId);
        var expectedCertainty = isEffect
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
            target.Entry.Assumptions,
            [],
            errors);
    }

    private static void ValidateAssumptionShape(
        WorkerAssumptionEvidence[]? actual,
        WorkerAssumptionEvidence[]? expected,
        IEnumerable<string> expectedUsed,
        HashSet<string> errors)
    {
        if (!SameAssumptions(actual, expected) ||
            !IsCanonicalAssumptions(actual, expected))
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

    private static void ValidateEffectClaim(
        CompilerCallablePreparation target,
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
            ValidateProofCore(target, result, errors, entryOnly: true);
            return;
        }

        if (result.Outcome == WorkerClaimOutcome.Refuted)
        {
            if (result.Model is { Length: > 0 } ||
                result.ProofCore is { Length: > 0 } ||
                !WitnessesEqual(result.EffectWitness, evidence.Witness) ||
                !WitnessContradictsContract(evidence, result.EffectWitness))
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

    private static void ValidatePostconditionClaim(
        CompilerCallablePreparation target,
        WorkerClaimResult result,
        HashSet<string> errors)
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
                !TryReplayPostcondition(target, result, out _))
            {
                errors.Add("response.model_authority");
            }

            return;
        }

        if (result.Outcome == WorkerClaimOutcome.Proven)
        {
            ValidateProofCore(
                target,
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

            if (HasLiteralFalsePrecondition(target) &&
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
        CompilerCallablePreparation target,
        WorkerClaimResult result,
        HashSet<string> errors,
        bool entryOnly)
    {
        var allowed = entryOnly ? EntryLabels(target) : AllLabels(target);
        if ((result.ProofCore ?? []).Any(label => !allowed.Contains(label)))
        {
            errors.Add("response.proof_core_authority");
        }
    }

    private static bool HasAdmissibleEntryCore(
        CompilerCallablePreparation target,
        string[]? proofCore)
    {
        if (!IsCanonicalProofCore(proofCore) || proofCore is not { Length: > 0 })
        {
            return false;
        }

        var labels = EntryLabels(target);
        return proofCore.All(labels.Contains) &&
            proofCore.Any(static label =>
                label.StartsWith("requires:", StringComparison.Ordinal) ||
                label.StartsWith("domain:", StringComparison.Ordinal));
    }

    private static HashSet<string> AllLabels(
        CompilerCallablePreparation target)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (label, _) in ClauseLabels(target))
        {
            labels.Add(label);
        }

        foreach (var variable in target.Variables)
        {
            if (variable.SourceIntegerInterval.HasValue)
            {
                labels.Add(DomainLabel(variable));
            }
        }

        if (target.Body is { } body)
        {
            foreach (var spec in body.SpecCalls.Values)
            {
                if (!string.IsNullOrWhiteSpace(spec.WitnessIdentifier))
                {
                    labels.Add("spec:" + spec.WitnessIdentifier);
                }
            }

            foreach (var summary in body.SummaryCalls.Values)
            {
                var prefix = SummaryPrefix(summary.Origin);
                if (prefix == null)
                {
                    continue;
                }

                labels.Add(SummaryLabel(summary));
            }

            labels.Add("body:normal-completion");
        }

        return labels;
    }

    private static HashSet<string> EntryLabels(
        CompilerCallablePreparation target)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (label, clause) in ClauseLabels(target))
        {
            if (clause.Kind == CompilerContractKind.Requires &&
                clause.Condition is not IrBooleanTerm { Value: true })
            {
                labels.Add(label);
            }
        }

        foreach (var variable in target.Variables)
        {
            if ((variable.Role is CompilerVariableRole.Receiver or
                CompilerVariableRole.Parameter) &&
                variable.SourceIntegerInterval.HasValue)
            {
                labels.Add(DomainLabel(variable));
            }
        }

        return labels;
    }

    private static IEnumerable<string> AssumptionIdsForCore(
        CompilerCallablePreparation target,
        IEnumerable<string>? proofCore,
        bool requiresOnly)
    {
        var ids = ClauseLabels(target)
            .Where(item => item.Clause.AssumptionId != null &&
                (requiresOnly
                    ? item.Clause.Kind == CompilerContractKind.Requires
                    : item.Clause.Kind == CompilerContractKind.Assume))
            .ToDictionary(static item => item.Label, static item => item.Clause.AssumptionId!,
                StringComparer.Ordinal);
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

    private static string? SummaryPrefix(CompilerSummaryOrigin origin)
    {
        return origin switch
        {
            CompilerSummaryOrigin.Source => "source-summary",
            CompilerSummaryOrigin.ImplementationIl => "il-summary",
            CompilerSummaryOrigin.SpecificationPack => "spec-pack",
            _ => null
        };
    }

    private static string SummaryLabel(CompilerPreparedSummaryCall summary)
    {
        var prefix = SummaryPrefix(summary.Origin);
        if (prefix == null)
        {
            return string.Empty;
        }

        var summaryEvidence = summary.Origin ==
                CompilerSummaryOrigin.SpecificationPack
            ? prefix + ":" + summary.EvidenceIdentity
            : prefix;
        return summaryEvidence + ":" + summary.CallIdentity +
            DependencyEvidenceLabel(summary.DependencyEvidence);
    }

    private static string DependencyEvidenceLabel(
        ImmutableArray<CompilerPreparedSummaryEvidence> evidence)
    {
        if (evidence.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var values = evidence.Select(item =>
        {
            var prefix = SummaryPrefix(item.Origin);
            if (prefix == null)
            {
                return string.Empty;
            }

            var evidencePrefix = item.Origin ==
                    CompilerSummaryOrigin.SpecificationPack
                ? prefix + ":" + item.EvidenceIdentity
                : prefix;
            return evidencePrefix + ":" + item.CallIdentity + ":" +
                item.EvidenceSha256;
        });
        return ":deps=" + string.Join(";", values);
    }

    private static bool HasLiteralFalsePrecondition(
        CompilerCallablePreparation target)
    {
        return target.Clauses.Any(static clause =>
            clause.Kind == CompilerContractKind.Requires &&
            clause.Condition is IrBooleanTerm { Value: false });
    }

    private static bool SameAssumptions(
        WorkerAssumptionEvidence[]? actual,
        WorkerAssumptionEvidence[]? expected)
    {
        static IEnumerable<(string Id, WorkerAssumptionKind Kind)> Normalize(
            WorkerAssumptionEvidence[]? values)
        {
            return (values ?? [])
                .Where(static value => value != null)
                .OrderBy(static value => value.Id, StringComparer.Ordinal)
                .Select(static value => (value.Id, value.Kind));
        }

        return Normalize(actual).SequenceEqual(Normalize(expected));
    }

    private static bool IsCanonicalAssumptions(
        WorkerAssumptionEvidence[]? actual,
        WorkerAssumptionEvidence[]? expected)
    {
        var canonical = (expected ?? [])
            .Where(static value => value != null)
            .OrderBy(static value => WorkerProtocolMetadata.GetAssumptionOrder(value.Kind))
            .ThenBy(static value => value.Id, StringComparer.Ordinal)
            .Select(static value => (value.Id, value.Kind))
            .ToArray();
        var actualShape = (actual ?? [])
            .Where(static value => value != null)
            .Select(static value => (value.Id, value.Kind))
            .ToArray();
        return actualShape.SequenceEqual(canonical);
    }

    private static bool IsCanonicalProofCore(string[]? values)
    {
        if (values == null)
        {
            return false;
        }

        return values.SequenceEqual(
            values.OrderBy(static value => value, StringComparer.Ordinal));
    }

    private static bool IsCanonicalModel(WorkerModelValue[]? values)
    {
        if (values == null)
        {
            return false;
        }

        var actual = values.Select(static value =>
                (value?.Variable ?? string.Empty,
                 value?.Kind ?? string.Empty,
                 value?.Value ?? string.Empty))
            .ToArray();
        var canonical = actual.OrderBy(static value => value.Item1, StringComparer.Ordinal)
            .ThenBy(static value => value.Item2, StringComparer.Ordinal)
            .ThenBy(static value => value.Item3, StringComparer.Ordinal)
            .ToArray();
        return actual.SequenceEqual(canonical);
    }

    private static bool WitnessContradictsContract(
        CompilerEffectClaimArtifact evidence,
        WorkerEffectViolationWitness? witness)
    {
        if (witness == null)
        {
            return false;
        }

        var unexpectedEffects = witness.Effects & ~evidence.Constraint.AllowedEffects;
        var unexpectedCapabilities =
            witness.Capabilities & ~evidence.Constraint.AllowedCapabilities;
        return evidence.ContractKind switch
        {
            WorkerEffectContractKind.EnforcePure =>
                witness.Effects != WorkerEffectSet.None ||
                witness.Capabilities != WorkerEffectCapabilitySet.None,
            WorkerEffectContractKind.ZeroAllocations =>
                (witness.Effects & WorkerEffectSet.Allocates) != 0,
            WorkerEffectContractKind.AllowedCapabilities =>
                unexpectedCapabilities != WorkerEffectCapabilitySet.None,
            WorkerEffectContractKind.DoesNotThrow =>
                (witness.Effects & WorkerEffectSet.Throws) != 0,
            WorkerEffectContractKind.AllowedExceptions =>
                (witness.Effects & WorkerEffectSet.Throws) != 0 &&
                witness.ExactExceptionTypeHierarchy.Any(type =>
                    !evidence.Constraint.AllowedExceptionTypes.Contains(
                        type, StringComparer.Ordinal)),
            WorkerEffectContractKind.EffectContract =>
                unexpectedEffects != WorkerEffectSet.None ||
                unexpectedCapabilities != WorkerEffectCapabilitySet.None ||
                (witness.Effects & WorkerEffectSet.Throws) != 0 &&
                witness.ExactExceptionTypeHierarchy.Any(type =>
                    !evidence.Constraint.AllowedExceptionTypes.Contains(
                        type, StringComparer.Ordinal)),
            _ => false
        };
    }

    private static bool TryReplayPostcondition(
        CompilerCallablePreparation target,
        WorkerClaimResult result,
        out ImmutableDictionary<IrVarId, IrValue> model)
    {
        model = ImmutableDictionary<IrVarId, IrValue>.Empty;
        if (!TryCreateModel(target, result.Model, out model) ||
            !EntryAssumptionsHold(target, model) ||
            target.Body is not { } body)
        {
            return false;
        }

        try
        {
            var final = model.ToBuilder();
            if (body.Kind == CompilerPreparedBodyKind.Program)
            {
                if (body.Program is not { } program ||
                    !ReferenceEquals(program.Factory, target.Factory))
                {
                    return false;
                }

                var initial = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
                foreach (var binding in body.ParameterBindings)
                {
                    if (!model.TryGetValue(binding.Value, out var value))
                    {
                        return false;
                    }

                    initial[binding.Key] = value;
                }

                var maximumSteps = program.Blocks.Sum(
                    static block => (long)block.Instructions.Length);
                if (maximumSteps is < 1 or > CompilerPreparedBody.MaximumInstructions)
                {
                    return false;
                }

                var execution = new IrProgramInterpreter(target.Factory).Execute(
                    program,
                    initial.ToImmutable(),
                    (int)maximumSteps);
                if (execution.Status != IrProgramExecutionStatus.Returned)
                {
                    return false;
                }

                foreach (var binding in body.ParameterBindings)
                {
                    if (!execution.Values.TryGetValue(binding.Key, out var value))
                    {
                        return false;
                    }

                    final[binding.Value] = value;
                }

                var results = target.Variables.Where(static variable =>
                    variable.Role == CompilerVariableRole.Result).ToArray();
                if (results.Length > 1 || results.Length == 1 &&
                    (execution.ReturnValue == null ||
                     execution.ReturnValue.Type != target.Factory.GetVariableInfo(
                         results[0].Variable).Type))
                {
                    return false;
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
                     target.Variables.Any(static variable =>
                         variable.Role == CompilerVariableRole.Result))
            {
                return false;
            }

            foreach (var variable in target.Variables.Where(static variable =>
                         variable.Role == CompilerVariableRole.PreState))
            {
                if (!variable.CurrentStateVariable.HasValue ||
                    !model.TryGetValue(variable.CurrentStateVariable.Value, out var value) ||
                    value.Type != target.Factory.GetVariableInfo(variable.Variable).Type)
                {
                    return false;
                }

                final[variable.Variable] = value;
            }

            var ensures = target.Clauses.Where(static clause =>
                clause.Kind == CompilerContractKind.Ensures).ToArray();
            var ordinal = Array.FindIndex(
                ensures,
                clause => clause.ClaimId == result.ClaimId);
            if (ordinal < 0)
            {
                return false;
            }

            var evaluated = new IrInterpreter(target.Factory).Evaluate(
                ensures[ordinal].Condition, final);
            return evaluated.Status == IrEvaluationStatus.Value &&
                evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: false };
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
            KeyNotFoundException)
        {
            return false;
        }
    }

    private static bool EntryAssumptionsHold(
        CompilerCallablePreparation target,
        IReadOnlyDictionary<IrVarId, IrValue> model)
    {
        var interpreter = new IrInterpreter(target.Factory);
        foreach (var clause in target.Clauses.Where(static clause =>
                     clause.Kind is CompilerContractKind.Requires or
                         CompilerContractKind.Assume))
        {
            var evaluated = interpreter.Evaluate(clause.Condition, model);
            if (evaluated.Status != IrEvaluationStatus.Value ||
                evaluated.Value is not { Kind: IrValueKind.Boolean, Boolean: true })
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryCreateModel(
        CompilerCallablePreparation target,
        WorkerModelValue[]? rows,
        out ImmutableDictionary<IrVarId, IrValue> model)
    {
        model = ImmutableDictionary<IrVarId, IrValue>.Empty;
        if (rows == null)
        {
            return false;
        }

        var variables = target.Variables.ToDictionary(
            static variable => variable.ModelLabel,
            StringComparer.Ordinal);
        var required = target.Variables.Where(variable =>
                variable.Role is (CompilerVariableRole.Receiver or
                    CompilerVariableRole.Parameter) &&
                (target.Factory.GetVariableInfo(variable.Variable).Type ==
                    target.Factory.BooleanType ||
                 target.Factory.GetVariableInfo(variable.Variable).Type ==
                    target.Factory.IntegerType))
            .ToArray();
        var result = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        foreach (var row in rows)
        {
            if (row == null ||
                !variables.TryGetValue(row.Variable, out var variable) ||
                variable.Role is not (CompilerVariableRole.Receiver or
                    CompilerVariableRole.Parameter) ||
                !TryCreateValue(target.Factory, variable, row, out var value) ||
                result.ContainsKey(variable.Variable))
            {
                return false;
            }

            result.Add(variable.Variable, value);
        }

        foreach (var variable in required)
        {
            var type = target.Factory.GetVariableInfo(variable.Variable).Type;
            if ((type != target.Factory.BooleanType &&
                 type != target.Factory.IntegerType) ||
                !result.ContainsKey(variable.Variable))
            {
                return false;
            }
        }

        model = result.ToImmutable();
        return true;
    }

    private static bool TryCreateValue(
        IrFactory factory,
        CompilerCanonicalVariable variable,
        WorkerModelValue row,
        out IrValue value)
    {
        var type = factory.GetVariableInfo(variable.Variable).Type;
        if (type == factory.BooleanType &&
            row is { Kind: nameof(IrValueKind.Boolean), Value: "true" or "false" })
        {
            value = factory.CreateBooleanValue(row.Value == "true");
            return true;
        }

        if (type == factory.IntegerType &&
            row is { Kind: nameof(IrValueKind.Integer) } &&
            long.TryParse(
                row.Value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var integer) &&
            row.Value == integer.ToString(CultureInfo.InvariantCulture) &&
            (variable.SourceIntegerInterval is not { } interval ||
             integer >= interval.Minimum && integer <= interval.Maximum))
        {
            value = factory.CreateIntegerValue(integer);
            return true;
        }

        value = null!;
        return false;
    }

    private static bool WitnessesEqual(
        WorkerEffectViolationWitness? actual,
        WorkerEffectViolationWitness? expected)
    {
        if (actual == null || expected == null)
        {
            return actual == null && expected == null;
        }

        return actual.Kind == expected.Kind &&
            actual.Detail == expected.Detail &&
            actual.Effects == expected.Effects &&
            actual.Capabilities == expected.Capabilities &&
            actual.ExactExceptionTypeHierarchy.SequenceEqual(
                expected.ExactExceptionTypeHierarchy,
                StringComparer.Ordinal) &&
            LocationsEqual(actual.Location, expected.Location);
    }

    private static bool LocationsEqual(
        WorkerSourceLocation? left,
        WorkerSourceLocation? right)
    {
        return left != null && right != null &&
            (left.Path, left.Start, left.Length, left.Line, left.Column) ==
            (right.Path, right.Start, right.Length, right.Line, right.Column);
    }
}
