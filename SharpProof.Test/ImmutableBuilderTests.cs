using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class ImmutableBuilderTests
{
    [Test]
    public async Task ImmutableListBuilderAdd_OnParameter_Diagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void {|SP0002:MutateBuilder|}(ImmutableList<int>.Builder builder)
    {
        builder.Add(1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}