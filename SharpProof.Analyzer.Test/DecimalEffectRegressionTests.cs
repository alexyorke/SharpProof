using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class DecimalEffectRegressionTests
{
    [Test]
    public async Task DecimalArithmeticAndConversionsCannotSatisfyDoesNotThrow()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static decimal Add(decimal left, decimal right) =>
                    left + right;
                [DoesNotThrow]
                public static decimal Subtract(decimal left, decimal right) =>
                    left - right;
                [DoesNotThrow]
                public static decimal Multiply(decimal left, decimal right) =>
                    left * right;
                [DoesNotThrow]
                public static decimal Divide(decimal left, decimal right) =>
                    left / right;
                [DoesNotThrow]
                public static decimal Remainder(decimal left, decimal right) =>
                    left % right;
                [DoesNotThrow]
                public static decimal CheckedRemainder(
                    decimal left,
                    decimal right) => checked(left % right);
                [DoesNotThrow]
                public static decimal Negate(decimal value) => -value;
                [DoesNotThrow]
                public static decimal Increment(decimal value) => ++value;
                [DoesNotThrow]
                public static decimal Decrement(decimal value) => --value;
                [DoesNotThrow]
                public static decimal? NullableAdd(
                    decimal? left,
                    decimal? right) => left + right;
                [DoesNotThrow]
                public static decimal? NullableDivide(
                    decimal? left,
                    decimal? right) => left / right;
                [DoesNotThrow]
                public static decimal? NullableRemainder(
                    decimal? left,
                    decimal? right) => left % right;
                [DoesNotThrow]
                public static decimal? NullableIncrement(decimal? value) => ++value;
                [DoesNotThrow]
                public static decimal AddAssign(decimal left, decimal right) {
                    left += right;
                    return left;
                }
                [DoesNotThrow]
                public static decimal DivideAssign(decimal left, decimal right) {
                    left /= right;
                    return left;
                }
                [DoesNotThrow]
                public static int ToInt(decimal value) => (int)value;
                [DoesNotThrow]
                public static long ToLong(decimal? value) => (long)value;
                [DoesNotThrow]
                public static decimal FromDouble(double value) => (decimal)value;
                [DoesNotThrow]
                public static decimal? FromNullableDouble(double? value) =>
                    (decimal?)value;

                [DoesNotThrow]
                public static decimal FromInt(int value) => (decimal)value;
                [DoesNotThrow]
                public static decimal CheckedFromInt(int value) =>
                    checked((decimal)value);
                [DoesNotThrow]
                public static double ToDouble(decimal value) => (double)value;
                [DoesNotThrow]
                public static float CheckedToFloat(decimal value) =>
                    checked((float)value);
            }
            """,
            "effects",
            ["SP0046"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0046", 19);
        var checkedRemainder = diagnostics.Single(static diagnostic =>
            diagnostic.GetMessage(CultureInfo.InvariantCulture)
                .Contains("'CheckedRemainder'", StringComparison.Ordinal));
        var checkedRemainderMessage = checkedRemainder.GetMessage(
            CultureInfo.InvariantCulture);
        Assert.That(checkedRemainderMessage, Does.Contain("DivideByZeroException"));
        Assert.That(checkedRemainderMessage, Does.Not.Contain("OverflowException"));
    }

    [Test]
    public async Task DecimalExceptionsKeepEffectsInMatchingCatchHandlers()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int s_state;

                [EnforcePure]
                public static void AddCatch(decimal left, decimal right) {
                    try { _ = left + right; }
                    catch (OverflowException) { s_state++; }
                }

                [EnforcePure]
                public static void ConversionCatch(decimal value) {
                    try { _ = (int)value; }
                    catch (OverflowException) { s_state++; }
                }

                [EnforcePure]
                public static void NullableUnwrapCatch(decimal? value) {
                    try { _ = (int)value; }
                    catch (InvalidOperationException) { s_state++; }
                }

                [EnforcePure]
                public static void SafeCheckedConversionCatch(int value) {
                    try { _ = checked((decimal)value); }
                    catch (OverflowException) { s_state++; }
                }
            }
            """,
            "effects",
            ["SP0002"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0002", 3);
    }
}
