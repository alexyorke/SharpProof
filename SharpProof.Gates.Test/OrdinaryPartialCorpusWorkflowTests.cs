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
public sealed class OrdinaryPartialCorpusWorkflowTests
{
    private const string Source = """
        public static partial class PartialScalar {
            public static partial int Target(int value);
            public static partial int Target(int value) => value;
        }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualPartialDeclarationSelectionUsesTheRealImplementationOutcome(bool selectImplementation)
    {
        var root = CSharpSyntaxTree.ParseText(Source).GetCompilationUnitRoot();
        var declarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        Assert.That(declarations, Has.Length.EqualTo(2));
        var definition = declarations.Single(static method => method.ExpressionBody == null);
        var implementation = declarations.Single(static method => method.ExpressionBody != null);
        var selected = selectImplementation ? implementation : definition;
        var location = selected.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-partial-scalar.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(Source), Source);
        var method = new OpenSourceCorpusMethod("ordinary-partial-scalar", file.SourceId, file.Path,
            location.StartLinePosition.Line + 1, location.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(selected)),
            selected.Identifier.ValueText, "effects", CorpusVerdict.Proven, CorpusSupport.Supported);
        var index = OpenSourceCorpusCatalog.BuildDeclarationIndex(root);
        Assert.That(OpenSourceCorpusCatalog.FindDeclaration(index, method), Is.SameAs(selected));
        var document = new OpenSourceCorpusDocument(2, [], [file], [method]);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None, purity: true);
        Assert.That(compilation.GetDiagnostics().Where(static item => item.Severity == DiagnosticSeverity.Error), Is.Empty);
        var preparedTree = compilation.SyntaxTrees.Single(static tree => tree.FilePath == "ordinary-partial-scalar.cs");
        var preparedMethods = (await preparedTree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var preparedDefinition = preparedMethods.Single(static item => item.ExpressionBody == null);
        var preparedImplementation = preparedMethods.Single(static item => item.ExpressionBody != null);
        var model = compilation.GetSemanticModel(preparedTree);
        var definitionSymbol = model.GetDeclaredSymbol(preparedDefinition)!;
        var implementationSymbol = model.GetDeclaredSymbol(preparedImplementation)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(definitionSymbol.PartialImplementationPart, implementationSymbol), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(implementationSymbol.PartialDefinitionPart, definitionSymbol), Is.True);
        Assert.That(OpenSourceCorpusRunner.CorpusMethodId(selectImplementation ? preparedImplementation : preparedDefinition),
            Is.EqualTo(method.Id));
        Assert.That(OpenSourceCorpusRunner.CorpusMethodId(selectImplementation ? preparedDefinition : preparedImplementation), Is.Null);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All).Build();
        var target = discovery.Targets.Values.Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(target.Method, implementationSymbol), Is.True);
        Assert.That(target.Declaration, Is.SameAs(preparedImplementation));
        Assert.That(target.EffectClaims, Has.Length.EqualTo(1));
        Assert.That(target.EffectClaims.Single().Entry.EffectContractKind, Is.EqualTo(WorkerEffectContractKind.EnforcePure));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("OrdinaryPartialScalar", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var invoke = assembly.GetType("PartialScalar", throwOnError: true)!.GetMethod("Target")!.CreateDelegate<Func<int, int>>();
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
        Assert.That(analysis.SemanticOutcomes.Single().Outcome, Is.EqualTo(AnalyzerSemanticOutcome.Proven));
        var native = await NativeCorpusVerifier.VerifyAsync(compilation, CancellationToken.None);
        Assert.That(native, Has.Length.EqualTo(1));
        Assert.That(native.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(native.Single().Declaration, Is.SameAs(preparedImplementation));
        if (selectImplementation)
        {
            var observation = (await OpenSourceCorpusRunner.ObserveAsync(document, CancellationToken.None)).Single();
            Assert.That(observation.CaseId, Is.EqualTo(method.Id + ".baseline"));
            Assert.That(observation.SemanticOutcome, Is.EqualTo(AnalyzerSemanticOutcome.Proven));
            Assert.That(observation.Verdict, Is.EqualTo(CorpusVerdict.Proven));
            Assert.That(observation.Diagnostics, Is.Empty);
        }
        else
        {
            var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                await OpenSourceCorpusRunner.ObserveAsync(document, CancellationToken.None)));
            Assert.That(error!.Message, Is.EqualTo("Analyzer did not record an outcome for OSS method ordinary-partial-scalar."));
        }
    }
}
