using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class NativeSourceEmissionWorkflowTests
{
    [TestCase(OptimizationLevel.Release)]
    [TestCase(OptimizationLevel.Debug)]
    public void ActualOrdinaryCompilerImageLoadsAndRunsFromItsInitialPosition(OptimizationLevel optimization)
    {
        var original = AnalyzerGateHost.CreateCompilation(
            "public static class C { public static int Value() => 42; }",
            "OrdinarySourceEmission", includeExternalEffectsFixture: false);
        var compilation = original.WithOptions(original.Options.WithOptimizationLevel(optimization));
        using var image = NativeSourceEmission.EmitImage(compilation,
            "The source oracle did not compile: ", CancellationToken.None);
        Assert.That(image.Position, Is.Zero);
        Assert.That(image.Length, Is.GreaterThan(0));
        var context = new AssemblyLoadContext("ordinary-source-emission-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var callable = assembly.GetType("C", throwOnError: true)!.GetMethod("Value")!;
            Assert.That(callable.Invoke(null, null), Is.EqualTo(42));
        }
        finally
        {
            context.Unload();
        }
    }

    [TestCase("The source oracle did not compile: ")]
    [TestCase("The allocation oracle source did not compile: ")]
    public void OrdinaryCompilerErrorKeepsTheExistingOracleDiagnostic(string prefix)
    {
        var compilation = AnalyzerGateHost.CreateCompilation(
            "public static class C { public static int Value() => MissingValue; }",
            "OrdinarySourceEmissionError", includeExternalEffectsFixture: false);

        var error = Assert.Throws<InvalidDataException>((Action)(() =>
        {
            using var image = NativeSourceEmission.EmitImage(compilation, prefix, CancellationToken.None);
        }));

        Assert.That(error!.Message, Does.StartWith(prefix));
        Assert.That(error.Message, Does.Contain("CS0103"));
        Assert.That(error.Message, Does.Contain("MissingValue"));
    }
}
