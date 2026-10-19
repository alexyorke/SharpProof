using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("var data = new System.Collections.Generic.Stack<int>() { }; data.Push(17); return data.Count + data.Pop();", 18)]
    [TestCase("var data = new System.Collections.Generic.Stack<int>(); data.Push(17); data.Push(data.Pop() + 1); return data.Pop() + data.Count;", 18)]
    [TestCase("var first = new System.Collections.Generic.Stack<int>(); var second = new System.Collections.Generic.Stack<int>(); second.Push(23); var data = first; data.Push((data = second).Pop()); return first.Pop() * 100 + data.Count;", 2300)]
    [TestCase("var data = new System.Collections.Generic.Stack<int>(); return data.Count;", 0)]
    [TestCase("var data = new System.Collections.Generic.Stack<int>(3); data.Push(17); data.Push(23); return data.Pop() * 100 + data.Pop() + data.Count;", 2317)]
    [TestCase("var data = new System.Collections.Generic.Stack<int>(); var alias = data; alias.Push(17); data.Push(23); alias.Pop(); data.Push(31); return alias.Pop() * 100 + data.Pop();", 3117)]
    [TestCase("var data = new System.Collections.Generic.Stack<object>(); var item = new object(); data.Push(item); data.Push(null); var empty = data.Pop(); var value = data.Pop(); return empty == null && object.ReferenceEquals(value, item) && data.Count == 0 ? 1 : 0;", 1)]
    [TestCase("var data = new System.Collections.Generic.Stack<int>(); var other = new System.Collections.Generic.Stack<int>(); data.Push(17); other.Push(23); var alias = data; data = other; return alias.Pop() * 100 + data.Pop();", 1723)]
    [TestCase("var data = new System.Collections.Generic.Stack<int>(); data.Push(17); return Copy<int>(data, 23) * 100 + data.Pop();", 2317)]
    public async Task StackStateModelsLifoAndAliases(string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        const string helper = "static T Copy<T>(System.Collections.Generic.Stack<T> data, T item) { data.Push(item); return data.Pop(); }";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body,
            WorkerClaimOutcome.Proven, members: helper);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + (expected + 1) + "); " + body,
            WorkerClaimOutcome.Unknown, members: helper);
    }

    [Test]
    public async Task IncomingStackSharesCountAndPushedValue()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Stack<int> data",
            "Contract.Requires(data != null && data.Count < int.MaxValue); Contract.Ensures(Contract.Result<int>() == 17); var before = data.Count; data.Push(17); var item = data.Pop(); return data.Count == before ? item : 0;",
            WorkerClaimOutcome.Proven);
    }

    [TestCase("decimal", "decimal.MinValue")]
    [TestCase("double", "double.NaN")]
    [TestCase("System.Guid", "default(System.Guid)")]
    public async Task StackCopiesOpaqueValuesWithoutCallbacks(string type, string value)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(value);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); var data = new System.Collections.Generic.Stack<" + type + ">(); data.Push(" + value + "); var item = data.Pop(); return data.Count;", WorkerClaimOutcome.Proven);
    }

    [TestCase("return new System.Collections.Generic.Stack<int>(-1).Count;", false, false, false)]
    [TestCase("return new System.Collections.Generic.Stack<int>().Count;", false, false, false)]
    [TestCase("return data.Pop();", false, false, false)]
    [TestCase("Contract.Requires(data != null && data.Count > 0); return data.Pop();", true, true, false)]
    [TestCase("Contract.Requires(data != null); return data.Count;", true, true, true)]
    [TestCase("Contract.Requires(data != null); data.Push(17); return data.Count;", false, false, false)]
    public async Task StackStateRetainsFaultAllocationAndMutationEffects(string body, bool noThrow, bool noAllocation, bool pure)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "System.Collections.Generic.Stack<int> data", body, noThrow, noAllocation, pure);
    }

    [Test]
    public async Task MixedIncomingStackJoinUsesActualReceiver()
    {
        await CheckInterfaceArrayContract("bool choose, System.Collections.Generic.Stack<int> other",
            "Contract.Requires(other != null && other.Count < int.MaxValue); Contract.Ensures(Contract.Result<int>() == 17); var fresh = new System.Collections.Generic.Stack<int>(); var data = choose ? fresh : other; data.Push(17); return data.Pop();", WorkerClaimOutcome.Proven);
    }
    [Test]
    public async Task StackRefRebindingDoesNotKeepThePreviousReceiver()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 17); var data = new System.Collections.Generic.Stack<int>(); var other = new System.Collections.Generic.Stack<int>(); data.Push(17); other.Push(23); Replace(ref data, other); return data.Pop();", WorkerClaimOutcome.Unknown,
            members: "static void Replace(ref System.Collections.Generic.Stack<int> data, System.Collections.Generic.Stack<int> other) { data = other; }");
    }

    [Test]
    public async Task BoundBaseStackMembersIgnoreHiddenDerivedMethods()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 17); System.Collections.Generic.Stack<int> data = new Derived(); data.Push(17); return data.Pop();", WorkerClaimOutcome.Proven,
            members: "sealed class Derived : System.Collections.Generic.Stack<int> { public new int Count => -1; public new int Pop() => 99; public new void Push(int value) {} }");
    }

    [Test]
    public async Task UnmodeledEnumerableConstructionDoesNotGainEmptyState()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); var data = new System.Collections.Generic.Stack<int>(new[] { 17 }); return data.Count;", WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task UnknownWriterInvalidatesStackContents()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 17); var data = new System.Collections.Generic.Stack<int>(); data.Push(17); Mutator.Change(data); return data.Pop();", WorkerClaimOutcome.Unknown,
            members: "static class Mutator { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)] public static extern void Change(object value); }");
    }

    [Test]
    public async Task DistinctClosedGenericStacksDoNotShareCountAfterPop()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Stack<int> first, System.Collections.Generic.Stack<string> second",
            "Contract.Requires(first != null && second != null && first.Count > 0); Contract.Ensures(Contract.Result<int>() == 0); var before = second.Count; first.Pop(); return second.Count - before;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task IncompatibleClosedGenericStackFieldsCannotRefuteCountAfterPop()
    {
        await CheckInterfaceArrayContract("Holder holder",
            "Contract.Requires(holder != null && holder.First != null && holder.Second != null && holder.First.Count > 0); Contract.Ensures(Contract.Result<int>() == 0); var before = holder.Second.Count; holder.First.Pop(); return holder.Second.Count - before;", WorkerClaimOutcome.Unknown,
            members: "public sealed class Holder { public System.Collections.Generic.Stack<int> First; public System.Collections.Generic.Stack<string> Second; }");
    }

}
