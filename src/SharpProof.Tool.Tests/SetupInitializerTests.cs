using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;
using SharpProof.Tool;

namespace SharpProof.Tool.Test;

[TestFixture]
public sealed class SetupInitializerTests
{
    [TestCase("portable")]
    [TestCase("container")]
    public async Task PreviewListsTypedChangesWithoutWritingOrExecutingProjectTargets(string verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        using var fixture = new InitFixture();
        var before = fixture.Snapshot();
        var result = await fixture.InitializeAsync(verification, false, verification == "container" ? Image : null);
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
        Assert.That(result.Files, Has.Count.EqualTo(verification == "container" ? 5 : 3));
        Assert.That(result.Files.All(static file => { ArgumentNullException.ThrowIfNull(file); return file.Changed && !file.Written; }), Is.True);
        Assert.That(result.Message, Does.Contain("PREVIEW").And.Contain("Preview only").And.Not.Contain("must-not-run"));
        Assert.That(Directory.Exists(Path.Combine(fixture.Root, "obj")), Is.False);
    }

    [Test]
    public async Task PortableApplyPinsFacadePrivateEditorToolAndCredentialFreeFeed()
    {
        using var fixture = new InitFixture();
        var result = await fixture.InitializeAsync("portable", true, null);
        var project = XDocument.Parse(await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken));
        Assert.That(project.Descendants("SharpProofProfile").Single().Value, Is.EqualTo("advisory"));
        Assert.That(project.Descendants("SharpProofVerify").Single().Value, Is.EqualTo("false"));
        var references = project.Descendants("PackageReference").ToArray();
        Assert.That(references, Has.Length.EqualTo(2));
        Assert.That((string?)references[0].Attribute("Include"), Is.EqualTo("SharpProof"));
        Assert.That((string?)references[0].Attribute("Version"), Is.EqualTo(SetupInitializer.PackageVersion));
        Assert.That(references[0].Attribute("PrivateAssets"), Is.Null);
        Assert.That((string?)references[1].Attribute("PrivateAssets"), Is.EqualTo("all"));
        var config = XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "NuGet.Config"), TestContext.CurrentContext.CancellationToken));
        Assert.That(config.Descendants("add").Any(static element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return (string?)element.Attribute("key") == "SharpProofPreview" &&
                (string?)element.Attribute("value") == "https://nuget.pkg.github.com/alexyorke/index.json";
        }), Is.True);
        Assert.That(config.Descendants("package").Any(static element =>
        {
            ArgumentNullException.ThrowIfNull(element);
            return (string?)element.Attribute("pattern") == "SharpProof*";
        }), Is.True);
        Assert.That(config.Descendants("packageSourceCredentials"), Is.Empty);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, ".config", "dotnet-tools.json"), TestContext.CurrentContext.CancellationToken));
        var tool = manifest.RootElement.GetProperty("tools").GetProperty("sharpproof.tool");
        Assert.That(tool.GetProperty("version").GetString(), Is.EqualTo(SetupInitializer.PackageVersion));
        Assert.That(tool.GetProperty("commands")[0].GetString(), Is.EqualTo("dotnet-sharpproof"));
        Assert.That(result.Files.All(static file => { ArgumentNullException.ThrowIfNull(file); return file.Written; }), Is.True);
    }

    [TestCase("portable")]
    [TestCase("container")]
    public async Task RepeatedApplyIsByteStableAndDoesNotRewriteFiles(string verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        using var fixture = new InitFixture();
        _ = await fixture.InitializeAsync(verification, true, verification == "container" ? Image : null);
        var before = fixture.Snapshot();
        var result = await fixture.InitializeAsync(verification, true, null);
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
        Assert.That(result.Files.All(static file => { ArgumentNullException.ThrowIfNull(file); return !file.Changed && !file.Written; }), Is.True);
        Assert.That(result.Message, Does.Not.Contain("APPLIED"));
    }

    [Test]
    public async Task SimpleCentralManagementPinsVersionsWithoutVersionedProjectReferences()
    {
        using var fixture = new InitFixture();
        await fixture.WriteAsync("Directory.Packages.props", "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><PackageVersion Include=\"Unrelated\" Version=\"2.3.4\" /></ItemGroup></Project>");
        _ = await fixture.InitializeAsync("portable", true, null);
        var project = XDocument.Parse(await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken));
        Assert.That(project.Descendants("PackageReference").All(static reference =>
        {
            ArgumentNullException.ThrowIfNull(reference);
            return reference.Attribute("Version") == null;
        }), Is.True);
        var central = XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Directory.Packages.props"), TestContext.CurrentContext.CancellationToken));
        Assert.That(central.Descendants("PackageVersion"), Is.Not.Empty);
        Assert.That(central.Descendants("PackageVersion").Count(), Is.EqualTo(3));
        Assert.That((string?)central.Descendants("PackageVersion").First().Attribute("Version"), Is.EqualTo("2.3.4"));
        var before = fixture.Snapshot();
        _ = await fixture.InitializeAsync("portable", true, null);
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task ExistingUnrelatedXmlAndManifestSettingsSurvive()
    {
        using var fixture = new InitFixture();
        await fixture.WriteAsync("NuGet.Config", "<configuration><!--keep-comment--><packageSources><add key=\"Company\" value=\"https://packages.example.test/v3/index.json\" /></packageSources><config><add key=\"dependencyVersion\" value=\"Highest\" /></config></configuration>");
        await fixture.WriteAsync(".config/dotnet-tools.json", "{\"version\":1,\"isRoot\":false,\"tools\":{\"unrelated.tool\":{\"version\":\"2.0.0\",\"commands\":[\"unrelated\"]}},\"extension\":\"keep-me\"}");
        _ = await fixture.InitializeAsync("portable", true, null);
        var config = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "NuGet.Config"), TestContext.CurrentContext.CancellationToken);
        Assert.That(config, Does.Contain("keep-comment").And.Contain("Highest").And.Contain("packages.example.test"));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, ".config", "dotnet-tools.json"), TestContext.CurrentContext.CancellationToken));
        Assert.That(manifest.RootElement.GetProperty("isRoot").GetBoolean(), Is.False);
        Assert.That(manifest.RootElement.GetProperty("extension").GetString(), Is.EqualTo("keep-me"));
        Assert.That(manifest.RootElement.GetProperty("tools").GetProperty("unrelated.tool").GetProperty("version").GetString(), Is.EqualTo("2.0.0"));
    }

    [TestCase("profile")]
    [TestCase("version")]
    [TestCase("conditional")]
    [TestCase("import")]
    [TestCase("private-facade")]
    [TestCase("custom-sdk")]
    public async Task AmbiguousOrConflictingProjectConfigurationLeavesEveryFileUnchanged(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        using var fixture = new InitFixture();
        var content = kind switch
        {
            "profile" => "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><SharpProofProfile>strict</SharpProofProfile></PropertyGroup></Project>",
            "version" => "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"SharpProof\" Version=\"0.1.0\" /></ItemGroup></Project>",
            "conditional" => "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup Condition=\"'$(Configuration)'=='Debug'\"><PackageReference Include=\"SharpProof\" /></ItemGroup></Project>",
            "import" => "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"custom.props\" /></Project>",
            "private-facade" => "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"SharpProof\" PrivateAssets=\"all\" /></ItemGroup></Project>",
            "custom-sdk" => "<Project Sdk=\"Custom.Sdk\" />",
            _ => throw new AssertionException("Unknown fixture.")
        };
        await File.WriteAllTextAsync(fixture.ProjectPath, content, TestContext.CurrentContext.CancellationToken);
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("portable", true, null)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [TestCase("feed")]
    [TestCase("mapping")]
    [TestCase("manifest")]
    [TestCase("credentials")]
    public async Task LateConfigurationConflictsFailBeforeProjectOrOtherConfigWrites(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        using var fixture = new InitFixture();
        switch (kind)
        {
            case "feed":
                await fixture.WriteAsync("NuGet.Config", "<configuration><packageSources><add key=\"SharpProofPreview\" value=\"https://other.example.test/feed\" /></packageSources></configuration>");
                break;
            case "mapping":
                await fixture.WriteAsync("NuGet.Config", "<configuration><packageSourceMapping><packageSource key=\"Other\"><package pattern=\"SharpProof*\" /></packageSource></packageSourceMapping></configuration>");
                break;
            case "manifest":
                await fixture.WriteAsync(".config/dotnet-tools.json", "{\"version\":1,\"isRoot\":true,\"tools\":{\"sharpproof.tool\":{\"version\":\"0.1.0\",\"commands\":[\"dotnet-sharpproof\"]}}}");
                break;
            case "credentials":
                await fixture.WriteAsync("NuGet.Config", "<configuration><packageSourceCredentials><SharpProofPreview /></packageSourceCredentials></configuration>");
                break;
            default:
                throw new AssertionException("Unknown fixture.");
        }
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("portable", true, null)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task DirectoryCollisionFailsWithoutCreatingOtherFiles()
    {
        using var fixture = new InitFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "NuGet.Config"));
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("portable", true, null)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task ContainerApplyCreatesGuardedIsolatedStarterAndRetainedEvidence()
    {
        using var fixture = new InitFixture();
        _ = await fixture.InitializeAsync("container", true, Image);
        var compose = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "compose.yaml"), TestContext.CurrentContext.CancellationToken);
        Assert.That(compose, Does.Contain(Image).And.Contain("platform: linux/amd64").And.Contain("sharpproof-dotnet:")
            .And.Contain("- NuGetPackageSourceCredentials_SharpProofPreview").And.Not.Contain("Password="));
        var props = XDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "Directory.Build.props"), TestContext.CurrentContext.CancellationToken));
        Assert.That((string?)props.Root!.Element("PropertyGroup")!.Attribute("Condition"), Is.EqualTo("'$(SHARPPROOF_CONTAINER)' == '1'"));
        Assert.That(props.Descendants("BaseIntermediateOutputPath").Single().Value, Does.Contain(".sharpproof-obj/$(SharpProofProjectOutputKey)/"));
        var project = XDocument.Parse(await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken));
        Assert.That(project.Descendants("SharpProofProfile").Single().Value, Is.EqualTo("strict"));
        Assert.That(project.Descendants("SharpProofVerifyResultFile").Single().Value, Does.EndWith("/artifacts/sharpproof/result.json"));
    }

    [Test]
    public async Task CompatibleExistingComposeSuppliesTheImageAndPreservesOtherServices()
    {
        using var fixture = new InitFixture();
        var compose = SetupInitializerContainer.Compose(Image).Replace("volumes:\n  sharpproof-obj:", "  cache:\n    image: unrelated.example.test/cache:1\nvolumes:\n  sharpproof-obj:", StringComparison.Ordinal);
        await fixture.WriteAsync("compose.yaml", compose);
        _ = await fixture.InitializeAsync("container", true, null);
        Assert.That(await File.ReadAllTextAsync(Path.Combine(fixture.Root, "compose.yaml"), TestContext.CurrentContext.CancellationToken), Is.EqualTo(compose));
    }

    [TestCase(null)]
    [TestCase("ghcr.io/alexyorke/sharpproof-preview:latest")]
    [TestCase("ghcr.io/other/sharpproof-preview@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task MissingOrUnqualifiedContainerPinNeverWrites(string? image)
    {
        using var fixture = new InitFixture();
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("container", true, image)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task ConflictingExistingComposeIsNotReplaced()
    {
        using var fixture = new InitFixture();
        await fixture.WriteAsync("compose.yaml", "services:\n  verify:\n    image: arbitrary:latest\n");
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("container", true, Image)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task ImportingCentralVersionsAreRejectedWithoutWrites()
    {
        using var fixture = new InitFixture();
        await fixture.WriteAsync("Directory.Packages.props", "<Project><Import Project=\"../other.props\" /></Project>");
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("portable", true, null)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public void CancellationPrecedesAnySetupWrites()
    {
        using var fixture = new InitFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () => _ = await SetupInitializer.InitializeAsync(
            fixture.ProjectPath, "portable", true, null, cancellation.Token)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [Test]
    public async Task ChangedInputsAreDetectedBeforeThePlanApplies()
    {
        using var fixture = new InitFixture();
        var files = new SetupInitializerFiles(TestContext.CurrentContext.CancellationToken);
        files.Add(fixture.ProjectPath, "planned");
        await File.WriteAllTextAsync(fixture.ProjectPath, "external-edit", TestContext.CurrentContext.CancellationToken);
        Assert.ThrowsAsync<IOException>(new Func<Task>(async () => await files.ApplyAsync(TestContext.CurrentContext.CancellationToken)));
        Assert.That(await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo("external-edit"));
    }

    [Test]
    public async Task TextEntrypointReportsPreviewAndRejectsUnsupportedModes()
    {
        using var fixture = new InitFixture();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await SetupInitializer.RunAsync(fixture.ProjectPath, "portable", false, null, output, TestContext.CurrentContext.CancellationToken), Is.Zero);
        Assert.That(output.ToString(), Does.Contain("Preview only"));
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("native", false, null)));
        Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("portable", false, Image)));
    }

    [TestCase("ghcr.io/alexyorke/sharpproof", true)]
    [TestCase("ghcr.io/alexyorke/sharpproof-preview", true)]
    [TestCase("ghcr.io/other/sharpproof-preview", false)]
    [TestCase("ghcr.io/alexyorke/sharpproof-preview-extra", false)]
    public void SharedImagePredicateKeepsTheExactCanonicalAndPreviewNamespaces(string repository, bool expected)
    {
        ArgumentNullException.ThrowIfNull(repository);
        Assert.That(SetupInitializer.IsPinnedConsumerImage(repository + "@sha256:" + new string('a', 64)), Is.EqualTo(expected));
    }

    [Test]
    public async Task DoctorInspectsTheWorkspaceComposeFromANestedProjectWithoutRunningTargets()
    {
        using var fixture = new InitFixture();
        _ = await fixture.InitializeAsync("container", true, Image);
        await fixture.WriteAsync("src/app/Nested.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var projectPath = Path.Combine(fixture.Root, "src", "app", "Nested.csproj");
        var assetsPath = Path.Combine(fixture.Root, "project.assets.json");
        var libraries = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["SharpProof/" + SetupInitializer.PackageVersion] = new { type = "package" },
            ["SharpProof.Attributes/" + SetupInitializer.PackageVersion] = new { type = "package" },
            ["SharpProof.Verifier/" + SetupInitializer.PackageVersion] = new { type = "package" }
        };
        await fixture.WriteAsync("project.assets.json", JsonSerializer.Serialize(new { libraries }));
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NETCoreSdkVersion"] = "10.0.402",
            ["SharpProofProfile"] = "strict",
            ["SharpProofFeatures"] = "all",
            ["SharpProofVerify"] = "true",
            ["SharpProofVerifyPolicy"] = "require-proven",
            ["SharpProofAssumptionPolicy"] = "error",
            ["DefineConstants"] = string.Empty,
            ["ProjectAssetsFile"] = assetsPath,
            ["TargetFramework"] = "net10.0",
            ["TargetFrameworks"] = string.Empty
        };
        string? composeDirectory = null;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = await SetupDoctor.RunAsync(projectPath, "container", output, (command, directory, token, arguments) =>
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(directory);
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();
            if (arguments[0] == "compose")
            { composeDirectory = directory; }
            var response = arguments[0] switch
            {
                "msbuild" => JsonSerializer.Serialize(new { Properties = properties, Items = new { PackageReference = new[] { new { Identity = "SharpProof" } } } }),
                "info" => "linux",
                "compose" => JsonSerializer.Serialize(new { services = new { verify = new { image = Image, platform = "linux/amd64" } } }),
                "image" => JsonSerializer.Serialize(new[] { new { Os = "linux", Architecture = "amd64", Config = new { Labels = new Dictionary<string, string> { ["io.sharpproof.consumer.version"] = SetupInitializer.PackageVersion } } } }),
                _ => throw new AssertionException("Unexpected inspection command.")
            };
            return Task.FromResult((0, response));
        }, false, TestContext.CurrentContext.CancellationToken);
        Assert.That(exitCode, Is.Zero, output.ToString());
        Assert.That(composeDirectory, Is.EqualTo(fixture.Root));
    }

    [Test]
    public async Task DoctorDoesNotCrossTheNearestRepositoryBoundaryForCompose()
    {
        using var fixture = new InitFixture();
        await fixture.WriteAsync("compose.yaml", SetupInitializerContainer.Compose(Image));
        var directory = Path.Combine(fixture.Root, "inner", "src", "app");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "inner", ".git"));
        Assert.That(SetupDoctor.FindComposeDirectory(directory, TestContext.CurrentContext.CancellationToken), Is.EqualTo(directory));
    }

    [Test]
    public async Task DoctorDoesNotAdoptAnUnmarkedAncestorCompose()
    {
        using var fixture = new InitFixture();
        await fixture.WriteAsync("compose.yaml", SetupInitializerContainer.Compose(Image));
        var directory = Path.Combine(fixture.Root, "src", "app");
        Directory.CreateDirectory(directory);
        Assert.That(SetupDoctor.FindComposeDirectory(directory, TestContext.CurrentContext.CancellationToken), Is.EqualTo(directory));
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task FilesCreatedOrEditedDuringStagingArePreservedBeforeAnyCommit(bool existed)
    {
        using var fixture = new InitFixture();
        var target = Path.Combine(fixture.Root, "NuGet.Config");
        if (existed)
        { await fixture.WriteAsync("NuGet.Config", "<configuration />"); }
        var originalProject = await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken);
        var plan = new SetupInitializerFiles(TestContext.CurrentContext.CancellationToken);
        plan.Add(fixture.ProjectPath, "planned-project-content");
        plan.Add(target, "planned-feed-content");
        const string external = "<configuration><packageSourceCredentials><Company /></packageSourceCredentials></configuration>";
        Assert.ThrowsAsync<IOException>(new Func<Task>(async () => await plan.ApplyAsync(async token =>
        {
            await File.WriteAllTextAsync(target, external, token);
        }, TestContext.CurrentContext.CancellationToken)));
        Assert.That(await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(originalProject));
        Assert.That(await File.ReadAllTextAsync(target, TestContext.CurrentContext.CancellationToken), Is.EqualTo(external));
        Assert.That(Directory.EnumerateFiles(fixture.Root, ".sharpproof-init-*"), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LateVerificationFailureRollsBackEarlierCommits(bool firstExisted)
    {
        using var fixture = new InitFixture();
        var first = firstExisted ? fixture.ProjectPath : Path.Combine(fixture.Root, "created.json");
        var original = firstExisted ? await File.ReadAllBytesAsync(first, TestContext.CurrentContext.CancellationToken) : null;
        var later = Path.Combine(fixture.Root, "NuGet.Config");
        await fixture.WriteAsync("NuGet.Config", "<configuration />");
        var plan = new SetupInitializerFiles(TestContext.CurrentContext.CancellationToken);
        plan.Add(first, "planned-first-content");
        plan.Add(later, "planned-later-content");
        const string external = "externally-updated-configuration";
        Assert.ThrowsAsync<IOException>(new Func<Task>(async () => await plan.ApplyAsync(static _ => Task.CompletedTask,
            async path =>
            {
                ArgumentNullException.ThrowIfNull(path);
                Assert.That(path, Is.EqualTo(first));
                await File.WriteAllTextAsync(later, external, TestContext.CurrentContext.CancellationToken);
            }, TestContext.CurrentContext.CancellationToken)));
        if (firstExisted)
        { Assert.That(await File.ReadAllBytesAsync(first, TestContext.CurrentContext.CancellationToken), Is.EqualTo(original)); }
        else
        { Assert.That(File.Exists(first), Is.False); }
        Assert.That(await File.ReadAllTextAsync(later, TestContext.CurrentContext.CancellationToken), Is.EqualTo(external));
        Assert.That(Directory.EnumerateFiles(fixture.Root, ".sharpproof-init-*"), Is.Empty);
    }

    [Test]
    public async Task CancellationAfterACommitRestoresOriginalFilesWithoutTheCanceledToken()
    {
        using var fixture = new InitFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CurrentContext.CancellationToken);
        var original = await File.ReadAllBytesAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken);
        var later = Path.Combine(fixture.Root, "NuGet.Config");
        var plan = new SetupInitializerFiles(cancellation.Token);
        plan.Add(fixture.ProjectPath, "planned-project-content");
        plan.Add(later, "planned-feed-content");
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () => await plan.ApplyAsync(static _ => Task.CompletedTask,
            path =>
            {
                ArgumentNullException.ThrowIfNull(path);
                Assert.That(path, Is.EqualTo(fixture.ProjectPath));
                cancellation.Cancel();
                return Task.CompletedTask;
            }, cancellation.Token)));
        Assert.That(await File.ReadAllBytesAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(original));
        Assert.That(File.Exists(later), Is.False);
        Assert.That(Directory.EnumerateFiles(fixture.Root, ".sharpproof-init-*"), Is.Empty);
    }

    [Test]
    public async Task FailureAfterSeveralCommitsRestoresTheWholePlan()
    {
        using var fixture = new InitFixture();
        var original = await File.ReadAllBytesAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken);
        var created = Path.Combine(fixture.Root, "NuGet.Config");
        var plan = new SetupInitializerFiles(TestContext.CurrentContext.CancellationToken);
        plan.Add(fixture.ProjectPath, "planned-project-content");
        plan.Add(created, "planned-feed-content");
        Assert.ThrowsAsync<IOException>(new Func<Task>(async () => await plan.ApplyAsync(static _ => Task.CompletedTask,
            path =>
            {
                ArgumentNullException.ThrowIfNull(path);
                if (path == created)
                { throw new IOException("simulated late publication failure"); }
                return Task.CompletedTask;
            }, TestContext.CurrentContext.CancellationToken)));
        Assert.That(await File.ReadAllBytesAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(original));
        Assert.That(File.Exists(created), Is.False);
        Assert.That(Directory.EnumerateFiles(fixture.Root, ".sharpproof-init-*"), Is.Empty);
    }

    [Test]
    public async Task RollbackPreservesConcurrentEditsAndRetainsTheOriginalRecoveryCopy()
    {
        using var fixture = new InitFixture();
        var original = await File.ReadAllBytesAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken);
        var plan = new SetupInitializerFiles(TestContext.CurrentContext.CancellationToken);
        plan.Add(fixture.ProjectPath, "planned-project-content");
        const string external = "concurrent-project-edit";
        var failure = Assert.ThrowsAsync<IOException>(new Func<Task>(async () => await plan.ApplyAsync(static _ => Task.CompletedTask,
            async path =>
            {
                ArgumentNullException.ThrowIfNull(path);
                await File.WriteAllTextAsync(path, external, TestContext.CurrentContext.CancellationToken);
                throw new IOException("simulated failure after a concurrent edit");
            }, TestContext.CurrentContext.CancellationToken)));
        Assert.That(await File.ReadAllTextAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(external));
        var backup = Directory.EnumerateFiles(fixture.Root, ".sharpproof-init-*.backup").Single();
        Assert.That(await File.ReadAllBytesAsync(backup, TestContext.CurrentContext.CancellationToken), Is.EqualTo(original));
        Assert.That(failure!.Message, Does.Contain(backup));
        Assert.That(Directory.EnumerateFiles(fixture.Root, ".sharpproof-init-*").Count(), Is.EqualTo(1));
    }

    [TestCase("empty")]
    [TestCase("invalid-utf8")]
    [TestCase("oversized")]
    public async Task UnreadableConcurrentEditsRetainRecoveryCopiesAndDoNotStopOtherRollbacks(string kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        using var fixture = new InitFixture();
        var originalProject = await File.ReadAllBytesAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken);
        var edited = Path.Combine(fixture.Root, "NuGet.Config");
        await fixture.WriteAsync("NuGet.Config", "<configuration />");
        var originalConfig = await File.ReadAllBytesAsync(edited, TestContext.CurrentContext.CancellationToken);
        var external = kind switch
        {
            "empty" => Array.Empty<byte>(),
            "invalid-utf8" => new byte[] { 0xff },
            _ => new byte[1024 * 1024 + 1]
        };
        var plan = new SetupInitializerFiles(TestContext.CurrentContext.CancellationToken);
        plan.Add(fixture.ProjectPath, "planned-project-content");
        plan.Add(edited, "planned-config-content");
        const string initiatingFailure = "simulated publication failure after a malformed concurrent edit";
        var failure = Assert.ThrowsAsync<IOException>(new Func<Task>(async () => await plan.ApplyAsync(static _ => Task.CompletedTask,
            async path =>
            {
                ArgumentNullException.ThrowIfNull(path);
                if (path == edited)
                {
                    await File.WriteAllBytesAsync(edited, external, TestContext.CurrentContext.CancellationToken);
                    throw new IOException(initiatingFailure);
                }
            }, TestContext.CurrentContext.CancellationToken)));
        Assert.That(await File.ReadAllBytesAsync(fixture.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(originalProject));
        Assert.That(await File.ReadAllBytesAsync(edited, TestContext.CurrentContext.CancellationToken), Is.EqualTo(external));
        var backup = Directory.EnumerateFiles(fixture.Root, ".sharpproof-init-*.backup").Single();
        Assert.That(await File.ReadAllBytesAsync(backup, TestContext.CurrentContext.CancellationToken), Is.EqualTo(originalConfig));
        Assert.That(failure!.Message, Does.Contain(backup));
        Assert.That(failure.InnerException, Is.TypeOf<AggregateException>());
        var failures = ((AggregateException)failure.InnerException!).InnerExceptions;
        Assert.That(failures, Has.Count.EqualTo(2));
        Assert.That(failures[0].Message, Is.EqualTo(initiatingFailure));
        Assert.That(Directory.EnumerateFiles(fixture.Root, ".sharpproof-init-*").Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task LinuxPrivateConfigRemainsPrivateDuringStagingAndAfterInitialization()
    {
        if (!OperatingSystem.IsLinux())
        { Assert.Ignore("The permission workflow requires the canonical Linux container."); return; }
        using var fixture = new InitFixture();
        // Use valid NuGet metadata; the permission test needs no credentials.
        const string privateConfig = "<configuration><config><add key=\"Company\" value=\"private-settings\" /></config></configuration>";
        await fixture.WriteAsync("NuGet.Config", privateConfig);
        var target = Path.Combine(fixture.Root, "NuGet.Config");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(target, mode);
        var plan = new SetupInitializerFiles(TestContext.CurrentContext.CancellationToken);
        plan.Add(target, privateConfig.Replace("</configuration>", "<!--updated--></configuration>", StringComparison.Ordinal));
        await plan.ApplyAsync(token =>
        {
            token.ThrowIfCancellationRequested();
            if (OperatingSystem.IsLinux())
            {
                foreach (var temporary in Directory.EnumerateFiles(fixture.Root, ".sharpproof-init-*"))
                { Assert.That(File.GetUnixFileMode(temporary), Is.EqualTo(mode)); }
            }
            return Task.CompletedTask;
        }, TestContext.CurrentContext.CancellationToken);
        Assert.That(File.GetUnixFileMode(target), Is.EqualTo(mode));
        _ = await fixture.InitializeAsync("portable", true, null);
        Assert.That(File.GetUnixFileMode(target), Is.EqualTo(mode));
        Assert.That(await File.ReadAllTextAsync(target, TestContext.CurrentContext.CancellationToken), Does.Contain("Company"));
    }

    [Test]
    public async Task InheritedCentralActivationIsRefusedBeforeAnyWrite()
    {
        using var fixture = new InitFixture();
        await fixture.WriteAsync("Directory.Build.props", "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>");
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("portable", true, null)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [TestCase("package")]
    [TestCase("property")]
    public async Task TargetScopedPackageAndActivationSettingsRequireManualSetup(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        using var fixture = new InitFixture();
        var settings = kind == "package" ? "<ItemGroup><PackageReference Include=\"SharpProof\" Version=\"" + SetupInitializer.PackageVersion + "\" /></ItemGroup>" :
            "<PropertyGroup><SharpProofProfile>advisory</SharpProofProfile></PropertyGroup>";
        await File.WriteAllTextAsync(fixture.ProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><Target Name=\"Late\">" + settings + "</Target></Project>", TestContext.CurrentContext.CancellationToken);
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("portable", true, null)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }

    [TestCase("    external: true")]
    [TestCase("    name: shared")]
    [TestCase("    driver_opts:\n      type: none\n      device: /shared\n      o: bind")]
    [TestCase("  sharpproof-obj:")]
    public async Task RequiredContainerVolumesRejectExternalBindingsOptionsAndDuplicates(string declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        using var fixture = new InitFixture();
        var compose = SetupInitializerContainer.Compose(Image).Replace("  sharpproof-obj:\n", "  sharpproof-obj:\n" + declaration + "\n", StringComparison.Ordinal);
        await fixture.WriteAsync("compose.yaml", compose);
        var before = fixture.Snapshot();
        Assert.ThrowsAsync<InvalidOperationException>(new Func<Task>(async () => _ = await fixture.InitializeAsync("container", true, null)));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
    }
    [Test]
    public async Task LinuxNewConfigPublishesReadableWhileExistingPrivateFilesStayPrivate()
    {
        if (!OperatingSystem.IsLinux())
        { Assert.Ignore("The permission workflow requires the canonical Linux container."); return; }
        using var fixture = new InitFixture();
        var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var publicMode = privateMode | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        File.SetUnixFileMode(fixture.ProjectPath, privateMode);
        var target = Path.Combine(fixture.Root, "NuGet.Config");
        var plan = new SetupInitializerFiles(TestContext.CurrentContext.CancellationToken);
        plan.Add(target, "<configuration />");
        await plan.ApplyAsync(token =>
        {
            token.ThrowIfCancellationRequested();
            Assert.That(File.Exists(target), Is.False);
            if (OperatingSystem.IsLinux())
            {
                foreach (var temporary in Directory.EnumerateFiles(fixture.Root, ".sharpproof-init-*"))
                { Assert.That(File.GetUnixFileMode(temporary), Is.EqualTo(privateMode)); }
            }
            return Task.CompletedTask;
        }, TestContext.CurrentContext.CancellationToken);
        Assert.That(File.GetUnixFileMode(target), Is.EqualTo(publicMode));
        _ = await fixture.InitializeAsync("portable", true, null);
        Assert.That(File.GetUnixFileMode(target), Is.EqualTo(publicMode));
        Assert.That(File.GetUnixFileMode(Path.Combine(fixture.Root, ".config", "dotnet-tools.json")), Is.EqualTo(publicMode));
        Assert.That(File.GetUnixFileMode(fixture.ProjectPath), Is.EqualTo(privateMode));
    }
    private const string Image = "ghcr.io/alexyorke/sharpproof-preview@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private sealed class InitFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "sharpproof-init-" + Guid.NewGuid().ToString("N"));
        internal string ProjectPath => Path.Combine(Root, "Consumer.csproj");

        internal InitFixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(ProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Target Name=\"MustNotRun\" BeforeTargets=\"Restore;Build\"><Error Text=\"must-not-run\" /></Target></Project>");
        }

        internal Task<SetupInitializationResult> InitializeAsync(string verification, bool apply, string? consumerImage)
        {
            ArgumentNullException.ThrowIfNull(verification);
            return SetupInitializer.InitializeAsync(ProjectPath, verification, apply, consumerImage, TestContext.CurrentContext.CancellationToken);
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
