using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class TimerTests
    {
        [TestCase("Start()")]
        [TestCase("Stop()")]
        public async Task SystemTimersTimerMembers_Diagnostic(string invocation)
        {
            var test = $$"""
using System.Timers;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(Timer timer)
    {
        timer.{{invocation}};
    }
}
""";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
