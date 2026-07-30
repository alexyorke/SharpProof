using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseCoverageBaselineTests
{
    private const string FirstPreviewBaseline =
        "8347a70187a63cc7302b35e747d484747a929f6c";

    [Test]
    public void ReleaseWorkflowUsesTheAllowlistedImmutableBaseline()
    {
        var root = RepositoryRoot();
        var resolver = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Resolve-SharpProofReleaseCoverageBaseline.ps1"));
        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "package-consumers.yml"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolver, Does.Contain(FirstPreviewBaseline));
            Assert.That(
                resolver,
                Does.Contain(
                    "'v1.0.0-preview.2' = 'v1.0.0-preview.1'"));
            Assert.That(
                resolver,
                Does.Contain("'v1.0.0-rc.1' = 'v1.0.0-preview.2'"));
            Assert.That(
                resolver,
                Does.Contain("'v1.0.0' = 'v1.0.0-rc.1'"));
            Assert.That(resolver, Does.Contain("merge-base"));
            Assert.That(resolver, Does.Contain("--is-ancestor"));
            Assert.That(resolver, Does.Contain("checked-out HEAD"));

            Assert.That(
                workflow.Split(
                    "Resolve-SharpProofReleaseCoverageBaseline.ps1",
                    StringSplitOptions.None),
                Has.Length.EqualTo(3));
            Assert.That(
                workflow,
                Does.Contain(
                    "-ComparisonRef $env:SHARPPROOF_COVERAGE_BASELINE"));
            Assert.That(
                workflow,
                Does.Not.Contain("-ComparisonRef HEAD^"));
            Assert.That(
                workflow,
                Does.Contain(
                    "$releaseCommit = [string]$selection.releaseCommit"));
            Assert.That(
                workflow,
                Does.Contain(
                    "$coverageBaseline = " +
                    "[string]$selection.coverageBaselineCommit"));
            Assert.That(
                workflow,
                Does.Contain(
                    "SHARPPROOF_RELEASE_COMMIT=$releaseCommit"));
        }
    }

    [Test]
    public async Task ResolverSelectsExactCommitsAndFailsClosed()
    {
        var root = RepositoryRoot();
        var head = await RunAsync(
            root,
            "git",
            "rev-parse",
            "HEAD");
        Assert.That(head.ExitCode, Is.Zero, head.Error);
        var headCommit = head.Output.Trim();

        var selected = await RunResolverAsync(
            root,
            "v1.0.0-preview.1",
            headCommit);
        Assert.That(selected.ExitCode, Is.Zero, selected.Error);
        using (var document = JsonDocument.Parse(selected.Output))
        {
            var evidence = document.RootElement;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    evidence.GetProperty("schemaVersion").GetInt32(),
                    Is.EqualTo(1));
                Assert.That(
                    evidence.GetProperty("coverageBaselineCommit").GetString(),
                    Is.EqualTo(FirstPreviewBaseline));
                Assert.That(
                    evidence.GetProperty("releaseCommit").GetString(),
                    Is.EqualTo(headCommit));
            }
        }

        var unknown = await RunResolverAsync(
            root,
            "v1.0.0-preview.99",
            headCommit);
        Assert.That(unknown.ExitCode, Is.Not.Zero);
        Assert.That(
            unknown.Error,
            Does.Contain("is not allowlisted"));

        var sameCommit = await RunResolverAsync(
            root,
            "v1.0.0-preview.1",
            FirstPreviewBaseline);
        Assert.That(sameCommit.ExitCode, Is.Not.Zero);
        Assert.That(
            sameCommit.Error,
            Does.Contain("must precede the release commit"));

        var parent = await RunAsync(
            root,
            "git",
            "rev-parse",
            FirstPreviewBaseline + "^");
        Assert.That(parent.ExitCode, Is.Zero, parent.Error);
        var nonDescendant = await RunResolverAsync(
            root,
            "v1.0.0-preview.1",
            parent.Output.Trim());
        Assert.That(nonDescendant.ExitCode, Is.Not.Zero);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                nonDescendant.Error,
                Does.Contain("Coverage baseline"));
            Assert.That(
                nonDescendant.Error,
                Does.Contain("ancestor of release commit"));
        }

        var releaseAncestor = await RunAsync(
            root,
            "git",
            "rev-list",
            "--ancestry-path",
            "--reverse",
            FirstPreviewBaseline + "..HEAD");
        Assert.That(
            releaseAncestor.ExitCode,
            Is.Zero,
            releaseAncestor.Error);
        var nonHeadReleaseCommit = releaseAncestor.Output
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .First(commit => commit != headCommit);
        var wrongCheckout = await RunResolverAsync(
            root,
            "v1.0.0-preview.1",
            nonHeadReleaseCommit);
        Assert.That(wrongCheckout.ExitCode, Is.Not.Zero);
        Assert.That(
            wrongCheckout.Error,
            Does.Contain("does not identify the"));
        Assert.That(
            wrongCheckout.Error,
            Does.Contain("checked-out HEAD"));
    }

    private static Task<ProcessResult> RunResolverAsync(
        string root,
        string tag,
        string releaseCommit)
    {
        return RunAsync(
            root,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                root,
                "scripts",
                "Resolve-SharpProofReleaseCoverageBaseline.ps1"),
            "-Tag",
            tag,
            "-ReleaseCommit",
            releaseCommit);
    }

    private static async Task<ProcessResult> RunAsync(
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
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await output,
            await error);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Could not find the repository root.");
    }

    private sealed record ProcessResult(
        int ExitCode,
        string Output,
        string Error);
}
