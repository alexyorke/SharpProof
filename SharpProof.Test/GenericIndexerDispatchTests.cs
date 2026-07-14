using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class GenericIndexerDispatchTests
{
    [Test]
    public async Task DictionaryIndexerWithUnresolvedGenericKey_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass<TKey, TValue>
{
    [EnforcePure]
    public TValue {|SP0002:TestMethod|}(Dictionary<TKey, TValue> values, TKey key)
    {
        return values[key];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task SortedDictionaryIndexerWithUnresolvedGenericKey_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass<TKey, TValue>
{
    [EnforcePure]
    public TValue {|SP0002:TestMethod|}(SortedDictionary<TKey, TValue> values, TKey key)
    {
        return values[key];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}