namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class TerminalDeconstructionEffectRegressionTests
{
    [Test]
    public void EarlierSetterAndTerminalSetterExceptionArePreserved()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;

                private static int First {
                    set { state = value; }
                }

                private static int Terminal {
                    set { throw new System.InvalidOperationException(); }
                }

                public static void Assign() {
                    (First, Terminal) = (1, 2);
                }
            }
            """);
        var method = EffectTestHost.SampleMethod(compilation, "Assign");

        var result = new EffectAnalysisSession(compilation).Analyze(method);
        var exceptionNames = result.Summary.Throws.Types.Select(
            static type => type.ToDisplayString()).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "the first setter executes before the terminal setter");
            Assert.That(
                exceptionNames,
                Does.Contain("System.InvalidOperationException"),
                "the terminal setter exception must escape");
            Assert.That(result.Summary.Throws.IncludesUnknown, Is.False);
            Assert.That(
                result.Summary.Termination,
                Is.EqualTo(EffectTermination.Terminates));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }
}
