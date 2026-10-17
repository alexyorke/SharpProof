using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerFactorEntryResourceBoundaryTests
{
    [Test]
    public async Task ActualCompanionEntryPreservesContradictionOrDefaultResourceBoundary()
    {
        await ObserveAsync(easyControl: true);
        await ObserveAsync(easyControl: false);
    }

    private static async Task ObserveAsync(bool easyControl)
    {
        var control = easyControl ? "Contract.Requires(x == 1000000007L);" : "";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                [SharpProofTrusted("Factor entry declaration control.")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static extern int Target(long x, long y);
            }
            [ContractFor(typeof(Subject))]
            public static class Specification {
                public static int Target(long x, long y) {
                    Contract.Requires(x >= 2L);
                    Contract.Requires(y >= 2L);
                    Contract.Requires(x <= 1000000009L);
                    Contract.Requires(y <= 1000000009L);
                    Contract.Requires(checked(x * y) == 1000000016000000063L);
                    Contract.Requires(x != 1000000007L);
                    Contract.Requires(x != 1000000009L);
                    {{control}}
                    return 0;
                }
            }
            """;
        var compilation = TestCompilation.Create("FactorCompanionEntryObservation", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var assembly = new MemoryStream();
        var emitted = compilation.Emit(assembly);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: CancellationToken.None), out var preparations, cancellationToken: CancellationToken.None);
        var preparation = preparations.Single(item => item.EffectClaims.Length != 0);
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        Assert.That(preparation.TotalEntry!.Clauses, Has.Length.EqualTo(easyControl ? 8 : 7));
        var evidence = preparation.EffectClaims.Single();
        Assert.That(evidence.Certainty, Is.EqualTo(WorkerEffectEvidenceCertainty.TrustedCompleteBoundary));
        var budgets = new WorkerBudgets();
        Assert.That(budgets.QueryRlimit, Is.EqualTo(3_000_000));
        Assert.That(budgets.MethodRlimit, Is.EqualTo(20_000_000));
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(preparation.TotalEntry.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
        var resource = new MethodResourceBudget(() => session.ConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
        var before = session.ConsumedResourceCount;
        var entry = await TotalCallableVerifier.VerifyEntryAsync(preparation, budgets, CancellationToken.None, session, resource);
        var after = session.ConsumedResourceCount;
        var projected = EffectClaimResultAssembler.Assemble(preparation, evidence, entry, CancellationToken.None);
        await TestContext.Progress.WriteLineAsync("FACTOR-ENTRY-OBSERVATION " + JsonSerializer.Serialize(new
        {
            easyControl,
            sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            entryKind = entry.Kind.ToString(),
            entryReason = entry.Reason.ToString(),
            core = entry.ProofCore.ToArray(),
            usedAssumptions = entry.UsedAssumptionIds.Order().ToArray(),
            projectedOutcome = projected.Outcome.ToString(),
            projectedReason = projected.Reason.ToString(),
            projectedCertainty = projected.EffectCertainty.ToString(),
            projectedVacuity = projected.Vacuity.ToString(),
            consumedBefore = before,
            consumedAfter = after,
            consumedDelta = after - before
        }));
        if (easyControl || entry.IsContradictory)
        {
            Assert.That(entry.IsContradictory, Is.True, entry.Reason.ToString());
            Assert.That(entry.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(entry.ProofCore, Is.Not.Empty);
            var ownedLabels = Enumerable.Range(0, preparation.TotalEntry.Clauses.Length)
                .Select(ordinal => "requires:" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ToHashSet(StringComparer.Ordinal);
            Assert.That(entry.ProofCore.All(ownedLabels.Contains), Is.True);
            var expectedUsed = entry.ProofCore.Select(label => preparation.TotalEntry.Clauses[
                int.Parse(label.AsSpan("requires:".Length), System.Globalization.CultureInfo.InvariantCulture)].AssumptionId!);
            Assert.That(entry.UsedAssumptionIds.SetEquals(expectedUsed), Is.True);
            Assert.That(projected.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(projected.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(projected.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.VacuousEntry));
            Assert.That(projected.Vacuity, Is.EqualTo(WorkerVacuityKind.ContradictoryPreconditions));
            Assert.That(projected.ProofCore, Is.Not.Empty);
        }
        else
        {
            Assert.That(entry.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Unknown));
            Assert.That(entry.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(after - before, Is.EqualTo((long)budgets.QueryRlimit));
            Assert.That(entry.ProofCore, Is.Empty);
            Assert.That(entry.UsedAssumptionIds, Is.Empty);
            Assert.That(projected.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(projected.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(projected.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(projected.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            Assert.That(projected.ProofCore, Is.Empty);
        }
        Assert.That(projected.EffectWitness, Is.Null);
        Assert.That(resource.IsExceeded, Is.False);
        Assert.That(after - before, Is.LessThanOrEqualTo((long)budgets.QueryRlimit));
    }
}
