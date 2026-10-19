using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase("var data = new System.Collections.Generic.HashSet<int>(); return data.Count;", 0)]
    [TestCase("var data = new System.Collections.Generic.HashSet<int>(1); var added = data.Add(17); var duplicate = data.Add(17); return added && !duplicate && data.Contains(17) && data.Count == 1 ? 1 : 0;", 1)]
    [TestCase("var data = new System.Collections.Generic.HashSet<int>(); var alias = data; data.Add(17); alias.Add(23); var removed = alias.Remove(17); return removed && !data.Contains(17) && data.Contains(23) && !data.Remove(17) && alias.Count == 1 ? 1 : 0;", 1)]
    [TestCase("var first = new System.Collections.Generic.HashSet<int>(); var second = new System.Collections.Generic.HashSet<int>(); first.Add(17); return !second.Contains(17) && first.Count == 1 && second.Count == 0 ? 1 : 0;", 1)]
    [TestCase("var data = new System.Collections.Generic.HashSet<bool>(); data.Add(false); data.Add(true); return data.Count == 2 && data.Remove(false) && !data.Contains(false) && data.Contains(true) ? 1 : 0;", 1)]
    [TestCase("var data = new System.Collections.Generic.HashSet<int>(); var alias = data; data.Add((data = new System.Collections.Generic.HashSet<int>()).Count + 17); return alias.Contains(17) && data.Count == 0 ? 1 : 0;", 1)]
    public async Task HashSetDefaultPrimitiveStatePreservesMembershipAndAliases(string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body,
            WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + (expected + 1) + "); " + body,
            WorkerClaimOutcome.Unknown);
    }

    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    [TestCase("ulong")]
    public async Task HashSetPrimitiveBoundaryKeysRemainDistinct(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var body = "var data = new System.Collections.Generic.HashSet<" + element + ">(); data.Add(" + element + ".MinValue); data.Add(" + element + ".MaxValue); " +
            "return data.Count == 2 && data.Remove(" + element + ".MinValue) && data.Contains(" + element + ".MaxValue) && !data.Contains(" + element + ".MinValue) && data.Count == 1 ? 1 : 0;";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " + body, WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task HashSetIncomingComparerIsNotAssumedDefault()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.HashSet<int> data",
            "Contract.Requires(data != null); Contract.Ensures(Contract.Result<int>() == 1); data.Add(17); return data.Contains(17) ? 1 : 0;",
            WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task HashSetUnknownWritersInvalidateMembership()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); var data = new System.Collections.Generic.HashSet<int>(); data.Add(17); Change(data); return data.Contains(17) ? 1 : 0;",
            WorkerClaimOutcome.Unknown, members: "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)] static extern void Change(System.Collections.Generic.HashSet<int> data);");
    }

    [Test]
    public async Task HashSetCountDoesNotRequireElementEquality()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.HashSet<decimal> data",
            "Contract.Requires(data != null); Contract.Ensures(Contract.Result<int>() == Contract.Old(data.Count)); return data.Count;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task HashSetDefaultOriginFlowsThroughImmutableGenericSourceParameters()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); var data = new System.Collections.Generic.HashSet<int>(); data.Add(17); return Change(data, 17, 23) && !data.Contains(17) && data.Contains(23) && data.Count == 1 ? 1 : 0;",
            WorkerClaimOutcome.Proven, members: "static bool Change<T>(System.Collections.Generic.HashSet<T> data, T before, T value) { data.Remove(before); return data.Add(value); }");
    }

    [Test]
    public async Task HashSetGenericHelperDoesNotInventAnIncomingDefaultComparer()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.HashSet<int> data",
            "Contract.Requires(data != null); Contract.Ensures(Contract.Result<int>() == 1); return Add(data, 17) ? 1 : 0;",
            WorkerClaimOutcome.Unknown, members: "static bool Add<T>(System.Collections.Generic.HashSet<T> data, T value) { return data.Add(value); }");
    }

    [Test]
    public async Task HashSetMembershipInitializationRemainsAnExceptionBoundary()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow]", "",
            "System.Collections.Generic.HashSet<int> data; try { data = new System.Collections.Generic.HashSet<int>(); } catch { return 0; } return data.Contains(17) ? 1 : 0;",
            noThrow: false, noAllocation: null, pure: null);
        await CheckInterfaceArrayEffects("[DoesNotThrow]", "",
            "System.Collections.Generic.HashSet<int> data; try { data = new System.Collections.Generic.HashSet<int>(); } catch { return 0; } return data.Count;",
            noThrow: true, noAllocation: null, pure: null);
    }

    [Test]
    public async Task HashSetMembershipIsNotAdmittedAsAnUnconditionallyPureLogicalClause()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); var data = new System.Collections.Generic.HashSet<int>(); data.Add(17); return Member(data, 17) ? 1 : 0;",
            WorkerClaimOutcome.Unknown, members: "static bool Member(System.Collections.Generic.HashSet<int> data, int key) { Contract.Requires(data != null && data.Contains(key)); return data.Contains(key); }");
    }

    [Test]
    public async Task HashSetCustomComparerConstructionDoesNotAcquireDefaultMembership()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.IEqualityComparer<int> comparer",
            "Contract.Requires(comparer != null); Contract.Ensures(Contract.Result<int>() == 2); var data = new System.Collections.Generic.HashSet<int>(comparer); data.Add(17); data.Add(23); return data.Count;",
            WorkerClaimOutcome.Unknown);
    }

    [TestCase("int", WorkerClaimOutcome.Proven)]
    [TestCase("decimal", WorkerClaimOutcome.Unknown)]
    public async Task HashSetConstructorDoesNotInventGenericInitializerHeapPreservation(string element, WorkerClaimOutcome expected)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayContract("int[] data",
            "Contract.Requires(data != null && data.Length == 1); Contract.Ensures(Contract.Result<int>() == Contract.Old(data[0])); var set = new System.Collections.Generic.HashSet<" + element + ">(); return data[0] + set.Count;",
            expected);
    }

    [Test]
    public async Task HashSetOpaqueElementConstructionStillEstablishesEmptyCount()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); var data = new System.Collections.Generic.HashSet<decimal>(); return data.Count;",
            WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task HashSetCallbackStateDoesNotGuaranteeNonnegativeCount()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.HashSet<int> data",
            "Contract.Requires(data != null); Contract.Ensures(Contract.Result<int>() >= 0); return data.Count;", WorkerClaimOutcome.Refuted);
    }

    [Test]
    public async Task DictionaryCallbackStateDoesNotAcquireAnUnsignedCountAfterOpaqueMutation()
    {
        await CheckInterfaceArrayContract("ReentrantKey key",
            "Contract.Requires(key != null); Contract.Ensures(Contract.Result<int>() >= 0); var data = new System.Collections.Generic.Dictionary<ReentrantKey, int>(); key.Data = data; key.Remaining = 1; data.Add(key, 17); data.Remove(key); return data.Count;",
            WorkerClaimOutcome.Unknown, members: "public sealed class ReentrantKey : System.IEquatable<ReentrantKey> { public System.Collections.Generic.Dictionary<ReentrantKey, int> Data; public int Remaining; public bool Equals(ReentrantKey other) { if (Remaining > 0) { Remaining--; Data.Remove(this); } return object.ReferenceEquals(this, other); } public override bool Equals(object other) { return other is ReentrantKey key && Equals(key); } public override int GetHashCode() { return 0; } }");
    }

    [Test]
    public async Task DictionaryIncomingCallbackStateDoesNotGuaranteeNonnegativeCount()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Dictionary<int, int> data",
            "Contract.Requires(data != null); Contract.Ensures(Contract.Result<int>() >= 0); return data.Count;", WorkerClaimOutcome.Refuted);
    }
}
