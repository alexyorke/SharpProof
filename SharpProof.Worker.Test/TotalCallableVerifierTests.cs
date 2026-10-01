using NUnit.Framework;
using SharpProof.Verify;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class TotalCallableVerifierTests
{
    [TestCase("throw", CallableEntryFeasibilityKind.Feasible, PassiveCallableFeasibilityKind.NoModeledNormalReturn)]
    [TestCase("loop", CallableEntryFeasibilityKind.Feasible, PassiveCallableFeasibilityKind.Unknown)]
    [TestCase("contradictory", CallableEntryFeasibilityKind.Contradictory, PassiveCallableFeasibilityKind.ContradictoryEntry)]
    public async Task EntryPublicationDoesNotUseNormalReturnEvidence(string scenario, int entryKind, int normalKind)
    {
        var source = scenario == "loop" ? WorkerVcLoopTests.BeyondSearchSource : """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x) {
                    Contract.Requires(x > 0 && x < 0);
                    Contract.Ensures(false);
                    throw null!;
                }
            }
            """;
        if (scenario == "throw")
        { source = source.Replace("Contract.Requires(x > 0 && x < 0);", "", StringComparison.Ordinal); }
        using var project = new ShadowTestProject(source);
        var preparation = project.Snapshot.Callables.Single();
        CallableEntryFeasibility? entry = null;
        var checks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets,
            check => checks[check.ClaimId] = check, value => entry = value, CancellationToken.None);
        Assert.That(entry, Is.Not.Null);
        Assert.That((int)entry!.Kind, Is.EqualTo(entryKind));
        Assert.That(checks.Values.Select(check => (int)check.Feasibility), Is.All.EqualTo(normalKind));
        if (scenario == "contradictory")
        {
            Assert.That(entry.ProofCore, Is.Not.Empty);
            Assert.That(entry.UsedAssumptionIds, Is.EquivalentTo(preparation.Entry.Assumptions.Select(assumption => assumption.Id)));
        }
        else
        { Assert.That(entry.ProofCore, Is.Empty); }
    }

    [Test]
    public void EntryPublicationSurvivesCancellationBeforeNormalQuery()
    {
        using var project = new ShadowTestProject(CompilerTotalCallableArtifactTests.DiamondSource);
        using var cancellation = new CancellationTokenSource();
        CallableEntryFeasibility? entry = null;
        var checks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            await TotalCallableVerifier.VerifyAsync(project.Snapshot.Callables.Single(), project.Request.Budgets,
                check => checks[check.ClaimId] = check,
                value => { entry = value; cancellation.Cancel(); }, cancellation.Token)));
        Assert.That(entry!.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Feasible));
        Assert.That(checks.Values.All(check => !check.Checked), Is.True);
    }

    [Test]
    public async Task LegacyLoweringFailureDoesNotSuppressTypedVerification()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static ulong Target(ulong x) {
                    Contract.Requires(x == 18446744073709551615UL);
                    Contract.Ensures(Contract.Result<ulong>() == 0UL);
                    return unchecked(x + 1UL);
                }
            }
            """);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.IsSuccess, Is.False);
        Assert.That(preparation.Total, Is.Not.Null);
        var checks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets,
            check => checks[check.ClaimId] = check, CancellationToken.None);
        var result = checks.Values.Single();
        Assert.That(result.Enrolled, Is.True);
        Assert.That(result.Checked, Is.True);
        Assert.That(result.Evidence.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(result.Feasibility, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That(result.Assumptions.Single().Used, Is.True);
    }

    [Test]
    public async Task RefutationPublishesCanonicalTotalEntryValues()
    {
        using var project = new ShadowTestProject(CompilerTotalCallableArtifactTests.DiamondSource);
        var preparation = project.Snapshot.Callables.Single();
        var checks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets,
            check => checks[check.ClaimId] = check, CancellationToken.None);
        var result = checks.Values.Single(check => check.Evidence.Outcome is RefutedOutcome);
        Assert.That(result.Checked, Is.True);
        Assert.That(result.Evidence.EntryModel.Keys,
            Is.EquivalentTo(preparation.Total!.Parameters.Select(parameter => parameter.Entry)));
        Assert.That(checks.Values.Count(check => check.Evidence.Outcome is ProvenOutcome), Is.EqualTo(1));
    }

    [Test]
    public void CancellationAfterFirstProofPreservesPublishedEvidence()
    {
        using var project = new ShadowTestProject(CompilerTotalCallableArtifactTests.DiamondSource);
        var checks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            await TotalCallableVerifier.VerifyAsync(project.Snapshot.Callables.Single(), project.Request.Budgets,
                check =>
                {
                    checks[check.ClaimId] = check;
                    if (check.Checked)
                    { cancellation.Cancel(); }
                }, cancellation.Token)));
        Assert.That(checks.Values.Count(check => check.Checked), Is.EqualTo(1));
        Assert.That(checks.Values.Single(check => check.Checked).Evidence.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(checks.Values.Single(check => !check.Checked).Enrolled, Is.True);
    }
}
