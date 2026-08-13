using System.Diagnostics;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[Platform("Linux")]
public sealed class ContainerAuthorityScriptTests
{
    private const string SdkArgument =
        "ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:9.0.316-bookworm-slim@" +
        "sha256:10e4355ee23eea62fddba62d35284d36e5fc682762561773ed76c8dae5fa8c9a";

    [Test]
    public async Task CanonicalContainerAuthorityIsAccepted()
    {
        var result = await ValidateAsync(static value => value, static value => value);

        Assert.That(result.ExitCode, Is.Zero, result.Error);
    }

    [TestCaseSource(nameof(DockerfileMutations))]
    public async Task DockerfileAuthorityDecoysAreRejected(
        string _,
        Func<string, string> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var result = await ValidateAsync(mutate, static value => value);

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output + result.Error);
    }

    [TestCaseSource(nameof(ComposeMutations))]
    public async Task ComposeAuthorityDecoysAreRejected(
        string _,
        Func<string, string> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        var result = await ValidateAsync(static value => value, mutate);

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output + result.Error);
    }

    private static IEnumerable<TestCaseData> DockerfileMutations()
    {
        yield return Case("duplicate-argument", value => value.Replace(
            SdkArgument,
            SdkArgument + "\nARG DOTNET_SDK_IMAGE=example.invalid/sdk@sha256:" +
            new string('1', 64),
            StringComparison.Ordinal));
        yield return Case("comment-decoy", value => value.Replace(
            SdkArgument,
            "# " + SdkArgument + "\nARG DOTNET_SDK_IMAGE=example.invalid/sdk@sha256:" +
            new string('2', 64),
            StringComparison.Ordinal));
        yield return Case("unused-pinned-stage", value => value.Replace(
            "FROM ${DOTNET_SDK_IMAGE} AS toolchain",
            "FROM ${DOTNET_SDK_IMAGE} AS reviewed-decoy\n" +
            "FROM example.invalid/sdk@sha256:" + new string('3', 64) + " AS toolchain",
            StringComparison.Ordinal));
        yield return Case("alternate-from", value => value.Replace(
            "FROM ${DOTNET_SDK_IMAGE} AS toolchain",
            "FROM example.invalid/sdk@sha256:" + new string('4', 64) + " AS toolchain",
            StringComparison.Ordinal));
        yield return Case("unpinned-frontend", value =>
            "# syntax=docker/dockerfile:1.7\n" + value);
    }

    private static IEnumerable<TestCaseData> ComposeMutations()
    {
        yield return Case("duplicate-platform", value => value.Replace(
            "  platform: linux/amd64",
            "  platform: linux/amd64\n  platform: linux/arm64",
            StringComparison.Ordinal));
        yield return Case("service-platform-override", value => value.Replace(
            "  tooling:\n    <<: *sharpproof-common",
            "  tooling:\n    <<: *sharpproof-common\n    platform: linux/arm64",
            StringComparison.Ordinal));
        yield return Case("comment-image-decoy", value => value.Replace(
            "  image: ${SHARPPROOF_TOOLING_IMAGE:-sharpproof-tooling:local}",
            "  # image: ${SHARPPROOF_TOOLING_IMAGE:-sharpproof-tooling:local}\n" +
            "  image: example.invalid/tooling:latest",
            StringComparison.Ordinal));
        yield return Case("duplicate-image", value => value.Replace(
            "  image: ${SHARPPROOF_TOOLING_IMAGE:-sharpproof-tooling:local}",
            "  image: ${SHARPPROOF_TOOLING_IMAGE:-sharpproof-tooling:local}\n" +
            "  image: example.invalid/tooling:latest",
            StringComparison.Ordinal));
        yield return Case("unused-build-decoy", value => value.Replace(
            "    dockerfile: eng/container/Dockerfile",
            "    # dockerfile: eng/container/Dockerfile\n" +
            "    dockerfile: eng/container/Alternate.Dockerfile",
            StringComparison.Ordinal));
    }

    private static TestCaseData Case(string name, Func<string, string> mutate)
    {
        return new TestCaseData(name, mutate).SetName($"AuthorityRejects_{name}");
    }

    private static async Task<ProcessResult> ValidateAsync(
        Func<string, string> mutateDockerfile,
        Func<string, string> mutateCompose)
    {
        var root = RepositoryRoot();
        var fixture = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.ContainerAuthority." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            var dockerfile = Path.Combine(fixture, "Dockerfile");
            var compose = Path.Combine(fixture, "compose.yaml");
            await File.WriteAllTextAsync(
                dockerfile,
                mutateDockerfile(await File.ReadAllTextAsync(Path.Combine(
                    root, "eng", "container", "Dockerfile"))));
            await File.WriteAllTextAsync(
                compose,
                mutateCompose(await File.ReadAllTextAsync(Path.Combine(
                    root, "compose.yaml"))));
            return await RunAsync(
                root,
                "pwsh",
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-File",
                Path.Combine(root, "scripts", "Test-SharpProofContainerContract.ps1"),
                "-DockerfilePath",
                dockerfile,
                "-ComposePath",
                compose,
                "-AuthorityOnly");
        }
        finally
        {
            Directory.Delete(fixture, recursive: true);
        }
    }

    private static string RepositoryRoot()
    {
        var path = TestContext.CurrentContext.TestDirectory;
        while (path is not null && !File.Exists(Path.Combine(path, "SharpProof.sln")))
        {
            path = Directory.GetParent(path)?.FullName;
        }
        return path ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static async Task<ProcessResult> RunAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
