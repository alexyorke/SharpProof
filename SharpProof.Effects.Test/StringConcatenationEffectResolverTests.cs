namespace SharpProof.Effects.Test;

[TestFixture]
[NonParallelizable]
public sealed class StringConcatenationEffectResolverTests
{
    [Test]
    public void InterpolationIncludesTryFormatAndIFormattableEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public readonly struct Money : ISpanFormattable {
                private static int s_spanCalls;
                private static int s_formattableCalls;

                public string ToString(
                    string? format,
                    IFormatProvider? provider) {
                    s_formattableCalls++;
                    throw new ApplicationException();
                }

                public override string ToString() => "";

                public bool TryFormat(
                    Span<char> destination,
                    out int charsWritten,
                    ReadOnlySpan<char> format,
                    IFormatProvider? provider) {
                    s_spanCalls++;
                    charsWritten = 0;
                    throw new InvalidOperationException();
                }
            }

            public sealed class Price : ISpanFormattable {
                private static int s_spanCalls;
                private static int s_formattableCalls;

                public string ToString(
                    string? format,
                    IFormatProvider? provider) {
                    s_formattableCalls++;
                    throw new ApplicationException();
                }

                public override string ToString() => "";

                public bool TryFormat(
                    Span<char> destination,
                    out int charsWritten,
                    ReadOnlySpan<char> format,
                    IFormatProvider? provider) {
                    s_spanCalls++;
                    charsWritten = 0;
                    throw new InvalidOperationException();
                }
            }

            public static class Sample {
                public static string InterpolateStruct(Money value) =>
                    $"{value}";

                public static string InterpolateSealedClass(Price value) =>
                    $"{value}";
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] {
                     "InterpolateStruct",
                     "InterpolateSealedClass"
                 })
        {
            var summary = session.Analyze(
                EffectTestHost.SampleMethod(compilation, methodName)).Summary;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    summary.Writes.Contains(EffectRegionId.Static()),
                    Is.True,
                    methodName);
                Assert.That(
                    summary.Throws.Types.Select(static type =>
                        type.ToDisplayString()),
                    Does.Contain("System.InvalidOperationException"),
                    methodName);
                Assert.That(
                    summary.Throws.Types.Select(static type =>
                        type.ToDisplayString()),
                    Does.Contain("System.ApplicationException"),
                    methodName);
                Assert.That(
                    summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    methodName);
            }
        }
    }

    [Test]
    public void RuntimeInterpolationUsesTryFormatAndCanRetryAfterBufferGrowth()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public readonly struct Money : ISpanFormattable {
                public static int TryFormatCalls;
                public static int ToStringCalls;

                public string ToString(
                    string? format,
                    IFormatProvider? provider) {
                    ToStringCalls++;
                    throw new InvalidOperationException();
                }

                public override string ToString() {
                    ToStringCalls++;
                    throw new InvalidOperationException();
                }

                public bool TryFormat(
                    Span<char> destination,
                    out int charsWritten,
                    ReadOnlySpan<char> format,
                    IFormatProvider? provider) {
                    TryFormatCalls++;
                    if (TryFormatCalls == 1) {
                        charsWritten = 0;
                        return false;
                    }

                    const string text = "span";
                    if (destination.Length < text.Length) {
                        charsWritten = 0;
                        return false;
                    }

                    text.AsSpan().CopyTo(destination);
                    charsWritten = text.Length;
                    return true;
                }
            }

            public static class RuntimeFixture {
                public static string Interpolate(Money value) => $"{value}";
            }
            """);
        var image = EffectTestHost.EmitImage(compilation);

        RuntimeAssemblyTestHost.WithRuntimeAssembly(
            "SharpProof.Effects.Test.StringConcatenation",
            image.Image,
            assembly =>
            {
                var moneyType = assembly.GetType(
                    "Money",
                    throwOnError: true)!;
                var value = Activator.CreateInstance(moneyType)!;
                var method = assembly.GetType(
                        "RuntimeFixture",
                        throwOnError: true)!
                    .GetMethod("Interpolate")!;
                var result = method.Invoke(null, [value]);

                Assert.That(result, Is.EqualTo("span"));
                Assert.That(
                    moneyType.GetField("TryFormatCalls")!.GetValue(null),
                    Is.EqualTo(2));
                Assert.That(
                    moneyType.GetField("ToStringCalls")!.GetValue(null),
                    Is.EqualTo(0));
            });
    }
}
