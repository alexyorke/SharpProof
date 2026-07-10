using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class GenericAttributesTests
{
    [Test]
    public async Task GenericAttribute_PureMethod_UnknownPurityDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



// Generic attribute definition
[AttributeUsage(AttributeTargets.All)]
public class TypeAttribute<T> : Attribute
{
    public T Value { get; }
    
    public TypeAttribute(T value)
    {
        Value = value;
    }
}

namespace TestNamespace
{
    public class GenericAttributeTest
    {
        // Pure method with generic attributes
        [EnforcePure]
        [Type<int>(42)]
        public string GetAttributeValue<T>(T value)
        {
            // Pure operation, just returning a string representation
            return value?.ToString() ?? ""null"";
        }
    }
}";


        var expectedSP0004_Getter = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004)
            .WithSpan(11, 14, 11, 19).WithArguments("get_Value");
        var expectedSP0004_Ctor = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004)
            .WithSpan(13, 12, 13, 25).WithArguments(".ctor");
        var expectedSP0002 = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002)
            .WithSpan(26, 23, 26, 40)
            .WithArguments("GetAttributeValue");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedSP0004_Getter, expectedSP0004_Ctor, expectedSP0002);
    }

    [Test]
    public async Task GenericAttributeWithTypeConstraint_PureMethod_ReportsMissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;



// Generic attribute with type constraint
[AttributeUsage(AttributeTargets.All)]
public class ValueAttribute<T> : Attribute where T : struct
{
    public T DefaultValue { get; }

    public ValueAttribute(T defaultValue)
    {
        DefaultValue = defaultValue;
    }
}

namespace TestNamespace
{
    public class GenericAttributeConstraintTest
    {
        // Pure method with generic attribute that has constraints
        [EnforcePure]
        [Value<int>(0)]
        public T GetDefaultValue<T>() where T : struct
        {
            // Using default for struct type - pure operation
            return default(T);
        }
    }
}";


        var expectedGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(11, 14, 11, 26)
            .WithArguments("get_DefaultValue");
        var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(13, 12, 13, 26)
            .WithArguments(".ctor");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetter, expectedCtor);
    }

    [Test]
    public async Task GenericAttributeWithReferenceConstraint_PureMethod_ReportsMissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class MyAttribute<T> : Attribute where T : class
{
    public T Data { get; }
    public MyAttribute(T data) { Data = data; }
}

public class TestClass
{
    [EnforcePure]
    [My<string>(""test"")] // Attribute application
    public void TestMethod()
    {
        // Method body is empty, trivially pure
    }
}
";


        var expectedGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(7, 14, 7, 18)
            .WithArguments("get_Data");
        var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(8, 12, 8, 23)
            .WithArguments(".ctor");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetter, expectedCtor);
    }

    [Test]
    public async Task
        GenericAttributeWithMultipleTypeParameters_GenericInterpolation_ReportsPurityAndMissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;



// Generic attribute with multiple type parameters
[AttributeUsage(AttributeTargets.All)]
public class PairAttribute<TKey, TValue> : Attribute
{
    public TKey Key { get; }
    public TValue Value { get; }
    
    public PairAttribute(TKey key, TValue value)
    {
        Key = key;
        Value = value;
    }
}

namespace TestNamespace
{
    public class GenericAttributeMultipleParamsTest
    {
        // Generic interpolation is conservative because TKey/TValue.ToString() can dispatch to user code.
        [EnforcePure]
        [Pair<int, string>(1, ""one"")]
        public string {|SP0002:FormatPair|}<TKey, TValue>(TKey key, TValue value)
        {
            return $""{key}: {value}"";
        }
    }
}";


        var expectedKeyGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(11, 17, 11, 20)
            .WithArguments("get_Key");
        var expectedValueGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(12, 19, 12, 24)
            .WithArguments("get_Value");
        var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(14, 12, 14, 25)
            .WithArguments(".ctor");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedKeyGetter, expectedValueGetter, expectedCtor);
    }

    [Test]
    public async Task GenericAttribute_ImpureMethod_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.IO;



// Generic attribute definition
[AttributeUsage(AttributeTargets.All)]
public class LogAttribute<T> : Attribute
{
    public T Value { get; }

    public LogAttribute(T value)
    {
        Value = value;
    }
}

namespace TestNamespace
{
    public class GenericAttributeImpureTest
    {
        // Impure method with generic attributes
        [EnforcePure]
        [Log<string>(""debug"")]
        public void LogValue<T>(T value)
        {
            // Writing to a file - impure operation
            File.AppendAllText(""log.txt"", value?.ToString() ?? ""null"");
        }
    }
}";


        var expectedSP0004_Getter = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004)
            .WithSpan(12, 14, 12, 19).WithArguments("get_Value");
        var expectedSP0004_Ctor = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004)
            .WithSpan(14, 12, 14, 24).WithArguments(".ctor");
        var expectedSP0002 = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(27, 21, 27, 29)
            .WithArguments("LogValue");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedSP0004_Getter, expectedSP0004_Ctor, expectedSP0002);
    }

    [Test]
    public async Task GenericAttributeWithGenericMethodParameter_PureMethod_ReportsMissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



// Generic attribute definition
[AttributeUsage(AttributeTargets.Parameter)]
public class ValidateAttribute<T> : Attribute
{
    public T MinValue { get; }
    public T MaxValue { get; }

    public ValidateAttribute(T minValue, T maxValue)
    {
        MinValue = minValue;
        MaxValue = maxValue;
    }
}

namespace TestNamespace
{
    public class GenericAttributeParameterTest
    {
        // Pure method with generic attributes on parameters
        [EnforcePure]
        public bool IsValid<T>(
            [Validate<int>(0, 100)] int value1,
            [Validate<double>(0.0, 1.0)] double value2) where T : IComparable<T>
        {
            // Pure operation - just comparing values
            return value1 >= 0 && value1 <= 100 && 
                   value2 >= 0.0 && value2 <= 1.0;
        }
    }
}";


        var expectedMinGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(12, 14, 12, 22).WithArguments("get_MinValue");
        var expectedMaxGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(13, 14, 13, 22).WithArguments("get_MaxValue");
        var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(15, 12, 15, 29).WithArguments(".ctor");
        await VerifyCS.VerifyAnalyzerAsync(test, expectedMinGetter, expectedMaxGetter, expectedCtor);
    }
}