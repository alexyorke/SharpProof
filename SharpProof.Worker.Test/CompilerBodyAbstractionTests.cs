using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerBodyAbstractionTests
{
    private const string Source = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x >= 0);
                Contract.Ensures(Contract.Result<int>() == Contract.Result<int>());
                Contract.Ensures(Contract.Result<int>() == x);
                System.Console.WriteLine(x);
                throw null!;
            }
        }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task AbstractBodyCanProveButCannotInventANormalReturnOrRefutation(bool emptyVoid)
    {
        var source = emptyVoid ? """
            using SharpProof.Attributes;
            public static class Subject { public static void Target() {
                Contract.Ensures(true);
                Contract.Ensures(false);
                System.Console.WriteLine(1);
                throw null!;
            } }
            """ : Source;
        using var project = new ShadowTestProject(source);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.IsBodyAbstraction, Is.True);
        var checks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets,
            check => checks[check.ClaimId] = check, CancellationToken.None);
        Assert.That(checks.Values.Count(check => check.Evidence.Outcome is ProvenOutcome), Is.EqualTo(1));
        var unknown = checks.Values.Single(check => check.Evidence.Outcome is not ProvenOutcome);
        Assert.That(unknown.Evidence.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        Assert.That(unknown.Checked, Is.True);
        Assert.That(checks.Values.Select(check => check.Vacuity), Is.All.EqualTo(WorkerVacuityKind.None));
        Assert.That(checks.Values.Select(check => check.Feasibility), Is.All.EqualTo(PassiveCallableFeasibilityKind.Unknown));
        Assert.That(checks.Values.Any(check => check.Evidence.Outcome is RefutedOutcome), Is.False);
    }

    [TestCase("drop-current")]
    [TestCase("havoc-entry")]
    [TestCase("origin")]
    [TestCase("initial-old")]
    [TestCase("missing-marker")]
    public void AbstractBodyWireCannotNarrowMutableState(string mutation)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source);
        var total = artifact.Callables.Single().Total!;
        var instructions = total.Graph.Blocks.Single().Instructions;
        var havoc = instructions.Single(instruction => instruction.Kind == IrInstructionKind.Havoc);
        switch (mutation)
        {
            case "missing-marker":
                total.IsBodyAbstraction = false;
                break;
            case "drop-current":
                havoc.Items = [total.Result];
                break;
            case "havoc-entry":
                havoc.Items = [total.Parameters[0].Entry, total.Result];
                break;
            case "origin":
                havoc.Origin = IrHavocOrigin.Input;
                break;
            case "initial-old":
                instructions[1].A = total.Parameters[0].Current;
                break;
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    [TestCase("object")]
    [TestCase("string")]
    [TestCase("int[]")]
    public void UnknownBodyCannotPreserveUnmodeledHeapObservations(string type)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject { public static {{type}} Target({{type}} x) {
                Contract.Ensures(true); System.Console.WriteLine(x); return x;
            } }
            """);
        Assert.That(project.Snapshot.Callables.Single().Total, Is.Null);
    }

    [TestCase("")]
    [TestCase("throw null!;")]
    [TestCase("Value = 1;")]
    public async Task OnlyCapturedEmptyInitializersPermitExactIlReplay(string initialization)
    {
        using var metadata = new MetadataTestSubject($$"""
            public static class Library {
                private static int Value;
                static Library() { {{initialization}} }
                public static int Target(int value) { return value; }
            }
            """, """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) {
                Contract.Ensures(Contract.Result<int>() == x);
                Contract.Ensures(Contract.Result<int>() != x);
                return Library.Target(x);
            } }
            """);
        using var project = new ShadowTestProject(metadata.CreateArtifact());
        var checks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        await TotalCallableVerifier.VerifyAsync(project.Snapshot.Callables.Single(), project.Request.Budgets,
            check => checks[check.ClaimId] = check, CancellationToken.None);
        if (string.IsNullOrEmpty(initialization))
        {
            Assert.That(checks.Values.Any(check => check.Evidence.Outcome is ProvenOutcome), Is.True);
            Assert.That(checks.Values.Any(check => check.Evidence.Outcome is RefutedOutcome), Is.True);
        }
        else
        { Assert.That(checks.Values.Select(check => check.Evidence.Reason), Is.All.EqualTo(WorkerClaimReason.CounterexampleNotReplayable)); }
    }
}
