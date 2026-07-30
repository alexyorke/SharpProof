using static SharpProof.Worker.SymbolicTermOperations;
using static SharpProof.Worker.PostconditionObligationBuilder;

namespace SharpProof.Worker;

internal sealed class CallableVerifier(ISmtBackend backend, int maximumExpressionDepth)
{
    private readonly ProofKernel _kernel = new(backend ?? throw new ArgumentNullException(nameof(backend)));
    private readonly AcyclicBlockPredicateExecutor _executor = new(maximumExpressionDepth);
    private readonly int _maximumExpressionDepth =
        maximumExpressionDepth > 0
            ? maximumExpressionDepth
            : throw new ArgumentOutOfRangeException(nameof(maximumExpressionDepth));

    internal async Task<ImmutableArray<WorkerClaimResult>> VerifyAsync(
        CompilerCallablePreparation target,
        MethodResourceBudget resourceBudget,
        CancellationToken cancellationToken)
    {
        var verification = await VerifyWithEntryFeasibilityAsync(
                target,
                resourceBudget,
                cancellationToken)
            .ConfigureAwait(false);
        return verification.Postconditions;
    }

    internal async Task<CallableProofVerification>
        VerifyWithEntryFeasibilityAsync(
            CompilerCallablePreparation target,
            MethodResourceBudget resourceBudget,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resourceBudget);
        cancellationToken.ThrowIfCancellationRequested();
        if (!target.IsSuccess)
        {
            return new CallableProofVerification(
                CallableClaimResultAssembler.PostconditionUnknowns(
                    target,
                    target.FailureReason),
                CallableEntryFeasibility.Unknown(
                    target.FailureReason));
        }

        if (target.Entry.ClaimIds.Length == 0)
        {
            return new CallableProofVerification(
                [],
                CallableEntryFeasibility.Feasible);
        }

