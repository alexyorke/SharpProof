using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed class MetadataSourceNativeProducerControls
{
    private const string Library = "public static long M0(long value) => value;";

    [TestCase("identity")]
    [TestCase("static-read")]
    [TestCase("countdown")]
    [TestCase("two-entry-cycle")]
    public async Task ActualOwnedMetadataPreparationPreservesTheOriginalInterpreterPremises(string family)
    {
        var body = family switch
        {
            "identity" => "return value;",
            "static-read" => "return State;",
            "countdown" => "while (value > 0) value = unchecked(value - 1); return value;",
            "two-entry-cycle" => "if (value > 3) goto A; goto B; " +
                "A: value = unchecked(value - 1); if (value > 0) goto B; return value; " +
                "B: value = unchecked(value - 1); if (value > 0) goto A; return value;",
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
        var subject = (family == "static-read" ? "public static long State = 7; " : "") +
            "public static long Target0(long value) { Contract.Ensures(true); " + body + " }";
        var directory = Directory.CreateTempSubdirectory("sharpproof-metadata-fuzz-");
        var runtime = new AssemblyLoadContext("MetadataProgramFuzz", isCollectible: true);
        MemoryStream? libraryImage = null;
        MemoryStream? sourceImage = null;
        try
        {
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, deterministic: true);
            var librarySource = "public static class MetadataLibrary { " + Library + " }";
            var library = CSharpCompilation.Create("MetadataLibrary", [Parse(librarySource, "Library.cs")],
                TestMetadataReferences.SortedDistinctPlatform, options);
            libraryImage = EmitActual(library);
            var imagePath = Path.Combine(directory.FullName, "MetadataLibrary.dll");
            await File.WriteAllBytesAsync(imagePath, libraryImage.ToArray());
            runtime.LoadFromStream(libraryImage);
            var source = "using SharpProof.Attributes; public static class MetadataSubject { " + subject + " }";
            var compilation = CSharpCompilation.Create("MetadataSubject",
                [Parse(source, Path.Combine(directory.FullName, "Subject.cs"))],
                TestMetadataReferences.WithAdditionalPaths([typeof(SharpProof.Attributes.Contract).Assembly.Location], sort: true)
                    .Add(MetadataReference.CreateFromFile(imagePath)), options);
            sourceImage = EmitActual(compilation);
            var actualSubject = runtime.LoadFromStream(sourceImage).GetType("MetadataSubject");
            Assert.That(actualSubject, Is.Not.Null);
            var artifact = CompilerManifestArtifactProducer.Create(compilation, directory.FullName, "net9.0",
                WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
                WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
            CompilerManifestArtifactJson.DeserializePrepared(
                CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(preparations, Has.Length.EqualTo(1));
            Assert.That(preparations[0].Entry.CallableId, Does.Contain("MetadataSubject.Target0("));
            Assert.That(preparations[0].Total, Is.Not.Null);
            var total = preparations[0].Total!;
            Assert.That(total.Result, Is.Not.Null);
            var parameter = total.Parameters.Single();
            var factory = total.Program.Factory;
            var entries = new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
            {
                [parameter.Entry] = factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, 7L)
            };
            var executed = new IrProgramInterpreter(factory).Execute(total.Program, entries);
            var expected = family is "identity" or "static-read" ? 7L : 0L;
            Assert.That(actualSubject!.GetMethod("Target0")!.Invoke(null, [7L]), Is.EqualTo(expected));
            if (family == "static-read")
            {
                Assert.That(actualSubject.GetField("State")!.GetValue(null), Is.EqualTo(7L));
                Assert.That(total.Program.Blocks.SelectMany(block => block.Instructions)
                    .OfType<IrHavocInstruction>().Any(instruction => instruction.Origin == IrHavocOrigin.Approximation), Is.True);
                Assert.That(executed.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
            }
            else
            {
                Assert.That(executed.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(executed.ConsumedApproximation, Is.False);
                Assert.That(executed.ReturnValue!.Integer, Is.EqualTo(expected));
            }
        }
        finally
        {
            if (sourceImage != null)
            {
                await sourceImage.DisposeAsync();
            }
            if (libraryImage != null)
            {
                await libraryImage.DisposeAsync();
            }
            runtime.Unload();
            directory.Delete(recursive: true);
        }
    }

    private static SyntaxTree Parse(string text, string path)
    {
        return CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp12), path);
    }

    private static MemoryStream EmitActual(CSharpCompilation compilation)
    {
        var image = new MemoryStream();
        try
        {
            var emitted = compilation.Emit(image);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
            image.Position = 0;
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }
}
