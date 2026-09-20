namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class DecimalEffectRegressionTests
{
    [Test]
    public void DecimalArithmeticReportsIntrinsicThrowsRegardlessOfContext()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static decimal Add(decimal left, decimal right) =>
                    left + right;
                public static decimal Subtract(decimal left, decimal right) =>
                    left - right;
                public static decimal Multiply(decimal left, decimal right) =>
                    left * right;
                public static decimal Divide(decimal left, decimal right) =>
                    left / right;
                public static decimal Remainder(decimal left, decimal right) =>
                    left % right;
                public static decimal CheckedRemainder(
                    decimal left,
                    decimal right) => checked(left % right);
                public static decimal Negate(decimal value) => -value;
                public static decimal Increment(decimal value) => ++value;
                public static decimal Decrement(decimal value) => --value;
                public static decimal AddAssign(decimal left, decimal right) {
                    left += right;
                    return left;
                }
                public static decimal DivideAssign(decimal left, decimal right) {
                    left /= right;
                    return left;
                }
                public static decimal RemainderAssign(decimal left, decimal right) {
                    left %= right;
                    return left;
                }
                public static decimal? NullableAdd(decimal? left, decimal? right) =>
                    left + right;
                public static decimal? NullableDivide(decimal? left, decimal? right) =>
                    left / right;
                public static decimal? NullableRemainder(
                    decimal? left,
                    decimal? right) => left % right;
                public static decimal? NullableIncrement(decimal? value) => ++value;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertThrows(session, compilation, "Add", "System.OverflowException");
        AssertThrows(session, compilation, "Subtract", "System.OverflowException");
        AssertThrows(session, compilation, "Multiply", "System.OverflowException");
        AssertThrows(
            session,
            compilation,
            "Divide",
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session,
            compilation,
            "Remainder",
            "System.DivideByZeroException");
        AssertThrows(
            session,
            compilation,
            "CheckedRemainder",
            "System.DivideByZeroException");
        AssertThrows(session, compilation, "Negate", "System.OverflowException");
        AssertThrows(session, compilation, "Increment", "System.OverflowException");
        AssertThrows(session, compilation, "Decrement", "System.OverflowException");
        AssertThrows(session, compilation, "AddAssign", "System.OverflowException");
        AssertThrows(
            session,
            compilation,
            "DivideAssign",
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session,
            compilation,
            "RemainderAssign",
            "System.DivideByZeroException");
        AssertThrows(session, compilation, "NullableAdd", "System.OverflowException");
        AssertThrows(
            session,
            compilation,
            "NullableDivide",
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session,
            compilation,
            "NullableRemainder",
            "System.DivideByZeroException");
        AssertThrows(
            session,
            compilation,
            "NullableIncrement",
            "System.OverflowException");
    }

    [Test]
    public void DecimalConversionsReportOnlyTheConversionsThatCanOverflow()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int ToInt(decimal value) => (int)value;
                public static long ToLong(decimal? value) => (long)value;
                public static decimal FromDouble(double value) => (decimal)value;
                public static decimal FromFloat(float value) => (decimal)value;
                public static decimal FromInt(int value) => (decimal)value;
                public static decimal CheckedFromInt(int value) =>
                    checked((decimal)value);
                public static double ToDouble(decimal value) => (double)value;
                public static double CheckedToDouble(decimal value) =>
                    checked((double)value);
                public static decimal? FromNullableDouble(double? value) =>
                    (decimal?)value;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertThrows(session, compilation, "ToInt", "System.OverflowException");
        AssertThrows(
            session,
            compilation,
            "ToLong",
            "System.InvalidOperationException",
            "System.OverflowException");
        AssertThrows(
            session,
            compilation,
            "FromDouble",
            "System.OverflowException");
        AssertThrows(
            session,
            compilation,
            "FromFloat",
            "System.OverflowException");

        foreach (var methodName in new[]
                 {
                     "FromInt",
                     "CheckedFromInt",
                     "ToDouble",
                     "CheckedToDouble"
                 })
        {
            var result = session.Analyze(
                EffectTestHost.SampleMethod(compilation, methodName));
            Assert.That(result.Summary.Throws.IsEmpty, Is.True, methodName);
        }

        AssertThrows(
            session,
            compilation,
            "FromNullableDouble",
            "System.OverflowException");
    }

    [Test]
    public void DecimalArithmeticAndConversionsKeepMatchingCatchesReachable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                private static int s_state;

                public static void AddCatch(decimal left, decimal right) {
                    try { _ = left + right; }
                    catch (OverflowException) { s_state++; }
                }

                public static void RemainderCatch(decimal left, decimal right) {
                    try { _ = left % right; }
                    catch (DivideByZeroException) { s_state++; }
                }

                public static void ConversionCatch(decimal value) {
                    try { _ = (int)value; }
                    catch (OverflowException) { s_state++; }
                }

                public static void NullableUnwrapCatch(decimal? value) {
                    try { _ = (int)value; }
                    catch (InvalidOperationException) { s_state++; }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[]
                 {
                     "AddCatch",
                     "RemainderCatch",
                     "ConversionCatch",
                     "NullableUnwrapCatch"
                 })
        {
            var result = session.Analyze(
                EffectTestHost.SampleMethod(compilation, methodName));
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                methodName);
        }
    }

    private static void AssertThrows(
        EffectAnalysisSession session,
        Compilation compilation,
        string methodName,
        params string[] expected)
    {
        var result = session.Analyze(
            EffectTestHost.SampleMethod(compilation, methodName));
        Assert.That(
            result.Summary.Throws.Types.Select(static type =>
                type.ToDisplayString()),
            Is.EquivalentTo(expected),
            methodName);
    }
}
