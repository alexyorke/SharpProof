using static SharpProof.Worker.PostconditionObligationBuilder;
using static SharpProof.Ir.IrSemanticTerms;
using static SharpProof.Ir.IrTermAnalysis;

namespace SharpProof.Worker;

internal static class CallableEvidenceBuilder
{
    internal static CallableEvidenceBuildResult Build(
        CompilerCallablePreparation target,
        SymbolicBodyExecution body,
        int maximumExpressionDepth,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            cancellationToken.ThrowIfCancellationRequested();
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
            if (clause.Kind is (CompilerContractKind.Requires or
                    CompilerContractKind.Assume) &&
                !string.IsNullOrWhiteSpace(clause.AssumptionId))
            {
                userAssumptionIds.Add(
                    justification,
                    clause.AssumptionId!);
            }

            labels.Add(
                justification,
                WorkerProjections.ClauseLabel(clause.Kind) + ":" +
                assumptionOrdinal.ToString(
                    CultureInfo.InvariantCulture));
            assumptionOrdinal++;
        }

        foreach (var specAssumption in body.SpecAssumptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

        foreach (var summaryAssumption in body.SummaryAssumptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var predicate = Guard(
                factory,
                summaryAssumption.Guard,
                summaryAssumption.Predicate);
            if (GetDepth(predicate) > maximumExpressionDepth)
            {
                return CallableEvidenceBuildResult.Fail(
                    WorkerClaimReason.UnsupportedExpression);
            }

            var summaryLabel = CompilerSummaryProofLabel.Create(
                summaryAssumption.Origin,
                summaryAssumption.CallIdentity,
                summaryAssumption.EvidenceSha256,
                summaryAssumption.EvidenceIdentity,
                summaryAssumption.DependencyEvidence);
            if (summaryLabel.Length == 0)
            {
                return CallableEvidenceBuildResult.Fail(
                    WorkerClaimReason.UnsupportedBody);
            }

            ProofJustification justification = new LoweredJustification(
                factory.CreateOperation(
                    summaryLabel));
            assumptions.Add(new Assumption(
                factory,
                predicate,
                justification));
            labels.Add(
                justification,
                summaryLabel);
        }

        if (!TryAddSourceDomainAssumptions(
                factory,
                target.Variables,
                body.Returns,
                body.SpecResultProjections,
                assumptions,
                entryDomainAssumptions,
                labels,
                userAssumptionIds))
        {
            return CallableEvidenceBuildResult.Fail(
                WorkerClaimReason.UnsupportedExpression);
        }
        cancellationToken.ThrowIfCancellationRequested();

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
        cancellationToken.ThrowIfCancellationRequested();
        var usesSupportedDomain = evidence.All(assumption =>
            IsSupportedProofDomain(
                factory,
                assumption.Predicate));
        cancellationToken.ThrowIfCancellationRequested();
        return CallableEvidenceBuildResult.Success(new CallableEvidence(
            evidence,
            preconditions.ToImmutable(),
            entryDomainAssumptions.ToImmutable(),
            labels,
            userAssumptionIds,
            normalCompletion,
            replayVariables,
            usesSupportedDomain));
    }

    internal static CallableEntryEvidenceBuildResult BuildEntry(
        CompilerCallablePreparation target,
        int maximumExpressionDepth,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var factory = target.Factory;
        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        var labels = new Dictionary<ProofJustification, string>(
            ReferenceEqualityComparer.Instance);
        var assumptionIds =
            new Dictionary<ProofJustification, string>(
                ReferenceEqualityComparer.Instance);
        var seenPredicates = new HashSet<IrId>();
        var assumptionOrdinal = 0;
        var hasNontrivialPrecondition = false;
        foreach (var clause in target.Clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clause.Kind == CompilerContractKind.Ensures)
            {
                continue;
            }

            if (clause.Kind == CompilerContractKind.Requires)
            {
                var predicate = ApplyBodySubstitutions(
                    factory,
                    clause.Condition,
                    target.Variables,
                    null,
                    ImmutableDictionary<IrVarId, IrTerm>.Empty,
                    allowMissingResult: true);
                if (predicate == null ||
                    GetDepth(predicate) > maximumExpressionDepth ||
                    !IsSupportedProofDomain(factory, predicate))
                {
                    return CallableEntryEvidenceBuildResult.Fail(
                        WorkerClaimReason.UnsupportedExpression);
                }

                if (predicate is not IrBooleanTerm { Value: true })
                {
                    hasNontrivialPrecondition = true;
                    Add(
                        predicate,
                        "requires:" +
                        assumptionOrdinal.ToString(
                            CultureInfo.InvariantCulture),
                        clause.AssumptionId);
                }
            }

            assumptionOrdinal++;
        }

        foreach (var variable in target.Variables
                     .Where(static variable =>
                         variable.Role is
                             CompilerVariableRole.Receiver or
                             CompilerVariableRole.Parameter)
                     .OrderBy(static variable =>
                         variable.Role ==
                         CompilerVariableRole.Receiver
                             ? -1
                             : variable.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (variable.SourceIntegerInterval is not { } sourceInterval)
            {
                continue;
            }

            var interval = IntervalDomain.Instance.Range(
                sourceInterval.Minimum,
                sourceInterval.Maximum);
            var term = factory.Variable(variable.Variable);
            if (interval.IsBottom ||
                term.Type != factory.IntegerType ||
                !SpecResultDomainProjection.TryCreateIntervalPredicate(
                    factory,
                    term,
                    interval,
                    out var predicate) ||
                predicate == null ||
                GetDepth(predicate) > maximumExpressionDepth ||
                !IsSupportedProofDomain(factory, predicate))
            {
                return CallableEntryEvidenceBuildResult.Fail(
                    WorkerClaimReason.UnsupportedExpression);
            }

            if (predicate is not IrBooleanTerm { Value: true })
            {
                Add(
                    predicate,
                    variable.Role == CompilerVariableRole.Receiver
                        ? "domain:receiver"
                        : "domain:parameter:" +
                          variable.Ordinal.ToString(
                              CultureInfo.InvariantCulture),
                    assumptionId: null);
            }
        }

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
        cancellationToken.ThrowIfCancellationRequested();
        return CallableEntryEvidenceBuildResult.Success(
            new CallableEntryEvidence(
                assumptions.ToImmutable(),
                labels,
                assumptionIds,
                replayVariables,
                hasNontrivialPrecondition));

        void Add(
            IrTerm predicate,
            string label,
            string? assumptionId)
        {
            if (!seenPredicates.Add(predicate.Id))
            {
                return;
            }

            ProofJustification justification =
                new LoweredJustification(
                    factory.CreateOperation(
                        "entry-feasibility:" + label));
            assumptions.Add(
                new Assumption(factory, predicate, justification));
            labels.Add(justification, label);
            if (!string.IsNullOrWhiteSpace(assumptionId))
            {
                assumptionIds.Add(justification, assumptionId);
            }
        }
    }

}

internal readonly partial record struct CallableEvidenceBuildResult
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
