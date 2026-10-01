using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcMetadataCallTests
{
    internal const string LibrarySource = """
        public static class Library {
            public static int Target(int value) { return Again(value); }
            private static int Again(int value) { return checked(value + 1); }
            public static int Pack(int first, int second) { return first * 10 + second; }
            public static ulong UAdd(ulong value) { return unchecked(value + 1UL); }
            public static int Div(int value) { return 10 / value; }
            public static byte Store(byte value) { value++; return value; }
        }
        """;

    internal const string IncrementSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 3);
                Contract.Ensures(Contract.Result<int>() == 4 && x == 3);
                Contract.Ensures(Contract.Result<int>() == 3);
                return Forward(x);
            }
            private static int Forward(int value) { return Library.Target(value); }
        }
        """;

    internal const string ArgumentSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 3);
                Contract.Ensures(Contract.Result<int>() == 33 && x == 4);
                Contract.Ensures(Contract.Result<int>() == x * 11);
                return Library.Pack(first: x, second: x++);
            }
        }
        """;

    internal const string UlongSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static ulong Target(ulong x) {
                Contract.Requires(x == 18446744073709551615UL);
                Contract.Ensures(Contract.Result<ulong>() == 0UL);
                Contract.Ensures(Contract.Result<ulong>() == 1UL);
                return Library.UAdd(x);
            }
        }
        """;

    internal const string ReverseArgumentSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 3);
                Contract.Ensures(Contract.Result<int>() == 43 && x == 4);
                Contract.Ensures(Contract.Result<int>() == 33);
                return Library.Pack(second: x++, first: x);
            }
        }
        """;

    internal const string StoreSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static byte Target(byte x) {
                Contract.Requires(x == 255);
                Contract.Ensures(Contract.Result<byte>() == 0 && x == Contract.Old(x));
                Contract.Ensures(Contract.Result<byte>() == x);
                return Library.Store(x);
            }
        }
        """;

    internal const string FaultSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 0);
                Contract.Ensures(Contract.Result<int>() == 1 && x == 11);
                Contract.Ensures(Contract.Result<int>() == 0);
                try { return Library.Div(x); }
                catch (System.DivideByZeroException) when (++x > 0) { return x; }
                finally { x += 10; }
            }
        }
        """;

    internal const string FilterFaultSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == 0);
                Contract.Ensures(Contract.Result<int>() == 1 && x == 11);
                Contract.Ensures(Contract.Result<int>() == 0);
                try { return 10 / x; }
                catch (System.DivideByZeroException) when (++x > 0 && Library.Div(x - 1) > 0) { return 7; }
                catch (System.DivideByZeroException) { return x; }
                finally { x += 10; }
            }
        }
        """;

    internal const string OverflowSource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int x) {
                Contract.Requires(x == int.MaxValue);
                Contract.Ensures(Contract.Result<int>() == Contract.Old(x) && x == int.MinValue);
                Contract.Ensures(Contract.Result<int>() == 0);
                try { return Library.Target(x); }
                catch (System.OverflowException) { return x; }
                finally { x = unchecked(x + 1); }
            }
        }
        """;

    [TestCase(IncrementSource, 3, 4)]
    [TestCase(ArgumentSource, 3, 33)]
    [TestCase(ReverseArgumentSource, 3, 43)]
    [TestCase(StoreSource, 255, 0)]
    [TestCase(UlongSource, ulong.MaxValue, 0UL)]
    [TestCase(FaultSource, 0, 1)]
    [TestCase(FilterFaultSource, 0, 1)]
    [TestCase(OverflowSource, int.MaxValue, int.MaxValue)]
    public async Task TypedMetadataProofRefutationUsesOriginalReplayAndValidatedCache(string source, object input, object expected)
    {
        using var subject = new MetadataTestSubject(LibrarySource, source);
        Assert.That(subject.InvokeRoot(input), Is.EqualTo(expected));
        using var project = new ShadowTestProject(subject.CreateArtifact(), cacheEnabled: true);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal));
        Assert.That(target.Total, Is.Not.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var refuted = await solver.VerifyEnsuresAsync(1);
        Assert.That(refuted.Outcome, Is.TypeOf<RefutedOutcome>(), refuted.Reason.ToString());
        Assert.That(refuted.EntryModel.Keys, Is.EquivalentTo(target.Total!.Parameters.Select(parameter => parameter.Entry)));
        var execution = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, refuted.EntryModel);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(expected is ulong number
            ? new System.Numerics.BigInteger(number) : new System.Numerics.BigInteger(Convert.ToInt64(expected, System.Globalization.CultureInfo.InvariantCulture))));
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        WorkerVcShadowReport? report = null;
        worker.ShadowReportSink = value => report = value;
        for (var invocation = 0; invocation < 2; invocation++)
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(invocation == 0 ? WorkerCacheStatus.Written : WorkerCacheStatus.Hit));
            Assert.That(report!.InputHash, Is.EqualTo(response.InputHash));
            Assert.That(report.RequestHash, Is.EqualTo(response.RequestHash));
            var claims = project.Snapshot.CompilerManifest.Manifest.Claims.ToDictionary(claim => claim.ClaimId, claim => claim.Ordinal, StringComparer.Ordinal);
            var rows = report.Rows.Where(row => row.CallableId == target.Entry.CallableId).OrderBy(row => claims[row.ClaimId]).ToArray();
            Assert.That(rows.Select(row => row.NewOutcome), Is.EqualTo(new[] { WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted }));
            Assert.That(rows.All(row => row.Checked && row.TotalPresent), Is.True);
            Assert.That(report.SoundnessDisagreements, Is.Zero);
        }
    }
}
