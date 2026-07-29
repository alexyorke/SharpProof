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

        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        var preconditions = ImmutableArray.CreateBuilder<Assumption>();
        var assumptionLabels = new Dictionary<ProofJustification, string>(ReferenceEqualityComparer.Instance);
        var userAssumptionIds = new Dictionary<ProofJustification, string>(ReferenceEqualityComparer.Instance);
        var assumptionOrdinal = 0;
        foreach (var clause in target.Clauses)
        {
            if (clause.Kind == CompilerContractKind.Ensures)
            {
                continue;
            }

            var predicate = ApplyBodySubstitutions(factory, clause.Condition, target.Variables, null,
                ImmutableDictionary<IrVarId, IrTerm>.Empty, allowMissingResult: true);
            if (predicate == null || GetDepth(predicate) > _maximumExpressionDepth)
            {
                return CallableClaimResultAssembler.PostconditionUnknowns(target, WorkerClaimReason.UnsupportedExpression);
            }

            ProofJustification justification = clause.Kind == CompilerContractKind.Assume
                ? new UserAssumedJustification(new SourceLocationId(assumptionOrdinal))
                : new LoweredJustification(factory.CreateOperation("contract:" + assumptionOrdinal));
            var assumption = new Assumption(factory, predicate, justification);
            assumptions.Add(assumption);
            if (clause.Kind == CompilerContractKind.Requires)
            {
                preconditions.Add(assumption);
            }

            if (clause.Kind == CompilerContractKind.Assume)
            {
                userAssumptionIds.Add(justification, clause.AssumptionId!);
            }

            assumptionLabels.Add(justification,
                ClauseLabel(clause.Kind) + ":" + assumptionOrdinal.ToString(CultureInfo.InvariantCulture));
            assumptionOrdinal++;
        }
        foreach (var specAssumption in body.SpecAssumptions)
        {
            var guard = SpecResultDomainProjection.Rewrite(factory, specAssumption.Guard, body.SpecResultProjections);
            var specPredicate = SpecResultDomainProjection.Rewrite(
                factory, specAssumption.Predicate, body.SpecResultProjections);
            var predicate = Guard(factory, guard, specPredicate);
            if (GetDepth(predicate) > _maximumExpressionDepth)
            {
                return CallableClaimResultAssembler.PostconditionUnknowns(target, WorkerClaimReason.UnsupportedExpression);
            }

            ProofJustification justification = new SpecJustification(specAssumption.Spec);
            assumptions.Add(new Assumption(factory, predicate, justification));
            assumptionLabels.Add(justification, "spec:" + specAssumption.WitnessIdentifier);
        }
        if (!TryAddSourceDomainAssumptions(
                factory, target.Variables, body.Returns, body.SpecResultProjections, assumptions, assumptionLabels))
        {
            return CallableClaimResultAssembler.PostconditionUnknowns(target, WorkerClaimReason.UnsupportedExpression);
        }

        var normalCompletion = AddNormalCompletionAssumption(
            factory,
            body.Returns,
            body.SpecResultProjections,
            assumptions,
            assumptionLabels);
        if (assumptions.Any(assumption => GetDepth(assumption.Predicate) > _maximumExpressionDepth))
        {
            return CallableClaimResultAssembler.PostconditionUnknowns(target, WorkerClaimReason.UnsupportedExpression);
        }

        var assumptionsUseSupportedDomain =
            assumptions.All(assumption => IsSupportedProofDomain(factory, assumption.Predicate));
        ImmutableArray<IrVarId> replayVariables = [.. target.Variables.Where(variable =>
            variable.Role is CompilerVariableRole.Receiver or CompilerVariableRole.Parameter &&
            factory.GetTypeInfo(factory.GetVariableInfo(variable.Variable).Type).Kind is
                IrTypeKind.Boolean or IrTypeKind.Integer).Select(static variable => variable.Variable)];
        ProofOutcome? vacuityUnknown = null;
        var contradictoryPreconditions = preconditions.Any(static assumption =>
            assumption.Predicate is IrBooleanTerm { Value: false });
        if (!contradictoryPreconditions && preconditions.Any(static assumption =>
                assumption.Predicate is not IrBooleanTerm { Value: true }))
        {
            var preconditionOutcome = await ProbeSatisfiabilityAsync(
                    factory,
                    preconditions,
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
            if (!assumptionsUseSupportedDomain || !IsSupportedProofDomain(factory, condition))
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

    private static string ClauseLabel(CompilerContractKind kind)
    {
        return kind switch
        {
            CompilerContractKind.Requires => "requires",
            CompilerContractKind.Assume => "assume",
            CompilerContractKind.Ensures => "ensures",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
