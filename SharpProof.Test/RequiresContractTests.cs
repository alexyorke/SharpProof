using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using SharpProof.Analyzer;
using static SharpProof.Test.AnalyzerTestHost;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class RequiresContractTests
    {
        [Test]
        public async Task Requires_CallSiteSatisfiesPrecondition_NoDiagnostic()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Requires(""value > 0"")]
    public static int Callee(int value) => value;

    public static int Caller() => Callee(1);
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Requires_CallSiteViolatesPrecondition_ReportsSp0027()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Requires(""value > 0"")]
    public static int Callee(int value) => value;

    public static int Caller() => {|SP0027:Callee(0)|};
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Requires_EmptyCondition_ReportsInvalidContractArgument()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0024:Requires("""")|}]
    public static int Value(int value) => value;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Requires_ResultPlaceholder_IsRejected()
        {
            var diagnostics = await GetDiagnosticsAsync(@"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Requires(""result > 0"")]
    public static int Value(int value) => value;
}");

            var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.RequiresUnsupportedId);
            Assert.That(diagnostic.GetMessage(), Does.Contain("result placeholder"));
        }

        [Test]
        public async Task Requires_MisplacedOnProperty_ReportsSp0029()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0029:Requires(""true"")|}]
    public int Value => 42;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Requires_AssumptionFeedsEnsures_ProvesPostcondition()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Requires(""value > 0"")]
    [Ensures(""result > 0"")]
    public static int Echo(int value)
    {
        return value;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Requires_AssumptionFeedsRuntimeHazards_SuppressesDivideByZero()
        {
            var diagnostics = await GetDiagnosticsAsync(@"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Requires(""divisor != 0"")]
    public static int Divide(int value, int divisor)
    {
        return value / divisor;
    }
}",
                globalOptions: ImmutableDictionary<string, string>.Empty.Add("sharpproof_runtime_hazard_mode", "sites"));

            Assert.That(diagnostics.Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.UncaughtExceptionSiteId), Is.Empty);
        }

        [Test]
        public async Task Requires_AssumptionFeedsPurityPathFacts_NoDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    [Requires(""divisor != 0"")]
    public static int Divide(int value, int divisor)
    {
        return value / divisor;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
