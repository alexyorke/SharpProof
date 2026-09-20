namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class CheckedEnumNullableConversionRegressionTests
{
    [Test]
    public void CheckedEnumAndNullableConversionsReportOverflow()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public enum Small : byte {
                A = 1,
                B = 200
            }

            public static class Sample {
                public static Small FromInt(int value) =>
                    checked((Small)value);

                public static int ToSByte(Small value) =>
                    checked((sbyte)value);

                public static byte? FromNullable(int? value) =>
                    checked((byte?)value);

                public static byte UnwrapNullable(int? value) =>
                    checked((byte)value);

                public static Small? NullableEnum(int value) =>
                    checked((Small?)value);

                public static Small CheckedBlock(int value) {
                    checked {
                        return (Small)value;
                    }
                }

                public static Small Unchecked(int value) =>
                    unchecked((Small)value);

                public static long? Widening(int value) =>
                    checked((long?)value);
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertOverflow(session, compilation, "FromInt", "ToSByte",
            "FromNullable", "NullableEnum", "CheckedBlock");
        AssertThrows(
            session.Analyze(EffectTestHost.SampleMethod(compilation, "UnwrapNullable"))
                .Summary,
            "System.InvalidOperationException",
            "System.OverflowException");

        foreach (var methodName in new[] { "Unchecked", "Widening" })
        {
            Assert.That(
                session.Analyze(EffectTestHost.SampleMethod(compilation, methodName))
                    .Summary.Throws.IsEmpty,
                Is.True,
                methodName);
        }
    }

    [Test]
    public void CheckOverflowCompilationOptionAppliesToEnumsAndNullableConversions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public enum Small : byte {
                A = 1,
                B = 200
            }

            public static class Sample {
                public static Small FromInt(int value) => (Small)value;

                public static byte? FromNullable(int? value) => (byte?)value;

                public static Small Unchecked(int value) =>
                    unchecked((Small)value);
            }
            """);
        compilation = compilation.WithOptions(
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                checkOverflow: true,
                nullableContextOptions: NullableContextOptions.Enable));
        var session = new EffectAnalysisSession(compilation);

        AssertOverflow(session, compilation, "FromInt", "FromNullable");
        Assert.That(
            session.Analyze(EffectTestHost.SampleMethod(compilation, "Unchecked"))
                .Summary.Throws.IsEmpty,
            Is.True);
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

    private static void AssertThrows(
        EffectSummary summary,
        params string[] expected)
    {
        Assert.That(
            summary.Throws.Types.Select(static type => type.ToDisplayString()),
            Is.EquivalentTo(expected));
    }
}
