using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class ObjectInitializerTests
{
    [Test]
    public async Task ObjectInitializerWithImpureSetter_Diagnostic()
    {
        var testCode = @"
using System;
using SharpProof.Attributes;

public class Target
{
    public int Value
    {
        set { Console.WriteLine(value); }
    }
}

public class TestClass
{
    [EnforcePure]
    public Target {|SP0002:Create|}()
    {
        return new Target { Value = 1 };
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Test]
    public async Task NestedObjectInitializerMutatingExistingMember_Diagnostic()
    {
        var testCode = @"
using SharpProof.Attributes;

public class Shared
{
    public int X;
}

public class Holder
{
    public static readonly Shared SharedInstance = new Shared();

    public Shared Field = SharedInstance;
}

public class TestClass
{
    [EnforcePure]
    public Holder {|SP0002:Create|}()
    {
        return new Holder { Field = { X = 1 } };
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Test]
    public async Task ObjectInitializerIndexerWithImpureIndex_Diagnostic()
    {
        var testCode = @"
using System;
using SharpProof.Attributes;

public class Target
{
    public int this[int index]
    {
        [EnforcePure]
        set { }
    }
}

public class TestClass
{
    [EnforcePure]
    public Target {|SP0002:Create|}()
    {
        return new Target { [Console.Read()] = 1 };
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Test]
    public async Task ObjectInitializerOwnedArrayFieldEscape_Diagnostic()
    {
        var testCode = @"
using SharpProof.Attributes;

public sealed class Holder
{
    public int[] Values;
}

public class TestClass
{
    [EnforcePure]
    public Holder {|SP0002:Create|}()
    {
        int[] values = [1, 2, 3];
        return new Holder { Values = values };
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Test]
    public async Task RecordPrimaryConstructorOwnedArrayEscape_Diagnostic()
    {
        var testCode = @"
using SharpProof.Attributes;

public sealed record Holder(int[] Values);

public class TestClass
{
    [EnforcePure]
    public Holder {|SP0002:Create|}()
    {
        int[] values = [1, 2, 3];
        return new Holder(values);
    }
}
";

        await VerifyCS.VerifyAnalyzerAsync(testCode);
    }

    [Test]
    public async Task FreshLocalArrayEscapesThroughClassConstructor_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public readonly int[] Items;

    [EnforcePure]
    public Box(int[] items)
    {
        Items = items;
    }
}

public class TestClass
{
    [EnforcePure]
    public Box {|SP0002:TestMethod|}()
    {
        var items = new int[1];
        items[0] = 42;
        return new Box(items);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshLocalArrayEscapesThroughTupleReturn_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public (int[] Values, int Count) {|SP0002:TestMethod|}()
    {
        var values = new int[1];
        return (values, values.Length);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshLocalArrayEscapesAfterTupleDeconstruction_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}()
    {
        var (values, count) = (new int[1], 1);
        return values;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshLocalArrayEscapesAfterTupleDeconstructionAssignment_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] {|SP0002:TestMethod|}()
    {
        int[] values;
        (values, _) = (new int[1], 1);
        return values;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshMutableObjectReturned_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public Box {|SP0002:TestMethod|}()
    {
        return new Box();
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshMutableObjectReturnedThroughLocalAlias_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public Box {|SP0002:TestMethod|}()
    {
        var box = new Box();
        return box;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshMutableObjectReturnedThroughObjectAlias_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}()
    {
        var box = new Box();
        object alias = box;
        return alias;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshMutableObjectEscapesThroughTupleReturn_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public (Box Box, int Count) {|SP0002:TestMethod|}()
    {
        return (new Box(), 1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshMutableObjectAliasEscapesThroughTupleReturn_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public (Box Box, int Count) {|SP0002:TestMethod|}()
    {
        var box = new Box();
        return (box, 1);
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshMutableObjectEscapesAfterTupleDeconstruction_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public Box {|SP0002:TestMethod|}()
    {
        var (box, count) = (new Box(), 1);
        return box;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshMutableObjectEscapesAfterTupleDeconstructionAssignment_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public Box {|SP0002:TestMethod|}()
    {
        Box box;
        (box, _) = (new Box(), 1);
        return box;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConditionalFreshMutableObjectAssignedThenReturned_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public Box {|SP0002:TestMethod|}(bool first)
    {
        Box box;
        if (first)
        {
            box = new Box();
        }
        else
        {
            box = new Box();
        }

        return box;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConditionalFreshMutableObjectAssignedThenReturnedThroughObjectAlias_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public object {|SP0002:TestMethod|}(bool first)
    {
        Box box;
        if (first)
        {
            box = new Box();
        }
        else
        {
            box = new Box();
        }

        object alias = box;
        return alias;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task ConditionalFreshMutableObjectAssignedThenMutatedLocally_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod(bool first)
    {
        Box box;
        if (first)
        {
            box = new Box();
        }
        else
        {
            box = new Box();
        }

        box.Value = 1;
        return box.Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshMutableObjectEscapesThroughImmutableWrapperConstructor_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Holder
{
    public Box Value { get; }

    [EnforcePure]
    public Holder(Box value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public Holder TestMethod()
    {
        return new Holder(new Box());
    }
}";

        var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");
        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(23, 19, 23, 29)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetValue, expectedTestMethod);
    }

    [Test]
    public async Task FreshMutableObjectAliasEscapesThroughImmutableWrapperConstructor_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Holder
{
    public Box Value { get; }

    [EnforcePure]
    public Holder(Box value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public Holder TestMethod()
    {
        var box = new Box();
        return new Holder(box);
    }
}";

        var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");
        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(23, 19, 23, 29)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetValue, expectedTestMethod);
    }

    [Test]
    public async Task FreshMutableObjectEscapesThroughLocalImmutableWrapperConstructor_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Holder
{
    public readonly Box Value;

    [EnforcePure]
    public Holder(Box value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public Holder {|SP0002:TestMethod|}()
    {
        var holder = new Holder(new Box());
        return holder;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshMutableObjectAliasEscapesThroughLocalImmutableWrapperConstructor_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Holder
{
    public readonly Box Value;

    [EnforcePure]
    public Holder(Box value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public Holder {|SP0002:TestMethod|}()
    {
        var box = new Box();
        var holder = new Holder(box);
        return holder;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task FreshMutableObjectEscapesThroughLocalInitOnlyWrapperInitializer_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Holder
{
    public Box Value { get; init; }
}

public class TestClass
{
    [EnforcePure]
    public Holder TestMethod()
    {
        var holder = new Holder { Value = new Box() };
        return holder;
    }
}";

        var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");
        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(17, 19, 17, 29)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetValue, expectedTestMethod);
    }

    [Test]
    public async Task FreshMutableObjectAliasEscapesThroughLocalInitOnlyWrapperInitializer_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Holder
{
    public Box Value { get; init; }
}

public class TestClass
{
    [EnforcePure]
    public Holder TestMethod()
    {
        var box = new Box();
        var holder = new Holder { Value = box };
        return holder;
    }
}";

        var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");
        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(17, 19, 17, 29)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetValue, expectedTestMethod);
    }

    [Test]
    public async Task FreshMutableObjectEscapesThroughDeepMixedConstructorWrappers_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Middle
{
    public Box Value { get; }

    [EnforcePure]
    public Middle(Box value)
    {
        Value = value;
    }
}

public sealed class Outer
{
    public readonly Middle Value;

    [EnforcePure]
    public Outer(Middle value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public Outer TestMethod()
    {
        return new Outer(new Middle(new Box()));
    }
}";

        var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");
        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(34, 18, 34, 28)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetValue, expectedTestMethod);
    }

    [Test]
    public async Task FreshMutableObjectEscapesThroughDeepInitOnlyWrapperChain_Diagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Middle
{
    public Box Value { get; init; }
}

public sealed class Outer
{
    public Middle Value { get; init; }
}

public class TestClass
{
    [EnforcePure]
    public Outer TestMethod()
    {
        return new Outer { Value = new Middle { Value = new Box() } };
    }
}";

        var expectedMiddleGetter = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");
        var expectedOuterGetter = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(16, 19, 16, 24)
            .WithArguments("get_Value");
        var expectedTestMethod = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0002).WithSpan(22, 18, 22, 28)
            .WithArguments("TestMethod");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedMiddleGetter, expectedOuterGetter, expectedTestMethod);
    }

    [Test]
    public async Task FreshMutableLocalObjectFieldMutation_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var box = new Box();
        box.Value = 1;
        return box.Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task OwnedFreshNestedMutableObjectFieldMutationThroughReadonlyWrapper_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Holder
{
    public readonly Box Value;

    [EnforcePure]
    public Holder(Box value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var holder = new Holder(new Box());
        holder.Value.Value = 1;
        return holder.Value.Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task OwnedFreshNestedMutableObjectFieldMutationThroughSourceFactoryReadonlyWrapper_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Holder
{
    public readonly Box Value;

    [EnforcePure]
    public Holder(Box value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var holder = CreateHolder();
        holder.Value.Value = 1;
        return holder.Value.Value;
    }

    private static Holder CreateHolder()
    {
        return new Holder(new Box());
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task OwnedFreshNestedMutableObjectFieldMutationThroughGetterWrapper_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Holder
{
    public Box Value { get; }

    [EnforcePure]
    public Holder(Box value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var holder = new Holder(new Box());
        holder.Value.Value = 1;
        return holder.Value.Value;
    }
}";

        var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetValue);
    }

    [Test]
    public async Task OwnedFreshDeepMutableObjectFieldMutationThroughSourceFactoryReadonlyWrapperChain_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Middle
{
    public readonly Box Value;

    [EnforcePure]
    public Middle(Box value)
    {
        Value = value;
    }
}

public sealed class Outer
{
    public readonly Middle Value;

    [EnforcePure]
    public Outer(Middle value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var outer = CreateOuter();
        outer.Value.Value.Value = 1;
        return outer.Value.Value.Value;
    }

    private static Outer CreateOuter()
    {
        return new Outer(new Middle(new Box()));
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }

    [Test]
    public async Task OwnedFreshDeepMutableObjectFieldMutationThroughMixedConstructorWrappers_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Middle
{
    public Box Value { get; }

    [EnforcePure]
    public Middle(Box value)
    {
        Value = value;
    }
}

public sealed class Outer
{
    public readonly Middle Value;

    [EnforcePure]
    public Outer(Middle value)
    {
        Value = value;
    }
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var outer = new Outer(new Middle(new Box()));
        outer.Value.Value.Value = 1;
        return outer.Value.Value.Value;
    }
}";

        var expectedGetValue = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedGetValue);
    }

    [Test]
    public async Task OwnedFreshDeepMutableObjectFieldMutationThroughInitOnlyWrapperChain_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Middle
{
    public Box Value { get; init; }
}

public sealed class Outer
{
    public Middle Value { get; init; }
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var outer = new Outer { Value = new Middle { Value = new Box() } };
        outer.Value.Value.Value = 1;
        return outer.Value.Value.Value;
    }
}";

        var expectedMiddleGetter = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");
        var expectedOuterGetter = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(16, 19, 16, 24)
            .WithArguments("get_Value");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedMiddleGetter, expectedOuterGetter);
    }

    [Test]
    public async Task OwnedFreshDeepMutableObjectFieldMutationThroughAliasedInitOnlyWrapperChain_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public sealed class Middle
{
    public Box Value { get; init; }
}

public sealed class Outer
{
    public Middle Value { get; init; }
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var middle = new Middle { Value = new Box() };
        var outer = new Outer { Value = middle };
        outer.Value.Value.Value = 1;
        return outer.Value.Value.Value;
    }
}";

        var expectedMiddleGetter = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(11, 16, 11, 21)
            .WithArguments("get_Value");
        var expectedOuterGetter = VerifyCS.Diagnostic(SharpProofAnalyzer.SP0004).WithSpan(16, 19, 16, 24)
            .WithArguments("get_Value");

        await VerifyCS.VerifyAnalyzerAsync(test, expectedMiddleGetter, expectedOuterGetter);
    }

    [Test]
    public async Task AliasedFreshMutableLocalObjectFieldMutation_NoDiagnostic()
    {
        var test = @"
using SharpProof.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var box = new Box();
        var alias = box;
        alias.Value = 1;
        return box.Value;
    }
}";

        await VerifyCS.VerifyAnalyzerAsync(test);
    }
}