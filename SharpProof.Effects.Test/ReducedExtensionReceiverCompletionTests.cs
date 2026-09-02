namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ReducedExtensionReceiverCompletionTests
{
    [Test]
    public void NullReceiverDoesNotMakeReturningReducedExtensionTerminal()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int Value;
            }

            public static class Extensions {
                public static int AcceptNull(this string? value) =>
                    value is null ? 1 : 0;
            }

            public static class Subject {
                private static void InvokeExtension() {
                    _ = ((string?)null).AcceptNull();
                }

                public static void Exercise(Box suffix) {
                    if (suffix is null) return;
                    InvokeExtension();
                    suffix.Value++;
                }
            }
            """);
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Subject",
            "Exercise");

        var summary = new EffectAnalysisSession(compilation)
            .Analyze(method)
            .Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True,
                "the returning extension call cannot suppress the suffix");
            Assert.That(
                summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(summary.Throws.IsEmpty, Is.True);
        }
    }
}
