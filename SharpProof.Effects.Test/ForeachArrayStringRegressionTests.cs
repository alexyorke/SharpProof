namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ForeachArrayStringRegressionTests
{
    [Test]
    public void ArrayAndStringForeachUseDirectIterationEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System.Collections.Generic;

            public static class Sample
            {
                public static int Array(int[] values)
                {
                    if (values is null)
                    {
                        return 0;
                    }

                    var total = 0;
                    foreach (var value in values)
                    {
                        total += value;
                    }

                    return total;
                }

                public static int String(string values)
                {
                    if (values is null)
                    {
                        return 0;
                    }

                    var total = 0;
                    foreach (var value in values)
                    {
                        total += value;
                    }

                    return total;
                }

                public static int ArrayFor(int[] values)
                {
                    var total = 0;
                    for (var index = 0; index < values.Length; index++)
                    {
                        total += values[index];
                    }

                    return total;
                }

                public static int StringFor(string values)
                {
                    var total = 0;
                    for (var index = 0; index < values.Length; index++)
                    {
                        total += values[index];
                    }

                    return total;
                }

                public static int StringKnown(string? values)
                {
                    var total = 0;
                    foreach (var value in values ?? "")
                    {
                        total += value;
                    }

                    return total;
                }

                public static int Interface(IEnumerable<int> values)
                {
                    var total = 0;
                    foreach (var value in values)
                    {
                        total += value;
                    }

                    return total;
                }

                public static int ArrayObject(int[] values)
                {
                    var total = 0;
                    foreach (object value in values)
                    {
                        total += value is int ? 1 : 0;
                    }

                    return total;
                }
            }
            """);

        var session = new EffectAnalysisSession(compilation);
        var array = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "Array")).Summary;
        var @string = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "String")).Summary;
        var @interface = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "Interface")).Summary;
        var arrayObject = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "ArrayObject")).Summary;
        var arrayFor = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "ArrayFor")).Summary;
        var stringKnown = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "StringKnown")).Summary;

        AssertDirectIteration(array, arrayFor);
        AssertDirectIteration(@string);
        AssertDirectIteration(stringKnown, requireNullCheck: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                @interface.Throws.IncludesUnknown,
                Is.True,
                "interface enumerators remain an unsupported dispatch boundary");
            Assert.That(
                @interface.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                arrayObject.Throws.IncludesUnknown,
                Is.True,
                "non-identity element conversions retain conservative enumeration");
            Assert.That(
                arrayObject.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
        }
    }

    private static void AssertDirectIteration(
        EffectSummary summary,
        EffectSummary? baseline = null,
        bool requireNullCheck = true)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                summary.Allocation,
                Is.Not.EqualTo(EffectAllocationKind.Unknown));
            if (baseline is { } expected)
            {
                Assert.That(summary.Allocation, Is.EqualTo(expected.Allocation));
                Assert.That(summary.Reads, Is.EqualTo(expected.Reads));
            }
            Assert.That(
                summary.Throws.IncludesUnknown,
                Is.False);
            if (requireNullCheck)
            {
                Assert.That(
                    summary.Throws.Types.Select(static type => type.ToDisplayString()),
                    Does.Contain("System.NullReferenceException"));
            }
            Assert.That(
                summary.Throws.Types.Select(static type => type.ToDisplayString()),
                Does.Not.Contain("System.InvalidCastException"));
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete),
                $"allocation={summary.Allocation}; unknown={summary.Throws.IncludesUnknown}; " +
                $"throws={string.Join(",", summary.Throws.Types.Select(static type => type.ToDisplayString()))}; " +
                $"uncertainty={summary.Uncertainty}; termination={summary.Termination}; " +
                $"reason={summary.AnalysisIncompleteReason}");
        }
    }
}
