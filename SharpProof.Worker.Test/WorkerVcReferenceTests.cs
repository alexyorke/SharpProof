using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerVcReferenceTests
{
    [TestCase("string", "x != null && x.Length == 3", "return x.Length;", 3)]
    [TestCase("int[]", "x != null && x.Length == 3", "return x.Length;", 3)]
    [TestCase("object", "x != null", "object copy = x; return x == copy ? 1 : 0;", 1)]
    [TestCase("object", "x == null", "object copy = x; return x == copy ? 1 : 0;", 1)]
    [TestCase("int[]", "x != null && x.Length == 3", "int[] copy = x; return x == copy ? 1 : 0;", 1)]
    [TestCase("int[]", "x == null", "int[] copy = x; return x == copy ? 1 : 0;", 1)]
    [TestCase("string", "x == null", "try { return Length(x); } catch (System.NullReferenceException) when (++seen == 1) { return seen; } finally { seen += 10; }", 1)]
    public async Task ReferenceLengthProofAndRefutationSurviveArtifactAndCache(string type, string requires, string body, int expected)
    {
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target({{type}} x) {
                    Contract.Requires({{requires}});
                    Contract.Ensures(Contract.Result<int>() == {{expected}});
                    Contract.Ensures(Contract.Result<int>() == {{expected + 1}});
                    int seen = 0;
                    {{body}}
                }
                private static int Length(string value) { return value.Length; }
            }
            """;
        using var project = new ShadowTestProject(source, cacheEnabled: true);
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
        var replay = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, refuted.EntryModel);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(expected));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        for (var invocation = 0; invocation < 2; invocation++)
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(invocation == 0 ? WorkerCacheStatus.Written : WorkerCacheStatus.Hit));
            Assert.That(response.Errors, Is.Empty);
            var ordinals = response.Manifest.Claims.Where(claim => claim.CallableId == target.Entry.CallableId && claim.Kind == WorkerClaimKind.Postcondition)
                .ToDictionary(claim => claim.ClaimId, claim => claim.Ordinal, StringComparer.Ordinal);
            var posts = response.ClaimResults.Where(result => ordinals.ContainsKey(result.ClaimId)).OrderBy(result => ordinals[result.ClaimId]).ToArray();
            Assert.That(posts.Select(result => result.Outcome), Is.EqualTo(new[] { WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted }));
            Assert.That(posts[1].Model, Is.Not.Empty);
        }
    }

    [Test]
    public async Task NullLengthInPostconditionRetainsUndefinednessAbstention()
    {
        var subject = PassiveSourceSubject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(string x) {
                    Contract.Requires(x == null);
                    Contract.Ensures(x.Length == x.Length);
                    return 0;
                }
            }
            """);
        var candidate = subject.Enroll();
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.PostconditionMayBeUndefined));
    }

    [TestCase("object", "Contract.Result<object>() == Contract.Old(x)")]
    [TestCase("int[]", "Contract.Result<int[]>() == Contract.Old(x)")]
    [TestCase("string", "Contract.Result<string>() != null && Contract.Result<string>().Length == x.Length")]
    public async Task ReferenceResultsCrossFreshSourceFramesWithoutLosingIdentityOrLength(string type, string predicate)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static {{type}} Target({{type}} x) {
                    Contract.Requires(x != null);
                    Contract.Ensures({{predicate}});
                    Contract.Ensures(Contract.Result<{{type}}>() == null);
                    return Forward(x);
                }
                private static {{type}} Forward({{type}} value) { return Copy(value); }
                private static {{type}} Copy({{type}} value) { return value; }
            }
            """);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal));
        Assert.That(target.Total, Is.Not.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var result = await solver.VerifyEnsuresAsync(1);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.EntryModel.Keys, Is.EquivalentTo(candidate.Parameters.Select(parameter => parameter.Entry)));
        var replay = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, result.EntryModel);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(ReferenceEquals(replay.ReturnValue, result.EntryModel[candidate.Parameters.Single().Entry]), Is.True);
    }

    [TestCase("sbyte", "sbyte.MinValue", 128UL)]
    [TestCase("byte", "byte.MaxValue", 255UL)]
    [TestCase("short", "short.MinValue", 32768UL)]
    [TestCase("ushort", "ushort.MaxValue", 65535UL)]
    [TestCase("int", "int.MinValue", 2147483648UL)]
    [TestCase("uint", "uint.MaxValue", 4294967295UL)]
    [TestCase("long", "long.MinValue", 9223372036854775808UL)]
    [TestCase("ulong", "ulong.MaxValue", ulong.MaxValue)]
    [TestCase("char", "'\\ud800'", 55296UL)]
    [TestCase("bool", "true", 1UL)]
    public async Task ScalarArrayReadsPreserveBitsThroughArtifactAndOriginalReplay(string type, string constant, ulong bits)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static {{type}} Target({{type}}[] x) {
                    Contract.Requires(x != null && x.Length == 1 && x[0] == {{constant}});
                    Contract.Ensures(Contract.Result<{{type}}>() == Contract.Old(x[0]));
                    Contract.Ensures(Contract.Result<{{type}}>() != Contract.Old(x[0]));
                    return Read(x);
                }
                private static {{type}} Read({{type}}[] value) { return value[0]; }
            }
            """);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal));
        Assert.That(target.Total, Is.Not.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var result = await solver.VerifyEnsuresAsync(1);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.EntryModel.Keys, Is.EquivalentTo(candidate.Parameters.Select(parameter => parameter.Entry)));
        var entry = result.EntryModel[candidate.Parameters.Single().Entry];
        Assert.That(entry.Elements.Length, Is.EqualTo(1));
        Assert.That(entry.Elements[0].Kind == IrValueKind.Boolean ? (ulong)(entry.Elements[0].Boolean ? 1 : 0) : entry.Elements[0].IntegerBits, Is.EqualTo(bits));
        var replay = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, result.EntryModel);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Kind == IrValueKind.Boolean ? (ulong)(replay.ReturnValue.Boolean ? 1 : 0) : replay.ReturnValue.IntegerBits, Is.EqualTo(bits));
    }

    [TestCase("x == null", 1)]
    [TestCase("x != null && x.Length == 0", 2)]
    [TestCase("x != null && x.Length == 1 && x[0] == 42", 42)]
    public async Task ArrayReadFaultsPreserveArgumentEffectsFilterSelectionAndCapturedReturn(string requires, int expected)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int[] x, uint index) {
                    Contract.Requires({{requires}} && index == 0);
                    Contract.Ensures(Contract.Result<int>() == {{expected}} && index == 1 && Contract.Old(index) == 0);
                    Contract.Ensures(Contract.Result<int>() == {{expected + 1}});
                    int seen = 0;
                    try { return Read(x, index++); }
                    catch (System.NullReferenceException) when (++seen == 1) { return seen; }
                    catch (System.IndexOutOfRangeException) when (++seen == 1) { return seen + 1; }
                    finally { seen += 10; }
                }
                private static int Read(int[] value, uint index) { return value[index]; }
            }
            """);
        var target = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal));
        var candidate = PassiveCallableArtifactAdapter.Enroll(target);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var result = await solver.VerifyEnsuresAsync(1);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        Assert.That(result.EntryModel.Keys, Is.EquivalentTo(candidate!.Parameters.Select(parameter => parameter.Entry)));
        var replay = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, result.EntryModel);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(expected));
        Assert.That(replay.ConsumedApproximation, Is.False);
    }

    [Test]
    public async Task UndefinedArrayReadPostconditionAbstains()
    {
        var subject = PassiveSourceSubject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int[] x) {
                    Contract.Requires(x != null && x.Length == 0);
                    Contract.Ensures(x[0] == x[0]);
                    return 0;
                }
            }
            """);
        var candidate = subject.Enroll();
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyEnsuresAsync(0);
        Assert.That(result.Outcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.PostconditionMayBeUndefined));
    }

    [TestCase("bool Target(string x, string y) { Contract.Ensures(true); return x == y; }")]
    [TestCase("int Target(int[] x, long index) { Contract.Ensures(true); return x[index]; }")]
    [TestCase("int Target(int[] x, ulong index) { Contract.Ensures(true); return x[index]; }")]
    [TestCase("int[] Target(int x) { Contract.Ensures(true); return new int[] { x }; }")]
    [TestCase("int[] Target() { Contract.Ensures(true); return []; }")]
    [TestCase("int[] Target(int[] x) { Contract.Ensures(true); return [..x]; }")]
    [TestCase("int[] Target(int x) { Contract.Ensures(true); return [x]; }")]
    [TestCase("System.Collections.Generic.List<int> Target() { Contract.Ensures(true); return [1, 2]; }")]
    [TestCase("int Target(string x) { Contract.Ensures(true); try { return x.Length; } catch (System.NullReferenceException error) { return error == null ? 1 : 0; } }")]
    public void UnsupportedReferenceOperationsRemainUnenrolled(string member)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class Subject { public static " + member + " }");
        Assert.That(artifact.Callables.Single().Total, Is.Null);
    }

    [Test]
    public async Task FreshArrayReturnPreservesExactLength()
    {
        var subject = PassiveSourceSubject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int[] Target() {
                    Contract.Ensures(Contract.Result<int[]>() != null && Contract.Result<int[]>().Length == 1);
                    return new int[1];
                }
            }
            """);
        var candidate = subject.Enroll();
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [TestCase("int[] values = new int[] { 1, -2 }; return values[0] + values[1];", -1)]
    [TestCase("int[] values = [1, -2]; return values[0] + values[1];", -1)]
    [TestCase("short[] values = [1, -2]; return values[0] + values[1];", -1)]
    [TestCase("bool[] values = new bool[] { true, false }; return values[0] && !values[1] ? 1 : 0;", 1)]
    [TestCase("long[] values = new long[] { 1L, -2L }; return (int)(values[0] + values[1]);", -1)]
    public async Task ConstantArrayInitializersPreserveContents(string body, int expected)
    {
        var subject = PassiveSourceSubject.Create("using SharpProof.Attributes; public static class Subject { " +
            "[DoesNotThrow, EnforcePure] public static int Target() { Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body + " } }");
        var candidate = subject.Enroll();
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyEnsuresAsync(0)).Outcome, Is.TypeOf<ProvenOutcome>());
        var replay = new IrProgramInterpreter(candidate!.Factory).Execute(candidate.Program, new Dictionary<IrVarId, IrValue>());
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(expected));
    }
}
