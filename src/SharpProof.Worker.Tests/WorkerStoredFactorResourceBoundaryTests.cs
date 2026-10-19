using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Host;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// An actual stored-factor search preserves a proof or the default native
// search-query resource boundary with compiler-owned plans and source.
[TestFixture]
public sealed class WorkerStoredFactorResourceBoundaryTests
{
    private const string HardSource = """
        using SharpProof.Attributes;

        public static class Subject
        {
            public static long Target(long startX, long startY)
            {
                Contract.Requires(startX >= 2L);
                Contract.Requires(startY >= 2L);
                Contract.Requires(startX <= 1000000009L);
                Contract.Requires(startY <= 1000000009L);
                Contract.Ensures(Contract.Result<long>() == 0L ||
                    Contract.Result<long>() == 1000000007L || Contract.Result<long>() == 1000000009L);
                long factor = 0L;
                long x = startX;
                while (x <= 1000000009L && factor == 0L)
                {
                    long y = startY;
                    while (y <= 1000000009L && factor == 0L)
                    {
                        if (checked(x * y) == 1000000016000000063L)
                        {
                            factor = x;
                        }
                        y++;
                    }
                    x++;
                }
                return Helper(factor);
            }

            private static long Helper(long value)
            {
                Contract.Requires(value == 0L || value == 1000000007L || value == 1000000009L);
                return value;
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
    public async Task ActualFactorSearchPreservesProofOrDefaultSearchResourceBoundary()
    {
        await ObserveAsync("easy-control", ControlSource, requireProof: true);
        await ObserveAsync("hard-observation", HardSource, requireProof: false);
    }

    private static async Task ObserveAsync(string label, string source, bool requireProof)
    {
        System.ArgumentNullException.ThrowIfNull(label);
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("FactorTheoremObservation", ("Subject.cs", source));
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
        if (!requireProof)
        {
            Assert.That(plan.LoopSearch, Is.Not.Null);
            Assert.That(plan.Candidate, Is.SameAs(candidate));
            Assert.That(plan.LoopSearch!.Candidate, Is.SameAs(candidate));
            Assert.That(plan.LoopSearch.Factory, Is.SameAs(plan.Factory));
            var proofQueries = plan.CallPreconditionQueries(0);
            var searchQueries = plan.LoopSearch.CallPreconditionQueries(0);
            Assert.That(proofQueries, Is.Not.Empty);
            Assert.That(searchQueries, Is.Not.Empty);
            await TestContext.Progress.WriteLineAsync("FACTOR-PLAN-OBSERVATION " + JsonSerializer.Serialize(new
            {
                sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
                proofIsBoundedSearch = plan.IsBoundedSearch,
                searchIsBoundedSearch = plan.LoopSearch.IsBoundedSearch,
                proofQueryCount = proofQueries.Length,
                searchQueryCount = searchQueries.Length,
                proofAssumptionCounts = proofQueries.Select(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (query.Assumptions.Length)).ToImmutableArray(),
                searchAssumptionCounts = searchQueries.Select(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (query.Assumptions.Length)).ToImmutableArray(),
                proofModelCounts = proofQueries.Select(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (query.ModelVariables.Length)).ToImmutableArray(),
                searchModelCounts = searchQueries.Select(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (query.ModelVariables.Length)).ToImmutableArray()
            }));
        }
        var budgets = new WorkerBudgets();
        Assert.That(budgets.QueryRlimit, Is.EqualTo(3_000_000));
        Assert.That(budgets.MethodRlimit, Is.EqualTo(20_000_000));
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(plan.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
        var resourceReads = new List<long>();
        long ReadActualResources()
        {
            var consumed = session.ConsumedResourceCount;
            resourceReads.Add(consumed);
            return consumed;
        }
        var resourceBudget = new MethodResourceBudget(ReadActualResources, budgets.QueryRlimit, budgets.MethodRlimit);
        using var solver = new PassiveCallableSolver(plan, session, resourceBudget, () => session.ConsumedResourceCount);
        var beforeEntry = solver.ConsumedResourceCount;
        var entry = await solver.VerifyEntryAsync(cancellationToken: TestContext.CurrentContext.CancellationToken);
        WriteObservation(label, "entry", source, entry, beforeEntry, solver.ConsumedResourceCount);
        Assert.That(entry.Outcome, Is.TypeOf<RefutedOutcome>(), entry.Reason.ToString());
        Assert.That(entry.Reason, Is.EqualTo(WorkerClaimReason.None));
        var beforeCall = solver.ConsumedResourceCount;
        resourceReads.Clear();
        var result = await solver.VerifyCallPreconditionAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var callResourceReads = resourceReads.ToImmutableArray();
        WriteObservation(label, "call-precondition", source, result, beforeCall, solver.ConsumedResourceCount);
        if (requireProof || result.Outcome is ProvenOutcome)
        {
            Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString());
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(result.Core, Is.Not.Empty);
            var ownedJustifications = plan.CallPreconditionQueries(0)
                .SelectMany(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (query.Assumptions)).Select(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Justification)).ToHashSet();
            Assert.That(((ProvenOutcome)result.Outcome!).Core.All(ownedJustifications.Contains), Is.True);
        }
        else
        {
            Assert.That(result.Outcome, Is.TypeOf<UnknownOutcome>(), result.Reason.ToString());
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(callResourceReads.Length % 2, Is.Zero);
            var queryDeltas = Enumerable.Range(0, callResourceReads.Length / 2)
                .Select(ordinal => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (callResourceReads[ordinal * 2 + 1] - callResourceReads[ordinal * 2]))).ToImmutableArray();
            var cutQueryCount = plan.CallPreconditionQueries(0).Length;
            Assert.That(queryDeltas.Length, Is.GreaterThan(cutQueryCount));
            Assert.That(queryDeltas.Length, Is.LessThanOrEqualTo(cutQueryCount + plan.LoopSearch!.CallPreconditionQueries(0).Length));
            Assert.That(queryDeltas.All(delta => delta >= 0 && delta <= budgets.QueryRlimit), Is.True);
            Assert.That(queryDeltas[^1], Is.EqualTo((long)budgets.QueryRlimit));
            Assert.That(solver.ConsumedResourceCount, Is.LessThanOrEqualTo((long)budgets.MethodRlimit));
            Assert.That(resourceBudget.IsExceeded, Is.False);
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
