using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Meta.Analyzers;

namespace SharpProof.Meta.Analyzers.Test;

[TestFixture]
public sealed class SharpProofSoundnessAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> PlatformReferences =
        CreatePlatformReferences();

    private static string SemanticCacheWriteFixture(string body) =>
        """
        namespace SharpProof.Verify;
        enum Answer { Unknown, Proven }
        sealed class ProofCache {
            internal void Write(Answer answer) { }
        }
        """ + body;

    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Frontend;
        sealed class C {
            Compilation M(Compilation compilation, SyntaxTree oldTree, SyntaxTree newTree) =>
                compilation.ReplaceSyntaxTree(oldTree, newTree);
        }
        """,
        "SPMETA001")]
    [TestCase(
        """
        using System.Runtime.CompilerServices;
        namespace SharpProof.Verify;
        static class C {
            static object M() =>
                RuntimeHelpers.GetUninitializedObject(typeof(object));
        }
        """,
        "SPMETA001")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.CompilerArtifact;
        sealed class C {
            void M(Compilation compilation) =>
                _ = compilation.GetSymbolsWithName(static _ => true);
        }
        """,
        "SPMETA001")]
    [TestCase(
        """
        namespace SharpProof.CompilerArtifact;
        sealed class C {
            bool M(string reason) {
                if (reason == "ir_condition_both_branches_feasible")
                    return true;
                return false;
            }
        }
        """,
        "SPMETA004")]
    [TestCase(
        """
        namespace SharpProof.CompilerArtifact;
        static class C {
            static string M(string name) =>
                "(" + name + ") is not null";
        }
        """,
        "SPMETA009")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Frontend;
        sealed class C {
            Compilation M(Compilation compilation, SyntaxTree tree) =>
                compilation.AddSyntaxTrees(tree);
        }
        """,
        "SPMETA001")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Frontend;
        sealed class C {
            Compilation M(Compilation compilation, SyntaxTree tree) =>
                compilation.RemoveSyntaxTrees(tree);
        }
        """,
        "SPMETA001")]
    [TestCase(
        """
        using System.Collections.Generic;
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Frontend;
        sealed class C {
            Compilation M(
                Compilation compilation,
                IEnumerable<SyntaxTree> trees) =>
                compilation.RemoveSyntaxTrees(trees);
        }
        """,
        "SPMETA001")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Frontend;
        sealed class C {
            Compilation M(Compilation compilation) =>
                compilation.RemoveAllSyntaxTrees();
        }
        """,
        "SPMETA001")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Frontend.Lowering;
        static class C {
            static string M(ISymbol symbol) => symbol.ToDisplayString();
        }
        """,
        "SPMETA001")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Frontend;
        static class C {
            static void M(SemanticModel model) {
                _ = model.GetDiagnostics();
            }
        }
        """,
        "SPMETA001")]
    [TestCase(
        """
        namespace SharpProof.Analyzer;
        static class C { private static int state; }
        """,
        "SPMETA002")]
    [TestCase(
        """
        using System;
        namespace SharpProof.Verify;
        sealed class C {
            void M() {
                try { }
                catch (OperationCanceledException) { Console.WriteLine(); }
            }
        }
        """,
        "SPMETA003")]
    [TestCase(
        """
        namespace SharpProof.Dataflow;
        sealed class C {
            bool M(string reason) {
                if (reason == "ir_condition_both_branches_feasible") return true;
                return false;
            }
        }
        """,
        "SPMETA004")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Analyzer;
        static class C {
            static readonly DiagnosticDescriptor Rule = new(
                "ID", "title", "message", "category",
                DiagnosticSeverity.Info, true);
        }
        """,
        "SPMETA005")]
    [TestCase(
        """
        namespace SharpProof.Ir;
        sealed class C { private readonly string identity = ""; }
        """,
        "SPMETA006")]
    [TestCase(
        """
        using System;
        namespace SharpProof.Frontend;
        sealed class C {
            bool M(string reason) {
                if (string.Equals(
                        reason,
                        "ir_condition_both_branches_feasible",
                        StringComparison.Ordinal))
                    return true;
                return false;
            }
        }
        """,
        "SPMETA004")]
    [TestCase(
        """
        namespace SharpProof.Frontend;
        static class C {
            static string M(string name) =>
                "(" + name + ") is not null";
        }
        """,
        "SPMETA009")]
    [TestCase(
        """
        namespace SharpProof.Verify {
            public sealed class Assumption {
                public Assumption() { }
            }
        }
        namespace SharpProof.Frontend {
            sealed class C {
                object M() => new SharpProof.Verify.Assumption();
            }
        }
        """,
        "SPMETA007")]
    [TestCase(
        """
        namespace SharpProof.Effects {
            public sealed class EffectSummary {
                public EffectSummary() { }
            }
        }
        namespace SharpProof.Analyzer {
            sealed class C {
                object M() => new SharpProof.Effects.EffectSummary();
            }
        }
        """,
        "SPMETA008")]
    [TestCase(
        """
        namespace SharpProof.Verify;
        enum Answer { Unknown }
        sealed class ProofCache {
            internal void Add(string key, Answer answer) { }
        }
        sealed class C {
            void M(ProofCache cache) =>
                cache.Add("answer", Answer.Unknown);
        }
        """,
        "SPMETA010")]
    [TestCase(
        """
        namespace SharpProof.Verify;
        enum Answer { Unknown }
        sealed class ProofCache {
            internal void Write(Answer answer) { }
        }
        sealed class C {
            static Answer CreateTimeout() => Answer.Unknown;
            void M(ProofCache cache) => cache.Write(CreateTimeout());
        }
        """,
        "SPMETA010")]
    [TestCase(
        """
        namespace SharpProof.Verify;
        sealed class ErrorAnswer { }
        sealed class ProofCache {
            internal void Set(ErrorAnswer answer) { }
        }
        sealed class C {
            void M(ProofCache cache) => cache.Set(new ErrorAnswer());
        }
        """,
        "SPMETA010")]
    public async Task ReportsSoundnessBoundaryViolation(string source, string expectedId)
    {
        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain(expectedId));
    }

    [Test]
    public async Task ReportsForbiddenApiCapturedAsMethodReference()
    {
        var diagnostics = await Analyze(
            """
            using Microsoft.CodeAnalysis;
            namespace SharpProof.Frontend;
            static class C {
                static Compilation M(Compilation compilation, SyntaxTree oldTree, SyntaxTree newTree) {
                    System.Func<SyntaxTree, SyntaxTree, Compilation> replace =
                        compilation.ReplaceSyntaxTree;
                    return replace(oldTree, newTree);
                }
            }
            """);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA001"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task ReportsDynamicInvocationInSoundnessCriticalLayer()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Frontend;
            static class C {
                static object M(dynamic value) => value.GetDiagnostics();
            }
            """);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA001"),
            Is.EqualTo(1));
    }

    [Test]
    public void CacheReachingDefinitionsObserveCancellationBeforeGraphConstruction()
    {
        var rules = typeof(SharpProofSoundnessAnalyzer).Assembly.GetType(
            "SharpProof.Meta.Analyzers.CacheSoundnessRules")!;
        var method = rules.GetMethods(
                System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static)
            .SingleOrDefault(candidate =>
                candidate.Name == "GetReachingLocalValues" &&
                candidate.GetParameters() is
                [_, _, { ParameterType: { } tokenType }] &&
                tokenType == typeof(CancellationToken));
        Assert.That(method, Is.Not.Null);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(
            (Action)(() => method!.Invoke(
                null,
                [null, null, cancellation.Token])));

        Assert.That(
            exception!.InnerException,
            Is.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task ReportsEveryRoslynTextParserEntryPoint()
    {
        var diagnostics = await Analyze(
            """
            using Microsoft.CodeAnalysis.CSharp;
            namespace SharpProof.Frontend;
            static class C {
                static void M() {
                    _ = SyntaxFactory.ParseCompilationUnit("class D { }");
                    _ = SyntaxFactory.ParseMemberDeclaration("class D { }");
                    _ = CSharpSyntaxTree.ParseText("class D { }");
                }
            }
            """);

        Assert.That(
            diagnostics
                .Where(static diagnostic => diagnostic.Id == "SPMETA001")
                .Select(static diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EquivalentTo((string[])
            [
                "API 'ParseCompilationUnit' is forbidden in " +
                    "soundness-critical SharpProof layers",
                "API 'ParseMemberDeclaration' is forbidden in " +
                    "soundness-critical SharpProof layers",
                "API 'ParseText' is forbidden in soundness-critical " +
                    "SharpProof layers"
            ]));
    }

    [Test]
    public async Task SemanticCacheWritesTrackAliasesAndAssignments()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify {
            using ExternalAnswer = Other.Answer;
            enum Answer { Unknown, TimedOut, Failed, Proven }
            sealed class AnswerSource {
                internal Answer Unknown => Answer.Proven;
                internal Answer CreateTimeout() => Answer.Proven;
            }
            sealed class ProofCache {
                internal Answer this[string key] { set { } }
                internal Answer Latest { set { } }
                internal void Add(string key, Answer answer) { }
                internal void AddOrUpdate(string key, Answer answer) { }
                internal void Write(Answer answer) { }
            }
            sealed class C {
                void AliasUnknown(ProofCache cache) {
                    var answer = Answer.Unknown;
                    cache.Add("key", answer);
                }
                void AliasTimedOut(ProofCache cache) {
                    var answer = Answer.TimedOut;
                    cache.Write(answer);
                }
                void AliasFailed(ProofCache cache) {
                    var answer = Answer.Failed;
                    cache["key"] = answer;
                }
                void Branch(ProofCache cache, bool condition) {
                    var answer = Answer.Proven;
                    if (condition) answer = Answer.Unknown;
                    cache.Write(answer);
                }
                void DirectIndexer(ProofCache cache) =>
                    cache["key"] = Answer.Unknown;
                void Property(ProofCache cache) =>
                    cache.Latest = Answer.Failed;
                void Overwrite(ProofCache cache) =>
                    cache.AddOrUpdate("key", Answer.TimedOut);
                void Unresolved(ProofCache cache, Answer answer) =>
                    cache.Write(answer);
                void Safe(ProofCache cache, AnswerSource source) {
                    var answer = Answer.Unknown;
                    answer = Answer.Proven;
                    cache.Add("key", answer);
                    cache["key"] = Answer.Proven;
                    cache.Latest = source.Unknown;
                    cache.Write(source.CreateTimeout());
                    var TimeoutAnswer = Answer.Proven;
                    cache.Write(TimeoutAnswer);
                    _ = ExternalAnswer.Unknown;
                }
            }
            }
            namespace Other {
                enum Answer { Unknown }
            }
            """);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA010"),
            Is.EqualTo(8));
    }

    [Test]
    public async Task SemanticCacheTryUpdateIgnoresComparisonArgument()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify;
            enum Answer { Unknown, Proven }
            sealed class ProofCache {
                internal void TryUpdate(string key, Answer value, Answer comparison) { }
            }
            sealed class C {
                void M(ProofCache cache, Answer comparison) =>
                    cache.TryUpdate("key", Answer.Proven, comparison);
            }
            """);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA010"),
            Is.Zero);
    }

    [Test]
    public async Task SemanticCacheWritesDistinguishAliasVersions()
    {
        var diagnostics = await Analyze(
            SemanticCacheWriteFixture(
            """
            sealed class C {
                void M(ProofCache cache) {
                    var answer = Answer.Proven;
                    var copy = answer;
                    answer = copy;
                    cache.Write(answer);
                }
            }
            """));

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA010"),
            Is.Zero);
    }

    [Test]
    public async Task SemanticCacheWritesClassifyCanonicalEnumValues()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify;
            enum Answer {
                Unknown = 0,
                Alias = Unknown,
                Proven = 1,
                Abstained = 2
            }
            static class ExternalConstants {
                internal const int Unknown = 0;
            }
            sealed class ProofCache {
                internal void Write(Answer answer) { }
            }
            sealed class C {
                void Alias(ProofCache cache) =>
                    cache.Write(Answer.Alias);
                void ExternalConstant(ProofCache cache) =>
                    cache.Write((Answer)ExternalConstants.Unknown);
                void DefaultUnknown(ProofCache cache) =>
                    cache.Write(default);
                void Abstention(ProofCache cache) =>
                    cache.Write(Answer.Abstained);
                void Safe(ProofCache cache) =>
                    cache.Write(Answer.Proven);
            }
            """);

        AssertSemanticCacheDiagnostics(diagnostics, 4);
    }

    [Test]
    public async Task SemanticCacheWritesInspectNeutralWrapperConstructorArguments()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify;
            enum Answer { Unknown, TimedOut, Proven }
            sealed class Envelope {
                internal Envelope(Answer answer) => Answer = answer;
                internal Answer Answer { get; }
            }
            sealed class ProofCache {
                internal void Write(Envelope envelope) { }
            }
            sealed class C {
                private static Envelope CreateTimeout() =>
                    new Envelope(Answer.TimedOut);

                void M(ProofCache cache) {
                    cache.Write(new Envelope(Answer.Unknown));
                    cache.Write(CreateTimeout());
                    cache.Write(new Envelope(Answer.Proven));
                }
            }
            """);

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA010"),
            Is.EqualTo(2));
    }

    [Test]
    public async Task SemanticCacheWritesClassifyNumericEnumConversions()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify;
            enum Answer {
                Unknown = 0,
                Proven = 1,
                Abstained = 2
            }
            sealed class ProofCache {
                internal void Write(Answer answer) { }
            }
            sealed class C {
                void M(ProofCache cache, int runtimeValue) {
                    var unknown = 0;
                    var proven = 1;
                    var abstained = 2;
                    var undefined = 42;
                    cache.Write((Answer)unknown);
                    cache.Write((Answer)proven);
                    cache.Write((Answer)abstained);
                    cache.Write((Answer)undefined);
                    cache.Write((Answer)runtimeValue);
                }
            }
            """);

        AssertSemanticCacheDiagnostics(diagnostics, 4);
    }

    [Test]
    public async Task SemanticCacheWritesTrackRefOutAndDeconstructionDefinitions()
    {
        var diagnostics = await Analyze(
            SemanticCacheWriteFixture(
            """
            sealed class C {
                private static void SetRef(ref Answer answer) =>
                    answer = Answer.Unknown;
                private static void SetOut(out Answer answer) =>
                    answer = Answer.Unknown;

                void RefWrite(ProofCache cache) {
                    var answer = Answer.Proven;
                    SetRef(ref answer);
                    cache.Write(answer);
                }

                void OutWrite(ProofCache cache) {
                    var answer = Answer.Proven;
                    SetOut(out answer);
                    cache.Write(answer);
                }

                void DeconstructionWrite(ProofCache cache) {
                    var answer = Answer.Proven;
                    (answer, _) = (Answer.Unknown, 0);
                    cache.Write(answer);
                }

                void SafeDeconstructionOverwrite(ProofCache cache) {
                    var answer = Answer.Unknown;
                    (answer, _) = (Answer.Proven, 0);
                    cache.Write(answer);
                }
            }
            """));

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA010"),
            Is.EqualTo(3));
    }

    [Test]
    public async Task SemanticCacheWritesUnwrapNestedOrdinaryConversions()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify;
            enum Answer { Unknown, Proven }
            interface IAnswerStore {
                Answer Latest { set; }
                void Write(Answer answer);
            }
            sealed class ProofCache : IAnswerStore {
                public Answer Latest { set { } }
                public void Write(Answer answer) { }
            }
            sealed class C {
                void WrappedValue(ProofCache cache) {
                    cache.Write((Answer)(object)Answer.Unknown);
                    cache.Latest = (Answer)(object)Answer.Unknown;
                }
                void WrappedReceiver(ProofCache cache) {
                    ((IAnswerStore)(object)cache).Write(Answer.Unknown);
                    ((IAnswerStore)(object)cache).Latest = Answer.Unknown;
                }
            }
            """);

        AssertSemanticCacheDiagnostics(diagnostics, 4);
    }

    [Test]
    public async Task SemanticCacheWritesFollowNestedAssignmentEvaluationOrder()
    {
        var diagnostics = await Analyze(
            SemanticCacheWriteFixture(
            """
            sealed class C {
                private static Answer ReturnUnknown(Answer answer) =>
                    Answer.Unknown;

                void M(ProofCache cache) {
                    var answer = Answer.Proven;
                    answer = ReturnUnknown(answer = Answer.Proven);
                    cache.Write(answer);
                }
            }
            """));

        AssertSemanticCacheDiagnostics(diagnostics, 1);
    }

    [Test]
    public async Task SemanticCacheWritesUseNestedCallableReachingValues()
    {
        var unsafeDiagnostics = await Analyze(
            SemanticCacheWriteFixture(
            """
            sealed class C {
                void Lambda(ProofCache cache) {
                    var answer = Answer.Proven;
                    System.Action write = () => {
                        answer = Answer.Unknown;
                        cache.Write(answer);
                    };
                    write();
                }

                void LocalFunction(ProofCache cache) {
                    var answer = Answer.Proven;
                    void Write() {
                        answer = Answer.Unknown;
                        cache.Write(answer);
                    }
                    Write();
                }
            }
            """));
        var safeDiagnostics = await Analyze(
            SemanticCacheWriteFixture(
            """
            sealed class C {
                void Lambda(ProofCache cache) {
                    var answer = Answer.Unknown;
                    System.Action write = () => {
                        answer = Answer.Proven;
                        cache.Write(answer);
                    };
                    write();
                }

                void LocalFunction(ProofCache cache) {
                    var answer = Answer.Unknown;
                    void Write() {
                        answer = Answer.Proven;
                        cache.Write(answer);
                    }
                    Write();
                }
            }
            """));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                unsafeDiagnostics.Count(static diagnostic =>
                    diagnostic.Id == "SPMETA010"),
                Is.EqualTo(2));
            Assert.That(
                safeDiagnostics.Count(static diagnostic =>
                    diagnostic.Id == "SPMETA010"),
                Is.Zero);
        }
    }

    [Test]
    public async Task SemanticCacheWritesRetainPreAssignmentValuesOnExceptionalPaths()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify;
            enum Answer { Unknown, Proven }
            sealed class AssignmentFailureException : System.Exception { }
            sealed class ProofCache {
                internal void Write(Answer answer) { }
            }
            sealed class C {
                private static Answer CompleteOrThrow(bool fail) {
                    if (fail) throw new AssignmentFailureException();
                    return Answer.Proven;
                }

                void M(ProofCache cache, bool fail) {
                    object answer = Answer.Unknown;
                    try {
                        answer = CompleteOrThrow(fail);
                    }
                    catch (AssignmentFailureException) {
                        cache.Write((Answer)answer);
                    }
                }

                void SafeOverwriteBeforeThrow(ProofCache cache) {
                    object answer = Answer.Unknown;
                    try {
                        answer = Answer.Proven;
                        throw new AssignmentFailureException();
                    }
                    catch (AssignmentFailureException) {
                        cache.Write((Answer)answer);
                    }
                }
            }
            """);

        AssertSemanticCacheDiagnostics(diagnostics, 1);
    }

    [Test]
    public async Task SemanticCacheWritesRejectCoalesceAndCompoundAssignments()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify;
            enum Answer { Proven, Unknown }
            sealed class ProofCache {
                internal Answer? Latest { get; set; }
                internal Answer? this[string key] {
                    get => Answer.Proven;
                    set { }
                }
                internal Answer Combined { get; set; }
                internal Answer this[int key] {
                    get => Answer.Proven;
                    set { }
                }
            }
            sealed class C {
                void M(ProofCache cache) {
                    cache.Latest ??= Answer.Unknown;
                    cache["key"] ??= Answer.Unknown;
                    cache.Combined |= Answer.Unknown;
                    cache[0] |= Answer.Unknown;
                }
            }
            """);

        AssertSemanticCacheDiagnostics(diagnostics, 4);
    }

    [Test]
    public async Task SemanticCacheFieldWritesAreRejected()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify;
            enum Answer { Proven, Unknown }
            sealed class ProofCache {
                internal Answer Latest;
                internal Answer? Optional;
                internal Answer Combined;
                internal Answer Safe;
            }
            sealed class C {
                void M(ProofCache cache) {
                    cache.Latest = Answer.Unknown;
                    cache.Optional ??= Answer.Unknown;
                    cache.Combined |= Answer.Unknown;
                    cache.Safe = Answer.Proven;
                }
            }
            """);

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA010"),
            Is.EqualTo(3));
    }

    [Test]
    public async Task SemanticCacheWritesRetainAllConditionalDefinitions()
    {
        var diagnostics = await Analyze(
            SemanticCacheWriteFixture(
            """
            sealed class C {
                void M(ProofCache cache, bool first, bool second) {
                    var answer = Answer.Unknown;
                    if (first) answer = Answer.Proven;
                    if (second) answer = Answer.Proven;
                    cache.Write(answer);
                }
            }
            """));

        AssertSemanticCacheDiagnostics(diagnostics, 1);
    }

    [Test]
    public async Task SemanticCacheWritesJoinBranchesLoopsAndOverwrites()
    {
        var diagnostics = await Analyze(
            SemanticCacheWriteFixture(
            """
            sealed class C {
                void ExhaustiveSafe(ProofCache cache, bool condition) {
                    var answer = Answer.Unknown;
                    if (condition) answer = Answer.Proven;
                    else answer = Answer.Proven;
                    cache.Write(answer);
                }
                void StraightLineSafe(ProofCache cache, bool condition) {
                    var answer = Answer.Unknown;
                    if (condition) answer = Answer.Unknown;
                    answer = Answer.Proven;
                    cache.Write(answer);
                }
                void LoopMayWriteUnknown(ProofCache cache, bool condition) {
                    var answer = Answer.Proven;
                    while (condition) answer = Answer.Unknown;
                    cache.Write(answer);
                }
                void ManyIndependentConditions(
                    ProofCache cache, bool first, bool second, bool third) {
                    var answer = Answer.Unknown;
                    if (first) answer = Answer.Proven;
                    if (second) answer = Answer.Proven;
                    if (third) answer = Answer.Proven;
                    cache.Write(answer);
                }
                void ExhaustiveMixed(ProofCache cache, bool condition) {
                    var answer = Answer.Proven;
                    if (condition) answer = Answer.Unknown;
                    else answer = Answer.Proven;
                    cache.Write(answer);
                }
            }
            """));

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA010"),
            Is.EqualTo(3));
    }

    [Test]
    public async Task SemanticCacheWritesFollowInterfaceAndBaseTypedAliases()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify;
            enum Answer { Unknown, Proven }
            interface IAnswerStore {
                Answer this[string key] { set; }
                Answer Latest { set; }
                void Write(Answer answer);
            }
            abstract class AnswerStoreBase {
                internal abstract Answer this[string key] { set; }
                internal abstract Answer Latest { set; }
                internal abstract void Write(Answer answer);
            }
            sealed class ProofCache : AnswerStoreBase, IAnswerStore {
                Answer IAnswerStore.this[string key] { set { } }
                Answer IAnswerStore.Latest { set { } }
                void IAnswerStore.Write(Answer answer) { }
                internal override Answer this[string key] { set { } }
                internal override Answer Latest { set { } }
                internal override void Write(Answer answer) { }
            }
            sealed class C {
                void ThroughInterface(ProofCache cache) {
                    IAnswerStore store = cache;
                    store.Write(Answer.Unknown);
                    store["key"] = Answer.Unknown;
                    store.Latest = Answer.Unknown;
                }
                void ThroughBase(ProofCache cache) {
                    AnswerStoreBase store = cache;
                    store.Write(Answer.Unknown);
                    store["key"] = Answer.Unknown;
                    store.Latest = Answer.Unknown;
                }
            }
            """);

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA010"),
            Is.EqualTo(6));
    }

    [Test]
    public async Task SemanticCacheWritesAnalyzeHelperReturnExpressions()
    {
        var diagnostics = await Analyze(
            SemanticCacheWriteFixture(
            """
            static class AnswerSource {
                internal static Answer Alias() {
                    var answer = Answer.Unknown;
                    return answer;
                }
                internal static Answer Conditional(bool condition) =>
                    condition ? Answer.Unknown : Answer.Proven;
                internal static Answer Switch(int value) => value switch {
                    0 => Answer.Unknown,
                    _ => Answer.Proven
                };
                internal static Answer Coalesce(Answer? answer) =>
                    answer ?? Answer.Unknown;
                internal static Answer Nested() => Alias();
                internal static Answer AliasProperty {
                    get {
                        var answer = Answer.Unknown;
                        return answer;
                    }
                }
                internal static Answer ConditionalProperty =>
                    true ? Answer.Unknown : Answer.Proven;
            }
            sealed class C {
                void M(ProofCache cache, bool condition, int value,
                    Answer? answer) {
                    cache.Write(AnswerSource.Alias());
                    cache.Write(AnswerSource.Conditional(condition));
                    cache.Write(AnswerSource.Switch(value));
                    cache.Write(AnswerSource.Coalesce(answer));
                    cache.Write(AnswerSource.Nested());
                    cache.Write(AnswerSource.AliasProperty);
                    cache.Write(AnswerSource.ConditionalProperty);
                }
            }
            """));

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA010"),
            Is.EqualTo(7));
    }

    [Test]
    public async Task SemanticCacheWritesIgnoreReturnsInConstantDisabledHelperBranches()
    {
        var diagnostics = await Analyze(
            SemanticCacheWriteFixture(
            """
            static class AnswerSource {
                internal static Answer Create() {
                    if (false) return Answer.Unknown;
                    return Answer.Proven;
                }
            }
            sealed class C {
                void M(ProofCache cache) => cache.Write(AnswerSource.Create());
            }
            """));

        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA010"), Is.EqualTo(0));
    }

    [Test]
    public async Task SemanticCacheWritesInspectVirtualAndInterfaceProducerImplementations()
    {
        var diagnostics = await Analyze(
            SemanticCacheWriteFixture(
            """
            interface IAnswerSource {
                Answer Create();
                Answer Value { get; }
            }
            sealed class InterfaceAnswerSource : IAnswerSource {
                public Answer Create() => Answer.Unknown;
                public Answer Value => Answer.Unknown;
            }
            class AnswerSource {
                internal virtual Answer Create() => Answer.Proven;
                internal virtual Answer Value => Answer.Proven;
            }
            sealed class UnstableAnswerSource : AnswerSource {
                internal override Answer Create() => Answer.Unknown;
                internal override Answer Value => Answer.Unknown;
            }
            sealed class C {
                void ThroughInterface(
                    ProofCache cache,
                    IAnswerSource source) {
                    cache.Write(source.Create());
                    cache.Write(source.Value);
                }
                void ThroughBase(
                    ProofCache cache,
                    AnswerSource source) {
                    cache.Write(source.Create());
                    cache.Write(source.Value);
                }
            }
            """));

        AssertSemanticCacheDiagnostics(diagnostics, 4);
    }

    [Test]
    public async Task SemanticCacheWritesFollowGenericForwardedArguments()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Verify;
            enum Answer { Unknown, Proven }
            sealed class ProofCache<T> {
                internal void Write(T answer) { }
            }
            static class CacheForwarder {
                internal static void Forward<T>(
                    ProofCache<T> cache,
                    T answer) =>
                    cache.Write(answer);
            }
            sealed class C {
                void M(
                    ProofCache<Answer> answers,
                    ProofCache<string> strings) {
                    CacheForwarder.Forward(answers, Answer.Unknown);
                    CacheForwarder.Forward(answers, Answer.Proven);
                    CacheForwarder.Forward(strings, "Unknown");
                }
            }
            """);

        AssertSemanticCacheDiagnostics(diagnostics, 1);
    }

    [TestCase("SetAsync")]
    [TestCase("Put")]
    [TestCase("Store")]
    [TestCase("Insert")]
    [TestCase("Update")]
    public async Task SemanticCacheWritesRecognizeCommonMutationNames(
        string methodName)
    {
        var diagnostics = await Analyze(
            $$"""
            namespace SharpProof.Verify;
            enum Answer { Unknown, Proven }
            sealed class ProofCache {
                internal void {{methodName}}(string key, Answer answer) { }
            }
            sealed class C {
                void M(ProofCache cache) =>
                    cache.{{methodName}}("key", Answer.Unknown);
            }
            """);

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA010"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task SemanticCacheGetOrAddInspectsValueFactories()
    {
        var diagnostics = await Analyze(
            """
            using System;
            namespace SharpProof.Verify;
            enum Answer { Unknown, TimedOut, Failed, Proven }
            sealed class ProofCache {
                internal Answer GetOrAdd(
                    string key,
                    Func<string, Answer> valueFactory) =>
                    valueFactory(key);
            }
            sealed class C {
                private static Answer CreateFailure(string key) =>
                    Answer.Failed;
                private static Answer CreateSafe(string key) =>
                    Answer.Proven;

                void M(ProofCache cache, bool condition) {
                    cache.GetOrAdd("unknown", _ => Answer.Unknown);
                    cache.GetOrAdd("timeout", _ => {
                        var answer = Answer.TimedOut;
                        return answer;
                    });
                    cache.GetOrAdd("failure", CreateFailure);
                    cache.GetOrAdd(
                        "conditional",
                        _ => condition ? Answer.Proven : Answer.Unknown);
                    cache.GetOrAdd("safe", _ => Answer.Proven);
                    cache.GetOrAdd("safe-helper", CreateSafe);
                }
            }
            """);

        AssertSemanticCacheDiagnostics(diagnostics, 4);
    }

    [Test]
    public async Task WorkerVerifyResponseIsAConservativeSemanticCacheValue()
    {
        var diagnostics = await Analyze(
            """
            namespace SharpProof.Worker.Protocol {
                sealed class WorkerVerifyResponse { }
            }
            namespace SharpProof.Worker {
                using SharpProof.Worker.Protocol;
                sealed class VerificationCache {
                    internal static bool IsCacheable(
                        WorkerVerifyResponse response) => true;
                    internal void TryWrite(WorkerVerifyResponse response) { }
                }
                sealed class C {
                    void M(VerificationCache cache,
                        WorkerVerifyResponse response) =>
                        cache.TryWrite(response);
                    void Guarded(VerificationCache cache,
                        WorkerVerifyResponse response) {
                        if (VerificationCache.IsCacheable(response)) {
                            cache.TryWrite(response);
                        }
                    }
                }
            }
            """);

        AssertSemanticCacheDiagnostics(diagnostics, 1);
    }

    [TestCaseSource(nameof(CSharpExpressionConstructionCases))]
    public async Task ReportsCSharpExpressionTextConstruction(string source)
    {
        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA009"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task AllowsOrdinaryInterpolatedFormattingAndValueDecoys()
    {
        const string source =
            """
            namespace SharpProof.Frontend;
            static class C {
                private const string Fragment = " is not null";
                internal static string Ordinary(string name) =>
                    $"Name: {name}";
                internal static string Formatted(int value) =>
                    $"Value: {value:D}";
                internal static string Aligned(string value) =>
                    $"Value: {value,10}";
                internal static string ValueOnly() => $"{Fragment}";
                internal static string FormatDecoy(int value) =>
                    $"{value: is not null}";
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SPMETA009"));
    }

    [TestCaseSource(nameof(SemanticPatternControlFlowCases))]
    public async Task ReportsSemanticPatternControlFlowOnce(string source)
    {
        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA004"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task AllowsNonsemanticAndNonconstantPatternControls()
    {
        const string source =
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static bool OrdinaryIs(string reason) =>
                    reason is "ordinary";
                internal static bool NullIs(string reason) => reason is null;
                internal static bool OrdinarySwitch(string reason) {
                    switch (reason) {
                        case "ordinary": return true;
                        default: return false;
                    }
                }
                internal static bool OrdinaryExpression(string reason) =>
                    reason switch {
                        "ordinary" => true,
                        _ => false
                    };
                internal static int Relational(int value) => value switch {
                    > 0 => 1,
                    _ => 0
                };
                internal static string DefaultOnly(string reason) {
                    switch (reason) {
                        default: return reason;
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SPMETA004"));
    }

    [Test]
    public async Task AllowsImmutableStateAndCancellationRethrow()
    {
        const string source =
            """
            using System;
            namespace SharpProof.Verify;
            enum Status { Unknown, Proven }
            static class C {
                private static readonly object Gate = new();
                static void M() {
                    try { }
                    catch (OperationCanceledException cancellation) { throw cancellation; }
                }
                static void BroadCatchAfterCancellationRethrow() {
                    try { }
                    catch (OperationCanceledException cancellation) { throw (cancellation); }
                    catch (Exception) { }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task RejectsMutableStaticPropertyAndEventStorage()
    {
        const string source =
            """
            using System;
            namespace SharpProof.Analyzer;
            sealed class C {
                internal static int State { get; set; }
                internal static event Action? Changed;
                internal static void Raise() => Changed?.Invoke();
            }
            """;

        var diagnostics = await Analyze(source);
        var mutableState = diagnostics
            .Where(static diagnostic => diagnostic.Id == "SPMETA002")
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mutableState, Has.Length.EqualTo(2));
            Assert.That(
                mutableState.Select(static diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                Has.Some.Contains("State"));
            Assert.That(
                mutableState.Select(static diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
                Has.Some.Contains("Changed"));
        }
    }

    [Test]
    public async Task AnalyzesGeneratedMutableStaticStorage()
    {
        const string source =
            """
            namespace SharpProof.Analyzer;
            static class GeneratedPolicy {
                internal static int[] Policies = [];
            }
            """;

        var diagnostics = await AnalyzeGenerated(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SPMETA002"));
    }

    [Test]
    public async Task AnalyzesGeneratedSemanticCacheWrites()
    {
        const string source =
            """
            namespace SharpProof.Verify;
            enum Answer { Unknown }
            sealed class ProofCache {
                internal void Add(string key, Answer answer) { }
            }
            sealed class GeneratedCacheWriter {
                internal void Write(ProofCache cache) =>
                    cache.Add("answer", Answer.Unknown);
            }
            """;

        var diagnostics = await AnalyzeGenerated(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SPMETA010"));
    }

    [Test]
    public async Task AllowsStaticAbstractInterfaceMembersWithoutStorage()
    {
        const string source =
            """
            namespace SharpProof.Analyzer;
            public interface IStorageFree {
                static abstract int Value { get; }
                static abstract event System.Action Changed;
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SPMETA002"));
    }

    [TestCase("SharpProof.Meta.Analyzers")]
    [TestCase("SharpProof.Meta.Analyzers.Rules")]
    [TestCase("SharpProof.ContractForGenerator")]
    [TestCase("SharpProof.ContractForGenerator.Generation")]
    public async Task RejectsMutableStaticStateInEveryRoslynProductionNamespace(
        string namespaceName)
    {
        var source =
            $$"""
            namespace {{namespaceName}};
            static class C {
                internal static int State;
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPMETA002"]));
    }

    [Test]
    public async Task RejectsReadonlyReferencesToMutableStaticStorage()
    {
        const string source =
            """
            using System.Collections.Concurrent;
            using System.Collections.Generic;
            namespace SharpProof.Analyzer;
            sealed class C {
                internal static readonly Dictionary<string, int> Table = new();
                internal static ConcurrentDictionary<string, int> Cache { get; } = new();
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA002"),
            Is.EqualTo(2));
    }

    [Test]
    public async Task RejectsReadonlyHoldersWithIndirectMutableState()
    {
        const string source =
            """
            using System;
            using System.Text;
            namespace SharpProof.Analyzer;
            sealed class MutableLeaf {
                internal int Value;
            }
            sealed class ReadonlyChildHolder {
                internal readonly MutableLeaf Child = new();
            }
            class MutableBase {
                internal int Value;
            }
            sealed class DerivedHolder : MutableBase { }
            sealed class ReadOnlyByName {
                internal int Value;
            }
            sealed class CallbackHolder {
                internal event Action? Changed;
                internal void Raise() => Changed?.Invoke();
            }
            static class C {
                internal static readonly ReadonlyChildHolder Nested = new();
                internal static readonly DerivedHolder Inherited = new();
                internal static readonly ReadOnlyByName MisleadingName = new();
                internal static readonly CallbackHolder Callbacks = new();
                internal static readonly StringBuilder Metadata = new();
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA002"),
            Is.EqualTo(5));
    }

    [Test]
    public async Task RejectsConstStringFieldInIr()
    {
        const string source =
            """
            namespace SharpProof.Ir;
            static class C {
                internal const string Unknown = "ir_unknown";
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA006"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task RejectsStringAutoPropertyInIr()
    {
        const string source =
            """
            namespace SharpProof.Ir;
            static class C {
                internal static string Unknown { get; set; } = "ir_unknown";
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA006"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task ReportsSemanticStringControlFlowInCatchFiltersAndSwitchGuards()
    {
        const string source =
            """
            using System;
            namespace SharpProof.Verify;
            static class C {
                static bool CatchFilter(string reason) {
                    try { throw new Exception(); }
                    catch (Exception) when (reason == "ir_unknown") { return true; }
                }
                static bool SwitchGuard(string reason) => reason switch {
                    _ when reason.Equals("ir_unknown") => true,
                    _ => false
                };
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA004"),
            Is.EqualTo(2));
    }

    [Test]
    public async Task ReportsSemanticStringControlFlowThroughCommonComparisonShapes()
    {
        const string source =
            """
            using System;
            using System.Linq;
            namespace SharpProof.Frontend;
            static class C {
                internal static bool ObjectEquality(string reason) =>
                    object.Equals(reason, "ir_object");

                internal static bool StringPredicate(string provenance) =>
                    provenance.StartsWith(
                        "ir_prefix",
                        StringComparison.Ordinal);

                internal static bool BooleanTemporary(string reason) {
                    var selected = reason == "ir_temporary";
                    if (selected) return true;
                    return false;
                }

                internal static bool LocalAlias(string reason) {
                    var expected = "ir_alias";
                    if (reason == expected) return true;
                    return false;
                }

                internal static string[] QueryFilter(string[] reasons) =>
                    reasons.Where(reason => reason == "ir_query").ToArray();
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA004"),
            Is.EqualTo(5));
    }

    [Test]
    public async Task AllowsStaticImmutableAndNonStorageMemberForms()
    {
        const string source =
            """
            using System;
            namespace SharpProof.Analyzer {
                sealed class Critical {
                    internal static int Immutable { get; } = 1;
                    internal int InstanceState { get; set; }
                    internal event Action? InstanceChanged;
                    internal static int Computed {
                        get => 1;
                        set { }
                    }
                    internal static event Action? CustomChanged {
                        add { }
                        remove { }
                    }
                    internal void Raise() => InstanceChanged?.Invoke();
                }
            }
            namespace SharpProof.Effects {
                sealed class Noncritical {
                    internal static int State { get; set; }
                    internal static event Action? Changed;
                    internal static void Raise() => Changed?.Invoke();
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SPMETA002"));
    }

    [Test]
    public async Task RejectsDerivedBroadAndBareCancellationCatches()
    {
        const string source =
            """
            using System;
            using System.Threading.Tasks;
            namespace SharpProof.Verify;
            interface IMarker { }
            sealed class CustomCancellationException : OperationCanceledException, IMarker { }
            static class C {
                static void TaskCanceled() {
                    try { } catch (TaskCanceledException) { }
                }
                static void Custom() {
                    try { } catch (CustomCancellationException) { }
                }
                static void SystemBase() {
                    try { } catch (SystemException) { }
                }
                static void ExceptionBase() {
                    try { } catch (Exception) { }
                }
                static void Bare() {
                    try { } catch { }
                }
                static void InterfaceFilter() {
                    try { }
                    catch (Exception exception) when (exception is IMarker) { }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA003"),
            Is.EqualTo(6));
    }

    [Test]
    public async Task RejectsRethrowDeferredUntilAfterCleanupOrDivergence()
    {
        const string source =
            """
            using System;
            namespace SharpProof.Verify;
            static class C {
                static void Cleanup(IDisposable cleanup) {
                    try { }
                    catch {
                        cleanup.Dispose();
                        throw;
                    }
                }
                static void Diverge() {
                    try { }
                    catch {
                        while (true) { }
                        throw;
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA003"),
            Is.EqualTo(2));
    }

    [Test]
    public async Task AllowsCatchFiltersThatExcludeCancellation()
    {
        const string source =
            """
            using System;
            namespace SharpProof.Verify;
            interface IMarker { }
            sealed class CustomCancellationException : OperationCanceledException, IMarker { }
            static class C {
                static void ExcludedType() {
                    try { }
                    catch (Exception exception)
                        when (exception is not OperationCanceledException) { }
                }
                static void Never() {
                    try { }
                    catch (Exception) when (false) { }
                }
                static void UnrelatedTypes() {
                    try { }
                    catch (Exception exception)
                        when (exception is ArgumentException or InvalidOperationException) { }
                }
                static void ExcludedImplementedInterface() {
                    try { }
                    catch (CustomCancellationException exception)
                        when (exception is not IMarker) { }
                }
                static void ParenthesizedExclusion() {
                    try { }
                    catch (Exception exception)
                        when ((exception is not OperationCanceledException)) { }
                }
                static void ParenthesizedPatternExclusion() {
                    try { }
                    catch (Exception exception)
                        when (exception is (not OperationCanceledException)) { }
                }
                static void ExhaustiveEarlierFilter() {
                    try { }
                    catch (OperationCanceledException) when (true) { throw; }
                    catch (Exception) { }
                }
                static void ExhaustiveEarlierTypePattern() {
                    try { }
                    catch (Exception exception)
                        when (exception is OperationCanceledException) { throw; }
                    catch (Exception) { }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SPMETA003"),
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
                diagnostic.ToString())));
    }

    [TestCase("caught is ArgumentException")]
    [TestCase("!(caught is OperationCanceledException)")]
    [TestCase("caught is null")]
    [TestCase("!(caught is not null)")]
    [TestCase("caught is not (OperationCanceledException or ArgumentException)")]
    [TestCase("caught is not OperationCanceledException && condition")]
    [TestCase("caught is ArgumentException || caught is InvalidOperationException")]
    public async Task AllowsComposedFiltersThatExcludeCancellation(string filter)
    {
        var source =
            $$"""
            using System;
            namespace SharpProof.Verify;
            static class C {
                static void M(bool condition) {
                    try { }
                    catch (Exception caught) when ({{filter}}) { }
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SPMETA003"),
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
                diagnostic.ToString())));
    }

    [TestCase("caught is not OperationCanceledException || condition")]
    [TestCase("caught is not ArgumentException && condition")]
    [TestCase("!(caught is ArgumentException)")]
    [TestCase("caught is not null")]
    public async Task RejectsComposedFiltersThatMayIncludeCancellation(string filter)
    {
        var source =
            $$"""
            using System;
            namespace SharpProof.Verify;
            static class C {
                static void M(bool condition) {
                    try { }
                    catch (Exception caught) when ({{filter}}) { }
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SPMETA003"),
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
                diagnostic.ToString())));
    }

    [TestCase("caught is not null")]
    [TestCase("caught is not ArgumentException")]
    [TestCase("!(caught is ArgumentException)")]
    [TestCase("caught is OperationCanceledException || condition")]
    [TestCase(
        "caught is OperationCanceledException && caught is not ArgumentException")]
    public async Task AllowsLaterCatchAfterExhaustiveCancellationFilter(
        string filter)
    {
        var source =
            $$"""
            using System;
            namespace SharpProof.Verify;
            static class C {
                static void M(bool condition) {
                    try { }
                    catch (Exception caught) when ({{filter}}) { throw; }
                    catch (Exception) { }
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SPMETA003"),
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
                diagnostic.ToString())));
    }

    [TestCase("MayThrow() || caught is OperationCanceledException")]
    [TestCase("!(MayThrow() && caught is ArgumentException)")]
    public async Task DoesNotTreatPotentiallyThrowingFilterAsExhaustive(
        string filter)
    {
        var source =
            $$"""
            using System;
            namespace SharpProof.Verify;
            static class C {
                static bool MayThrow() => throw new InvalidOperationException();
                static void M() {
                    try { }
                    catch (Exception caught) when ({{filter}}) { throw; }
                    catch (Exception) { }
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Count(static diagnostic => diagnostic.Id == "SPMETA003"),
            Is.EqualTo(1),
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
                diagnostic.ToString())));
    }

    [Test]
    public async Task UserDefinedConversionCannotExcludeCancellationFilterAnalysis()
    {
        const string source =
            """
            using System;
            namespace SharpProof.Verify;

            sealed class FilterException : Exception { }

            sealed class CustomCancellationException :
                OperationCanceledException
            {
                public static explicit operator FilterException(
                    CustomCancellationException exception) => new();
            }

            static class C
            {
                static void M()
                {
                    try { }
                    catch (CustomCancellationException caught)
                        when (((object)(FilterException)caught) is not
                            OperationCanceledException)
                    {
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPMETA003"]),
            string.Join(Environment.NewLine, diagnostics.Select(
                static diagnostic => diagnostic.ToString())));
    }

    [TestCase(
        """
        using System;
        namespace SharpProof.Verify;
        sealed class C {
            void M(bool condition) {
                try { }
                catch (OperationCanceledException) {
                    if (condition) throw;
                }
            }
        }
        """)]
    [TestCase(
        """
        using System;
        namespace SharpProof.Verify;
        sealed class C {
            void M() {
                try { }
                catch (OperationCanceledException) {
                    { throw; }
                }
            }
        }
        """)]
    [TestCase(
        """
        using System;
        using System.Threading;
        namespace SharpProof.Verify;
        sealed class C {
            void M(CancellationToken token) {
                try { }
                catch (OperationCanceledException) {
                    token.ThrowIfCancellationRequested();
                }
            }
        }
        """)]
    [TestCase(
        """
        using System;
        using System.Threading;
        namespace SharpProof.Verify;
        sealed class C {
            void M() {
                try { }
                catch (OperationCanceledException) {
                    CancellationToken.None.ThrowIfCancellationRequested();
                }
            }
        }
        """)]
    [TestCase(
        """
        using System;
        using System.Threading;
        namespace SharpProof.Verify;
        sealed class C {
            void M(bool condition, CancellationToken token) {
                try { }
                catch (OperationCanceledException) {
                    if (condition)
                        token.ThrowIfCancellationRequested();
                }
            }
        }
        """)]
    [TestCase(
        """
        using System;
        namespace SharpProof.Verify;
        sealed class LookalikeToken {
            internal void ThrowIfCancellationRequested() { }
        }
        sealed class C {
            void M(LookalikeToken token) {
                try { }
                catch (OperationCanceledException) {
                    token.ThrowIfCancellationRequested();
                }
            }
        }
        """)]
    [TestCase(
        """
        using System;
        using System.Threading;
        namespace SharpProof.Verify;
        sealed class C {
            void M(CancellationToken token) {
                try { }
                catch (OperationCanceledException) {
                    return;
                    token.ThrowIfCancellationRequested();
                }
            }
        }
        """)]
    [TestCase(
        """
        using System;
        namespace SharpProof.Worker;
        static class Program {
            internal static int Main() {
                try { }
                catch (OperationCanceledException) { return 4; }
                return 0;
            }
        }
        """)]
    public async Task RejectsDeferredOrUnrelatedCancellationPropagation(
        string source)
    {
        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SPMETA003"));
    }

    [Test]
    public async Task RejectsArbitraryWorkerMainCancellationTranslation()
    {
        const string source =
            """
            using System;
            using System.Threading.Tasks;
            namespace SharpProof.Worker {
                static class Program {
                    internal static async Task<int> Main(string[] args) {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) { return 4; }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task CallerCancellationRethrowCheckDoesNotAuthorizeArbitraryFallback()
    {
        const string source =
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace SharpProof.Worker {
                sealed class CallableVerificationResult { }

                static class CallableVerificationPolicy {
                    private static async Task<CallableVerificationResult>
                        VerifyTargetAsync(
                            object verifier,
                            object target,
                            object budgets,
                            object parallelism,
                            object resourceGate,
                            object projectBoundary,
                            CancellationToken callerCancellation) {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            callerCancellation.ThrowIfCancellationRequested();
                            return new CallableVerificationResult();
                        }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task RejectsBodyBlindWorkerVerifyAsyncCancellationTranslation()
    {
        const string source =
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SharpProof.Worker.Protocol {
                sealed class WorkerVerifyRequest { }
                sealed class WorkerVerifyResponse { }
            }
            namespace SharpProof.Worker {
                using SharpProof.Worker.Protocol;
                sealed class SharpProofWorker {
                    internal async Task<WorkerVerifyResponse> VerifyAsync(
                        WorkerVerifyRequest request,
                        CancellationToken cancellationToken) {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            return new WorkerVerifyResponse();
                        }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task WorkerVerifyAsyncRefKindOverloadDoesNotCrashAnalysis()
    {
        const string source =
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SharpProof.Worker.Protocol {
                sealed class WorkerVerifyRequest { }
                sealed class WorkerVerifyResponse { }
            }
            namespace SharpProof.Worker {
                using SharpProof.Worker.Protocol;
                sealed class SharpProofWorker {
                    internal async Task<WorkerVerifyResponse> VerifyAsync(
                        WorkerVerifyRequest request,
                        CancellationToken cancellationToken) {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            return new WorkerVerifyResponse();
                        }
                    }

                    internal Task<WorkerVerifyResponse> VerifyAsync(
                        WorkerVerifyRequest request,
                        ref CancellationToken cancellationToken) =>
                        Task.FromResult(new WorkerVerifyResponse());
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(1));
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("AD0001"));
    }

    [Test]
    public async Task WorkerCancellationResponseHelperMustPublishResponse()
    {
        const string source =
            """
            using System;
            using System.Threading.Tasks;

            namespace SharpProof.Worker.Protocol {
                sealed class WorkerVerifyResponse { }
                enum WorkerRunStatus { Canceled }
                static class WorkerResultAssembler {
                    internal static WorkerVerifyResponse Create(
                        WorkerRunStatus runStatus) => new();
                }
            }

            namespace SharpProof.Worker {
                using SharpProof.Worker.Protocol;

                static class Program {
                    internal static async Task<int> Main(string[] args) {
                        async Task<int> Respond(
                            WorkerVerifyResponse response) {
                            await Task.Yield();
                            return 0;
                        }
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            return await Respond(WorkerResultAssembler.Create(
                                WorkerRunStatus.Canceled));
                        }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(1));
    }

    [TestCase("", 0)]
    [TestCase("cancellationToken = default;", 1)]
    public async Task WorkerCancellationReificationRequiresIncomingToken(
        string tokenSetup,
        int expectedDiagnostics)
    {
        var source =
            $$"""
            using System;
            using System.IO;
            using System.Threading;
            using System.Threading.Tasks;

            namespace SharpProof.Worker.Protocol {
                sealed class WorkerVerifyRequest { }
                sealed class WorkerVerifyResponse { }
                enum WorkerRunStatus { Canceled, TimedOut }
                enum WorkerCallableCoverageReason { Canceled, ProjectTimeout }
                enum WorkerClaimReason { Canceled, ProjectTimeout }
                static class WorkerResultAssembler {
                    internal static WorkerVerifyResponse Create(
                        string inputHash,
                        WorkerRunStatus runStatus) => new();
                    internal static WorkerVerifyResponse CreateIncomplete(
                        WorkerRunStatus status,
                        WorkerCallableCoverageReason callableReason,
                        WorkerClaimReason claimReason) => new();
                }
            }

            namespace SharpProof.Worker {
                using SharpProof.Worker.Protocol;

                static class Program {
                    private static Task WriteResponseAtomicAsync(
                        string path,
                        WorkerVerifyResponse response) =>
                        File.WriteAllTextAsync(path, response.ToString()!);

                    internal static async Task<int> Main(string[] args) {
                        var resultPath = "result.json";
                        async Task<int> Respond(WorkerVerifyResponse response) {
                            await WriteResponseAtomicAsync(
                                resultPath,
                                response).ConfigureAwait(false);
                            return 0;
                        }
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            return await Respond(WorkerResultAssembler.Create(
                                "input", WorkerRunStatus.Canceled))
                                .ConfigureAwait(false);
                        }
                    }
                }

                sealed class SharpProofWorker {
                    internal async Task<WorkerVerifyResponse> VerifyAsync(
                        WorkerVerifyRequest request,
                        CancellationToken cancellationToken) {
                        {{tokenSetup}}
                        WorkerVerifyResponse Interrupted(object input = null) {
                            var canceled =
                                cancellationToken.IsCancellationRequested;
                            return WorkerResultAssembler.CreateIncomplete(
                                canceled
                                    ? WorkerRunStatus.Canceled
                                    : WorkerRunStatus.TimedOut,
                                canceled
                                    ? WorkerCallableCoverageReason.Canceled
                                    : WorkerCallableCoverageReason.ProjectTimeout,
                                canceled
                                    ? WorkerClaimReason.Canceled
                                    : WorkerClaimReason.ProjectTimeout);
                        }
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            return Interrupted();
                        }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(expectedDiagnostics));
    }

    [TestCase(
        "true ? await Respond(WorkerResultAssembler.Create(WorkerRunStatus.Failed)) : await Respond(WorkerResultAssembler.Create(WorkerRunStatus.Canceled))")]
    [TestCase(
        "await Respond(true ? WorkerResultAssembler.Create(WorkerRunStatus.Failed) : WorkerResultAssembler.Create(WorkerRunStatus.Canceled))")]
    [TestCase(
        "await Respond(WorkerResultAssembler.Create(WorkerRunStatus.Canceled == WorkerRunStatus.Canceled ? WorkerRunStatus.Failed : WorkerRunStatus.Failed))")]
    [TestCase(
        "await Respond(WorkerResultAssembler.Create(WorkerRunStatus.Failed, WorkerRunStatus.Canceled))")]
    [TestCase(
        "await Respond(Pick(WorkerResultAssembler.Create(WorkerRunStatus.Failed), WorkerResultAssembler.Create(WorkerRunStatus.Canceled)))")]
    public async Task RejectsInexactWorkerCancellationResponseShapes(
        string returnExpression)
    {
        var source =
            $$"""
            using System;
            using System.Threading.Tasks;

            namespace SharpProof.Worker.Protocol {
                sealed class WorkerVerifyResponse { }
                enum WorkerRunStatus { Canceled, Failed }
                static class WorkerResultAssembler {
                    internal static WorkerVerifyResponse Create(
                        WorkerRunStatus runStatus) => new();
                    internal static WorkerVerifyResponse Create(
                        WorkerRunStatus first,
                        WorkerRunStatus second) => new();
                }
            }

            namespace SharpProof.Worker {
                using SharpProof.Worker.Protocol;

                static class Program {
                    internal static async Task<int> Main(string[] args) {
                        async Task<int> Respond(
                            WorkerVerifyResponse response) {
                            await Task.Yield();
                            return 0;
                        }
                        WorkerVerifyResponse Pick(
                            WorkerVerifyResponse selected,
                            WorkerVerifyResponse decoy) => selected;
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            return {{returnExpression}};
                        }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(1));
    }

    [TestCase(
        "WorkerLookalike",
        "internal async Task<WorkerVerifyResponse> VerifyAsync(WorkerVerifyRequest request, CancellationToken cancellationToken)")]
    [TestCase(
        "SharpProofWorker",
        "internal static async Task<WorkerVerifyResponse> VerifyAsync(WorkerVerifyRequest request, CancellationToken cancellationToken)")]
    [TestCase(
        "SharpProofWorker",
        "internal async Task<WorkerVerifyResponse> Verify(WorkerVerifyRequest request, CancellationToken cancellationToken)")]
    [TestCase(
        "SharpProofWorker",
        "internal async Task<WorkerVerifyResponse> VerifyAsync(object request, CancellationToken cancellationToken)")]
    [TestCase(
        "SharpProofWorker",
        "internal async Task<object> VerifyAsync(WorkerVerifyRequest request, CancellationToken cancellationToken)")]
    [TestCase(
        "SharpProofWorker",
        "internal async Task<WorkerVerifyResponse> VerifyAsync(WorkerVerifyRequest input, CancellationToken cancellationToken)")]
    [TestCase(
        "SharpProofWorker",
        "internal async Task<WorkerVerifyResponse> VerifyAsync(WorkerVerifyRequest request, CancellationToken token)")]
    public async Task RejectsWorkerVerifyAsyncLookalikes(
        string typeName,
        string methodSignature)
    {
        var exactTypeDeclaration =
            typeName == "SharpProofWorker"
                ? ""
                : "sealed class SharpProofWorker { }";
        var source =
            $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SharpProof.Worker.Protocol {
                sealed class WorkerVerifyRequest { }
                sealed class WorkerVerifyResponse { }
            }
            namespace SharpProof.Worker {
                using SharpProof.Worker.Protocol;
                {{exactTypeDeclaration}}
                sealed class {{typeName}} {
                    {{methodSignature}} {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            return new WorkerVerifyResponse();
                        }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(1));
    }

    [TestCase("", 0)]
    [TestCase("callerCancellation = default;", 1)]
    public async Task TypedCancellationReificationRequiresIncomingToken(
        string tokenSetup,
        int expectedDiagnostics)
    {
        var source =
            $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SharpProof.Worker.Protocol {
                enum WorkerClaimReason { ProjectTimeout, Canceled }
                enum WorkerCallableCoverageReason { ProjectTimeout, Canceled }
            }
            namespace SharpProof.Worker {
                using SharpProof.Worker.Protocol;
                sealed class CallableVerificationResult { }
                static class CallableVerificationPolicy {
                    private static CallableVerificationResult Unknown(
                        object target,
                        WorkerClaimReason claimReason,
                        WorkerCallableCoverageReason callableReason) =>
                        new();
                    private static async Task<CallableVerificationResult>
                        VerifyTargetAsync(
                            object verifier,
                            object target,
                            object budgets,
                            object parallelism,
                            object resourceGate,
                            object projectBoundary,
                            CancellationToken callerCancellation) {
                        {{tokenSetup}}
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            if (callerCancellation.IsCancellationRequested)
                                return Unknown(
                                    target,
                                    WorkerClaimReason.Canceled,
                                    WorkerCallableCoverageReason.Canceled);
                            return Unknown(
                                target,
                                WorkerClaimReason.ProjectTimeout,
                                WorkerCallableCoverageReason.ProjectTimeout);
                        }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(expectedDiagnostics));
    }

    [TestCase(
        "unrelatedCancellation",
        "target",
        "WorkerClaimReason.Canceled",
        "WorkerCallableCoverageReason.Canceled",
        "Unknown")]
    [TestCase(
        "callerCancellation",
        "target",
        "WorkerClaimReason.ProjectTimeout",
        "WorkerCallableCoverageReason.Canceled",
        "Unknown")]
    [TestCase(
        "callerCancellation",
        "target",
        "WorkerClaimReason.Canceled",
        "WorkerCallableCoverageReason.ProjectTimeout",
        "Unknown")]
    [TestCase(
        "callerCancellation",
        "target",
        "WorkerClaimReason.Canceled",
        "WorkerCallableCoverageReason.Canceled",
        "CancellationReifier.Unknown")]
    [TestCase(
        "callerCancellation",
        "new object()",
        "WorkerClaimReason.Canceled",
        "WorkerCallableCoverageReason.Canceled",
        "Unknown")]
    public async Task AuditedWorkerTypedCancellationReificationMustBeExact(
        string cancellationReceiver,
        string targetArgument,
        string claimReason,
        string callableReason,
        string unknownHelper)
    {
        var source =
            $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SharpProof.Worker.Protocol {
                enum WorkerClaimReason { ProjectTimeout, Canceled }
                enum WorkerCallableCoverageReason { ProjectTimeout, Canceled }
            }
            namespace SharpProof.Worker {
                using SharpProof.Worker.Protocol;
                sealed class CallableVerificationResult { }
                static class CallableVerificationPolicy {
                    private static CallableVerificationResult Unknown(
                        object target,
                        WorkerClaimReason claimReason,
                        WorkerCallableCoverageReason callableReason) =>
                        new();
                    private static class CancellationReifier {
                        internal static CallableVerificationResult Unknown(
                            object target,
                            WorkerClaimReason claimReason,
                            WorkerCallableCoverageReason callableReason) =>
                            new();
                    }
                    private static async Task<CallableVerificationResult>
                        VerifyTargetAsync(
                            object verifier,
                            object target,
                            object budgets,
                            object parallelism,
                            object resourceGate,
                            CancellationToken unrelatedCancellation,
                            CancellationToken callerCancellation) {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            if ({{cancellationReceiver}}.IsCancellationRequested)
                                return {{unknownHelper}}(
                                    {{targetArgument}},
                                    {{claimReason}},
                                    {{callableReason}});
                            return Unknown(
                                target,
                                WorkerClaimReason.ProjectTimeout,
                                WorkerCallableCoverageReason.ProjectTimeout);
                        }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(1));
    }

    [Test]
    public async Task AuditedWorkerTimeoutBoundaryMustGuardCallerCancellation()
    {
        const string source =
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SharpProof.Worker {
                sealed class CallableVerificationResult { }
                static class CallableVerificationPolicy {
                    private static async Task<CallableVerificationResult>
                        VerifyTargetAsync(
                            object verifier,
                            object target,
                            object budgets,
                            object parallelism,
                            object resourceGate,
                            object projectBoundary,
                            CancellationToken callerCancellation) {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) {
                            return new CallableVerificationResult();
                        }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SPMETA003"));
    }

    [Test]
    public async Task RejectsTargetTypedProofOutcomesOutsideTheKernel()
    {
        const string source =
            """
            namespace SharpProof.Verify {
                public sealed class ProvenOutcome {
                    public ProvenOutcome() { }
                }
                public sealed class RefutedOutcome {
                    public RefutedOutcome() { }
                }
                public sealed class ValidatedModel {
                    public ValidatedModel() { }
                }
                public sealed class ProofKernel { }
            }
            namespace FriendAssembly.Consumer {
                sealed class FriendCode {
                    SharpProof.Verify.ProvenOutcome Proven() => new();
                    SharpProof.Verify.RefutedOutcome Refuted() => new();
                    SharpProof.Verify.ValidatedModel Model() => new();
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA011"),
            Is.EqualTo(3));
    }

    [Test]
    public async Task AllowsProofOutcomeConstructionInsideTheKernel()
    {
        const string source =
            """
            namespace SharpProof.Verify {
                public sealed class ProvenOutcome {
                    public ProvenOutcome() { }
                }
                public sealed class RefutedOutcome {
                    public RefutedOutcome(ValidatedModel model) { }
                }
                public sealed class ValidatedModel {
                    public ValidatedModel() { }
                }
                public sealed class ProofKernel {
                    ProvenOutcome Proven() => new();
                    RefutedOutcome Refuted() => new(new ValidatedModel());
                    ValidatedModel Model() => new();
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task AllowsTrustedEvidenceAndEffectConstruction()
    {
        const string source =
            """
            namespace SharpProof.Verify {
                public sealed class Assumption {
                    public Assumption() { }
                }
                public sealed class ProofKernel {
                    object M() => new Assumption();
                }
            }
            namespace SharpProof.Worker {
                public sealed class CallableVerifier {
                    object M() => new SharpProof.Verify.Assumption();
                }
                public static class PostconditionObligationBuilder {
                    static object M() => new SharpProof.Verify.Assumption();
                }
            }
            namespace SharpProof.Effects {
                public sealed class EffectSummary {
                    public EffectSummary() { }
                    static object M() => new EffectSummary();
                }
                public sealed class EffectSummaryDomain {
                    object M() => new EffectSummary();
                }
                public sealed class EffectSummaryOperations {
                    object M() => new EffectSummary();
                }
                public sealed class ExternalEffectResolver {
                    object M() => new EffectSummary();
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task RejectsDisplayStringsRegardlessOfProductionNamespace()
    {
        const string source =
            """
            using Microsoft.CodeAnalysis;
            namespace SharpProof.Tooling;
            static class C {
                static string M(ISymbol symbol) => symbol.ToDisplayString();
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SPMETA001"));
    }

    [Test]
    public async Task AllowsLookalikeSymbolTypesInsideSoundnessCriticalLayers()
    {
        const string source =
            """
            namespace Example {
                interface ISymbol {
                    string ToDisplayString();
                }
            }
            namespace SharpProof.Frontend {
                static class C {
                    static string M(Example.ISymbol symbol) =>
                        symbol.ToDisplayString();
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task AllowsOnlyTheResolvedGeneratedDescriptorCatalog()
    {
        const string source =
            """
            using Microsoft.CodeAnalysis;
            namespace SharpProof.Analyzer {
                static class GeneratedDiagnosticDescriptors {
                    static readonly DiagnosticDescriptor Rule = new(
                        "ID", "title", "message", "category",
                        DiagnosticSeverity.Info, true);
                }
            }
            namespace SharpProof.Analyzer.Nested {
                static class GeneratedDiagnosticDescriptors {
                    static readonly DiagnosticDescriptor Rule = new(
                        "ID", "title", "message", "category",
                        DiagnosticSeverity.Info, true);
                }
            }
            namespace SharpProof.ContractForGenerator {
                static class GeneratedDiagnosticDescriptors {
                    static readonly DiagnosticDescriptor Rule = new(
                        "ID", "title", "message", "category",
                        DiagnosticSeverity.Info, true);
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPMETA005"]));
    }

    [Test]
    public async Task AllowsOnlyTheResolvedMetaDescriptorCatalog()
    {
        const string source =
            """
            using Microsoft.CodeAnalysis;
            namespace SharpProof.Meta.Analyzers {
                static class MetaDiagnosticDescriptors {
                    static readonly DiagnosticDescriptor Rule = new(
                        "ID", "title", "message", "category",
                        DiagnosticSeverity.Info, true);
                }
                static class HandwrittenDescriptors {
                    static readonly DiagnosticDescriptor Rule = new(
                        "ID", "title", "message", "category",
                        DiagnosticSeverity.Info, true);
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPMETA005"]));
    }

    [Test]
    public async Task AllowsOnlyTheNamedSemanticModelHostAdapter()
    {
        const string source =
            """
            #pragma warning disable RSEXPERIMENTAL001
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.CSharp;
            namespace SharpProof.Frontend.Host;
            static class CompilationModelProvider {
                internal static SemanticModel Get(
                    Compilation compilation,
                    SyntaxTree tree) =>
                    compilation.GetSemanticModel(tree);

                internal static SemanticModel GetCSharp(
                    CSharpCompilation compilation,
                    SyntaxTree tree) =>
                    compilation.GetSemanticModel(
                        tree, default(SemanticModelOptions));
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [TestCase("SharpProof.Frontend.Host", "OtherProvider")]
    [TestCase("SharpProof.Analyzer.Host", "CompilationModelProvider")]
    public async Task RejectsSemanticModelCallsOutsideTheNamedHostAdapter(
        string namespaceName,
        string typeName)
    {
        var source =
            $$"""
            using Microsoft.CodeAnalysis;
            namespace {{namespaceName}};
            static class {{typeName}} {
                internal static SemanticModel Get(
                    Compilation compilation,
                    SyntaxTree tree) =>
                    compilation.GetSemanticModel(tree);
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SPMETA001"));
    }

    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Frontend;
        static class C {
            static SymbolInfo M(SemanticModel model, SyntaxNode node) =>
                model.GetSpeculativeSymbolInfo(
                    0, node, SpeculativeBindingOption.BindAsExpression);
        }
        """,
        "GetSpeculativeSymbolInfo")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Frontend;
        static class C {
            static TypeInfo M(SemanticModel model, SyntaxNode node) =>
                model.GetSpeculativeTypeInfo(
                    0, node, SpeculativeBindingOption.BindAsExpression);
        }
        """,
        "GetSpeculativeTypeInfo")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        namespace SharpProof.Frontend;
        static class C {
            static IAliasSymbol? M(SemanticModel model, SyntaxNode node) =>
                model.GetSpeculativeAliasInfo(
                    0, node, SpeculativeBindingOption.BindAsTypeOrNamespace);
        }
        """,
        "GetSpeculativeAliasInfo")]
    [TestCase(
        """
        #pragma warning disable RSEXPERIMENTAL001
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp;
        namespace SharpProof.Frontend;
        static class C {
            static SemanticModel M(
                CSharpCompilation compilation,
                SyntaxTree tree) =>
                compilation.GetSemanticModel(
                    tree, default(SemanticModelOptions));
        }
        """,
        "GetSemanticModel")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        namespace SharpProof.Frontend;
        static class C {
            static bool M(
                SemanticModel model,
                StatementSyntax statement,
                out SemanticModel speculative) =>
                Microsoft.CodeAnalysis.CSharp.CSharpExtensions
                    .TryGetSpeculativeSemanticModel(
                    model, 0, statement, out speculative);
        }
        """,
        "TryGetSpeculativeSemanticModel")]
    [TestCase(
        """
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.CSharp;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        namespace SharpProof.Frontend;
        static class C {
            static bool M(
                SemanticModel model,
                BaseMethodDeclarationSyntax declaration,
                out SemanticModel speculative) =>
                Microsoft.CodeAnalysis.CSharp.CSharpExtensions
                    .TryGetSpeculativeSemanticModelForMethodBody(
                    model, 0, declaration, out speculative);
        }
        """,
        "TryGetSpeculativeSemanticModelForMethodBody")]
    public async Task RejectsEverySpeculativeSemanticApiVariant(
        string source,
        string methodName)
    {
        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics
                .Where(static diagnostic => diagnostic.Id == "SPMETA001")
                .Select(static diagnostic =>
                    diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo([
                $"API '{methodName}' is forbidden in " +
                "soundness-critical SharpProof layers"
            ]));
    }

    [Test]
    public void ForbiddenCatalogIncludesInternalCSharpSpeculativeVariants()
    {
        var analyzer = typeof(SharpProofSoundnessAnalyzer);
        var knownTypeNames = (ImmutableArray<string>)analyzer.GetField(
                "KnownTypeNames",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        var forbidden = (System.Collections.IEnumerable)analyzer.GetField(
                "ForbiddenMethods",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        var entry = forbidden.Cast<object>().SingleOrDefault(item =>
            string.Equals(
                item.GetType().GetProperty("Key")!.GetValue(item)!.ToString(),
                "CSharpSemanticModel",
                StringComparison.Ordinal));
        IEnumerable<string> methods = entry == null
            ? []
            : (IEnumerable<string>)entry.GetType()
                .GetProperty("Value")!
                .GetValue(entry)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry, Is.Not.Null);
            Assert.That(
                knownTypeNames,
                Does.Contain(
                    "Microsoft.CodeAnalysis.CSharp.CSharpSemanticModel"));
            Assert.That(
                methods,
                Is.SupersetOf([
                    "TryGetSpeculativeSemanticModel",
                    "TryGetSpeculativeSemanticModelForMethodBody"
                ]));
        }
    }

    [Test]
    public void KnownTypeCatalogMatchesEnumAndResolvesEveryEntry()
    {
        var analyzer = typeof(SharpProofSoundnessAnalyzer);
        var knownTypeNames = (ImmutableArray<string>)analyzer.GetField(
                "KnownTypeNames",
                System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        var knownType = analyzer.GetNestedType(
            "KnownType",
            System.Reflection.BindingFlags.NonPublic)!;

        Assert.That(knownTypeNames, Is.Unique);
        Assert.That(
            knownTypeNames.All(static name => !string.IsNullOrWhiteSpace(name)),
            Is.True);
        Assert.That(
            knownTypeNames.Length,
            Is.EqualTo(Enum.GetNames(knownType).Length));

        var stubTrees = knownTypeNames
            .Where(static name => !name.StartsWith(
                "Microsoft.",
                StringComparison.Ordinal) && !name.StartsWith(
                "System.",
                StringComparison.Ordinal))
            .Select(static name =>
            {
                var parts = name.Split('.');
                var namespaceName = string.Join('.', parts, 0, parts.Length - 1);
                return CSharpSyntaxTree.ParseText(
                    $"namespace {namespaceName} {{ public class {parts[^1]} {{ }} }}");
            })
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "KnownTypeCatalog",
            stubTrees,
            PlatformReferences);
        var knownSymbolsType = analyzer.GetNestedType(
            "KnownSymbols",
            System.Reflection.BindingFlags.NonPublic)!;
        var knownSymbols = Activator.CreateInstance(
            knownSymbolsType,
            System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [compilation],
            culture: null)!;
        var indexer = knownSymbolsType.GetProperty(
            "Item",
            System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic)!;
        var unresolved = Enum.GetValues(knownType)
            .Cast<object>()
            .Where(value => indexer.GetValue(knownSymbols, [value]) is null)
            .Select(value => value.ToString())
            .ToArray();

        Assert.That(
            unresolved,
            Is.Empty,
            "KnownType entries must resolve in the analyzer compilation.");
    }

    [Test]
    public async Task AuditsCancellationCatchFilterSemantics()
    {
        const string source =
            """
            using System;
            namespace SharpProof.Verify;

            sealed class CustomCancellationException :
                OperationCanceledException { }

            static class C
            {
                static void BareExhaustive()
                {
                    try { }
                    catch when (true) { throw; }
                    catch (Exception) { }
                }

                static void BareSelective(bool include)
                {
                    try { }
                    catch when (include) { throw; }
                    catch (Exception) { }
                }

                static void WrongTypeTestLocal()
                {
                    Exception other = new Exception();
                    try { }
                    catch (Exception caught)
                        when (other is OperationCanceledException) { throw; }
                    catch (Exception) { }
                }

                static void WrongPatternTestLocal()
                {
                    Exception other = new Exception();
                    try { }
                    catch (Exception caught)
                        when (other is
                            OperationCanceledException or ArgumentException)
                    {
                        throw;
                    }
                    catch (Exception) { }
                }

                static void ExhaustiveOrLeft()
                {
                    try { }
                    catch (Exception caught)
                        when (caught is
                            OperationCanceledException or ArgumentException)
                    {
                        throw;
                    }
                    catch (Exception) { }
                }

                static void ExhaustiveOrRight()
                {
                    try { }
                    catch (Exception caught)
                        when (caught is
                            ArgumentException or OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception) { }
                }

                static void ExhaustiveAnd()
                {
                    try { }
                    catch (Exception caught)
                        when (caught is
                            OperationCanceledException and Exception)
                    {
                        throw;
                    }
                    catch (Exception) { }
                }

                static void ExhaustiveNonNullPattern()
                {
                    try { }
                    catch (Exception caught)
                        when (caught is not null) { throw; }
                    catch (Exception) { }
                }

                static void ParenthesizedTypeTest()
                {
                    try { }
                    catch (Exception caught)
                        when (caught is (OperationCanceledException))
                    {
                        throw;
                    }
                    catch (Exception) { }
                }

                static void ConvertedTypeTest()
                {
                    try { }
                    catch (Exception caught)
                        when (((object)caught) is OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception) { }
                }

                static void ExcludesExactCaughtType()
                {
                    try { }
                    catch (CustomCancellationException caught)
                        when (caught is not CustomCancellationException) { }
                }

                static void ExcludesParenthesizedPattern()
                {
                    try { }
                    catch (Exception caught)
                        when (caught is (not OperationCanceledException)) { }
                }

                static void DoesNotExcludeCancellation()
                {
                    try { }
                    catch (Exception caught)
                        when (caught is not ArgumentException) { }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        var cancellationDiagnostics = diagnostics
            .Where(static diagnostic => diagnostic.Id == "SPMETA003")
            .ToArray();
        Assert.That(
            cancellationDiagnostics,
            Has.Length.EqualTo(4),
            string.Join(
                ", ",
                cancellationDiagnostics.Select(static diagnostic =>
                    diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1)));
    }

    [Test]
    public async Task ThrowingPropertyPatternDoesNotHandleCancellationExhaustively()
    {
        const string source =
            """
            using System;
            namespace SharpProof.Verify;

            sealed class CustomCancellationException :
                OperationCanceledException
            {
                public bool Throws => throw new Exception();
            }

            static class C
            {
                static void Call()
                {
                    try { }
                    catch (Exception caught)
                        when (caught is
                            CustomCancellationException { Throws: true }
                            or OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception) { }
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SPMETA003"]));
    }

    [Test]
    public async Task AuditedWorkerReificationHandlesConversionsAndBlocksExactly()
    {
        const string source =
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace SharpProof.Worker.Protocol
            {
                enum WorkerClaimReason
                {
                    ProjectTimeout,
                    Canceled
                }

                enum WorkerCallableCoverageReason
                {
                    ProjectTimeout,
                    Canceled
                }
            }

            namespace SharpProof.Worker
            {
                using SharpProof.Worker.Protocol;

                sealed class CallableVerificationResult { }

                static class CallableVerificationPolicy
                {
                    private static CallableVerificationResult Unknown(
                        object target,
                        WorkerClaimReason claimReason,
                        WorkerCallableCoverageReason callableReason) =>
                        new();

                    private static CallableVerificationResult Unknown(
                        object target,
                        int claimReason,
                        int callableReason) =>
                        new();

                    private static async Task<CallableVerificationResult>
                        VerifyTargetAsync(
                            object verifier,
                            string target,
                            object budgets,
                            object parallelism,
                            object resourceGate,
                            object projectBoundary,
                            CancellationToken callerCancellation)
                    {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException)
                        {
                            if (callerCancellation.IsCancellationRequested)
                            {
                                return Unknown(
                                    target,
                                    (WorkerClaimReason)(int)
                                        WorkerClaimReason.Canceled,
                                    (WorkerCallableCoverageReason)(int)
                                        WorkerCallableCoverageReason.Canceled);
                            }

                            return Unknown(
                                target,
                                WorkerClaimReason.ProjectTimeout,
                                WorkerCallableCoverageReason.ProjectTimeout);
                        }
                    }

                    private static async Task<CallableVerificationResult>
                        VerifyTargetAsync(
                            string verifier,
                            object target,
                            object budgets,
                            object parallelism,
                            object resourceGate,
                            object projectBoundary,
                            CancellationToken callerCancellation)
                    {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException)
                        {
                            if (callerCancellation.IsCancellationRequested)
                            {
                                _ = target;
                                return Unknown(
                                    target,
                                    WorkerClaimReason.Canceled,
                                    WorkerCallableCoverageReason.Canceled);
                            }

                            return Unknown(
                                target,
                                WorkerClaimReason.ProjectTimeout,
                                WorkerCallableCoverageReason.ProjectTimeout);
                        }
                    }

                    private static async Task<CallableVerificationResult>
                        VerifyTargetAsync(
                            int verifier,
                            object target,
                            object budgets,
                            object parallelism,
                            object resourceGate,
                            object projectBoundary,
                            CancellationToken callerCancellation)
                    {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException)
                        {
                            if (callerCancellation.IsCancellationRequested)
                                return Unknown(target, 0, 0);

                            return Unknown(
                                target,
                                WorkerClaimReason.ProjectTimeout,
                                WorkerCallableCoverageReason.ProjectTimeout);
                        }
                    }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Count(static diagnostic =>
                diagnostic.Id == "SPMETA003"),
            Is.EqualTo(2));
    }

    private static void AssertSemanticCacheDiagnostics(
        ImmutableArray<Diagnostic> diagnostics,
        int expectedCount)
    {
        var actualIds = diagnostics
            .Select(static diagnostic => diagnostic.Id)
            .ToArray();
        var expectedIds = Enumerable
            .Repeat("SPMETA010", expectedCount)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actualIds, Is.EqualTo(expectedIds));
            Assert.That(
                diagnostics.All(static diagnostic =>
                    diagnostic.Location.IsInSource),
                Is.True);
        }
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source)
    {
        return await AnalyzeCore(source, "MetaAnalyzerTest");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeGenerated(
        string source)
    {
        return await AnalyzeCore(
            source,
            "MetaAnalyzerGeneratedTest",
            "Generated.g.cs");
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeCore(
        string source,
        string assemblyName,
        string? path = null)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp12),
                path: path ?? string.Empty)],
            PlatformReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var compilerErrors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(compilerErrors, Is.Empty);

        return await compilation
            .WithAnalyzers([new SharpProofSoundnessAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<TestCaseData>
        CSharpExpressionConstructionCases()
    {
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static string M(string name) =>
                    $"({name}) is not null";
            }
            """).SetName("InterpolatedExpressionTextIsRejected");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static string M() => $"({42}) is not null";
            }
            """).SetName("ConstantInterpolationExpressionTextIsRejected");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static string M(int value) =>
                    $"({value:D}) is not null";
            }
            """).SetName("FormattedInterpolationExpressionTextIsRejected");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static string M(int value) =>
                    $"({value,10}) is not null";
            }
            """).SetName("AlignedInterpolationExpressionTextIsRejected");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static string M(string name) =>
                    $"\"{name}\" is not null";
            }
            """).SetName("EscapedInterpolationExpressionTextIsRejected");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static string M(string name) =>
                    "(" + name + ") is not null";
            }
            """).SetName("ConcatenatedExpressionTextRemainsRejected");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static string M(string name) {
                    var result = "(" + name + ")";
                    result += " is not null";
                    return result;
                }
            }
            """).SetName("CompoundAssignedExpressionTextIsRejected");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static string M(string name) =>
                    string.Concat("(", name, ") is not null");
            }
            """).SetName("StringConcatExpressionTextIsRejected");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static string M(string name) =>
                    "(" + name + ") is" + " not null";
            }
            """).SetName("SplitExpressionTextIsRejected");
    }

    private static IEnumerable<TestCaseData> SemanticPatternControlFlowCases()
    {
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static bool M(string reason) =>
                    reason is "ir_condition_both_branches_feasible";
            }
            """).SetName("SemanticIsConstantPatternIsRejectedOnce");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static bool M(string reason) {
                    switch (reason) {
                        case "ir_condition_both_branches_feasible":
                            return true;
                        default:
                            return false;
                    }
                }
            }
            """).SetName("SemanticSwitchStatementCaseIsRejectedOnce");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static bool M(string reason) => reason switch {
                    "ir_condition_both_branches_feasible" => true,
                    _ => false
                };
            }
            """).SetName("SemanticSwitchExpressionArmIsRejectedOnce");
        yield return new TestCaseData(
            """
            namespace SharpProof.Frontend;
            static class C {
                internal static bool M(string reason) =>
                    (reason is ("ir_condition_both_branches_feasible"))
                        switch {
                            true => true,
                            _ => false
                        };
            }
            """).SetName("NestedSemanticPatternIsRejectedOnce");
    }

    private static ImmutableArray<MetadataReference> CreatePlatformReferences()
    {
        var trustedAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return [.. trustedAssemblies
            .Split(Path.PathSeparator)
            .Append(typeof(Compilation).Assembly.Location)
            .Append(typeof(CSharpCompilation).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path))];
    }
}
