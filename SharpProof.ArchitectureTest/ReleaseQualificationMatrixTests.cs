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
    public async Task WorkflowExecutesTheExactCatalogOwnedQualificationMatrix()
    {
        var root = TestRepository.FindRoot();
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
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

        var projected = await RunAsync(root, "pwsh", "-NoLogo", "-NoProfile", "-Command",
            """
            $ErrorActionPreference = 'Stop'
            $matrix = Get-Content eng/acceptance/preview-evidence.v1.json -Raw | ConvertFrom-Json
            $ast = [System.Management.Automation.Language.Parser]::ParseFile(
                (Join-Path $PWD 'scripts/Invoke-SharpProofReleaseContainer.ps1'),
                [ref]$null, [ref]$null)
            $assignments = @($ast.FindAll({ param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                $node.Left.Extent.Text -ceq '$requiredGates'
            }, $true))
            if ($assignments.Count -ne 1) { throw 'Expected one receipt projection.' }
            . ([scriptblock]::Create($assignments[0].Extent.Text))
            ConvertTo-Json -InputObject @($requiredGates) -Compress
            """);
        Assert.That(JsonSerializer.Deserialize<string[]>(projected), Is.EqualTo(s_receipts));

        var workflow = await File.ReadAllTextAsync(Path.Combine(
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

        var dispatcher = await File.ReadAllTextAsync(Path.Combine(
            root, "scripts", "Invoke-SharpProofContainer.ps1"));
        Assert.That(
            dispatcher,
            Does.Contain("ForcedTerminationDeadlineIsStableAcrossLaunches"));
    }

    [Test]
    public async Task PilotReviewResumeUsesTheOriginalRunAndGatesPublication()
    {
        var root = TestRepository.FindRoot();
        var workflow = await File.ReadAllTextAsync(Path.Combine(
            root, ".github", "workflows", "package-consumers.yml"));
        var package = Job(workflow, "package", "container-verifier");
        var portable = Job(workflow, "portable-consumers", "release-qualification");
        var qualification = Job(
            workflow,
            "release-qualification",
            "publish-private-preview");
        var privatePublish = Job(workflow, "publish-private-preview", "publish");
        var publicPublish = workflow[
            workflow.IndexOf("  publish:\n", StringComparison.Ordinal)..];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workflow, Does.Contain("pilot_review_source_run_id"));
            Assert.That(workflow, Does.Contain("pilot_review_ledger"));
            Assert.That(package, Does.Contain(".head_sha == $sha"));
            Assert.That(package, Does.Contain(".event == \"push\""));
            Assert.That(package, Does.Contain(".conclusion == \"success\""));
            Assert.That(package, Does.Contain("pilot-review-packages-${{ github.sha }}"));
            Assert.That(portable, Does.Contain("inputs.pilot_review_ledger != ''"));
            Assert.That(qualification, Does.Contain("pilot-review-report-${{ github.sha }}"));
            Assert.That(qualification, Does.Contain(
                "eng/pilots/*/obj/Release/net8.0/SharpProof/result.json"));
            Assert.That(qualification, Does.Contain("path: ."));
            Assert.That(qualification, Does.Contain("New-SharpProofPilotReviewLedger.ps1"));
            Assert.That(qualification, Does.Contain("tooling pilot-review"));
            Assert.That(qualification, Does.Contain("inputs.pilot_review_ledger != ''"));
            Assert.That(qualification, Does.Contain(
                "qualified: ${{ steps.mark-qualified.outputs.qualified }}"));
            Assert.That(qualification, Does.Contain("id: mark-qualified"));
            Assert.That(privatePublish, Does.Contain(
                "needs.release-qualification.outputs.qualified == 'true'"));
            Assert.That(publicPublish, Does.Contain(
                "needs.release-qualification.outputs.qualified == 'true'"));
        }
    }

    [Test]
    public async Task WorkflowArtifactsSurvivePartialRerunsAndFollowProducerNeeds()
    {
        var root = TestRepository.FindRoot();
        var workflow = await File.ReadAllTextAsync(Path.Combine(
            root, ".github", "workflows", "package-consumers.yml"));
        var packageAction = await File.ReadAllTextAsync(Path.Combine(
            root, ".github", "actions", "prepare-qualified-packages", "action.yml"));
        var package = Job(workflow, "package", "container-verifier");
        var container = Job(workflow, "container-verifier", "portable-consumers");
        var portable = Job(workflow, "portable-consumers", "release-qualification");
        var qualification = Job(
            workflow,
            "release-qualification",
            "publish-private-preview");
        var privatePublish = Job(workflow, "publish-private-preview", "publish");
        var publicPublishStart = workflow.IndexOf(
            "  publish:\n",
            StringComparison.Ordinal);
        Assert.That(publicPublishStart, Is.GreaterThanOrEqualTo(0));
        var publicPublish = workflow[publicPublishStart..];
        var packageUpload = Step(package, "Upload exact NuGet artifacts");
        var consumerUpload = Step(
            container,
            "Upload package-consumer qualification evidence");

        const string packages =
            "nuget-packages-${{ github.run_id }}-${{ github.sha }}";
        const string consumers =
            "package-consumer-qualification-${{ github.run_id }}-${{ github.sha }}";
        const string portableReceipt =
            "portable-receipt-${{ matrix.family }}-${{ github.run_id }}-${{ github.sha }}";

        static string ResolveName(string template, string attempt)
        {
            return template
                .Replace("${{ github.run_id }}", "12345", StringComparison.Ordinal)
                .Replace("${{ github.sha }}", "abcdef", StringComparison.Ordinal)
                .Replace("${{ github.run_attempt }}", attempt, StringComparison.Ordinal);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(packageUpload, Does.Contain($"name: {packages}"));
            Assert.That(packageUpload, Does.Contain("overwrite: true"));
            Assert.That(packageAction, Does.Contain($"name: {packages}"));
            Assert.That(packageAction, Does.Not.Contain("github.run_attempt"));
            Assert.That(ResolveName(packages, "2"), Is.EqualTo(ResolveName(packages, "1")));
            Assert.That(ResolveName(consumers, "2"), Is.EqualTo(ResolveName(consumers, "1")));
            Assert.That(ResolveName(portableReceipt, "2"), Is.EqualTo(ResolveName(portableReceipt, "1")));

            Assert.That(container, Does.Contain("needs: package"));
            Assert.That(container, Does.Contain("./.github/actions/prepare-qualified-packages"));
            Assert.That(consumerUpload, Does.Contain($"name: {consumers}"));
            Assert.That(consumerUpload, Does.Contain("overwrite: true"));

            Assert.That(portable, Does.Contain("needs: package"));
            Assert.That(portable, Does.Contain($"name: {packages}"));
            Assert.That(portable, Does.Contain($"name: {portableReceipt}"));
            Assert.That(portable, Does.Contain("overwrite: true"));

            Assert.That(qualification, Does.Contain("- container-verifier"));
            Assert.That(qualification, Does.Contain("- portable-consumers"));
            Assert.That(qualification, Does.Contain("- package"));
            Assert.That(qualification, Does.Contain("./.github/actions/prepare-qualified-packages"));
            Assert.That(qualification, Does.Contain($"name: {consumers}"));
            Assert.That(qualification, Does.Contain(
                "name: release-qualification-${{ github.sha }}-${{ github.run_attempt }}"));
            Assert.That(qualification, Does.Contain(
                "pattern: portable-receipt-*-${{ github.run_id }}-${{ github.sha }}"));
            Assert.That(qualification, Does.Not.Contain(
                "package-consumer-qualification-${{ github.sha }}-${{ github.run_attempt }}"));
            Assert.That(qualification, Does.Not.Contain(
                "portable-receipt-*-${{ github.sha }}-${{ github.run_attempt }}"));

            Assert.That(privatePublish, Does.Contain("needs: release-qualification"));
            Assert.That(privatePublish, Does.Contain("./.github/actions/prepare-qualified-packages"));
            Assert.That(publicPublish, Does.Contain("needs: release-qualification"));
            Assert.That(publicPublish, Does.Contain("./.github/actions/prepare-qualified-packages"));
        }
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
                         "SharpProof.ReleaseBundle.ps1",
                         "Test-SharpProofPilotReport.ps1",
                         "SharpProof.ReleaseJson.ps1",
                         "Resolve-SharpProofContainedPath.ps1",
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
                schemaVersion = 2,
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
    public async Task ReceiptWriterBindsTheValidatedEvidenceSnapshot()
    {
        var sourceRoot = TestRepository.FindRoot();
        using var fixture = new TempDirectory("qualification-snapshot-");
        var scripts = Directory.CreateDirectory(Path.Combine(
            fixture.FullName,
            "scripts"));
        foreach (var name in new[]
                 {
                         "Write-SharpProofQualificationReceipt.ps1",
                         "SharpProof.ReleaseBundle.ps1",
                         "Test-SharpProofPilotReport.ps1",
                         "SharpProof.ReleaseJson.ps1",
                         "SharpProof.PackageIdentity.psm1"
                     })
        {
            File.Copy(
                Path.Combine(sourceRoot, "scripts", name),
                Path.Combine(scripts.FullName, name));
        }
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
        var head = (await RunAsync(
            fixture.FullName,
            "git",
            "rev-parse",
            "HEAD")).Trim();
        var evidencePath = Path.Combine(fixture.FullName, "coverage.json");
        var receiptPath = Path.Combine(
            fixture.FullName,
            "artifacts",
            "release-qualification",
            "qualification-receipts",
            "coverage.json");
        var originalEvidence = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            passed = true,
            commit = head
        });
        var replacementEvidence = originalEvidence.Replace(
            "\"passed\":true",
            "\"passed\":null",
            StringComparison.Ordinal);
        var originalBytes = System.Text.Encoding.UTF8.GetBytes(originalEvidence);
        var replacementBytes = System.Text.Encoding.UTF8.GetBytes(replacementEvidence);
        Assert.That(replacementBytes.Length, Is.EqualTo(originalBytes.Length));
        var originalHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(originalBytes));
        var replacementHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(replacementBytes));
        Assert.That(replacementHash, Is.Not.EqualTo(originalHash));

        async Task WriteReceiptAsync()
        {
            await RunAsync(
                fixture.FullName,
                "pwsh",
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(scripts.FullName, "Write-SharpProofQualificationReceipt.ps1"),
                "-Gate",
                "coverage",
                "-EvidencePath",
                evidencePath);
        }

        async Task WriteEvidenceAsync(string content)
        {
            await File.WriteAllTextAsync(evidencePath, content);
        }

        async Task AssertReceiptBindsAsync(byte[] expectedBytes)
        {
            using var receipt = JsonDocument.Parse(
                await File.ReadAllTextAsync(receiptPath));
            var evidence = receipt.RootElement.GetProperty("evidence");
            Assert.That(
                evidence.GetProperty("bytes").GetInt64(),
                Is.EqualTo(expectedBytes.LongLength));
            Assert.That(
                StringComparer.OrdinalIgnoreCase.Equals(
                    evidence.GetProperty("sha256").GetString(),
                    Convert.ToHexString(
                        System.Security.Cryptography.SHA256.HashData(expectedBytes))),
                Is.True);
        }

        await WriteEvidenceAsync(originalEvidence);
        await WriteReceiptAsync();
        await AssertReceiptBindsAsync(originalBytes);

        var writerPath = Path.Combine(
            scripts.FullName,
            "Write-SharpProofQualificationReceipt.ps1");
        var writer = await File.ReadAllTextAsync(writerPath);
        const string bindingMarker =
            "$receiptCandidate = if ([IO.Path]::IsPathRooted($ReceiptDirectory)) {";
        var replacementCommand =
            "[IO.File]::WriteAllBytes($resolvedEvidence, [Convert]::FromBase64String('" +
            Convert.ToBase64String(replacementBytes) +
            "'))";
        var instrumentedWriter = writer.Replace(
            bindingMarker,
            replacementCommand + Environment.NewLine + bindingMarker,
            StringComparison.Ordinal);
        Assert.That(instrumentedWriter, Is.Not.EqualTo(writer));
        await File.WriteAllTextAsync(
            writerPath,
            instrumentedWriter,
            new System.Text.UTF8Encoding(false));
        await WriteEvidenceAsync(originalEvidence);
        await WriteReceiptAsync();

        Assert.That(
            await File.ReadAllBytesAsync(evidencePath),
            Is.EqualTo(replacementBytes));
        await AssertReceiptBindsAsync(originalBytes);
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
            Path.Combine(sourceRoot, "scripts", "SharpProof.ReleaseBundle.ps1"),
            Path.Combine(scripts.FullName, "SharpProof.ReleaseBundle.ps1"));
        File.Copy(
            Path.Combine(sourceRoot, "scripts", "Resolve-SharpProofContainedPath.ps1"),
            Path.Combine(scripts.FullName, "Resolve-SharpProofContainedPath.ps1"));
        File.Copy(
            Path.Combine(sourceRoot, "scripts", "SharpProof.ReleaseJson.ps1"),
            Path.Combine(scripts.FullName, "SharpProof.ReleaseJson.ps1"));
        await File.WriteAllTextAsync(
            Path.Combine(scripts.FullName, "Test-SharpProofPilotReport.ps1"),
            "function Test-SharpProofPilotReport { return $true }\n" +
            "function Get-SharpProofPilotReviewLedgerSummary { " +
            "return [pscustomobject]@{ falsePositiveCounts = @{} } }\n");
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
        var ledgerPath = Path.Combine(fixture.FullName, "review-ledger.json");
        var packages = CreatePackageArtifacts();
        var ledgerBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            commit = "fixture",
            packageArtifacts = packages,
            reviews = Array.Empty<object>()
        });
