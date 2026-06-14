using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public class ObjectInitializerTests
    {
        [Test]
        public async Task ObjectInitializerWithImpureSetter_Diagnostic()
        {
            var testCode = @"
using System;
using PurelySharp.Attributes;

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
    public Target {|PS0002:Create|}()
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
using PurelySharp.Attributes;

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
    public Holder {|PS0002:Create|}()
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
using PurelySharp.Attributes;

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
    public Target {|PS0002:Create|}()
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
using PurelySharp.Attributes;

public sealed class Holder
{
    public int[] Values;
}

public class TestClass
{
    [EnforcePure]
    public Holder {|PS0002:Create|}()
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
using PurelySharp.Attributes;

public sealed record Holder(int[] Values);

public class TestClass
{
    [EnforcePure]
    public Holder {|PS0002:Create|}()
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
using PurelySharp.Attributes;

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
    public Box {|PS0002:TestMethod|}()
    {
        var items = new int[1];
        items[0] = 42;
        return new Box(items);
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FreshMutableObjectReturned_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public Box {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

public sealed class Box
{
    public int Value;
}

public class TestClass
{
    [EnforcePure]
    public Box {|PS0002:TestMethod|}()
    {
        var box = new Box();
        return box;
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task FreshMutableObjectEscapesThroughImmutableWrapperConstructor_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

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

            var expectedGetValue = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(11, 16, 11, 21).WithArguments("get_Value");
            var expectedTestMethod = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(23, 19, 23, 29).WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedGetValue, expectedTestMethod });
        }

        [Test]
        public async Task FreshMutableObjectAliasEscapesThroughImmutableWrapperConstructor_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

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

            var expectedGetValue = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(11, 16, 11, 21).WithArguments("get_Value");
            var expectedTestMethod = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(23, 19, 23, 29).WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedGetValue, expectedTestMethod });
        }

        [Test]
        public async Task FreshMutableObjectEscapesThroughLocalImmutableWrapperConstructor_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

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
    public Holder {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

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
    public Holder {|PS0002:TestMethod|}()
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
using PurelySharp.Attributes;

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

            var expectedGetValue = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(11, 16, 11, 21).WithArguments("get_Value");
            var expectedTestMethod = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(17, 19, 17, 29).WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedGetValue, expectedTestMethod });
        }

        [Test]
        public async Task FreshMutableObjectAliasEscapesThroughLocalInitOnlyWrapperInitializer_Diagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

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

            var expectedGetValue = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(11, 16, 11, 21).WithArguments("get_Value");
            var expectedTestMethod = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0002).WithSpan(17, 19, 17, 29).WithArguments("TestMethod");

            await VerifyCS.VerifyAnalyzerAsync(test, new[] { expectedGetValue, expectedTestMethod });
        }

        [Test]
        public async Task FreshMutableLocalObjectFieldMutation_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

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
using PurelySharp.Attributes;

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
        public async Task OwnedFreshNestedMutableObjectFieldMutationThroughGetterWrapper_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

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

            var expectedGetValue = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(11, 16, 11, 21).WithArguments("get_Value");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedGetValue);
        }

        [Test]
        public async Task OwnedFreshDeepMutableObjectFieldMutationThroughMixedConstructorWrappers_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

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

            var expectedGetValue = VerifyCS.Diagnostic(PurelySharpAnalyzer.PS0004).WithSpan(11, 16, 11, 21).WithArguments("get_Value");

            await VerifyCS.VerifyAnalyzerAsync(test, expectedGetValue);
        }

        [Test]
        public async Task AliasedFreshMutableLocalObjectFieldMutation_NoDiagnostic()
        {
            var test = @"
using PurelySharp.Attributes;

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
}
