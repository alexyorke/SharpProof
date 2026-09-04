using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed partial class ReleaseQualificationMatrixTests
{
    private static readonly string[] s_rows =
    [
        "debug-solution", "release-acceptance", "release-configuration",
        "portable-linux", "portable-windows", "portable-macos",
        "repeated-forced-termination", "minimum-sdk", "coverage",
        "mutation", "package-consumers", "pilots"
    ];
    private static readonly string[] s_receipts =
    [
        "acceptance-debug", "acceptance-release", "release-configuration",
        "portable-linux", "portable-windows", "portable-macos",
        "package-consumers", "coverage", "mutation", "pilots"
    ];

    [Test]
    public void WorkflowExecutesTheExactCatalogOwnedQualificationMatrix()
    {
        var root = TestRepository.FindRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "eng", "acceptance", "preview-evidence.v1.json")));
        var matrix = document.RootElement
            .GetProperty("releaseQualificationMatrix")
            .EnumerateArray()
            .Select(row => (
                Id: row.GetProperty("id").GetString(),
                Receipt: row.GetProperty("receipt").GetString()))
            .ToArray();
        Assert.That(matrix.Select(row => row.Id), Is.EqualTo(s_rows));
        Assert.That(
            matrix.Select(row => row.Receipt).Distinct(),
            Is.EqualTo(s_receipts));

        var workflow = File.ReadAllText(Path.Combine(
            root, ".github", "workflows", "package-consumers.yml"));
        var portable = Job(workflow, "portable-consumers", "release-qualification");
        var qualification = Job(
            workflow,
            "release-qualification",
            "publish-private-preview");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(portable, Does.Contain("ubuntu-latest"));
            Assert.That(portable, Does.Contain("windows-latest"));
            Assert.That(portable, Does.Contain("macos-latest"));
            Assert.That(portable, Does.Contain("Test-SharpProofPortableConsumer.ps1"));
            Assert.That(qualification, Does.Contain("- portable-consumers"));
            Assert.That(
                FoldedCommand().IsMatch(qualification),
                Is.True,
                "Debug acceptance command");
            Assert.That(
                qualification,
                Does.Contain("Test-SharpProofReleaseConfiguration.ps1"));
        }

        var dispatcher = File.ReadAllText(Path.Combine(
            root, "scripts", "Invoke-SharpProofContainer.ps1"));
        Assert.That(
            dispatcher,
            Does.Contain("ForcedTerminationDeadlineIsStableAcrossLaunches"));
    }

    [Test]
    public async Task ReceiptWriterRejectsStaleAndPackageMismatchedMatrixRows()
    {
        var sourceRoot = TestRepository.FindRoot();
        using var fixture = new TempDirectory("sp004-receipts-");
        Directory.CreateDirectory(Path.Combine(fixture.FullName, "scripts"));
        foreach (var name in new[]
                 {
                         "Write-SharpProofQualificationReceipt.ps1",
                         "Test-SharpProofPilotReport.ps1",
                         "SharpProof.ReleaseJson.ps1",
                         "SharpProof.PackageIdentity.psm1"
                     })
        {
            File.Copy(
                Path.Combine(sourceRoot, "scripts", name),
                Path.Combine(fixture.FullName, "scripts", name));
        }
        await ArchitectureGitRepository.InitializeAsync(
            fixture.FullName,
            "fixture@example.invalid",
            "Fixture");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.FullName, "tracked.txt"),
            "fixture\n");
        await RunAsync(fixture.FullName, "git", "add", ".");
        await RunAsync(fixture.FullName, "git", "commit", "-q", "-m", "fixture");
        var head = (await RunAsync(
            fixture.FullName, "git", "rev-parse", "HEAD")).Trim();
        var evidence = Path.Combine(fixture.FullName, "portable-linux.json");
        var packages = CreatePackageArtifacts();

        async Task<int> WriteAsync(string commit, string osFamily, int count)
        {
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                status = "passed",
                commit,
                osFamily,
                packageArtifacts = packages.Take(count)
            }));
            return await RunExitCodeAsync(
                fixture.FullName,
                "pwsh", "-NoLogo", "-NoProfile", "-File",
                Path.Combine(
                    fixture.FullName,
                    "scripts",
                    "Write-SharpProofQualificationReceipt.ps1"),
                "-Gate", "portable-linux", "-EvidencePath", evidence);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await WriteAsync(head, "linux", 6), Is.Zero);
            Assert.That(await WriteAsync(new string('0', 40), "linux", 6), Is.Not.Zero);
            Assert.That(await WriteAsync(head, "windows", 6), Is.Not.Zero);
            Assert.That(await WriteAsync(head, "linux", 5), Is.Not.Zero);
        }
    }

    [Test]
    public async Task ReceiptWriterRequiresReviewedPilotEvidence()
    {
        var sourceRoot = TestRepository.FindRoot();
        using var fixture = new TempDirectory("sp004-pilot-receipt-");
        var scripts = Directory.CreateDirectory(Path.Combine(
            fixture.FullName,
            "scripts"));
        File.Copy(
            Path.Combine(
                sourceRoot,
                "scripts",
                "Write-SharpProofQualificationReceipt.ps1"),
            Path.Combine(
                scripts.FullName,
                "Write-SharpProofQualificationReceipt.ps1"));
        File.Copy(
            Path.Combine(sourceRoot, "scripts", "SharpProof.ReleaseJson.ps1"),
            Path.Combine(scripts.FullName, "SharpProof.ReleaseJson.ps1"));
        File.Copy(
            Path.Combine(sourceRoot, "scripts", "SharpProof.PackageIdentity.psm1"),
            Path.Combine(scripts.FullName, "SharpProof.PackageIdentity.psm1"));
        await File.WriteAllTextAsync(
            Path.Combine(scripts.FullName, "Test-SharpProofPilotReport.ps1"),
            "function Test-SharpProofPilotReport { return $true }\n");
        await ArchitectureGitRepository.InitializeAsync(
            fixture.FullName,
            "fixture@example.invalid",
            "Fixture");
        await File.WriteAllTextAsync(
            Path.Combine(fixture.FullName, "tracked.txt"),
            "fixture\n");
        await RunAsync(fixture.FullName, "git", "add", "tracked.txt");
        await RunAsync(
            fixture.FullName,
            "git",
            "commit",
            "-q",
            "-m",
            "fixture");
        var evidence = Path.Combine(fixture.FullName, "pilots.json");
        var packages = CreatePackageArtifacts();

        async Task<int> WriteAsync(string reviewStatus)
        {
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new
            {
                reviewStatus,
                packageArtifacts = packages,
                pilots = Array.Empty<object>()
            }));
            return await RunExitCodeAsync(
                fixture.FullName,
                "pwsh", "-NoLogo", "-NoProfile", "-File",
                Path.Combine(
                    scripts.FullName,
                    "Write-SharpProofQualificationReceipt.ps1"),
                "-Gate", "pilots", "-EvidencePath", evidence);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await WriteAsync("Reviewed"), Is.Zero);
            Assert.That(await WriteAsync("Unreviewed"), Is.Not.Zero);
        }
    }

    private static string Job(string workflow, string name, string next)
    {
        var start = workflow.IndexOf("  " + name + ":", StringComparison.Ordinal);
        var end = workflow.IndexOf("  " + next + ":", start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), name);
        Assert.That(end, Is.GreaterThan(start), next);
        return workflow[start..end];
    }

    private sealed record PackageArtifact(string fileName, int bytes);

    private static PackageArtifact[] CreatePackageArtifacts()
    {
        return Enumerable.Range(0, 6)
            .Select(index => new PackageArtifact($"package-{index}.nupkg", 1))
            .ToArray();
    }

    private static async Task<string> RunAsync(
        string workingDirectory,
        string executable,
        params string[] arguments)
    {
        var result = await ArchitectureRepository.RunProcessAsync(
            workingDirectory,
            executable,
            arguments);
        Assert.That(
            result.ExitCode,
            Is.Zero,
            result.Output + Environment.NewLine + result.Error);
        return result.Output;
    }

    private static async Task<int> RunExitCodeAsync(
        string workingDirectory,
        string executable,
        params string[] arguments)
    {
        return (await ArchitectureRepository.RunProcessAsync(
            workingDirectory,
            executable,
            arguments)).ExitCode;
    }

    [GeneratedRegex(
        @"tooling\s+acceptance\s+-Configuration\s+Debug",
        RegexOptions.CultureInvariant)]
    private static partial Regex FoldedCommand();

}
