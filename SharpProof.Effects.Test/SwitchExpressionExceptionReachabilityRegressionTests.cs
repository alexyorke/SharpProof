namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class SwitchExpressionExceptionReachabilityRegressionTests
{
    [Test]
    public void CalleeSwitchExpressionKeepsMatchingCatchReachable()
    {
        var compilation = CreateCompilation();
        var session = new EffectAnalysisSession(compilation);
        var caller = EffectTestHost.SampleMethod(compilation, "Caller");
        var reachability = EffectTestHost.CreateHandlerReachability(
            compilation,
            caller,
            session);

        Assert.That(
            reachability.IsReachable(
                EffectTestHost.CatchClauseIn(caller),
                inFilter: false),
            Is.True);
    }

    [Test]
    public void CalleeSwitchExpressionDoesNotReachWrongCatch()
    {
        var compilation = CreateCompilation();
        var session = new EffectAnalysisSession(compilation);
        var caller = EffectTestHost.SampleMethod(compilation, "WrongCatch");
        var reachability = EffectTestHost.CreateHandlerReachability(
            compilation,
            caller,
            session);

        Assert.That(
            reachability.IsReachable(
                EffectTestHost.CatchClauseIn(caller),
                inFilter: false),
            Is.False);
    }

    [Test]
    public void LocalCalleeSwitchExpressionKeepsMatchingCatchReachable()
    {
        var compilation = CreateCompilation();
        var session = new EffectAnalysisSession(compilation);
        var caller = EffectTestHost.SampleMethod(
            compilation,
            "LocalFunctionCaller");
        var reachability = EffectTestHost.CreateHandlerReachability(
            compilation,
            caller,
            session);

        Assert.That(
            reachability.IsReachable(
                EffectTestHost.CatchClauseIn(caller),
                inFilter: false),
            Is.True);
    }

    [Test]
    public void CalleeSummaryRecordsSwitchExpressionException()
    {
        var compilation = CreateCompilation();
        var summary = EffectTestHost.AnalyzeSample(compilation, "Partial")
            .Summary;

        Assert.That(
            summary.Throws.Types.Select(static type => type.ToDisplayString()),
            Does.Contain("System.Runtime.CompilerServices.SwitchExpressionException"));
    }

    private static CSharpCompilation CreateCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Runtime.CompilerServices;

            public static class Sample {
                private static int Partial(int value) => value switch {
                    1 => 10,
                    2 => 20
                };

                public static void Caller(int value) {
                    try {
                        _ = Partial(value);
                    }
                    catch (SwitchExpressionException) {
                    }
                }

                public static void WrongCatch(int value) {
                    try {
                        _ = Partial(value);
                    }
                    catch (ArgumentException) {
                    }
                }

                public static void LocalFunctionCaller(int value) {
                    static int LocalPartial(int localValue) =>
                        localValue switch {
                            1 => 10,
                            2 => 20
                        };

                    try {
                        _ = LocalPartial(value);
                    }
                    catch (SwitchExpressionException) {
                    }
                }
            }
            """);
    }
}
