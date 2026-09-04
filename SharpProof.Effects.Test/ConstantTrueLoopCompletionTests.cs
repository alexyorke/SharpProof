namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ConstantTrueLoopCompletionTests
{
    private static readonly Compilation SharedCompilation = CreateCompilation();

    [TestCase("DoForever", "RunAfterDoForever", false)]
    [TestCase("ForForever", "RunAfterForForever", false)]
    [TestCase("DoBreak", "RunAfterDoBreak", true)]
    [TestCase("ForBreak", "RunAfterForBreak", true)]
    [TestCase("BreakThroughFinally", "RunAfterFinally", false)]
    [TestCase("ReturnFromLoop", "RunAfterReturn", true)]
    [TestCase("GotoOutOfLoop", "RunAfterGoto", true)]
    [TestCase("RootBreak", "RunAfterBreak", true)]
    public void ConstantLoopCompletionControlsCallerSuffix(
        string helperName,
        string callerName,
        bool expectedCompletion)
    {
        var (compilation, helper, caller) = CreateCase(
            helperName,
            callerName);
        var completion = EffectTestHost.CreateCompletionFacts(compilation);
        var summary = new EffectAnalysisSession(compilation)
            .Analyze(caller)
            .Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                completion.MethodCanCompleteNormally(helper),
                Is.EqualTo(expectedCompletion),
                helperName);
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Static()),
                Is.EqualTo(expectedCompletion),
                callerName);
        }
    }

    private static (
        Compilation Compilation,
        IMethodSymbol Helper,
        IMethodSymbol Caller) CreateCase(
            string helperName,
            string callerName)
    {
        var compilation = SharedCompilation;
        return (
            compilation,
            EffectTestHost.SampleMethod(compilation, helperName),
            EffectTestHost.SampleMethod(compilation, callerName));
    }

    private static CSharpCompilation CreateCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int s_state;

                private static void Spin() {
                    while (true) {
                    }
                }

                private static void DoForever() {
                    do {
                    } while (true);
                }

                private static void ForForever() {
                    for (; true;) {
                    }
                }

                private static void DoBreak() {
                    do {
                        break;
                    } while (true);
                }

                private static void ForBreak() {
                    for (; true;) {
                        break;
                    }
                }

                private static void BreakThroughFinally() {
                    while (true) {
                        try {
                            break;
                        }
                        finally {
                            Spin();
                        }
                    }
                }

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

                public static void RunAfterDoForever() {
                    DoForever();
                    s_state++;
                }

                public static void RunAfterForForever() {
                    ForForever();
                    s_state++;
                }

                public static void RunAfterDoBreak() {
                    DoBreak();
                    s_state++;
                }

                public static void RunAfterForBreak() {
                    ForBreak();
                    s_state++;
                }

                public static void RunAfterFinally() {
                    BreakThroughFinally();
                    s_state++;
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
    }
}
