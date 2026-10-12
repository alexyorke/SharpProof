using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreMetadataLifetimeCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void OwnedCompiledNetmoduleRetainsItsClosedPreconditionClassification(bool containsClosedPrecondition)
    {
        var parameter = containsClosedPrecondition ? "[Positive] int value" : "int value";
        using var module = EmitOwnedModule(
            "using SharpProof.Attributes; public static class Api { public static int Value(" + parameter + ") => value; }");
        var reference = module.GetReference();
        Assert.That(reference.Properties.Kind, Is.EqualTo(MetadataImageKind.Module));
        Assert.That(module.IsDisposed, Is.False);
        Assert.That(SharpProofAnalyzerEngine.PortableReferenceContainsClosedPrecondition(reference),
            Is.EqualTo(containsClosedPrecondition));
    }

    [Test]
    public void DisposedOwnedModuleRetainsConservativeReferenceAdmission()
    {
        using var module = EmitOwnedModule("public static class Api { public static int Value(int value) => value; }");
        var reference = module.GetReference();
        Assert.That(SharpProofAnalyzerEngine.PortableReferenceContainsClosedPrecondition(reference), Is.False);
        module.Dispose();
        Assert.That(module.IsDisposed, Is.True);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            using var metadata = reference.GetMetadata();
        });
        Assert.That(SharpProofAnalyzerEngine.PortableReferenceContainsClosedPrecondition(reference), Is.True);
    }

    private static ModuleMetadata EmitOwnedModule(string source)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(source, [])
            .WithAssemblyName("AnalyzerCoreOwnedMetadataLifetime");
        compilation = compilation.WithOptions(compilation.Options.WithOutputKind(OutputKind.NetModule));
        return ModuleMetadata.CreateFromImage(AnalyzerTestHost.EmitImage(compilation));
    }
}
