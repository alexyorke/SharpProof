using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class ImmutableDictionaryTests
{
    [Test]
    public async Task ImmutableDictionaryCreate_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

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