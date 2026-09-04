namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class IndirectLocalMutationNullnessRegressionTests
{
    private static readonly Compilation RefAliasCompilation =
        CreateRefAliasCompilation();

    private static readonly Compilation ReceiverEffectsCompilation =
        CreateReceiverEffectsCompilation();

    [TestCase("WriteThroughAlias")]
    [TestCase("ReadThroughAlias")]
    public void RefAliasesDoNotPreserveStalePointeeFacts(
        string methodName)
    {
        var compilation = RefAliasCompilation;
        var result = EffectTestHost.AnalyzeSample(compilation, methodName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.DivideByZeroException"),
                methodName);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete),
                methodName);
        }
    }

    private static CSharpCompilation CreateRefAliasCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int WriteThroughAlias() {
                    var divisor = 0;
                    ref var alias = ref divisor;
                    divisor = 1;
                    alias = 0;
                    return 1 / divisor;
                }

                public static int ReadThroughAlias() {
                    var divisor = 1;
                    ref readonly var alias = ref divisor;
                    divisor = 0;
                    return 1 / alias;
                }
            }
            """);
    }

    [TestCase("ThroughRefAlias", true, EffectCompleteness.Complete)]
    [TestCase("ThroughLocalFunction", null, null)]
    public void IndirectMutationDoesNotSuppressReceiverEffects(
        string methodName,
        bool? expectedStaticWrite,
        EffectCompleteness? expectedCompleteness)
    {
        var compilation = ReceiverEffectsCompilation;
        var result = EffectTestHost.AnalyzeSample(compilation, methodName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.ApplicationException"),
                methodName);
            if (expectedStaticWrite is { } staticWrite)
            {
                Assert.That(
                    result.Summary.Writes.Regions.Contains(
                        EffectRegionId.Static()),
                    Is.EqualTo(staticWrite),
                    methodName);
            }
            if (expectedCompleteness is { } completeness)
            {
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(completeness),
                    methodName);
            }
        }
    }

    private static CSharpCompilation CreateReceiverEffectsCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Global {
                public static int State;
            }

            public sealed class Target {
                public void Touch() {
                    Global.State++;
                    throw new ApplicationException();
                }
            }

            public static class Sample {
                public static void ThroughRefAlias() {
                    Target? value = null;
                    ref Target? alias = ref value;
                    alias = new Target();
                    value.Touch();
                }

                public static void ThroughLocalFunction() {
                    Target? value = null;
                    Initialize();
                    value.Touch();

                    void Initialize() => value = new Target();
                }
            }
            """);
    }
}
