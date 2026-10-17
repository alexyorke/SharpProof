using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.Attributes;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ActualOwnedSessionEntryCompletionTests
{
    private static readonly object[] RuntimeControlArguments = [2L, 3L];
    [Test]
    public async Task EntryCompletionReadsActualOverdrawnRawNativeSession()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Target(long x, long y) {
                    Contract.Requires(x >= 2L);
                    Contract.Requires(y >= 2L);
                    Contract.Requires(x <= 1000000033L);
                    Contract.Requires(y <= 1000000033L);
                    Contract.Ensures(Contract.Result<long>() == x);
                    return x;
                }
            }
            """;
        var compilation = TestCompilation.Create("OwnedSessionEntryCompletion", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        // Same source/text paths; supported Conditional contract calls are
        // absent only from this bounded CLR control image, not native artifact.
        var runtimeTrees = compilation.SyntaxTrees.Select(tree => CSharpSyntaxTree.ParseText(tree.GetText(),
            ((CSharpParseOptions)tree.Options).WithPreprocessorSymbols(
                ((CSharpParseOptions)tree.Options).PreprocessorSymbolNames.Where(symbol => symbol != Contract.ConditionalSymbol)), tree.FilePath));
        var runtimeCompilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(runtimeTrees);
        TestCompilation.AssertNoErrors(runtimeCompilation);
        using var runtimeImage = new MemoryStream();
        Assert.That(runtimeCompilation.Emit(runtimeImage).Success, Is.True);
        var runtimeTarget = Assembly.Load(runtimeImage.ToArray()).GetType("Subject")!.GetMethod("Target")!;
        Assert.That(runtimeTarget.Invoke(null, RuntimeControlArguments), Is.EqualTo(2L));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var artifactJson = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(artifactJson, out var preparations, cancellationToken: CancellationToken.None);
        var preparation = preparations.Single();
        var entry = preparation.TotalEntry!;
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry.Parameters, Has.Length.EqualTo(2));
        Assert.That(entry.Clauses, Has.Length.EqualTo(4));
        var budgets = new WorkerBudgets();
        Assert.That(budgets.QueryRlimit, Is.EqualTo(3_000_000));
        Assert.That(budgets.MethodRlimit, Is.EqualTo(20_000_000));
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
        Assert.That(budgets.MethodWallTimeMilliseconds, Is.EqualTo(10_000));
        Assert.That(budgets.ProjectWallTimeMilliseconds, Is.EqualTo(300_000));
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(entry.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
        var factory = entry.Factory;
        var x = factory.Variable(entry.Parameters[0].Entry);
        var y = factory.Variable(entry.Parameters[1].Entry);
        Assert.That(factory.GetTypeInfo(x.Type).Width, Is.EqualTo(64));
        Assert.That(factory.GetTypeInfo(x.Type).Signed, Is.True);
        Assert.That(y.Type, Is.EqualTo(x.Type));
        var labels = new Dictionary<ProofJustification, string>();
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var domainBuilder = ImmutableArray.CreateBuilder<Assumption>(entry.Clauses.Length);
        foreach (var clause in entry.Clauses)
        {
            var label = "requires:" + domainBuilder.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var assumption = new Assumption(factory, factory.Binary(IrBinaryOperator.AndAlso, clause.Safe, clause.Value),
                new LoweredJustification(clause.Operation));
            domainBuilder.Add(assumption);
            labels.Add(assumption.Justification, label);
            ids.Add(label, clause.AssumptionId!);
        }
        var domain = domainBuilder.MoveToImmutable();
        var entryQuery = new VerificationQuery(factory, domain, Goal.CreateInternalConsistency(factory),
            [.. entry.Parameters.Select(parameter => parameter.Entry)]);
        var kernel = new ProofKernel(session);
        var controlMeter = new MethodResourceBudget(() => session.ConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
        var controlOutcome = await kernel.VerifyAsync(entryQuery, CancellationToken.None);
        Assert.That(controlOutcome, Is.TypeOf<RefutedOutcome>());
        var control = TotalCallableVerifier.CompleteEntry(controlOutcome, controlMeter, labels, ids);
        Assert.That(control.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Feasible));
        Assert.That(control.Reason, Is.EqualTo(WorkerClaimReason.None));
        var batchStart = session.ConsumedResourceCount;
        var meter = new MethodResourceBudget(() => session.ConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
        long[] primes = [1000000007L, 1000000009L, 1000000021L, 1000000033L];
        const long maximumInput = 1000000033L;
        foreach (var prime in primes)
        {
            Assert.That(prime, Is.InRange(2L, maximumInput));
            Assert.That(IsPrime(prime), Is.True, prime.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var maximumProduct = (BigInteger)maximumInput * maximumInput;
        Assert.That(maximumProduct, Is.LessThanOrEqualTo(new BigInteger(long.MaxValue)));
        var startedQueries = 0;
        long observedBatchDelta = 0;
        (int First, int Second)[] lemmaPairs = [(0, 1), (0, 2), (0, 3), (1, 2), (1, 3), (2, 3), (0, 0), (1, 1)];
        Assert.That(lemmaPairs.Distinct().Count(), Is.EqualTo(8));
        // Eight distinct mathematical API obligations: six semiprimes and two
        // prime squares. This raw session API has only per-query native caps;
        // warm work deliberately has no Total method-admission authority.
        foreach (var (first, second) in lemmaPairs)
        {
            var p = primes[first];
            var q = primes[second];
            var product = checked(p * q);
            Assert.That((BigInteger)p * q, Is.EqualTo(new BigInteger(product)));
            Assert.That(new BigInteger(product), Is.InRange(new BigInteger(4), maximumProduct));
            var constraints = new List<IrTerm>
            {
                factory.Binary(IrBinaryOperator.Equal,
                    factory.Binary(IrBinaryOperator.Multiply, x, y), factory.Integer(x.Type, product)),
                factory.Binary(IrBinaryOperator.NotEqual, x, factory.Integer(x.Type, p))
            };
            if (p != q)
            { constraints.Add(factory.Binary(IrBinaryOperator.NotEqual, x, factory.Integer(x.Type, q))); }
            Assert.That(runtimeTarget.Invoke(null, [p, 2L]), Is.EqualTo(p));
            // Manually constructed IR/Smt API lemmas use compiler-decoded
            // bindings. They are not compiler-produced contract clauses.
            var assumptions = domain.AddRange(constraints.Select(predicate =>
                new Assumption(factory, predicate, new LoweredJustification(factory.CreateOperation()))));
            var query = new VerificationQuery(factory, assumptions,
                Goal.CreateInternalConsistency(factory), [.. entry.Parameters.Select(parameter => parameter.Entry)]);
            var before = session.ConsumedResourceCount;
            startedQueries++;
            var outcome = await kernel.VerifyAsync(query, CancellationToken.None);
            var delta = session.ConsumedResourceCount - before;
            Assert.That(delta, Is.GreaterThan(0));
            observedBatchDelta = checked(observedBatchDelta + delta);
            if (outcome is ProvenOutcome proven)
            {
                Assert.That(proven.Core, Is.Not.Empty);
                Assert.That(proven.Core.All(core => assumptions.Any(assumption => assumption.Justification == core)), Is.True);
            }
            else
            {
                Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
                Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(AbstentionReason.ResourceLimit));
            }
            await TestContext.Progress.WriteLineAsync("OWNED-SESSION-LEMMA " + JsonSerializer.Serialize(new
            {
                p,
                q,
                outcome = outcome.GetType().Name,
                reason = outcome is UnknownOutcome unknown ? unknown.Reason.ToString() : "None",
                core = outcome is ProvenOutcome proof ? proof.Core.Cast<LoweredJustification>().Select(core => core.Operation.Value).ToArray() : [],
                delta,
                consumed = session.ConsumedResourceCount
            }));
        }
        var beforeEntry = session.ConsumedResourceCount;
        var spent = beforeEntry - batchStart;
        Assert.That(spent, Is.EqualTo(observedBatchDelta));
        Assert.That(startedQueries, Is.EqualTo(8));
        // Final entry is a new actual query over the same genuine factory,
        // bindings, assumption objects and parameter identities, not old output.
        var finalOutcome = await kernel.VerifyAsync(entryQuery, CancellationToken.None);
        Assert.That(finalOutcome, Is.TypeOf<RefutedOutcome>());
        var afterEntry = session.ConsumedResourceCount;
        Assert.That(afterEntry, Is.GreaterThan(beforeEntry));
        var exceeded = checked(afterEntry - batchStart) > budgets.MethodRlimit;
        Assert.That(meter.IsExceeded, Is.EqualTo(exceeded));
        var result = TotalCallableVerifier.CompleteEntry(finalOutcome, meter, labels, ids);
        Assert.That(result.Kind, Is.EqualTo(exceeded ? CallableEntryFeasibilityKind.Unknown : CallableEntryFeasibilityKind.Feasible));
        Assert.That(result.Reason, Is.EqualTo(exceeded ? WorkerClaimReason.ResourceLimit : WorkerClaimReason.None));
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(result.UsedAssumptionIds, Is.Empty);
        Assert.That(session.ConsumedResourceCount, Is.EqualTo(afterEntry));
        await TestContext.Progress.WriteLineAsync("OWNED-SESSION-ENTRY " + JsonSerializer.Serialize(new
        {
            sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
            artifactSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artifactJson))),
            imageSha256 = Convert.ToHexString(SHA256.HashData(image.ToArray())),
            controlKind = control.Kind.ToString(),
            controlReason = control.Reason.ToString(),
            algorithm = "Manually constructed IR/Smt API lemmas over compiler-decoded bindings",
            primeChecks = primes.Select(prime => new
            {
                prime,
                isPrime = IsPrime(prime)
            }).ToArray(),
            maximumProduct = maximumProduct.ToString(System.Globalization.CultureInfo.InvariantCulture),
            startedQueries,
            observedBatchDelta,
            batchStart,
            spent,
            exceeded,
            kind = result.Kind.ToString(),
            reason = result.Reason.ToString(),
            beforeEntry,
            afterEntry,
            finalOutcome = finalOutcome.GetType().Name,
            runtimeImageSha256 = Convert.ToHexString(SHA256.HashData(runtimeImage.ToArray()))
        }));
    }

    private static bool IsPrime(long value)
    {
        if (value < 2 || value % 2 == 0)
        {
            return value == 2;
        }
        for (long divisor = 3; divisor <= value / divisor; divisor += 2)
        {
            if (value % divisor == 0)
            {
                return false;
            }
        }
        return true;
    }
}
