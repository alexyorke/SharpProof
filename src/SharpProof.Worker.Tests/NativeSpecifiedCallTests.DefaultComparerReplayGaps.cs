using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCaseSource(nameof(ClosedGenericIntegerComparerProofCases))]
    public async Task ClosedGenericDefaultComparerWrongContractsHaveReplayableWitnesses(string element, bool local)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static int Compare<T>() => System.Collections.Generic.Comparer<T>.Default.Compare(default(T), default(T));";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() != 0); return Compare<" + element + ">(); " + (local ? helper : ""),
            WorkerClaimOutcome.Refuted, "int", local ? "" : helper);
    }
    [TestCase("var observed = System.Environment.TickCount; var comparer = System.Collections.Generic.Comparer<int>.Default; return observed;")]
    [TestCase("var comparer = System.Collections.Generic.Comparer<int>.Default; return System.Environment.TickCount;")]
    public async Task FrameworkGetterWitnessesCannotConfirmUnrelatedOpaqueReads(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 17); " + body, WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task DefaultComparerReferenceWitnessesPreserveRepeatedGetterIdentity()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<bool>() == false); " +
            "var first = System.Collections.Generic.Comparer<int>.Default; var second = System.Collections.Generic.Comparer<int>.Default; " +
            "return (object)first == (object)second;", WorkerClaimOutcome.Refuted, "bool");
    }

    [Test]
    public async Task DefaultComparerReferenceWitnessesDistinguishEqualWidthPrimitiveTypes()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<bool>() == true); return (object)" +
            "System.Collections.Generic.Comparer<char>.Default == (object)System.Collections.Generic.Comparer<ushort>.Default;",
            WorkerClaimOutcome.Refuted, "bool");
    }
}
