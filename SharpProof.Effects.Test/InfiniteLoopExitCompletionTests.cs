namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class InfiniteLoopExitCompletionTests
{
    [TestCase("ReturnFromLoop", "RunAfterReturn")]
    [TestCase("GotoOutOfLoop", "RunAfterGoto")]
    [TestCase("RootBreak", "RunAfterBreak")]
    public void ValidInfiniteLoopExitsAllowCallerSuffix(
        string helperName,
        string callerName)
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int s_state;

                private static void ReturnFromLoop() {
                    while (true) {
                        return;
                    }
                }

                private static void GotoOutOfLoop() {
                    while (true) {
                        goto Exit;
                    }

                Exit:
                    return;
                }

                private static void RootBreak() {
                    while (true)
                        break;
                }

                public static void RunAfterReturn() {
                    ReturnFromLoop();
                    s_state++;
                }

                public static void RunAfterGoto() {
                    GotoOutOfLoop();
                    s_state++;
                }

                public static void RunAfterBreak() {
                    RootBreak();
                    s_state++;
                }
            }
            """);
        var helper = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            helperName);
        var caller = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            callerName);
        var facts = new DefiniteOperationFacts(
            compilation,
            System.Threading.CancellationToken.None);
        var result = new EffectAnalysisSession(compilation).Analyze(caller);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                facts.MethodCanCompleteNormally(helper),
                Is.True,
                helperName);
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                callerName);
        }
    }
}
