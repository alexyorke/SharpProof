using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class CompilerDriverAdditionalFileContractTests
{
    private const string AdditionalText = "ordinary compiler additional input\n";
    private const string Source = "public static class Subject { public static int Value() => 1; }\n";

    [Test]
    public void MissingAdditionalFileReportsCollectorFailureThroughCompilerDriver()
    {
        InTemporaryDirectory(directory =>
        {
            var result = RunCompiler(directory, readable: false);

            Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
            Assert.That(result.Output, Does.Contain("CS2001"));
            Assert.That(result.Output, Does.Contain(result.AdditionalPath));
            Assert.That(result.Output, Does.Contain("SP0049"));
            Assert.That(result.Output, Does.Contain(
                "InvalidOperationException: An additional file has no compiler text."));
            Assert.That(result.Output, Does.Not.Contain("AD0001"));
            Assert.That(File.Exists(result.ManifestPath), Is.False);
        });
    }

    [Test]
    public void ReadableAdditionalFileEmitsCollectorManifestThroughCompilerDriver()
    {
        InTemporaryDirectory(directory =>
        {
            var result = RunCompiler(directory, readable: true);

            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That(result.Output, Does.Not.Contain("SP0049"));
            Assert.That(result.Output, Does.Not.Contain("CS2001"));
            Assert.That(result.Output, Does.Not.Contain("AD0001"));
            Assert.That(File.Exists(result.ManifestPath), Is.True, result.Output);
            using var manifest = JsonDocument.Parse(File.ReadAllText(result.ManifestPath));
            var additionalFiles = manifest.RootElement.GetProperty("compilation")
                .GetProperty("additionalFiles");
            Assert.That(additionalFiles.GetArrayLength(), Is.EqualTo(1));
            var snapshot = additionalFiles[0];
            Assert.That(snapshot.GetProperty("path").GetString(),
                Is.EqualTo(Path.GetFullPath(result.AdditionalPath)));
            var expectedHash = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(AdditionalText)));
            Assert.That(snapshot.GetProperty("sha256").GetString(), Is.EqualTo(expectedHash));
        });
    }

    private static DriverResult RunCompiler(string directory, bool readable)
    {
        var sourcePath = Path.Combine(directory, "Subject.cs");
        var additionalPath = Path.Combine(directory, "input.txt");
        var configPath = Path.Combine(directory, "compiler.globalconfig");
        var manifestPath = Path.Combine(directory, "compiler-manifest.json");
        var outputPath = Path.Combine(directory, "Subject.dll");
        var collectorPath = typeof(FinalCompilationCollectorAnalyzer).Assembly.Location;
        Assert.That(Path.IsPathFullyQualified(collectorPath), Is.True);
        Assert.That(File.Exists(collectorPath), Is.True);
        File.WriteAllText(sourcePath, Source, new UTF8Encoding(false));
        if (readable)
        {
            File.WriteAllText(additionalPath, AdditionalText, new UTF8Encoding(false));
        }
        Assert.That(File.Exists(additionalPath), Is.EqualTo(readable));

        File.WriteAllText(configPath, $"""
            is_global = true
            build_property._SharpProofCompilerManifestPath = {manifestPath}
            build_property._SharpProofCompilationTargetFramework = net9.0
            build_property._SharpProofProjectDirectory = {directory}
            build_property.SharpProofVerifyMaximumExpressionDepth = {WorkerBudgets.DefaultMaximumExpressionDepth.ToString(CultureInfo.InvariantCulture)}
            build_property.SharpProofProfile = advisory
            build_property.SharpProofFeatures = all
            build_property.SharpProofVerifyPolicy = advisory
            build_property.SharpProofAssumptionPolicy = allow
            """, new UTF8Encoding(false));

        var runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetDirectory = runtimeDirectory.Parent?.Parent?.Parent;
        Assert.That(dotnetDirectory, Is.Not.Null);
        var compilerPath = Path.Combine(dotnetDirectory!.FullName,
            "sdk", "9.0.316", "Roslyn", "bincore", "csc.dll");
        Assert.That(File.Exists(compilerPath), Is.True, compilerPath);
        var compiler = AssemblyLoadContext.Default.LoadFromAssemblyPath(compilerPath);
        Assert.That(Path.GetFullPath(compiler.Location), Is.EqualTo(compilerPath));
        var entry = compiler.EntryPoint;
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.IsPublic && entry.IsStatic, Is.True);
        Assert.That(entry.DeclaringType!.FullName,
            Is.EqualTo("Microsoft.CodeAnalysis.CSharp.CommandLine.Program"));
        Assert.That(entry.ReturnType, Is.EqualTo(typeof(int)));
        Assert.That(entry.GetParameters(), Has.Length.EqualTo(1));
        Assert.That(entry.GetParameters()[0].ParameterType, Is.EqualTo(typeof(string[])));

        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        Assert.That(trustedAssemblies, Is.Not.Null.And.Not.Empty);
        var references = trustedAssemblies!.Split(Path.PathSeparator)
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.That(references.All(Path.IsPathFullyQualified), Is.True);
        Assert.That(references.All(File.Exists), Is.True);
        var arguments = new List<string>
        {
            "/noconfig", "/nologo", "/target:library",
            "/out:" + outputPath,
            "/analyzer:" + collectorPath,
            "/analyzerconfig:" + configPath,
            "/additionalfile:" + additionalPath
        };
        arguments.AddRange(references.Select(path => "/reference:" + path));
        arguments.Add(sourcePath);
        var originalOutput = Console.Out;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        object? returned;
        try
        {
            Console.SetOut(output);
            returned = entry.Invoke(null, [arguments.ToArray()]);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
        var compilerOutput = output.ToString();
        TestContext.WriteLine(compilerOutput);
        Assert.That(returned, Is.TypeOf<int>(), compilerOutput);
        return new DriverResult((int)returned!, compilerOutput, manifestPath, additionalPath);
    }

    private static void InTemporaryDirectory(Action<string> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var directory = Directory.CreateTempSubdirectory("SharpProofCompilerDriver-");
        try
        {
            action(directory.FullName);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed record DriverResult(
        int ExitCode, string Output, string ManifestPath, string AdditionalPath);
}
