using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class ConvertTests
{
    [Test]
    public async Task ConvertFromBase64String_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|SP0002:TestMethod|}(string value)
    {
        return Convert.FromBase64String(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertFromBase64String_LocalNonEscapingUse_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(string value)
    {
        var bytes = Convert.FromBase64String(value);
        return bytes.Length;
    }
            }";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertFromBase64String_LocalReturned_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|SP0002:TestMethod|}(string value)
    {
        var bytes = Convert.FromBase64String(value);
        return bytes;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertFromHexString_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|SP0002:TestMethod|}(string value)
    {
        return Convert.FromHexString(value);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertFromHexString_LocalNonEscapingUse_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(string value)
    {
        var bytes = Convert.FromHexString(value);
        return bytes.Length;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertFromHexString_LocalReturned_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|SP0002:TestMethod|}(string value)
    {
        var bytes = Convert.FromHexString(value);
        return bytes;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertFromBase64CharArray_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|SP0002:TestMethod|}(string value)
    {
        var chars = value.ToCharArray();
        return Convert.FromBase64CharArray(chars, 0, chars.Length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertFromBase64CharArray_LocalNonEscapingUse_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}(string value)
    {
        var chars = value.ToCharArray();
        var bytes = Convert.FromBase64CharArray(chars, 0, chars.Length);
        return bytes.Length;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertFromBase64CharArray_LocalReturned_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] {|SP0002:TestMethod|}(string value)
    {
        var chars = value.ToCharArray();
        var bytes = Convert.FromBase64CharArray(chars, 0, chars.Length);
        return bytes;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertToBase64String_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(byte[] bytes)
    {
        return Convert.ToBase64String(bytes);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertToBase64StringSegment_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(byte[] bytes)
    {
        return Convert.ToBase64String(bytes, 0, bytes.Length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertToHexString_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(byte[] bytes)
    {
        return Convert.ToHexString(bytes);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertToHexStringSegment_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(byte[] bytes)
    {
        return Convert.ToHexString(bytes, 0, bytes.Length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertToHexStringSpan_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public string {|SP0002:TestMethod|}(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(bytes);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConvertChangeTypeTypeOverload_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}(object value)
    {
        return Convert.ChangeType(value, typeof(int));
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}