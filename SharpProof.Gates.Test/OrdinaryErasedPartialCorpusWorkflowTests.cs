using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryErasedPartialCorpusWorkflowTests
{
    private const string Source = """
        public static partial class ErasedPartial {
            static partial void Optional();
            public static int Control(int value) => value;
        }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualErasedPartialRetainsCompletenessFailureAndCallableControlProves(bool selectControl)
    {
        var root = CSharpSyntaxTree.ParseText(Source).GetCompilationUnitRoot();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : ((item.ExpressionBody != null) == selectControl));
        var location = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-erased-partial.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(Source), Source);
        var method = new OpenSourceCorpusMethod("ordinary-erased-partial-" + (selectControl ? "control" : "optional"),
            file.SourceId, file.Path, location.StartLinePosition.Line + 1, location.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            declaration.Identifier.ValueText, "effects", selectControl ? CorpusVerdict.Proven : CorpusVerdict.Unknown,
            selectControl ? CorpusSupport.Supported : CorpusSupport.IntentionallyUnsupported);
        var document = new OpenSourceCorpusDocument(2, [], [file], [method]);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None, purity: true);
        Assert.That(compilation.GetDiagnostics().Where(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var preparedTree = compilation.SyntaxTrees.Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.FilePath == file.Path));
        var preparedRoot = await preparedTree.GetRootAsync();
        var selectedDeclaration = preparedRoot.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.Identifier.ValueText == method.MethodName));
        var selectedSymbol = compilation.GetSemanticModel(preparedTree).GetDeclaredSymbol(selectedDeclaration)!;
        Assert.That(selectedSymbol.IsPartialDefinition, Is.EqualTo(!selectControl));
        Assert.That(selectedSymbol.IsAbstract, Is.False);
        Assert.That(selectedSymbol.IsExtern, Is.False);
        Assert.That(selectedSymbol.PartialDefinitionPart, Is.Null);
        Assert.That(selectedSymbol.PartialImplementationPart, Is.Null);
        Assert.That(OpenSourceCorpusRunner.CorpusMethodId(selectedDeclaration), Is.EqualTo(method.Id));
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All).Build();
        var target = discovery.Targets.Values.Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(target.Method, selectedSymbol), Is.True);
        Assert.That(target.Declaration, Is.SameAs(selectedDeclaration));
        Assert.That(target.EffectClaims, Has.Length.EqualTo(1));
        Assert.That(target.EffectClaims.Single().Entry.EffectContractKind, Is.EqualTo(WorkerEffectContractKind.EnforcePure));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("OrdinaryErasedPartialCorpus", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var type = assembly.GetType("ErasedPartial", throwOnError: true)!;
            Assert.That(type.GetMethod("Optional", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic), Is.Null);
            var invoke = type.GetMethod("Control")!.CreateDelegate<Func<int, int>>();
            Assert.That(invoke(-7), Is.EqualTo(-7));
            Assert.That(invoke(0), Is.Zero);
            Assert.That(invoke(int.MaxValue), Is.EqualTo(int.MaxValue));
        }
        finally
        {
            context.Unload();
        }
        var analysis = await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(compilation, "effects", concurrentAnalysis: true);
        Assert.That(analysis.Diagnostics, Is.Empty);
        Assert.That(analysis.SemanticOutcomes, Has.Length.EqualTo(1));
        Assert.That(analysis.SemanticOutcomes.Single().Accessibility, Is.EqualTo(Accessibility.Public));
        Assert.That(analysis.SemanticOutcomes.Single().Outcome,
            Is.EqualTo(selectControl ? AnalyzerSemanticOutcome.Unknown : AnalyzerSemanticOutcome.NotApplicable));
        var native = await NativeCorpusVerifier.VerifyAsync(compilation, CancellationToken.None);
        Assert.That(native, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(native.Single().Method, selectedSymbol), Is.True);
        Assert.That(native.Single().Declaration, Is.SameAs(selectedDeclaration));
        Assert.That(native.Single().Outcome, Is.EqualTo(selectControl ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Unknown));
        if (!selectControl)
        {
            var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await OpenSourceCorpusRunner.ObserveAsync(document, CancellationToken.None)));
            Assert.That(error!.Message, Is.EqualTo("Analyzer did not record an outcome for OSS method " + method.Id + "."));
            return;
        }
        var observation = (await OpenSourceCorpusRunner.ObserveAsync(document, CancellationToken.None)).Single();
        Assert.That(observation.CaseId, Is.EqualTo(method.Id + ".baseline"));
        Assert.That(observation.SemanticOutcome, Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        Assert.That(observation.Verdict, Is.EqualTo(CorpusVerdict.Proven));
        Assert.That(observation.Diagnostics, Is.Empty);
    }
}
