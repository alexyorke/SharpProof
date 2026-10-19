using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase("int", "0")]
    [TestCase("bool", "false")]
    [TestCase("byte", "0")]
    [TestCase("sbyte", "0")]
    [TestCase("short", "0")]
    [TestCase("ushort", "0")]
    [TestCase("char", "'\\0'")]
    [TestCase("uint", "0U")]
    [TestCase("long", "0L")]
    [TestCase("ulong", "0UL")]
    public async Task ArrayClearZeroesOnlyItsSymbolicRange(string element, string zero)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(zero);
        var body = "Contract.Requires(data != null && start >= 0 && length >= 0 && start <= data.Length && length <= data.Length - start && at >= 0 && at < data.Length); " +
            "Contract.Ensures(Contract.Result<" + element + ">() == (at >= start && at - start < length ? " + zero + " : Contract.Old(data[at]))); " +
            "var alias = data; System.Array.Clear(data, start, length); return alias[at];";
        await CheckInterfaceArrayContract(element + "[] data, int start, int length, int at", body, WorkerClaimOutcome.Proven, result: element);
    }

    [TestCase("System.Array.Clear(data, 1, 2)", 9)]
    [TestCase("System.Array.Clear(data)", 0)]
    public async Task ArrayClearNativeProofAndWrongClaim(string clear, int expected)
    {
        ArgumentNullException.ThrowIfNull(clear);
        var body = "var data = new int[] { 4, 5, 6, 5 }; " + clear + "; return data[0] + data[1] + data[2] + data[3];";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body, WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + (expected + 1) + "); " + body, WorkerClaimOutcome.Refuted);
    }

    [Test]
    public async Task ArrayClearSourceHelpersPreserveGenericStorageAndCallerOldState()
    {
        await CheckInterfaceArrayContract("int[] data", "Contract.Requires(data != null && data.Length > 1); Contract.Ensures(Contract.Result<int>() == 0); Clear<int>(data); return data[1];", WorkerClaimOutcome.Proven,
            members: "static void Clear<T>(T[] data) { Forward<T>(data); } static void Forward<U>(U[] data) { System.Array.Clear(data, 0, data.Length); }");
        await CheckInterfaceArrayContract("int[] data", "Contract.Requires(data != null && data.Length > 1); Contract.Ensures(Contract.Result<int>() == 1); Clear<int>(data); return data[1];", WorkerClaimOutcome.Refuted,
            members: "static void Clear<T>(T[] data) { Forward<T>(data); } static void Forward<U>(U[] data) { System.Array.Clear(data, 0, data.Length); }");
    }

    [Test]
    public async Task ArrayClearDoesNotSurviveAnOpaqueLaterWriter()
    {
        await CheckInterfaceArrayContract("int[] data", "Contract.Requires(data != null && data.Length > 0); Contract.Ensures(Contract.Result<int>() == 0); System.Array.Clear(data); System.Array.Copy(new int[] { 17 }, data, 1); return data[0];", WorkerClaimOutcome.Unknown);
    }

    [TestCase("int[] data = null; var state = 0; try { System.Array.Clear(data, state = 1, state = 2); return -1; } catch (System.Exception) { return state; }", 2)]
    [TestCase("int[] data = null; var state = 0; var value = int.MaxValue; try { System.Array.Clear(data, checked(value + 1), state = 7); return -1; } catch (System.OverflowException) { return state; } catch (System.Exception) { return state + 10; }", 0)]
    public async Task ArrayClearArgumentFaultsAndAssignmentsPrecedeNullChecks(string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body, WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ArrayClearValidRangeDoesNotThrowOrAllocateButWrites()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] data, int start, int length",
            "Contract.Requires(data != null && start >= 0 && length >= 0 && start <= data.Length && length <= data.Length - start); System.Array.Clear(data, start, length); return 0;",
            noThrow: true, noAllocation: true, pure: false);
    }

    [Test]
    public async Task ArrayClearUnpublishedArrayHasNoObservableWrite()
    {
        await CheckInterfaceArrayEffects("[EnforcePure]", "", "var data = new int[4]; System.Array.Clear(data, 0, 4); return data[0];", noThrow: null, noAllocation: null, pure: true);
    }

    [Test]
    public async Task ArrayClearEmptyRangeHasNoObservableWrite()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] data",
            "Contract.Requires(data != null); System.Array.Clear(data, data.Length, 0); return 0;", noThrow: true, noAllocation: true, pure: true);
    }

    [Test]
    public async Task ArrayClearDoesNotEraseFailingReferenceDowncast()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); try { System.Array.Clear((string[])(object)new object[] { new object() }, 0, 1); return 0; } catch (System.InvalidCastException) { return 1; }", WorkerClaimOutcome.Unknown);
    }

    [TestCase("System.Array.Clear((int[])null)")]
    [TestCase("System.Array.Clear((int[])null, -1, -1)")]
    public async Task ArrayClearNullArrayRetainsFaultEffectsAfterArgumentEvaluation(string invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations]", "", invocation + "; return 0;", noThrow: false, noAllocation: false, pure: null);
    }

    [TestCase("-1, 0")]
    [TestCase("0, -1")]
    [TestCase("4, 1")]
    [TestCase("1, int.MaxValue")]
    public async Task ArrayClearInvalidRangesCannotProveNoThrowOrNoAllocation(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations]", "int[] data",
            "Contract.Requires(data != null && data.Length == 4); System.Array.Clear(data, " + arguments + "); return 0;", noThrow: false, noAllocation: false, pure: null);
    }

    [TestCase("decimal")]
    [TestCase("double")]
    [TestCase("System.Guid")]
    [TestCase("int?")]
    [TestCase("object")]
    [TestCase("string")]
    public async Task ArrayClearValidValueDomainsRetainNoThrowAndNoAllocation(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations]", element + "[] data",
            "Contract.Requires(data != null); System.Array.Clear(data); return 0;", noThrow: true, noAllocation: true, pure: null);
    }

    [TestCase("int[][] data", "System.Array.Clear(data, 0, 1);")]
    [TestCase("System.Array data", "System.Array.Clear(data, 0, 1);")]
    [TestCase("int[,] data", "System.Array.Clear(data, 0, 1);")]
    public async Task ArrayClearDoesNotInventSzArrayShape(string parameters, string clear)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(clear);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations]", parameters,
            "Contract.Requires(data != null); " + clear + " return 0;", noThrow: false, noAllocation: false, pure: null);
    }
}
