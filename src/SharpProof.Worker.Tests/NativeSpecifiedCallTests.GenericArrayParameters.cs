using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCaseSource(nameof(GenericArrayReturnCases))]
    public async Task GenericArrayParameterReadsPreserveScalarContents(string element, bool local, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static T Read<T>(T[] list, int index) => list[index];";
        await CheckInterfaceArrayContract(element + "[] values, int index",
            "Contract.Requires(values != null && index >= 0 && index < values.Length); " +
            "Contract.Ensures(Contract.Result<" + element + ">() " + (correct ? "==" : "!=") + " values[index]); " +
            "return Read<" + element + ">(values, index); " + (local ? helper : ""),
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element, local ? "" : helper);
    }

    [TestCaseSource(nameof(GenericArrayReturnCases))]
    public async Task GenericArrayParameterSwapPreservesAllContents(string element, bool local, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static void Swap<T>(T[] list, int first, int second) { var old = list[first]; list[first] = list[second]; list[second] = old; }";
        var expected = correct ? "(read == first ? Contract.Old(values[second]) : read == second ? Contract.Old(values[first]) : Contract.Old(values[read]))" :
            "Contract.Old(values[read])";
        await CheckInterfaceArrayContract(element + "[] values, int first, int second, int read",
            "Contract.Requires(values != null && first >= 0 && first < values.Length && second >= 0 && second < values.Length && read >= 0 && read < values.Length); " +
            "Contract.Ensures(Contract.Result<" + element + ">() == " + expected + "); " +
            "Swap<" + element + ">(values, first, second); return values[read]; " + (local ? helper : ""),
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element, local ? "" : helper);
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task GenericArrayParameterRebindingUsesTheNewReceiver(bool correct)
    {
        await CheckInterfaceArrayContract("int[] values, int[] other",
            "Contract.Requires(values != null && values.Length > 0 && other != null && other.Length > 0 && values[0] == 17 && other[0] == 23); " +
            "Contract.Ensures(Contract.Result<int>() == " + (correct ? "23" : "17") + "); return Read<int>(values, other); " +
            "static T Read<T>(T[] first, T[] second) { first = second; return first[0]; }",
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted);
    }

    [Test]
    public async Task GenericArrayParameterStoresAfterRebindingPreserveTheOriginalArray()
    {
        await CheckInterfaceArrayContract("int[] values, int[] other",
            "Contract.Requires(values != null && values.Length > 0 && other != null && other.Length > 1 && values != other); " +
            "Contract.Ensures(Contract.Result<int>() == Contract.Old(values[0])); Change<int>(values, other); return values[0]; " +
            "static void Change<T>(T[] first, T[] second) { first = second; first[0] = first[1]; }", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task GenericArrayParametersPreserveReadWriteAndFaultEffects()
    {
        const string read = "return Read<int>(values, index); static T Read<T>(T[] list, int at) => list[at];";
        const string requires = "Contract.Requires(values != null && index >= 0 && index < values.Length); ";
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] values, int index", requires + read, true, true, true);
        await CheckInterfaceArrayEffects("[DoesNotThrow]", "int[] values, int index", read, false, null, null);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] values, int index", requires +
            "Write<int>(values, index); return values[index]; static void Write<T>(T[] list, int at) { list[at] = list[at]; }", true, true, false);
    }
}
