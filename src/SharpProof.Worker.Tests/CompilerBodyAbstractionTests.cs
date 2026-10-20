using System.Collections.Immutable;
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
                var unsupported = 1.5 * x;
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
                var unsupported = 0; System.Threading.Interlocked.Increment(ref unsupported);
                throw null!;
            } }
            """ : Source;
        using var project = new ShadowTestProject(source);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.IsBodyAbstraction, Is.True);
        var checkBuilder = ImmutableDictionary.CreateBuilder<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets,
            check => { System.ArgumentNullException.ThrowIfNull(check); checkBuilder[check.ClaimId] = check; }, TestContext.CurrentContext.CancellationToken);
        var checks = checkBuilder.ToImmutable();
        Assert.That(checks.Values.Count(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Evidence.Outcome is ProvenOutcome)), Is.EqualTo(1));
        var unknown = checks.Values.Single(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Evidence.Outcome is not ProvenOutcome));
        Assert.That(unknown.Evidence.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        Assert.That(unknown.Checked, Is.True);
        Assert.That(checks.Values.Select(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Vacuity)), Is.All.EqualTo(WorkerVacuityKind.None));
        Assert.That(checks.Values.Select(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Feasibility)), Is.All.EqualTo(PassiveCallableFeasibilityKind.Unknown));
        Assert.That(checks.Values.Any(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Evidence.Outcome is RefutedOutcome)), Is.False);
    }

    [TestCase("drop-current")]
    [TestCase("havoc-entry")]
    [TestCase("origin")]
    [TestCase("initial-old")]
    [TestCase("missing-marker")]
    public void AbstractBodyWireCannotNarrowMutableState(string mutation)
    {
        System.ArgumentNullException.ThrowIfNull(mutation);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source);
        var total = artifact.Callables.Single().Total!;
        var instructions = total.Graph.Blocks.Single().Instructions;
        var havoc = instructions.Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Havoc));
        switch (mutation)
        {
            case "missing-marker":
                {
                    total = total with { IsBodyAbstraction = false };
                    artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total }) };
                }
                break;
            case "drop-current":
                {
                    {
                        var changedFixture = total.Graph.ChangeRow(havoc, havoc with { Items = [total.Result] });
                        havoc = changedFixture.Item1;
                        total = total with { Graph = changedFixture.Item2 };
                        artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total }) };
                    }
                }
                break;
            case "havoc-entry":
                {
                    {
                        var changedFixture = total.Graph.ChangeRow(havoc, havoc with { Items = [total.Parameters[0].Entry, total.Result] });
                        havoc = changedFixture.Item1;
                        total = total with { Graph = changedFixture.Item2 };
                        artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total }) };
                    }
                }
                break;
            case "origin":
                {
                    {
                        var changedFixture = total.Graph.ChangeRow(havoc, havoc with { Origin = IrHavocOrigin.Input });
                        havoc = changedFixture.Item1;
                        total = total with { Graph = changedFixture.Item2 };
                        artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total }) };
                    }
                }
                break;
            case "initial-old":
                {
                    total = total with { Graph = total.Graph.ReplaceRow(instructions[1], instructions[1] with { A = total.Parameters[0].Current }) };
                    artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = total }) };
                }
                break;
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [TestCase("object")]
    [TestCase("string")]
    [TestCase("int[]")]
    public void UnknownBodyCannotPreserveUnmodeledHeapObservations(string type)
    {
        // A reference passes through an opaque call exactly; no body
        // abstraction is needed to preserve its heap.
        System.ArgumentNullException.ThrowIfNull(type);
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject { public static {{type}} Target({{type}} x) {
                Contract.Ensures(true); System.Console.WriteLine(x); return x;
            } }
            """);
        Assert.That(project.Snapshot.Callables.Single().Total!.IsBodyAbstraction, Is.False);
    }

    [TestCase("")]
    [TestCase("throw null!;")]
    [TestCase("Value = 1;")]
    public async Task OnlyCapturedEmptyInitializersPermitExactIlReplay(string initialization)
    {
        System.ArgumentNullException.ThrowIfNull(initialization);
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
        var checkBuilder = ImmutableDictionary.CreateBuilder<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        await TotalCallableVerifier.VerifyAsync(project.Snapshot.Callables.Single(), project.Request.Budgets,
            check => { System.ArgumentNullException.ThrowIfNull(check); checkBuilder[check.ClaimId] = check; }, TestContext.CurrentContext.CancellationToken);
        var checks = checkBuilder.ToImmutable();
        Assert.That(checks, Has.Count.EqualTo(2));
        if (string.IsNullOrEmpty(initialization))
        {
            Assert.That(checks.Values.Any(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Evidence.Outcome is ProvenOutcome)), Is.True);
            Assert.That(checks.Values.Any(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Evidence.Outcome is RefutedOutcome)), Is.True);
        }
        else
        { Assert.That(checks.Values.Select(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Evidence.Reason)), Is.All.EqualTo(WorkerClaimReason.CounterexampleNotReplayable)); }
    }
}
