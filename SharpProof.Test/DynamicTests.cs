using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class DynamicTests
{
    [Test]
    public async Task DynamicUnaryOperation_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    private static readonly dynamic DynamicValue = 10;

    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        return -DynamicValue;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task DynamicInterfaceInvocation_ConservativeImpure()
    {
        var test = @"
using SharpProof.Attributes;

public interface ICounter
{
    int Increment(int value);
}

public class Counter : ICounter
{
    public int Increment(int value) => value + 1;
}

public class TestClass
{
    private static readonly dynamic _counter = new Counter();

    [EnforcePure]
    public int {|SP0002:Process|}(int value)
    {
        return _counter.Increment(value);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}