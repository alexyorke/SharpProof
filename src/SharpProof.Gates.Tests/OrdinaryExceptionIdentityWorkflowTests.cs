using System.Runtime.Loader;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryExceptionIdentityWorkflowTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void RealEmittedSourceExceptionMustMatchItsCompilerAssemblyIdentity(bool sameAssembly)
    {
        const string source = """
            public sealed class OrdinaryException : System.Exception {
                public OrdinaryException() : base() { }
            }
            """;
        var expectedCompilation = AnalyzerGateHost.CreateCompilation(source,
            "ExpectedOrdinaryException", includeExternalEffectsFixture: false);
        var runtimeCompilation = sameAssembly ? expectedCompilation : AnalyzerGateHost.CreateCompilation(source,
            "OtherOrdinaryException", includeExternalEffectsFixture: false);
        var expectedType = expectedCompilation.GetTypeByMetadataName("OrdinaryException")!;
        using var image = new MemoryStream();
        var emitted = runtimeCompilation.Emit(image);
        Assert.That(emitted.Success, Is.True,
            string.Join(Environment.NewLine, emitted.Diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
        image.Position = 0;
        var context = new AssemblyLoadContext("ordinary-exception-identity-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var actual = (Exception)Activator.CreateInstance(assembly.GetType("OrdinaryException", throwOnError: true)!)!;

            Assert.That(actual.GetType().FullName, Is.EqualTo(expectedType.MetadataName));
            Assert.That(NativeExceptionWitnessOracle.MatchesExceptionType(expectedCompilation,
                CompilerIdentityBridge.CreateTypeDisplay(expectedType), exact: true, actual), Is.EqualTo(sameAssembly));
        }
        finally
        {
            context.Unload();
        }
    }
}
