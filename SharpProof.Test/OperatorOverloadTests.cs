using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class OperatorOverloadTests
{
    [Test]
    public async Task PureOperatorOverload_MissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public struct Vector2
{
    public float X { get; }
    public float Y { get; }

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static Vector2 operator +(Vector2 a, Vector2 b)
    {
        return new Vector2(a.X + b.X, a.Y + b.Y);
    }

    public static Vector2 operator -(Vector2 a, Vector2 b)
    {
        return new Vector2(a.X - b.X, a.Y - b.Y);
    }

    public static Vector2 operator *(Vector2 a, float scalar)
    {
        return new Vector2(a.X * scalar, a.Y * scalar);
    }
}";

        var expectedX = VerifyCS.Diagnostic("SP0004").WithSpan(7, 18, 7, 19)
            .WithArguments("get_X");
        var expectedY = VerifyCS.Diagnostic("SP0004").WithSpan(8, 18, 8, 19)
            .WithArguments("get_Y");
        var expectedCtor = VerifyCS.Diagnostic("SP0004")
            .WithSpan(10, 12, 10, 19).WithArguments(".ctor");
        var expectedAdd = VerifyCS.Diagnostic("SP0004")
            .WithSpan(16, 36, 16, 37).WithArguments("op_Addition");
        var expectedSub = VerifyCS.Diagnostic("SP0004")
            .WithSpan(21, 36, 21, 37).WithArguments("op_Subtraction");
        var expectedMul = VerifyCS.Diagnostic("SP0004")
            .WithSpan(26, 36, 26, 37).WithArguments("op_Multiply");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedX, expectedY, expectedCtor, expectedAdd, expectedSub,
            expectedMul);
    }

    [Test]
    public async Task ImpureOperatorOverload_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class Counter
{
    private static int _totalOperations = 0;

    public int Value { get; }

    public Counter(int value)
    {
        Value = value;
    }

    // This operator is impure because it modifies static state
    [EnforcePure]
    public static Counter operator +(Counter a, Counter b)
    {
        _totalOperations++; 
        return new Counter(a.Value + b.Value);
    }
}";
        var expectedVal = VerifyCS.Diagnostic("SP0004")
            .WithSpan(9, 16, 9, 21).WithArguments("get_Value");
        var expectedCtor = VerifyCS.Diagnostic("SP0004")
            .WithSpan(11, 12, 11, 19).WithArguments(".ctor");
        var expectedOp = VerifyCS.Diagnostic("SP0002").WithSpan(18, 36, 18, 37)
            .WithArguments("op_Addition");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedVal, expectedCtor, expectedOp);
    }

    [Test]
    public async Task ImpureOperatorOverload_CompoundAssignment_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public struct Counter
{
    private static int Hits;

    [Impure]
    public static Counter operator +(Counter left, Counter right)
    {
        Hits++;
        return left;
    }
}

public static class Demo
{
    [EnforcePure]
    public static Counter {|SP0002:TestMethod|}()
    {
        var value = default(Counter);
        value += default(Counter);
        return value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }


    [Test]
    public async Task ComparisonOperatorOverload_MissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public struct Temperature
{
    public double Celsius { get; }

    public Temperature(double celsius)
    {
        Celsius = celsius;
    }

    public static bool operator <(Temperature a, Temperature b)
    {
        return a.Celsius < b.Celsius;
    }

    public static bool operator >(Temperature a, Temperature b)
    {
        return a.Celsius > b.Celsius;
    }

    public static bool operator ==(Temperature a, Temperature b)
    {
        return a.Celsius == b.Celsius;
    }

    public static bool operator !=(Temperature a, Temperature b)
    {
        return a.Celsius != b.Celsius;
    }
}";

        var expectedGet = VerifyCS.Diagnostic("SP0004")
            .WithSpan(7, 19, 7, 26).WithArguments("get_Celsius");
        var expectedCtor = VerifyCS.Diagnostic("SP0004")
            .WithSpan(9, 12, 9, 23).WithArguments(".ctor");
        var expectedLess = VerifyCS.Diagnostic("SP0004")
            .WithSpan(14, 33, 14, 34).WithArguments("op_LessThan");
        var expectedGreater = VerifyCS.Diagnostic("SP0004")
            .WithSpan(19, 33, 19, 34).WithArguments("op_GreaterThan");
        var expectedEqual = VerifyCS.Diagnostic("SP0004")
            .WithSpan(24, 33, 24, 35).WithArguments("op_Equality");
        var expectedNotEqual = VerifyCS.Diagnostic("SP0004")
            .WithSpan(29, 33, 29, 35).WithArguments("op_Inequality");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedGet, expectedCtor, expectedLess, expectedGreater,
            expectedEqual, expectedNotEqual);
    }


    [Test]
    public async Task ConversionOperatorOverload_MissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public struct Meter
{
    public double Value { get; }
    public Meter(double value) { Value = value; }

    public static explicit operator Foot(Meter meter)
    {
        return new Foot(meter.Value * 3.28084);
    }
}

public struct Foot
{
    public double Value { get; }
    public Foot(double value) { Value = value; }

    public static explicit operator Meter(Foot foot)
    {
        return new Meter(foot.Value / 3.28084);
    }
}";

        var expectedMeterVal = VerifyCS.Diagnostic("SP0004")
            .WithSpan(7, 19, 7, 24).WithArguments("get_Value");
        var expectedMeterCtor = VerifyCS.Diagnostic("SP0004")
            .WithSpan(8, 12, 8, 17).WithArguments(".ctor");
        var expectedFootVal = VerifyCS.Diagnostic("SP0004")
            .WithSpan(18, 19, 18, 24).WithArguments("get_Value");
        var expectedFootCtor = VerifyCS.Diagnostic("SP0004")
            .WithSpan(19, 12, 19, 16).WithArguments(".ctor");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedMeterVal, expectedMeterCtor, expectedFootVal,
            expectedFootCtor);
    }
}