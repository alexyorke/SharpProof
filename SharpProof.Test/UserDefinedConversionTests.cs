using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class UserDefinedConversionTests
{
    [Test]
    public async Task ImplicitConversion_PureImplementation_MissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public struct Celsius
{
    public double Value { get; }

    public Celsius(double value)
    {
        Value = value;
    }

    public static implicit operator double(Celsius celsius)
    {
        return celsius.Value;
    }

    public static implicit operator Celsius(double value)
    {
        return new Celsius(value);
    }
}";


        var expectedGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(9, 19, 9, 24)
            .WithArguments("get_Value");
        var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(11, 12, 11, 19)
            .WithArguments(".ctor");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetter, expectedCtor);
    }


    [Test]
    public async Task ExplicitConversion_PureImplementation_MissingAttributeDiagnostics()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static explicit operator decimal(Money money)
    {
        return money.Amount;
    }

    public static explicit operator Money(decimal amount)
    {
        return new Money(amount, ""USD"");
    }
}";


        var expectedGetterAmount = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(9, 20, 9, 26)
            .WithArguments("get_Amount");
        var expectedGetterCurrency = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(10, 19, 10, 27)
            .WithArguments("get_Currency");
        var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(12, 12, 12, 17)
            .WithArguments(".ctor");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetterAmount, expectedGetterCurrency, expectedCtor);
    }

    [Test]
    public async Task ImpureConversion_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class Counter
{
    private static int _conversionCount = 0;
    
    public int Value { get; }

    public Counter(int value)
    {
        Value = value;
    }

    public static explicit operator int(Counter counter)
    {
        _conversionCount++; // Impure operation - modifies static field
        return counter.Value;
    }
}";


        var expectedGetter = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");
        var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(13, 12, 13, 19)
            .WithArguments(".ctor");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetter, expectedCtor);
    }

    [Test]
    public async Task ComplexConversion_ImpureParsing_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class DateOnly
{
    public int Year { get; }
    public int Month { get; }
    public int Day { get; }

    public DateOnly(int year, int month, int day)
    {
        Year = year;
        Month = month;
        Day = day;
    }

    public static explicit operator string(DateOnly date)
    {
        return $""{date.Year}-{date.Month}-{date.Day}"";
    }

    public static explicit operator DateOnly(string dateString)
    {
        // Simple parsing without exception handling for test simplicity
        var parts = dateString.Split('-');
        return new DateOnly(
            int.Parse(parts[0]),
            int.Parse(parts[1]),
            int.Parse(parts[2]));
    }
}";


        var expectedGetterYear = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(9, 16, 9, 20)
            .WithArguments("get_Year");
        var expectedGetterMonth = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(10, 16, 10, 21)
            .WithArguments("get_Month");
        var expectedGetterDay = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(11, 16, 11, 19)
            .WithArguments("get_Day");
        var expectedCtor = VerifyCS.Diagnostic(SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .WithSpan(13, 12, 13, 20)
            .WithArguments(".ctor");


        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetterYear, expectedGetterMonth, expectedGetterDay,
            expectedCtor);
    }
}