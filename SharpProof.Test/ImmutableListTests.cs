using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ImmutableListTests
    {
        [Test]
        public async Task ImmutableListCount_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Count(ImmutableList<int> list)
    {
        return list.Count;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task ImmutableListIndexer_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int First(ImmutableList<int> list)
    {
        return list[0];
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
