using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class ReleasePublicationScriptTests
{
    private static readonly string[] ExpectedPackageOrder = [
        PackagedProductFeed.AttributesPackageId,
        PackagedProductFeed.PortablePackageId,
        PackagedProductFeed.VerifierPackageId
    ];

    [Test]
    public async Task PublisherNeverSkipsExistingRemoteArtifacts()
    {
        var script = await File.ReadAllTextAsync(
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "Publish-SharpProofRelease.ps1"));

        Assert.That(script, Does.Not.Contain("--skip-duplicate"));
        Assert.That(
            script,
            Does.Contain("Remote main package already exists"));
        Assert.That(
            script,
            Does.Contain("Remote symbol package already exists"));
    }

    [Test]
    public async Task PublisherUsesTheRepositorySdkPolicyForRealPushes()
    {
        var root = FindRepositoryRoot();
        var script = await File.ReadAllTextAsync(
            Path.Combine(root, "scripts", "Publish-SharpProofRelease.ps1"));
        var globalJson = await File.ReadAllTextAsync(
            Path.Combine(root, "global.json"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Contain("Get-RepositorySdkVersion"));
            Assert.That(script, Does.Contain("Resolve-ReleaseDotNet"));
            Assert.That(script, Does.Contain("--version"));
            Assert.That(script, Does.Contain("project-local"));
            Assert.That(globalJson, Does.Contain("9.0.316"));
        }
    }

    [Test]
    public async Task PublicationDocumentationDescribesFailClosedDuplicates()
    {
        var root = FindRepositoryRoot();
        var documentationPaths = new[]
        {
            Path.Combine(root, "README.md"),
            Path.Combine(root, "docs", "README.md"),
            Path.Combine(root, "docs", "coverage-and-limits.md"),
            Path.Combine(root, "docs", "native-smt-packaging.md")
        };
        foreach (var path in documentationPaths)
        {
            var documentation = await File.ReadAllTextAsync(path);
            var normalized = Normalize(documentation);
            var policy = PublicationPolicy(documentation);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    normalized,
                    Does.Not.Contain("USE DUPLICATE SKIPPING"),
                    path);
                Assert.That(
                    normalized,
                    Does.Not.Contain("DUPLICATE SKIPPING IS ENABLED"),
                    path);
                Assert.That(
                    normalized,
                    Does.Not.Contain("EXISTING V3 PACKAGES ARE ACCEPTED"),
                    path);
                Assert.That(
                    normalized,
                    Does.Not.Contain("VERIFIED RETRIES"),
                    path);
                Assert.That(
                    DescribesAbsentMainPackages(policy),
                    Is.True,
                    path);
                Assert.That(
                    DescribesNoDuplicateSkipping(policy),
                    Is.True,
                    path);
                Assert.That(
                    DescribesNewVersionAfterPartialPublication(policy),
                    Is.True,
                    path);
            }
        }
    }

    [Test]
    public async Task OfflinePlanIsOrderedAndRejectsEveryExistingArtifact()
    {
        var repositoryRoot = FindRepositoryRoot();
        var repositoryHead = await RunProcessAsync(
            repositoryRoot,
            "git",
            "rev-parse",
            "HEAD");
        Assert.That(
            repositoryHead.ExitCode,
            Is.Zero,
            repositoryHead.Output);
        var expectedRepositoryCommit = repositoryHead.Output.Trim();
        Assert.That(
            expectedRepositoryCommit,
            Does.Match("^[0-9a-f]{40}$"));

        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PublicationWorkspace.Create();
        foreach (var package in feed.Packages.Concat(feed.SymbolPackages))
        {
            File.Copy(
                package.Path,
                Path.Combine(
                    workspace.PackageSource,
                    Path.GetFileName(package.Path)));
        }

        var evidence = await RunProcessAsync(
            repositoryRoot,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "New-SharpProofReleaseEvidence.ps1"),
            "-PackageSource",
            workspace.PackageSource);
        Assert.That(evidence.ExitCode, Is.Zero, evidence.Output);

        using (var absentPlan = await RunPlanAsync(workspace))
        {
            AssertPlan(
                absentPlan.RootElement,
                expectedRepositoryCommit,
                remoteState: "Absent",
                action: "Push");
        }

        var fixtures = feed.Packages
            .Select(static package => (Package: package, Kind: "main"))
            .Concat(feed.SymbolPackages.Select(static package =>
                (Package: package, Kind: "symbol")));
        foreach (var fixture in fixtures)
        {
            var remotePath = Path.Combine(
                workspace.RemoteSource,
                Path.GetFileName(fixture.Package.Path));
            File.Copy(fixture.Package.Path, remotePath);
            try
            {
                var existing = await RunPublicationScriptAsync(
                    workspace,
                    Path.Combine(
                        workspace.Root,
                        "existing-" + fixture.Kind + "-" +
                        fixture.Package.Id + "-plan.json"));
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(
                        existing.ExitCode,
                        Is.Not.Zero,
                        existing.Output);
                    Assert.That(
                        existing.Output,
                        Does.Contain(
                            fixture.Kind == "main"
                                ? "Remote main package already exists"
                                : "Remote symbol package already exists"));
                    Assert.That(
                        existing.Output,
                        Does.Contain(fixture.Package.Id));
                    Assert.That(
                        existing.Output,
                        Does.Contain(fixture.Package.Version));
                }
            }
            finally
            {
                File.Delete(remotePath);
            }
        }
    }

    [Test]
    public async Task OfflinePlanRejectsPackagesFromDifferentCheckoutCommit()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PublicationWorkspace.Create();
        foreach (var package in feed.Packages.Concat(feed.SymbolPackages))
        {
            var destination = Path.Combine(
                workspace.PackageSource,
                Path.GetFileName(package.Path));
            File.Copy(package.Path, destination);
        }

        var previousRevision = await RunProcessAsync(
            FindRepositoryRoot(),
            "git",
            "rev-parse",
            "HEAD^");
        Assert.That(
            previousRevision.ExitCode,
            Is.Zero,
            previousRevision.Output);
        var staleCommit = previousRevision.Output.Trim();
        Assert.That(staleCommit, Does.Match("^[0-9a-f]{40}$"));

        foreach (var path in Directory.EnumerateFiles(
                     workspace.PackageSource,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            if (Path.GetExtension(path) is ".nupkg" or ".snupkg")
            {
                RewriteRepositoryCommit(path, staleCommit);
            }
        }

        var evidence = await RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "New-SharpProofReleaseEvidence.ps1"),
            "-PackageSource",
            workspace.PackageSource);
        Assert.That(evidence.ExitCode, Is.Not.Zero, evidence.Output);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evidence.Output,
                Does.Contain("does not match checkout"));
            Assert.That(
                Directory.EnumerateFiles(workspace.RemoteSource),
                Is.Empty);
        }
    }

    [Test]
    public async Task EveryReleaseAuthorityUsesStrictSymbolValidation()
    {
        var root = FindRepositoryRoot();
        foreach (var scriptName in new[]
                 {
                     "New-SharpProofReleaseEvidence.ps1",
                     "Test-SharpProofReleaseArtifacts.ps1",
                     "Publish-SharpProofRelease.ps1",
                     "Test-SharpProofPackageConsumers.ps1"
                 })
        {
            var script = await File.ReadAllTextAsync(
                Path.Combine(root, "scripts", scriptName));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    script,
                    Does.Contain("Test-SharpProofSymbolPackages.ps1"),
                    scriptName);
                Assert.That(
                    script,
                    Does.Contain("Test-SharpProofSymbolPackagePair"),
                    scriptName);
            }
        }
    }

    [TestCase("missing")]
    [TestCase("foreign")]
    [TestCase("wrong-commit")]
    [TestCase("duplicate")]
    [TestCase("malformed")]
    public async Task ReleaseEvidenceRejectsInvalidSymbolPayload(
        string mutation)
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PublicationWorkspace.Create();
        foreach (var package in feed.Packages.Concat(feed.SymbolPackages))
        {
            File.Copy(
                package.Path,
                Path.Combine(
                    workspace.PackageSource,
                    Path.GetFileName(package.Path)));
        }

        var symbolsPath = Path.Combine(
            workspace.PackageSource,
            Path.GetFileName(feed.GetSymbolPackagePath(
                PackagedProductFeed.AttributesPackageId)));
        using (var symbols = ZipFile.Open(
                   symbolsPath,
                   ZipArchiveMode.Update))
        {
            var pdb = symbols.Entries.Single(entry =>
                entry.FullName.EndsWith(
                    ".pdb",
                    StringComparison.Ordinal));
            var pdbName = pdb.FullName;
            switch (mutation)
            {
                case "missing":
                    pdb.Delete();
                    break;
                case "foreign":
                    {
                        var foreignPath = Path.Combine(
                            workspace.PackageSource,
                            Path.GetFileName(feed.GetSymbolPackagePath(
                                PackagedProductFeed.PortablePackageId)));
                        using var foreign = ZipFile.OpenRead(foreignPath);
                        var foreignPdb = foreign.Entries.First(entry =>
                            entry.FullName.EndsWith(
                                ".pdb",
                                StringComparison.Ordinal));
                        using var image = new MemoryStream();
                        using (var input = foreignPdb.Open())
                        {
                            await input.CopyToAsync(image);
                        }
                        RewriteEntry(symbols, pdb, pdbName, image.ToArray());
                        break;
                    }
                case "wrong-commit":
                    {
                        using var image = new MemoryStream();
                        using (var input = pdb.Open())
                        {
                            await input.CopyToAsync(image);
                        }
                        var bytes = image.ToArray();
                        var head = Encoding.ASCII.GetBytes(
                            (await RunProcessAsync(
                                FindRepositoryRoot(),
                                "git",
                                "rev-parse",
                                "HEAD")).Output.Trim());
                        var offset = bytes.AsSpan().IndexOf(head);
                        Assert.That(offset, Is.GreaterThanOrEqualTo(0));
                        Encoding.ASCII.GetBytes(new string('0', 40))
                            .CopyTo(bytes, offset);
                        RewriteEntry(symbols, pdb, pdbName, bytes);
                        break;
                    }
                case "duplicate":
                    {
                        using var image = new MemoryStream();
                        using (var input = pdb.Open())
                        {
                            await input.CopyToAsync(image);
                        }
                        var duplicate = symbols.CreateEntry(pdbName);
                        await using var output = duplicate.Open();
                        await output.WriteAsync(image.ToArray());
                        break;
                    }
                case "malformed":
                    RewriteEntry(
                        symbols,
                        pdb,
                        pdbName,
                        Encoding.ASCII.GetBytes("not-a-portable-pdb"));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(mutation),
                        mutation,
                        "Unknown symbol mutation.");
            }
        }

        var evidence = await RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "New-SharpProofReleaseEvidence.ps1"),
            "-PackageSource",
            workspace.PackageSource);
        var expectedFailure = mutation switch
        {
            "missing" => "exact PDB entry set",
            "foreign" => "debug identifier",
            "wrong-commit" => "canonical repository commit",
            "duplicate" => "duplicate entry",
            "malformed" => "portable PDB",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        using (Assert.EnterMultipleScope())
        {
            Assert.That(evidence.ExitCode, Is.Not.Zero, evidence.Output);
            Assert.That(
                evidence.Output,
                Does.Contain(expectedFailure).IgnoreCase,
                evidence.Output);
            Assert.That(
                evidence.Output,
                Does.Not.Contain("Unable to find type"),
                evidence.Output);
        }
    }

    [Test]
    public async Task EveryReleaseAuthorityUsesExactPackagePayloadValidation()
    {
        var root = FindRepositoryRoot();
        foreach (var scriptName in new[]
                 {
                     "New-SharpProofReleaseEvidence.ps1",
                     "Test-SharpProofReleaseArtifacts.ps1",
                     "Publish-SharpProofRelease.ps1"
                 })
        {
            var script = await File.ReadAllTextAsync(
                Path.Combine(root, "scripts", scriptName));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    script,
                    Does.Contain("Test-SharpProofPackagePayloads.ps1"),
                    scriptName);
                Assert.That(
                    script,
                    Does.Contain("Test-SharpProofPackagePayload"),
                    scriptName);
            }
        }
    }

    [TestCase("foreign")]
    [TestCase("z3-byte")]
    [TestCase("duplicate-first-party")]
    [TestCase("missing-managed")]
    [TestCase("missing-native")]
    [TestCase("valid")]
    public async Task ReleaseEvidenceAuthenticatesExactPackagePayloadClosure(
        string mutation)
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PublicationWorkspace.Create();
        foreach (var package in feed.Packages.Concat(feed.SymbolPackages))
        {
            File.Copy(
                package.Path,
                Path.Combine(
                    workspace.PackageSource,
                    Path.GetFileName(package.Path)));
        }

        if (mutation != "valid")
        {
            var packageId = mutation == "duplicate-first-party" ||
                mutation == "missing-managed"
                ? PackagedProductFeed.AttributesPackageId
                : PackagedProductFeed.VerifierPackageId;
            var packagePath = Path.Combine(
                workspace.PackageSource,
                Path.GetFileName(feed.Packages.Single(package =>
                    package.Id == packageId).Path));
            using var archive = ZipFile.Open(
                packagePath,
                ZipArchiveMode.Update);
            switch (mutation)
            {
                case "foreign":
                    {
                        var entry = archive.CreateEntry(
                            "tools/net9/SharpProof.Untracked.dll");
                        await using var output = entry.Open();
                        await output.WriteAsync(new byte[] { 1, 2, 3, 4 });
                        break;
                    }
                case "z3-byte":
                    {
                        var entry = archive.GetEntry(
                            "tools/native/linux-x64/libz3.so")!;
                        using var image = new MemoryStream();
                        await using (var input = entry.Open())
                        {
                            await input.CopyToAsync(image);
                        }
                        var bytes = image.ToArray();
                        bytes[^1] ^= 0x01;
                        RewriteEntry(
                            archive,
                            entry,
                            "tools/native/linux-x64/libz3.so",
                            bytes);
                        break;
                    }
                case "duplicate-first-party":
                    {
                        var entry = archive.GetEntry(
                            "lib/netstandard2.0/SharpProof.Attributes.dll")!;
                        using var image = new MemoryStream();
                        await using (var input = entry.Open())
                        {
                            await input.CopyToAsync(image);
                        }
                        var duplicate = archive.CreateEntry(
                            "tools/net9/SharpProof.Attributes.dll");
                        await using var output = duplicate.Open();
                        await output.WriteAsync(image.ToArray());
                        break;
                    }
                case "missing-managed":
                    archive.GetEntry(
                        "lib/netstandard2.0/SharpProof.Attributes.dll")!
                        .Delete();
                    break;
                case "missing-native":
                    archive.GetEntry(
                        "tools/native/linux-x64/libz3.so")!
                        .Delete();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(mutation),
                        mutation,
                        "Unknown payload mutation.");
            }
        }

        var evidence = await RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "New-SharpProofReleaseEvidence.ps1"),
            "-PackageSource",
            workspace.PackageSource);
        if (mutation == "valid")
        {
            Assert.That(evidence.ExitCode, Is.Zero, evidence.Output);
            return;
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(evidence.ExitCode, Is.Not.Zero, evidence.Output);
            Assert.That(
                evidence.Output,
                Does.Contain("payload").IgnoreCase,
                evidence.Output);
        }
    }

    private static string PublicationPolicy(string documentation)
    {
        var paragraphs = Regex.Split(
                documentation,
                @"(?:\r?\n)[ \t]*(?:\r?\n)")
            .Where(paragraph =>
                paragraph.Contains(
                    "--skip-duplicate",
                    StringComparison.OrdinalIgnoreCase) ||
                paragraph.Contains(
                    "duplicate skipping",
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.That(
            paragraphs,
            Is.Not.Empty,
            "Publication documentation has no duplicate policy.");
        return Normalize(string.Join(" ", paragraphs));
    }

    private static string Normalize(string value)
    {
        return Regex.Replace(
                value.Replace("`", string.Empty, StringComparison.Ordinal),
                @"\s+",
                " ")
            .ToUpperInvariant();
    }

    private static bool DescribesAbsentMainPackages(string policy)
    {
        return (policy.Contains(
                    "MAIN PACKAGE",
                    StringComparison.Ordinal) ||
                policy.Contains(
                    "MAIN-PACKAGE",
                    StringComparison.Ordinal)) &&
            (policy.Contains("REJECT", StringComparison.Ordinal) ||
             policy.Contains("FAIL", StringComparison.Ordinal) ||
             policy.Contains("ABSENT", StringComparison.Ordinal) ||
             policy.Contains("ABSENCE", StringComparison.Ordinal));
    }

    private static bool DescribesNoDuplicateSkipping(string policy)
    {
        return policy.Contains(
                "WITHOUT DUPLICATE SKIPPING",
                StringComparison.Ordinal) ||
            policy.Contains(
                "DUPLICATE SKIPPING IS NEVER USED",
                StringComparison.Ordinal) ||
            policy.Contains("NO PUSH USES", StringComparison.Ordinal) &&
            (policy.Contains(
                    "DUPLICATE SKIPPING",
                    StringComparison.Ordinal) ||
                policy.Contains(
                    "--skip-duplicate",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool DescribesNewVersionAfterPartialPublication(
        string policy)
    {
        return (policy.Contains("PARTIAL", StringComparison.Ordinal) ||
                policy.Contains("INTERRUPTED", StringComparison.Ordinal) ||
                policy.Contains("CONFLICTING", StringComparison.Ordinal) ||
                policy.Contains("COLLISION", StringComparison.Ordinal)) &&
            (policy.Contains("NEW VERSION", StringComparison.Ordinal) ||
             policy.Contains(
                 "NEW PACKAGE VERSION",
                 StringComparison.Ordinal));
    }

    private static void AssertPlan(
        JsonElement root,
        string expectedRepositoryCommit,
        string remoteState,
        string action)
    {
        Assert.That(
            root.GetProperty("schemaVersion").GetInt32(),
            Is.EqualTo(1));
        Assert.That(
            root.GetProperty("planOnly").GetBoolean(),
            Is.True);
        Assert.That(
            root.GetProperty("repositoryCommit").GetString(),
            Is.EqualTo(expectedRepositoryCommit));
        var packages = root.GetProperty("packages")
            .EnumerateArray()
            .ToArray();
        Assert.That(
            packages.Select(package =>
                package.GetProperty("packageId").GetString()),
            Is.EqualTo(ExpectedPackageOrder));
        Assert.That(
            packages.Select(package =>
                package.GetProperty("remoteState").GetString()),
            Is.All.EqualTo(remoteState));
        Assert.That(
            packages.Select(package =>
                package.GetProperty("mainAction").GetString()),
            Is.All.EqualTo(action));
        Assert.That(
            packages.Select(package =>
                package.GetProperty("symbolsAction").GetString()),
            Is.All.EqualTo(action));
    }

    private static async Task<JsonDocument> RunPlanAsync(
        PublicationWorkspace workspace)
    {
        var planPath = Path.Combine(
            workspace.Root,
            "plan-" + Guid.NewGuid().ToString("N") + ".json");
        var result = await RunPublicationScriptAsync(workspace, planPath);
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        return JsonDocument.Parse(
            await File.ReadAllBytesAsync(planPath));
    }

    private static Task<ProcessResult> RunPublicationScriptAsync(
        PublicationWorkspace workspace,
        string planPath)
    {
        return RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "Publish-SharpProofRelease.ps1"),
            "-PackageSource",
            workspace.PackageSource,
            "-PlanOnly",
            "-RemotePackageDirectory",
            workspace.RemoteSource,
            "-PlanOutputPath",
            planPath,
            "-DotNetPath",
            "dotnet-must-not-run-in-plan-only");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
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

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            (await standardOutput) + Environment.NewLine +
            (await standardError));
    }

    private static void RewriteRepositoryCommit(
        string packagePath,
        string commit)
    {
        using var archive = ZipFile.Open(
            packagePath,
            ZipArchiveMode.Update);
        var nuspec = archive.Entries.Single(entry =>
            entry.FullName.EndsWith(
                ".nuspec",
                StringComparison.OrdinalIgnoreCase));
        var entryName = nuspec.FullName;
        XDocument document;
        using (var stream = nuspec.Open())
        {
            document = XDocument.Load(stream);
        }
        var repository = document.Descendants().Single(element =>
            element.Name.LocalName == "repository");
        repository.SetAttributeValue("commit", commit);
        nuspec.Delete();
        var replacement = archive.CreateEntry(
            entryName,
            CompressionLevel.Optimal);
        using var output = new StreamWriter(
            replacement.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(output);
    }

    private static void RewriteEntry(
        ZipArchive archive,
        ZipArchiveEntry entry,
        string entryName,
        byte[] contents)
    {
        entry.Delete();
        var replacement = archive.CreateEntry(
            entryName,
            CompressionLevel.Optimal);
        using var output = replacement.Open();
        output.Write(contents);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            typeof(ReleasePublicationScriptTests).Assembly.Location);
        while (directory != null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "SharpProof.Release.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Repository root was not found.");
    }

    private sealed class PublicationWorkspace : IDisposable
    {
        private readonly string _expectedParent;

        private PublicationWorkspace(string root, string expectedParent)
        {
            Root = root;
            _expectedParent = expectedParent;
            PackageSource = Path.Combine(root, "packages");
            RemoteSource = Path.Combine(root, "remote");
            Directory.CreateDirectory(PackageSource);
            Directory.CreateDirectory(RemoteSource);
        }

        internal string Root
        {
            get;
        }
        internal string PackageSource
        {
            get;
        }
        internal string RemoteSource
        {
            get;
        }

        internal static PublicationWorkspace Create()
        {
            var parent = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "SharpProof.ReleasePublication"));
            Directory.CreateDirectory(parent);
            var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new PublicationWorkspace(root, parent);
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            var relative = Path.GetRelativePath(_expectedParent, resolved);
            if (Path.IsPathRooted(relative) ||
                relative == "." ||
                relative == ".." ||
                relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected publication directory.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
