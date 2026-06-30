using System.Threading.Tasks;
using NUnit.Framework;
using VerifyCS = PurelySharp.Test.CSharpAnalyzerVerifier<
    PurelySharp.Analyzer.PurelySharpAnalyzer>;

namespace PurelySharp.Test
{
    [TestFixture]
    public sealed class PatternSmtInvariantTests
    {
        [Test]
        public async Task NegatedDeclarationPatternEarlyExit_FeedsBindingFacts()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(object value)
    {
        if (value is not string text)
        {
            return;
        }

        if (text == null)
        {
            Console.WriteLine(text);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task DeclarationPatternBindingReassignedBeforeGuard_RemainsReachable()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(object value)
    {
        if (value is string text)
        {
            text = null;
            if (text == null)
            {
                Console.WriteLine(text);
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task RecursivePropertyPatternBindingReassignedBeforeGuard_RemainsReachable()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Box
{
    [Pure]
    public string Value { get; init; }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(Box box)
    {
        if (box is { Value: { } text })
        {
            text = null;
            if (text == null)
            {
                Console.WriteLine(text);
            }
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementPatternBindingReassignedBeforeGuard_RemainsReachable()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(object value)
    {
        switch (value)
        {
            case string text:
                text = null;
                if (text == null)
                {
                    Console.WriteLine(text);
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementTypePatternWithoutBinding_FeedsNonNullFact()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(object value)
    {
        switch (value)
        {
            case string _:
                if (value == null)
                {
                    Console.WriteLine(value);
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementCustomListPatternWithCount_FeedsLengthFact()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    [Pure]
    public int Count { get; }

    [Pure]
    public int this[int index] => index;

    [Pure]
    public Bag this[Range range] => this;
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Bag bag)
    {
        switch (bag)
        {
            case [_]:
                if (bag.Count != 1)
                {
                    Console.WriteLine(bag.Count);
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementCustomListPatternWithSlice_FeedsMinimumLengthFact()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    [Pure]
    public int Count { get; }

    [Pure]
    public int this[int index] => index;

    [Pure]
    public Bag this[Range range] => this;
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Bag bag)
    {
        switch (bag)
        {
            case [_, ..]:
                if (bag.Count < 1)
                {
                    Console.WriteLine(bag.Count);
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task NegatedCustomListPatternEarlyExit_FeedsLengthFact()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    [Pure]
    public int Count { get; }

    [Pure]
    public int this[int index] => index;
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Bag bag)
    {
        if (bag is not [_])
        {
            return;
        }

        if (bag.Count != 1)
        {
            Console.WriteLine(bag.Count);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task NegatedCustomListPatternWithElementConstraint_RemainsReachable()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    [Pure]
    public int Count { get; }

    [Pure]
    public int this[int index] => index;
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|PS0002:TestMethod|}(Bag bag)
    {
        if (bag is not [1] && bag.Count == 1)
        {
            Console.WriteLine(bag.Count);
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task NestedSwitchStatementCustomListPatternElementContradiction_PrunesSection()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    [Pure]
    public int Count { get; }

    [Pure]
    public int this[int index] => index;

    [Pure]
    public Bag this[Range range] => this;
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Bag bag)
    {
        switch (bag)
        {
            case [> 0, ..]:
                switch (bag)
                {
                    case [< 0, ..]:
                        Console.WriteLine(bag.Count);
                        break;
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task NestedSwitchExpressionCustomListPatternTrailingElementContradiction_PrunesArm()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    [Pure]
    public int Count { get; }

    [Pure]
    public int this[int index] => index;

    [Pure]
    public Bag this[Range range] => this;
}

public sealed class TestClass
{
    [EnforcePure]
    public string TestMethod(Bag bag)
    {
        switch (bag)
        {
            case [.., > 0]:
                return bag switch
                {
                    [.., < 0] => Console.ReadLine(),
                    _ => string.Empty
                };
            default:
                return string.Empty;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementRelationalPropertyConjunction_PrunesContradictoryGuard()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Box
{
    [Pure]
    public int Count { get; init; }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Box box)
    {
        switch (box)
        {
            case { Count: > 0 and < 10 }:
                if (box.Count <= 0 || box.Count >= 10)
                {
                    Console.WriteLine(box.Count);
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementExtendedPropertyPattern_FeedsIntermediateNonNullFact()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Child
{
    [Pure]
    public int Value { get; init; }
}

public sealed class Box
{
    [Pure]
    public Child Child { get; init; }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Box box)
    {
        switch (box)
        {
            case { Child.Value: > 0 }:
                if (box.Child == null)
                {
                    Console.WriteLine(box);
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementDefaultExcludesCustomListPatternLength()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    [Pure]
    public int Count { get; }

    [Pure]
    public int this[int index] => index;

    [Pure]
    public Bag this[Range range] => this;
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Bag bag)
    {
        switch (bag)
        {
            case [_]:
                return;
            default:
                if (bag != null && bag.Count == 1)
                {
                    Console.WriteLine(bag.Count);
                }

                return;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementPriorCustomListPatternWithGuardExcludesLaterSection()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    [Pure]
    public int Count { get; }

    [Pure]
    public int this[int index] => index;
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Bag bag)
    {
        switch (bag)
        {
            case [_, ..] when bag.Count >= 1:
                return;
            case [_]:
                Console.WriteLine(bag.Count);
                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementDefaultExcludesNestedCustomListPropertyPattern()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    [Pure]
    public int Count { get; }

    [Pure]
    public int this[int index] => index;
}

public sealed class Box
{
    [Pure]
    public Bag Items { get; init; }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Box box)
    {
        switch (box)
        {
            case { Items: [_] }:
                return;
            default:
                if (box != null && box.Items != null && box.Items.Count == 1)
                {
                    Console.WriteLine(box.Items.Count);
                }

                return;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementNestedSliceExactLength_PrunesContradictoryLengthGuard()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(int[][] values)
    {
        switch (values)
        {
            case [_, .. [_, _], _]:
                if (values.Length != 4)
                {
                    Console.WriteLine(values.Length);
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementDefaultExcludesNestedSliceExactLength()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(int[][] values)
    {
        switch (values)
        {
            case [_, .. [_, _], _]:
                return;
            default:
                if (values != null && values.Length == 4)
                {
                    Console.WriteLine(values.Length);
                }

                return;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementNestedSlicePrefixElementFact_PrunesContradictoryGuard()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        switch (values)
        {
            case [_, .. [> 0, ..], _]:
                if (values[1] <= 0)
                {
                    Console.WriteLine(values[1]);
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementNestedSliceSuffixElementFact_PrunesContradictoryGuard()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        switch (values)
        {
            case [_, .. [.., > 0], _]:
                if (values[^2] <= 0)
                {
                    Console.WriteLine(values[^2]);
                }

                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Test]
        public async Task SwitchStatementPriorNestedSlicePatternExcludesLaterArm()
        {
            var test = @"
using System;
using PurelySharp.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        switch (values)
        {
            case [_, .. [> 0], _]:
                return;
            case [_, _, _] when values[1] > 0:
                Console.WriteLine(values[1]);
                break;
        }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
