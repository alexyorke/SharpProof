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
                    return explicitReference() + Inferred(0);

                    int Dead() => Positive(-1);
                    int Reachable<T>() => Positive(-2);
                    int Inferred<T>(T value) => Positive(-3);
                }
            }
            """;
        var diagnostics = await Analyze(source);

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
            enabledIds: ["SP0024"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0024", 6)));
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

    private static Task<ImmutableArray<Diagnostic>> Analyze(
        string source,
        IAnalyzerSessionFactory? sessionFactory = null,
        string[]? enabledIds = null)
    {
        return AnalyzerTestHost.AnalyzeAsync(
            source,
            "contracts",
            enabledIds ?? ["SP0027"],
            sessionFactory == null
                ? null
                : new SharpProofAnalyzer(
                    sessionFactory));
    }

    private static void AssertRequiresDiagnostics(
        ImmutableArray<Diagnostic> diagnostics,
        int count)
    {
        Assert.That(
            diagnostics.Select(static diagnostic =>
                diagnostic.Id),
            Is.EqualTo(
                Enumerable.Repeat("SP0027", count)));
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
