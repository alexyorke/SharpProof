using System.Text;
using NUnit.Framework;
using SharpProof.Tool;

namespace SharpProof.Tool.Test;

[TestFixture]
public sealed class SetupInitializerCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidProjectTargetsDoNotWriteSetupFiles(bool existingNonProject)
    {
        using var fixture = new CoverageFixture();
        var path = Path.Combine(fixture.Root, existingNonProject ? "notes.txt" : "missing.csproj");
        if (existingNonProject)
        { await File.WriteAllTextAsync(path, "unrelated text", TestContext.CurrentContext.CancellationToken); }
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () => _ = await SetupInitializer.InitializeAsync(
            path, "portable", true, null, TestContext.CurrentContext.CancellationToken)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task MultipleComposeFilesRequireAnExplicitManualResolution()
    {
        using var fixture = new CoverageFixture();
        await fixture.WriteAsync("compose.yaml", SetupInitializerContainer.Compose(Image));
        await fixture.WriteAsync("docker-compose.yml", SetupInitializerContainer.Compose(Image));
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("container", Image)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task ContainerOutputIsolationDoesNotShadowInheritedBuildSettings()
    {
        using var fixture = new CoverageFixture();
        await fixture.WriteAsync("Directory.Build.props", "<Project><PropertyGroup><UnrelatedSetting>preserve-me</UnrelatedSetting></PropertyGroup></Project>");
        await fixture.WriteAsync("src/app/Nested.csproj", Project);
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await SetupInitializer.InitializeAsync(
            Path.Combine(fixture.Root, "src", "app", "Nested.csproj"), "container", true, Image, TestContext.CurrentContext.CancellationToken)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [TestCase("tab")]
    [TestCase("include")]
    [TestCase("extends")]
    [TestCase("missing-service")]
    [TestCase("missing-volumes")]
    public async Task UnsupportedComposeFeaturesCannotBeClaimedAsQualifiedIsolation(string feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        using var fixture = new CoverageFixture();
        var compose = SetupInitializerContainer.Compose(Image);
        compose = feature switch
        {
            "tab" => compose.Replace("    image:", "\timage:", StringComparison.Ordinal),
            "include" => "include: extra.yaml\n" + compose,
            "extends" => compose.Replace("    platform:", "    extends: external\n    platform:", StringComparison.Ordinal),
            "missing-service" => compose.Replace("  verify:", "  unrelated:", StringComparison.Ordinal),
            "missing-volumes" => compose[..compose.IndexOf("\nvolumes:\n", StringComparison.Ordinal)],
            _ => throw new AssertionException("Unknown compose feature.")
        };
        await fixture.WriteAsync("compose.yaml", compose);
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("container", null)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task ExistingOutputPathsArePreservedInsteadOfSilentlyRedirected()
    {
        using var fixture = new CoverageFixture();
        await fixture.WriteAsync("Directory.Build.props", "<Project><PropertyGroup><BaseOutputPath>custom-output/</BaseOutputPath></PropertyGroup></Project>");
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("container", Image)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task ProjectEncodingAndCrLfConventionsSurviveApplyAndReapply()
    {
        using var fixture = new CoverageFixture();
        const string project = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Project Sdk=\"Microsoft.NET.Sdk\">\r\n  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>\r\n</Project>\r\n";
        await File.WriteAllTextAsync(fixture.ProjectPath, project, new UTF8Encoding(true), TestContext.CurrentContext.CancellationToken);
        _ = await fixture.InitializeAsync("portable", null);
        var bytes = await File.ReadAllBytesAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken);
        Assert.That(bytes[0], Is.EqualTo(0xEF));
        Assert.That(bytes[1], Is.EqualTo(0xBB));
        Assert.That(bytes[2], Is.EqualTo(0xBF));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.That(text, Does.Contain("\r\n"));
        Assert.That(text.Replace("\r\n", string.Empty, StringComparison.Ordinal), Does.Not.Contain("\n"));
        _ = await fixture.InitializeAsync("portable", null);
        Assert.That(await File.ReadAllBytesAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(bytes));
    }

    [Test]
    public void ContradictoryFilePlansCannotModifyTheOriginalTarget()
    {
        using var fixture = new CoverageFixture();
        var before = fixture.Snapshot();
        var plan = new SetupInitializerFiles(TestContext.CurrentContext.CancellationToken);
        plan.Add(fixture.ProjectPath, "first planned change");
        Assert.Throws<InvalidOperationException>(new Action(() => plan.Add(fixture.ProjectPath, "second planned change")));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task LinkedConfigFilesAreNeverReplaced()
    {
        if (!OperatingSystem.IsLinux())
        { Assert.Ignore("Symbolic-link safety runs in the canonical Linux container."); return; }
        using var fixture = new CoverageFixture();
        await fixture.WriteAsync("original.config", "<configuration />");
        var original = Path.Combine(fixture.Root, "original.config");
        var link = Path.Combine(fixture.Root, "NuGet.Config");
        File.CreateSymbolicLink(link, original);
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("portable", null)));
        Assert.That(new FileInfo(link).LinkTarget, Is.EqualTo(original));
        Assert.That(await File.ReadAllTextAsync(original, TestContext.CurrentContext.CancellationToken), Is.EqualTo("<configuration />"));
        Assert.That(await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(Project));
    }

    [Test]
    public async Task LinkedProjectDirectoriesCannotRedirectSetupWrites()
    {
        if (!OperatingSystem.IsLinux())
        { Assert.Ignore("Symbolic-link safety runs in the canonical Linux container."); return; }
        using var fixture = new CoverageFixture();
        await fixture.WriteAsync("real/Linked.csproj", Project);
        var originalDirectory = Path.Combine(fixture.Root, "real");
        var linkedDirectory = Path.Combine(fixture.Root, "linked");
        Directory.CreateSymbolicLink(linkedDirectory, originalDirectory);
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await SetupInitializer.InitializeAsync(
            Path.Combine(linkedDirectory, "Linked.csproj"), "portable", true, null, TestContext.CurrentContext.CancellationToken)));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(originalDirectory, "Linked.csproj"), TestContext.CurrentContext.CancellationToken), Is.EqualTo(Project));
        Assert.That(File.Exists(Path.Combine(originalDirectory, "NuGet.Config")), Is.False);
    }

    [Test]
    public async Task AFileAtTheManifestDirectoryPathBlocksTheWholePlan()
    {
        using var fixture = new CoverageFixture();
        await fixture.WriteAsync(".config", "preserve this file");
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("portable", null)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [TestCase("<configuration />")]
    [TestCase("<Project Sdk=\"Microsoft.NET.Sdk\"><broken>")]
    [TestCase("<!DOCTYPE Project><Project Sdk=\"Microsoft.NET.Sdk\" />")]
    public async Task UnsupportedOrMalformedProjectXmlFailsWithoutPartialSetup(string project)
    {
        ArgumentNullException.ThrowIfNull(project);
        using var fixture = new CoverageFixture();
        await File.WriteAllTextAsync(fixture.ProjectPath, project, TestContext.CurrentContext.CancellationToken);
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidDataException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("portable", null)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [TestCase("1.0.0-preview.1", "1.0.0-preview.1")]
    [TestCase("1.0.0-preview.1+abcdef1234", "1.0.0-preview.1")]
    [TestCase("1.2.3.4-rc.1+build.2", "1.2.3.4-rc.1")]
    [TestCase("1.2.3+build.7", "1.2.3")]
    [TestCase("1.2", "1.2")]
    public void InstalledVersionMetadataKeepsValidPackagePinsAndDropsBuildMetadata(string metadata, string expected)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(expected);
        Assert.That(SetupInitializer.ResolvePackageVersion(metadata), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("1.2.3-")]
    [TestCase("1.2.3-preview..1")]
    [TestCase("1.2.3-preview_1")]
    [TestCase("1..3")]
    [TestCase("1.2.3+")]
    [TestCase("1.2.3+build..1")]
    [TestCase("1.2.3+build+second")]
    [TestCase("1.2.3+branch/name")]
    public void InvalidInstalledVersionMetadataCannotGenerateUnrestorablePins(string? metadata)
    {
        Assert.Throws<InvalidOperationException>(new Action(() => SetupInitializer.ResolvePackageVersion(metadata)));
    }
    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
    private const string Image = "ghcr.io/alexyorke/sharpproof-preview@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private sealed class CoverageFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "sharpproof-init-coverage-" + Guid.NewGuid().ToString("N"));
        internal string ProjectPath => Path.Combine(Root, "Consumer.csproj");

        internal CoverageFixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(ProjectPath, Project);
        }

        internal Task<SetupInitializationResult> InitializeAsync(string verification, string? image)
        {
            ArgumentException.ThrowIfNullOrEmpty(verification);
            return SetupInitializer.InitializeAsync(ProjectPath, verification, true, image, TestContext.CurrentContext.CancellationToken);
        }

        internal async Task WriteAsync(string relativePath, string content)
        {
            ArgumentException.ThrowIfNullOrEmpty(relativePath);
            ArgumentNullException.ThrowIfNull(content);
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, TestContext.CurrentContext.CancellationToken);
        }

        internal Dictionary<string, string> Snapshot()
        {
            return Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories).ToDictionary(static path =>
            {
                ArgumentNullException.ThrowIfNull(path);
                return path;
            }, static path =>
            {
                ArgumentNullException.ThrowIfNull(path);
                return Directory.Exists(path) ? "directory" : File.ReadAllText(path);
            }, StringComparer.Ordinal);
        }

        public void Dispose()
        { Directory.Delete(Root, recursive: true); }
    }
}
