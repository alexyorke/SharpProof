using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class TimeSpanTests
    {
        [Test]
        public async Task TimeSpanConstructor_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan TestMethod()
    {
        return new TimeSpan(1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task TimeSpanAdd_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeSpan {|PS0002:TestMethod|}(TimeSpan left, TimeSpan right)
    {
        return left.Add(right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
