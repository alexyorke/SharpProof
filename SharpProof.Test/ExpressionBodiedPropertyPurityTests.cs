using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public sealed class ExpressionBodiedPropertyPurityTests
{
    [Test]
    public async Task PureExpressionBodiedProperty_WithImpureBody_ReportsSp0002()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    private int _counter;

    [Pure]
    public int Bad => _counter++;
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(9, 16, 9, 19)
            .WithArguments("get_Bad");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task PureExpressionBodiedProperty_WithPureBody_RemainsPure()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    private readonly int _value = 3;

    [Pure]
    public int Good => _value + 1;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureExpressionBodiedIndexer_WithImpureBody_ReportsSp0002()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    private int _counter;

    [Pure]
    public int this[int value] => _counter++;
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(9, 16, 9, 20)
            .WithArguments("get_Item");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task ReadingImpureExpressionBodiedProperty_FromPureMethod_ReportsSp0002()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Counter
{
    private int _reads;

    public int Value => _reads++;
}

public static class TestClass
{
    [EnforcePure]
    public static int Read(Counter counter) => counter.Value;
}";

        var expected = VerifyCS.Diagnostic(SharpProofDiagnostics.PurityNotVerifiedId)
            .WithSpan(14, 23, 14, 27)
            .WithArguments("Read");

        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task ReadingPureExpressionBodiedProperty_FromPureMethod_RemainsPure()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Counter
{
    private readonly int _value = 5;

    [Pure]
    public int Value => _value;
}

public static class TestClass
{
    [EnforcePure]
    public static int Read(Counter counter) => counter.Value;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UnattributedPureExpressionBodiedProperty_DoesNotReportSp0004()
    {
        var test = @"
public sealed class TestClass
{
    private readonly int _value = 7;

    public int Value => _value + 1;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}