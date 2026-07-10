using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class GuidTests
{
    [Test]
    public async Task GuidNewGuid_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Guid {|SP0002:TestMethod|}()
    {
        return Guid.NewGuid();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidParse_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Guid {|SP0002:TestMethod|}(string value)
    {
        return Guid.Parse(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidToString_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(Guid value)
    {
        return value.ToString();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidDeterministicValueMembers_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Guid value)
    {
        return value.Equals(Guid.Empty) || value.CompareTo(Guid.Empty) == 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidTryParse_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(string value)
    {
        return Guid.TryParse(value, out _);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidExactParseAndFormat_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(string value)
    {
        var parsed = Guid.ParseExact(value, ""D"");
        return Guid.TryParseExact(value, ""D"", out var other)
            ? parsed.ToString(""D"") + other.ToString(""N"")
            : parsed.ToString(""B"");
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidStringConstructor_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Guid {|SP0002:TestMethod|}(string value)
    {
        return new Guid(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidByteArrayConstructor_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Guid {|SP0002:TestMethod|}(byte[] value)
    {
        return new Guid(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidToByteArrayNonEscapingUse_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Guid value)
    {
        return value.ToByteArray().Length;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidToByteArrayReturnedArray_UsesGeneratedFreshOwnedArrayEvidence_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(Guid value)
    {
        return value.ToByteArray();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidToByteArrayLocalReturned_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(Guid value)
    {
        var bytes = value.ToByteArray();
        return bytes;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidToByteArrayBigEndianNonEscapingUse_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Guid value)
    {
        return value.ToByteArray(true).Length;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidToByteArrayBigEndianReturnedArray_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(Guid value)
    {
        return value.ToByteArray(true);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task GuidToByteArrayBigEndianLocalReturned_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(Guid value)
    {
        var bytes = value.ToByteArray(true);
        return bytes;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}