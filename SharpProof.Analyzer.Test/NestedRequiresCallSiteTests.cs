using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class NestedRequiresCallSiteTests
{
    [Test]
    public async Task BlockAndExpressionBodiedLocalFunctionsAreAnalyzed()
    {
        var diagnostics = await Analyze(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    int Block() {
                        return Positive(-1);
                    }

                    int Expression() => Positive(-2);
                    int Safe() => Positive(1);
                    return Block() + Expression() +
                        Safe();
                }
            }
            """);

        AssertRequiresDiagnostics(diagnostics, 2);
    }

    [Test]
    public async Task LambdasAndAnonymousMethodsAreAnalyzed()
    {
        var diagnostics = await Analyze(
            """
            using System;
            using System.Linq.Expressions;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    Func<int> block = () => {
                        return Positive(-1);
                    };
                    Func<int> expression =
                        () => Positive(-2);
                    Func<int> anonymous = delegate {
                        return Positive(-3);
                    };
                    return block() + expression() +
                        anonymous();
                }
            }
            """);

        AssertRequiresDiagnostics(diagnostics, 3);
    }

    [Test]
    public async Task DeeplyNestedCallablesAreAnalyzedExactlyOnce()
    {
        var diagnostics = await Analyze(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    int First() {
                        int Second() =>
                            Positive(-1);
                        Func<int> lambda =
                            () => Positive(-2);
                        return Second() + lambda();
                    }

                    var first = First();
                    return first + First();
                }
            }
            """);

        AssertRequiresDiagnostics(diagnostics, 2);
    }

    [Test]
    public async Task LongDelegateAliasChainDoesNotOverflowAnalysis()
    {
        const int AliasCount = 8192;
        var aliases = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, AliasCount).Select(index =>
                $"                    Func<int> alias{index} = " +
                $"alias{index - 1};"));
        var source = $$"""
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    Func<int> alias0 = Reachable;
            {{aliases}}
                    return alias{{AliasCount}}();

                    int Reachable() => Positive(-1);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
    }

    [Test]
    public async Task RootAndNestedOutcomesRemainIndependent()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await Analyze(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    int Nested() => Positive(-2);
                    _ = Positive(-1);
                    return Nested();
                }
            }
            """,
            factory);

        AssertRequiresDiagnostics(diagnostics, 2);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.GetNamedOutcome("Outer"),
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.GetNamedOutcome("Nested"),
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Refuted));
        }
    }

    [Test]
    public async Task NestedFlowRefinesLocalsButNotCapturedValues()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await Analyze(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer(
                    int captured,
                    int value) {
                    int Proven() {
                        if (value <= 0) {
                            value = 1;
                        }

                        return Positive(value);
                    }

                    int Refuted() {
                        var local = value;
                        if (local > 0) {
                            local = -1;
                        }
                        else {
                            local = -2;
                        }

                        return Positive(local);
                    }

                    Func<int> unknown =
                        () => Positive(captured);
                    return Proven() + Refuted() +
                        unknown();
                }
            }
            """,
            factory);

        AssertRequiresDiagnostics(diagnostics, 1);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                factory.GetNamedOutcome("Proven"),
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Proven));
            Assert.That(
                factory.GetNamedOutcome("Refuted"),
                Is.EqualTo(
                    AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.GetOutcomes(
                    MethodKind.AnonymousFunction),
                Does.Contain(
                    AnalyzerSemanticOutcome.Unknown));
        }
    }

    [Test]
    public async Task NestedObjectCreationChecksConstructorPreconditions()
    {
        var diagnostics = await Analyze(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class PositiveValue {
                public PositiveValue(int value) {
                    Contract.Requires(value > 0);
                }
            }

            public static class Fixture {
                public static object Outer() {
                    object Local() =>
                        new PositiveValue(-1);
                    Func<object> lambda =
                        () => new PositiveValue(-2);
                    return new[] {
                        Local(),
                        lambda()
                    };
                }
            }
            """);

        AssertRequiresDiagnostics(diagnostics, 2);
    }

    [Test]
    public async Task ExpressionTreeBodiesAreNotTreatedAsExecutingDelegates()
    {
        var diagnostics = await Analyze(
            """
            using System;
            using System.Linq.Expressions;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static Expression<Func<int>> Quote() =>
                    () => Positive(-1);
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task LambdasInUnreachableBlocksAreNotAnalyzed()
    {
        var diagnostics = await Analyze(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    if (false) {
                        Func<int> unreachable =
                            () => Positive(-1);
                        return unreachable();
                    }

                    return 0;
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task LambdaBodiesRequireInvocationOrConservativeEscape()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                private static void Consume(Func<int> callback) { }

                public static Func<int> Escaped() =>
                    () => Positive(-4);

                public static int Outer(bool condition) {
                    Func<int> dead = () => Positive(-1);
                    Func<int> invoked = () => Positive(-2);
                    Func<int> conditional = () => Positive(-3);
                    Func<int> copied = invoked;
                    Consume(() => Positive(-5));

                    Func<int> deadOuter = () => {
                        Func<int> nested = () => Positive(-6);
                        return nested();
                    };
                    Func<int> liveOuter = () => {
                        Func<int> nested = () => Positive(-7);
                        return nested();
                    };

                    var result = copied() + liveOuter();
                    return condition
                        ? result + conditional()
                        : result;
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 5);
        Assert.That(
            diagnostics.Select(static diagnostic =>
                diagnostic.Location.SourceSpan.Start),
            Is.EqualTo(new[]
            {
                source.IndexOf("Positive(-4)", StringComparison.Ordinal),
                source.IndexOf("Positive(-2)", StringComparison.Ordinal),
                source.IndexOf("Positive(-3)", StringComparison.Ordinal),
                source.IndexOf("Positive(-5)", StringComparison.Ordinal),
                source.IndexOf("Positive(-7)", StringComparison.Ordinal)
            }.OrderBy(static position => position)));
    }

    [Test]
    public async Task UnreferencedLocalFunctionsAreNotAnalyzed()
    {
        const string source =
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    return Reachable();

                    int Dead() => Positive(-1);
                    int ThroughSibling() => Positive(-2);
                    int Reachable() => ThroughSibling();
                }
            }
            """;
        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
        Assert.That(
            diagnostics[0].Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-2)", StringComparison.Ordinal)));
    }

    [Test]
    public async Task DiscardedMethodGroupsAndLambdasDoNotReachNestedCallables()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    _ = (Func<int>)Dead;
                    _ = (Func<int>)(() => Positive(-2));
                    return 0;

                    int Dead() => Positive(-1);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task NameofReferencesDoNotReachLocalFunctions()
    {
        var diagnostics = await Analyze(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static string Outer() {
                    return nameof(Dead);

                    int Dead() => Positive(-1);
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task GenericAndMethodGroupReferencesReachLocalFunctions()
    {
        const string source =
            """
            using System;
            using System.Linq.Expressions;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    Expression<Func<int>> quoted = () => Dead();
                    Func<int> explicitReference = Reachable<int>;
                    Func<int> unusedReference = Unused;
                    return explicitReference() + Inferred(0);

                    int Dead() => Positive(-1);
                    int Reachable<T>() => Positive(-2);
                    int Inferred<T>(T value) => Positive(-3);
                    int Unused() => Positive(-4);
                }
            }
            """;
        var diagnostics = await Analyze(
            source,
            allowCompilationErrors: true);

        AssertRequiresDiagnostics(diagnostics, 2);
        Assert.That(
            diagnostics.Select(static diagnostic =>
                diagnostic.Location.SourceSpan.Start),
            Is.EqualTo(new[]
            {
                source.IndexOf("Positive(-2)", StringComparison.Ordinal),
                source.IndexOf("Positive(-3)", StringComparison.Ordinal)
            }));
    }

    [Test]
    public async Task OverwrittenMethodGroupsDoNotReachLocalFunctions()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer(bool condition) {
                    Func<int> overwritten = Dead;
                    if (condition) overwritten = () => 1;
                    else overwritten = () => 2;
                    Func<int> source = Reachable;
                    Func<int> alias = source;
                    source = () => 3;
                    return overwritten() + alias();

                    int Dead() => Positive(-1);
                    int Reachable() => Positive(-2);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
        Assert.That(
            diagnostics[0].Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-2)", StringComparison.Ordinal)));
    }

    [Test]
    public async Task AssignmentTargetEvaluationConsumesDelegates()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private sealed class Holder {
                    public int Value;
                }

                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                private static Holder Select(int value) => new();

                public static int Outer() {
                    Func<int> index = Index;
                    Func<int> receiver = Receiver;
                    var values = new int[1];
                    values[index()] = 1;
                    Select(receiver()).Value = 2;
                    return values[0];

                    int Index() => Positive(-1);
                    int Receiver() => Positive(-2);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 2);
        Assert.That(
            diagnostics.Select(static diagnostic =>
                diagnostic.Location.SourceSpan.Start),
            Is.EquivalentTo(new[]
            {
                source.IndexOf("Positive(-1)", StringComparison.Ordinal),
                source.IndexOf("Positive(-2)", StringComparison.Ordinal)
            }));
    }

    [Test]
    public async Task DelegateSubtractionDoesNotReachRemovedLocalFunctions()
    {
        var diagnostics = await Analyze(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    Func<int> callback = () => 1;
                    callback -= Removed;
                    return callback();

                    int Removed() => Positive(-1);
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ObservingMethodGroupsDoesNotReachLocalFunctions()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    Func<int> value = Dead;
                    return value == null ? 0 : 1;

                    int Dead() => Positive(-1);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 0);
    }

    [Test]
    public async Task OnlyExecutingOrEscapedDelegateUsesReachNestedCallables()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                private static void Replace(out Func<int> callback) =>
                    callback = () => 0;

                public static int MetadataOnly() {
                    Func<int> callback = Dead;
                    _ = callback.Method;
                    _ = callback.Target;
                    return 0;

                    int Dead() => Positive(-1);
                }

                public static int WriteOnlyOut() {
                    Func<int> callback = Dead;
                    Replace(out callback);
                    return callback();

                    int Dead() => Positive(-2);
                }

                public static int DiscardedCombination() {
                    Func<int> callback =
                        () => Positive(-3);
                    _ = callback + (Func<int>)(() => 0);
                    return 0;
                }

                public static int ConsumedCombination() {
                    Func<int> callback =
                        () => Positive(-4);
                    var combined =
                        callback + (Func<int>)(() => 0);
                    return combined();
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
        Assert.That(
            diagnostics[0].Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-4)", StringComparison.Ordinal)));
    }

    [Test]
    public async Task CoalesceAssignmentOnlyReachesLaterConsumedMethodGroups()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    Func<int>? unused = Dead;
                    unused ??= () => 1;
                    Func<int>? consumed = Reachable;
                    consumed ??= () => 2;
                    return consumed();

                    int Dead() => Positive(-1);
                    int Reachable() => Positive(-2);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
        Assert.That(
            diagnostics[0].Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-2)", StringComparison.Ordinal)));
    }

    [Test]
    public async Task TupleMethodGroupsOnlyReachThroughTheirOwnComponent()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    var unused = (Callback: (Func<int>)Dead, Number: 1);
                    _ = unused.Number;
                    var consumed =
                        (Callback: (Func<int>)Reachable, Number: 2);
                    return consumed.Callback();

                    int Dead() => Positive(-1);
                    int Reachable() => Positive(-2);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
        Assert.That(
            diagnostics[0].Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-2)", StringComparison.Ordinal)));
    }

    [Test]
    public async Task TupleItemAliasesReachNamedDelegateComponents()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    var unused = (
                        Callback: (Func<int>)Dead,
                        Number: 1);
                    _ = unused.Item2;
                    var consumed = (
                        Number: 2,
                        Callback: (Func<int>)Reachable);
                    return consumed.Item2();

                    int Dead() => Positive(-1);
                    int Reachable() => Positive(-2);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
        Assert.That(
            diagnostics[0].Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-2)", StringComparison.Ordinal)));
    }

    [Test]
    public async Task NestedTupleMethodGroupsTrackTheirFullProjectionPath()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    var unused = (Inner: (
                        Callback: (Func<int>)Dead,
                        Number: 1), Other: 0);
                    _ = unused.Inner.Number;
                    var consumed = (Inner: (
                        Callback: (Func<int>)Reachable,
                        Number: 2), Other: 0);
                    var inner = consumed.Inner;
                    return inner.Callback();

                    int Dead() => Positive(-1);
                    int Reachable() => Positive(-2);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
        Assert.That(
            diagnostics[0].Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-2)", StringComparison.Ordinal)));
    }

    [Test]
    public async Task NestedTupleProjectionAliasDoesNotConsumeSiblingDelegate()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    var outer = (Inner: (
                        Callback: (Func<int>)Dead,
                        Number: 1), Other: 0);
                    var inner = outer.Inner;
                    return inner.Number;

                    int Dead() => Positive(-1);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task TupleAssignmentsKillOnlyOverwrittenComponents()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int SiblingAssignment() {
                    var pair = (
                        Callback: (Func<int>)Reachable,
                        Number: 1);
                    pair.Number = 2;
                    return pair.Callback();

                    int Reachable() => Positive(-1);
                }

                public static int CallbackAssignment() {
                    var pair = (
                        Callback: (Func<int>)Dead,
                        Number: 1);
                    pair.Callback = Safe;
                    return pair.Callback();

                    int Dead() => Positive(-2);
                    int Safe() => 0;
                }

                public static int OuterAssignment() {
                    var outer = (Inner: (
                        Callback: (Func<int>)Dead,
                        Number: 1), Other: 0);
                    outer.Inner = (Safe, 2);
                    return outer.Inner.Callback();

                    int Dead() => Positive(-3);
                    int Safe() => 0;
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
        Assert.That(
            diagnostics[0].Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-1)", StringComparison.Ordinal)));
    }

    [Test]
    public async Task TupleComponentNullObservationsDoNotConsumeDelegates()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Equality() {
                    var pair = (
                        Callback: (Func<int>)Dead,
                        Number: 1);
                    return pair.Callback == null ? 0 : 1;

                    int Dead() => Positive(-1);
                }

                public static int Pattern() {
                    var outer = (Inner: (
                        Callback: (Func<int>)Dead,
                        Number: 1), Other: 0);
                    return outer.Inner.Callback is null ? 0 : 1;

                    int Dead() => Positive(-2);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task TupleDeconstructionTracksOnlyTheDelegateDestination()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Unused() {
                    var pair = (Callback: (Func<int>)Dead, Number: 1);
                    var (callback, number) = pair;
                    return number;

                    int Dead() => Positive(-1);
                }

                public static int Invoked() {
                    var pair = (Callback: (Func<int>)Reachable, Number: 1);
                    var (callback, number) = pair;
                    return callback();

                    int Reachable() => Positive(-2);
                }

                public static int Discarded() {
                    var pair = (Callback: (Func<int>)Dead, Number: 1);
                    var (_, number) = pair;
                    return number;

                    int Dead() => Positive(-3);
                }

                public static int NestedUnused() {
                    var outer = (Inner: (
                        Callback: (Func<int>)Dead,
                        Number: 1), Other: 0);
                    var (inner, other) = outer;
                    return inner.Number;

                    int Dead() => Positive(-4);
                }

                public static int NestedInvoked() {
                    var outer = (Inner: (
                        Callback: (Func<int>)Reachable,
                        Number: 1), Other: 0);
                    var (inner, other) = outer;
                    return inner.Callback();

                    int Reachable() => Positive(-5);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 2);
        Assert.That(
            diagnostics[0].Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-2)", StringComparison.Ordinal)));
        Assert.That(
            diagnostics[1].Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-5)", StringComparison.Ordinal)));
    }

    [Test]
    public async Task NullMethodGroupOverwriteKeepsOldDelegateReachableInCatch()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private sealed class Target {
                    public int Read() => 0;
                }

                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int CatchUse() {
                    Func<int> callback = Reachable;
                    Target target = null!;
                    try { callback = target.Read; }
                    catch (ArgumentException) { return callback(); }
                    return callback();

                    int Reachable() => Positive(-1);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
        Assert.That(
            diagnostics.Single().Location.SourceSpan.Start,
            Is.EqualTo(source.IndexOf(
                "Positive(-1)", StringComparison.Ordinal)));
    }

    [Test]
    public async Task ThrowingUserDefinedOperatorsKeepOldDelegateReachableInCatch()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                public struct Source {
                    public static implicit operator Func<int>(Source value) =>
                        throw new InvalidOperationException();

                    public static bool operator &(Source left, Source right) =>
                        throw new InvalidOperationException();

                    public static bool operator !(Source value) =>
                        throw new InvalidOperationException();

                    public static Source operator +(Source left, Source right) =>
                        throw new InvalidOperationException();

                    public static Source operator ++(Source value) =>
                        throw new InvalidOperationException();
                }

                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Conversion(Source source) {
                    Func<int> callback = Reachable;
                    try { callback = source; }
                    catch (InvalidOperationException) { return callback(); }
                    return 0;

                    int Reachable() => Positive(-1);
                }

                public static int Binary(Source source) {
                    Func<int> callback = Reachable;
                    Func<int> replacement = () => 0;
                    try {
                        callback = source & source
                            ? replacement
                            : replacement;
                    }
                    catch (InvalidOperationException) { return callback(); }
                    return 0;

                    int Reachable() => Positive(-2);
                }

                public static int Unary(Source source) {
                    Func<int> callback = Reachable;
                    Func<int> replacement = () => 0;
                    try {
                        callback = !source
                            ? replacement
                            : replacement;
                    }
                    catch (InvalidOperationException) { return callback(); }
                    return 0;

                    int Reachable() => Positive(-3);
                }

                public static int Compound(Source source) {
                    Func<int> callback = Reachable;
                    Func<int> replacement = () => 0;
                    try {
                        source += source;
                        callback = replacement;
                    }
                    catch (InvalidOperationException) { return callback(); }
                    return 0;

                    int Reachable() => Positive(-4);
                }

                public static int Increment(Source source) {
                    Func<int> callback = Reachable;
                    Func<int> replacement = () => 0;
                    try {
                        source++;
                        callback = replacement;
                    }
                    catch (InvalidOperationException) { return callback(); }
                    return 0;

                    int Reachable() => Positive(-5);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 5);
        Assert.That(
            diagnostics.Select(diagnostic =>
                diagnostic.Location.SourceSpan.Start),
            Is.EquivalentTo(new[] {
                source.IndexOf("Positive(-1)", StringComparison.Ordinal),
                source.IndexOf("Positive(-2)", StringComparison.Ordinal),
                source.IndexOf("Positive(-3)", StringComparison.Ordinal),
                source.IndexOf("Positive(-4)", StringComparison.Ordinal),
                source.IndexOf("Positive(-5)", StringComparison.Ordinal)
            }));
    }

    [Test]
    public async Task ExceptionHandlersCanConsumeTrackedDelegates()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private sealed class Holder {
                    public Func<int> Callback = null!;
                }
                private sealed class Boom {
                    public Boom(int value) =>
                        throw new InvalidOperationException();
                }
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
                private static void Fail() =>
                    throw new InvalidOperationException();
                private static Func<int> FailDelegate() =>
                    throw new InvalidOperationException();

                public static int CatchUse() {
                    Func<int> callback = Reachable;
                    try { Fail(); }
                    catch { return callback(); }

                    int Reachable() => Positive(-1);
                }

                public static int FinallyUse() {
                    Func<int> callback = Reachable;
                    try { Fail(); }
                    finally { _ = callback(); }

                    int Reachable() => Positive(-2);
                }

                public static int OverwrittenBeforeThrow() {
                    Func<int> callback = Dead;
                    callback = Safe;
                    try { Fail(); }
                    catch { return callback(); }

                    int Dead() => Positive(-3);
                    int Safe() => 0;
                }

                public static int ThrowingOverwrite() {
                    Func<int> callback = Reachable;
                    try { callback = FailDelegate(); }
                    catch { return callback(); }

                    int Reachable() => Positive(-4);
                }

                public static int ThrowingTupleOverwrite() {
                    (Func<int> callback, int other) pair = (Reachable, 0);
                    try { pair.callback = FailDelegate(); }
                    catch { return pair.callback(); }

                    int Reachable() => Positive(-5);
                }

                public static int ThrowingFieldOverwrite() {
                    Holder holder = null!;
                    Func<int> callback = Reachable;
                    try { callback = holder.Callback; }
                    catch { return callback(); }

                    int Reachable() => Positive(-6);
                }

                public static int ThrowingTupleFieldOverwrite() {
                    Holder holder = null!;
                    (Func<int> callback, int other) pair = (Reachable, 0);
                    try { pair.callback = holder.Callback; }
                    catch { return pair.callback(); }

                    int Reachable() => Positive(-7);
                }

                public static int ThrowingCastOverwrite(object source) {
                    Func<int> callback = Reachable;
                    try { callback = (Func<int>)source; }
                    catch { return callback(); }

                    int Reachable() => Positive(-8);
                }

                public static int ThrowingTupleCastOverwrite(object source) {
                    (Func<int> callback, int other) pair = (Reachable, 0);
                    try { pair.callback = (Func<int>)source; }
                    catch { return pair.callback(); }

                    int Reachable() => Positive(-9);
                }

                public static int ThrowingArrayLengthOverwrite() {
                    Func<int>[] values = null!;
                    Func<int> callback = Reachable;
                    try { callback = values.Length == 0 ? Safe : Safe; }
                    catch { return callback(); }

                    int Reachable() => Positive(-10);
                    int Safe() => 0;
                }

                public static int ThrowingTupleArrayLengthOverwrite() {
                    Func<int>[] values = null!;
                    (Func<int> callback, int other) pair = (Reachable, 0);
                    try { pair.callback = values.Length == 0 ? Safe : Safe; }
                    catch { return pair.callback(); }

                    int Reachable() => Positive(-11);
                    int Safe() => 0;
                }

                public static int ThrowingCheckedOverwrite(int value) {
                    Func<int> callback = Reachable;
                    try {
                        callback = checked(int.MaxValue + value) > 0
                            ? Safe
                            : Safe;
                    }
                    catch { return callback(); }

                    int Reachable() => Positive(-12);
                    int Safe() => 0;
                }

                public static int ThrowingTupleCheckedOverwrite(int value) {
                    (Func<int> callback, int other) pair = (Reachable, 0);
                    try {
                        pair.callback = checked(int.MaxValue + value) > 0
                            ? Safe
                            : Safe;
                    }
                    catch { return pair.callback(); }

                    int Reachable() => Positive(-13);
                    int Safe() => 0;
                }

                public static int ThrowingDynamicCreationOverwrite(
                    dynamic value) {
                    Func<int> callback = Reachable;
                    try {
                        callback = new Boom(value) != null ? Safe : Safe;
                    }
                    catch { return callback(); }

                    int Reachable() => Positive(-14);
                    int Safe() => 0;
                }

                public static int ThrowingTupleDynamicCreationOverwrite(
                    dynamic value) {
                    (Func<int> callback, int other) pair = (Reachable, 0);
                    try {
                        pair.callback = new Boom(value) != null ? Safe : Safe;
                    }
                    catch { return pair.callback(); }

                    int Reachable() => Positive(-15);
                    int Safe() => 0;
                }
            }
            """;

        var diagnostics = await Analyze(
            source,
            allowCompilationErrors: true);

        AssertRequiresDiagnostics(diagnostics, 14);
        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan.Start),
            Is.EquivalentTo(new[] {
                source.IndexOf("Positive(-1)", StringComparison.Ordinal),
                source.IndexOf("Positive(-2)", StringComparison.Ordinal),
                source.IndexOf("Positive(-4)", StringComparison.Ordinal),
                source.IndexOf("Positive(-5)", StringComparison.Ordinal),
                source.IndexOf("Positive(-6)", StringComparison.Ordinal),
                source.IndexOf("Positive(-7)", StringComparison.Ordinal),
                source.IndexOf("Positive(-8)", StringComparison.Ordinal),
                source.IndexOf("Positive(-9)", StringComparison.Ordinal),
                source.IndexOf("Positive(-10)", StringComparison.Ordinal),
                source.IndexOf("Positive(-11)", StringComparison.Ordinal),
                source.IndexOf("Positive(-12)", StringComparison.Ordinal),
                source.IndexOf("Positive(-13)", StringComparison.Ordinal),
                source.IndexOf("Positive(-14)", StringComparison.Ordinal),
                source.IndexOf("Positive(-15)", StringComparison.Ordinal)
            }));
    }

    [Test]
    public async Task NormalFinallyFlowCanConsumeTrackedDelegates()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int FinallyUse() {
                    Func<int> callback = Reachable;
                    try { _ = 0; }
                    finally { _ = callback(); }
                    return 0;

                    int Reachable() => Positive(-1);
                }

                public static int FinallyReturn() {
                    Func<int> callback = Reachable;
                    try { return 0; }
                    finally { _ = callback(); }

                    int Reachable() => Positive(-2);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 2);
        Assert.That(
            diagnostics.Select(static diagnostic =>
                diagnostic.Location.SourceSpan.Start),
            Is.EquivalentTo(new[]
            {
                source.IndexOf("Positive(-1)", StringComparison.Ordinal),
                source.IndexOf("Positive(-2)", StringComparison.Ordinal)
            }));
    }

    [Test]
    public async Task PatternAliasesReachLocalFunctions()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    Func<int> value = Reachable;
                    if (value is (var alias)) return alias();
                    return 0;

                    int Reachable() => Positive(-1);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 1);
    }

    [Test]
    public async Task NestedPatternAliasesReachOnlyTheirDelegateComponents()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Recursive() {
                    var pair = (
                        Callback: (Func<int>)Reachable,
                        Number: 1);
                    if (pair is (var callback, _)) return callback();
                    return 0;

                    int Reachable() => Positive(-1);
                }

                public static int List() {
                    Func<int>[] callbacks = { Reachable };
                    if (callbacks is [var callback]) return callback();
                    return 0;

                    int Reachable() => Positive(-2);
                }

                public static int RecursiveSibling() {
                    var pair = (
                        Dead: (Func<int>)Dead,
                        Used: (Func<int>)Safe);
                    if (pair is (var dead, var used)) return used();
                    return 0;

                    int Dead() => Positive(-3);
                    int Safe() => 0;
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRequiresDiagnostics(diagnostics, 2);
        Assert.That(
            diagnostics.Select(static diagnostic =>
                diagnostic.Location.SourceSpan.Start),
            Is.EquivalentTo(new[]
            {
                source.IndexOf("Positive(-1)", StringComparison.Ordinal),
                source.IndexOf("Positive(-2)", StringComparison.Ordinal)
            }));
    }

    [Test]
    public async Task NestedCallableSuppressionsAreValidatedAndRecorded()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await Analyze(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    [SharpProofSuppress("reviewed local")]
                    int Local() => Positive(-1);
                    Func<int> lambda =
                        [SharpProofSuppress("reviewed lambda")]
                        () => Positive(-2);
                    return Local() + lambda();
                }
            }
            """,
            factory,
            ["SP0024", "SP0027"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(
                factory.GetNamedOutcome("Local"),
                Is.EqualTo(AnalyzerSemanticOutcome.Suppressed));
            Assert.That(
                factory.GetOutcomes(MethodKind.AnonymousFunction),
                Does.Contain(AnalyzerSemanticOutcome.Suppressed));
        }
    }

    [Test]
    public async Task InvalidNestedSuppressionReasonsDoNotSuppressAnalysis()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await Analyze(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    [SharpProofSuppress("")]
                    int Local() => Positive(-1);
                    Func<int> lambda =
                        [SharpProofSuppress(" ")]
                        () => Positive(-2);
                    return Local() + lambda();
                }
            }
            """,
            factory,
            ["SP0024", "SP0027"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EquivalentTo(["SP0024", "SP0024", "SP0027", "SP0027"]));
            Assert.That(
                factory.GetNamedOutcome("Local"),
                Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
            Assert.That(
                factory.GetOutcomes(MethodKind.AnonymousFunction),
                Does.Contain(AnalyzerSemanticOutcome.Refuted));
        }
    }

    [Test]
    public async Task NestedCallableControlReasonsAreValidatedRegardlessOfReachability()
    {
        const string source =
            """
            using System;
            using System.Linq.Expressions;
            using SharpProof.Attributes;

            public static class Fixture {
                public static int Outer() {
                    [SharpProofSuppress("")]
                    int UnusedLocal() => 0;

                    [SharpProofTrusted(" ")]
                    int UsedLocal() => 1;

                    [SharpProofTrusted("")]
                    int EscapedLocal() => 2;
                    Func<int> escaped = EscapedLocal;

                    Func<int> unusedExpression =
                        [SharpProofSuppress("")]
                        () => 3;
                    Func<int> usedBlock =
                        [SharpProofTrusted(" ")]
                        () => { return 4; };
                    Func<int> unusedAnonymous =
                        delegate { return 5; };
                    Expression<Func<int>> quoted =
                        [SharpProofSuppress(" ")]
                        () => 6;

                    [SharpProofSuppress("reviewed outer")]
                    int ValidNested() {
                        Func<int> nested =
                            [SharpProofTrusted("reviewed nested")]
                            () => 5;
                        return nested();
                    }

                    return UsedLocal() + escaped() + usedBlock() +
                        ValidNested();
                }
            }
            """;

        var diagnostics = await Analyze(
            source,
            enabledIds: ["SP0024"],
            allowCompilationErrors: true);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0024", 6);
        Assert.That(
            diagnostics.Select(static diagnostic =>
                diagnostic.Location.SourceSpan.Start),
            Is.EqualTo(new[]
            {
                source.IndexOf("SharpProofSuppress(\"\")", StringComparison.Ordinal),
                source.IndexOf("SharpProofTrusted(\" \")", StringComparison.Ordinal),
                source.IndexOf("SharpProofTrusted(\"\")", StringComparison.Ordinal),
                source.IndexOf(
                    "SharpProofSuppress(\"\")",
                    source.IndexOf("unusedExpression", StringComparison.Ordinal),
                    StringComparison.Ordinal),
                source.IndexOf(
                    "SharpProofTrusted(\" \")",
                    source.IndexOf("usedBlock", StringComparison.Ordinal),
                    StringComparison.Ordinal),
                source.IndexOf(
                    "SharpProofSuppress(\" \")",
                    source.IndexOf("quoted", StringComparison.Ordinal),
                    StringComparison.Ordinal)
            }));
    }

    [Test]
    public async Task GeneratedNestedCallableControlReasonsRemainExcluded()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static void Outer() {
                    [SharpProofSuppress("")]
                    void Local() { }
                    Func<int> lambda =
                        [SharpProofTrusted(" ")]
                        () => Positive(-1);
                    _ = lambda();
                }
            }
            """,
            "contracts",
            ["SP0024", "SP0027"],
            filePath: "Fixture.g.cs");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task GeneratedNestedCallablesInHandwrittenCodeAreExcluded()
    {
        var factory = new RecordingSessionFactory();
        var diagnostics = await Analyze(
            """
            using System;
            using System.CodeDom.Compiler;
            using System.Linq.Expressions;
            using SharpProof.Attributes;

            public static class Fixture {
                private static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Outer() {
                    [GeneratedCode("test", "1")]
                    int Local() {
                        int Inner() => Positive(-1);
                        return Inner();
                    }
                    Func<int> lambda =
                        [GeneratedCode("test", "1")]
                        () => Positive(-2);
                    Expression<Func<int>> expression =
                        [GeneratedCode("test", "1")]
                        () => Positive(-3);
                    return Local() + lambda() + expression.Compile()();
                }
            }
            """,
            factory,
            allowCompilationErrors: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(
                factory.GetNamedOutcome("Inner"),
                Is.EqualTo(AnalyzerSemanticOutcome.NotApplicable));
            Assert.That(
                factory.GetOutcomes(MethodKind.AnonymousFunction),
                Is.EqualTo(Enumerable.Repeat(
                    AnalyzerSemanticOutcome.NotApplicable,
                    2)));
        }
    }

    private static Task<ImmutableArray<Diagnostic>> Analyze(
        string source,
        IAnalyzerSessionFactory? sessionFactory = null,
        string[]? enabledIds = null,
        bool allowCompilationErrors = false)
    {
        return AnalyzerTestHost.AnalyzeAsync(
            source,
            "contracts",
            enabledIds ?? ["SP0027"],
            sessionFactory == null
                ? null
                : new SharpProofAnalyzer(
                    sessionFactory),
            allowCompilationErrors: allowCompilationErrors);
    }

    private static void AssertRequiresDiagnostics(
        ImmutableArray<Diagnostic> diagnostics,
        int count)
    {
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(
                Enumerable.Repeat("SP0027", count)),
            string.Join(", ", diagnostics.Select(static diagnostic =>
                diagnostic.Location.SourceSpan.Start)));
    }

    private sealed class RecordingSessionFactory :
        IAnalyzerSessionFactory
    {
        private readonly ConcurrentDictionary<
            MethodIdentity,
            AnalyzerSemanticOutcome> _outcomes =
                new();

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken)
        {
            return new AnalyzerSession(
                compilation,
                configuration,
                cancellationToken,
                (method, outcome) =>
                    _outcomes.AddOrUpdate(
                        MethodIdentity.Create(method),
                        outcome,
                        (_, current) =>
                            AnalyzerSemanticOutcomes
                                .Combine(
                                    current,
                                    outcome)));
        }

        internal AnalyzerSemanticOutcome
            GetNamedOutcome(string name)
        {
            return _outcomes.Single(pair =>
                    string.Equals(
                        pair.Key.Name,
                        name,
                        StringComparison.Ordinal))
                .Value;
        }

        internal ImmutableArray<
            AnalyzerSemanticOutcome> GetOutcomes(
                MethodKind kind)
        {
            return [
                .. _outcomes
                    .Where(pair =>
                        pair.Key.Kind == kind)
                    .Select(static pair =>
                        pair.Value)
            ];
        }
    }

    private readonly record struct MethodIdentity(
        MethodKind Kind,
        string Name,
        int SpanStart)
    {
        internal static MethodIdentity Create(
            IMethodSymbol method)
        {
            return new(
                method.MethodKind,
                method.Name,
                method.DeclaringSyntaxReferences
                    .FirstOrDefault()?.Span.Start ??
                    -1);
        }
    }
}
