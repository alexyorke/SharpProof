using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class CollectionExpressionSpreadTests
{
    [Test]
    public async Task ImmutableArraySpread_NoDiagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public ImmutableArray<int> Extend(ImmutableArray<int> values)
    {
        return [.. values, 42];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableArraySpread_ImpureOperand_Diagnostic()
    {
        var test = @"
using System;
using System.Collections.Immutable;
using SharpProof.Attributes;

public class TestClass
{
    private static ImmutableArray<int> GetValues()
    {
        Console.WriteLine(""side effect"");
        return ImmutableArray<int>.Empty;
    }

    [EnforcePure]
    public ImmutableArray<int> {|SP0002:Extend|}()
    {
        return [.. GetValues(), 42];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableArraySpread_ImpureGetEnumerator_Diagnostic()
    {
        var test = @"
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using SharpProof.Attributes;

public sealed class ImpureSequence : IEnumerable<int>
{
    public IEnumerator<int> GetEnumerator()
    {
        Console.WriteLine(""enumerating"");
        return ((IEnumerable<int>)System.Array.Empty<int>()).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public class TestClass
{
    [EnforcePure]
    public ImmutableArray<int> {|SP0002:Extend|}(ImpureSequence values)
    {
        return [.. values, 42];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ImmutableArraySpread_ImpureMoveNext_Diagnostic()
    {
        var test = @"
using System.Collections.Immutable;
using SharpProof.Attributes;

public static class GlobalState
{
    public static int Count;
}

public sealed class Sequence
{
    [EnforcePure]
    public Enumerator GetEnumerator() => new Enumerator();

    public sealed class Enumerator
    {
        public int Current => 1;

        public bool MoveNext()
        {
            GlobalState.Count++;
            return false;
        }
    }
}

public class TestClass
{
    [EnforcePure]
    public ImmutableArray<int> {|SP0002:Extend|}(Sequence values)
    {
        return [.. values, 42];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}