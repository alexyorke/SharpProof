using System.Diagnostics;
using System.Runtime.InteropServices;
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
        var root = RepositoryRoot();
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
            Assert.That(portable, Does.Contain("actions/setup-dotnet@"));
            Assert.That(portable, Does.Contain("dotnet-version: 9.0.316"));
            Assert.That(portable, Does.Contain("NUGET_PACKAGES"));
            foreach (var package in new[]
                     {
                         "netstandard.library",
                         "microsoft.netcore.platforms",
                         "microsoft.netframework.referenceassemblies",
                         "microsoft.netframework.referenceassemblies.net472",
                         "microsoft.netcore.app.ref",
                         "microsoft.aspnetcore.app.ref"
                     })
            {
                Assert.That(portable, Does.Contain(package), package);
            }
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
        var sourceRoot = RepositoryRoot();
        var fixture = Directory.CreateTempSubdirectory("sp004-receipts-");
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture.FullName, "scripts"));
            foreach (var name in new[]
                     {
                         "Write-SharpProofQualificationReceipt.ps1",
                         "Test-SharpProofPilotReport.ps1",
                         "SharpProof.MutationEvidence.psm1",
                         "SharpProof.ReleaseConfigurationEvidence.psm1"
                     })
            {
                File.Copy(
                    Path.Combine(sourceRoot, "scripts", name),
                    Path.Combine(fixture.FullName, "scripts", name));
            }
            await RunAsync(fixture.FullName, "git", "init", "-q");
            await RunAsync(fixture.FullName, "git", "config", "user.email", "fixture@example.invalid");
            await RunAsync(fixture.FullName, "git", "config", "user.name", "Fixture");
            await File.WriteAllTextAsync(
                Path.Combine(fixture.FullName, "tracked.txt"),
                "fixture\n");
            await RunAsync(fixture.FullName, "git", "add", ".");
            await RunAsync(fixture.FullName, "git", "commit", "-q", "-m", "fixture");
            var head = (await RunAsync(
                fixture.FullName, "git", "rev-parse", "HEAD")).Trim();
            var evidence = Path.Combine(fixture.FullName, "portable-linux.json");
            var packages = Enumerable.Range(0, 6).Select(index => new
            {
                fileName = $"package-{index}.nupkg",
                bytes = 1,
                sha256 = new string((char)('a' + index), 64)
            }).ToArray();

            async Task<int> WriteAsync(string commit, string osFamily, int count)
            {
                await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    status = "passed",
                    commit,
                    osFamily,
                    architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
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
        finally
        {
            fixture.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReceiptWriterRequiresReviewedPilotEvidence()
    {
        var sourceRoot = RepositoryRoot();
        var fixture = Directory.CreateTempSubdirectory("sp004-pilot-receipt-");
        try
        {
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
            foreach (var name in new[]
                     {
                         "SharpProof.MutationEvidence.psm1",
                         "SharpProof.ReleaseConfigurationEvidence.psm1"
                     })
            {
                File.Copy(
                    Path.Combine(sourceRoot, "scripts", name),
                    Path.Combine(scripts.FullName, name));
            }
            await File.WriteAllTextAsync(
                Path.Combine(scripts.FullName, "Test-SharpProofPilotReport.ps1"),
                "function Test-SharpProofPilotReport { return $true }\n");
            await RunAsync(fixture.FullName, "git", "init", "-q");
            await RunAsync(
                fixture.FullName,
                "git",
                "config",
                "user.email",
                "fixture@example.invalid");
            await RunAsync(
                fixture.FullName,
                "git",
                "config",
                "user.name",
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
            var packages = Enumerable.Range(0, 6).Select(index => new
            {
                fileName = $"package-{index}.nupkg",
                bytes = 1,
                sha256 = new string((char)('a' + index), 64)
            }).ToArray();

            async Task<int> WriteAsync(string reviewStatus, bool automated)
            {
                await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new
                {
                    reviewStatus,
                    packageArtifacts = packages,
                    pilots = Array.Empty<object>()
                }));
                var arguments = new List<string> {
                    "-NoLogo", "-NoProfile", "-File",
                    Path.Combine(
                        scripts.FullName,
                        "Write-SharpProofQualificationReceipt.ps1"),
                    "-Gate", "pilots", "-EvidencePath", evidence
                };
                if (automated)
                {
                    arguments.Add("-Automated");
                }
                return await RunExitCodeAsync(
                    fixture.FullName,
                    "pwsh",
                    [.. arguments]);
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(await WriteAsync("Reviewed", automated: false), Is.Zero);
                Assert.That(await WriteAsync("Unreviewed", automated: true), Is.Zero);
                Assert.That(await WriteAsync("Reviewed", automated: true), Is.Not.Zero);
                Assert.That(await WriteAsync("Unreviewed", automated: false), Is.Not.Zero);
            }
        }
        finally
        {
            fixture.Delete(recursive: true);
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

    private static async Task<string> RunAsync(
        string workingDirectory,
        string executable,
        params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.Zero, output + error);
        return output;
    }

    private static async Task<int> RunExitCodeAsync(
        string workingDirectory,
        string executable,
        params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static string RepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", ".."));
    }

    [GeneratedRegex(
        @"tooling\s+acceptance\s+-Configuration\s+Debug",
        RegexOptions.CultureInvariant)]
    private static partial Regex FoldedCommand();
}
