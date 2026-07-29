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
        ArgumentNullException.ThrowIfNull(resourceBudget);
        cancellationToken.ThrowIfCancellationRequested();
        if (!target.IsSuccess)
        {
            return CallableClaimResultAssembler.PostconditionUnknowns(target, target.FailureReason);
        }

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
        var preconditions = evidence.Preconditions;
        ImmutableArray<Assumption> preconditionEvidence = [
            .. preconditions,
            .. evidence.EntryDomainAssumptions
        ];
        var assumptionLabels = evidence.AssumptionLabels;
        var userAssumptionIds = evidence.UserAssumptionIds;
        var normalCompletion = evidence.NormalCompletion;
        var replayVariables = evidence.ReplayVariables;
        ProofOutcome? vacuityUnknown = null;
        var contradictoryPreconditions = preconditionEvidence.Any(static assumption =>
            assumption.Predicate is IrBooleanTerm { Value: false });
        if (!contradictoryPreconditions && preconditions.Any(static assumption =>
                assumption.Predicate is not IrBooleanTerm { Value: true }))
        {
            var preconditionOutcome = await ProbeSatisfiabilityAsync(
                    factory,
                    preconditionEvidence,
                    replayVariables,
                    resourceBudget,
                    cancellationToken)
                .ConfigureAwait(false);
            if (preconditionOutcome == null)
            {
                return CallableClaimResultAssembler.PostconditionUnknowns(target, WorkerClaimReason.ResourceLimit);
            }

            contradictoryPreconditions = preconditionOutcome is ProvenOutcome;
            if (preconditionOutcome is not (ProvenOutcome or RefutedOutcome))
            {
                vacuityUnknown = preconditionOutcome;
            }
        }
        var noModeledNormalReturn = normalCompletion is IrBooleanTerm { Value: false };
        if (!contradictoryPreconditions && vacuityUnknown == null && normalCompletion is not IrBooleanTerm)
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
            if (completionOutcome is not (ProvenOutcome or RefutedOutcome))
            {
                vacuityUnknown = completionOutcome;
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
            if (outcome is ProvenOutcome && vacuityUnknown != null)
            {
                outcome = vacuityUnknown;
            }

            var replayed = outcome is RefutedOutcome refuted
                ? CallableCounterexampleReplayer.Replay(target, index, refuted.Model.Assignments, cancellationToken)
                : WorkerClaimReason.None;
            cancellationToken.ThrowIfCancellationRequested();
            var vacuity = contradictoryPreconditions ? WorkerVacuityKind.ContradictoryPreconditions :
                noModeledNormalReturn ? WorkerVacuityKind.NoModeledNormalReturn : WorkerVacuityKind.None;
            records.Add(CallableClaimResultAssembler.FromOutcome(target, index, outcome, target.Variables,
                assumptionLabels, userAssumptionIds, replayed, vacuity));
        }
        return records.ToImmutable();
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
