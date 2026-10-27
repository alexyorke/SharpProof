using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Fixed integral factor theorem: a real proof is accepted, and an exhausted
// default native query must abstain with exact resource accounting.
[TestFixture]
public sealed class WorkerFactorResourceBoundaryTests
{
    private const string HardSource = """
        using SharpProof.Attributes;

        public static class Subject
        {
            public static int Target(long x, long y)
            {
                Contract.Requires(x >= 2L);
                Contract.Requires(y >= 2L);
                Contract.Requires(x <= 1000000009L);
                Contract.Requires(y <= 1000000009L);
                Contract.Ensures(Contract.Result<int>() == 0);
                return Helper(x, y);
            }

            private static int Helper(long x, long y)
            {
                Contract.Requires(checked(x * y) != 1000000016000000063L ||
                    x == 1000000007L || x == 1000000009L);
                return 0;
            }
        }
        """;
    private const string ControlSource = """
        using SharpProof.Attributes;

        public static class Subject
        {
            public static int Target(long x, long y)
            {
                Contract.Requires(x >= 2L);
                Contract.Requires(y >= 2L);
                Contract.Requires(x <= 1000000009L);
                Contract.Requires(y <= 1000000009L);
                Contract.Requires(x == 1000000007L);
                Contract.Requires(y == 1000000009L);
                Contract.Ensures(Contract.Result<int>() == 0);
                return Helper(x, y);
            }

            private static int Helper(long x, long y)
            {
                Contract.Requires(checked(x * y) != 1000000016000000063L ||
                    x == 1000000007L || x == 1000000009L);
                return 0;
            }
        }
        """;

    [Test]
    public async Task ActualFactorTheoremPreservesProofOrDefaultResourceBoundary()
    {
        await ObserveAsync("easy-control", ControlSource, requireProof: true);
        await ObserveAsync("hard-observation", HardSource, requireProof: false);
    }

    private static async Task ObserveAsync(string label, string source, bool requireProof)
    {
        System.ArgumentNullException.ThrowIfNull(label);
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.CreateExact("FactorTheoremObservation", [("Subject.cs", source)]);
        TestCompilation.AssertNoErrors(compilation);
        using var assembly = new MemoryStream();
        var emitted = compilation.Emit(assembly);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single(static item =>
            item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Entry.CallableId.Contains("Target", StringComparison.Ordinal)));
        Assert.That(preparation.Total, Is.Not.Null, preparation.FailureReason.ToString());
        Assert.That(preparation.Total!.IsBodyAbstraction, Is.False);
        Assert.That(preparation.Total.CallPreconditions, Has.Length.EqualTo(1));
        var instructions = preparation.Total.Program.Blocks.SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).ToImmutableArray();
        Assert.That(instructions.OfType<IrHavocInstruction>()
            .Any(static havoc => havoc is null ? throw new System.ArgumentNullException(nameof(havoc)) : (havoc.Origin == IrHavocOrigin.Approximation)), Is.False);
        Assert.That(instructions.Single(instruction =>
            instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Id == preparation.Total.CallPreconditions[0].Instruction)), Is.TypeOf<IrAssignInstruction>());
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken),
            Is.True, failure.ToString());
        Assert.That(plan, Is.Not.Null);
        Assert.That(plan!.HasBodyAbstraction, Is.False);
        Assert.That(plan.IsBoundedSearch, Is.False);
        Assert.That(plan.CallPreconditionCount, Is.EqualTo(1));
        var budgets = new WorkerBudgets();
        Assert.That(budgets.QueryRlimit, Is.EqualTo(3_000_000));
        Assert.That(budgets.MethodRlimit, Is.EqualTo(20_000_000));
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
        using var solver = new PassiveCallableSolver(plan, budgets.QueryRlimit, budgets.MethodRlimit);
        var beforeEntry = solver.ConsumedResourceCount;
        var entry = await solver.VerifyEntryAsync(cancellationToken: TestContext.CurrentContext.CancellationToken);
        WriteObservation(label, "entry", source, entry, beforeEntry, solver.ConsumedResourceCount);
        Assert.That(entry.Outcome, Is.TypeOf<RefutedOutcome>(), entry.Reason.ToString());
        Assert.That(entry.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(entry.EntryModel, Has.Count.EqualTo(2));
        foreach (var value in entry.EntryModel.Values)
        {
            Assert.That(value.Kind, Is.EqualTo(IrValueKind.Integer));
            Assert.That(value.IntegerNumericValue >= 2 && value.IntegerNumericValue <= 1_000_000_009, Is.True);
        }
        var beforeCall = solver.ConsumedResourceCount;
        var result = await solver.VerifyCallPreconditionAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        WriteObservation(label, "call-precondition", source, result, beforeCall, solver.ConsumedResourceCount);
        if (requireProof || result.Outcome is ProvenOutcome)
        {
            Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(result.Core, Is.Not.Empty);
            var ownedLabels = Enumerable.Range(0, candidate!.Requires.Length)
                .Select(ordinal => "requires:" + ordinal).ToImmutableHashSet(StringComparer.Ordinal);
            Assert.That(result.Core.All(ownedLabels.Contains), Is.True);
        }
        else
        {
            Assert.That(result.Outcome, Is.TypeOf<UnknownOutcome>(), result.Reason.ToString());
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(solver.ConsumedResourceCount - beforeCall, Is.EqualTo((long)budgets.QueryRlimit));
            Assert.That(solver.ConsumedResourceCount, Is.LessThanOrEqualTo((long)budgets.MethodRlimit));
            Assert.That(result.Core, Is.Empty);
        }
        Assert.That(result.EntryModel, Is.Empty);
        Assert.That(result.BodyAssumptions, Is.Empty);
        Assert.That(result.QueryCompleted, Is.False);
        Assert.That(result.ExceptionWitness, Is.Null);
        Assert.That(result.AllocationWitness, Is.Null);
        Assert.That(result.WriteWitness, Is.Null);
        Assert.That(result.LockWitness, Is.Null);
        Assert.That(result.CallPreconditionWitness, Is.Null);
        Assert.That(result.ReceiverWriteWitness, Is.False);
    }

    private static void WriteObservation(string label, string stage, string source,
        PassiveCallableCheckResult result, long before, long after)
    {
        System.ArgumentNullException.ThrowIfNull(label);
        System.ArgumentNullException.ThrowIfNull(result);
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(stage);
        TestContext.Progress.WriteLine("FACTOR-OBSERVATION " + JsonSerializer.Serialize(new
        {
            label,
            stage,
            sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            outcome = result.Outcome?.GetType().Name,
            reason = result.Reason.ToString(),
            result.QueryCompleted,
            core = result.Core,
            model = result.EntryModel.OrderBy(pair => pair.Key.Value).Select(pair => new
            { variable = pair.Key.Value, kind = pair.Value.Kind.ToString(), integer = pair.Value.IntegerNumericValue.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToImmutableArray(),
            bodyAssumptions = result.BodyAssumptions.Select(item => item.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(item)) : (item.Value)).ToImmutableArray(),
            callPreconditionWitness = result.CallPreconditionWitness?.Value,
            consumedBefore = before,
            consumedAfter = after,
            consumedDelta = after - before
        }));
    }
}
