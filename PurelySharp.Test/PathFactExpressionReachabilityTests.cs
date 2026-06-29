using NUnit.Framework;
using System.Threading.Tasks;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class PathFactExpressionReachabilityTests
    {
        [Test]
        public async Task ConditionalExpression_ImpossibleArmWithImpureCall_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value > 0 ? value : Impure();
    }

    [Impure]
    private static int Impure() => 1;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalAnd_ImpossibleRightOperandWithImpureCall_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(int value)
    {
        if (value <= 0)
        {
            return false;
        }

        return value <= 0 && Impure();
    }

    [Impure]
    private static bool Impure() => true;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ConditionalOr_ImpossibleRightOperandWithImpureCall_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(int value)
    {
        if (value <= 0)
        {
            return true;
        }

        return value > 0 || Impure();
    }

    [Impure]
    private static bool Impure() => true;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Coalesce_ImpossibleWhenNullWithImpureCall_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value ?? Impure();
    }

    [Impure]
    private static string Impure() => string.Empty;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Coalesce_WhenNullBranchReceivesNullFact_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return value ?? (value is null ? string.Empty : Impure());
    }

    [Impure]
    private static string Impure() => string.Empty;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
