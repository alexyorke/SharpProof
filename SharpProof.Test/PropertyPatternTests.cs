using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class PropertyPatternTests
{
    [Test]
    public async Task PurePropertyPattern_NoDiagnostic()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class Point
{
    [EnforcePure]
    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }

    public int Y { get; }
}

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Point point)
    {
        return point is { X: 0, Y: 0 };
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PropertyPatternWithImpureGetter_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class Probe
{
    public int Value
    {
        get
        {
            Console.WriteLine(""reading"");
            return 1;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Probe probe)
    {
        return probe is { Value: 1 };
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PositionalPatternWithImpureDeconstruct_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class Probe
{
    public void Deconstruct(out int value)
    {
        Console.WriteLine(""deconstruct"");
        value = 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Probe probe)
    {
        return probe is Probe(var value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PropertyPatternWithRelationalPattern_NoDiagnostic()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class Probe
{
    public int Value { get; }
}

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Probe probe)
    {
        return probe is { Value: > 0 };
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task NegatedNullPattern_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(object value)
    {
        return value is not null;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task BareTypePattern_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(object value)
    {
        return value is string;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}