namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class NumericWideningOverflowRegressionTests
{
    [Test]
    public void WidenedSmallIntegerAndLongArithmeticRetainOperandRanges()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int ByteProduct(byte left, byte right) =>
                    checked(left * right);

                public static int ShortIncrement(short value) =>
                    checked(value + 1);

                public static int CharIncrement(char value) =>
                    checked(value + 1);

                public static int UShortSum(ushort left, ushort right) =>
                    checked(left + right);

                public static int SByteNegate(sbyte value) =>
                    checked(-value);

                public static int ByteLocal(byte value) {
                    int widened = value;
                    return checked(widened + 1);
                }

                public static long LongProduct(int left, int right) =>
                    checked((long)left * right);

                public static long LongSum(int left, int right) =>
                    checked((long)left + right);

                public static int KnownLongNarrow(
                    [SharpProof.Attributes.InRange(-10, 10)] long value) =>
                    checked((int)value);

                public static int UnknownIntAdd(int value) =>
                    checked(value + 1);

                public static int UnknownLongNarrow(long value) =>
                    checked((int)value);
            }
            """);
        Assert.That(
            compilation.GetDiagnostics().Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "The regression source must compile.");

        var session = new EffectAnalysisSession(compilation);
        foreach (var methodName in new[]
                 {
                     "ByteProduct",
                     "ShortIncrement",
                     "CharIncrement",
                     "UShortSum",
                     "SByteNegate",
                     "ByteLocal",
                     "LongProduct",
                     "LongSum",
                     "KnownLongNarrow"
                 })
        {
            var summary = session.Analyze(
                EffectTestHost.SampleMethod(compilation, methodName)).Summary;
            Assert.That(
                summary.Throws.IsEmpty,
                Is.True,
                methodName + ": unknown=" + summary.Throws.IncludesUnknown + "; " +
                string.Join(", ", summary.Throws.Types.Select(
                    static type => type.ToDisplayString())));
        }

        AssertOverflow(session, compilation, "UnknownIntAdd", "UnknownLongNarrow");
    }

    private static void AssertOverflow(
        EffectAnalysisSession session,
        Compilation compilation,
        params string[] methodNames)
    {
        foreach (var methodName in methodNames)
        {
            Assert.That(
                session.Analyze(EffectTestHost.SampleMethod(compilation, methodName))
                    .Summary.Throws.Types.Select(static type => type.ToDisplayString()),
                Does.Contain("System.OverflowException"),
                methodName);
        }
    }
}
