using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class PurityPostCfgRegressionTests
{
    [Test]
    public async Task UninvokedLambdaWithImpureBody_DoesNotImpurifyOuterMethod()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        Func<int> callback = () => Console.Read();
        return 42;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UninvokedLocalFunctionWithThrow_DoesNotImpurifyOuterMethod()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        int Local()
        {
            throw new InvalidOperationException();
        }

        return 42;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task UninvokedLocalFunctionWithKnownImpureInvocation_DoesNotImpurifyOuterMethod()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        void Local()
        {
            Console.WriteLine();
        }

        return 42;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}