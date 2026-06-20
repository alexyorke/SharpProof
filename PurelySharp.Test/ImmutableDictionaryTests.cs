using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ImmutableDictionaryTests
    {
        [Test]
        public async Task ImmutableDictionaryCreate_NoDiagnostic()
        {
            var test = @"
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableDictionary<int, string> CreateDictionary()
    {
        return ImmutableDictionary.Create<int, string>();
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
