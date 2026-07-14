using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class RuntimeVersioningTests
{
    [Test]
    public async Task FrameworkNameConstructor_Diagnostic()
    {
        var test = @"
using System.Runtime.Versioning;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public FrameworkName {|SP0002:TestMethod|}()
    {
        return new FrameworkName("".NETCoreApp,Version=v8.0"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}