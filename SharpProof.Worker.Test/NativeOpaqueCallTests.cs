using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A call that is neither inlined nor modeled is opaque: unknown result, an
// exception of unknown type, and unknown effects.
[TestFixture]
public sealed class NativeOpaqueCallTests
{
    [TestCase(false, false, "untouched")]
    [TestCase(false, true, "untouched")]
    [TestCase(true, false, "untouched")]
    [TestCase(false, false, "no-op-stable")]
    [TestCase(true, false, "no-op-stable")]
    [TestCase(true, true, "untouched")]
    [TestCase(true, true, "overwrite-true")]
    [TestCase(true, true, "overwrite-false")]
    [TestCase(true, true, "neighbor")]
    [TestCase(false, true, "old")]
    [TestCase(true, true, "old")]
    [TestCase(false, true, "identity")]
    [TestCase(true, true, "identity")]
    [TestCase(true, true, "length")]
    [TestCase(true, true, "lazy")]
    [TestCase(true, true, "guard")]
    public async Task TrustedWriterCannotRefuteUsingStaleHeap(bool array, bool writer, string scenario)
    {
        using var directory = new TempDirectory("sharpproof-trusted-writer");
        var external = TestCompilation.Create("TrustedWriter", """
            using SharpProof.Attributes;
            public sealed class Box { public int Value; }
            public static class Boundary {
                [SharpProofTrusted("Reviewed argument state boundary.")]
                [EffectContract(
            """ + (writer ? "SharpProofEffect.WritesArgumentState" : "SharpProofEffect.None") + """
                , Complete = true, PreconditionFree = true)]
                public static void Set(
            """ + (array ? "int[] value" : "Box value") + ") { " +
            (writer ? (array ? "if (value != null && value.Length > 0) { value[0] = 5; if (value.Length > 1) value[1] = 5; }"
                : "if (value != null) value.Value = 5;") : "") + " } }");
        var path = Path.Combine(directory.FullName, "TrustedWriter.dll");
        Assert.That(external.Emit(path).Success, Is.True);
        var runtime = System.Reflection.Assembly.Load(await File.ReadAllBytesAsync(path));
        var boxType = runtime.GetType("Box")!;
        var actual = array ? (object)new[] { 3, 3 } : Activator.CreateInstance(boxType)!;
        if (!array)
        { boxType.GetField("Value")!.SetValue(actual, 3); }
        runtime.GetType("Boundary")!.GetMethod("Set")!.Invoke(null, [actual]);
        Assert.That(array ? ((int[])actual)[0] : boxType.GetField("Value")!.GetValue(actual), Is.EqualTo(writer ? 5 : 3));
        var type = array ? "int[]" : "Box";
        var cell = array ? "value[0]" : "value.Value";
        var length = scenario is "neighbor" or "guard" ? 2 : 1;
        var postcondition = scenario switch
        {
            "overwrite-true" => cell + " == 7",
            "no-op-stable" => cell + " == 3",
            "neighbor" => "value[1] == 5",
            "old" => array ? "Contract.Old(value)[0] == 3" : "Contract.Old(value).Value == 3",
            "identity" => "value == Contract.Old(value)",
            "length" => "value.Length == 1",
            "lazy" => "true || value[0] == 5",
            "guard" => "value[value[1]] == 7",
            _ => cell + " == 5"
        };
        var afterCall = scenario is "overwrite-true" or "overwrite-false" or "neighbor" ? cell + " = 7; " : "";
        var source = "using SharpProof.Attributes; public static class Subject { public static int Target(" + type + " value) { " +
            "Contract.Requires(value != null" + (array ? " && value.Length == " + length : "") + " && " + cell + " == 3); " +
            "Contract.Ensures(" + postcondition + "); " + (array ? cell + " = 3; " : "") + "Boundary.Set(value); " + afterCall + "return 0; } }";
        var compilation = CSharpCompilation.Create("TrustedCaller", [CSharpSyntaxTree.ParseText(source,
            (CSharpParseOptions)external.SyntaxTrees.Single().Options, "Subject.cs")],
            external.References.Append(MetadataReference.CreateFromFile(path)), external.Options);
        TestCompilation.AssertNoErrors(compilation);
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null, preparation.FailureReason.ToString());
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        var call = candidate.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrCallInstruction>().Single();
        var description = candidate.Factory.GetString(candidate.Factory.GetOperationInfo(call.Operation).Description!.Value);
        Assert.That(description, Does.StartWith("opaque-call:"));
        var effects = int.Parse(description.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(effects & 4, Is.EqualTo(writer ? 4 : 0));
        Assert.That(effects & 1, Is.Zero);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyEnsuresAsync(0);
        var unknown = writer && scenario is "untouched" or "neighbor" or "guard";
        var refuted = !writer && scenario != "no-op-stable" || scenario == "overwrite-false";
        Assert.That(result.Outcome, unknown ? Is.TypeOf<UnknownOutcome>() : refuted ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>());
        Assert.That(result.Reason, Is.EqualTo(unknown ? WorkerClaimReason.CounterexampleNotReplayable : WorkerClaimReason.None));
    }

