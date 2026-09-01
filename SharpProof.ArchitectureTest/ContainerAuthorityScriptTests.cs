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

    [Test]
    public async Task ComposeToolingImageIsProjectPrivateAndOverrideable()
    {
        var root = TestRepository.FindRoot();
        var compose = await File.ReadAllTextAsync(
            Path.Combine(root, "compose.yaml"));
        var imageLine = compose.Split('\n').Single(static line =>
            line.StartsWith("  image: ", StringComparison.Ordinal));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                imageLine,
                Is.EqualTo(
                    "  image: ${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}"));
            Assert.That(
                ResolveComposeImage(imageLine, "audit-one", null),
                Is.EqualTo("audit-one-tooling:local"));
            Assert.That(
                ResolveComposeImage(imageLine, "audit-two", null),
                Is.EqualTo("audit-two-tooling:local"));
            Assert.That(
                ResolveComposeImage(imageLine, "audit-one", null),
                Is.EqualTo(ResolveComposeImage(imageLine, "audit-one", null)));
            Assert.That(
                ResolveComposeImage(
                    imageLine,
                    "audit-two",
                    "reviewed/tooling:candidate"),
                Is.EqualTo("reviewed/tooling:candidate"));
        }
    }

    [Test]
    public void NamedStagesHaveStandaloneNonRootExecutionContracts()
    {
        var root = TestRepository.FindRoot();
        var dockerfile = File.ReadAllText(Path.Combine(
            root,
            "eng",
            "container",
            "Dockerfile"));
        var compose = File.ReadAllText(Path.Combine(root, "compose.yaml"));
        var stages = ParseStages(dockerfile);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                stages["toolchain"],
                Does.Contain("/home/sharpproof/.local/share/NuGet")
                    .And.Contain("/home/sharpproof/.nuget/packages")
                    .And.Contain("useradd --uid \"${USER_UID}\""));
            AssertStage(stages["dev"], "/workspace/SharpProof", "dev");
            AssertStage(stages["build"], "/src", "build");
            AssertStage(stages["test"], "/src", "portable-tests");
            AssertStage(stages["package"], "/src", "pack");
            Assert.That(
                stages["build"],
                Does.Contain("COPY --chown=sharpproof:sharpproof . ."));
            Assert.That(
                compose,
                Does.Contain("SHARPPROOF_REPO_ROOT: /workspace/SharpProof")
                    .And.Contain("target: dev"));
        }
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
        yield return Case("missing-nuget-state", value => value.Replace(
            "        /home/sharpproof/.local/share/NuGet \\\n",
            string.Empty,
            StringComparison.Ordinal));
        yield return Case("build-root-mismatch", value => value.Replace(
            "ENV SHARPPROOF_REPO_ROOT=/src\nWORKDIR /src\n" +
            "COPY --chown=sharpproof:sharpproof . .",
            "ENV SHARPPROOF_REPO_ROOT=/workspace/SharpProof\nWORKDIR /src\n" +
            "COPY --chown=sharpproof:sharpproof . .",
            StringComparison.Ordinal));
        yield return Case("package-root-inherited-decoy", value => value.Replace(
            "FROM build AS package\nENV SHARPPROOF_REPO_ROOT=/src",
            "FROM build AS package\n# ENV SHARPPROOF_REPO_ROOT=/src",
            StringComparison.Ordinal));
        yield return Case("test-root-user", value => value.Replace(
            "FROM build AS test\nENV SHARPPROOF_REPO_ROOT=/src\nWORKDIR /src\nUSER sharpproof",
            "FROM build AS test\nENV SHARPPROOF_REPO_ROOT=/src\nWORKDIR /src\nUSER root",
            StringComparison.Ordinal));
        yield return Case("build-unowned-copy", value => value.Replace(
            "COPY --chown=sharpproof:sharpproof . .",
            "COPY . .",
            StringComparison.Ordinal));
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
            "  image: ${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}",
            "  # image: ${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}\n" +
            "  image: example.invalid/tooling:latest",
            StringComparison.Ordinal));
        yield return Case("duplicate-image", value => value.Replace(
            "  image: ${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}",
            "  image: ${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}\n" +
            "  image: example.invalid/tooling:latest",
            StringComparison.Ordinal));
        yield return Case("shared-global-image", value => value.Replace(
            "  image: ${SHARPPROOF_TOOLING_IMAGE:-${COMPOSE_PROJECT_NAME}-tooling:local}",
            "  image: ${SHARPPROOF_TOOLING_IMAGE:-sharpproof-tooling:local}",
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

    private static Dictionary<string, string> ParseStages(string dockerfile)
    {
        var stages = new Dictionary<string, string>(StringComparer.Ordinal);
        string? name = null;
        var lines = new List<string>();
        foreach (var line in dockerfile.Replace("\r", "", StringComparison.Ordinal)
                     .Split('\n'))
        {
            if (line.StartsWith("FROM ", StringComparison.Ordinal))
            {
                if (name != null)
                {
                    stages.Add(name, string.Join("\n", lines));
                }
                name = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
                lines.Clear();
            }
            else if (name != null)
            {
                lines.Add(line);
            }
        }
        if (name != null)
        {
            stages.Add(name, string.Join("\n", lines));
        }
        return stages;
    }

    private static void AssertStage(
        string stage,
        string repositoryRoot,
        string command)
    {
        Assert.That(
            stage,
            Does.Contain("ENV SHARPPROOF_REPO_ROOT=" + repositoryRoot)
                .And.Contain("WORKDIR " + repositoryRoot)
                .And.Contain("USER sharpproof")
                .And.Contain("ENTRYPOINT [\"/usr/local/bin/sharpproof-container\"]")
                .And.Contain("CMD [\"" + command + "\"]"));
    }

    private static async Task<ProcessResult> ValidateAsync(
        Func<string, string> mutateDockerfile,
        Func<string, string> mutateCompose)
    {
        var root = TestRepository.FindRoot();
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

    private static string ResolveComposeImage(
        string imageLine,
        string projectName,
        string? image)
    {
        const string prefix = "  image: ${SHARPPROOF_TOOLING_IMAGE:-";
        var fallback = imageLine.Substring(
            prefix.Length,
            imageLine.Length - prefix.Length - 1);
        fallback = fallback.Replace(
            "${COMPOSE_PROJECT_NAME}",
            projectName,
            StringComparison.Ordinal);
        return image ?? fallback;
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
