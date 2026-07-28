using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Meta.Analyzers;

namespace SharpProof.Meta.Analyzers.Test;

[TestFixture]
public sealed class SharpProofSoundnessAnalyzerTests {
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
    public async Task ReportsSoundnessBoundaryViolation(string source, string expectedId) {
        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain(expectedId));
    }

    [Test]
    public async Task AllowsImmutableStateAndCancellationRethrow() {
        const string source =
            """
            using System;
            namespace SharpProof.Verify;
            enum Status { Unknown, Proven }
            static class C {
                private static readonly object Gate = new();
                static void M() {
                    try { }
                    catch (OperationCanceledException) { throw; }
                }
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics, Is.Empty);
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
        string source) {
        var diagnostics = await Analyze(source);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SPMETA003"));
    }

    [Test]
    public async Task AllowsImmediateRethrowAndAuditedWorkerBoundaries() {
        const string source =
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SharpProof.Worker {
                static class Program {
                    internal static async Task<int> Main(string[] args) {
                        await Task.Yield();
                        try { throw new OperationCanceledException(); }
                        catch (OperationCanceledException) { return 4; }
                    }
                }

                sealed class SharpProofWorker {
                    private sealed class CallableVerificationResult { }
                    private static async Task<CallableVerificationResult>
                        VerifyTargetAsync(
                            object verifier,
                            object target,
                            object budgets,
                            object parallelism,
                            object resourceGate,
                            object resourceCount,
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
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task AllowsExactWorkerVerifyAsyncCancellationBoundary() {
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
        Assert.That(diagnostics, Is.Empty);
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
        string methodSignature) {
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

    [Test]
    public async Task AllowsAuditedWorkerTypedCancellationReification() {
        const string source =
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SharpProof.Worker.Protocol {
                enum WorkerClaimReason { ProjectTimeout, Canceled }
                enum WorkerCallableCoverageReason { ProjectTimeout, Canceled }
            }
            namespace SharpProof.Worker {
                using SharpProof.Worker.Protocol;
                sealed class SharpProofWorker {
                    private sealed class CallableVerificationResult { }
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
                            object resourceCount,
                            object projectBoundary,
                            CancellationToken callerCancellation) {
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
        Assert.That(diagnostics, Is.Empty);
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
        string unknownHelper) {
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
                sealed class SharpProofWorker {
                    private sealed class CallableVerificationResult { }
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
                            object resourceCount,
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
    public async Task AuditedWorkerTimeoutBoundaryMustGuardCallerCancellation() {
        const string source =
            """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            namespace SharpProof.Worker {
                sealed class SharpProofWorker {
                    private sealed class CallableVerificationResult { }
                    private static async Task<CallableVerificationResult>
                        VerifyTargetAsync(
                            object verifier,
                            object target,
                            object budgets,
                            object parallelism,
                            object resourceGate,
                            object resourceCount,
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
    public async Task RejectsTargetTypedProofOutcomesOutsideTheKernel() {
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
    public async Task AllowsProofOutcomeConstructionInsideTheKernel() {
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
    public async Task AllowsTrustedEvidenceAndEffectConstruction() {
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
    public async Task AllowsDisplayStringsOutsideSoundnessCriticalLayers() {
        const string source =
            """
            using Microsoft.CodeAnalysis;
            namespace SharpProof.Tooling;
            static class C {
                static string M(ISymbol symbol) => symbol.ToDisplayString();
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task AllowsLookalikeSymbolTypesInsideSoundnessCriticalLayers() {
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
    public async Task AllowsOnlyTheResolvedGeneratedDescriptorCatalog() {
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
    public async Task AllowsOnlyTheNamedSemanticModelHostAdapter() {
        const string source =
            """
            using Microsoft.CodeAnalysis;
            namespace SharpProof.Frontend.Host;
            static class CompilationModelProvider {
                internal static SemanticModel Get(
                    Compilation compilation,
                    SyntaxTree tree) =>
                    compilation.GetSemanticModel(tree);
            }
            """;

        var diagnostics = await Analyze(source);
        Assert.That(diagnostics, Is.Empty);
    }

    [TestCase("SharpProof.Frontend.Host", "OtherProvider")]
    [TestCase("SharpProof.Analyzer.Host", "CompilationModelProvider")]
    public async Task RejectsSemanticModelCallsOutsideTheNamedHostAdapter(
        string namespaceName,
        string typeName) {
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

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source) {
        var compilation = CSharpCompilation.Create(
            "MetaAnalyzerTest",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12))],
            PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var compilerErrors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(compilerErrors, Is.Empty);

        return await compilation
            .WithAnalyzers([new SharpProofSoundnessAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> PlatformReferences() {
        var trustedAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return trustedAssemblies
            .Split(Path.PathSeparator)
            .Append(typeof(Compilation).Assembly.Location)
            .Append(typeof(CSharpCompilation).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path));
    }
}
