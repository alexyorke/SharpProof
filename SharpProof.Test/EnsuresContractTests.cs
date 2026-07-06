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
    public sealed class EnsuresContractTests
    {
        [Test]
        public async Task Ensures_StraightLineReturn_Proven()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result == 1"")]
    public int Value()
    {
        return 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_BranchRefinedReturn_Proven()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    public int Normalize(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_FailingReturn_ReportsSp0018()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    public int Identity()
    {
        return {|SP0018:0|};
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_OneOfMultipleReturnSitesFails_ReportsOnlyFailingSite()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result >= 0"")]
    public int Normalize(bool useValue)
    {
        if (useValue)
        {
            return {|SP0018:-1|};
        }

        return 0;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_MultipleAttributes_CanAllBeProven()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    [Ensures(""result < 10"")]
    public int Value()
    {
        return 5;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_LocalVariableReference_IsRejected()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0019:Ensures(""local > 0"")|}]
    public int Value(int input)
    {
        var local = input + 1;
        return local;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_VoidMethod_IsRejected()
        {
            var diagnostics = await GetDiagnosticsAsync(@"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""true"")]
    public void Run()
    {
    }
}");

            Assert.That(SingleDiagnostic(diagnostics, SharpProofDiagnostics.EnsuresUnsupportedId).GetMessage(), Does.Contain("void-returning members"));
        }

        [Test]
        public async Task Ensures_MisplacedOnProperty_ReportsSp0020()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [{|SP0020:Ensures(""true"")|}]
    public int Value => 42;
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task Ensures_SmtOff_RemainsConservative()
        {
            var diagnostics = await GetDiagnosticsAsync(@"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    public int Normalize(int value)
    {
        if (value > 0)
        {
            return value;
        }

        return 1;
    }
}",
                globalOptions: ImmutableDictionary<string, string>.Empty.Add("sharpproof_smt_mode", "off"));

            var ensuresDiagnostics = diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.EnsuresUnsupportedId)
                .ToArray();
            Assert.That(ensuresDiagnostics, Has.Length.EqualTo(2));
            Assert.That(ensuresDiagnostics.All(diagnostic => diagnostic.GetMessage().Contains("SMT")), Is.True);
        }

        [Test]
        public async Task Ensures_UnreachableReturnSite_DoesNotReport()
        {
            var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    public int Value()
    {
        return 1;
        return -1;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
