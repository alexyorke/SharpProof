using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.Package.Test;

[SetUpFixture]
public sealed class PackagedProductFeedLifecycle
{
    [OneTimeTearDown]
    public void RemoveFeed()
    {
        PackagedProductFeed.DisposeShared();
        PackageLayoutSmokeTests.DisposeSharedPackageCache();
    }
}

internal sealed class PackagedProductFeed : IDisposable
{
    internal const string AttributesPackageId = "SharpProof.Attributes";
    internal const string PortablePackageId = "SharpProof";
    internal const string VerifierPackageId =
        "SharpProof.Verifier";
    internal const string PackageSourceEnvironmentVariable =
        "SHARPPROOF_PACKAGE_SOURCE";

    private static readonly string[] s_expectedPackageIds = [
        AttributesPackageId,
        PortablePackageId,
        VerifierPackageId
    ];
    private static readonly Lazy<Task<PackagedProductFeed>> s_shared =
        new(CreateAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly string s_sharedCompilationServerId =
        "sharpproof-package-feed-" +
        typeof(PackagedProductFeed).Assembly.ManifestModule.ModuleVersionId
            .ToString("N");

    private readonly bool _ownsRoot;
    private readonly string? _ownedRoot;

    private PackagedProductFeed(
        string source,
        IReadOnlyList<PackagedPackage> packages,
        IReadOnlyList<PackagedPackage> symbolPackages,
        bool ownsRoot,
        string? ownedRoot)
    {
        Source = source;
        Packages = packages;
        SymbolPackages = symbolPackages;
        _ownsRoot = ownsRoot;
        _ownedRoot = ownedRoot;
    }

    internal string Source
    {
        get;
    }
    internal IReadOnlyList<PackagedPackage> Packages
    {
        get;
    }
    internal IReadOnlyList<PackagedPackage> SymbolPackages
    {
        get;
    }
    internal string Version => Packages[0].Version;

    internal static Task<PackagedProductFeed> GetAsync()
    {
        return s_shared.Value;
    }

    internal static void DisposeShared()
    {
        if (!s_shared.IsValueCreated ||
            !s_shared.Value.IsCompletedSuccessfully)
        {
            return;
        }

        s_shared.Value.Result.Dispose();
    }

    internal PackagedPackage GetPackage(string id)
    {
        return Packages.Single(package =>
            string.Equals(package.Id, id, StringComparison.Ordinal));
    }

    internal string GetPackagePath(string id)
    {
        return GetPackage(id).Path;
    }

    internal string GetSymbolPackagePath(string id)
    {
        return SymbolPackages.Single(package =>
            string.Equals(package.Id, id, StringComparison.Ordinal)).Path;
    }

    public void Dispose()
    {
        if (!_ownsRoot || _ownedRoot == null)
        {
            return;
        }

        TestRepository.DeleteOwnedTemporaryDirectory(
            _ownedRoot,
            "SharpProof.PackagedProductFeed",
            "Refusing to remove an unexpected package-feed directory.");
    }

    private static async Task<PackagedProductFeed> CreateAsync()
    {
        var suppliedSource = Environment.GetEnvironmentVariable(
            PackageSourceEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(suppliedSource))
        {
            var source = Path.GetFullPath(suppliedSource);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(
                    PackageSourceEnvironmentVariable +
                    " does not name an existing directory: " + source);
            }

            return CreateValidated(
                source,
                ownsRoot: false,
                ownedRoot: null);
        }

        var repositoryRoot = TestRepository.FindRoot();
        var root = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.PackagedProductFeed",
            Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "feed");
        Directory.CreateDirectory(sourceDirectory);
        try
        {
            foreach (var project in ReadPackageProjects(repositoryRoot))
            {
                var result = await RunDotNetAsync(
                    repositoryRoot,
                    "pack",
                    Path.Combine(repositoryRoot, project),
                    "-c",
                    "Release",
                    "--nologo",
                    "/nodeReuse:false",
                    "-p:GeneratePackageOnBuild=false",
                    "--output",
                    sourceDirectory);
                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        "Packing failed for " + project +
                        Environment.NewLine + result.Output);
                }
            }
            return CreateValidated(
                sourceDirectory,
                ownsRoot: true,
                ownedRoot: root);
        }
        catch
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            throw;
        }
    }

    private static PackagedProductFeed CreateValidated(
        string source,
        bool ownsRoot,
        string? ownedRoot)
    {
        var packages = ReadPackages(source, ".nupkg");
        var symbolPackages = ReadPackages(source, ".snupkg");
        ValidatePackages(packages, "package");
        ValidatePackages(symbolPackages, "symbol package");
        if (!string.Equals(
                packages[0].Version,
                symbolPackages[0].Version,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "All SharpProof packages and symbol packages must have " +
                "the same version.");
        }

        return new PackagedProductFeed(
            source,
            packages,
            symbolPackages,
            ownsRoot,
            ownedRoot);
    }

    private static PackagedPackage[] ReadPackages(
        string source,
        string extension)
    {
        return [
            .. Directory.EnumerateFiles(
                source,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(
                Path.GetExtension(path),
                extension,
                StringComparison.OrdinalIgnoreCase))
            .Select(ReadPackage)
            .OrderBy(package => Array.IndexOf(
                s_expectedPackageIds,
                package.Id))
        ];
    }

    private static void ValidatePackages(
        PackagedPackage[] packages,
        string kind)
    {
        if (!packages.Select(static package => package.Id)
                .SequenceEqual(s_expectedPackageIds, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "The package source must contain exactly one " + kind +
                " for " +
                string.Join(", ", s_expectedPackageIds) + ". Found: " +
                string.Join(
                    ", ",
                    packages.Select(static package =>
                        package.Id + " " + package.Version)));
        }

        if (packages.Select(static package => package.Version)
                .Distinct(StringComparer.Ordinal).Count() != 1)
        {
            throw new InvalidOperationException(
                "All SharpProof " + kind + "s must have the same version.");
        }
    }

    private static PackagedPackage ReadPackage(string path)
    {
        var document = PackageNuspecReader.Read(path);
        var metadata = document.Root?.Elements()
            .Single(element =>
                element.Name.LocalName == "metadata") ??
            throw new InvalidDataException(
                "Package nuspec metadata was not found: " + path);
        var id = metadata.Elements()
            .Single(element => element.Name.LocalName == "id").Value;
        var version = metadata.Elements()
            .Single(element => element.Name.LocalName == "version").Value;
        return new PackagedPackage(id, version, path);
    }

    private static string[] ReadPackageProjects(string repositoryRoot)
    {
        var path = Path.Combine(
            repositoryRoot,
            "scripts",
            "package-projects.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidDataException(
                "Unsupported package-projects schema.");
        }

        var projects = root.GetProperty("projects")
            .EnumerateArray()
            .Select(static value => value.GetString() ??
                throw new InvalidDataException(
                    "A package project path is null."))
            .ToArray();
        var expectedProjects = new[] {
            "SharpProof.Attributes/SharpProof.Attributes.csproj",
            "SharpProof.Package/SharpProof.Package.csproj",
            "SharpProof.Verifier/" +
                "SharpProof.Verifier.csproj"
        };
        if (!projects.SequenceEqual(
                expectedProjects,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "package-projects.json must list the three product " +
                "packages in dependency order.");
        }

        return projects;
    }

    private static async Task<PackageProcessResult> RunDotNetAsync(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["SharedCompilationId"] =
            s_sharedCompilationServerId;

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start dotnet.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new PackageProcessResult(
            process.ExitCode,
            (await standardOutput) + Environment.NewLine +
            (await standardError));
    }

}

internal sealed record PackagedPackage(
    string Id,
    string Version,
    string Path);

internal readonly record struct PackageProcessResult(
    int ExitCode,
    string Output);

internal static class PackageNuspecReader
{
    internal static XDocument Read(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var nuspec = archive.Entries.Single(entry =>
            entry.FullName.EndsWith(
                ".nuspec",
                StringComparison.OrdinalIgnoreCase));
        using var stream = nuspec.Open();
        return XDocument.Load(stream);
    }
}
