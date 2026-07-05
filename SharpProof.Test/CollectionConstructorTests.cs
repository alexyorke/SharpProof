using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class CollectionConstructorTests
    {
        [Test]
        public async Task ListConstructor_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public List<int> {|SP0002:TestMethod|}()
    {
        return new List<int>();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DictionaryConstructor_Diagnostic()
        {
            var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Dictionary<int, string> {|SP0002:TestMethod|}()
    {
        return new Dictionary<int, string>();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
