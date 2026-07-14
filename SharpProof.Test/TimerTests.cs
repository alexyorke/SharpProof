using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class TimerTests
{
    [TestCase("Start()")]
    [TestCase("Stop()")]
    public async Task SystemTimersTimerMembers_Diagnostic(string invocation)
    {
        var test = $$"""
                     using System.Timers;
                     using SharpProof.Attributes;

                     public class TestClass
                     {
                         [EnforcePure]
                         public void {|SP0002:TestMethod|}(Timer timer)
                         {
                             timer.{{invocation}};
                         }
                     }
                     """;

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}