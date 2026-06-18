using NUnit.Framework;
using PurelySharp.Analyzer;
using System.Threading.Tasks;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class KeyedCollectionTests
    {
        [Test]
        public async Task KeyedCollectionContainsForBuiltinKey_Diagnostic()
        {
            var test = @"
using System.Collections.ObjectModel;
using PurelySharp.Attributes;

public sealed class NameCollection : KeyedCollection<string, string>
{
    protected override string GetKeyForItem(string item) => item;
}

public class TestClass
{
    [EnforcePure]
    public bool {|PS0002:TestMethod|}(NameCollection values, string key)
    {
        return values.Contains(key);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
