using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;
using static SharpProof.Test.AnalyzerTestHost;

namespace SharpProof.Test;

[TestFixture]
public sealed class RequiresContractTests
{
    [Test]
    public void Requires_RewriterPreservesShadowedLambdaParameter()
    {
        var arguments = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal)
        {
            ["items"] = SyntaxFactory.ParseExpression("source"),
            ["x"] = SyntaxFactory.ParseExpression("outer"),
            ["limit"] = SyntaxFactory.ParseExpression("5")
        };

        Assert.That(
            RequiresContractHelpers.TryRewriteForArguments(
                "items.Any(x => x > limit) && x > 0",
                arguments,
                out var rewritten),
            Is.True);
        Assert.That(rewritten, Does.Contain("x => x > (5)"));
        Assert.That(rewritten, Does.Contain("(outer) > 0"));
        Assert.That(rewritten, Does.Not.Contain("x => (outer)"));
    }

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

        var diagnostic = SingleDiagnostic(diagnostics, "SP0028");
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
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_runtime_hazard_mode", "sites"));

        Assert.That(diagnostics.Where(diagnostic => diagnostic.Id == "SP0011"),
            Is.Empty);
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

    [Test]
    public async Task Requires_GenericTypeParameterUsesCallSiteTypeArgument()
    {
        var test = @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class TestClass
{
    [Requires(""typeof(T) == typeof(int)"")]
    public static void Generic<T>()
    {
    }

    public static void Caller()
    {
        Generic<int>();
        {|SP0027:Generic<string>()|};
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
