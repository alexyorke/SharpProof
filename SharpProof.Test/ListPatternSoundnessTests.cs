using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class ListPatternSoundnessTests
{
    [Test]
    public async Task ArrayListPattern_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public bool TestMethod(int[] values)
    {
        return values is [1, _, ..];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StringListPattern_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public bool TestMethod(string text)
    {
        return text is [_, _];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task CustomListPatternImpureLength_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence
{
    public int Length
    {
        get
        {
            GlobalState.Count++;
            return 2;
        }
    }

    public int this[int index] => index;
}

public sealed class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Sequence values)
    {
        return values is [0, 1];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task CustomListPatternImpureIndexer_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence
{
    public int Length => 2;

    public int this[int index]
    {
        get
        {
            GlobalState.Count++;
            return index;
        }
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Sequence values)
    {
        return values is [0, 1];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task InterfaceListPatternImpureCountImplementation_Diagnostic()
    {
        var test = @"
using System.Collections;
using System.Collections.Generic;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence : IReadOnlyList<int>
{
    public int Count
    {
        get
        {
            GlobalState.Count++;
            return 2;
        }
    }

    public int this[int index] => index;

    public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)new int[0]).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IReadOnlyList<int> values)
    {
        return values is [0, 1];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task InterfaceListPatternImpureIndexerImplementation_Diagnostic()
    {
        var test = @"
using System.Collections;
using System.Collections.Generic;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence : IReadOnlyList<int>
{
    public int Count => 2;

    public int this[int index]
    {
        get
        {
            GlobalState.Count++;
            return index;
        }
    }

    public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)new int[0]).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(IReadOnlyList<int> values)
    {
        return values is [0, 1];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArraySlicePattern_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public bool TestMethod(int[] values)
    {
        return values is [1, .. var tail] && tail.Length >= 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task CustomSlicePatternImpureSlice_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence
{
    public int Length => 2;
    public int this[int index] => index;

    public Sequence Slice(int start, int length)
    {
        GlobalState.Count++;
        return this;
    }
}

public sealed class TestClass
{
    [EnforcePure]
    public bool {|SP0002:TestMethod|}(Sequence values)
    {
        return values is [0, .. var tail] && tail.Length >= 0;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}