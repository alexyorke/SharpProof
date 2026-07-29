using static SharpProof.Worker.PostconditionObligationBuilder;
using static SharpProof.Worker.SymbolicTermOperations;

namespace SharpProof.Worker;

internal static class CallableEvidenceBuilder
{
    internal static CallableEvidenceBuildResult Build(
        CompilerCallablePreparation target,
        SymbolicBodyExecution body,
        int maximumExpressionDepth)
    {
        var factory = target.Factory;
        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        var preconditions = ImmutableArray.CreateBuilder<Assumption>();
        var entryDomainAssumptions =
            ImmutableArray.CreateBuilder<Assumption>();
        var labels = new Dictionary<ProofJustification, string>(
            ReferenceEqualityComparer.Instance);
        var userAssumptionIds = new Dictionary<ProofJustification, string>(
            ReferenceEqualityComparer.Instance);
        var assumptionOrdinal = 0;
        foreach (var clause in target.Clauses)
        {
            if (clause.Kind == CompilerContractKind.Ensures)
            {
                continue;
            }

            var predicate = ApplyBodySubstitutions(
                factory,
                clause.Condition,
                target.Variables,
                null,
                ImmutableDictionary<IrVarId, IrTerm>.Empty,
                allowMissingResult: true);
            if (predicate == null ||
                GetDepth(predicate) > maximumExpressionDepth)
            {
                return CallableEvidenceBuildResult.Fail(
                    WorkerClaimReason.UnsupportedExpression);
            }

            ProofJustification justification =
                clause.Kind == CompilerContractKind.Assume
                    ? new UserAssumedJustification(
                        new SourceLocationId(assumptionOrdinal))
                    : new LoweredJustification(
                        factory.CreateOperation(
                            "contract:" + assumptionOrdinal));
            var assumption = new Assumption(
                factory,
                predicate,
                justification);
            assumptions.Add(assumption);
            if (clause.Kind == CompilerContractKind.Requires)
            {
                preconditions.Add(assumption);
            }
            if (clause.Kind == CompilerContractKind.Assume)
            {
                userAssumptionIds.Add(
                    justification,
                    clause.AssumptionId!);
            }

            labels.Add(
                justification,
                ClauseLabel(clause.Kind) + ":" +
                assumptionOrdinal.ToString(
                    CultureInfo.InvariantCulture));
            assumptionOrdinal++;
        }

        foreach (var specAssumption in body.SpecAssumptions)
        {
            var guard = SpecResultDomainProjection.Rewrite(
                factory,
                specAssumption.Guard,
                body.SpecResultProjections);
            var specPredicate = SpecResultDomainProjection.Rewrite(
                factory,
                specAssumption.Predicate,
                body.SpecResultProjections);
            var predicate = Guard(factory, guard, specPredicate);
            if (GetDepth(predicate) > maximumExpressionDepth)
            {
                return CallableEvidenceBuildResult.Fail(
                    WorkerClaimReason.UnsupportedExpression);
            }

            ProofJustification justification =
                new SpecJustification(specAssumption.Spec);
            assumptions.Add(new Assumption(
                factory,
                predicate,
                justification));
            labels.Add(
                justification,
                "spec:" + specAssumption.WitnessIdentifier);
        }

        if (!TryAddSourceDomainAssumptions(
                factory,
                target.Variables,
                body.Returns,
                body.SpecResultProjections,
                assumptions,
                entryDomainAssumptions,
                labels))
        {
            return CallableEvidenceBuildResult.Fail(
                WorkerClaimReason.UnsupportedExpression);
        }

        var normalCompletion = AddNormalCompletionAssumption(
            factory,
            body.Returns,
            body.SpecResultProjections,
            assumptions,
            labels);
        if (assumptions.Any(assumption =>
                GetDepth(assumption.Predicate) >
                maximumExpressionDepth))
        {
            return CallableEvidenceBuildResult.Fail(
                WorkerClaimReason.UnsupportedExpression);
        }

        var evidence = assumptions.ToImmutable();
        var replayVariables = target.Variables
            .Where(variable =>
                variable.Role is
                    CompilerVariableRole.Receiver or
                    CompilerVariableRole.Parameter &&
                factory.GetTypeInfo(
                    factory.GetVariableInfo(variable.Variable).Type)
                    .Kind is
                    IrTypeKind.Boolean or
                    IrTypeKind.Integer)
            .Select(static variable => variable.Variable)
            .ToImmutableArray();
        return CallableEvidenceBuildResult.Success(new CallableEvidence(
            evidence,
            preconditions.ToImmutable(),
            entryDomainAssumptions.ToImmutable(),
            labels,
            userAssumptionIds,
            normalCompletion,
            replayVariables,
            evidence.All(assumption =>
                IsSupportedProofDomain(
                    factory,
                    assumption.Predicate))));
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

internal sealed record CallableEvidence(
    ImmutableArray<Assumption> Assumptions,
    ImmutableArray<Assumption> Preconditions,
    ImmutableArray<Assumption> EntryDomainAssumptions,
    IReadOnlyDictionary<ProofJustification, string> AssumptionLabels,
    IReadOnlyDictionary<ProofJustification, string> UserAssumptionIds,
    IrTerm NormalCompletion,
    ImmutableArray<IrVarId> ReplayVariables,
    bool UsesSupportedDomain);

internal readonly record struct CallableEvidenceBuildResult(
    CallableEvidence? Evidence,
    WorkerClaimReason FailureReason)
{
    internal bool IsSuccess => Evidence != null;

    internal static CallableEvidenceBuildResult Success(
        CallableEvidence evidence)
    {
        return new(evidence, WorkerClaimReason.None);
    }

    internal static CallableEvidenceBuildResult Fail(
        WorkerClaimReason reason)
    {
        return new(null, reason);
    }
}
