using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [Test]
    public async Task QueueDistinctClosedGenericReceiversCannotShareLogicalCount()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Queue<int> first, System.Collections.Generic.Queue<string> second",
            "Contract.Requires(first != null && second != null && first.Count == 1 && second.Count == 1); Contract.Ensures(Contract.Result<int>() == 1); first.Dequeue(); return second.Count;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task IncompatibleQueueFieldMutationCannotBecomeAnAuthoritativeCounterexample()
    {
        await CheckInterfaceArrayContract("Holder holder",
            "Contract.Requires(holder != null && holder.First != null && holder.Second != null && holder.First.Count == 1); Contract.Ensures(Contract.Result<int>() == 0); var before = holder.Second.Count; holder.First.Dequeue(); return holder.Second.Count - before;", WorkerClaimOutcome.Unknown,
            members: "public sealed class Holder { public System.Collections.Generic.Queue<int> First; public System.Collections.Generic.Queue<string> Second; }");
    }

    [TestCase("var data = new System.Collections.Generic.Queue<int>(); return data.Count;", 0)]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(0); return data.Count;", 0)]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(17); data.Enqueue(17); data.Enqueue(23); return data.Dequeue() * 100 + data.Dequeue() + data.Count;", 1723)]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(); data.Enqueue(17); var alias = data; alias.Enqueue(23); var first = data.Dequeue(); data.Enqueue(29); return first * 10000 + alias.Dequeue() * 100 + data.Dequeue();", 172329)]
    [TestCase("var data = new System.Collections.Generic.Queue<object>(); var item = new object(); data.Enqueue(item); data.Enqueue(null); return object.ReferenceEquals(data.Dequeue(), item) && data.Dequeue() == null && data.Count == 0 ? 1 : 0;", 1)]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(); var alias = data; data.Enqueue((data = new System.Collections.Generic.Queue<int>()).Count + 17); return alias.Dequeue() + data.Count;", 17)]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(); var other = new System.Collections.Generic.Queue<int>(); data.Enqueue(17); other.Enqueue(29); return data.Dequeue() + other.Dequeue();", 46)]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(); data.Enqueue(17); return RoundTrip(data, 23) + data.Dequeue();", 40)]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(); data.Enqueue(17); data.Enqueue(23); return data.Count * 100 + data.Dequeue();", 217)]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(); data.Enqueue(17); return data.Count * 100 + RoundTrip(data, 23);", 117)]
    public async Task QueueSharedStatePreservesFifoAndAliases(string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body,
            WorkerClaimOutcome.Proven, members: "static T RoundTrip<T>(System.Collections.Generic.Queue<T> data, T value) { data.Enqueue(value); return data.Dequeue(); }");
    }

    [TestCase("sbyte", "sbyte.MinValue", "sbyte.MaxValue")]
    [TestCase("byte", "byte.MinValue", "byte.MaxValue")]
    [TestCase("short", "short.MinValue", "short.MaxValue")]
    [TestCase("ushort", "ushort.MinValue", "ushort.MaxValue")]
    [TestCase("char", "char.MinValue", "char.MaxValue")]
    [TestCase("int", "int.MinValue", "int.MaxValue")]
    [TestCase("uint", "uint.MinValue", "uint.MaxValue")]
    [TestCase("long", "long.MinValue", "long.MaxValue")]
    [TestCase("ulong", "ulong.MinValue", "ulong.MaxValue")]
    [TestCase("bool", "false", "true")]
    public async Task QueueScalarBoundariesKeepFifo(string element, string first, string second)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var body = "var data = new System.Collections.Generic.Queue<" + element + ">(); data.Enqueue(" + first + "); data.Enqueue(" + second + "); return data.Dequeue() == " + first + " && data.Dequeue() == " + second + " && data.Count == 0 ? 1 : 0;";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " + body, WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); " + body, WorkerClaimOutcome.Unknown);
    }

    [TestCase("decimal")]
    [TestCase("double")]
    [TestCase("System.Guid")]
    public async Task QueueOpaqueValuesDoNotRequireEqualityCallbacks(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayContract("System.Collections.Generic.Queue<" + element + "> data, " + element + " value",
            "Contract.Requires(data != null && data.Count == 0); Contract.Ensures(Contract.Result<int>() == 0); data.Enqueue(value); data.Dequeue(); return data.Count;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task QueueIncomingStateAndOldCountShareStorage()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Queue<int> data",
            "Contract.Requires(data != null && data.Count > 0); Contract.Ensures(Contract.Result<int>() == Contract.Old(data.Count) - 1); data.Dequeue(); return data.Count;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task QueueUnknownWritersInvalidateStorage()
    {
        await CheckInterfaceArrayContract("System.Action<System.Collections.Generic.Queue<int>> mutate",
            "Contract.Requires(mutate != null); Contract.Ensures(Contract.Result<int>() == 17); var data = new System.Collections.Generic.Queue<int>(); data.Enqueue(17); mutate(data); return data.Dequeue();", WorkerClaimOutcome.Unknown);
    }

    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public async Task QueueIncomingEmptyReceiverRelatesContentsToArbitraryHead(int item)
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Queue<int> data",
            "Contract.Requires(data != null && data.Count == 0); Contract.Ensures(Contract.Result<int>() == " + item + "); data.Enqueue(" + item + "); return data.Dequeue();", WorkerClaimOutcome.Proven);
    }
    [Test]
    public async Task QueueIncomingEnqueueRelatesOldCountWithoutOwningReceiver()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Queue<int> data",
            "Contract.Requires(data != null && data.Count < int.MaxValue); Contract.Ensures(Contract.Result<int>() == Contract.Old(data.Count) + 1); data.Enqueue(17); return data.Count;", WorkerClaimOutcome.Proven);
    }

    [TestCase("")]
    [TestCase("where T : struct")]
    [TestCase("where T : class")]
    public async Task QueueOpenGenericSourceHelpersPreserveStorage(string constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target<T>(T first, T second) " + constraint + " { Contract.Ensures(Contract.Result<int>() == 0); var data = new System.Collections.Generic.Queue<T>(); Move<T>(data, first); Move<T>(data, second); return data.Count; } static U Move<U>(System.Collections.Generic.Queue<U> data, U item) { data.Enqueue(item); return data.Dequeue(); } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), response.ClaimResults.Single().Reason.ToString());
    }

    [Test]
    public async Task QueueDequeueOnNonemptyReceiverDoesNotInvokeElementCode()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations]", "System.Collections.Generic.Queue<object> data",
            "Contract.Requires(data != null && data.Count > 0); return data.Dequeue();", noThrow: true, noAllocation: true, pure: null, result: "object");
    }

    [TestCase("var data = new System.Collections.Generic.Queue<int>(); return data.Dequeue();")]
    [TestCase("var data = new System.Collections.Generic.Queue<int>(-1); return data.Count;")]
    [TestCase("System.Collections.Generic.Queue<int> data = null; return data.Count;")]
    public async Task QueueInvalidPathsDoNotInventNormalResults(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayEffects("[DoesNotThrow]", "", body, noThrow: false, noAllocation: null, pure: null);
    }
    [Test]
    public async Task QueueGrowthInitializationAndEmptyRetainEffects()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "", "var data = new System.Collections.Generic.Queue<int>(17); data.Enqueue(23); return data.Dequeue();", false, false, false);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "System.Collections.Generic.Queue<int> data", "return data.Dequeue();", false, false, false);
    }
}
