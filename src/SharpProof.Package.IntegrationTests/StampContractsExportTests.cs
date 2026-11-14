using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NUnit.Framework;
using SharpProof.BuildTasks;

namespace SharpProof.Package.Test;

// Runs the stamping task in this test host. The packed-consumer tests only
// run it inside MSBuild child processes.
[TestFixture]
public sealed class StampContractsExportTests
{
    private const string Export = """{"schema":"SharpProof.ContractsExport","version":1,"contracts":[]}""";

    [Test]
    public void CompletedCaptureBindsTheExportToBothProducedImages()
    {
        using var directory = new TempDirectory("sharpproof-stamp-");
        var implementation = EmitLibrary(directory, "bin/Stamped.dll", metadataOnly: false);
        var reference = EmitLibrary(directory, "ref/Stamped.dll", metadataOnly: true);
        var engine = new BuildTaskTests.RecordingBuildEngine();
        var task = CreateTask(directory, engine, Export);
        task.ReferenceAssembly = "ref/Stamped.dll";

        Assert.That(task.Execute(), Is.True);

        Assert.That(engine.Errors, Is.Empty);
        Assert.That(engine.Warnings, Is.Empty);
        var output = OutputPath(directory);
        var text = File.ReadAllText(output);
        Assert.That(text, Does.EndWith("\n"));
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("SharpProof.ContractsExport.Stamped"));
        Assert.That(root.GetProperty("version").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("export").GetRawText(), Is.EqualTo(Export));
        var images = ReadImages(root);
        Assert.That(images, Is.EqualTo(ImmutableArray.Create(
            Identity("implementation", implementation),
            Identity("reference", reference))));
        Assert.That(images[1].Sha256, Is.Not.EqualTo(images[0].Sha256));
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(output)!), Is.EqualTo(ImmutableArray.Create(output)));
    }

    [TestCase(null)]
    [TestCase(" ")]
    [TestCase("ref/Missing.dll")]
    public void AbsentReferenceImageStampsOnlyTheImplementation(string? reference)
    {
        using var directory = new TempDirectory("sharpproof-stamp-no-reference-");
        var implementation = EmitLibrary(directory, "bin/Stamped.dll", metadataOnly: false);
        var engine = new BuildTaskTests.RecordingBuildEngine();
        var task = CreateTask(directory, engine, Export);
        task.ReferenceAssembly = reference;

        Assert.That(task.Execute(), Is.True);

        Assert.That(engine.Errors, Is.Empty);
        using var document = JsonDocument.Parse(File.ReadAllText(OutputPath(directory)));
        Assert.That(ReadImages(document.RootElement), Is.EqualTo(ImmutableArray.Create(
            Identity("implementation", implementation))));
    }

    [TestCase("capture-incomplete", 0)]
    [TestCase("missing-export", 0)]
    [TestCase("empty-export", 1)]
    [TestCase("oversized-export", 1)]
    [TestCase("non-object-export", 1)]
    [TestCase("missing-schema", 1)]
    [TestCase("unknown-schema", 1)]
    [TestCase("missing-version", 1)]
    [TestCase("unknown-version", 1)]
    public void UnusableCaptureRemovesThePreviousExportWithoutStamping(string kind, int warnings)
    {
        using var directory = new TempDirectory("sharpproof-stamp-unusable-");
        var engine = new BuildTaskTests.RecordingBuildEngine();
        var task = CreateTask(directory, engine, kind switch
        {
            "missing-export" => null,
            "empty-export" => string.Empty,
            "non-object-export" => "[]",
            "missing-schema" => """{"version":1}""",
            "unknown-schema" => """{"schema":"SharpProof.ContractsExport.Stamped","version":1}""",
            "missing-version" => """{"schema":"SharpProof.ContractsExport"}""",
            "unknown-version" => """{"schema":"SharpProof.ContractsExport","version":2}""",
            _ => Export
        });
        task.CaptureCompleted = kind != "capture-incomplete";
        if (kind == "oversized-export")
        {
            using var stream = File.OpenWrite(Path.Combine(directory.FullName, "export.json"));
            stream.SetLength((16L * 1024 * 1024) + 1);
        }
        var output = OutputPath(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, "export of an earlier compilation");

        Assert.That(task.Execute(), Is.True);

        Assert.That(engine.Errors, Is.Empty);
        Assert.That(engine.Warnings, Has.Length.EqualTo(warnings));
        Assert.That(File.Exists(output), Is.False);
    }

    [TestCase("malformed-export", "")]
    [TestCase("non-numeric-version", "")]
    [TestCase("missing-implementation", "")]
    [TestCase("unreadable-implementation", "")]
    [TestCase("native-implementation", "no metadata")]
    [TestCase("output-is-directory", "")]
    public void StampingFailuresReportOneErrorAndPublishNothing(string kind, string expectedMessage)
    {
        using var directory = new TempDirectory("sharpproof-stamp-failure-");
        var engine = new BuildTaskTests.RecordingBuildEngine();
        var task = CreateTask(directory, engine, kind switch
        {
            "malformed-export" => "{",
            "non-numeric-version" => """{"schema":"SharpProof.ContractsExport","version":"1"}""",
            _ => Export
        });
        var implementation = Path.Combine(directory.FullName, "bin", "Stamped.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(implementation)!);
        switch (kind)
        {
            case "missing-implementation":
                break;
            case "unreadable-implementation":
                File.WriteAllText(implementation, "not a portable executable");
                break;
            case "native-implementation":
                File.WriteAllBytes(implementation, NativeImage());
                break;
            default:
                _ = EmitLibrary(directory, "bin/Stamped.dll", metadataOnly: false);
                break;
        }
        var output = OutputPath(directory);
        if (kind == "output-is-directory")
        { Directory.CreateDirectory(output); }

        Assert.That(task.Execute(), Is.False);

        Assert.That(engine.Errors, Has.Length.EqualTo(1));
        Assert.That(engine.Errors[0].Message, Does.Contain(expectedMessage));
        Assert.That(File.Exists(output), Is.False);
        Assert.That(Directory.GetFiles(directory.FullName, "*.tmp", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void CanceledStampKeepsThePreviousExport()
    {
        using var directory = new TempDirectory("sharpproof-stamp-canceled-");
        _ = EmitLibrary(directory, "bin/Stamped.dll", metadataOnly: false);
        var engine = new BuildTaskTests.RecordingBuildEngine();
        var task = CreateTask(directory, engine, Export);
        var output = OutputPath(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, "export of an earlier compilation");

        task.Cancel();

        Assert.That(task.Execute(), Is.False);
        Assert.That(engine.Errors, Is.Empty);
        Assert.That(File.ReadAllText(output), Is.EqualTo("export of an earlier compilation"));
    }

    [Test]
    [Platform("Linux")]
    public async System.Threading.Tasks.Task CancellationWhileReadingImagesPublishesNothing()
    {
        using var directory = new TempDirectory("sharpproof-stamp-active-cancel-");
        var image = await File.ReadAllBytesAsync(EmitLibrary(directory, "source/Stamped.dll", metadataOnly: false));
        var pipe = Path.Combine(directory.FullName, "bin", "Stamped.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(pipe)!);
        using (var mkfifo = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mkfifo",
                UseShellExecute = false,
                ArgumentList = { pipe }
            })!)
        {
            await mkfifo.WaitForExitAsync();
            Assert.That(mkfifo.ExitCode, Is.Zero);
        }
        var engine = new BuildTaskTests.RecordingBuildEngine();
        var task = CreateTask(directory, engine, Export);
        var execution = System.Threading.Tasks.Task.Run(task.Execute);

        // A pipe opens for writing only once the task is reading the image.
        await using (var writer = await System.Threading.Tasks.Task.Run(() =>
            new FileStream(pipe, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)).WaitAsync(TimeSpan.FromSeconds(30)))
        {
            task.Cancel();
            await writer.WriteAsync(image);
        }

        Assert.That(await execution.WaitAsync(TimeSpan.FromSeconds(30)), Is.False);
        Assert.That(engine.Errors, Is.Empty);
        Assert.That(File.Exists(OutputPath(directory)), Is.False);
    }

    private static string OutputPath(TempDirectory directory)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        return Path.Combine(directory.FullName, "obj", "contracts-export.json");
    }

    // A null export leaves the unstamped file absent.
    private static StampContractsExport CreateTask(
        TempDirectory directory, BuildTaskTests.RecordingBuildEngine engine, string? export)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        System.ArgumentNullException.ThrowIfNull(engine);
        if (export is not null)
        { File.WriteAllText(Path.Combine(directory.FullName, "export.json"), export); }
        return new StampContractsExport
        {
            BuildEngine = engine,
            ProjectDirectory = directory.FullName,
            UnstampedPath = "export.json",
            OutputPath = "obj/contracts-export.json",
            ImplementationAssembly = "bin/Stamped.dll",
            CaptureCompleted = true
        };
    }

    private static string EmitLibrary(TempDirectory directory, string relativePath, bool metadataOnly)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        System.ArgumentNullException.ThrowIfNull(relativePath);
        var path = Path.Combine(directory.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var compilation = CSharpCompilation.Create(
            "Stamped",
            [CSharpSyntaxTree.ParseText("public static class Stamped { public static int One() => 1; }")],
            TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var result = compilation.Emit(stream, options: new EmitOptions(metadataOnly: metadataOnly));
        Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }

    private static (string Role, string Mvid, string Sha256) Identity(string role, string path)
    {
        System.ArgumentNullException.ThrowIfNull(role);
        System.ArgumentNullException.ThrowIfNull(path);
        using var metadata = AssemblyMetadata.CreateFromFile(path);
        return (role,
            metadata.GetModules()[0].GetModuleVersionId().ToString("D"),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
    }

    private static ImmutableArray<(string Role, string Mvid, string Sha256)> ReadImages(JsonElement root)
    {
        return [.. root.GetProperty("images").EnumerateArray().Select(image => (
            image.GetProperty("role").GetString()!,
            image.GetProperty("mvid").GetString()!,
            image.GetProperty("sha256").GetString()!))];
    }

    // A PE32 image with no sections and no CLI header.
    private static byte[] NativeImage()
    {
        var image = new byte[0x40 + 4 + 20 + 224];
        image[0x00] = (byte)'M';
        image[0x01] = (byte)'Z';
        image[0x3c] = 0x40;
        image[0x40] = (byte)'P';
        image[0x41] = (byte)'E';
        image[0x44] = 0x4c;
        image[0x45] = 0x01;
        image[0x54] = 224;
        image[0x58] = 0x0b;
        image[0x59] = 0x01;
        image[0x58 + 92] = 16;
        return image;
    }
}
