using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class BasicPureTests
{
    [Test]
    public async Task NameOf_ShouldBePure()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string GetName()
    {
        // nameof is resolved at compile time to a string literal, which is pure.
        string name = nameof(System.Console.WriteLine);
        return name;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AnonymousObjectCreation_WithPureInitializers_ShouldBePure()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Project(int x)
    {
        var item = new { Value = x, Next = x + 1 };
        return item.Value + item.Next;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task AnonymousObjectCreation_WithImpureInitializer_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Project()
    {
        var item = new { Now = DateTime.Now };
        return item.Now.Day;
    }
}";

        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(8, 16, 8, 23)
            .WithArguments("Project");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task CoalesceAssignment_WithPureFallback_ShouldBePure()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Normalize(string input)
    {
        string value = input;
        value ??= ""fallback"";
        return value.Length;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task CoalesceAssignment_WithImpureFallback_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string Normalize(string input)
    {
        string value = input;
        value ??= DateTime.Now.ToString();
        return value;
    }
}";

        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(8, 19, 8, 28)
            .WithArguments("Normalize");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task Method_WithBothEnforcePureAndPure_ReportsSP0005()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class C
{
    [EnforcePure]
    [Pure]
    public int Add(int a, int b) => a + b;
}";

        var expected = VerifyCS.Diagnostic("SP0005")
            .WithSpan(9, 16, 9, 19)
            .WithArguments("Add");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task Misplaced_AllowSynchronization_OnClass_ReportsSP0007()
    {
        var test = @"
using System;
namespace SharpProof.Attributes { [AttributeUsage(AttributeTargets.All)] public sealed class AllowSynchronizationAttribute : Attribute {} }

[SharpProof.Attributes.AllowSynchronization]
public class C { }
";

        var expected = VerifyCS.Diagnostic("SP0007")
            .WithSpan(5, 2, 5, 44);
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task Misplaced_Pure_OnParameter_ReportsSP0003()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class C
{
    [Impure]
    public int M([{|SP0003:Pure|}] int value) => value;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ReadonlyRecordStruct_WithPureOnType_ReportsExpectedDiagnostics()
    {
        var test = @"
using SharpProof.Attributes;

[Pure]
public readonly record struct Zzz
{
    // Constructor body only assigns readonly record struct properties.
    public Zzz(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }
    public int Y { get; }
}

public class TestUsage
{
    [EnforcePure]
    public Zzz CreateZzz()
    {
        // Object creation remains pure; diagnostics are on declarations above.
        return new Zzz(1, 2);
    }
}
";


        var expectedCtor = VerifyCS.Diagnostic("SP0004")
            .WithSpan(8, 12, 8, 15).WithArguments(".ctor");
        var expectedGetX = VerifyCS.Diagnostic("SP0004")
            .WithSpan(14, 16, 14, 17).WithArguments("get_X");
        var expectedGetY = VerifyCS.Diagnostic("SP0004")
            .WithSpan(15, 16, 15, 17).WithArguments("get_Y");
        var expectedSP0003 = VerifyCS.Diagnostic("SP0003").WithSpan(4, 2, 4, 6);

        await VerifyCS.VerifyAnalyzerAsync(test, expectedCtor, expectedGetX, expectedGetY, expectedSP0003);
    }

    [Test]
    public async Task ConstructorInitializer_CallingPureThis_ReportsOnlyAccessorMissingAttributes()
    {
        var test = @"
using SharpProof.Attributes;

public struct MyStruct
{
    public int X { get; }
    public int Y { get; }

    [Pure]
    public MyStruct(int x)
    {
        X = x;
        Y = 0; // Default value
    }

    [Pure] // This constructor is pure
    public MyStruct(int x, int y) : this(x) // Calls another [Pure] constructor
    {
        Y = y; // Remaining assignment is allowed in constructor
    }
}

public class TestUsage
{
    [EnforcePure]
    public MyStruct CreateMyStruct()
    {
        // Constructor call is pure; only accessor declaration diagnostics are expected.
        return new MyStruct(1, 2);
    }
}
";


        var expectedGetX = VerifyCS.Diagnostic("SP0004")
            .WithSpan(6, 16, 6, 17).WithArguments("get_X");
        var expectedGetY = VerifyCS.Diagnostic("SP0004")
            .WithSpan(7, 16, 7, 17).WithArguments("get_Y");


        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetX, expectedGetY);
    }

    [Test]
    public async Task ConstructorInitializer_CallingUnannotatedPureThis_ReportsMissingAttributeDiagnostics()
    {
        var test = @"
using SharpProof.Attributes;

public struct MyStruct
{
    public int X { get; }
    public int Y { get; }

    // This constructor is unannotated and expected to get SP0004.
    public MyStruct(int x)
    {
        X = x;
        Y = 0; // Default value
    }

    // Although this constructor calls a constructor not marked [Pure],
    // that constructor is analyzable and found to be pure, so this one is also pure.
    [EnforcePure]
    public MyStruct(int x, int y) : this(x)
    {
        Y = y;
    }
}
";


        var expectedGetX = VerifyCS.Diagnostic("SP0004")
            .WithSpan(6, 16, 6, 17).WithArguments("get_X");
        var expectedGetY = VerifyCS.Diagnostic("SP0004")
            .WithSpan(7, 16, 7, 17).WithArguments("get_Y");
        var expectedCtor = VerifyCS.Diagnostic("SP0004")
            .WithSpan(10, 12, 10, 20).WithArguments(".ctor");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetX, expectedGetY, expectedCtor);
    }

    [Test]
    public async Task PositionalReadonlyRecordStruct_NoBodyOrInterfaces_ShouldBePure()
    {
        var test = @"
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit {}
}

namespace TestNamespace // Wrap everything in a namespace
{
    using SharpProof.Attributes;

    // Simple positional readonly record struct with no body or interfaces.
    // Creating an instance should be pure.
    public readonly record struct A(int X, int Y);

    public class TestUsage
    {
        [EnforcePure]
        public A CreateA()
        {
            // This object creation should be pure.
            return new A(1, 2);
        }
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PropertyAccessors_ReportMissingAttributeDiagnostics()
    {
        var test = @"
using SharpProof.Attributes;

public struct MyStruct
{
    public int X { get; }
    public int Y { get; }

    [Pure]
    public MyStruct(int x)
    {
        X = x;
        Y = 0; // Default value
    }

    [Pure] // This constructor is pure
    public MyStruct(int x, int y) : this(x) // Calls another [Pure] constructor
    {
        Y = y; // Remaining assignment is allowed in constructor
    }

    public int GetX()
    {
        return X;
    }

    public int GetY()
    {
        return Y;
    }
}

public class TestUsage
{
    [Pure]
    public MyStruct CreateMyStruct()
    {
        // Call to MyStruct(int, int) should be pure
        return new MyStruct(1, 2);
    }
}
";

        var expectedGetX = VerifyCS.Diagnostic("SP0004")
            .WithSpan(6, 16, 6, 17).WithArguments("get_X");
        var expectedGetY = VerifyCS.Diagnostic("SP0004")
            .WithSpan(7, 16, 7, 17).WithArguments("get_Y");


        var expectedGetX2 = VerifyCS.Diagnostic("SP0004")
            .WithSpan(22, 16, 22, 20).WithArguments("GetX");
        var expectedGetY2 = VerifyCS.Diagnostic("SP0004")
            .WithSpan(27, 16, 27, 20).WithArguments("GetY");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetX, expectedGetY, expectedGetX2, expectedGetY2);
    }
}