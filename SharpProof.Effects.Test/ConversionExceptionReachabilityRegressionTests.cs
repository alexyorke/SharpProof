namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ConversionExceptionReachabilityRegressionTests
{
    [Test]
    public void ReferenceAndUnboxingFailuresKeepMatchingCatchesReachable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                private static int s_state;

                public static void ReferenceCast(object value) {
                    try { _ = (string)value; }
                    catch (InvalidCastException) { s_state++; }
                }

                public static void IncompatibleUnbox(object value) {
                    try { _ = (int)value; }
                    catch (InvalidCastException) { s_state++; }
                }

                public static void NullUnbox(object? value) {
                    try { _ = (int)value!; }
                    catch (NullReferenceException) { s_state++; }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[]
                 {
                     "ReferenceCast",
                     "IncompatibleUnbox",
                     "NullUnbox"
                 })
        {
            var result = session.Analyze(EffectTestHost.SampleMethod(compilation, methodName));

            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                methodName);
        }
    }
}
