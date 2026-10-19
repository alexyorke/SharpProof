using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ContractApiIdentityResolverRebuildTests
{
    [Test]
    public async Task AuthenticRebuildRejectsStaleBoundImageAndPreservesImageOnlyApproval()
    {
        TestRepository.RequireCanonicalContainer();
        var totalClock = Stopwatch.StartNew();
        var root = TestRepository.FindRoot();
        var project = Path.Combine(root, "src/SharpProof.Attributes", "SharpProof.Attributes.csproj");
        var lockPath = Path.Combine(root, "src/SharpProof.Attributes", "packages.lock.json");
        var lockBytes = await File.ReadAllBytesAsync(lockPath);
        var assembly = typeof(SharpProof.Attributes.Contract).Assembly;
        var original = await File.ReadAllBytesAsync(assembly.Location);
        var originalMvid = ReadMvid(original);
        var configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        Assert.That(configuration, Is.EqualTo("Debug").Or.EqualTo("Release"));
        var rebuiltConfiguration = configuration == "Release" ? "Debug" : "Release";
        using var temporary = new TempDirectory("SharpProofAuthenticRebuild-");
        var artifacts = PrivatePath(temporary.FullName, ["artifacts"]);
        var output = PrivatePath(temporary.FullName, ["artifacts", "bin", "SharpProof.Attributes",
            rebuiltConfiguration, "netstandard2.0", "SharpProof.Attributes.dll"]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllBytesAsync(output, original);
        var reference = MetadataReference.CreateFromImage(original, filePath: output);
        var before = ContractApiIdentityResolver.ForCompilation(CreateConsumer(reference));
        Assert.That(before.Contract, Is.Not.Null, "Actual approved A must resolve before replacement.");
        Assert.That(before.ResolveAttribute(ContractApiMetadata.NotNull), Is.Not.Null);
        Assert.That(before.UnreadableContractApiReason, Is.Null);

        var packages = PrivatePath(temporary.FullName, ["packages"]);
        StageLockedPackages(lockBytes, temporary.FullName);
        var start = ProcessRunner.CreateStartInfo(root, "dotnet",
            ["build", project, "--configuration", rebuiltConfiguration,
                "-p:ArtifactsDir=" + artifacts + Path.DirectorySeparatorChar, "--nologo", "-p:RunAnalyzersDuringBuild=false"]);
        start.Environment["NUGET_PACKAGES"] = packages;
        start.Environment["NUGET_HTTP_CACHE_PATH"] = PrivatePath(temporary.FullName, ["http-cache"]);
        start.Environment["NUGET_SCRATCH"] = PrivatePath(temporary.FullName, ["nuget-scratch"]);
        start.Environment["DOTNET_CLI_HOME"] = PrivatePath(temporary.FullName, ["dotnet-home"]);
        var clock = Stopwatch.StartNew();
        var build = await ProcessRunner.RunCapturedAsync(start, TestContext.CurrentContext.CancellationToken);
        clock.Stop();
        totalClock.Stop();
        TestContext.WriteLine($"Maintained Attributes rebuild {configuration}->{rebuiltConfiguration}: {clock.Elapsed}.");
        TestContext.WriteLine($"Total setup and build: {totalClock.Elapsed}.");
        TestContext.WriteLine(build.CombinedOutput);
        Assert.That(build.ExitCode, Is.Zero, "The unchanged maintained Attributes project must build normally.");
        Assert.That(await File.ReadAllBytesAsync(lockPath), Is.EqualTo(lockBytes));
        var rebuilt = await File.ReadAllBytesAsync(output);
        var rebuiltMvid = ReadMvid(rebuilt);
        TestContext.WriteLine($"A MVID={originalMvid}, SHA256={Convert.ToHexString(SHA256.HashData(original))}");
        TestContext.WriteLine($"B MVID={rebuiltMvid}, SHA256={Convert.ToHexString(SHA256.HashData(rebuilt))}");
        TestContext.WriteLine($"Maintained csproj SHA256={Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(project)))}");
        Assert.That(AssemblyName.GetAssemblyName(output).FullName, Is.EqualTo(assembly.GetName().FullName));
        Assert.That(rebuiltMvid, Is.Not.EqualTo(originalMvid), "The single opposite-configuration build must produce distinct B.");
        using (var bound = (AssemblyMetadata)reference.GetMetadata())
        {
            var reader = bound.GetModules().Single().GetMetadataReader();
            Assert.That(reader.GetGuid(reader.GetModuleDefinition().Mvid), Is.EqualTo(originalMvid));
        }

        var staleConsumer = CreateConsumer(reference);
        var candidate = staleConsumer.GetTypeByMetadataName(ContractApiMetadata.Contract)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly,
            staleConsumer.GetAssemblyOrModuleSymbol(reference)), Is.True);
        var stale = ContractApiIdentityResolver.ForCompilation(staleConsumer);
        Assert.That(stale.Contract, Is.Null);
        Assert.That(stale.ResolveAttribute(ContractApiMetadata.NotNull), Is.Null);
        Assert.That(stale.UnreadableContractApiReason, Is.Null, "Readable B is a mismatch, not an I/O fault.");
        var imageOnly = ContractApiIdentityResolver.ForCompilation(
            CreateConsumer(MetadataReference.CreateFromImage(original)));
        Assert.That(imageOnly.Contract, Is.Not.Null);
        Assert.That(imageOnly.ResolveAttribute(ContractApiMetadata.NotNull), Is.Not.Null);
        Assert.That(imageOnly.UnreadableContractApiReason, Is.Null);
    }

    private static CSharpCompilation CreateConsumer(PortableExecutableReference reference)
    {
        System.ArgumentNullException.ThrowIfNull(reference);
        var compilation = CSharpCompilation.Create("AuthenticAttributesRebuildConsumer",
            [CSharpSyntaxTree.ParseText("public static class Subject { }")],
            TestMetadataReferences.WithoutSharpProof.Add(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        return compilation;
    }

    private static Guid ReadMvid(byte[] image)
    {
        System.ArgumentNullException.ThrowIfNull(image, nameof(image));
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        Assert.That(pe.HasMetadata, Is.True);
        var reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }

    private static string PrivatePath(string root, ImmutableArray<string> parts)
    {
        if (parts.IsDefault)
        { throw new System.ArgumentNullException(nameof(parts)); }
        System.ArgumentNullException.ThrowIfNull(root);
        var prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine([root, .. parts]));
        Assert.That(path.StartsWith(prefix, StringComparison.Ordinal), Is.True,
            "Every output must remain inside the fixture-owned temporary root.");
        return path;
    }

    private static void StageLockedPackages(byte[] lockBytes, string privateRoot)
    {
        System.ArgumentNullException.ThrowIfNull(lockBytes, nameof(lockBytes));
        System.ArgumentNullException.ThrowIfNull(privateRoot);
        var cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Assert.That(cache, Is.Not.Null.And.Not.Empty);
        using var document = JsonDocument.Parse(lockBytes);
        foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (var package in framework.Value.EnumerateObject())
            {
                var packageDirectories = Directory.EnumerateDirectories(cache!).Where(directory =>
                    directory is null ? throw new System.ArgumentNullException(nameof(directory)) : (string.Equals(Path.GetFileName(directory), package.Name, StringComparison.OrdinalIgnoreCase))).ToImmutableArray();
                Assert.That(packageDirectories, Has.Length.EqualTo(1), "The exact locked package must have one actual cache directory.");
                var version = package.Value.GetProperty("resolved").GetString()!;
                var versionDirectories = Directory.EnumerateDirectories(packageDirectories[0]).Where(directory =>
                    directory is null ? throw new System.ArgumentNullException(nameof(directory)) : (string.Equals(Path.GetFileName(directory), version, StringComparison.OrdinalIgnoreCase))).ToImmutableArray();
                Assert.That(versionDirectories, Has.Length.EqualTo(1), "The exact locked version must have one actual cache directory.");
                var source = versionDirectories[0];
                var suffix = Path.Combine(Path.GetFileName(packageDirectories[0]), Path.GetFileName(source));
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    var target = PrivatePath(privateRoot, ["packages", suffix, Path.GetRelativePath(source, file)]);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            }
        }
    }
}
