using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class ConstructorTests
{
    [Test]
    public async Task PureConstructor_MissingAttributeDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private readonly int _value;

    public TestClass(int value) // Unannotated pure constructor; expect SP0004.
    {
        _value = value;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(9, 12, 9, 21)
                .WithArguments(".ctor")
        );
    }

    [Test]
    public async Task ImpureConstructor_Unannotated_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private int _counter;

    public TestClass(int startValue)
    {
        _counter = startValue;
        Console.WriteLine($""Initialized with: {startValue}"");
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstructorPropertyAssignmentWithImpureSetter_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public {|SP0002:TestClass|}()
    {
        Value = 1;
    }

    public int Value
    {
        set { Console.WriteLine(value); }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task VirtualPropertySetterInPureConstructorDispatchesToImpureOverride_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class Base
{
    [EnforcePure]
    public {|SP0002:Base|}()
    {
        Value = 1;
    }

    public virtual int Value
    {
        set { }
    }
}

public sealed class Derived : Base
{
    private static int _writes;

    public override int Value
    {
        set { _writes++; }
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstructorWithMutableField_MissingAttributeDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private int _counter; // Mutable, but OK in constructor

    public TestClass(int startValue) // Unannotated constructor; expect SP0004 rather than SP0002.
    {
        _counter = startValue;
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(9, 12, 9, 21)
                .WithArguments(".ctor")
        );
    }

    [Test]
    public async Task ConstructorWithStaticFieldModification_Unannotated_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private static int _instanceCount = 0;

    public TestClass() // Unannotated constructor; no diagnostic expected.
    {
        _instanceCount++;
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstructorWithCollectionInitialization_Unannotated_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;

public class TestClass
{
    private readonly List<int> _items;

    public TestClass() // Unannotated constructor; no diagnostic expected.
    {
        _items = new List<int> { 1, 2, 3 };
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstructorCallingImpureMethod_Unannotated_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private readonly int _value;

    public TestClass(int value) // Unannotated constructor; no diagnostic expected.
    {
        _value = value;
        LogInitialization(value);
    }

    private void LogInitialization(int value) // Unannotated helper; no diagnostic expected.
    {
        Console.WriteLine($""Initialized with: {value}"");
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstructorCallingPureMethod_MissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    private readonly int _value;

    public TestClass(int value) // Unannotated constructor; expect SP0004 for .ctor.
    {
        _value = ProcessValue(value);
    }

    private int ProcessValue(int value) // Unannotated helper; expect SP0004 for ProcessValue.
    {
        return value * 2;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(9, 12, 9, 21)
                .WithArguments(".ctor"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(14, 17, 14, 29)
                .WithArguments("ProcessValue")
        );
    }

    [Test]
    public async Task PureMethodReturningGeneratedPureExceptionConstructors_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Exception TestMethod(bool useRange)
    {
        return useRange
            ? new ArgumentOutOfRangeException(""value"")
            : new BadImageFormatException(""bad image"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RecordConstructor_MissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public record Person // SP0004 expected for .ctor, get_Name, get_Age
{
    public Person(string name, int age)
    {
        Name = name;
        Age = age;
    }

    public string Name { get; }
    public int Age { get; }
}";
        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(7, 12, 7, 18)
                .WithArguments(".ctor"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(13, 19, 13, 23)
                .WithArguments("get_Name"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(14, 16, 14, 19)
                .WithArguments("get_Age")
        );
    }

    [Test]
    public async Task StructConstructor_MissingAttributeDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public struct Point // SP0004 expected
{
    public readonly int X;
    public readonly int Y;

    public Point(int x, int y)
    {
        X = x;
        Y = y;
    }
}";
        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(10, 12, 10, 17)
                .WithArguments(".ctor")
        );
    }

    [Test]
    public async Task ConstructorWithBaseCallToImpureConstructor_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class BaseClass
{
    protected BaseClass(int value) // Unannotated constructor; no diagnostic expected.
    {
        Console.WriteLine($""Base initialized with: {value}"");
    }
}

public class DerivedClass : BaseClass // The derived constructor is also unannotated, so this case stays diagnostic-free.
{
    public DerivedClass(int value) : base(value) { }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConstructorWithBaseCallToPureConstructor_MissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class BaseClass
{
    private readonly int _value;

    protected BaseClass(int value) // Unannotated base constructor; expect SP0004.
    {
        _value = value;
    }
}

public class DerivedClass : BaseClass // The derived constructor is also unannotated, so this case expects SP0004.
{
    public DerivedClass(int value) : base(value) { }
}";
        await VerifyCS.VerifyAnalyzerAsync(
            test,
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(9, 15, 9, 24)
                .WithArguments(".ctor"),
            VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
                .WithSpan(17, 12, 17, 24)
                .WithArguments(".ctor")
        );
    }
}