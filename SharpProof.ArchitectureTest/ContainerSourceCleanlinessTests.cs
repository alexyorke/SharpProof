using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[Platform("Linux")]
[NonParallelizable]
public sealed class ContainerSourceCleanlinessTests
{
    private static readonly string[] s_exactCommitCommands =
    [
        "nightly",
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
        "quick",
        "pr",
        "security",
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
        using var repositoryWorkspace = await CreateRepositoryAsync();
        var repository = repositoryWorkspace.FullName;
        await MakeDirtyAsync(repository, state);

        var result = await RunEntrypointAsync(repository, "pack");

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        Assert.That(
            result.Error,
            Does.Contain("requires clean exact-commit source"));
    }

    [TestCaseSource(nameof(s_exactCommitCommands))]
    public async Task EveryExactCommitCommandRejectsUntrackedProductionSource(
        string command)
    {
        using var repositoryWorkspace = await CreateRepositoryAsync();
        var repository = repositoryWorkspace.FullName;
        await MakeDirtyAsync(repository, "untracked");

        var result = await RunEntrypointAsync(repository, command);

        Assert.That(result.ExitCode, Is.Not.Zero, command);
    }

    [Test]
    public async Task CleanAndDevelopmentInputsRemainAdmissible()
    {
        using var cleanWorkspace = await CreateRepositoryAsync();
        using var developmentWorkspace = await CreateRepositoryAsync();
        using var releaseInputWorkspace = await CreateRepositoryAsync();
        var clean = cleanWorkspace.FullName;
        var development = developmentWorkspace.FullName;
        var releaseInput = releaseInputWorkspace.FullName;
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

    [Test]
    public async Task GitBoundCommandAcceptsRepositoryWithDifferentOwner()
    {
        using var repositoryWorkspace = await CreateRepositoryAsync();
        var repository = repositoryWorkspace.FullName;
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

    [Test]
    public async Task GitBoundCommandPreservesIgnoredPackageInputs()
    {
        using var repositoryWorkspace = await CreateRepositoryAsync();
        var repository = repositoryWorkspace.FullName;
        var packageDirectory = Path.Combine(repository, "nupkgs");
        Directory.CreateDirectory(packageDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(packageDirectory, "SharpProof.1.0.0.nupkg"),
            "fixture");

        var result = await RunEntrypointAsync(
            repository,
            "package-consumers");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.Error);
            Assert.That(result.Output, Does.Contain("package:fixture"));
        }
    }

    [TestCase("contract")]
    [TestCase("build")]
    public async Task FiniteCommandsRunFromAnArchiveWithoutGit(string command)
    {
        using var repositoryWorkspace = await CreateRepositoryAsync();
        using var archiveWorkspace = await CreateArchiveSnapshotAsync(
            repositoryWorkspace.FullName);
        var archive = archiveWorkspace.FullName;
        var result = await RunEntrypointAsync(archive, command);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.Zero, result.Error);
            Assert.That(result.Output, Does.Contain("executed:" + command));
            Assert.That(result.Output, Does.Contain("executable:True"));
        }
    }

    [TestCaseSource(nameof(s_gitSourceCommands))]
    public async Task GitBoundCommandsRejectArchiveSource(string command)
    {
        using var repositoryWorkspace = await CreateRepositoryAsync();
        using var archiveWorkspace = await CreateArchiveSnapshotAsync(
            repositoryWorkspace.FullName);
        var archive = archiveWorkspace.FullName;
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

    [Test]
    public async Task DevelopmentSnapshotPreservesDirtyDeletedAndUntrackedFiles()
    {
        using var repositoryWorkspace = await CreateRepositoryAsync();
        var repository = repositoryWorkspace.FullName;
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

    private static async Task<TempDirectory> CreateRepositoryAsync()
    {
        var workspace = new TempDirectory("SharpProof.CleanSource.");
        try
        {
            await InitializeRepositoryAsync(workspace.FullName);
            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static async Task InitializeRepositoryAsync(string repository)
    {
        Directory.CreateDirectory(Path.Combine(repository, "scripts"));
        Directory.CreateDirectory(Path.Combine(repository, "Project"));
        await File.WriteAllTextAsync(
            Path.Combine(repository, "scripts", "Invoke-SharpProofContainer.ps1"),
            "[CmdletBinding()]\n" +
            "param([Parameter(Mandatory = $true)][string]$Command)\n" +
            "Write-Output \"executed:$Command\"\n" +
            "Write-Output ('production:' + (Get-Content Project/Production.cs -Raw).Trim())\n" +
            "Write-Output ('deleted:' + (Test-Path Project/Deleted.cs))\n" +
            "Write-Output ('untracked:' + (Test-Path Project/Untracked.cs))\n" +
            "Write-Output ('package:' + $(if (Test-Path nupkgs/SharpProof.1.0.0.nupkg) { (Get-Content nupkgs/SharpProof.1.0.0.nupkg -Raw).Trim() } else { 'missing' }))\n" +
            "Write-Output ('executable:' + [bool]([IO.File]::GetUnixFileMode('scripts/executable.sh') -band [IO.UnixFileMode]::UserExecute))\n");
        await File.WriteAllTextAsync(
            Path.Combine(repository, ".gitignore"),
            "*.nupkg\n*.snupkg\n");
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
        await ArchitectureGitRepository.InitializeAsync(
            repository,
            "fixture@sharpproof.test",
            "SharpProof Fixture");
        await RequireSuccessAsync(repository, "git", "add", "--", ".");
        await RequireSuccessAsync(
            repository,
            "git",
            "commit",
            "--quiet",
            "-m",
            "fixture");
    }

    private static async Task<TempDirectory> CreateArchiveSnapshotAsync(
        string repository)
    {
        var workspace = new TempDirectory("SharpProof.ArchiveSource.");
        try
        {
            var copy = await ArchitectureRepository.RunProcessAsync(
                repository,
                (IReadOnlyDictionary<string, string>?)null,
                "bash",
                "-c",
                "tar --exclude=./.git -cf - . | tar -C \"$1\" -xf -",
                "copy-archive",
                workspace.FullName);
            Assert.That(copy.ExitCode, Is.Zero, copy.Error);
            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
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

    private static Task<ProcessRunnerResult> RunEntrypointAsync(
        string repository,
        string command,
        bool assumeDifferentOwner = false)
    {
        var environment = new Dictionary<string, string>
        {
            ["SHARPPROOF_REPO_ROOT"] = repository
        };
        if (assumeDifferentOwner)
        {
            environment["GIT_TEST_ASSUME_DIFFERENT_OWNER"] = "1";
        }

        return ArchitectureRepository.RunProcessAsync(
            repository,
            environment,
            "bash",
            Path.Combine(
                TestRepository.FindRoot(),
                "eng",
                "container",
                "entrypoint.sh"),
            command);
    }

    private static async Task RequireSuccessAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var result = await ArchitectureRepository.RunProcessAsync(
            workingDirectory,
            (IReadOnlyDictionary<string, string>?)null,
            fileName,
            arguments);
        Assert.That(result.ExitCode, Is.Zero, result.Error);
    }

}
