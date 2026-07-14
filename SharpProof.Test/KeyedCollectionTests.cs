using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class KeyedCollectionTests
{
    [Test]
    public async Task KeyedCollectionContainsForBuiltinKey_Diagnostic()
    {
        var test = @"
using System.Collections.ObjectModel;
using SharpProof.Attributes;

public sealed class NameCollection : KeyedCollection<string, string>
{
    protected override string GetKeyForItem(string item) => item;
}

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(NameCollection values, string key)
    {
        return values.Contains(key);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}