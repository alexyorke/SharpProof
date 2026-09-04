using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.Package.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class ReleasePublicationScriptTests
{
    private static readonly string[] ExpectedPackageOrder = [
        PackagedProductFeed.AttributesPackageId,
        PackagedProductFeed.PortablePackageId,
        PackagedProductFeed.VerifierPackageId
    ];
    private static readonly string[] s_releaseAuthorityScriptNames = [
        "New-SharpProofReleaseEvidence.ps1",
        "Test-SharpProofReleaseArtifacts.ps1",
        "Publish-SharpProofRelease.ps1",
        "Test-SharpProofPackageConsumers.ps1"
    ];
    private static readonly Lazy<Task<Dictionary<string, string>>>
        s_releaseAuthorityScripts = new(LoadReleaseAuthorityScriptsAsync);

    [Test]
    public async Task PublisherNeverSkipsExistingRemoteArtifacts()
    {
        var script = await File.ReadAllTextAsync(
            Path.Combine(
                TestRepository.FindRoot(),
                "scripts",
                "Publish-SharpProofRelease.ps1"));

        Assert.That(script, Does.Not.Contain("--skip-duplicate"));
    }

    [Test]
    public async Task PublisherUsesTheRepositorySdkPolicyForRealPushes()
    {
        var root = TestRepository.FindRoot();
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
        var root = TestRepository.FindRoot();
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
    public async Task OfflinePlanIsOrderedAndProjectsEveryExistingArtifactCollision()
    {
        var repositoryRoot = TestRepository.FindRoot();
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
        workspace.CopyAllPackages(feed);

        var evidence = await RunProcessAsync(
            repositoryRoot,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                TestRepository.FindRoot(),
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
                mainState: "FixtureAbsent",
                mainAction: "Push",
                symbolsState: "FixtureAbsent",
                symbolsAction: "Push");
        }

        var fixtureSets = new[]
        {
            (Packages: feed.Packages, Kind: "main"),
            (Packages: feed.SymbolPackages, Kind: "symbol")
        };
        foreach (var fixtureSet in fixtureSets)
        {
            foreach (var fixture in fixtureSet.Packages)
            {
                File.Copy(
                    fixture.Path,
                    Path.Combine(
                        workspace.RemoteSource,
                        Path.GetFileName(fixture.Path)));
            }
            try
            {
                var planPath = Path.Combine(
                    workspace.Root,
                    "existing-" + fixtureSet.Kind + "-plan.json");
                var existing = await RunPublicationScriptAsync(
                    workspace,
                    planPath);
                Assert.That(existing.ExitCode, Is.Zero, existing.Output);
                using var existingPlan = JsonDocument.Parse(
                    await File.ReadAllBytesAsync(planPath));
                foreach (var fixture in fixtureSet.Packages)
                {
                    var package = existingPlan.RootElement
                        .GetProperty("packages")
                        .EnumerateArray()
                        .Single(candidate =>
                            candidate.GetProperty("packageId").GetString() ==
                            fixture.Id);
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(
                            package.GetProperty("remoteState").ValueKind,
                            Is.EqualTo(JsonValueKind.Null),
                            fixture.Id);
                        Assert.That(
                            package.GetProperty("mainState").GetString(),
                            Is.EqualTo(fixtureSet.Kind == "main"
                                ? "FixturePresent"
                                : "FixtureAbsent"),
                            fixture.Id);
                        Assert.That(
                            package.GetProperty("mainAction").GetString(),
                            Is.EqualTo(fixtureSet.Kind == "main"
                                ? "Collision"
                                : "Push"),
                            fixture.Id);
                        Assert.That(
                            package.GetProperty("symbolsState").GetString(),
                            Is.EqualTo(fixtureSet.Kind == "symbol"
                                ? "FixturePresent"
                                : "FixtureAbsent"),
                            fixture.Id);
                        Assert.That(
                            package.GetProperty("symbolsAction").GetString(),
                            Is.EqualTo(fixtureSet.Kind == "symbol"
                                ? "Collision"
                                : "Push"),
                            fixture.Id);
                    }
                }
            }
            finally
            {
                foreach (var fixture in fixtureSet.Packages)
                {
                    File.Delete(Path.Combine(
                        workspace.RemoteSource,
                        Path.GetFileName(fixture.Path)));
                }
            }
        }
    }

    [Test]
    public async Task OfflinePlanRejectsPackagesFromDifferentCheckoutCommit()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PublicationWorkspace.Create();
        workspace.CopyAllPackages(feed);

        var previousRevision = await RunProcessAsync(
            TestRepository.FindRoot(),
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
            TestRepository.FindRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                TestRepository.FindRoot(),
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
        var scripts = await GetReleaseAuthorityScriptsAsync();
        foreach (var scriptName in s_releaseAuthorityScriptNames)
        {
            var script = scripts[scriptName];
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
        workspace.CopyAllPackages(feed);

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
                                TestRepository.FindRoot(),
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
            TestRepository.FindRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                TestRepository.FindRoot(),
                "scripts",
                "New-SharpProofReleaseEvidence.ps1"),
            "-PackageSource",
            workspace.PackageSource);
        var expectedFailure = mutation switch
        {
            "missing" => "invalid symbol package layout",
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

    [TestCase("valid")]
    [TestCase("role-swap")]
    [TestCase("renamed-main")]
    [TestCase("renamed-symbols")]
    [TestCase("cross-id")]
    [TestCase("wrong-commit")]
    public async Task ReleasePackageRolesAuthenticateNamesArchivesAndNuspecs(
        string mutation)
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PublicationWorkspace.Create();
        var packageId = PackagedProductFeed.AttributesPackageId;
        var mainSource = feed.Packages.Single(package =>
            package.Id == packageId).Path;
        var symbolsSource = feed.SymbolPackages.Single(package =>
            package.Id == packageId).Path;
        var mainPath = Path.Combine(
            workspace.PackageSource,
            Path.GetFileName(mainSource));
        var symbolsPath = Path.Combine(
            workspace.PackageSource,
            Path.GetFileName(symbolsSource));
        File.Copy(mainSource, mainPath);
        File.Copy(symbolsSource, symbolsPath);

        switch (mutation)
        {
            case "valid":
                break;
            case "role-swap":
                var mainBytes = await File.ReadAllBytesAsync(mainPath);
                var symbolsBytes = await File.ReadAllBytesAsync(symbolsPath);
                await File.WriteAllBytesAsync(mainPath, symbolsBytes);
                await File.WriteAllBytesAsync(symbolsPath, mainBytes);
                break;
            case "renamed-main":
                mainPath += ".renamed";
                File.Move(
                    Path.Combine(
                        workspace.PackageSource,
                        Path.GetFileName(mainSource)),
                    mainPath);
                break;
            case "renamed-symbols":
                symbolsPath += ".renamed";
                File.Move(
                    Path.Combine(
                        workspace.PackageSource,
                        Path.GetFileName(symbolsSource)),
                    symbolsPath);
                break;
            case "cross-id":
                File.Copy(
                    feed.Packages.Single(package =>
                        package.Id == PackagedProductFeed.PortablePackageId).Path,
                    mainPath,
                    overwrite: true);
                break;
            case "wrong-commit":
                RewriteRepositoryCommit(symbolsPath, new string('0', 40));
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(mutation), mutation, "Unknown role mutation.");
        }

        var probePath = Path.Combine(workspace.Root, "role-probe.ps1");
        await File.WriteAllTextAsync(
            probePath,
            "param($Validator,$Main,$Symbols,$Id,$Version,$Commit)\n" +
            "$ErrorActionPreference = 'Stop'\n" +
            ". $Validator\n" +
            "Test-SharpProofSymbolPackagePair -PackagePath $Main " +
            "-SymbolPackagePath $Symbols -PackageId $Id " +
            "-PackageVersion $Version -RepositoryCommit $Commit\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var head = (await RunProcessAsync(
            TestRepository.FindRoot(), "git", "rev-parse", "HEAD")).Output.Trim();
        var result = await RunProcessAsync(
            TestRepository.FindRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            probePath,
            Path.Combine(
                TestRepository.FindRoot(),
                "scripts",
                "Test-SharpProofSymbolPackages.ps1"),
            mainPath,
            symbolsPath,
            packageId,
            feed.Version,
            head);

        if (mutation == "valid")
        {
            Assert.That(result.ExitCode, Is.Zero, result.Output);
        }
        else
        {
            Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        }
    }

    [Test]
    public async Task EveryReleaseAuthorityBindsExactPackageRoles()
    {
        var scripts = await GetReleaseAuthorityScriptsAsync();
        foreach (var scriptName in s_releaseAuthorityScriptNames[..3])
        {
            var script = scripts[scriptName];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    script,
                    Does.Contain("Test-SharpProofSymbolPackagePair"),
                    scriptName);
                Assert.That(
                    script,
                    Does.Contain("-PackageVersion"),
                    scriptName);
            }
        }
    }

    [Test]
    public async Task EveryReleaseAuthorityUsesExactPackagePayloadValidation()
    {
        var scripts = await GetReleaseAuthorityScriptsAsync();
        foreach (var scriptName in s_releaseAuthorityScriptNames[..3])
        {
            var script = scripts[scriptName];
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
    [TestCase("duplicate-first-party")]
    [TestCase("missing-managed")]
    [TestCase("missing-native")]
    [TestCase("valid")]
    public async Task ReleaseEvidenceAuthenticatesExactPackagePayloadClosure(
        string mutation)
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PublicationWorkspace.Create();
        workspace.CopyAllPackages(feed);

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
            TestRepository.FindRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                TestRepository.FindRoot(),
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
        string mainState,
        string mainAction,
        string symbolsState,
        string symbolsAction)
    {
        Assert.That(
            root.GetProperty("schemaVersion").GetInt32(),
            Is.EqualTo(2));
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
            Is.All.Null);
        Assert.That(
            packages.Select(package =>
                package.GetProperty("mainState").GetString()),
            Is.All.EqualTo(mainState));
        Assert.That(
            packages.Select(package =>
                package.GetProperty("mainAction").GetString()),
            Is.All.EqualTo(mainAction));
        Assert.That(
            packages.Select(package =>
                package.GetProperty("symbolsState").GetString()),
            Is.All.EqualTo(symbolsState));
        Assert.That(
            packages.Select(package =>
                package.GetProperty("symbolsAction").GetString()),
            Is.All.EqualTo(symbolsAction));
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
            TestRepository.FindRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                TestRepository.FindRoot(),
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
        var result = await ProcessRunner.RunCapturedAsync(
            workingDirectory,
            fileName,
            arguments);
        return new ProcessResult(
            result.ExitCode,
            result.Output + Environment.NewLine + result.Error);
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
        RewriteEntry(
            archive,
            nuspec,
            entryName,
            stream =>
            {
                using var output = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                document.Save(output);
            });
    }

    private static void RewriteEntry(
        ZipArchive archive,
        ZipArchiveEntry entry,
        string entryName,
        Action<Stream> writeContents)
    {
        entry.Delete();
        var replacement = archive.CreateEntry(
            entryName,
            CompressionLevel.Optimal);
        using var output = replacement.Open();
        writeContents(output);
    }

    private static void RewriteEntry(
        ZipArchive archive,
        ZipArchiveEntry entry,
        string entryName,
        byte[] contents)
    {
        RewriteEntry(
            archive,
            entry,
            entryName,
            stream => stream.Write(contents));
    }

    private static Task<Dictionary<string, string>>
        GetReleaseAuthorityScriptsAsync()
    {
        return s_releaseAuthorityScripts.Value;
    }

    private static async Task<Dictionary<string, string>>
        LoadReleaseAuthorityScriptsAsync()
    {
        var root = TestRepository.FindRoot();
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var scriptName in s_releaseAuthorityScriptNames)
        {
            scripts.Add(
                scriptName,
                await File.ReadAllTextAsync(
                    Path.Combine(root, "scripts", scriptName)));
        }
        return scripts;
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

        internal void CopyAllPackages(PackagedProductFeed feed)
        {
            foreach (var package in feed.Packages.Concat(feed.SymbolPackages))
            {
                File.Copy(
                    package.Path,
                    Path.Combine(
                        PackageSource,
                        Path.GetFileName(package.Path)));
            }
        }

        public void Dispose()
        {
            TestRepository.DeleteOwnedTemporaryDirectory(
                Root,
                Path.GetFileName(_expectedParent),
                "Refusing to remove an unexpected publication directory.");
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
