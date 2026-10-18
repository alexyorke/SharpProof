using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); return data.Count;", 0)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(17, 23); return data[17];", 23)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(17, 23); return data.ContainsKey(17) ? 1 : 0;", 1)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(17, 23); return data.ContainsKey(18) ? 1 : 0;", 0)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); data[17] = 23; data[17] = 29; return data.Count == 1 && data[17] == 29 ? 1 : 0;", 1)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); var alias = data; alias.Add(17, 23); return data[17];", 23)]
    public async Task DictionaryStateGapFreshDefaultIntegerState(string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body, WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task DictionaryStateGapIncorrectValueClaimHasAConcreteWitness()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 24); " +
            "var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(17, 23); return data[17];", WorkerClaimOutcome.Refuted);
    }

    [Test]
    public async Task DictionaryStateGapUnknownEntryComparersCannotAcquireDefaultSemantics()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Dictionary<int, int> data",
            "Contract.Ensures(Contract.Result<int>() == 23); data.Add(17, 23); return data[17];", WorkerClaimOutcome.Unknown);
    }
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(17, 23); try { data.Add(17, 29); } catch (System.ArgumentException) { return data[17]; } return 0;", 23)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(); try { return data[17]; } catch (System.Collections.Generic.KeyNotFoundException) { return 123; }", 123)]
    public async Task DictionaryStateGapResourceAndExceptionPathsRemainOpen(string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body, WorkerClaimOutcome.Unknown);
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
    public async Task DictionaryStateGapPrimitiveBoundaryKeysProveAndRefute(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var body = "var data = new System.Collections.Generic.Dictionary<" + type + ", bool>(); " +
            "var alias = data; alias.Add(value: true, key: " + type + ".MinValue); " +
            "data[" + type + ".MaxValue] = false; " +
            "return data.Count == 2 && alias[" + type + ".MinValue] && !data[" + type + ".MaxValue] ? 1 : 0;";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " + body, WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); " + body, WorkerClaimOutcome.Refuted);
    }

    [Test]
    public async Task DictionaryStateGapUnknownWriterInvalidatesEntries()
    {
        await CheckInterfaceArrayContract("Mutator change", "Contract.Requires(change != null); Contract.Ensures(Contract.Result<int>() == 23); " +
            "var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(17, 23); change.Change(data); return data[17];",
            WorkerClaimOutcome.Unknown, members: "public abstract class Mutator { public abstract void Change(System.Collections.Generic.Dictionary<int, int> data); }");
    }

    [Test]
    public async Task DictionaryStateGapAliasBeforeParameterResetRemainsUnknown()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.Dictionary<int, int> data",
            "Contract.Ensures(Contract.Result<int>() == 23); var alias = data; data = new System.Collections.Generic.Dictionary<int, int>(); " +
            "alias.Add(17, 23); return alias[17];", WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task DictionaryStateGapCustomComparerConstructorRemainsUnknown()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.IEqualityComparer<int> comparer",
            "Contract.Ensures(Contract.Result<int>() == 23); var data = new System.Collections.Generic.Dictionary<int, int>(comparer); " +
            "data.Add(17, 23); return data[17];", WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task DictionaryStateGapSnapshotsKeyBeforeRightHandSideMutation()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " +
            "var data = new System.Collections.Generic.Dictionary<int, int>(); data[data.Count] = (data[17] = 23); " +
            "return data.Count == 2 && data.ContainsKey(0) && data[0] == 23 ? 1 : 0;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task DictionaryStateGapFrameworkInitializationRetainsFaultUncertainty()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow]", "", "var data = new System.Collections.Generic.Dictionary<int, int>(); " +
            "data.Add(17, 23); return data[17];", noThrow: false, noAllocation: null, pure: null);
    }
    [TestCase("int")]
    [TestCase("long")]
    [TestCase("ulong")]
    public async Task DictionaryStateGapSymbolicKeysUseTypedEquality(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        await CheckInterfaceArrayContract(type + " key, " + type + " other",
            "Contract.Ensures(Contract.Result<int>() == 1); var data = new System.Collections.Generic.Dictionary<" + type + ", int>(); " +
            "data.Add(key, 23); if (key != other) data.Add(other, 29); " +
            "return data[key] == 23 && data.Count == (key == other ? 1 : 2) && data.ContainsKey(key) ? 1 : 0;", WorkerClaimOutcome.Proven);
    }
    [Test]
    public async Task DictionaryStateGapMixedReceiverJoinRemainsUnknown()
    {
        await CheckInterfaceArrayContract("bool choose, System.Collections.Generic.Dictionary<int, int> incoming",
            "Contract.Ensures(Contract.Result<int>() == 23); var data = choose ? new System.Collections.Generic.Dictionary<int, int>() : incoming; " +
            "data.Add(17, 23); return data[17];", WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task DictionaryStateGapFreshReceiverJoinsKeepEmptyState()
    {
        await CheckInterfaceArrayContract("bool choose", "Contract.Ensures(Contract.Result<int>() == 0); " +
            "var data = choose ? new System.Collections.Generic.Dictionary<int, int>() : new System.Collections.Generic.Dictionary<int, int>(); return data.Count;",
            WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task DictionaryStateGapRefEscapeCannotRetainDefaultComparerProvenance()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.IEqualityComparer<int> comparer",
            "Contract.Ensures(Contract.Result<int>() == 23); var data = new System.Collections.Generic.Dictionary<int, int>(); " +
            "Reset(ref data, comparer); data.Add(17, 23); return data[17];", WorkerClaimOutcome.Unknown,
            members: "private static void Reset(ref System.Collections.Generic.Dictionary<int, int> data, System.Collections.Generic.IEqualityComparer<int> comparer) { data = new System.Collections.Generic.Dictionary<int, int>(comparer); }");
    }

    [Test]
    public async Task DictionaryStateGapCapturedMutableReceiverRemainsUnknown()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.IEqualityComparer<int> comparer",
            "Contract.Ensures(Contract.Result<int>() == 23); var data = new System.Collections.Generic.Dictionary<int, int>(); " +
            "System.Action reset = () => data = new System.Collections.Generic.Dictionary<int, int>(comparer); " +
            "reset(); data.Add(17, 23); return data[17];", WorkerClaimOutcome.Unknown);
    }
}
