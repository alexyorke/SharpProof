using System.Reflection;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryAllocationSourceTests
{
    [TestCase(nameof(SwitchValue), false)]
    [TestCase(nameof(LoopValue), false)]
    [TestCase(nameof(FilteredDivision), false)]
    [TestCase(nameof(CompareValues), false)]
    [TestCase(nameof(WideConstant), false)]
    [TestCase(nameof(SwitchStatement), false)]
    [TestCase(nameof(CreateArray), true)]
    public void CompilerEmittedOrdinaryMethodsHaveExpectedAllocationClassification(string name, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        var method = typeof(OrdinaryAllocationSourceTests).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
        Assert.That(AllocationIlOracle.HasPotentialAllocation(method), Is.EqualTo(expected));
    }

    [Test]
    public void InterfaceDeclarationConservativelyHasPotentialAllocationWithoutAnImplementation()
    {
        var method = typeof(IComparable).GetMethod(nameof(IComparable.CompareTo))!;
        Assert.That(method.GetMethodBody(), Is.Null);
        Assert.That(AllocationIlOracle.HasPotentialAllocation(method), Is.True);
    }

    public static int SwitchValue(int value)
    {
        return value switch
        {
            0 => 13,
            1 => 29,
            2 => 41,
            3 => 53,
            4 => 67,
            5 => 79,
            _ => 97
        };
    }

    public static int LoopValue(int value)
    {
        var total = 0;
        for (var index = 0; index < value; index++)
        {
            total += index;
        }
        return total;
    }

    public static int FilteredDivision(int value /* SPINT001: all values valid - the zero divisor has an explicit exception-filter fallback */)
    {
        try
        {
            return 100 / value;
        }
        catch (DivideByZeroException) when (value == 0)
        {
            return -1;
        }
    }

    public static bool CompareValues(long first, long second)
    {
        return first == second;
    }

    public static long WideConstant(long value)
    {
        return value + 123456789012345L;
    }

    public static int SwitchStatement(int value)
    {
        switch (value)
        {
            case 0:
                return 13;
            case 1:
                return 29;
            case 2:
                return 41;
            case 3:
                return 53;
            case 4:
                return 67;
            case 5:
                return 79;
            case 6:
                return 83;
            case 7:
                return 89;
            default:
                return 97;
        }
    }

    public static int[] CreateArray(int length /* SPINT001: all values valid - the IL allocation oracle includes faulting array lengths */)
    {
        return new int[length];
    }
}
