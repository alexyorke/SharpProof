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

            var predicate = NormalizeDirectClause(
                target,
                clause,
                maximumExpressionDepth);
            if (predicate == null)
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
                WorkerProjections.ClauseLabel(clause.Kind) + ":" +
                assumptionOrdinal.ToString(
                    CultureInfo.InvariantCulture));
            assumptionOrdinal++;
        }

        var uncheckedAssumptionStart = assumptions.Count;
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
            if (!TryGetSummaryPrefix(
                    summaryAssumption.Origin,
                    out var summaryPrefix))
            {
                return CallableEvidenceBuildResult.Fail(
                    WorkerClaimReason.UnsupportedBody);
            }

            // Summary contracts can contain the same result terms as direct
            // specifications. Apply the established result-domain projection
            // before checking/solving them as well.
            var projectedGuard = SpecResultDomainProjection.Rewrite(
                factory,
                summaryAssumption.Guard,
                body.SpecResultProjections);
            var projectedPredicate = SpecResultDomainProjection.Rewrite(
                factory,
                summaryAssumption.Predicate,
                body.SpecResultProjections);
            var predicate = Guard(
                factory,
                projectedGuard,
                projectedPredicate);

            var summaryEvidence = summaryAssumption.Origin ==
                    CompilerSummaryOrigin.SpecificationPack
                ? summaryPrefix + ":" +
                    summaryAssumption.EvidenceIdentity
                : summaryPrefix;
            var dependencyEvidence = BuildDependencyEvidenceLabel(
                summaryAssumption.DependencyEvidence);

            ProofJustification justification = new LoweredJustification(
                factory.CreateOperation(
                    summaryEvidence + ":" +
                    summaryAssumption.EvidenceSha256 +
                    dependencyEvidence));
            assumptions.Add(new Assumption(
                factory,
                predicate,
                justification));
            labels.Add(
                justification,
                summaryEvidence + ":" +
                summaryAssumption.CallIdentity +
                dependencyEvidence);
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
        cancellationToken.ThrowIfCancellationRequested();

        var normalCompletion = AddNormalCompletionAssumption(
            factory,
            body.Returns,
            body.SpecResultProjections,
            assumptions,
            labels);
        for (var index = uncheckedAssumptionStart;
             index < assumptions.Count;
             index++)
        {
            if (GetDepth(assumptions[index].Predicate) >
                maximumExpressionDepth)
            {
                return CallableEvidenceBuildResult.Fail(
                    WorkerClaimReason.UnsupportedExpression);
            }
        }

        var evidence = assumptions.ToImmutable();
        var replayVariables = ReplayVariables(target);
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

    private static IrTerm? NormalizeDirectClause(
        CompilerCallablePreparation target,
        CompilerPreparedClause clause,
        int maximumExpressionDepth)
    {
        var predicate = ApplyBodySubstitutions(
            target.Factory,
            clause.Condition,
            target.Variables,
            null,
            ImmutableDictionary<IrVarId, IrTerm>.Empty,
            allowMissingResult: true);
        return predicate == null || GetDepth(predicate) > maximumExpressionDepth
            ? null
            : predicate;
    }

    private static bool TryGetSummaryPrefix(
        CompilerSummaryOrigin origin,
        out string prefix)
    {
        prefix = CompilerSpecificationPackAuthorityValidation
            .GetSummaryPrefix(origin) ?? string.Empty;
        return prefix.Length != 0;
    }

    private static string BuildDependencyEvidenceLabel(
        ImmutableArray<CompilerPreparedSummaryEvidence> evidence)
    {
        return CompilerDependencyEvidenceFormatter.Format(
            evidence,
            throwOnUnsupportedOrigin: true);
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
                var predicate = NormalizeDirectClause(
                    target,
                    clause,
                    maximumExpressionDepth);
                if (predicate == null ||
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

            var term = factory.Variable(variable.Variable);
            if (!TryCreateSourceDomainPredicate(
                    factory,
                    term,
                    sourceInterval,
                    out var predicate) ||
                predicate != null &&
                (GetDepth(predicate) > maximumExpressionDepth ||
                 !IsSupportedProofDomain(factory, predicate)))
            {
                return CallableEntryEvidenceBuildResult.Fail(
                    WorkerClaimReason.UnsupportedExpression);
            }

            if (predicate != null &&
                predicate is not IrBooleanTerm { Value: true })
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

        var replayVariables = ReplayVariables(target);
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

    private static ImmutableArray<IrVarId> ReplayVariables(
        CompilerCallablePreparation target)
    {
        return target.Variables
            .Where(variable =>
                variable.Role is
                    CompilerVariableRole.Receiver or
                    CompilerVariableRole.Parameter &&
                target.Factory.GetTypeInfo(
                    target.Factory.GetVariableInfo(variable.Variable).Type)
                    .Kind is
                    IrTypeKind.Boolean or
                    IrTypeKind.Integer)
            .Select(static variable => variable.Variable)
            .ToImmutableArray();
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
