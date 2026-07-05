using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ClaimsPrincipalTests
    {
        [Test]
        public async Task ClaimsPrincipalIsInRole_Diagnostic()
        {
            var test = @"
using System.Security.Claims;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(ClaimsPrincipal principal)
    {
        return principal.IsInRole(""admin"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
