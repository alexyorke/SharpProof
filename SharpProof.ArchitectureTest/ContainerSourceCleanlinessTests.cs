using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[Platform("Linux")]
[NonParallelizable]
public sealed class ContainerSourceCleanlinessTests
{
    private static readonly string[] s_exactCommitCommands =
    [
        "acceptance",
        "mutation",
        "fuzz-nightly",
        "pack",
        "pilots",
        "release-tag",
        "release-baseline",
        "release-plan",
        "release-qualification",
        "release-publish"
    ];

    private static readonly string[] s_gitSourceCommands =
    [
        .. s_exactCommitCommands,
        "pr-gates",
        "test-changed",
        "package-consumers",
        "performance",
        "coverage"
    ];

    [TestCase("tracked")]
    [TestCase("staged")]
    [TestCase("untracked")]
    public async Task PackRejectsDirtyProductionSource(string state)
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            await MakeDirtyAsync(repository, state);

            var result = await RunEntrypointAsync(repository, "pack");

            Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
            Assert.That(
                result.Error,
                Does.Contain("requires clean exact-commit source"));
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [TestCaseSource(nameof(s_exactCommitCommands))]
    public async Task EveryExactCommitCommandRejectsUntrackedProductionSource(
        string command)
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            await MakeDirtyAsync(repository, "untracked");

            var result = await RunEntrypointAsync(repository, command);

