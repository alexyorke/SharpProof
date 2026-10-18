using System.Collections.Immutable;
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
public sealed class ActualOwnedSessionEntryReservationTests
{
    [Test]
    public async Task EntryReservationReadsPriorDistinctOwnedNativeObligations()
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
        var compilation = TestCompilation.Create("OwnedSessionEntryReservation", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        var artifactJson = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(artifactJson, out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single();
        var entry = preparation.TotalEntry!;
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry.Parameters, Has.Length.EqualTo(2));
        Assert.That(entry.Clauses, Has.Length.EqualTo(4));
        var budgets = new WorkerBudgets();
        Assert.That(budgets.QueryRlimit, Is.EqualTo(3_000_000));
        Assert.That(budgets.MethodRlimit, Is.EqualTo(20_000_000));
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(entry.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
        var controlMeter = new MethodResourceBudget(() => session.ConsumedResourceCount,
            budgets.QueryRlimit, budgets.MethodRlimit);
        var control = await TotalCallableVerifier.VerifyEntryAsync(preparation, budgets,
            TestContext.CurrentContext.CancellationToken, session, controlMeter);
        Assert.That(control.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Feasible));
        Assert.That(control.Reason, Is.EqualTo(WorkerClaimReason.None));
        var batchStart = session.ConsumedResourceCount;
        var meter = new MethodResourceBudget(() => session.ConsumedResourceCount,
            budgets.QueryRlimit, budgets.MethodRlimit);
        var factory = entry.Factory;
        var x = factory.Variable(entry.Parameters[0].Entry);
        var y = factory.Variable(entry.Parameters[1].Entry);
        Assert.That(factory.GetTypeInfo(x.Type).Width, Is.EqualTo(64));
        Assert.That(factory.GetTypeInfo(x.Type).Signed, Is.True);
        Assert.That(y.Type, Is.EqualTo(x.Type));
        var domain = entry.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (new Assumption(factory,
            factory.Binary(IrBinaryOperator.AndAlso, clause.Safe, clause.Value),
            new LoweredJustification(clause.Operation)))).ToImmutableArray();
        long[] primes = [1000000007L, 1000000009L, 1000000021L, 1000000033L];
        const long maximumInput = 1000000033L;
        foreach (var prime in primes)
        {
            Assert.That(prime, Is.InRange(2L, maximumInput));
            Assert.That(IsPrime(prime), Is.True, prime.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var maximumProduct = (BigInteger)maximumInput * maximumInput;
        Assert.That(maximumProduct, Is.LessThanOrEqualTo(new BigInteger(long.MaxValue)));
        var kernel = new ProofKernel(session);
        var startedQueries = 0;
        long observedBatchDelta = 0;
        var methodAdmissionDenied = false;
        for (var first = 0; first < primes.Length; first++)
        {
            for (var second = first + 1; second < primes.Length; second++)
            {
                var p = primes[first];
                var q = primes[second];
                var product = checked(p * q);
                Assert.That((BigInteger)p * q, Is.EqualTo(new BigInteger(product)));
                Assert.That(new BigInteger(product), Is.InRange(new BigInteger(4), maximumProduct));
                if (!meter.TryStartQuery())
                {
                    methodAdmissionDenied = true;
                    break;
                }
                var constraints = new[]
                {
                    factory.Binary(IrBinaryOperator.Equal,
                        factory.Binary(IrBinaryOperator.Multiply, x, y), factory.Integer(x.Type, product)),
                    factory.Binary(IrBinaryOperator.NotEqual, x, factory.Integer(x.Type, p)),
                    factory.Binary(IrBinaryOperator.NotEqual, x, factory.Integer(x.Type, q))
                };
                // Manually constructed IR/Smt API lemmas use compiler-decoded
                // bindings. They are not compiler-produced contract clauses.
                var assumptions = domain.AddRange(constraints.Select(predicate =>
                    predicate is null ? throw new System.ArgumentNullException(nameof(predicate)) : (new Assumption(factory, predicate, new LoweredJustification(factory.CreateOperation())))));
                var query = new VerificationQuery(factory, assumptions,
                    Goal.CreateInternalConsistency(factory), [.. entry.Parameters.Select(parameter => parameter.Entry)]);
                var before = session.ConsumedResourceCount;
                startedQueries++;
                var outcome = await kernel.VerifyAsync(query, TestContext.CurrentContext.CancellationToken);
                var delta = session.ConsumedResourceCount - before;
                Assert.That(delta, Is.GreaterThan(0));
                observedBatchDelta = checked(observedBatchDelta + delta);
                if (outcome is ProvenOutcome proven)
                {
                    Assert.That(proven.Core, Is.Not.Empty);
                    Assert.That(proven.Core.All(core => core is null ? throw new System.ArgumentNullException(nameof(core)) : (assumptions.Any(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Justification == core)))), Is.True);
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
                    core = outcome is ProvenOutcome proof ? proof.Core.Cast<LoweredJustification>().Select(core => core is null ? throw new System.ArgumentNullException(nameof(core)) : (core.Operation.Value)).ToArray() : [],
                    delta,
                    consumed = session.ConsumedResourceCount
                }));
            }
            if (methodAdmissionDenied)
            {
                break;
            }
        }
        var beforeEntry = session.ConsumedResourceCount;
        var spent = beforeEntry - batchStart;
        Assert.That(spent, Is.EqualTo(observedBatchDelta));
        Assert.That(startedQueries, Is.InRange(1, 6));
        var reservationDenied = budgets.MethodRlimit - spent < budgets.QueryRlimit;
        var result = await TotalCallableVerifier.VerifyEntryAsync(preparation, budgets,
            TestContext.CurrentContext.CancellationToken, session, meter);
        Assert.That(result.Kind, Is.EqualTo(reservationDenied
            ? CallableEntryFeasibilityKind.Unknown : CallableEntryFeasibilityKind.Feasible));
        Assert.That(result.Reason, Is.EqualTo(reservationDenied
            ? WorkerClaimReason.ResourceLimit : WorkerClaimReason.None));
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(result.UsedAssumptionIds, Is.Empty);
        if (reservationDenied)
        {
            Assert.That(session.ConsumedResourceCount, Is.EqualTo(beforeEntry));
        }
        else
        {
            Assert.That(session.ConsumedResourceCount, Is.GreaterThan(beforeEntry));
        }
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
            methodAdmissionDenied,
            batchStart,
            spent,
            reservationDenied,
            kind = result.Kind.ToString(),
            reason = result.Reason.ToString(),
            beforeEntry,
            afterEntry = session.ConsumedResourceCount
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
