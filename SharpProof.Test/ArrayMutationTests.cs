using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ArrayMutationTests
{
    [Test]
    public async Task ArrayReverseGeneric_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int[] values)
    {
        Array.Reverse(values);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayReverseGenericRange_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int[] values)
    {
        Array.Reverse(values, 0, values.Length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayFillGeneric_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int[] values)
    {
        Array.Fill(values, 42);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayFillGenericRange_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int[] values)
    {
        Array.Fill(values, 42, 0, values.Length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayResize_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(ref int[] values)
    {
        Array.Resize(ref values, values.Length + 1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayCopyRange_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int[] source, int[] destination)
    {
        Array.Copy(source, 0, destination, 0, source.Length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayCopyLengthOverload_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Array source, Array destination, int length)
    {
        Array.Copy(source, destination, length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayConstrainedCopy_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Array source, Array destination, int length)
    {
        Array.ConstrainedCopy(source, 0, destination, 0, length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayCopyTo_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int[] source, int[] destination)
    {
        source.CopyTo(destination, 0);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task BufferBlockCopy_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int[] source, int[] destination)
    {
        Buffer.BlockCopy(source, 0, destination, 0, source.Length * sizeof(int));
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayClearFullArray_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(int[] values)
    {
        Array.Clear(values);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}