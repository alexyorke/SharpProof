using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCaseSource(nameof(GenericArrayReturnCases))]
    public async Task GenericScalarDefaultsUseTheEstablishedScalarRepresentation(string element, bool local, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static T Reset<T>(T value) => default(T);";
        await CheckInterfaceArrayContract(element + " value", "Contract.Ensures(Contract.Result<" + element + ">() " +
            (correct ? "==" : "!=") + " default(" + element + ")); return Reset<" + element + ">(value); " + (local ? helper : ""),
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element, local ? "" : helper);
    }
    [TestCaseSource(nameof(GenericArrayReturnCases))]
    public async Task GenericArrayStoresOfDefaultsPreserveTheScalarDomain(string element, bool local, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static T Reset<T>(T[] values, int index) { values[index] = default(T); return values[index]; }";
        await CheckInterfaceArrayContract(element + "[] values, int index",
            "Contract.Requires(values != null && index >= 0 && index < values.Length); Contract.Ensures(Contract.Result<" + element + ">() " +
            (correct ? "==" : "!=") + " default(" + element + ")); return Reset<" + element + ">(values, index); " + (local ? helper : ""),
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element, local ? "" : helper);
    }

    [Test]
    public async Task GenericScalarDefaultsRetainKnownEffects()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "bool value",
            "return Reset<bool>(value); static T Reset<T>(T input) => default(T);", true, true, true, "bool");
    }

    [Test]
    public async Task GenericDefaultWithoutScalarParameterEvidenceRemainsConservative()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); return Reset<int>(); static T Reset<T>() => default(T);",
            WorkerClaimOutcome.Unknown);
    }
}
