using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ActualCompilerEntryOwnershipAdmissionTests
{
    [Test]
    public async Task EntryAdmissionRejectsOtherActualCompilerOwnedEntryWithoutRebinding()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int First(int value) { Contract.Requires(value >= 0); Contract.Ensures(Contract.Result<int>() == value); return value; }
                public static int Second(int value) { Contract.Requires(value <= 0); Contract.Ensures(Contract.Result<int>() == value); return value; }
            }
            """;
        var compilation = TestCompilation.Create("ActualEntryOwnershipAdmission", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var targets = project.Snapshot.Callables.ToArray();
        Assert.That(targets, Has.Length.EqualTo(2));
        Assert.That(targets[0].Entry.CallableId, Is.Not.EqualTo(targets[1].Entry.CallableId));
        foreach (var target in targets)
        {
            Assert.That(target.TotalEntry, Is.Not.Null);
            Assert.That(target.TotalEntry!.CallableId, Is.EqualTo(target.Entry.CallableId));
            Assert.DoesNotThrow(new Action(() => TotalCallableVerifier.ValidateEntryOwner(target, target.TotalEntry!)));
            var actual = await TotalCallableVerifier.VerifyEntryAsync(target, project.Request.Budgets, TestContext.CurrentContext.CancellationToken);
            Assert.That(actual.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Feasible));
            Assert.That(actual.Reason, Is.EqualTo(WorkerClaimReason.None));
        }
        // Wrong actual owner at the raw validator only: no preparation constructed, rebound or enrolled.
        foreach (var (target, other) in new[] { (targets[0], targets[1]), (targets[1], targets[0]) })
        {
            var exception = Assert.Throws<ArgumentException>(new Action(() =>
                TotalCallableVerifier.ValidateEntryOwner(target, other.TotalEntry!)));
            Assert.That(exception!.ParamName, Is.EqualTo("preparation"));
            Assert.That(exception.Message, Does.StartWith("The entry preparation belongs to another callable."));
            Assert.That(target.TotalEntry!.CallableId, Is.EqualTo(target.Entry.CallableId));
            Assert.That(other.TotalEntry!.CallableId, Is.EqualTo(other.Entry.CallableId));
        }
    }
}