#pragma warning disable CA1308 // Serialized SHA-256 evidence uses lowercase hex.
        var ledgerSha256 = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(ledgerBytes)).ToLowerInvariant();
#pragma warning restore CA1308
        await File.WriteAllBytesAsync(ledgerPath, ledgerBytes);

        async Task<int> WriteAsync(string reviewStatus)
        {
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new
            {
                reviewStatus,
                reviewLedgerSha256 = ledgerSha256,
                packageArtifacts = packages,
                pilots = Array.Empty<object>()
            }));
            return await RunExitCodeAsync(
                fixture.FullName,
                "pwsh", "-NoLogo", "-NoProfile", "-File",
                Path.Combine(
                    scripts.FullName,
                    "Write-SharpProofQualificationReceipt.ps1"),
                "-Gate", "pilots", "-EvidencePath", evidence,
                "-PilotReviewLedgerPath", ledgerPath);
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

    private static string Step(string job, string name)
    {
        var start = job.IndexOf(
            "      - name: " + name,
            StringComparison.Ordinal);
        var end = job.IndexOf("\n      - ", start + 1, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), name);
        return end > start ? job[start..end] : job[start..];
    }

    private sealed record PackageArtifact(string fileName, int bytes, string sha256);

    private static PackageArtifact[] CreatePackageArtifacts()
    {
        return Enumerable.Range(0, 6)
            .Select(index => new PackageArtifact(
                $"package-{index}.nupkg", 1, new string('a', 64)))
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
