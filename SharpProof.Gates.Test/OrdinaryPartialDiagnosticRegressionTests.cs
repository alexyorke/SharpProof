using System.Runtime.Loader;
using System.Text;
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
public sealed class OrdinaryPartialDiagnosticRegressionTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["SP0002", "SP0045"];

    private const string Source = """
        public static partial class PartialDiagnostics {
            private static object? State;
            public static partial int Target(int value);
            [SharpProof.Attributes.ZeroAllocations]
            public static partial int Target(int value) {
                State = new object();
                return value;
            }
        }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task BothActualPartialSelectionsPreserveNativeRefutationAndDiagnostics(bool selectImplementation)
    {
        var root = CSharpSyntaxTree.ParseText(Source).GetCompilationUnitRoot();
        var declarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        Assert.That(declarations, Has.Length.EqualTo(2));
        var definition = declarations.Single(static method => method.Body == null && method.ExpressionBody == null);
        var implementation = declarations.Single(static method => method.Body != null || method.ExpressionBody != null);
        var selected = selectImplementation ? implementation : definition;
        var location = selected.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-partial-diagnostics.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(Source), Source);
        var method = new OpenSourceCorpusMethod("ordinary-partial-diagnostics", file.SourceId, file.Path,
            location.StartLinePosition.Line + 1, location.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(selected)),
            selected.Identifier.ValueText, "effects", CorpusVerdict.Refuted, CorpusSupport.Supported);
        var index = OpenSourceCorpusCatalog.BuildDeclarationIndex(root);
        Assert.That(OpenSourceCorpusCatalog.FindDeclaration(index, method), Is.SameAs(selected));
        var document = new OpenSourceCorpusDocument(2, [], [file], [method]);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None, purity: true);
        Assert.That(compilation.GetDiagnostics().Where(static item => item.Severity == DiagnosticSeverity.Error), Is.Empty);
        var preparedTree = compilation.SyntaxTrees.Single(static tree => tree.FilePath == "ordinary-partial-diagnostics.cs");
        var preparedMethods = (await preparedTree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var preparedDefinition = preparedMethods.Single(static item => item.Body == null && item.ExpressionBody == null);
        var preparedImplementation = preparedMethods.Single(static item => item.Body != null || item.ExpressionBody != null);
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
        Assert.That(target.EffectClaims, Has.Length.EqualTo(2));
        Assert.That(target.EffectClaims.Select(static claim => claim.Entry.EffectContractKind), Has.Member(WorkerEffectContractKind.EnforcePure));
        Assert.That(target.EffectClaims.Select(static claim => claim.Entry.EffectContractKind), Has.Member(WorkerEffectContractKind.ZeroAllocations));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("OrdinaryPartialDiagnostics", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var invoke = assembly.GetType("PartialDiagnostics", throwOnError: true)!.GetMethod("Target")!.CreateDelegate<Func<int, int>>();
            Assert.That(invoke(-7), Is.EqualTo(-7));
            Assert.That(invoke(0), Is.Zero);
            Assert.That(invoke(int.MaxValue), Is.EqualTo(int.MaxValue));
        }
        finally
        {
            context.Unload();
        }
        var analysis = await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(compilation, "effects", concurrentAnalysis: true);
        Assert.That(analysis.Diagnostics, Has.Length.EqualTo(2));
        Assert.That(analysis.Diagnostics.Select(static item => item.Id).OrderBy(static id => id, StringComparer.Ordinal), Is.EqualTo(ExpectedDiagnosticIds));
        Assert.That(analysis.SemanticOutcomes, Has.Length.EqualTo(1));
        Assert.That(analysis.SemanticOutcomes.Single().Outcome, Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        var native = await NativeCorpusVerifier.VerifyAsync(compilation, CancellationToken.None);
        Assert.That(native, Has.Length.EqualTo(1));
        Assert.That(native.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(native.Single().Declaration, Is.SameAs(preparedImplementation));
        var observation = (await OpenSourceCorpusRunner.ObserveAsync(document, CancellationToken.None)).Single();
        Assert.That(observation.CaseId, Is.EqualTo(method.Id + ".baseline"));
        Assert.That(observation.SemanticOutcome, Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        Assert.That(observation.Verdict, Is.EqualTo(CorpusVerdict.Refuted));
        Assert.That(observation.Diagnostics, Has.Length.EqualTo(2));
        Assert.That(observation.Diagnostics.Select(static item => item.Split('@')[0]), Is.EqualTo(ExpectedDiagnosticIds));
        Assert.That(observation.Diagnostics,
            Is.EqualTo(observation.Diagnostics.OrderBy(static item => item, StringComparer.Ordinal)));
        foreach (var diagnostic in observation.Diagnostics)
        {
            var fields = diagnostic.Split('@');
            Assert.That(fields, Has.Length.EqualTo(4));
            Assert.That(fields[1], Is.EqualTo("Warning"));
            Assert.That(fields[2], Does.StartWith(file.Path + ":"));
            Assert.That(Encoding.UTF8.GetString(Convert.FromBase64String(fields[3])), Does.Contain("Target"));
        }
    }
}
