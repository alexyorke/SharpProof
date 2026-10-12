using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerCaptureInputWorkflowTests
{
    [TestCase(0L, 1L, 1)]
    [TestCase(2L, 1L, 1)]
    [TestCase(1L, 1L, 0)]
    public void InvalidScalarReferenceLimitsHaveTheExistingNamedError(long moduleBytes, long closureBytes, int count)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            new CompilerCompilationCapture.ReferenceCaptureLimits(moduleBytes, closureBytes, count)));
        Assert.That(error!.ParamName, Is.EqualTo("maximumModuleBytes"));
    }

    [Test]
    public void SmallValidReferenceLimitsPreserveAllThreeValues()
    {
        var limits = new CompilerCompilationCapture.ReferenceCaptureLimits(1, 1, 1);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(limits.MaximumModuleBytes, Is.EqualTo(1));
            Assert.That(limits.MaximumClosureBytes, Is.EqualTo(1));
            Assert.That(limits.MaximumModuleCount, Is.EqualTo(1));
        }
    }

    [TestCase("", "net8.0")]
    [TestCase("project", "")]
    public void MissingCaptureIdentityOptionsAreRejected(string directory, string framework)
    {
        var compilation = AnalyzerTestHost.CreateCompilation("internal class Subject { }", []);
        Assert.That((Action)(() => CompilerCompilationCapture.Capture(compilation,
            directory, framework, [], CancellationToken.None)),
            Throws.TypeOf<ArgumentException>().With.Message.EqualTo("The project directory and target framework are required."));
    }

    [Test]
    public void EnhancedLineDirectiveCaptureBindsTheRealCompilerLocation()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            internal static class Subject {
            #line (5,3)-(5,17) 11 "template.dsl"
                internal static int Identity() => 1;
            #line default
            }
            """, [], filePath: "generated.cs");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var captured = CompilerCompilationCapture.CaptureTree(tree, CancellationToken.None);
        var identifier = tree.GetRoot().DescendantTokens().Single(token =>
            token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == "Identity");
        var location = CompilerSourceLocationProjection.Create(identifier.GetLocation());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(captured.LineMap.Any(line => line.CharacterOffset == 11), Is.True);
            Assert.That(location.Path, Is.EqualTo("template.dsl"));
            Assert.That(location.Line, Is.EqualTo(5));
            Assert.That(CompilerSourceCoordinates.HasValidLocationGeometry(location, captured), Is.True);
            Assert.That(CompilerSourceCoordinates.FindUniqueTree(location, [captured]), Is.Zero);
        }
    }

    [Test]
    public void GenuineEmittedNetmoduleCapturesItsModuleIdentityAndImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofCaptureModule-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var compilation = AnalyzerTestHost.CreateCompilation("public static class NetModuleSubject { }", [])
                .WithAssemblyName("CaptureModule");
            compilation = compilation.WithOptions(compilation.Options.WithOutputKind(OutputKind.NetModule));
            var image = AnalyzerTestHost.EmitImage(compilation);
            var path = Path.Combine(directory, "CaptureModule.netmodule");
            File.WriteAllBytes(path, image);
            var reference = MetadataReference.CreateFromImage(image,
                new MetadataReferenceProperties(MetadataImageKind.Module), filePath: path);
            var captured = CompilerCompilationCapture.CaptureReferences([reference],
                CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None).Single();
            using var pe = new PEReader(new MemoryStream(image, writable: false));
            var reader = pe.GetMetadataReader();
            var module = captured.Modules.Single();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(reader.IsAssembly, Is.False);
                Assert.That(captured.Kind, Is.EqualTo("Module"));
                Assert.That(module.Name, Is.EqualTo(reader.GetString(reader.GetModuleDefinition().Name)));
                Assert.That(captured.Identity, Is.EqualTo(module.Name));
                Assert.That(module.Path, Is.EqualTo(Path.GetFullPath(path)));
                Assert.That(module.SizeBytes, Is.EqualTo(image.Length));
                Assert.That(module.Mvid, Is.EqualTo(reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString("D")));
                Assert.That(module.Sha256, Is.EqualTo(Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant()));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
