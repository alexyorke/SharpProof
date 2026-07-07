using System.Threading.Tasks;
using SharpProof.Analyzer;
using NUnit.Framework;

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
    public bool TestMethod(ClaimsPrincipal principal)
    {
        return principal.IsInRole(""admin"");
    }
}";

            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(test, concurrentAnalysis: true);
            var diagnostic = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);
            Assert.That(diagnostic.GetMessage(), Does.Contain("TestMethod"));
        }
    }
}
