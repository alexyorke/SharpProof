using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Insert(0, 17); return data.Count * 100 + data[0];", 117)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(11); data.Add(23); data.Insert(0, 17); return data.Count * 1000 + data[0] * 100 + data[1] * 10 + data[2];", 4833)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(11); data.Add(23); data.Insert(1, 17); return data.Count * 1000 + data[0] * 100 + data[1] * 10 + data[2];", 4293)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(11); data.Insert(1, 17); return data.Count * 100 + data[0] + data[1];", 228)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(11); System.Collections.Generic.IList<int> alias = data; alias.Insert(0, 17); return alias.Count * 100 + data[0] + data[1];", 228)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(11); data.Add(23); var copy = data.GetRange(0, 2); copy[0] = 17; data[1] = 29; return copy.Count * 1000 + data[0] * 100 + copy[0] * 10 + copy[1];", 3293)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(11); data.Add(23); var copy = data.GetRange(1, 1); System.Collections.Generic.IList<int> alias = copy; alias.Insert(1, 17); return data.Count * 100 + copy.Count * 10 + copy[0] + copy[1];", 260)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); var copy = data.GetRange(0, 0); return object.ReferenceEquals(data, copy) ? -1 : copy.Count;", 0)]
    public async Task ListRangesPreserveSharedContentsAndDistinctCopies(string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body, WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + (expected + 1) + "); " + body, WorkerClaimOutcome.Unknown);
    }

    [TestCase("data.Insert(-1, 17);", "System.ArgumentOutOfRangeException")]
    [TestCase("data.Insert(2, 17);", "System.ArgumentOutOfRangeException")]
    [TestCase("data.GetRange(-1, 0);", "System.ArgumentOutOfRangeException")]
    [TestCase("data.GetRange(0, -1);", "System.ArgumentOutOfRangeException")]
    [TestCase("data.GetRange(2, 0);", "System.ArgumentException")]
    [TestCase("data.GetRange(1, 1);", "System.ArgumentException")]
    [TestCase("data.GetRange(int.MaxValue, int.MaxValue);", "System.ArgumentException")]
    public async Task ListRangeBoundsPreserveFaultKinds(string call, string fault)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(fault);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 71); var data = new System.Collections.Generic.List<int>(); data.Add(11); try { " + call + " return -1; } catch (" + fault + ") { return 71; }", WorkerClaimOutcome.Proven);
    }

    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Insert(0, 17); return data.Count;")]
    [TestCase("var data = new System.Collections.Generic.List<int>(); var copy = data.GetRange(0, 0); return copy.Count;")]
    public async Task ListRangeEffectsRemainConservative(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "", body, false, false, false);
    }
    [TestCase("System.Collections.Generic.List<int> data", "Contract.Requires(data != null && data.Count == 2);", "data.Insert(1, 17); return data.Count;", 3)]
    [TestCase("System.Collections.Generic.List<int> data", "Contract.Requires(data != null && data.Count == 2);", "var copy = data.GetRange(1, 1); return copy.Count * 100 + data.Count;", 102)]
    [TestCase("", "", "var data = new System.Collections.Generic.List<object>(); var item = new object(); data.Insert(0, item); data.Insert(1, null); var copy = data.GetRange(0, 2); data[0] = null; return object.ReferenceEquals(copy[0], item) && copy[1] == null && !object.ReferenceEquals(data, copy) ? 1 : 0;", 1)]
    public async Task ListRangesUseConcreteIncomingAndReferenceStorage(string parameters, string requires, string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(requires);
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract(parameters, requires + "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body, WorkerClaimOutcome.Proven);
    }

    [TestCase("System.Collections.Generic.IList<int> data", "Contract.Requires(data != null); data.Insert(0, 17); return data.Count;", 1)]
    [TestCase("System.Action<System.Collections.Generic.List<int>> mutate", "Contract.Requires(mutate != null); var data = new System.Collections.Generic.List<int>(); data.Insert(0, 17); mutate(data); return data[0];", 17)]
    [TestCase("System.Action<System.Collections.Generic.List<int>> mutate", "Contract.Requires(mutate != null); var data = new System.Collections.Generic.List<int>(); data.Add(17); var copy = data.GetRange(0, 1); mutate(copy); return copy[0];", 17)]
    public async Task ListRangesDoNotAssumeIncomingInterfaceOrInvalidatedContents(string parameters, string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract(parameters, "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body, WorkerClaimOutcome.Unknown);
    }
    [TestCase("data.Insert(-1, 17);")]
    [TestCase("data.GetRange(-1, 0);")]
    public async Task ListRangeBoundsPreserveUnknownResourceCallbacks(string call)
    {
        ArgumentNullException.ThrowIfNull(call);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 71); var data = new System.Collections.Generic.List<int>(); try { " + call + " return -1; } catch (System.ArgumentOutOfRangeException) { return 71; } catch (System.Exception) { return 0; }", WorkerClaimOutcome.Unknown);
        var preparation = Prepare("[AllowedCapabilities(SharpProofCapability.IO | SharpProofCapability.Randomness | SharpProofCapability.Reflection | SharpProofCapability.Synchronization | SharpProofCapability.NativeInterop)] public static int Target(System.Collections.Generic.List<int> data) { Contract.Requires(data != null); " + call + " return 0; }");
        var result = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }
    [Test]
    public async Task ListRangesShareGenericSourceHelperStorage()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 123); var data = new System.Collections.Generic.List<int>(); Put<int>(data, 0, 23); var copy = Slice<int>(data, 0, 1); return copy.Count * 100 + copy[0];", WorkerClaimOutcome.Proven,
            members: "static void Put<T>(System.Collections.Generic.IList<T> data, int index, T item) { data.Insert(index, item); } static System.Collections.Generic.List<T> Slice<T>(System.Collections.Generic.List<T> data, int index, int count) => data.GetRange(index, count);");
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(12)]
    public async Task ListRangesKeepCountAndRejectWrongClaimsBeyondThePeeledPrefix(int count)
    {
        var adds = string.Join(" ", Enumerable.Range(1, count).Select(value => "data.Add(" + value + ");"));
        var body = "var data = new System.Collections.Generic.List<int>(); " + adds + " data.Insert(0, 73); var copy = data.GetRange(0, " + (count + 1) + "); return copy.Count;";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + (count + 1) + "); " + body, WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + (count + 2) + "); " + body, WorkerClaimOutcome.Unknown);
    }
    [Test]
    public async Task ListRangeInterfaceDispatchRejectsAReimplementingSubclass()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); var data = new Strange(); System.Collections.Generic.IList<int> alias = data; alias.Insert(0, 17); return data.Count;", WorkerClaimOutcome.Unknown,
            members: "public sealed class Strange : System.Collections.Generic.List<int>, System.Collections.Generic.IList<int> { void System.Collections.Generic.IList<int>.Insert(int index, int item) { } }");
    }
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task ListRangesKeepUnsupportedOpenGenericAliasExpressionsConservative(bool inserts, bool objectView)
    {
        var mutation = inserts ? "first.Insert(0, replacement);" : "first[0] = replacement;";
        var source = "using SharpProof.Attributes; public static class C { public static bool Target" + (objectView ? "<T>" : "<T,U>") + "(System.Collections.Generic.List<T> first, System.Collections.Generic.List<" + (objectView ? "object" : "U") + "> second, T replacement) where T : class " + (objectView ? "" : "where U : class ") + "{ Contract.Requires(first != null && second != null && object.ReferenceEquals(first, second) && second.Count == 1); Contract.Ensures(Contract.Result<bool>()); var before = second[0]; " + mutation + " return object.ReferenceEquals(second[0], before); } }";
        using var project = WorkerTests.TestProject.Create(source);
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        // These roots stop at an unsupported expression; they do not prove generic alias semantics.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(response.ClaimResults.Single().Reason, Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
            Assert.That(response.CallableResults.Single().Reason, Is.EqualTo(WorkerCallableCoverageReason.SemanticUnknown));
        }
        Assert.That(response.ClaimResults.Single().Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), response.ClaimResults.Single().Reason.ToString());
        var before = new object();
        var replacement = new object();
        var data = new List<object> { before };
        var alias = data;
        if (inserts)
        { data.Insert(0, replacement); }
        else
        { data[0] = replacement; }
        Assert.That(ReferenceEquals(alias[0], before), Is.False);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task ListRangesDoNotSeparateOpenGenericInstantiationsThatCanAlias(bool objectView)
    {
        var resultType = objectView ? "object" : "U";
        var source = "using SharpProof.Attributes; public static class C { public static " + resultType + " Target" +
            (objectView ? "<T>" : "<T,U>") + "(System.Collections.Generic.List<T> first, System.Collections.Generic.List<" + resultType + "> second, T item) where T : class " +
            (objectView ? "" : "where U : class ") + "{ Contract.Requires(first != null && second != null && second.Count > 0); Contract.Ensures(object.ReferenceEquals(Contract.Result<" + resultType + ">(), Contract.Old(second[0]))); first.Insert(0, item); return second[0]; } }";
        using var project = WorkerTests.TestProject.Create(source);
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), response.ClaimResults.Single().Reason.ToString());
        var before = new object();
        var replacement = new object();
        var data = new List<object> { before };
        var alias = data;
        data.Insert(0, replacement);
        Assert.That(ReferenceEquals(alias[0], before), Is.False);
    }
}
