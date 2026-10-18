using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Host;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinarySummaryUnsupportedReportTests
{
    [TestCase("public static object State; public static void Target(object value) { State = value; }", "UnsupportedWriteFieldType")]
    [TestCase("public int Value; public static void Target([global::SharpProof.Attributes.NotNull] SummaryUnsupported value) { value.Value = 1; }", "UnsupportedWriteTarget")]
    [TestCase("public static void Target([global::SharpProof.Attributes.NotNull] int[] values) { global::SharpProof.Attributes.Contract.Requires(values.Length > 0); values[0] = 1; }", "UnsupportedWriteTarget")]
    public async Task ActualUnsupportedWriteReportsAnUnperformedRuntimeGap(string member, string expectedRuntime)
    {
        System.ArgumentNullException.ThrowIfNull(expectedRuntime);
        System.ArgumentNullException.ThrowIfNull(member);
        var compilation = await PrepareAsync("public class SummaryUnsupported { " + member + " }");
        var evidence = await EvidenceAsync(compilation);
        Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(evidence.WriteWitness, Is.Not.Null);
        Assert.That(evidence.ExceptionWitness, Is.Null);
        Assert.That(evidence.AllocationWitness, Is.Null);
        Assert.That(evidence.LockWitness, Is.Null);

        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
            "unsupported-summary-write", 1, summary: true, cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo(expectedRuntime));
        Assert.That(report.Rows.Single().RuntimeChecks, Is.Zero);
        Assert.That(report.RuntimeWitnesses, Is.Zero);
        Assert.That(report.RuntimeContradictions, Is.Zero);
        Assert.That(report.Passed, Is.True);
    }

    [Test]
    public async Task ActualSummaryLockReportsAnUnperformedRuntimeGap()
    {
        var compilation = await PrepareAsync("public class SummaryUnsupported { public static void Target([global::SharpProof.Attributes.NotNull] object gate) { lock (gate) { } } }");
        var evidence = await EvidenceAsync(compilation);
        Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(evidence.LockWitness, Is.Not.Null);
        Assert.That(evidence.ExceptionWitness, Is.Null);
        Assert.That(evidence.AllocationWitness, Is.Null);
        Assert.That(evidence.WriteWitness, Is.Null);

        var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
            "unsupported-summary-lock", 1, summary: true, cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("LockOracleNotRun"));
        Assert.That(report.Rows.Single().RuntimeChecks, Is.Zero);
        Assert.That(report.RuntimeWitnesses, Is.Zero);
        Assert.That(report.RuntimeContradictions, Is.Zero);
        Assert.That(report.Passed, Is.True);
    }

    private static async Task<PassiveCallableCheckResult> EvidenceAsync(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var treeRoot = await tree.GetRootAsync();
            foreach (var invocation in treeRoot.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.Expression.ToString() == "global::SharpProof.Attributes.Contract.Requires")))
            {
                Assert.That(((CSharpParseOptions)tree.Options).PreprocessorSymbolNames,
                    Does.Contain(SharpProof.Attributes.Contract.ConditionalSymbol));
                var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree);
                var method = (IMethodSymbol)model.GetSymbolInfo(invocation).Symbol!;
                Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingType,
                    compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")), Is.True);
                var clause = preparation.Total!.Clauses.Single(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Requires &&
                    preparation.Total.Program.Factory.GetOperationInfo(clause.Operation).SourceSpan is { } source &&
                    source.Document == Path.GetFullPath(tree.FilePath) && source.Start == invocation.SpanStart &&
                    source.Length == invocation.Span.Length));
                Assert.That(preparation.Entry.Assumptions.Any(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.Precondition &&
                    assumption.Id == clause.AssumptionId)), Is.True);
            }
        }
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var evidence = await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        await TestContext.Progress.WriteLineAsync($"Unsupported native: outcome={evidence.Outcome?.GetType().Name ?? "null"}; reason={evidence.Reason}; total={preparation.Total != null}; complete={preparation.Total?.EffectsCompleteAtEntry}; abstraction={preparation.Total?.IsBodyAbstraction}; inputKinds={string.Join(",", evidence.EntryModel.Values.Select(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Kind)))}");
        return evidence;
    }

    private static async Task<CSharpCompilation> PrepareAsync(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var root = await CSharpSyntaxTree.ParseText(source).GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "unsupported-summary.cs", OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", file.SourceId, file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]), TestContext.CurrentContext.CancellationToken, summary: true);
        var trees = new List<SyntaxTree>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var treeRoot = await tree.GetRootAsync();
            trees.Add(tree.WithRootAndOptions(treeRoot,
                ((CSharpParseOptions)tree.Options).WithPreprocessorSymbols(((CSharpParseOptions)tree.Options).PreprocessorSymbolNames
                    .Append(SharpProof.Attributes.Contract.ConditionalSymbol))));
        }
        compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(trees);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        return compilation;
    }
}
