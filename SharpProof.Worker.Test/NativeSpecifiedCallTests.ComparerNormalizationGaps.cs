using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("comparer = comparer ?? System.Collections.Generic.Comparer<T>.Default;")]
    [TestCase("comparer ??= System.Collections.Generic.Comparer<T>.Default;")]
    [TestCase("if (comparer == null) comparer = System.Collections.Generic.Comparer<T>.Default;")]
    public async Task NullComparerSourceParametersNormalizeToPrimitiveDefault(string normalization)
    {
        ArgumentNullException.ThrowIfNull(normalization);
        await CheckComparisonContract("int", "return Compare<int>(null, left, right); static int Compare<T>(System.Collections.Generic.Comparer<T> comparer, T x, T y) { " +
            normalization + " return comparer.Compare(x, y); }", WorkerClaimOutcome.Proven);
    }

    [TestCase("comparer = comparer ?? System.Collections.Generic.Comparer<T>.Default;")]
    [TestCase("comparer ??= System.Collections.Generic.Comparer<T>.Default;")]
    [TestCase("if (comparer == null) comparer = System.Collections.Generic.Comparer<T>.Default;")]
    public async Task DefaultComparerSourceParametersRetainIdentityThroughNormalization(string normalization)
    {
        ArgumentNullException.ThrowIfNull(normalization);
        await CheckComparisonContract("int", "return Compare<int>(System.Collections.Generic.Comparer<int>.Default, left, right); " +
            "static int Compare<T>(System.Collections.Generic.Comparer<T> comparer, T x, T y) { " + normalization + " return comparer.Compare(x, y); }",
            WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task NormalizedDefaultComparerRejectsAnIncorrectContract()
    {
        await CheckComparisonContract("int", "return Compare<int>(null, left, right); static int Compare<T>(System.Collections.Generic.Comparer<T> comparer, T x, T y) { " +
            "comparer ??= System.Collections.Generic.Comparer<T>.Default; return comparer.Compare(x, y); }", WorkerClaimOutcome.Refuted, "42");
    }

    [Test]
    public async Task AComparerThatRemainsNullRetainsItsNullReceiverFault()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<bool>()); try { Compare<int>(null, 1, 2); } catch (System.NullReferenceException) { return true; } return false; " +
            "static int Compare<T>(System.Collections.Generic.Comparer<T> comparer, T x, T y) { return comparer.Compare(x, y); }", WorkerClaimOutcome.Proven, "bool");
    }

    [TestCase("comparer ??= System.Collections.Generic.Comparer<T>.Default;")]
    [TestCase("comparer = comparer ?? System.Collections.Generic.Comparer<T>.Default;")]
    public async Task NormalizationDoesNotTurnCustomComparersIntoDefaultComparers(string normalization)
    {
        ArgumentNullException.ThrowIfNull(normalization);
        await CheckComparisonContract("int", "return Compare<int>(System.Collections.Generic.Comparer<int>.Create((a, b) => 42), left, right); " +
            "static int Compare<T>(System.Collections.Generic.Comparer<T> comparer, T x, T y) { " + normalization + " return comparer.Compare(x, y); }",
            WorkerClaimOutcome.Unknown);
    }
    [TestCase("comparer = System.Collections.Generic.Comparer<int>.Create((a, b) => 42);")]
    [TestCase("if (x == y) comparer = System.Collections.Generic.Comparer<int>.Create((a, b) => 42);")]
    [TestCase("ref var alias = ref comparer; alias = System.Collections.Generic.Comparer<int>.Create((a, b) => 42);")]
    [TestCase("System.Action change = () => comparer = System.Collections.Generic.Comparer<int>.Create((a, b) => 42); change();")]
    public async Task MutableComparerProvenanceRejectsCustomWritesAndEscapes(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await CheckComparisonContract("int", "return Compare(System.Collections.Generic.Comparer<int>.Default, left, right); " +
            "static int Compare(System.Collections.Generic.Comparer<int> comparer, int x, int y) { " + mutation + " return comparer.Compare(x, y); }",
            WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task NullableComparerReceiverCheckFollowsArgumentSideEffects()
    {
        await CheckInterfaceArrayContract("int[] values", "Contract.Requires(values != null && values.Length > 0); Contract.Ensures(Contract.Result<int>() == 19); " +
            "try { Compare(null, values); } catch (System.NullReferenceException) { return values[0]; } return 0; " +
            "static int Compare(System.Collections.Generic.Comparer<int> comparer, int[] data) { return comparer.Compare(data[0] = 17, data[0] = 19); }",
            WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ConditionalDefaultAssignmentRetainsConservativeGetterFaults()
    {
        await CheckInterfaceArrayContract("bool initialize", "Contract.Ensures(Contract.Result<bool>() == !initialize); " +
            "try { Compare(null, initialize); } catch (System.NullReferenceException) { return true; } return false; " +
            "static int Compare(System.Collections.Generic.Comparer<int> comparer, bool initialize) { if (initialize) comparer = System.Collections.Generic.Comparer<int>.Default; return comparer.Compare(1, 2); }",
            WorkerClaimOutcome.Unknown, "bool");
    }
    [Test]
    public async Task ConditionalDefaultAssignmentPreservesAnUninitializedNullReceiver()
    {
        await CheckInterfaceArrayContract("bool initialize", "Contract.Requires(!initialize); Contract.Ensures(Contract.Result<bool>()); " +
            "try { Compare(null, initialize); } catch (System.NullReferenceException) { return true; } return false; " +
            "static int Compare(System.Collections.Generic.Comparer<int> comparer, bool initialize) { if (initialize) comparer = System.Collections.Generic.Comparer<int>.Default; return comparer.Compare(1, 2); }",
            WorkerClaimOutcome.Proven, "bool");
    }
    [TestCaseSource(nameof(ClosedGenericIntegerComparerProofCases))]
    public async Task NullableComparerNormalizationKeepsEveryPrimitiveRepresentation(string element, bool local)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static int Compare<T>(System.Collections.Generic.Comparer<T> comparer, T x, T y) { comparer ??= System.Collections.Generic.Comparer<T>.Default; return comparer.Compare(x, y); }";
        var expected = element is "char" or "sbyte" or "byte" or "short" or "ushort" ? "(int)left - (int)right" : "left < right ? -1 : left > right ? 1 : 0";
        await CheckInterfaceArrayContract(element + " left, " + element + " right",
            "Contract.Ensures(Contract.Result<int>() == (" + expected + ")); return Compare<" + element + ">(null, left, right); " + (local ? helper : ""),
            WorkerClaimOutcome.Proven, "int", local ? "" : helper);
    }

    [TestCaseSource(nameof(ClosedGenericIntegerComparerProofCases))]
    public async Task NullableComparerNormalizationHasConcretePrimitiveWrongClaimWitnesses(string element, bool local)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static int Compare<T>(System.Collections.Generic.Comparer<T> comparer, T x, T y) { comparer ??= System.Collections.Generic.Comparer<T>.Default; return comparer.Compare(x, y); }";
        await CheckInterfaceArrayContract(element + " left, " + element + " right",
            "Contract.Ensures(Contract.Result<int>() == 42); return Compare<" + element + ">(null, left, right); " + (local ? helper : ""),
            WorkerClaimOutcome.Refuted, "int", local ? "" : helper);
    }

    [Test]
    public async Task NullableComparerNormalizationPreservesProvenanceThroughNestedHelpers()
    {
        await CheckComparisonContract("int", "return Compare<int>(null, left, right); " +
            "static int Compare<T>(System.Collections.Generic.Comparer<T> comparer, T x, T y) { comparer ??= System.Collections.Generic.Comparer<T>.Default; return Next(comparer, x, y); } " +
            "static int Next<T>(System.Collections.Generic.Comparer<T> comparer, T x, T y) { return comparer.Compare(x, y); }", WorkerClaimOutcome.Proven);
    }
    [Test]
    public async Task ComparerAliasBeforeParameterResetRetainsUnknownEntryDispatch()
    {
        await CheckComparisonContract("int", "return Compare(System.Collections.Generic.Comparer<int>.Create((a, b) => 42), left, right); " +
            "static int Compare(System.Collections.Generic.Comparer<int> value, int x, int y) { var before = value; value = System.Collections.Generic.Comparer<int>.Default; return before.Compare(x, y); }",
            WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task InterfaceAliasBeforeParameterResetRetainsUnknownEntryRepresentation()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.IList<int> value", "Contract.Ensures(Contract.Result<int>() == 1); " +
            "var before = value; value = new int[] { 1 }; return before[0];", WorkerClaimOutcome.Unknown);
    }
    [Test]
    public async Task NullableComparerNormalizationModelsAnOmittedOptionalArgument()
    {
        await CheckComparisonContract("int", "return Compare(left, right); static int Compare<T>(T x, T y, System.Collections.Generic.Comparer<T> comparer = null) { " +
            "comparer ??= System.Collections.Generic.Comparer<T>.Default; return comparer.Compare(x, y); }", WorkerClaimOutcome.Proven);
    }
}
