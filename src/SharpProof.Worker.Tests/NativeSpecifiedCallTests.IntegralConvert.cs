using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    public async Task IntegralToInt64PreservesValueAndEffects(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        await CheckInterfaceArrayContract(type + " value", "Contract.Ensures(Contract.Result<long>() == (long)value); return System.Convert.ToInt64(value);", WorkerClaimOutcome.Proven, "long");
        await CheckInterfaceArrayContract(type + " value", "Contract.Ensures(Contract.Result<long>() != (long)value); return System.Convert.ToInt64(value);", WorkerClaimOutcome.Refuted, "long");
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", type + " value", "return System.Convert.ToInt64(value);", false, false, false, "long");
    }

    [Test]
    public async Task IntegralToInt64ClausesRemainUnknownWithoutInitializationEvidence()
    {
        await CheckInterfaceArrayContract("int value", "Contract.Ensures(Contract.Result<long>() == System.Convert.ToInt64(value)); return value;", WorkerClaimOutcome.Unknown, "long");
        await CheckInterfaceArrayContract("ulong value", "Contract.Requires(value <= long.MaxValue); Contract.Ensures(Contract.Result<long>() == System.Convert.ToInt64(value)); return (long)value;", WorkerClaimOutcome.Unknown, "long");
    }

    [Test]
    public async Task IntegralToInt64BooleanUsesZeroAndOne()
    {
        await CheckInterfaceArrayContract("bool value", "Contract.Ensures(Contract.Result<long>() == (value ? 1L : 0L)); return System.Convert.ToInt64(value);", WorkerClaimOutcome.Proven, "long");
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "bool value", "return System.Convert.ToInt64(value);", false, false, false, "long");
    }

    [Test]
    public async Task IntegralToInt64UInt64KeepsCheckedOverflowEvenInUncheckedContext()
    {
        await CheckInterfaceArrayContract("ulong value", "Contract.Requires(value <= long.MaxValue); Contract.Ensures(Contract.Result<long>() == (long)value); return unchecked(System.Convert.ToInt64(value));", WorkerClaimOutcome.Proven, "long");
        await CheckInterfaceArrayContract("ulong value", "Contract.Ensures(Contract.Result<int>() == 1); try { System.Convert.ToInt64(value); return value <= long.MaxValue ? 1 : 0; } catch (System.OverflowException) { return value > long.MaxValue ? 1 : 0; }", WorkerClaimOutcome.Unknown);
        // Both bounded and unbounded domains retain framework-initialization effects.
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "ulong value", "return System.Convert.ToInt64(value);", false, false, false, "long");
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "ulong value", "Contract.Requires(value <= long.MaxValue); return System.Convert.ToInt64(value);", false, false, false, "long");
    }

    [Test]
    public async Task IntegralToInt64NormalUInt64RangeAndBooleanRefutationsRemainConcrete()
    {
        await CheckInterfaceArrayContract("ulong value", "Contract.Ensures(Contract.Result<long>() >= 0); return System.Convert.ToInt64(value);", WorkerClaimOutcome.Proven, "long");
        await CheckInterfaceArrayContract("ulong value", "Contract.Ensures(Contract.Result<long>() < 0); return System.Convert.ToInt64(value);", WorkerClaimOutcome.Refuted, "long");
        await CheckInterfaceArrayContract("bool value", "Contract.Ensures(Contract.Result<long>() == 2); return System.Convert.ToInt64(value);", WorkerClaimOutcome.Refuted, "long");
    }

    [Test]
    public async Task IntegralToInt64KeepsUnknownArgumentEffects()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "System.Func<int> read", "return System.Convert.ToInt64(read());", false, false, false, "long");
    }

    [Test]
    public async Task IntegralToInt64InitializationPreservesUnrelatedCallerArrayState()
    {
        await CheckInterfaceArrayContract("int[] values, int value", "Contract.Requires(values != null && values.Length > 0); Contract.Ensures(Contract.Result<int>() == 17); values[0] = 17; System.Convert.ToInt64(value); return values[0];", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task IntegralToInt64EvaluatesArgumentOnce()
    {
        await CheckInterfaceArrayContract("int value", "Contract.Ensures(Contract.Result<long>() == 18); var n = 16; var converted = System.Convert.ToInt64(++n); return converted + n - 16;", WorkerClaimOutcome.Proven, "long");
    }
}
