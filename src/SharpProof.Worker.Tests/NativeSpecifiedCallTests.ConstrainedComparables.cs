using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Worker.Protocol;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ConstrainedComparableReceiverLocationsLowerWithoutApproximations(bool field)
    {
        var method = field
            ? "[ZeroAllocations] public static int Target() { return new Box<int> { Value = 17 }.Compare(23); } public sealed class Box<T> where T : System.IComparable<T> { public T Value; public int Compare(T replacement) => Value.CompareTo(Value = replacement); }"
            : "[ZeroAllocations] public static int Target() { return Compare(new int[] { 17, 23 }, new int[] { 23 }); } static int Compare<T>(T[] data, T[] other) where T : System.IComparable<T> => data[0].CompareTo((data = other)[0]);";
        var preparation = Prepare(method);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        var messages = candidate.Program.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrHavocInstruction>().Select(instruction => instruction is null ? throw new ArgumentNullException(nameof(instruction)) :
                candidate.Factory.GetString(candidate.Factory.GetOperationInfo(instruction.Operation).Description!.Value)).ToImmutableArray();
        Assert.That(messages, Is.Empty);
    }

    [Test]
    public async Task ConstrainedComparableConditionalArgumentsKeepTheirManagedReceiverLocation()
    {
        await CheckInterfaceArrayContract("bool choose", "Contract.Ensures(Contract.Result<int>() == 0); return Compare(17, 23, choose);",
            WorkerClaimOutcome.Proven, members: "static int Compare<T>(T value, T replacement, bool choose) where T : System.IComparable<T> => value.CompareTo(choose ? (value = replacement) : (value = default(T))); ");
        await CheckInterfaceArrayContract("bool choose", "Contract.Ensures(Contract.Result<int>() == 0); return Compare(new int[] { 17 }, 23, choose);",
            WorkerClaimOutcome.Proven, members: "static int Compare<T>(T[] data, T replacement, bool choose) where T : System.IComparable<T> => data[0].CompareTo(choose ? (data[0] = replacement) : (data[0] = default(T))); ");
        await CheckInterfaceArrayContract("bool choose", "Contract.Ensures(Contract.Result<int>() == 0); return new Box<int> { Value = 17 }.Compare(23, choose);",
            WorkerClaimOutcome.Proven, members: "public sealed class Box<T> where T : System.IComparable<T> { public T Value; public int Compare(T replacement, bool choose) => Value.CompareTo(choose ? (Value = replacement) : (Value = default(T))); }");
    }

    [Test]
    public async Task ConstrainedComparableNullSuppressionPreservesVariableClassification()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); return Compare(17, 23);",
            WorkerClaimOutcome.Proven, members: "static int Compare<T>(T value, T replacement) where T : System.IComparable<T> => (value!).CompareTo(value = replacement);");
    }

    [Test]
    public async Task PrimitiveComparableStaticFieldLocationsRetainTheirUnsupportedBoundary()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); value = 17; return value.CompareTo(value = 23);",
            WorkerClaimOutcome.Unknown, members: "static int value;");
    }

    [Test]
    public async Task ConstrainedIntegerComparableHasNoBoxingAllocationOrCallbackEffects()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int left, int right",
            "return Compare(left, right); static int Compare<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y);", true, true, true);
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
    public async Task ConstrainedIntegerComparablesPreserveExactBoundaryArithmetic(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var expected = element is "sbyte" or "byte" or "short" or "ushort" or "char" ? "(int)left - (int)right" : null;
        await CheckComparisonContract(element, "return Compare(left, right); static int Compare<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y);",
            WorkerClaimOutcome.Proven, expected);
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
    public async Task ConstrainedIntegerComparablesRejectIncorrectBoundaryClaims(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckComparisonContract(element, "return Compare(left, right); static int Compare<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y);",
            WorkerClaimOutcome.Refuted, "42");
    }

    [Test]
    public async Task ConstrainedIntegerComparableImportsItsTypedPrecondition()
    {
        await CheckInterfaceArrayContract("int left, int right", "Contract.Requires(left <= right); Contract.Ensures(Contract.Result<int>() <= 0); return Compare(left, right);",
            WorkerClaimOutcome.Proven, members: "static int Compare<T>(T x, T y) where T : System.IComparable<T> { Contract.Requires(x.CompareTo(y) <= 0); return x.CompareTo(y); }");
    }

    [Test]
    public async Task ConstrainedIntegerComparableArgumentMutationReadsTheCurrentValueReceiver()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); return Compare(17, 23);",
            WorkerClaimOutcome.Proven, members: "static int Compare<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(x = y);");
    }

    [TestCase("data[0].CompareTo(data[0] = replacement)", 0)]
    [TestCase("data[index].CompareTo(data[index = 1])", -1)]
    [TestCase("data[0].CompareTo((data = other)[0])", -1)]
    public async Task ConstrainedIntegerComparableArrayReceiversPreserveTheirManagedAddress(string expression, int expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); return Compare(new int[] { 17, 23 }, new int[] { 23 }, 0, 23);",
            WorkerClaimOutcome.Proven, members: "static int Compare<T>(T[] data, T[] other, int index, T replacement) where T : System.IComparable<T> => " + expression + ";");
    }

    [TestCase("(choose ? value : value).CompareTo(value = replacement)")]
    [TestCase("Copy(value).CompareTo(value = replacement)")]
    public async Task ConstrainedIntegerComparableValueExpressionsKeepTheirEarlierCopy(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        await CheckInterfaceArrayContract("bool choose", "Contract.Ensures(Contract.Result<int>() == -1); return Compare(17, 23, choose);",
            WorkerClaimOutcome.Proven, members: "static T Copy<T>(T value) => value; static int Compare<T>(T value, T replacement, bool choose) where T : System.IComparable<T> => " + expression + ";");
    }

    [TestCase(false, 0)]
    [TestCase(true, -1)]
    public async Task ConstrainedIntegerComparableFieldsAndPropertiesHaveDifferentCopySemantics(bool property, int expected)
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); return new Box<int> { Value = 17 }.Compare(23);",
            WorkerClaimOutcome.Proven, members: "public sealed class Box<T> where T : System.IComparable<T> { public T Value" + (property ? " { get; set; }" : ";") +
            " public int Compare(T replacement) => Value.CompareTo(Value = replacement); }");
    }

    [TestCase("value.Value.CompareTo((value = other).Value)", -1)]
    [TestCase("value.Value.CompareTo(value.Value = replacement)", 0)]
    public async Task ConstrainedIntegerComparableFieldAddressesKeepTheirEarlierOwner(string expression, int expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); return Compare(new Box<int> { Value = 17 }, new Box<int> { Value = 23 }, 23);",
            WorkerClaimOutcome.Proven, members: "public sealed class Box<T> { public T Value; } static int Compare<T>(Box<T> value, Box<T> other, T replacement) where T : System.IComparable<T> => " + expression + ";");
    }

    [Test]
    public async Task ConstrainedIntegerComparableFieldSlotsRemainDistinctAcrossConstructedOwners()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); return new Box<sbyte> { Value = 17 }.Compare(23) + new Box<long> { Value = 17 }.Compare(23);",
            WorkerClaimOutcome.Proven, members: "public sealed class Box<T> where T : System.IComparable<T> { public T Value; public int Compare(T replacement) => Value.CompareTo(Value = replacement); }");
    }

    [Test]
    public async Task ConstrainedIntegerComparableDoesNotClaimRefConditionalReceiverSupport()
    {
        await CheckInterfaceArrayContract("bool choose", "Contract.Ensures(Contract.Result<int>() == 0); return Compare(17, 23, choose);",
            WorkerClaimOutcome.Unknown, members: "static int Compare<T>(T value, T replacement, bool choose) where T : System.IComparable<T> => (choose ? ref value : ref replacement).CompareTo(value = replacement);");
    }

    [TestCase("null", "System.NullReferenceException")]
    [TestCase("new int[0]", "System.IndexOutOfRangeException")]
    public async Task ConstrainedIntegerComparableArrayAddressFaultsPrecedeArgumentWrites(string data, string exception)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(exception);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 17); var effects = new int[] { 17 }; try { Compare<int>(" + data + ", effects, 23); } catch (" + exception + ") { return effects[0]; } return 0;",
            WorkerClaimOutcome.Proven, members: "static int Compare<T>(T[] data, T[] effects, T replacement) where T : System.IComparable<T> => data[0].CompareTo(effects[0] = replacement);");
    }

    [Test]
    public async Task ConstrainedIntegerComparableContainingTypeArgumentsRemainDistinct()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 254); return Box<sbyte>.Compare(sbyte.MaxValue, sbyte.MinValue) + Box<ulong>.Compare(0, ulong.MaxValue);",
            WorkerClaimOutcome.Proven, members: "public static class Box<T> where T : System.IComparable<T> { public static int Compare(T x, T y) => x.CompareTo(y); }");
    }

    [Test]
    public async Task ConstrainedIntegerComparableDoesNotEraseExplicitInterfaceBoxing()
    {
        await CheckComparisonContract("int", "return ((System.IComparable<int>)left).CompareTo(right);", WorkerClaimOutcome.Unknown);
    }

    [TestCase("bool", "false", "true")]
    [TestCase("double", "0.0", "1.0")]
    [TestCase("decimal", "0m", "1m")]
    public async Task NonIntegerConstrainedComparableImplementationsRemainOpaque(string element, string left, string right)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == -1); return Compare<" + element + ">(" + left + ", " + right + ");",
            WorkerClaimOutcome.Unknown, members: "static int Compare<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y);");
    }

    [Test]
    public async Task CustomConstrainedComparableRetainsItsCallbackBehavior()
    {
        await CheckInterfaceArrayContract("Custom value", "Contract.Requires(value != null); Contract.Ensures(Contract.Result<int>() == 0); return Compare(value, value);",
            WorkerClaimOutcome.Unknown, members: "static int Compare<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y); public sealed class Custom : System.IComparable<Custom> { public int CompareTo(Custom other) => 42; }");
    }

    [TestCaseSource(nameof(ClosedGenericIntegerComparerProofCases))]
    public async Task ConstrainedIntegerComparablesProveEqualValues(string element, bool local)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static int Compare<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y);";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); return Compare<" + element + ">(default(" + element + "), default(" + element + ")); " + (local ? helper : ""),
            WorkerClaimOutcome.Proven, "int", local ? "" : helper);
    }

    [TestCaseSource(nameof(ClosedGenericIntegerComparerProofCases))]
    public async Task ConstrainedIntegerComparableWrongClaimsAreRefuted(string element, bool local)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static int Compare<T>(T x, T y) where T : System.IComparable<T> => x.CompareTo(y);";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() != 0); return Compare<" + element + ">(default(" + element + "), default(" + element + ")); " + (local ? helper : ""),
            WorkerClaimOutcome.Refuted, "int", local ? "" : helper);
    }
}
