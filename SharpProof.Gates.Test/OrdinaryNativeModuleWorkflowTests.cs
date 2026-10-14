using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryNativeModuleWorkflowTests
{
    [TestCase(OutputKind.DynamicallyLinkedLibrary)]
    [TestCase(OutputKind.NetModule)]
    public async Task ActualSdkModuleOutputRequiresAnAssemblyForNativeReplay(OutputKind outputKind)
    {
        const string source = "public static class OrdinaryModule { public static int Target(int value) => value; }";
        var root = await CSharpSyntaxTree.ParseText(source).GetRootAsync();
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "ordinary-module.cs", OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", file.SourceId, file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Proven, CorpusSupport.Supported);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]),
            CancellationToken.None, allocations: true);
        compilation = compilation.WithOptions(compilation.Options.WithOutputKind(outputKind));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = NativeSourceEmission.EmitImage(compilation, "Ordinary module emission failed: ", CancellationToken.None);
        var context = new AssemblyLoadContext("ordinary-sdk-module-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            if (outputKind == OutputKind.NetModule)
            {
                Assert.Throws<BadImageFormatException>((Action)(() => context.LoadFromStream(image)));
                Assert.ThrowsAsync<BadImageFormatException>((Func<Task>)(async () =>
                    await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
                        "ordinary-sdk-module", 1, allocations: true)));
            }
            else
            {
                var callable = context.LoadFromStream(image).GetType("OrdinaryModule", throwOnError: true)!.GetMethod("Target")!;
                Assert.That(callable.Invoke(null, [23]), Is.EqualTo(23));
                var report = await NativeEffectOracleGate.ObserveAsync(compilation, ["sample"], RepositoryLayout.FindRoot(),
                    "ordinary-sdk-module", 1, allocations: true);
                Assert.That(report.Rows.Single().NativeOutcome, Is.EqualTo(WorkerClaimOutcome.Proven));
                Assert.That(report.Rows.Single().RuntimeOracle, Is.EqualTo("Confirmed"));
                Assert.That(report.Passed, Is.True);
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
