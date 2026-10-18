using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCaseSource(nameof(GenericArrayReturnCases))]
    public async Task ClosedGenericScalarDefaultsUseTheirDeclaredTypeArguments(string element, bool local, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static T Reset<T>() => default(T);";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<" + element + ">() " +
            (correct ? "==" : "!=") + " default(" + element + ")); return Reset<" + element + ">(); " + (local ? helper : ""),
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element, local ? "" : helper);
    }
    [TestCaseSource(nameof(GenericArrayReturnCases))]
    public async Task ClosedGenericResultUsesItsOwnTypeParameter(string element, bool local, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static U Reset<T, U>(T[] values) => default(U);";
        await CheckInterfaceArrayContract("int[] values", "Contract.Ensures(Contract.Result<" + element + ">() " +
            (correct ? "==" : "!=") + " default(" + element + ")); return Reset<int, " + element + ">(values); " + (local ? helper : ""),
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element, local ? "" : helper);
    }

    [TestCaseSource(nameof(GenericArrayReturnCases))]
    public async Task ClosedContainingTypesPreserveNestedScalarArguments(string element, bool local, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        var helper = "static " + element + " Reset() => Outer<int>.Reset<" + element + ">();";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<" + element + ">() " +
            (correct ? "==" : "!=") + " default(" + element + ")); return Reset(); " + (local ? helper : ""),
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element, (local ? "" : helper) +
            " static class Outer<T> { public static U Reset<U>() => Inner<T, U>.Reset(); " +
            "static class Inner<V, W> { public static W Reset() => default(W); } }");
    }
}
