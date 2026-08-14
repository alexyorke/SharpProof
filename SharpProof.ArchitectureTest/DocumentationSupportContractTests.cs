using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[NonParallelizable]
public sealed class DocumentationSupportContractTests
{
    [TestCase("clean", true)]
    [TestCase("stale-win-x64", false)]
    [TestCase("package-version-drift", false)]
    [TestCase("support-drift", false)]
    public async Task DocumentationSupportContractRejectsDrift(
        string mutation,
        bool expectedSuccess)
    {
        var root = FindRepositoryRoot();
        var info = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(Path.Combine(
            root,
            "scripts",
            "Test-SharpProofDocumentationSupportFixtures.ps1"));
        info.ArgumentList.Add("-Mutation");
        info.ArgumentList.Add(mutation);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(
            process.ExitCode == 0,
            Is.EqualTo(expectedSuccess),
            await output + Environment.NewLine + await error);
    }

    [Test]
    public async Task DocumentationGatePrecedesPackagingAndReleaseEvidence()
    {
        var root = FindRepositoryRoot();
        var acceptance = await File.ReadAllTextAsync(Path.Combine(
            root, "eng", "acceptance", "Verify.ps1"));
        var dispatcher = await File.ReadAllTextAsync(Path.Combine(
            root, "scripts", "Invoke-SharpProofContainer.ps1"));
        var workflow = await File.ReadAllTextAsync(Path.Combine(
            root, ".github", "workflows", "package-consumers.yml"));

        AssertOrdered(
            acceptance,
            "Start-AcceptanceTimingPhase -Name 'static-validation'",
            "scripts\\Generate-Readme.ps1') -Verify",
            "Complete-AcceptanceTimingPhase");
        AssertCommandGatePrecedes(
            dispatcher,
            "'pack' {",
            "scripts/Generate-Readme.ps1') -Verify",
            "New-SharpProofReleaseEvidence.ps1");
        AssertCommandGatePrecedes(
            dispatcher,
            "'release-qualification' {",
            "scripts/Generate-Readme.ps1') -Verify",
            "-Mode WriteQualificationEvidence");
        Assert.That(workflow, Does.Contain("tooling acceptance"));
        Assert.That(workflow, Does.Contain("tooling release-qualification"));
    }

    private static void AssertCommandGatePrecedes(
        string text,
        string command,
        string gate,
        string consumer)
    {
        var start = text.IndexOf(command, StringComparison.Ordinal);
        var gateIndex = text.IndexOf(gate, start, StringComparison.Ordinal);
        var consumerIndex = text.IndexOf(consumer, start, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), command);
        Assert.That(gateIndex, Is.GreaterThan(start), gate);
        Assert.That(consumerIndex, Is.GreaterThan(gateIndex), consumer);
    }

    private static void AssertOrdered(
        string text,
        string first,
        string second,
        string third)
    {
        var firstIndex = text.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = firstIndex < 0
            ? -1
            : text.IndexOf(second, firstIndex, StringComparison.Ordinal);
        var thirdIndex = secondIndex < 0
            ? -1
            : text.IndexOf(third, secondIndex, StringComparison.Ordinal);
        Assert.That(firstIndex, Is.GreaterThanOrEqualTo(0), first);
        Assert.That(secondIndex, Is.GreaterThan(firstIndex), second);
        Assert.That(thirdIndex, Is.GreaterThan(secondIndex), third);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
