using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ObjectRuntimeTests
    {
        [Test]
        public async Task ObjectGetType_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type TestMethod(object value)
    {
        return value.GetType();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ObjectGetHashCode_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
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
        public async Task ObjectEqualsInstance_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
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
        public async Task ObjectEqualsStatic_NoDiagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
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
        public async Task ExceptionToString_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(Exception error)
    {
        return error.ToString();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ObjectMemberwiseClone_Diagnostic()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public class CloneableSample
{
    [EnforcePure]
    public object {|SP0002:CloneSelf|}()
    {
        return MemberwiseClone();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