        var entryFeasibility = await EvaluateEntryFeasibilityAsync(
                target,
                resourceBudget,
                cancellationToken)
            .ConfigureAwait(false);
        var postconditions = await VerifyPostconditionsAsync(
                target,
                resourceBudget,
                entryFeasibility,
                cancellationToken)
            .ConfigureAwait(false);
        return new CallableProofVerification(
            postconditions,
            entryFeasibility);
    }

    private async Task<ImmutableArray<WorkerClaimResult>>
        VerifyPostconditionsAsync(
            CompilerCallablePreparation target,
            MethodResourceBudget resourceBudget,
            CallableEntryFeasibility entryFeasibility,
            CancellationToken cancellationToken)
    {
        var factory = target.Factory;
        var ensures = target.Clauses.Where(static clause => clause.Kind == CompilerContractKind.Ensures).ToImmutableArray();
        if (ensures.Length > target.Entry.ClaimIds.Length ||
            !ensures.Select(static clause => clause.ClaimId!).SequenceEqual(target.Entry.ClaimIds.Take(ensures.Length),
                StringComparer.Ordinal))
        {
            return CallableClaimResultAssembler.PostconditionUnknowns(target, WorkerClaimReason.UnsupportedContract);
        }

        if (ensures.IsDefaultOrEmpty)
        {
            return [];
        }

        if (entryFeasibility.IsUnknown)
        {
            return CallableClaimResultAssembler.PostconditionUnknowns(
                target,
                entryFeasibility.Reason);
        }

        SymbolicBodyExecution body = target.Body switch
        {
            { Kind: CompilerPreparedBodyKind.Trivial } => TrivialBody(factory),
            { Kind: CompilerPreparedBodyKind.Program, Program: not null } prepared =>
                _executor.Execute(target.Variables, factory, prepared.Program,
                    prepared.SpecCalls, prepared.ParameterBindings.ToImmutableDictionary(
                        static item => item.Key, item => (IrTerm)factory.Variable(item.Value)),
                    prepared.ParameterBindings),
            _ => SymbolicBodyExecution.Failed(WorkerClaimReason.UnsupportedBody)
        };
        if (!body.IsSuccess)
        {
            return CallableClaimResultAssembler.PostconditionUnknowns(target, body.Reason);
        }

        var evidenceResult = CallableEvidenceBuilder.Build(
            target,
            body,
            _maximumExpressionDepth);
        if (!evidenceResult.IsSuccess)
        {
            return CallableClaimResultAssembler.PostconditionUnknowns(
                target,
                evidenceResult.FailureReason);
        }
        var evidence = evidenceResult.Evidence!;
        var assumptions = evidence.Assumptions;
        var assumptionLabels = evidence.AssumptionLabels;
        var userAssumptionIds = evidence.UserAssumptionIds;
        var normalCompletion = evidence.NormalCompletion;
        var replayVariables = evidence.ReplayVariables;
        var normalCompletionUnknown = WorkerClaimReason.None;
        var normalCompletionProofCore =
            normalCompletion is IrBooleanTerm { Value: false }
                ? ImmutableArray.Create("body:normal-completion")
                : [];
        var noModeledNormalReturn = normalCompletion is IrBooleanTerm { Value: false };
        if (!entryFeasibility.IsContradictory &&
            normalCompletion is not IrBooleanTerm)
        {
            var bodyEvidence = assumptions.Where(
                static assumption =>
                    assumption.Justification is not UserAssumedJustification);
            var completionOutcome = await ProbeSatisfiabilityAsync(
                    factory,
                    bodyEvidence,
                    replayVariables,
                    resourceBudget,
                    cancellationToken)
                .ConfigureAwait(false);
            if (completionOutcome == null)
            {
                return CallableClaimResultAssembler.PostconditionUnknowns(target, WorkerClaimReason.ResourceLimit);
            }

            noModeledNormalReturn = completionOutcome is ProvenOutcome;
            if (completionOutcome is ProvenOutcome proven)
            {
                normalCompletionProofCore = CreateProofCore(
                    proven,
                    assumptionLabels);
                if (normalCompletionProofCore.IsDefaultOrEmpty)
                {
                    noModeledNormalReturn = false;
                    normalCompletionUnknown =
                        WorkerClaimReason.MalformedBackendResult;
                }
            }
            else if (completionOutcome is UnknownOutcome unknown)
            {
                normalCompletionUnknown =
                    CallableClaimResultAssembler.MapAbstention(
                        unknown.Reason);
            }
        }
        var records = ImmutableArray.CreateBuilder<WorkerClaimResult>(ensures.Length);
        for (var index = 0; index < ensures.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathObligations = ImmutableArray.CreateBuilder<IrTerm>(body.Returns.Length);
            var missingReturnValue = false;
            foreach (var path in body.Returns)
            {
                var pathCondition = ApplyBodySubstitutions(factory, ensures[index].Condition,
                    target.Variables, path.ReturnTerm, path.CurrentStates);
                if (pathCondition == null)
                {
                    missingReturnValue = true;
                    break;
                }
                pathCondition = SpecResultDomainProjection.Rewrite(factory, pathCondition, body.SpecResultProjections);
                var executionCondition = SpecResultDomainProjection.Rewrite(
                    factory, path.Predicate, body.SpecResultProjections);
                pathObligations.Add(Guard(factory, executionCondition, pathCondition));
            }
            if (missingReturnValue)
            {
                records.Add(CallableClaimResultAssembler.Unknown(target, index, WorkerClaimReason.MissingReturnValue));
                continue;
            }
            var condition = Conjoin(factory, pathObligations);
            if (GetDepth(condition) > _maximumExpressionDepth)
            {
                records.Add(CallableClaimResultAssembler.Unknown(target, index, WorkerClaimReason.DeepPostcondition));
                continue;
            }
            if (!evidence.UsesSupportedDomain ||
                !IsSupportedProofDomain(factory, condition))
            {
                records.Add(CallableClaimResultAssembler.Unknown(target, index, WorkerClaimReason.UnsupportedExpression));
                continue;
            }
            if (!resourceBudget.TryStartQuery())
            {
                records.AddRange(CallableClaimResultAssembler.PostconditionUnknowns(target, WorkerClaimReason.ResourceLimit).Skip(index));
                break;
            }
            var query = new VerificationQuery(factory, assumptions,
                new Goal(factory, condition, ProofDiagnosticKind.Postcondition, new SourceLocationId(index)),
                replayVariables);
            var outcome = await _kernel.VerifyAsync(query, cancellationToken).ConfigureAwait(false);
            if (resourceBudget.IsExceeded)
            {
                records.AddRange(CallableClaimResultAssembler.PostconditionUnknowns(target, WorkerClaimReason.ResourceLimit).Skip(index));
                break;
            }
            if (outcome is ProvenOutcome &&
                normalCompletionUnknown != WorkerClaimReason.None)
            {
                records.Add(
                    CallableClaimResultAssembler.Unknown(
                        target,
                        index,
                        normalCompletionUnknown));
                continue;
            }

            var replayed = outcome is RefutedOutcome refuted
                ? CallableCounterexampleReplayer.Replay(target, index, refuted.Model.Assignments, cancellationToken)
                : WorkerClaimReason.None;
            cancellationToken.ThrowIfCancellationRequested();
            var vacuity = entryFeasibility.IsContradictory
                ? WorkerVacuityKind.ContradictoryPreconditions :
                noModeledNormalReturn ? WorkerVacuityKind.NoModeledNormalReturn : WorkerVacuityKind.None;
            var record = CallableClaimResultAssembler.FromOutcome(
                target,
                index,
                outcome,
                target.Variables,
                assumptionLabels,
                userAssumptionIds,
                replayed,
                vacuity);
            if (record.Outcome == WorkerClaimOutcome.Proven)
            {
                record.ProofCore = MergeProofCore(
                    record.ProofCore,
                    vacuity switch
                    {
                        WorkerVacuityKind.ContradictoryPreconditions =>
                            entryFeasibility.ProofCore,
                        WorkerVacuityKind.NoModeledNormalReturn =>
                            normalCompletionProofCore,
                        _ => []
                    });
                if (vacuity != WorkerVacuityKind.None &&
                    record.ProofCore.Length == 0)
                {
                    record = CallableClaimResultAssembler.Unknown(
                        target,
                        index,
                        WorkerClaimReason.MalformedBackendResult);
                }
                else if (vacuity ==
                         WorkerVacuityKind
                             .ContradictoryPreconditions)
                {
                    record.Assumptions =
                        CallableClaimResultAssembler
                            .MarkAssumptionsUsed(
                                target,
                                entryFeasibility
                                    .UsedAssumptionIds);
                }
            }

            records.Add(record);
        }
        return records.ToImmutable();
    }

    private async Task<CallableEntryFeasibility>
        EvaluateEntryFeasibilityAsync(
            CompilerCallablePreparation target,
            MethodResourceBudget resourceBudget,
            CancellationToken cancellationToken)
    {
        var build = CallableEvidenceBuilder.BuildEntry(
            target,
            _maximumExpressionDepth);
        if (!build.IsSuccess)
        {
            return CallableEntryFeasibility.Unknown(
                build.FailureReason);
        }

        var evidence = build.Evidence!;
        var literalContradiction = evidence.Assumptions.FirstOrDefault(
            static assumption =>
                assumption.Predicate is
                    IrBooleanTerm { Value: false });
        if (literalContradiction != null)
        {
            return CallableEntryFeasibility.Contradictory(
                [evidence.Labels[literalContradiction.Justification]],
                AssumptionIds(
                    [literalContradiction.Justification],
                    evidence.AssumptionIds));
        }

        if (!evidence.HasNontrivialPrecondition)
        {
            return CallableEntryFeasibility.Feasible;
        }

        if (!resourceBudget.TryStartQuery())
        {
            return CallableEntryFeasibility.Unknown(
                WorkerClaimReason.ResourceLimit);
        }

        var factory = target.Factory;
        var query = new VerificationQuery(
            factory,
            evidence.Assumptions,
            new Goal(
                factory,
                factory.Boolean(false),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)),
            evidence.ReplayVariables);
        var outcome = await _kernel.VerifyAsync(
                query,
                cancellationToken)
            .ConfigureAwait(false);
        if (resourceBudget.IsExceeded)
        {
            return CallableEntryFeasibility.Unknown(
                WorkerClaimReason.ResourceLimit);
        }

        return outcome switch
        {
            ProvenOutcome proven =>
                CallableEntryFeasibility.Contradictory(
                    CreateProofCore(proven, evidence.Labels),
                    AssumptionIds(
                        proven.Core,
                        evidence.AssumptionIds)),
            RefutedOutcome => CallableEntryFeasibility.Feasible,
            UnknownOutcome unknown =>
                CallableEntryFeasibility.Unknown(
                    CallableClaimResultAssembler.MapAbstention(
                        unknown.Reason)),
            _ => CallableEntryFeasibility.Unknown(
                WorkerClaimReason.MalformedBackendResult)
        };
    }

    private async Task<ProofOutcome?> ProbeSatisfiabilityAsync(
        IrFactory factory,
        IEnumerable<Assumption> evidence,
        ImmutableArray<IrVarId> replayVariables,
        MethodResourceBudget resourceBudget,
        CancellationToken cancellationToken)
    {
        if (!resourceBudget.TryStartQuery())
        {
            return null;
        }

        var query = new VerificationQuery(
            factory,
            evidence,
            new Goal(
                factory,
                factory.Boolean(false),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)),
            replayVariables);
        var outcome = await _kernel.VerifyAsync(query, cancellationToken)
            .ConfigureAwait(false);
        return resourceBudget.IsExceeded ? null : outcome;
    }

    private static ImmutableArray<string> CreateProofCore(
        ProvenOutcome outcome,
        IReadOnlyDictionary<ProofJustification, string> labels)
    {
        var result = ImmutableArray.CreateBuilder<string>(
            outcome.Core.Length);
        foreach (var justification in outcome.Core)
        {
            if (!labels.TryGetValue(justification, out var label))
            {
                return [];
            }

            result.Add(label);
        }

        return [.. result
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static label => label, StringComparer.Ordinal)];
    }

    private static string[] MergeProofCore(
        IEnumerable<string> left,
        IEnumerable<string> right)
    {
        return [.. left
            .Concat(right)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static label => label, StringComparer.Ordinal)];
    }

    private static IEnumerable<string> AssumptionIds(
        IEnumerable<ProofJustification> proofCore,
        IReadOnlyDictionary<ProofJustification, string>
            assumptionIds)
    {
        return proofCore
            .Select(justification =>
                assumptionIds.TryGetValue(
                    justification,
                    out var assumptionId)
                    ? assumptionId
                    : null)
            .OfType<string>();
    }

    private static SymbolicBodyExecution TrivialBody(IrFactory factory)
    {
        return new(
            WorkerClaimReason.None,
            [new SymbolicReturn(
                factory.Boolean(true),
                null,
                ImmutableDictionary<IrVarId, IrTerm>.Empty)],
            ImmutableDictionary<IrVarId, SpecResultProjection>.Empty,
            []);
    }

}
