using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public class RangeAndIndexTests
{
    [Test]
    public async Task FromEndIndex_IsPure()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class C
{
    [EnforcePure]
    public int Last(int[] a)
    {
        return a[^1];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RangeSlice_IsPure()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class C
{
    [EnforcePure]
    public int[] Tail(int[] a)
    {
        return a[1..];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RangeWithExpressions_PureWhenEndpointsPure()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class C
{
    [EnforcePure]
    public int[] Middle(int[] a, int start, int len)
    {
        var s = start;
        var e = start + len;
        return a[s..e];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task IndexVariable_IsPure()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class C
{
    [EnforcePure]
    public int Last(int[] a)
    {
        Index idx = ^1;
        return a[idx];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task RangeVariable_IsPure()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class C
{
    [EnforcePure]
    public int[] Middle(int[] a)
    {
        Range range = 1..^1;
        return a[range];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImplicitIndexerReference_WithPureLengthAndIndexer_IsPure()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Bag
{
    public int Length => 3;
    public int this[int index] => index + 10;
}

public sealed class C
{
    [EnforcePure]
    public int Last(Bag bag)
    {
        return bag[^1];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImplicitIndexerReference_WithImpureLengthGetter_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class Bag
{
    public int Length
    {
        get
        {
            Console.WriteLine(""length"");
            return 3;
        }
    }

    public int this[int index] => index + 10;
}

public sealed class C
{
    [EnforcePure]
    public int {|SP0002:Last|}(Bag bag)
    {
        return bag[^1];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImplicitIndexerReference_WithImpureIndexerGetter_ReportsSP0002()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public sealed class Bag
{
    public int Length => 3;

    public int this[int index]
    {
        get
        {
            Console.WriteLine(index);
            return index + 10;
        }
    }
}

public sealed class C
{
    [EnforcePure]
    public int {|SP0002:Last|}(Bag bag)
    {
        return bag[^1];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImplicitRangeIndexer_WithPureSliceMethod_IsPure()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Buffer
{
    public int Length => 8;
    [EnforcePure]
    public int Slice(int start, int length) => start + length;
}

public sealed class C
{
    [EnforcePure]
    public int Middle(Buffer buffer)
    {
        return buffer[1..^1];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}