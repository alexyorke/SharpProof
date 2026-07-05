using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

#nullable enable

namespace SharpProof.Test
{
    [TestFixture]
    public class InteropTests
    {
        [Test]
        public async Task SafeHandleIsInvalid_Diagnostic()
        {
            var test = @"
using System.Runtime.InteropServices;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(SafeHandle handle)
    {
        return handle.IsInvalid;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }










    }
}
