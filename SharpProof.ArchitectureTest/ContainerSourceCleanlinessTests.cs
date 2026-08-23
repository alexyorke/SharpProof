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

        return RunAsync(
            repository,
            environment,
            "bash",
            Path.Combine(
                RepositoryRoot(),
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
