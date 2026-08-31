namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class IndirectLocalMutationNullnessRegressionTests
{
    [TestCase("WriteThroughAlias")]
    [TestCase("ReadThroughAlias")]
    public void RefAliasesDoNotPreserveStalePointeeFacts(
        string methodName)
    {
        var compilation = EffectTestHost.CreateCompilation(
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
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                methodName));

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

    [TestCase("ThroughRefAlias")]
    [TestCase("ThroughLocalFunction")]
    public void IndirectMutationDoesNotSuppressReceiverEffects(
        string methodName)
    {
        var compilation = EffectTestHost.CreateCompilation(
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
        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                methodName));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.ApplicationException"),
                methodName);
            if (methodName == "ThroughRefAlias")
            {
                Assert.That(
                    result.Summary.Writes.Regions,
                    Does.Contain(EffectRegionId.Static()),
                    methodName);
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    methodName);
            }
        }
    }
}
