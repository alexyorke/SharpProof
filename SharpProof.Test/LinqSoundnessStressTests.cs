using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class LinqSoundnessStressTests
{
    [Test]
    public async Task EnumerableRepeatImpureElementArgument_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}()
    {
        return Enumerable.Repeat(Console.Read(), 5);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumerableRangeImpureCountArgument_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}()
    {
        return Enumerable.Range(0, Console.Read());
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumerableTakeImpureCountArgument_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}(IEnumerable<int> values)
    {
        return values.Take(GetImpureCount());
    }

    private static int GetImpureCount() => DateTime.Now.Millisecond;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task EnumerableSkipImpureCountBeforeMaterialize_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}(IEnumerable<int> values)
    {
        return values.Skip(GetImpureCount()).ToArray();
    }

    private static int GetImpureCount() => DateTime.Now.Millisecond;
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqSelectImpureLambda_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}(IEnumerable<int> values)
    {
        return values.Select(value => value + DateTime.Now.Millisecond);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqWherePureLambda_UnknownExternalEnumerator_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public IEnumerable<int> {|SP0002:TestMethod|}(IEnumerable<int> values)
    {
        return values.Where(value => value > 0);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqMaterializePureRangeSelect_Diagnostic()
    {
        var test = @"
using System.Linq;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}()
    {
        return Enumerable.Range(0, 4).Select(value => value + 1).ToArray();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqAnyImpureEnumerator_Diagnostic()
    {
        var test = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SharpProof.Attributes;

public sealed class ImpureEnumerable : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator()
    {
        _ = DateTime.Now.Millisecond;
        yield return 1;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}()
    {
        return new ImpureEnumerable().Any();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task LinqToArrayPureArraySource_Diagnostic()
    {
        var test = @"
using System.Linq;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}(int[] values)
    {
        return values.Where(value => value >= 0).ToArray();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}
