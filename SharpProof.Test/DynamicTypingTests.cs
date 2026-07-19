using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class DynamicTypingTests
{
    [Test]
    public async Task DynamicParameter_PropertyRead_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public int {|SP0002:ProcessDynamic|}(dynamic value)
    {
        // Reading through dynamic dispatch is conservatively impure
        int result = value.Count;
        return result + 1;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DynamicParameter_PropertyModification_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public void {|SP0002:ModifyDynamic|}(dynamic value)
    {
        // Modifying dynamic value property is impure
        value.Count = 10;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DynamicParameter_MethodInvocation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public void {|SP0002:CallDynamicMethod|}(dynamic value)
    {
        // Calling methods on dynamic objects is impure
        value.Save();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DynamicMethodCall_ToKnownPureMemberName_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public int {|SP0002:GetDynamicToString|}(dynamic value)
    {
        return value.ToString().Length;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DynamicExtensionMethodCall_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public static class IntExtensions
{
    public static int Increment(this int value) => value + 1;
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:ApplyIncrement|}(dynamic value)
    {
        return value.Increment();
    }
}";

        var expectedIncrement = VerifyCS.Diagnostic("SP0004")
            .WithSpan(6, 23, 6, 32)
            .WithArguments("Increment");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedIncrement);
    }

    [Test]
    public async Task DynamicMethodCall_WithExplicitCastToConcreteType_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;



public class Counter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:GetDynamicViaCast|}(dynamic value)
    {
        return ((Counter)value).Increment(1);
    }
}";

        var expectedIncrement = VerifyCS.Diagnostic("SP0004")
            .WithSpan(8, 16, 8, 25)
            .WithArguments("Increment");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedIncrement);
    }

    [Test]
    public async Task DynamicExplicitConversion_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:ConvertDynamic|}(dynamic value)
    {
        return (int)value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DynamicMethodCall_WithExplicitAsCast_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;



public class Counter
{
    public int Increment(int value)
    {
        return value + 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:GetDynamicViaAsCast|}(dynamic value)
    {
        return (value as Counter)!.Increment(1);
    }
}";

        var expectedIncrement = VerifyCS.Diagnostic("SP0004")
            .WithSpan(8, 16, 8, 25)
            .WithArguments("Increment");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedIncrement);
    }

    [Test]
    public async Task DynamicCreation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public dynamic {|SP0002:CreateDynamic|}()
    {
        // Creating dynamic object is impure
        dynamic obj = new System.Dynamic.ExpandoObject();
        obj.Name = ""Test"";
        return obj;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DynamicLocalBinaryOperation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    private static readonly dynamic StaticDynamic = 10;

    [EnforcePure]
    public int {|SP0002:UseDynamicLocally|}(int input)
    {
        // Dynamic binary operations are conservatively impure
        var result = StaticDynamic + input;
        return result;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DynamicConditionalAccess_MethodInvocation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class TestClass
{
    [EnforcePure]
    public int {|SP0002:CallDynamicMethodViaNullConditional|}(dynamic value)
    {
        return value?.ToString()?.Length ?? 0;
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DynamicIndexerAccess_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:ReadDynamicIndexer|}(dynamic value)
    {
        return value[0];
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}