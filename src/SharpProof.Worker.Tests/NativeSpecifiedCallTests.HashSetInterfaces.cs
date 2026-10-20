using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [Test]
    public async Task HashSetCollectionInterfaceSharesStateWithConcreteAliases()
    {
        const string body = "var data = new System.Collections.Generic.HashSet<int>(); System.Collections.Generic.ICollection<int> alias = data; " +
            "alias.Add(17); var duplicate = data.Add(17); var removed = alias.Remove(17); " +
            "return !duplicate && removed && !alias.Contains(17) && alias.Count == 0 ? 1 : 0;";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " + body, WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 2); " + body, WorkerClaimOutcome.Unknown);
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
    [TestCase("bool")]
    public async Task HashSetCollectionInterfacePreservesPrimitiveBoundaryKeys(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var first = element == "bool" ? "false" : element + ".MinValue";
        var second = element == "bool" ? "true" : element + ".MaxValue";
        var body = "System.Collections.Generic.ICollection<" + element + "> data = new System.Collections.Generic.HashSet<" + element + ">(); " +
            "data.Add(" + first + "); data.Add(" + second + "); data.Add(" + first + "); var count = data.Count; " +
            "return count == 2 && data.Remove(" + first + ") && !data.Remove(" + first + ") && !data.Contains(" + first + ") && data.Contains(" + second + ") && data.Count == 1 ? 1 : 0;";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " + body, WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task HashSetCollectionInterfaceFlowsThroughImmutableGenericHelpers()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); var data = new System.Collections.Generic.HashSet<int>(); " +
            "System.Collections.Generic.ICollection<int> alias = data; return Change(alias, 17, 23) && data.Count == 1 && data.Contains(23) ? 1 : 0;", WorkerClaimOutcome.Proven,
            members: "static bool Change<T>(System.Collections.Generic.ICollection<T> data, T before, T after) { data.Add(before); data.Add(after); " +
                "return data.Remove(before) && !data.Contains(before) && data.Contains(after) && data.Count == 1; }");
    }

    [Test]
    public async Task HashSetCollectionInterfaceCountDoesNotRequireElementEquality()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); System.Collections.Generic.ICollection<decimal> data = " +
            "new System.Collections.Generic.HashSet<decimal>(); return data.Count;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task HashSetCollectionInterfaceJoinsRequireTheSameImplementation()
    {
        await CheckInterfaceArrayContract("bool choose", "Contract.Ensures(Contract.Result<int>() == 1); System.Collections.Generic.ICollection<int> data = " +
            "choose ? new System.Collections.Generic.HashSet<int>() : new System.Collections.Generic.HashSet<int>(1); data.Add(17); data.Add(17); return data.Count;", WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("bool choose", "Contract.Ensures(Contract.Result<int>() == 1); System.Collections.Generic.ICollection<int> data = " +
            "choose ? new System.Collections.Generic.HashSet<int>() : new System.Collections.Generic.List<int>(); data.Add(17); data.Add(17); return data.Count;", WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task IncomingCollectionInterfacesDoNotGainHashSetSemantics()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.ICollection<int> data", "Contract.Requires(data != null); Contract.Ensures(Contract.Result<int>() == 1); " +
            "data.Add(17); data.Add(17); return data.Count;", WorkerClaimOutcome.Unknown);
        await CheckInterfaceArrayContract("System.Collections.Generic.ICollection<int> data", "Contract.Requires(data != null); Contract.Ensures(Contract.Result<int>() >= 0); " +
            "return data.Count;", WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task UnknownWritersInvalidateHashSetInterfaceContents()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); System.Collections.Generic.ICollection<int> data = " +
            "new System.Collections.Generic.HashSet<int>(); data.Add(17); Change(data); return data.Contains(17) ? 1 : 0;", WorkerClaimOutcome.Unknown,
            members: "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)] static extern void Change(System.Collections.Generic.ICollection<int> data);");
    }

    [Test]
    public async Task RefEscapesDoNotRetainHashSetInterfaceProvenance()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); System.Collections.Generic.ICollection<int> data = " +
            "new System.Collections.Generic.HashSet<int>(); Change(ref data); data.Add(17); data.Add(17); return data.Count;", WorkerClaimOutcome.Unknown,
            members: "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)] static extern void Change(ref System.Collections.Generic.ICollection<int> data);");
    }

    [Test]
    public async Task CapturedMutableBindingsDoNotRetainHashSetInterfaceProvenance()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); System.Collections.Generic.ICollection<int> data = " +
            "new System.Collections.Generic.HashSet<int>(); System.Action change = () => data = new System.Collections.Generic.List<int>(); " +
            "change(); data.Add(17); data.Add(17); return data.Count;", WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task ReimplementedCollectionInterfacesDoNotUseBaseHashSetStorage()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); System.Collections.Generic.ICollection<int> data = new Other(); return data.Count;",
            WorkerClaimOutcome.Unknown, members: "sealed class Other : System.Collections.Generic.HashSet<int>, System.Collections.Generic.ICollection<int> { int System.Collections.Generic.ICollection<int>.Count => -9; }");
    }
    [Test]
    public async Task HashSetInterfaceReceiverIsCapturedBeforeArgumentRebinding()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); System.Collections.Generic.ICollection<int> data = " +
            "new System.Collections.Generic.HashSet<int>(); var original = data; data.Add((data = new System.Collections.Generic.HashSet<int>()).Count + 17); " +
            "return original.Contains(17) && data.Count == 0 ? 1 : 0;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task HashSetInterfacesDoNotInventDefaultComparerProvenance()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.IEqualityComparer<int> comparer", "Contract.Requires(comparer != null); Contract.Ensures(Contract.Result<int>() == 2); " +
            "System.Collections.Generic.ICollection<int> data = new System.Collections.Generic.HashSet<int>(comparer); data.Add(17); data.Add(23); return data.Count;", WorkerClaimOutcome.Unknown);
    }
    [Test]
    public async Task NullableHashSetInterfaceJoinsPreserveNullReceiverFaults()
    {
        const string body = "System.Collections.Generic.ICollection<int> data = missing ? null : new System.Collections.Generic.HashSet<int>(); " +
            "try { return data.Count; } catch (System.NullReferenceException) { return 7; }";
        await CheckInterfaceArrayContract("bool missing", "Contract.Ensures(Contract.Result<int>() == 0 || Contract.Result<int>() == 7); " + body,
            WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("bool missing", "Contract.Ensures(Contract.Result<int>() == 0); " + body,
            WorkerClaimOutcome.Refuted);
    }

    [Test]
    public async Task ReboundHelperParametersDoNotInheritHashSetInterfaceOrigins()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); var data = new System.Collections.Generic.HashSet<int>(); return Change(data, 17);",
            WorkerClaimOutcome.Unknown, members: "static int Change<T>(System.Collections.Generic.ICollection<T> data, T value) { " +
                "var other = new System.Collections.Generic.List<T>(); other.Add(value); other.Add(value); data = other; return data.Count; }");
    }
}
