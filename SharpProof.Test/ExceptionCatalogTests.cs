using NUnit.Framework;
using SharpProof.Analyzer;
using System.Threading.Tasks;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ExceptionCatalogTests
    {
        [Test]
        public async Task FileNotFoundExceptionStringConstructor_NoDiagnostic()
        {
            var test = @"
using System.IO;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public FileNotFoundException TestMethod()
    {
        return new FileNotFoundException(""missing.txt"");
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