            Assert.That(result.ExitCode, Is.Not.Zero, command);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Test]
    public async Task CleanAndDevelopmentInputsRemainAdmissible()
    {
        var clean = await CreateRepositoryAsync();
        var development = await CreateRepositoryAsync();
        var releaseInput = await CreateRepositoryAsync();
        try
        {
            await MakeDirtyAsync(development, "untracked");
            var packageInput = Path.Combine(releaseInput, "nupkgs");
            Directory.CreateDirectory(packageInput);
            await File.WriteAllTextAsync(
                Path.Combine(packageInput, "SharpProof.1.0.0.nupkg"),
                "fixture");

            var cleanResult = await RunEntrypointAsync(clean, "pack");
            var developmentResult = await RunEntrypointAsync(
                development,
                "build");
            var releaseInputResult = await RunEntrypointAsync(
                releaseInput,
                "release-plan");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cleanResult.ExitCode, Is.Zero, cleanResult.Error);
                Assert.That(
                    developmentResult.ExitCode,
                    Is.Zero,
                    developmentResult.Error);
                Assert.That(
                    releaseInputResult.ExitCode,
                    Is.Zero,
                    releaseInputResult.Error);
            }
        }
        finally
        {
            Directory.Delete(clean, recursive: true);
            Directory.Delete(development, recursive: true);
            Directory.Delete(releaseInput, recursive: true);
        }
    }

    [Test]
    public async Task ExactCommitCommandDoesNotOverlayIgnoredSourceFiles()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(repository, ".gitignore"),
                "Project/IgnoredCompileBreak.cs\n" +
                "nupkgs/\n");
            await File.WriteAllTextAsync(
                Path.Combine(repository, "scripts", "Invoke-SharpProofContainer.ps1"),
                "[CmdletBinding()]\n" +
                "param([Parameter(Mandatory = $true)][string]$Command)\n" +
                "if (Test-Path -LiteralPath 'Project/IgnoredCompileBreak.cs') { Write-Error 'ignored source was overlaid'; exit 91 }\n" +
                "Write-Output ('ignored-absent:' + (-not (Test-Path -LiteralPath 'Project/IgnoredCompileBreak.cs')))\n" +
                "Write-Output ('package-present:' + (Test-Path -LiteralPath 'nupkgs/allowed.nupkg'))\n");
            await RequireSuccessAsync(repository, "git", "add", "--", ".gitignore", "scripts/Invoke-SharpProofContainer.ps1");
            await RequireSuccessAsync(repository, "git", "commit", "--quiet", "-m", "fixture allowlist");
            await File.WriteAllTextAsync(
                Path.Combine(repository, "Project", "IgnoredCompileBreak.cs"),
                "#error ignored source must not be compiled\n");
            Directory.CreateDirectory(Path.Combine(repository, "nupkgs"));
            await File.WriteAllTextAsync(
                Path.Combine(repository, "nupkgs", "allowed.nupkg"),
                "package\n");

            var result = await RunEntrypointAsync(repository, "pack");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, result.Error);
                Assert.That(result.Output, Does.Contain("ignored-absent:True"));
                Assert.That(result.Output, Does.Contain("package-present:True"));
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Test]
    public async Task GitBoundCommandAcceptsRepositoryWithDifferentOwner()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            var result = await RunEntrypointAsync(
                repository,
                "package-consumers",
                assumeDifferentOwner: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, result.Error);
                Assert.That(
                    result.Output,
                    Does.Contain("executed:package-consumers"));
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Test]
    public async Task LinkedWorktreeWithWindowsGitPointerUsesMountedMetadata()
    {
        var repository = await CreateRepositoryAsync();
        var linked = Path.Combine(
            Path.GetDirectoryName(repository)!,
            "SharpProof.Linked." + Guid.NewGuid().ToString("N"));
        try
        {
            await RequireSuccessAsync(
                repository,
                "git",
                "worktree",
                "add",
                "--detach",
                "--quiet",
                linked,
                "HEAD");

            var worktreeName = Path.GetFileName(linked);
            await File.WriteAllTextAsync(
                Path.Combine(linked, ".git"),
                "gitdir: C:/w/PurelySharp/.git/worktrees/" +
                worktreeName + "\n");

            var result = await RunEntrypointAsync(
                linked,
                "release-tag",
                gitParentDirectory: Path.GetDirectoryName(repository)!);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, result.Error);
                Assert.That(result.Output, Does.Contain("executed:release-tag"));
            }
        }
        finally
        {
            Directory.Delete(linked, recursive: true);
            Directory.Delete(repository, recursive: true);
        }
    }

    [Test]
    public async Task DisposableToolingDevRunsInsideStagedWorkspace()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            var result = await RunEntrypointAsync(
                repository,
                "dev",
                commandArguments:
                [
                    "-lc",
                    "printf 'PWD=%s\\nREPO=%s\\n' \"$PWD\" \"$SHARPPROOF_REPO_ROOT\""
                ]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, result.Error);
                Assert.That(result.Output, Does.Contain("/tmp/sharpproof-task."));
                Assert.That(result.Output, Does.Not.Contain(repository));
                Assert.That(
                    result.Output,
                    Does.Contain("REPO=/tmp/sharpproof-task."));
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [TestCase("contract")]
    [TestCase("build")]
    public async Task FiniteCommandsRunFromAnArchiveWithoutGit(string command)
    {
        var repository = await CreateRepositoryAsync();
        var archive = await CreateArchiveSnapshotAsync(repository);
        try
        {
            var result = await RunEntrypointAsync(archive, command);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, result.Error);
                Assert.That(result.Output, Does.Contain("executed:" + command));
                Assert.That(result.Output, Does.Contain("executable:True"));
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            Directory.Delete(archive, recursive: true);
        }
    }

    [TestCaseSource(nameof(s_gitSourceCommands))]
    public async Task GitBoundCommandsRejectArchiveSource(string command)
    {
        var repository = await CreateRepositoryAsync();
        var archive = await CreateArchiveSnapshotAsync(repository);
        try
        {
            var result = await RunEntrypointAsync(archive, command);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Not.Zero, command);
                Assert.That(
                    result.Error,
                    Does.Contain("requires a Git checkout with an exact commit"),
                    command);
                Assert.That(result.Output, Does.Not.Contain("executed:"));
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            Directory.Delete(archive, recursive: true);
        }
    }

    [Test]
    public async Task DevelopmentSnapshotPreservesDirtyDeletedAndUntrackedFiles()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(repository, "Project", "Production.cs"),
                "dirty\n");
            File.Delete(Path.Combine(repository, "Project", "Deleted.cs"));
            await File.WriteAllTextAsync(
                Path.Combine(repository, "Project", "Untracked.cs"),
                "untracked\n");

            var result = await RunEntrypointAsync(repository, "build");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, result.Error);
                Assert.That(result.Output, Does.Contain("production:dirty"));
                Assert.That(result.Output, Does.Contain("deleted:False"));
                Assert.That(result.Output, Does.Contain("untracked:True"));
                Assert.That(result.Output, Does.Contain("executable:True"));
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Test]
    public async Task CorpusUpdatePersistsSourceMutations()
    {
        var repository = await CreateRepositoryAsync();
        try
        {
            var result = await RunEntrypointAsync(repository, "corpus-update");
            var marker = Path.Combine(
                repository,
                "Project",
                "CorpusUpdate.txt");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Zero, result.Error);
                Assert.That(File.Exists(marker), Is.True);
                Assert.That(
                    (await File.ReadAllTextAsync(marker)).Trim(),
                    Is.EqualTo("updated"));
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Test]
    public async Task ExactCommitCommandRejectsGitUntrackedScanFailure()
    {
        var repository = await CreateRepositoryAsync();
        var wrapperDirectory = await CreateGitFailureWrapperAsync(
            repository,
            "[[ \"$*\" == *\"ls-files\"* && \"$*\" == *\"--others\"* ]]",
            73);
        try
        {
            var result = await RunEntrypointAsync(
                repository,
                "pack",
                gitWrapperDirectory: wrapperDirectory);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Error,
                    Does.Contain("could not inspect Git untracked paths"));
                Assert.That(result.Output, Does.Not.Contain("executed:pack"));
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Test]
    public async Task DevelopmentCommandRejectsGitDeletedScanFailure()
    {
        var repository = await CreateRepositoryAsync();
        var wrapperDirectory = await CreateGitFailureWrapperAsync(
            repository,
            "[[ \"$*\" == *\"--diff-filter=D\"* ]]",
            74);
        try
        {
            File.Delete(Path.Combine(repository, "Project", "Deleted.cs"));
            var result = await RunEntrypointAsync(
                repository,
                "build",
                gitWrapperDirectory: wrapperDirectory);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
                Assert.That(
                    result.Error,
                    Does.Contain("could not inspect Git deleted paths"));
                Assert.That(result.Output, Does.Not.Contain("executed:build"));
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Test]
    public void ArchiveTestRunExcludesGitBoundFixturesExplicitly()
    {
        var root = RepositoryRoot();
        var dispatcher = File.ReadAllText(Path.Combine(
            root,
            "scripts",
            "Invoke-SharpProofContainer.ps1"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher, Does.Contain("TestCategory!=GitBound"));
            Assert.That(
                File.ReadAllText(Path.Combine(
                    root,
                    "SharpProof.ArchitectureTest",
                    "ReleaseAuthorityClosureTests.cs")),
                Does.Contain("Category(\"GitBound\")"));
            Assert.That(
                File.ReadAllText(Path.Combine(
                    root,
                    "SharpProof.ArchitectureTest",
                    "SbomReleaseIdentityTests.cs")),
                Does.Contain("Category(\"GitBound\")"));
        }
    }

    private static async Task<string> CreateGitFailureWrapperAsync(
        string repository,
        string condition,
        int exitCode)
    {
        var directory = Path.Combine(repository, "fake-git");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "git"),
            "#!/usr/bin/env bash\n" +
            $"if {condition}; then exit {exitCode}; fi\n" +
            "exec /usr/bin/git \"$@\"\n");
        await RequireSuccessAsync(
            repository,
            "chmod",
            "+x",
            "fake-git/git");
        return directory;
    }

    private static async Task<string> CreateRepositoryAsync()
    {
        var repository = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.CleanSource." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(repository, "scripts"));
        Directory.CreateDirectory(Path.Combine(repository, "Project"));
        await File.WriteAllTextAsync(
            Path.Combine(repository, "scripts", "Invoke-SharpProofContainer.ps1"),
            "[CmdletBinding()]\n" +
            "param([Parameter(Mandatory = $true)][string]$Command)\n" +
            "if ($Command -eq 'corpus-update') { Set-Content -LiteralPath Project/CorpusUpdate.txt -Value 'updated' }\n" +
            "Write-Output \"executed:$Command\"\n" +
            "Write-Output ('production:' + (Get-Content Project/Production.cs -Raw).Trim())\n" +
            "Write-Output ('deleted:' + (Test-Path Project/Deleted.cs))\n" +
            "Write-Output ('untracked:' + (Test-Path Project/Untracked.cs))\n" +
            "Write-Output ('executable:' + [bool]([IO.File]::GetUnixFileMode('scripts/executable.sh') -band [IO.UnixFileMode]::UserExecute))\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "scripts", "executable.sh"),
            "#!/usr/bin/env bash\nexit 0\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "Project", "Production.cs"),
            "internal static class Production { }\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, "Project", "Deleted.cs"),
            "internal static class Deleted { }\n");
        await RequireSuccessAsync(
            repository,
            "chmod",
            "+x",
            "scripts/executable.sh");
        await RequireSuccessAsync(repository, "git", "init", "--quiet");
        await RequireSuccessAsync(
            repository,
            "git",
            "config",
            "user.email",
            "fixture@sharpproof.test");
        await RequireSuccessAsync(
            repository,
            "git",
            "config",
            "user.name",
            "SharpProof Fixture");
        await RequireSuccessAsync(repository, "git", "add", "--", ".");
        await RequireSuccessAsync(
            repository,
            "git",
            "commit",
            "--quiet",
            "-m",
            "fixture");
        await RequireSuccessAsync(
            repository,
            "chmod",
            "-R",
            "a+rwX",
            ".");
        return repository;
    }

    private static async Task<string> CreateArchiveSnapshotAsync(
        string repository)
    {
        var archive = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.ArchiveSource." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(archive);
        var copy = await RunAsync(
            repository,
            environment: null,
            "bash",
            "-c",
            "tar --exclude=./.git -cf - . | tar -C \"$1\" -xf -",
            "copy-archive",
            archive);
        Assert.That(copy.ExitCode, Is.Zero, copy.Error);
        return archive;
    }

    private static async Task MakeDirtyAsync(
        string repository,
        string state)
    {
        var tracked = Path.Combine(repository, "Project", "Production.cs");
        switch (state)
        {
            case "tracked":
                await File.AppendAllTextAsync(tracked, "// worktree change\n");
                break;
            case "staged":
                await File.AppendAllTextAsync(tracked, "// index change\n");
                await RequireSuccessAsync(
                    repository,
                    "git",
                    "add",
                    "--",
                    "Project/Production.cs");
                break;
            case "untracked":
                await File.WriteAllTextAsync(
                    Path.Combine(repository, "Project", "Untracked.cs"),
                    "internal static class Untracked { }\n");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    private static Task<ProcessResult> RunEntrypointAsync(
        string repository,
        string command,
        bool assumeDifferentOwner = false,
        string? gitWrapperDirectory = null,
        string? gitParentDirectory = null,
        params string[] commandArguments)
    {
        var environment = new Dictionary<string, string>
        {
            ["SHARPPROOF_REPO_ROOT"] = repository
        };
        if (assumeDifferentOwner)
        {
            environment["GIT_TEST_ASSUME_DIFFERENT_OWNER"] = "1";
        }
        if (gitWrapperDirectory != null)
        {
            environment["PATH"] = string.Join(
                Path.PathSeparator,
                gitWrapperDirectory,
                Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        }
        if (gitParentDirectory != null)
        {
            environment["SHARPPROOF_GIT_PARENT_ROOT"] = gitParentDirectory;
        }

        var invocationArguments = new string[commandArguments.Length + 1];
        invocationArguments[0] = command;
        commandArguments.CopyTo(invocationArguments, 1);

        var scriptPath = Path.Combine(
            RepositoryRoot(),
            "eng",
            "container",
            "entrypoint.sh");
        var bashArguments = new string[invocationArguments.Length + 1];
        bashArguments[0] = scriptPath;
        invocationArguments.CopyTo(bashArguments, 1);

        return RunAsync(
            repository,
            environment,
            "bash",
            bashArguments);
    }

    private static async Task RequireSuccessAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var result = await RunAsync(
            workingDirectory,
            environment: null,
            fileName,
            arguments);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
    }

    private static async Task<ProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
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
