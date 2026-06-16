using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class SearchLibBackedPurityFlowTests
    {
        [Test]
        public async Task ContradictoryNestedGuardedImpureCall_NoDiagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int x)
    {
        if (x > 0)
        {
            if (x < 0)
            {
                Console.WriteLine(x);
            }
        }

        return x;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ReachableNestedGuardedImpureCall_Diagnostic()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|PS0002:TestMethod|}(int x)
    {
        if (x > 0)
        {
            if (x >= 0)
            {
                Console.WriteLine(x);
            }
        }

        return x;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
