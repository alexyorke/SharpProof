namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class IndirectLocalMutationNullnessRegressionTests
{
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
