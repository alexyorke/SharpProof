using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ClaimsPrincipalTests
    {
        [Test]
        public async Task ClaimsPrincipalIsInRole_Diagnostic()
        {
            var test = @"
using System.Security.Claims;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(ClaimsPrincipal principal)
    {
        return principal.IsInRole(""admin"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
