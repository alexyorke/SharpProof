using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class CollectionExpressionTests
{
    [Test]
    public async Task PureMethod_CreateImmutableArray_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Immutable;



public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableArray<int> GetNumbers()
    {
        // Using Create method for immutable array
        return ImmutableArray.Create(1, 2, 3, 4, 5);
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_CreateImmutableArrayRangeFromFreshArray_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Collections.Immutable;

public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableArray<int> GetNumbers()
    {
        return ImmutableArray.CreateRange(new[] { 1, 2, 3 });
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_CreateImmutableArrayRangeProjection_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Collections.Immutable;

public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableArray<int> ProjectNumbers(ImmutableArray<int> values)
    {
        return ImmutableArray.CreateRange(values, static value => value + 1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_CreateImmutableArrayRangeProjectionWithArg_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Collections.Immutable;

public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableArray<int> AddOffset(ImmutableArray<int> values, int offset)
    {
        return ImmutableArray.CreateRange(values, static (value, delta) => value + delta, offset);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_CreateImmutableArraySliceProjection_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Collections.Immutable;

public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableArray<int> ProjectSlice(ImmutableArray<int> values)
    {
        return ImmutableArray.CreateRange(values, 0, 0, static value => value + 1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_CreateImmutableArraySliceProjectionWithArg_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;
using System.Collections.Immutable;

public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableArray<int> ProjectSliceWithOffset(ImmutableArray<int> values, int offset)
    {
        return ImmutableArray.CreateRange(values, 0, 0, static (value, delta) => value + delta, offset);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_CreateImmutableList_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Immutable;



public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableList<string> GetNames()
    {
        // Using Create method for immutable list
        return ImmutableList.Create(""Alice"", ""Bob"", ""Charlie"");
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_MutableArrayWithArrayCreation_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class CollectionExpressionExample
{
    [EnforcePure]
    public int[] GetNumbers()
    {
        // Using new[] array creation expression
        return new[] { 1, 2, 3, 4, 5 };
    }
}";


        var expected = VerifyCS.Diagnostic(AnalyzerDiagnosticCatalog.Get("PurityNotVerifiedRule"))
            .WithSpan(10, 18, 10, 28)
            .WithArguments("GetNumbers");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task PureMethod_MutableListWithArrayInitializer_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



public class CollectionExpressionExample
{
    [EnforcePure]
    public List<string> GetNames()
    {
        // List initialization with collection initializer syntax
        return new List<string> { ""Alice"", ""Bob"", ""Charlie"" };
    }
}";


        var expected = VerifyCS.Diagnostic(AnalyzerDiagnosticCatalog.Get("PurityNotVerifiedRule"))
            .WithSpan(11, 25, 11, 33)
            .WithArguments("GetNames");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task PureMethod_MutableArrayCollectionExpressionSyntax_Diagnostic()
    {
        var testCode = @"
using System;
using SharpProof.Attributes;



public class CollectionExpressionExample
{
    [EnforcePure]
    public int[] GetArray()
    {
        // Using collection expression syntax with array type
        return [1, 2, 3, 4, 5];
    }
}";


        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(10, 18, 10, 26)
            .WithArguments("GetArray");
        await VerifyCS.VerifyAnalyzerAsync(testCode, expected);
    }

    [Test]
    public async Task PureMethod_MutableListWithCollectionExpression_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Generic;



public class CollectionExpressionExample
{
    [EnforcePure]
    public List<int> GetList()
    {
        // Using collection expression with List
        return [1, 2, 3, 4, 5];
    }
}";


        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(11, 22, 11, 29)
            .WithArguments("GetList");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }

    [Test]
    public async Task PureMethod_LocalMutableArrayCollectionExpression_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class CollectionExpressionExample
{
    [EnforcePure]
    public int SumWithLocalArray(int value)
    {
        int[] array = [1, 2, 3];
        array[0] = value;
        return array[0] + array[1] + array[2];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_ReturningFreshLocalCollectionExpressionArray_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class CollectionExpressionExample
{
    [EnforcePure]
    public int[] {|SP0002:GetArray|}()
    {
        int[] array = [1, 2, 3];
        return array;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_ReturningModifiedFreshLocalArray_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;



public class CollectionExpressionExample
{
    [EnforcePure]
    public int[] {|SP0002:GetModifiedArray|}()
    {
        int[] array = new int[5];
        array[0] = 10;
        return array;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_ImmutableArrayCollectionExpressionSyntax_NoDiagnostic()
    {
        var test = @"
// Requires LangVersion 12+
#nullable enable
using System;
using SharpProof.Attributes;
using System.Collections.Immutable;

public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableArray<int> GetImmutableArray()
    {
        return [1, 2, 3, 4, 5];
    }
}";


        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_ReadOnlySpanCollectionExpression_NoDiagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class CollectionExpressionExample
{
    [EnforcePure]
    public ReadOnlySpan<int> GetSpan()
    {
        return [1, 2, 3, 4, 5];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task PureMethod_ImmutableArrayCollectionExpression_ImpureElement_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;
using System.Collections.Immutable;

public class CollectionExpressionExample
{
    [EnforcePure]
    public ImmutableArray<int> GetImmutableArray()
    {
        return [1, 2, Random.Shared.Next(), 4];
    }
}";

        var expected = VerifyCS.Diagnostic("SP0002")
            .WithSpan(9, 32, 9, 49)
            .WithArguments("GetImmutableArray");
        await VerifyCS.VerifyAnalyzerAsync(test, expected);
    }
}