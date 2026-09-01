namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ConstantTrueLoopCompletionTests
{
    [TestCase("DoForever", "RunAfterDoForever")]
    [TestCase("ForForever", "RunAfterForForever")]
    public void NonexitingConstantTrueLoopsSuppressCallerSuffix(
        string helperName,
        string callerName)
    {
        var (compilation, helper, caller) = CreateCase(
            helperName,
            callerName);
        var completion = new DefiniteOperationFacts(
            compilation,
            CancellationToken.None);
        var summary = new EffectAnalysisSession(compilation)
            .Analyze(caller)
            .Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                completion.MethodCanCompleteNormally(helper),
                Is.False,
                helperName);
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Static()),
                Is.False,
                callerName);
        }
    }

    [TestCase("DoBreak", "RunAfterDoBreak")]
    [TestCase("ForBreak", "RunAfterForBreak")]
    public void ExitingConstantTrueLoopsRetainCallerSuffix(
        string helperName,
        string callerName)
    {
        var (compilation, helper, caller) = CreateCase(
            helperName,
            callerName);
        var completion = new DefiniteOperationFacts(
            compilation,
            CancellationToken.None);
        var summary = new EffectAnalysisSession(compilation)
            .Analyze(caller)
            .Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                completion.MethodCanCompleteNormally(helper),
                Is.True,
                helperName);
            Assert.That(
                summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
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
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int s_state;

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
            }
            """);
        return (
            compilation,
            EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                helperName),
            EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                callerName));
    }
}
