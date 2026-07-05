using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test
{
    [TestFixture]
    public class ImmutableInterlockedTests
    {
        [Test]
        public async Task ImmutableInterlockedTryAdd_OnField_Diagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    private ImmutableDictionary<string, int> _map = ImmutableDictionary<string, int>.Empty;

    [EnforcePure]
    public void {|SP0002:AddEntry|}()
    {
        ImmutableInterlocked.TryAdd(ref _map, ""a"", 1);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
