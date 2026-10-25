using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using SharpProof.Tool;

namespace SharpProof.Tool.Test;

[TestFixture]
public sealed class SetupInitializerConfigurationCoverageTests
{
    [Test]
    public async Task CaseDistinctNugetFilesRequireAnExplicitConfigurationChoice()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("Case-distinct NuGet files require the canonical Linux filesystem.");
            return;
        }

        using var fixture = new ConfigurationFixture();
        await fixture.WriteAsync("nuget.config", "<configuration />");
        AssertRejected<InvalidOperationException>(fixture, "Multiple NuGet configuration files");
    }

    [Test]
    public async Task ExplicitlyDisabledPreviewSourceIsNotSilentlyEnabled()
    {
        using var fixture = new ConfigurationFixture();
        await fixture.WriteAsync("NuGet.Config", """
            <configuration>
              <packageSources><add key="SharpProofPreview" value="https://nuget.pkg.github.com/alexyorke/index.json" /></packageSources>
              <disabledPackageSources><add key="SharpProofPreview" value="TRUE" /></disabledPackageSources>
            </configuration>
            """);
        AssertRejected<InvalidOperationException>(fixture, "explicitly disabled");
    }

    [Test]
    public async Task CompetingRootAndConfigToolManifestsLeaveTheWholePlanUnwritten()
    {
        using var fixture = new ConfigurationFixture();
        await fixture.WriteAsync("dotnet-tools.json", ConfigurationFixture.EmptyManifest);
        AssertRejected<InvalidOperationException>(fixture, "Multiple tool manifests");
    }

    [Test]
    public async Task RootToolManifestIsUpdatedInPlaceWithoutCreatingALocalManifest()
    {
        using var fixture = new ConfigurationFixture();
        File.Delete(Path.Combine(fixture.Root, ".config", "dotnet-tools.json"));
        await fixture.WriteAsync("dotnet-tools.json", """
            {"version":1,"isRoot":false,"tools":{"company.tool":{"version":"2.0.0","commands":["company"]}}}
            """);
        _ = await fixture.InitializeAsync();
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "dotnet-tools.json"), TestContext.CurrentContext.CancellationToken));
        Assert.That(manifest.RootElement.GetProperty("isRoot").GetBoolean(), Is.False);
        Assert.That(manifest.RootElement.GetProperty("tools").GetProperty("company.tool").GetProperty("version").GetString(), Is.EqualTo("2.0.0"));
        Assert.That(manifest.RootElement.GetProperty("tools").GetProperty("sharpproof.tool").GetProperty("version").GetString(), Is.EqualTo(SetupInitializer.PackageVersion));
        Assert.That(File.Exists(Path.Combine(fixture.Root, ".config", "dotnet-tools.json")), Is.False);
    }

    [Test]
    public async Task MalformedManifestIsReportedAsInvalidEvidenceBeforeAnyWrites()
    {
        using var fixture = new ConfigurationFixture();
        await fixture.WriteAsync(".config/dotnet-tools.json", "{ invalid JSON");
        AssertRejected<InvalidDataException>(fixture, "well-formed JSON");
    }

    [TestCase("""{"version":2,"isRoot":true,"tools":{}}""")]
    [TestCase("""{"version":1,"tools":{}}""")]
    [TestCase("""{"version":1,"isRoot":true,"tools":[]}""")]
    public async Task UnsupportedManifestSchemasAreNotReplaced(string manifest)
    {
        ArgumentException.ThrowIfNullOrEmpty(manifest);
        using var fixture = new ConfigurationFixture();
        await fixture.WriteAsync(".config/dotnet-tools.json", manifest);
        AssertRejected<InvalidOperationException>(fixture, "version-1 .NET tool manifest");
    }

    [Test]
    public async Task CaseDuplicateSharpProofToolEntriesRequireManualResolution()
    {
        using var fixture = new ConfigurationFixture();
        var entry = new { version = SetupInitializer.PackageVersion, commands = ImmutableArray.Create("dotnet-sharpproof") };
        var tools = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["sharpproof.tool"] = entry,
            ["SharpProof.Tool"] = entry
        };
        await fixture.WriteAsync(".config/dotnet-tools.json", JsonSerializer.Serialize(new { version = 1, isRoot = true, tools }));
        AssertRejected<InvalidOperationException>(fixture, "duplicate SharpProof tool entries");
    }

    [TestCase("packageSources")]
    [TestCase("packageSourceMapping")]
    public async Task RepeatedNugetSectionsArePreservedForManualReview(string section)
    {
        ArgumentException.ThrowIfNullOrEmpty(section);
        using var fixture = new ConfigurationFixture();
        var config = new XDocument(new XElement("configuration", new XElement(section), new XElement(section)));
        await fixture.WriteAsync("NuGet.Config", config.ToString());
        AssertRejected<InvalidOperationException>(fixture, "Repeated NuGet configuration sections");
    }

    [TestCase("sources")]
    [TestCase("mapping")]
    public async Task CaseDuplicatePreviewSourceEntriesAreNotMerged(string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        using var fixture = new ConfigurationFixture();
        var entries = kind == "sources"
            ? new XElement("packageSources",
                new XElement("add", new XAttribute("key", "SharpProofPreview"), new XAttribute("value", "https://nuget.pkg.github.com/alexyorke/index.json")),
                new XElement("add", new XAttribute("key", "sharpproofpreview"), new XAttribute("value", "https://nuget.pkg.github.com/alexyorke/index.json")))
            : new XElement("packageSourceMapping", new XElement("packageSource", new XAttribute("key", "SharpProofPreview")),
                new XElement("packageSource", new XAttribute("key", "SHARPPROOFPREVIEW")));
        await fixture.WriteAsync("NuGet.Config", new XDocument(new XElement("configuration", entries)).ToString());
        AssertRejected<InvalidOperationException>(fixture, "Repeated setup entries");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CentralManagementActivationMustBeAnExplicitBoolean(bool inCentralFile)
    {
        using var fixture = new ConfigurationFixture();
        const string property = "<PropertyGroup><ManagePackageVersionsCentrally>$(UseCentralVersions)</ManagePackageVersionsCentrally></PropertyGroup>";
        await fixture.WriteAsync(inCentralFile ? "Directory.Packages.props" : "Consumer.csproj",
            inCentralFile ? "<Project>" + property + "</Project>" : "<Project Sdk=\"Microsoft.NET.Sdk\">" + property + "</Project>");
        AssertRejected<InvalidOperationException>(fixture, "literal true or false");
    }

    [Test]
    public async Task CentralVersionFileWithoutActivationDoesNotImplyPolicy()
    {
        using var fixture = new ConfigurationFixture();
        await fixture.WriteAsync("Directory.Packages.props", "<Project><ItemGroup><PackageVersion Include=\"Company.Library\" Version=\"2.0.0\" /></ItemGroup></Project>");
        AssertRejected<InvalidOperationException>(fixture, "Central package version activation is ambiguous");
    }

    [Test]
    public async Task ExplicitProjectCentralManagementCreatesPinnedCentralVersions()
    {
        using var fixture = new ConfigurationFixture();
        var project = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("ManagePackageVersionsCentrally", "true")),
            new XElement("ItemGroup",
                new XElement("PackageReference", new XAttribute("Include", "SharpProof"), new XElement("Version", SetupInitializer.PackageVersion)),
                new XElement("PackageReference", new XAttribute("Include", "SharpProof.CodeFixes"), new XElement("Version", SetupInitializer.PackageVersion), new XElement("PrivateAssets", "all")))));
        await fixture.WriteAsync("Consumer.csproj", project.ToString());
        _ = await fixture.InitializeAsync();
        var updated = XDocument.Parse(await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken));
        Assert.That(updated.Descendants("PackageReference").All(static reference =>
        {
            ArgumentNullException.ThrowIfNull(reference);
            return reference.Attribute("Version") == null && reference.Element("Version") == null;
        }), Is.True);
        var central = XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Directory.Packages.props"), TestContext.CurrentContext.CancellationToken));
        Assert.That(central.Descendants("ManagePackageVersionsCentrally").Single().Value, Is.EqualTo("true"));
        Assert.That(central.Descendants("PackageVersion").Select(static item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Attribute("Include")!.Value),
            Is.EquivalentTo(ImmutableArray.Create("SharpProof", "SharpProof.CodeFixes")));
        Assert.That(central.Descendants("PackageVersion").All(static item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Attribute("Version")!.Value == SetupInitializer.PackageVersion), Is.True);
    }

    [TestCase("attribute-and-element")]
    [TestCase("duplicate-elements")]
    [TestCase("conditional-element")]
    public async Task AmbiguousPackageVersionMetadataIsNotNormalizedSilently(string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        using var fixture = new ConfigurationFixture();
        var reference = new XElement("PackageReference", new XAttribute("Include", "SharpProof"), new XElement("Version", SetupInitializer.PackageVersion));
        switch (kind)
        {
            case "attribute-and-element":
                reference.Add(new XAttribute("Version", SetupInitializer.PackageVersion));
                break;
            case "duplicate-elements":
                reference.Add(new XElement("Version", SetupInitializer.PackageVersion));
                break;
            case "conditional-element":
                reference.Element("Version")!.Add(new XAttribute("Condition", "'$(Configuration)' == 'Debug'"));
                break;
        }

        await fixture.WriteAsync("Consumer.csproj", new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), new XElement("ItemGroup", reference))).ToString());
        AssertRejected<InvalidOperationException>(fixture, "Ambiguous package metadata");
    }

    [Test]
    public async Task ExistingEditorAssetPolicyIsNotOverwrittenToMakeInitializationPass()
    {
        using var fixture = new ConfigurationFixture();
        var project = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), new XElement("ItemGroup",
            new XElement("PackageReference", new XAttribute("Include", "SharpProof.CodeFixes"), new XAttribute("Version", SetupInitializer.PackageVersion), new XAttribute("PrivateAssets", "none")))));
        await fixture.WriteAsync("Consumer.csproj", project.ToString());
        AssertRejected<InvalidOperationException>(fixture, "Existing package metadata conflicts");
    }

    [Test]
    public async Task ExistingCentralFacadeVersionIsNotOverwritten()
    {
        using var fixture = new ConfigurationFixture();
        await fixture.WriteAsync("Directory.Packages.props", "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><PackageVersion Include=\"SharpProof\" Version=\"0.0.0-conflict\" /></ItemGroup></Project>");
        AssertRejected<InvalidOperationException>(fixture, "Existing package metadata conflicts");
    }

    private static void AssertRejected<TException>(ConfigurationFixture fixture, string message) where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrEmpty(message);
        var before = fixture.Snapshot();
        var error = Assert.ThrowsAsync<TException>(new Func<Task>(async () => _ = await fixture.InitializeAsync()));
        Assert.That(error!.Message, Does.Contain(message));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
        Assert.That(Directory.Exists(Path.Combine(fixture.Root, "obj")), Is.False);
        Assert.That(Directory.GetFiles(fixture.Root, ".sharpproof-init-*", SearchOption.AllDirectories), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Linux")]
    public async Task InitializationPreservesColdRestoreFromAnInheritedFeed(bool userLevel)
    {
        using var fixture = new ConfigurationFixture();
        var feed = Path.Combine(fixture.Root, "feed");
        var package = Path.Combine(fixture.Root, "package");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(package);
        await fixture.WriteAsync("package/Company.Demo.nuspec", "<package><metadata><id>Company.Demo</id><version>1.0.0</version><authors>Test</authors><description>Local restore fixture</description></metadata></package>");
        await System.IO.Compression.ZipFile.CreateFromDirectoryAsync(package, Path.Combine(feed, "Company.Demo.1.0.0.nupkg"), TestContext.CurrentContext.CancellationToken);
        await fixture.WriteAsync(userLevel ? "home/.nuget/NuGet/NuGet.Config" : "NuGet.Config", new XDocument(new XElement("configuration", new XElement("packageSources",
            new XElement("clear"), new XElement("add", new XAttribute("key", "Company"), new XAttribute("value", feed))))).ToString());
        await fixture.WriteAsync("app/NuGet.Config", "<configuration><packageSources><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" /></packageSources></configuration>");
        const string project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Company.Demo\" Version=\"1.0.0\" /></ItemGroup></Project>";
        await fixture.WriteAsync("app/Consumer.csproj", project);
        await fixture.WriteAsync("app/Control.csproj", project);
        var app = Path.Combine(fixture.Root, "app");
        async Task<(int ExitCode, string Output)> RunDotnetAsync(params string[] arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            return userLevel
                ? await ToolProcess.RunAsync("/usr/bin/env", app, TestContext.CurrentContext.CancellationToken,
                    ["HOME=" + Path.Combine(fixture.Root, "home"), "DOTNET_CLI_HOME=" + Path.Combine(fixture.Root, "home"), "dotnet", .. arguments])
                : await ToolProcess.RunAsync("dotnet", app, TestContext.CurrentContext.CancellationToken, arguments);
        }
        var before = await RunDotnetAsync("restore", "Control.csproj", "--packages", Path.Combine(fixture.Root, "before"), "--nologo");
        Assert.That(before.ExitCode, Is.Zero, before.Output);
        if (userLevel)
        {
            var initialized = await RunDotnetAsync(typeof(SetupInitializer).Assembly.Location, "init", "--project", Path.Combine(app, "Consumer.csproj"), "--apply");
            Assert.That(initialized.ExitCode, Is.Zero, initialized.Output);
        }
        else
        { _ = await SetupInitializer.InitializeAsync(Path.Combine(app, "Consumer.csproj"), "portable", true, null, TestContext.CurrentContext.CancellationToken); }
        var after = await RunDotnetAsync("restore", "Control.csproj", "--packages", Path.Combine(fixture.Root, "after"), "--nologo");
        Assert.That(after.ExitCode, Is.Zero, after.Output);
    }

    [Test]
    public async Task InitializationDoesNotBroadenInheritedPackageMappings()
    {
        using var fixture = new ConfigurationFixture();
        await fixture.WriteAsync("NuGet.Config", "<configuration><packageSources><clear/><add key=\"Company\" value=\"https://packages.example.test/v3/index.json\" /></packageSources><packageSourceMapping><packageSource key=\"Company\"><package pattern=\"Company.*\" /></packageSource></packageSourceMapping></configuration>");
        await fixture.WriteAsync("app/NuGet.Config", "<configuration />");
        await fixture.WriteAsync("app/Consumer.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var parent = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "NuGet.Config"), TestContext.CurrentContext.CancellationToken);
        _ = await SetupInitializer.InitializeAsync(Path.Combine(fixture.Root, "app/Consumer.csproj"), "portable", true, null, TestContext.CurrentContext.CancellationToken);
        var child = XDocument.Load(Path.Combine(fixture.Root, "app/NuGet.Config"));
        Assert.That(child.Descendants("package").Select(static element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return (string?)element.Attribute("pattern");
        }), Is.EqualTo(ImmutableArray.Create("SharpProof*")));
        Assert.That(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "NuGet.Config"), TestContext.CurrentContext.CancellationToken), Is.EqualTo(parent));
    }

    [TestCase("SharpProof.Attributes", false)]
    [TestCase("SharpProof.Verifier", false)]
    [TestCase("SharpProof.Attributes", true)]
    [TestCase("SharpProof.Verifier", true)]
    public async Task IncompatibleCorePackagePinsAreRejectedBeforeAnyWrites(string package, bool central)
    {
        ArgumentException.ThrowIfNullOrEmpty(package);
        using var fixture = new ConfigurationFixture();
        if (central)
        {
            await fixture.WriteAsync("Directory.Packages.props", "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><PackageVersion Include=\"" + package + "\" Version=\"0.9.0\" /></ItemGroup></Project>");
        }
        else
        {
            await fixture.WriteAsync("Consumer.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"" + package + "\" Version=\"0.9.0\" /></ItemGroup></Project>");
        }
        AssertRejected<InvalidOperationException>(fixture, "package versions conflict");
    }

    [Test]
    public async Task InheritedAuthenticationConfigurationIsNotCopiedOrRewritten()
    {
        using var fixture = new ConfigurationFixture();
        // An environment reference supplies no credential and makes no request.
        const string inherited = "<configuration><packageSources><add key=\"SharpProofPreview\" value=\"https://nuget.pkg.github.com/alexyorke/index.json\" /></packageSources><packageSourceCredentials><SharpProofPreview><add key=\"Username\" value=\"fixture\" /><add key=\"ClearTextPassword\" value=\"%SHARPPROOF_TEST_PASSWORD_REFERENCE%\" /></SharpProofPreview></packageSourceCredentials></configuration>";
        await fixture.WriteAsync("NuGet.Config", inherited);
        await fixture.WriteAsync("app/NuGet.Config", "<configuration />");
        await fixture.WriteAsync("app/Consumer.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        _ = await SetupInitializer.InitializeAsync(Path.Combine(fixture.Root, "app/Consumer.csproj"), "portable", true, null, TestContext.CurrentContext.CancellationToken);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "NuGet.Config"), TestContext.CurrentContext.CancellationToken), Is.EqualTo(inherited));
        var generated = XDocument.Load(Path.Combine(fixture.Root, "app/NuGet.Config"));
        Assert.That(generated.Root!.Element("packageSourceCredentials"), Is.Null);
    }

    [TestCase("SharpProof.Attributes")]
    [TestCase("SharpProof.Verifier")]
    public async Task MatchingExplicitCorePackagePinsRemainSupported(string package)
    {
        ArgumentException.ThrowIfNullOrEmpty(package);
        using var fixture = new ConfigurationFixture();
        await fixture.WriteAsync("Consumer.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"" + package + "\" Version=\"" + SetupInitializer.PackageVersion + "\" /></ItemGroup></Project>");
        _ = await fixture.InitializeAsync();
        var project = XDocument.Load(fixture.ProjectPath);
        var reference = project.Descendants("PackageReference").Single(element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return (string?)element.Attribute("Include") == package;
        });
        Assert.That((string?)reference.Attribute("Version"), Is.EqualTo(SetupInitializer.PackageVersion));
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        internal const string EmptyManifest = """{"version":1,"isRoot":true,"tools":{}}""";
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "sharpproof-init-configuration-" + Guid.NewGuid().ToString("N"));
        internal string ProjectPath => Path.Combine(Root, "Consumer.csproj");

        internal ConfigurationFixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, ".config"));
            File.WriteAllText(ProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Target Name=\"MustNotRun\" BeforeTargets=\"Restore;Build\"><Error Text=\"must-not-run\" /></Target></Project>", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Root, "NuGet.Config"), "<configuration />", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Root, ".config", "dotnet-tools.json"), EmptyManifest, new UTF8Encoding(false));
        }

        internal Task<SetupInitializationResult> InitializeAsync()
        {
            return SetupInitializer.InitializeAsync(ProjectPath, "portable", true, null, TestContext.CurrentContext.CancellationToken);
        }

        internal async Task WriteAsync(string relativePath, string content)
        {
            ArgumentException.ThrowIfNullOrEmpty(relativePath);
            ArgumentNullException.ThrowIfNull(content);
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), TestContext.CurrentContext.CancellationToken);
        }

        internal Dictionary<string, string> Snapshot()
        {
            return Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories).ToDictionary(static path =>
                path ?? throw new ArgumentNullException(nameof(path)), static path =>
            {
                ArgumentNullException.ThrowIfNull(path);
                return Directory.Exists(path) ? "directory" : Convert.ToBase64String(File.ReadAllBytes(path));
            }, StringComparer.Ordinal);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
