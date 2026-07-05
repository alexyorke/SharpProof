using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ObjectDispatchSoundnessStressTests
    {
        [Test]
        public async Task ObjectToStringVirtualDispatch_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(object value)
    {
        return value.ToString();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ObjectGetHashCodeVirtualDispatch_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(object value)
    {
        return value.GetHashCode();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ObjectEqualsVirtualDispatch_Diagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(object left, object right)
    {
        return left.Equals(right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StaticObjectEqualsVirtualDispatch_NoDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public bool TestMethod(object left, object right)
    {
        return object.Equals(left, right);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task StringToStringKnownTarget_NoDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return value.ToString();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SealedOverrideToStringKnownTarget_NoDiagnostic()
        {
            var test = @"
using SharpProof.Attributes;

public sealed class PureValue
{
    [EnforcePure]
    public override string ToString()
    {
        return ""PureValue"";
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public string TestMethod(PureValue value)
    {
        return value.ToString();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
