namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class NullableUnwrapCatchReachabilityRegressionTests
{
    [Test]
    public void MatchingHandlersReceiveDirectAndHelperUnwrapEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                private static int s_state;

                public static void Direct() {
                    int? value = null;
                    try { _ = (int)value; }
                    catch (InvalidOperationException) { s_state++; }
                }

                private static int Unwrap(int? value) => (int)value;

                public static void ViaHelper(int? value) {
                    try { _ = Unwrap(value); }
                    catch (InvalidOperationException) { s_state++; }
                }

                public static void Widening(int? value) {
                    try { _ = (long)value; }
                    catch (InvalidOperationException) { s_state++; }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            foreach (var methodName in new[] { "Direct", "ViaHelper", "Widening" })
            {
                var method = EffectTestHost.SampleMethod(compilation, methodName);
                Assert.That(
                    session.Analyze(method).Summary.Writes.Contains(
                        EffectRegionId.Static()),
                    Is.True,
                    methodName);
            }
        }
    }

    [Test]
    public void UnrelatedHandlersStayUnreachable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                private static int s_state;

                public static void WrongCatch(int? value) {
                    try { _ = (int)value; }
                    catch (ArgumentException) { s_state++; }
                }

                public static void WrongCatchBeforeMatching(int? value) {
                    try { _ = (int)value; }
                    catch (ArgumentException) { s_state++; }
                    catch (InvalidOperationException) { }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            foreach (var methodName in new[]
                     { "WrongCatch", "WrongCatchBeforeMatching" })
            {
                var method = EffectTestHost.SampleMethod(compilation, methodName);
                Assert.That(
                    session.Analyze(method).Summary.Writes.Contains(
                        EffectRegionId.Static()),
                    Is.False,
                    methodName);
            }
        }
    }
}
