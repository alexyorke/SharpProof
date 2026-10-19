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
}