    [TestCase("version.GetHashCode()")]
    [TestCase("version.Major")]
    [TestCase("System.Environment.TickCount")]
    public async Task UnknownResultsDoNotBlockUnrelatedPostconditions(string call)
    {
        var preparation = Prepare("public static int Target(System.Version version, int value) { " +
            "Contract.Ensures(Contract.Result<int>() == value); var ignored = " + call + "; return value; }");
        Assert.That(await PostconditionAsync(preparation), Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task PostconditionOnAnUnknownResultIsNeverRefuted()
    {
        var preparation = Prepare("public static int Target(System.Version version) { " +
            "Contract.Ensures(Contract.Result<int>() == 1); return version.GetHashCode(); }");
        Assert.That(await PostconditionAsync(preparation), Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("System.Array.Sort(items); return items[0];")]
    [TestCase("var first = items[0]; System.Array.Sort(items); return first;")]
    public async Task OpaqueCallsMakeElementReadsApproximations(string body)
    {
        // The callee may write any array, so no read may assume the entry value.
        var preparation = Prepare("public static int Target(int[] items) { " +
            "Contract.Requires(items != null && items.Length == 2 && items[0] == 2 && items[1] == 1); " +
            "Contract.Ensures(Contract.Result<int>() == 2); " + body + " }");
        var outcome = await PostconditionAsync(preparation);
        Assert.That(outcome, Is.Not.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task OpaqueCallsMayThrowUnlessACatchAllHandlesThem()
    {
        var uncaught = await ExceptionsAsync("Contract.Requires(version != null); return version.GetHashCode();");
        var caught = await ExceptionsAsync("try { return version.GetHashCode(); } catch (System.Exception) { return 0; }");
        var bare = await ExceptionsAsync("try { return version.GetHashCode(); } catch { return 0; }");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(uncaught.Outcome, Is.Not.TypeOf<ProvenOutcome>());
            Assert.That(uncaught.Outcome, Is.Not.TypeOf<RefutedOutcome>());
            Assert.That(caught.Outcome, Is.TypeOf<ProvenOutcome>(), caught.Reason.ToString());
            Assert.That(bare.Outcome, Is.TypeOf<ProvenOutcome>(), bare.Reason.ToString());
        }
    }

    [Test]
    public async Task NarrowHandlersAroundOpaqueCallsMayMatch()
    {
        // The unknown exception may or may not be an ArgumentException.
        var narrow = await ExceptionsAsync("try { return version.GetHashCode(); } catch (System.ArgumentException) { return 0; }");
        var covered = await ExceptionsAsync("try { return version.GetHashCode(); } catch (System.ArgumentException) { return 0; } " +
            "catch (System.Exception) { return 1; }");
        var rethrown = await ExceptionsAsync("try { return version.GetHashCode(); } catch (System.ArgumentException) { throw; } " +
            "catch (System.Exception) { return 1; }");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(narrow.Outcome, Is.Not.TypeOf<ProvenOutcome>());
            Assert.That(narrow.Outcome, Is.Not.TypeOf<RefutedOutcome>());
            Assert.That(covered.Outcome, Is.TypeOf<ProvenOutcome>(), covered.Reason.ToString());
            Assert.That(rethrown.Outcome, Is.Not.TypeOf<ProvenOutcome>());
            Assert.That(rethrown.Outcome, Is.Not.TypeOf<RefutedOutcome>());
        }
    }

    [TestCase("Contract.Requires(x > 0); return x > 0 ? x : version.GetHashCode();", true)]
    [TestCase("Contract.Requires(version != null); return version.GetHashCode();", false)]
    public async Task ReachableOpaqueCallsBlockAllocationAndPurityProofs(string body, bool proven)
    {
        var preparation = Prepare("[ZeroAllocations, EnforcePure] public static int Target(System.Version version, int x) { " + body + " }");
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            foreach (var result in new[] { allocations, purity })
            {
                if (proven)
                { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
                else
                {
                    Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>());
                    Assert.That(result.Outcome, Is.Not.TypeOf<RefutedOutcome>());
                }
            }
        }
    }

    [TestCase("var ignored = time.ToBinary(); return value;", WorkerClaimOutcome.Proven)]
    [TestCase("var copy = time; var ignored = copy.AddTicks(1).Ticks; return value;", WorkerClaimOutcome.Proven)]
    [TestCase("var created = new System.DateTime(1); return value;", WorkerClaimOutcome.Unknown)]
    public async Task StructValuesAreOpaque(string body, WorkerClaimOutcome outcome)
    {
        // Only opaque calls read a struct, so mutation through `this` is
        // unobservable; constructing one stays unsupported.
        var preparation = Prepare("public static int Target(System.DateTime time, int value) { " +
            "Contract.Ensures(Contract.Result<int>() == value); " + body + " }");
        Assert.That(await PostconditionAsync(preparation), Is.EqualTo(outcome));
    }

    [TestCase(true, typeof(ProvenOutcome))]
    [TestCase(false, null)]
    public async Task CatchAllHandlesDispatchedAndConstrainedCalls(bool guarded, Type? outcome)
    {
        var body = "var entry = _collection.Find(item.Key); return entry.Value.Equals(item.Value);";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using System.Collections.Generic;
            using SharpProof.Attributes;
            public class Tree<TKey, TValue> { public virtual KeyValuePair<TKey, TValue> Find(TKey key) { throw new System.Exception(); } }
            public class Map<TKey, TValue> {
                private Tree<TKey, TValue> _collection { get; set; }
                [DoesNotThrow] public bool Contains(KeyValuePair<TKey, TValue> item) {
            """ + (guarded ? "try { " + body + " } catch (System.Exception) { return false; }" : body) + " } }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(
            preparations.Single(preparation => preparation.EffectClaims.Length != 0), new WorkerBudgets());
        if (outcome != null)
        { Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString()); }
        else
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        }
    }

    private static async Task<PassiveCallableCheckResult> ExceptionsAsync(string body)
    {
        var preparation = Prepare("[DoesNotThrow] public static int Target(System.Version version) { " + body + " }");
        return await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
    }

    private static async Task<WorkerClaimOutcome> PostconditionAsync(CompilerCallablePreparation preparation)
    {
        var results = new List<WorkerClaimResult>();
        await TotalCallableVerifier.VerifyAsync(preparation, new WorkerBudgets(),
            check => results.Add(CallableClaimResultAssembler.FromTotal(preparation, check)), null, CancellationToken.None);
        return results.Count == 0 ? WorkerClaimOutcome.Unknown : results[^1].Outcome;
    }

    private static CompilerCallablePreparation Prepare(string method)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            "using SharpProof.Attributes; public static class C { " + method + " }");
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal));
    }
}
