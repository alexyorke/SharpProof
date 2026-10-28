using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Tool;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
internal sealed class WorkspaceCoverageRegressionTests
{
    private static readonly ImmutableArray<string> CompleteDoctorCommands = ["dotnet msbuild", "docker info", "docker compose", "docker image"];
    private static readonly ImmutableArray<string> SetupInspectionCommands = ["dotnet msbuild", "docker info"];
    private static readonly ImmutableArray<string> MissingComposeCommands = ["dotnet msbuild", "docker info", "docker compose"];
    [Test]
    public async Task ProjectLocalComposeTakesPrecedenceOverAmbiguousAncestorConfiguration()
    {
        using var fixture = new DoctorWorkspace();
        await fixture.WriteAsync("compose.yaml", DoctorWorkspace.Compose, TestContext.CurrentContext.CancellationToken);
        await fixture.WriteAsync("docker-compose.yml", DoctorWorkspace.Compose, TestContext.CurrentContext.CancellationToken);
        await fixture.WriteAsync("src/app/compose.yaml", DoctorWorkspace.Compose, TestContext.CurrentContext.CancellationToken);

        var exitCode = await fixture.RunAsync(TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero, fixture.Output.ToString());
            Assert.That(fixture.ComposeDirectories, Is.EqualTo(new[] { fixture.ProjectDirectory }));
            Assert.That(fixture.Commands, Is.EqualTo(CompleteDoctorCommands));
            Assert.That(fixture.Output.ToString(), Does.Contain("PASS Pinned canonical image").And.Contain("PASS Image/package compatibility"));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AmbiguousComposeFilesFailInspectionBeforeExecutingCompose(bool projectLocal)
    {
        using var fixture = new DoctorWorkspace();
        var prefix = projectLocal ? "src/app/" : string.Empty;
        await fixture.WriteAsync(prefix + "compose.yaml", DoctorWorkspace.Compose, TestContext.CurrentContext.CancellationToken);
        await fixture.WriteAsync(prefix + "docker-compose.yml", DoctorWorkspace.Compose, TestContext.CurrentContext.CancellationToken);
        var originalProject = await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken);

        var exitCode = await fixture.RunAsync(TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(fixture.ComposeDirectories, Is.Empty);
            Assert.That(fixture.Commands, Is.EqualTo(SetupInspectionCommands));
            Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Setup inspection"));
            Assert.That(Directory.GetDirectories(fixture.Root, "obj", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetDirectories(fixture.Root, "bin", SearchOption.AllDirectories), Is.Empty);
        }

        Assert.That(await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(originalProject));
    }

    [Test]
    public async Task HomeDirectoryBoundaryDoesNotAdoptAnUnmarkedAncestorCompose()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.That(home, Is.Not.Empty);
        Assert.That(Directory.Exists(home), Is.True);
        using var fixture = new DoctorWorkspace(home);
        await fixture.WriteAsync("compose.yaml", DoctorWorkspace.Compose, TestContext.CurrentContext.CancellationToken);

        var exitCode = await fixture.RunAsync(TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(fixture.ComposeDirectories, Is.EqualTo(new[] { fixture.ProjectDirectory }));
            Assert.That(fixture.Commands, Is.EqualTo(MissingComposeCommands));
            Assert.That(fixture.Output.ToString(), Does.Contain("FAIL Consumer container configuration").And.Not.Contain("PASS Local consumer image"));
            Assert.That(File.Exists(Path.Combine(fixture.Root, "compose.yaml")), Is.True);
        }
    }

    private sealed class DoctorWorkspace : IDisposable
    {
        private const string PackageVersion = "1.2.3";

        internal static string Compose => JsonSerializer.Serialize(new
        {
            services = new { verify = new { image = "ghcr.io/alexyorke/sharpproof-preview@sha256:" + new string('a', 64), platform = "linux/amd64" } }
        });

