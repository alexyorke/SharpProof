using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
public sealed class ReleaseCoverageBaselineTests
{
    private const string FirstPreviewBaseline =
        "8347a70187a63cc7302b35e747d484747a929f6c";
    private static readonly string[] s_upstreamResultExpressions =
    [
        "${{ needs.package.result }}",
        "${{ needs.portable-consumer.result }}",
        "${{ needs.container-verifier.result }}",
        "${{ needs.minimum-sdk-consumer.result }}",
        "${{ needs.security.result }}",
        "${{ needs.attest.result }}"
    ];

    [Test]
    public void ReleaseQualificationImportsEveryUpstreamResult()
    {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "package-consumers.yml"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(workflow, Does.Contain("always() &&"));
            Assert.That(workflow, Does.Contain("- attest"));
            foreach (var result in s_upstreamResultExpressions)
            {
                Assert.That(workflow, Does.Contain(result), result);
            }
            Assert.That(
                workflow,
                Does.Contain(
                    "Where-Object { $_ -ne 'success' }"));
        }
    }

    [Test]
    public void ReleaseQualificationInitializesBeforeSdkAndAvoidsStaleExitCodes()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            ".github",
            "workflows",
            "package-consumers.yml"));
        var qualificationStart = workflow.IndexOf(
            "  release-qualification:",
            StringComparison.Ordinal);
        var qualificationEnd = workflow.IndexOf(
            "  publish-private-preview:",
            qualificationStart,
            StringComparison.Ordinal);
        Assert.That(qualificationStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(qualificationEnd, Is.GreaterThan(qualificationStart));
        var qualification = workflow[
            qualificationStart..qualificationEnd];
        var tagValidation = qualification.IndexOf(
            "Require successful upstream gates and an annotated exact tag",
            StringComparison.Ordinal);
        var setup = qualification.IndexOf(
            "Build the pinned toolchain",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tagValidation, Is.GreaterThanOrEqualTo(0));
            Assert.That(setup, Is.GreaterThan(tagValidation));
            Assert.That(
                qualification,
                Does.Contain("cat-file -t $tagRef"));
            Assert.That(
                qualification,
                Does.Contain("-cne $env:GITHUB_SHA"));
            Assert.That(
                qualification,
                Does.Not.Contain("Setup required .NET SDKs"));
            Assert.That(
                qualification.Split(
                    "docker compose run --rm tooling",
                    StringSplitOptions.None),
                Has.Length.GreaterThanOrEqualTo(5));
            Assert.That(
                qualification,
                Does.Contain("tooling acceptance"));
            Assert.That(
                qualification.Split(
                    "tooling mutation",
                    StringSplitOptions.None),
                Has.Length.EqualTo(2));
            Assert.That(
                qualification,
                Does.Contain("tooling coverage"));
            Assert.That(
                qualification,
                Does.Contain("tooling package-consumers"));
            Assert.That(qualification, Does.Contain("-PlanOnly"));
            Assert.That(
                qualification,
                Does.Contain("qualification.json"));
        }
    }

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
                Has.Length.EqualTo(2));
            Assert.That(
                workflow,
                Does.Contain(
                    "SHARPPROOF_COVERAGE_COMPARISON_REF"));
            Assert.That(
                workflow,
                Does.Not.Contain("-ComparisonRef HEAD^"));
            Assert.That(
                workflow,
                Does.Contain("-ReleaseCommit $env:GITHUB_SHA"));
            Assert.That(workflow, Does.Contain("tooling coverage"));
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
            var normalizedError =
                System.Text.RegularExpressions.Regex.Replace(
                    nonDescendant.Error,
                    @"\s+",
                    " ",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            Assert.That(
                normalizedError,
                Does.Contain("Coverage baseline"));
            Assert.That(
                normalizedError,
                Does.Contain("ancestor of release"));
            Assert.That(
                normalizedError,
                Does.Contain("commit"));
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
            Does.Contain("does not"));
        Assert.That(
            wrongCheckout.Error,
            Does.Contain("identify the"));
        Assert.That(
            wrongCheckout.Error,
            Does.Contain("checked-out HEAD"));
    }

    [Test]
    public void ReleaseDigestCanonicalStreamIncludesGitModeAndType()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "scripts",
            "Get-SharpProofReleaseDigests.ps1"));
        var digestStart = script.IndexOf(
            "function Get-CanonicalDigest",
            StringComparison.Ordinal);
        var digestEnd = script.IndexOf(
            "if ($null -eq (Get-Command git",
            digestStart,
            StringComparison.Ordinal);
        Assert.That(digestStart, Is.GreaterThanOrEqualTo(0));
        Assert.That(digestEnd, Is.GreaterThan(digestStart));
        var digest = script[digestStart..digestEnd];
        var mode = digest.IndexOf(
            "[Text.Encoding]::ASCII.GetBytes([string]$entry.Mode)",
            StringComparison.Ordinal);
        var type = digest.IndexOf(
            "[Text.Encoding]::ASCII.GetBytes([string]$entry.Type)",
            StringComparison.Ordinal);
        var path = digest.IndexOf(
            "[Text.Encoding]::UTF8.GetBytes($path)",
            StringComparison.Ordinal);
        var content = digest.IndexOf(
            "$hash.AppendData($contentDigest)",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(script, Does.Contain("Get-GitTreeEntries"));
            Assert.That(script, Does.Not.Contain("'--name-only'"));
            Assert.That(mode, Is.GreaterThanOrEqualTo(0));
            Assert.That(type, Is.GreaterThan(mode));
            Assert.That(path, Is.GreaterThan(type));
            Assert.That(content, Is.GreaterThan(path));
        }
    }

    [Test]
    public async Task ReleaseDigestsBindEntryModeAndRemainCultureStable()
    {
        var root = RepositoryRoot();
        var repository = Path.Combine(
            Path.GetTempPath(),
            "sharpproof-release-digest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repository);
        try
        {
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "init",
                "--object-format=sha1"));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "config",
                "user.email",
                "release-digest@example.invalid"));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "config",
                "user.name",
                "Release Digest Test"));

            var paths = new[]
            {
                "src/I-alpha.txt",
                "src/i-beta.txt",
                "src/\u0130-gamma.txt",
                "src/\u0131-delta.txt",
                "scripts/Get-SharpProofTcbPaths.ps1"
            };
            foreach (var path in paths)
            {
                var absolutePath = Path.Combine(
                    repository,
                    path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                await File.WriteAllTextAsync(
                    absolutePath,
                    "same blob\n");
            }
            File.Copy(
                Path.Combine(
                    root,
                    "scripts",
                    "Get-SharpProofTcbPaths.ps1"),
                Path.Combine(
                    repository,
                    "scripts",
                    "Get-SharpProofTcbPaths.ps1"),
                overwrite: true);

            var acceptancePath = Path.Combine(
                repository,
                "eng",
                "acceptance",
                "contract.json");
            Directory.CreateDirectory(
                Path.GetDirectoryName(acceptancePath)!);
            await File.WriteAllTextAsync(
                acceptancePath,
                JsonSerializer.Serialize(new
                {
                    trustedKernel = new
                    {
                        paths = new[] { paths[0] }
                    },
                    trustedComputingBase = new
                    {
                        components = new[]
                        {
                            new
                            {
                                paths = paths.Skip(1).ToArray()
                            }
                        }
                    }
                }));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "add",
                "--",
                "."));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "commit",
                "-m",
                "regular files"));
            var regularCommit = (await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "rev-parse",
                "HEAD"))).Output.Trim();

            var english = await RunReleaseDigestAsync(
                root,
                repository,
                regularCommit,
                "en-US");
            var turkish = await RunReleaseDigestAsync(
                root,
                repository,
                regularCommit,
                "tr-TR");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(turkish, Is.EqualTo(english));
                Assert.That(
                    english.TrustedComputingBaseFileCount,
                    Is.EqualTo(paths.Length + 1));
            }

            var componentPath = Path.Combine(
                repository,
                paths[1].Replace('/', Path.DirectorySeparatorChar));
            await File.WriteAllTextAsync(
                componentPath,
                "changed component\n");
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "add",
                "--",
                paths[1]));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "commit",
                "-m",
                "component change"));
            var componentCommit = (await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "rev-parse",
                "HEAD"))).Output.Trim();
            var component = await RunReleaseDigestAsync(
                root,
                repository,
                componentCommit,
                "en-US");

            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "update-index",
                "--chmod=+x",
                "--",
                paths[0]));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "commit",
                "-m",
                "executable file"));
            var executableCommit = (await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "rev-parse",
                "HEAD"))).Output.Trim();
            var executable = await RunReleaseDigestAsync(
                root,
                repository,
                executableCommit,
                "en-US");
            var blobIdentity = (await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "rev-parse",
                $"{executableCommit}:{paths[0]}"))).Output.Trim();
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "update-index",
                "--cacheinfo",
                "120000",
                blobIdentity,
                paths[0]));
            await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "commit",
                "-m",
                "symbolic link"));
            var symbolicLinkCommit = (await AssertSuccessAsync(RunAsync(
                repository,
                "git",
                "rev-parse",
                "HEAD"))).Output.Trim();
            var symbolicLink = await RunReleaseDigestAsync(
                root,
                repository,
                symbolicLinkCommit,
                "en-US");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    executable.ProductionDigest,
                    Is.Not.EqualTo(english.ProductionDigest));
                Assert.That(
                    executable.TrustedComputingBaseDigest,
                    Is.Not.EqualTo(english.TrustedComputingBaseDigest));
                Assert.That(
                    executable.ProductionFileCount,
                    Is.EqualTo(english.ProductionFileCount));
                Assert.That(
                    executable.TrustedComputingBaseFileCount,
                    Is.EqualTo(paths.Length + 1));
                Assert.That(
                    component.TrustedComputingBaseDigest,
                    Is.Not.EqualTo(english.TrustedComputingBaseDigest));
                Assert.That(
                    component.TrustedComputingBaseFileCount,
                    Is.EqualTo(paths.Length + 1));
                Assert.That(
                    symbolicLink.ProductionDigest,
                    Is.Not.EqualTo(executable.ProductionDigest));
                Assert.That(
                    symbolicLink.TrustedComputingBaseDigest,
                    Is.Not.EqualTo(
                        executable.TrustedComputingBaseDigest));
                Assert.That(
                    symbolicLink.ProductionFileCount,
                    Is.EqualTo(english.ProductionFileCount));
                Assert.That(
                    symbolicLink.TrustedComputingBaseFileCount,
                    Is.EqualTo(paths.Length + 1));
            }
        }
        finally
        {
            DeleteTemporaryRepository(repository);
        }
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

    private static async Task<ReleaseDigest> RunReleaseDigestAsync(
        string root,
        string repository,
        string commit,
        string culture)
    {
        const string command =
            "$culture = [Globalization.CultureInfo]::GetCultureInfo(" +
            "$env:SHARPPROOF_TEST_CULTURE); " +
            "[Globalization.CultureInfo]::CurrentCulture = $culture; " +
            "[Globalization.CultureInfo]::CurrentUICulture = $culture; " +
            "& $env:SHARPPROOF_TEST_SCRIPT " +
            "-RepositoryPath $env:SHARPPROOF_TEST_REPOSITORY " +
            "-Commit $env:SHARPPROOF_TEST_COMMIT";
        var result = await RunAsyncCore(
            root,
            "pwsh",
            new Dictionary<string, string>
            {
                ["SHARPPROOF_TEST_CULTURE"] = culture,
                ["SHARPPROOF_TEST_SCRIPT"] = Path.Combine(
                    root,
                    "scripts",
                    "Get-SharpProofReleaseDigests.ps1"),
                ["SHARPPROOF_TEST_REPOSITORY"] = repository,
                ["SHARPPROOF_TEST_COMMIT"] = commit
            },
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            command);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        var evidence = document.RootElement;
        return new ReleaseDigest(
            evidence
                .GetProperty("productionDigestSha256")
                .GetString()!,
            evidence
                .GetProperty("trustedComputingBaseDigestSha256")
                .GetString()!,
            evidence
                .GetProperty("productionFileCount")
                .GetInt32(),
            evidence
                .GetProperty("trustedComputingBaseFileCount")
                .GetInt32());
    }

    private static async Task<ProcessResult> AssertSuccessAsync(
        Task<ProcessResult> operation)
    {
        var result = await operation;
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        return result;
    }

    private static void DeleteTemporaryRepository(string repository)
    {
        if (!Directory.Exists(repository))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
            repository,
            "*",
            SearchOption.AllDirectories))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
        Directory.Delete(repository, recursive: true);
    }

    private static async Task<ProcessResult> RunAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        return await RunAsyncCore(
            workingDirectory,
            fileName,
            environment: null,
            arguments);
    }

    private static async Task<ProcessResult> RunAsyncCore(
        string workingDirectory,
        string fileName,
        IReadOnlyDictionary<string, string>? environment,
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
        if (environment != null)
        {
            foreach (var entry in environment)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var standardOutput = await output;
        var standardError = await error;
        const string AnsiPattern =
            "\\x1B\\[[0-?]*[ -/]*[@-~]";
        return new ProcessResult(
            process.ExitCode,
            System.Text.RegularExpressions.Regex.Replace(
                standardOutput,
                AnsiPattern,
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant),
            System.Text.RegularExpressions.Regex.Replace(
                standardError,
                AnsiPattern,
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant));
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

    private sealed record ReleaseDigest(
        string ProductionDigest,
        string TrustedComputingBaseDigest,
        int ProductionFileCount,
        int TrustedComputingBaseFileCount);
}
