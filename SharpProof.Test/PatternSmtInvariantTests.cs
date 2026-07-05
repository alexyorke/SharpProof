using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class PatternSmtInvariantTests
    {
        [Test]
        public async Task NegatedDeclarationPatternEarlyExit_FeedsBindingFacts()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task DeclarationPatternBindingReassignedBeforeGuard_RemainsReachable()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(object value)
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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task RecursivePropertyPatternBindingReassignedBeforeGuard_RemainsReachable()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class Box
{
    [Pure]
    public string Value { get; init; }
}

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(Box box)
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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementPatternBindingReassignedBeforeGuard_RemainsReachable()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void {|SP0002:TestMethod|}(object value)
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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementTypePatternWithoutBinding_FeedsNonNullFact()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task TypeTestTrueBranch_FeedsNonNullAndTypeFacts()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(object value)
    {
        if (value is string)
        {
            if (value == null || value is not string)
            {
                Console.WriteLine(value);
            }
        }
    }
}";

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task TypeTestTrueBranch_ProvesNegatedTypeAndNonNullConjunctionUnreachable()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(object value)
    {
        if (value is string)
        {
            if (value is not string && value is not null)
            {
                Console.WriteLine(value);
            }
        }
    }
}";

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementDefault_AfterStringPatternExcludesStringType()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(object value)
    {
        switch (value)
        {
            case string:
                break;
            default:
                if (value is string)
                {
                    Console.WriteLine(value);
                }

                break;
        }
    }
}";

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementCustomListPatternWithCount_FeedsLengthFact()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementCustomListPatternWithSlice_FeedsMinimumLengthFact()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task NegatedCustomListPatternEarlyExit_FeedsLengthFact()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task NegatedCustomListPatternWithElementConstraint_RemainsReachable()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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
    public void {|SP0002:TestMethod|}(Bag bag)
    {
        if (bag is not [1] && bag.Count == 1)
        {
            Console.WriteLine(bag.Count);
        }
    }
}";

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task NestedSwitchStatementCustomListPatternElementContradiction_PrunesSection()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task NestedSwitchExpressionCustomListPatternTrailingElementContradiction_PrunesArm()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementRelationalPropertyConjunction_PrunesContradictoryGuard()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task ConditionalReceiverPropertyPattern_FeedsSelectedArmFacts()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class Box
{
    [Pure]
    public int Count { get; init; }
}

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(Box left, Box right, bool flag)
    {
        if ((flag ? left : right) is { Count: > 0 })
        {
            if (flag && left.Count <= 0)
            {
                Console.WriteLine(left.Count);
            }

            if (!flag && right.Count <= 0)
            {
                Console.WriteLine(right.Count);
            }
        }
    }
}";

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementExtendedPropertyPattern_FeedsIntermediateNonNullFact()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementDefaultExcludesCustomListPatternLength()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementPriorCustomListPatternWithGuardExcludesLaterSection()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementDefaultExcludesNestedCustomListPropertyPattern()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementNestedSliceExactLength_PrunesContradictoryLengthGuard()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementDefaultExcludesNestedSliceExactLength()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementNestedSlicePrefixElementFact_PrunesContradictoryGuard()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementNestedSliceSuffixElementFact_PrunesContradictoryGuard()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementPriorNestedSlicePatternExcludesLaterArm()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementPriorPropertyBindingGuardExcludesLaterSection()
        {
            var test = @"
using System;
using SharpProof.Attributes;

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
            case { Count: var count } when count > 0:
                return;
            case { Count: > 0 }:
                Console.WriteLine(box.Count);
                break;
        }
    }
}";

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchExpressionPriorListBindingGuardExcludesLaterArm()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public string TestMethod(int[] values)
    {
        return values switch
        {
            [var first, ..] when first > 0 => string.Empty,
            [> 0, ..] => Console.ReadLine(),
            _ => string.Empty
        };
    }
}";

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchStatementGuardContradictsTrackedAssignment_PrunesSection()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void TestMethod(int[] values)
    {
        var gate = 0;
        switch (values)
        {
            case [_, ..] when gate != 0:
                Console.WriteLine(gate);
                break;
        }
    }
}";

            await AssertPatternDiagnosticsAsync(test);
        }

        [Test]
        public async Task SwitchExpressionGuardContradictsTrackedAssignment_PrunesArm()
        {
            var test = @"
using System;
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public string TestMethod(int[] values)
    {
        var gate = 0;
        return values switch
        {
            [_, ..] when gate != 0 => Console.ReadLine(),
            _ => string.Empty
        };
    }
}";

            await AssertPatternDiagnosticsAsync(test);
        }

        private static async Task AssertPatternDiagnosticsAsync(string markedSource)
        {
            var (source, expectedSpanText) = StripSp0002Markup(markedSource);
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
                .ToArray();

            if (expectedSpanText == null)
            {
                Assert.That(purityDiagnostics, Is.Empty);
                Assert.That(diagnostics, Is.Empty);
                return;
            }

            Assert.That(purityDiagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics, Has.Length.EqualTo(1));

            var diagnostic = purityDiagnostics[0];
            var actualSpanText = source.Substring(
                diagnostic.Location.SourceSpan.Start,
                diagnostic.Location.SourceSpan.Length);
            Assert.That(actualSpanText, Is.EqualTo(expectedSpanText));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        private static (string Source, string? ExpectedSpanText) StripSp0002Markup(string markedSource)
        {
            const string prefix = "{|SP0002:";
            const string suffix = "|}";
            var start = markedSource.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0)
            {
                return (markedSource, null);
            }

            var contentStart = start + prefix.Length;
            var end = markedSource.IndexOf(suffix, contentStart, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThanOrEqualTo(0), "Expected SP0002 markup end.");

            var expectedSpanText = markedSource.Substring(contentStart, end - contentStart);
            var source = markedSource.Remove(end, suffix.Length).Remove(start, prefix.Length);
            return (source, expectedSpanText);
        }
    }
}
