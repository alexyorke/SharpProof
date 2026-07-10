using NUnit.Framework;
using VerifyCS = SharpProof.Test.CSharpAnalyzerVerifier<
    SharpProof.Analyzer.SharpProofAnalyzer>;

namespace SharpProof.Test;

[TestFixture]
public class CollectionArrayTests
{
    [Test]
    public async Task ListToArray_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}(List<int> values)
    {
        return values.ToArray();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task QueueToArray_NoDiagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod(Queue<int> values)
    {
        return values.ToArray();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task StackToArray_Diagnostic()
    {
        var test = @"
using System.Collections.Generic;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}(Stack<int> values)
    {
        return values.ToArray();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ArrayConvertAll_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}(int[] values)
    {
        return Array.ConvertAll(values, static value => value + 1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ReadOnlySpanToArray_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}(ReadOnlySpan<int> values)
    {
        return values.ToArray();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshLocalArrayReturnedThroughObjectAlias_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}()
    {
        var array = new int[1];
        object boxed = array;
        return boxed;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshArrayCreationReturnedThroughObjectAlias_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}()
    {
        object boxed = new int[1];
        return boxed;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshLocalArrayReturnedThroughExplicitObjectCast_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}()
    {
        var array = new int[1];
        return (object)array;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshLocalArrayReturnedThroughExplicitObjectCastAlias_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}()
    {
        var array = new int[1];
        object boxed = (object)array;
        return boxed;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshLocalArrayReturnedThroughPostDeclarationObjectAlias_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}()
    {
        var array = new int[1];
        object boxed;
        boxed = array;
        return boxed;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshLocalArrayReturnedThroughSameDeclarationAlias_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}()
    {
        int[] first = new int[1], second = first;
        return second;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshLocalArrayReturnedThroughCoalesce_Diagnostic()
    {
        var test = @"
#nullable enable
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}()
    {
        int[]? array = new int[1];
        return array ?? Array.Empty<int>();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConditionalFreshArrayAssignedThenReturned_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}(bool first)
    {
        int[] array;
        if (first)
        {
            array = new int[1];
        }
        else
        {
            array = new int[2];
        }

        return array;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConditionalFreshArrayAssignedThenMutatedLocally_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(bool first)
    {
        int[] array;
        if (first)
        {
            array = new int[1];
        }
        else
        {
            array = new int[2];
        }

        array[0] = 42;
        return array[0];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshLocalArrayAssignmentWithImpureIndex_Diagnostic()
    {
        var test = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int {|SP0002:TestMethod|}()
    {
        var array = new int[1];
        array[Console.Read()] = 0;
        return array[0];
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}