using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("Contract.Requires(data != null); var before = data.Count; return data.Count - before;", 0)]
    [TestCase("Contract.Requires(data != null && data.Count > 0); data[0] = 17; return data[0];", 17)]
    [TestCase("Contract.Requires(data != null && data.Count > 0 && data[0] == 23); return data[0];", 23)]
    [TestCase("Contract.Requires(data != null && data.Count < 100); var before = data.Count; data.Add(17); return data.Count - before;", 1)]
    [TestCase("Contract.Requires(data != null); var index = data.Count; data.Add(29); return data[index];", 29)]
    [TestCase("Contract.Requires(data != null && data.Count > 0); var before = data.Count; data[0] = 17; return data.Count - before;", 0)]
    [TestCase("Contract.Requires(data != null && data.Count > 0); var alias = data; alias[0] = 31; return data[0];", 31)]
    [TestCase("Contract.Requires(data != null && data.Count == 23); return data.Count;", 23)]
    public async Task ConcreteListParametersShareCountAndContents(string body, int expected)
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.List<int> data", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body,
            WorkerClaimOutcome.Proven);
    }

    [TestCase("System.Collections.Generic.List<int>", "Contract.Requires(data != null && data.Count == 1 && data[0] == 23); return data[0];", "24", WorkerClaimOutcome.Refuted)]
    [TestCase("System.Collections.Generic.List<int>", "Contract.Requires(data != null && data.Count == 1); data[0] = 17; return data[0];", "18", WorkerClaimOutcome.Refuted)]
    [TestCase("System.Collections.Generic.List<object>", "Contract.Requires(data != null && data.Count == 1 && data[0] == null); return data[0] == null ? 1 : 0;", "1", WorkerClaimOutcome.Proven)]
    [TestCase("System.Collections.Generic.List<bool>", "Contract.Requires(data != null && data.Count == 1 && data[0]); return data[0] ? 1 : 0;", "1", WorkerClaimOutcome.Proven)]
    [TestCase("System.Collections.Generic.IList<int>", "Contract.Requires(data != null); data[0] = 17; return data[0];", "17", WorkerClaimOutcome.Unknown)]
    public async Task ConcreteListEntryClaimsPreserveDomainsAndRejectWrongClaims(string type, string body, string expected, WorkerClaimOutcome outcome)
    {
        await CheckInterfaceArrayContract(type + " data", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body, outcome);
    }

    [Test]
    public async Task ConcreteListSourceHelpersShareCallerState()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 240); var data = new System.Collections.Generic.List<int>() { 17 }; return Use(data);",
            WorkerClaimOutcome.Proven, members: "private static int Use(System.Collections.Generic.List<int> data) { data.Add(23); return data.Count * 100 + data[0] + data[1]; }");
    }

    [TestCase("sbyte", "sbyte.MinValue")]
    [TestCase("byte", "byte.MaxValue")]
    [TestCase("short", "short.MinValue")]
    [TestCase("ushort", "ushort.MaxValue")]
    [TestCase("char", "char.MaxValue")]
    [TestCase("int", "int.MinValue")]
    [TestCase("uint", "uint.MaxValue")]
    [TestCase("long", "long.MinValue")]
    [TestCase("ulong", "ulong.MaxValue")]
    public async Task ConcreteListEntryContentsKeepScalarBits(string type, string extreme)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(extreme);
        await CheckInterfaceArrayContract("System.Collections.Generic.List<" + type + "> data",
            "Contract.Requires(data != null && data.Count == 1 && data[0] == " + extreme + "); Contract.Ensures(Contract.Result<int>() == 1); return data[0] == " + extreme + " ? 1 : 0;",
            WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ConcreteListEntrySnapshotsSurviveLaterStores()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.List<int> data",
            "Contract.Requires(data != null && data.Count == 1 && data[0] == 23); Contract.Ensures(Contract.Result<int>() == Contract.Old(data[0])); data[0] = 31; return 23;",
            WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("System.Collections.Generic.List<int> data",
            "Contract.Requires(data != null && data.Count == 1); Contract.Ensures(data.Count == Contract.Old(data.Count)); data[0] = 31; return 0;",
            WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ConcreteListUnknownMutationDiscardsEntryCells()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.List<int> data",
            "Contract.Requires(data != null && data.Count == 1 && data[0] == 23); Contract.Ensures(Contract.Result<int>() == 23); data.Reverse(); return data[0];",
            WorkerClaimOutcome.Unknown);
    }
}
