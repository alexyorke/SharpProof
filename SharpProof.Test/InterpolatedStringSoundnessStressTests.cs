using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class InterpolatedStringSoundnessStressTests
    {
        [Test]
        public async Task ObjectInterpolationVirtualToString_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(object value)
    {
        return $""{value}"";
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task GenericInterpolationUnknownToString_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}<T>(T value)
    {
        return $""{value}"";
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SealedImpureToStringInterpolation_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class ImpureValue
{
    public override string ToString()
    {
        Console.WriteLine(""format"");
        return ""value"";
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(ImpureValue value)
    {
        return $""{value}"";
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SealedPureToStringInterpolation_NoDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class PureValue
{
    [EnforcePure]
    public override string ToString()
    {
        return ""value"";
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public string TestMethod(PureValue value)
    {
        return $""{value}"";
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringAndIntInterpolation_PreservesBalancedNoDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public string TestMethod(string name, int count)
    {
        return $""{name}: {count}"";
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task InterpolatedStringInsideLocalFunction_NoDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public string TestMethod(int x)
    {
        string Local() => $""value={x}"";
        return Local();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
