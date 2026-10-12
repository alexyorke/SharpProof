using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzEmissionLifecycleTests
{
    [Test]
    public void RealCompilationEmissionReturnsItsSuccessfulReadableImage()
    {
        var compilation = CreateValidCompilation();
        var emission = GeneratedCompilationEmitter.Emit(
            compilation, CancellationToken.None);
        using var image = emission.Image;

        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        Assert.That(image.CanRead, Is.True);
        Assert.That(image.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task RealCompilationEmissionPropagatesTheActualPrecanceledTokenException()
    {
        var compilation = CreateValidCompilation();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Action emit = () =>
        {
            using var image = GeneratedCompilationEmitter.Emit(
                compilation, cancellation.Token).Image;
        };

        Assert.Throws<OperationCanceledException>(emit);
    }

    [Test]
    public void MetadataEmissionReportsAnActualCompilerReturnTypeError()
    {
        const string source = "public static class MetadataEmissionControl { " +
            "public static int Target() => \"bad\"; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "metadata-emission-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("MetadataEmissionInvalidReturn", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id), Does.Contain("CS0029"));
        Action emit = () =>
        {
            using var image = MetadataProgramDifferentialOracle.Emit(
                compilation, CancellationToken.None);
        };

        var exception = Assert.Throws<InvalidOperationException>(emit);

        Assert.That(exception!.Message, Does.Contain("CS0029"));
        Assert.That(exception.Message, Does.Contain("metadata-emission-control.cs"));
    }

    private static CSharpCompilation CreateValidCompilation()
    {
        const string source = "public static class EmittedControl { " +
            "public static bool Target(bool value) => !value; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("FuzzEmissionControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: true, nullableContextOptions: NullableContextOptions.Enable));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return compilation;
    }
}
