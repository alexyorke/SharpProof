using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class FieldReferenceValueTypeTests
{
    [Test]
    public async Task StructObjectCreationFieldRead_NoDiagnostic()
    {
        var code = @"
using SharpProof.Attributes;

public struct Counter
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public int ReadObjectCreation()
    {
        return new Counter().Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(code);
    }

    [Test]
    public async Task StructTemporaryFieldRead_NoDiagnostic()
    {
        var code = @"
using SharpProof.Attributes;

public struct Counter
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public int ReadTemporary()
    {
        return default(Counter).Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(code);
    }
}