namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class UsingInterfaceReimplementationDispatchRegressionTests
{
    [Test]
    public void OpenClassDisposeImplementationAllowsDerivedInterfaceReimplementation()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public class BaseResource : IDisposable {
                public void Dispose() { }
            }

            public sealed class DerivedResource : BaseResource, IDisposable {
                private static int s_state;

                void IDisposable.Dispose() {
                    s_state++;
                    throw new InvalidOperationException();
                }
            }

            public static class Subject {
                public static void Exercise(BaseResource resource) {
                    using (resource) { }
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
            Assert.That(summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                summary.Uncertainty & EffectUncertainty.Dispatch,
                Is.EqualTo(EffectUncertainty.Dispatch));
            Assert.That(summary.Writes.IsUnknown, Is.True);
            Assert.That(summary.Throws.IncludesUnknown, Is.True);
        }
    }
}