        internal string Root { get; }
        internal string ProjectDirectory { get; }
        internal string ProjectPath => Path.Combine(ProjectDirectory, "Consumer.csproj");
        internal StringWriter Output { get; } = new(CultureInfo.InvariantCulture);
        internal List<string> Commands { get; } = [];
        internal List<string> ComposeDirectories { get; } = [];

        internal DoctorWorkspace(string? parentDirectory = null)
        {
            var parent = parentDirectory ?? Path.GetTempPath();
            ArgumentException.ThrowIfNullOrEmpty(parent);
            Root = Path.Combine(parent, "sharpproof-doctor-workspace-" + Guid.NewGuid().ToString("N"));
            ProjectDirectory = Path.Combine(Root, "src", "app");
            Directory.CreateDirectory(ProjectDirectory);
            File.WriteAllText(ProjectPath, "<Project />");
            File.WriteAllText(Path.Combine(ProjectDirectory, "project.assets.json"), JsonSerializer.Serialize(new
            {
                libraries = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["SharpProof/" + PackageVersion] = new { type = "package" },
                    ["SharpProof.Attributes/" + PackageVersion] = new { type = "package" },
                    ["SharpProof.Verifier/" + PackageVersion] = new { type = "package" }
                }
            }));
        }

        internal async Task WriteAsync(string relativePath, string contents, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(relativePath);
            ArgumentNullException.ThrowIfNull(contents);
            await File.WriteAllTextAsync(Path.Combine(Root, relativePath), contents, cancellationToken);
        }

        internal Task<int> RunAsync(CancellationToken cancellationToken)
        {
            return SetupDoctor.RunAsync(ProjectPath, "container", Output, RunProcessAsync, false, cancellationToken);
        }

        private async Task<(int ExitCode, string Output)> RunProcessAsync(string command, string directory,
            CancellationToken cancellationToken, params string[] arguments)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(directory);
            ArgumentNullException.ThrowIfNull(arguments);
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command + " " + arguments[0]);
            if (command == "dotnet" && arguments[0] == "msbuild")
            {
                Assert.That(directory, Is.EqualTo(ProjectDirectory));
                Assert.That(arguments, Has.Some.StartsWith("-getProperty:"));
                Assert.That(arguments, Does.Contain("-getItem:PackageReference"));
                var properties = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["NETCoreSdkVersion"] = "10.0.402",
                    ["SharpProofProfile"] = "strict",
                    ["SharpProofFeatures"] = "all",
                    ["SharpProofVerify"] = "true",
                    ["SharpProofVerifyPolicy"] = "require-proven",
                    ["SharpProofAssumptionPolicy"] = "error",
                    ["DefineConstants"] = "DEBUG;TRACE",
                    ["ProjectAssetsFile"] = Path.Combine(ProjectDirectory, "project.assets.json"),
                    ["TargetFramework"] = "net10.0",
                    ["TargetFrameworks"] = string.Empty
                };
                return (0, JsonSerializer.Serialize(new
                {
                    Properties = properties,
                    Items = new { PackageReference = new[] { new { Identity = "SharpProof" } } }
                }));
            }

            Assert.That(command, Is.EqualTo("docker"));
            switch (arguments[0])
            {
                case "info":
                    return (0, "linux\n");
                case "compose":
                    ComposeDirectories.Add(directory);
                    var path = Path.Combine(directory, "compose.yaml");
                    return File.Exists(path)
                        ? (0, await File.ReadAllTextAsync(path, cancellationToken))
                        : (1, "No project-local Compose configuration exists.");
                case "image":
                    return (0, JsonSerializer.Serialize(new[]
                    {
                        new
                        {
                            Os = "linux",
                            Architecture = "amd64",
                            Config = new { Labels = new Dictionary<string, string> { ["io.sharpproof.consumer.version"] = PackageVersion } }
                        }
                    }));
                default:
                    throw new AssertionException("Unexpected setup inspection command.");
            }
        }

        public void Dispose()
        {
            Output.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }
}
