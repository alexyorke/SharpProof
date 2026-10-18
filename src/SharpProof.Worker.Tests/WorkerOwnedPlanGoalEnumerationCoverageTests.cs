using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task OwnedPlanEnumeratesEveryActualGoalAndRetainsRefutedControls(bool broken)
    {
        var returned = broken ? "-value" : "value + 1";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value >= 1 && value <= 9);
                    Contract.Ensures(Contract.Result<int>() > 0);
                    Contract.Ensures(Contract.Result<int>() >= value);
                    return {{returned}};
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single();
        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        Assert.That(preparation.Total, Is.Not.Null);
        var total = preparation.Total!;
        Assert.That(total.IsBodyAbstraction, Is.False);
        Assert.That(total.EffectsCompleteAtEntry, Is.True);
        Assert.That(total.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrHavocInstruction>()
            .Any(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Origin == IrHavocOrigin.Approximation)), Is.False);
        Assert.That(total.Clauses.Count(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Requires)), Is.EqualTo(1));
        var actualGoals = total.Clauses.Where(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Ensures)).ToArray();
        Assert.That(actualGoals, Has.Length.EqualTo(2));
        Assert.That(actualGoals.Select(goal => goal is null ? throw new System.ArgumentNullException(nameof(goal)) : (goal.ClaimId)), Is.EquivalentTo(preparation.Entry.ClaimIds));
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True,
            failure.ToString());
        Assert.That(plan!.CallableId, Is.EqualTo(preparation.Entry.CallableId));
        Assert.That(plan.EnsuresCount, Is.EqualTo(actualGoals.Length));
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var solver = new PassiveCallableSolver(plan);
        var checkedIds = new List<string>();
        for (var ordinal = 0; ordinal < plan.EnsuresCount; ordinal++)
        {
            var checkedGoal = await solver.VerifyEnsuresAsync(ordinal, cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(checkedGoal.Outcome, broken ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>(),
                checkedGoal.Reason.ToString());
            Assert.That(checkedGoal.Reason, Is.EqualTo(WorkerClaimReason.None));
            if (broken)
            { Assert.That(checkedGoal.EntryModel, Is.Not.Empty); }
            else
            { Assert.That(checkedGoal.Core, Is.Not.Empty); }
            checkedIds.Add(actualGoals[ordinal].ClaimId!);
        }
        Assert.That(checkedIds, Is.EquivalentTo(preparation.Entry.ClaimIds));
        Assert.That(checkedIds.Distinct().Count(), Is.EqualTo(plan.EnsuresCount));

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
            Assert.That(response.ClaimResults.Select(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.ClaimId)).Distinct().Count(), Is.EqualTo(2));
            Assert.That(response.ClaimResults.All(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.Outcome == (broken
                ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven) &&
                result.Reason == WorkerClaimReason.None && result.Vacuity == WorkerVacuityKind.None)), Is.True);
            Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
        Assert.That(response.ClaimResults.Select(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (GetCallableId(response, result))).Distinct().Count(), Is.EqualTo(1));
    }
}
