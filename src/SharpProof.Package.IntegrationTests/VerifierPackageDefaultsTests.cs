using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using NUnit.Framework;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class VerifierPackageDefaultsTests
{
    private static readonly TempDirectory s_packageCache = TempDirectory.CreateOwned(
        "SharpProof.Verifier.Defaults.Test", "package-cache-", "Refusing to remove an unexpected verifier-defaults package cache.");
    private static readonly string s_sharedCompilationServerId = "sharpproof-verifier-defaults-" + Guid.NewGuid().ToString("N");

    private const string Source = """
        using SharpProof.Attributes;
        public static class Subject {
            public static long Identity(long value) {
                Contract.Ensures(Contract.Result<long>() == value);
                return value;
            }
        }
        """;

    [OneTimeTearDown]
    public void RemovePackageCache()
    {
        s_packageCache.Dispose();
    }

    // Each case restores and builds its own consumer. Naming the cases lets CI
    // package partitions split them instead of running all of them in one lane.
    [TestCase("SharpProof", "net461", TestName = "StrictDefaultSharpProofNet461")]
    [TestCase("SharpProof", "net462", TestName = "StrictDefaultSharpProofNet462")]
    [TestCase("SharpProof", "net472", TestName = "StrictDefaultSharpProofNet472")]
    [TestCase("SharpProof", "net48", TestName = "StrictDefaultSharpProofNet48")]
    [TestCase("SharpProof", "net481", TestName = "StrictDefaultSharpProofNet481")]
    [TestCase("SharpProof", "netstandard2.0", TestName = "StrictDefaultSharpProofNetStandard20")]
    [TestCase("SharpProof", "netcoreapp2.0", TestName = "StrictDefaultSharpProofNetCoreApp20")]
    [TestCase("SharpProof", "netcoreapp3.1", TestName = "StrictDefaultSharpProofNetCoreApp31")]
    [TestCase("SharpProof", "net5.0", TestName = "StrictDefaultSharpProofNet5")]
    [TestCase("SharpProof", "net6.0", TestName = "StrictDefaultSharpProofNet6")]
    [TestCase("SharpProof", "net7.0", TestName = "StrictDefaultSharpProofNet7")]
    [TestCase("SharpProof", "net9.0", TestName = "StrictDefaultSharpProofNet9")]
    [TestCase("SharpProof", "net8.0", TestName = "StrictDefaultSharpProofNet8")]
    [TestCase("SharpProof", "net10.0", TestName = "StrictDefaultSharpProofNet10")]
    [TestCase("SharpProof.Verifier", "net8.0", TestName = "StrictDefaultVerifierNet8")]
    [TestCase("SharpProof.Verifier", "net10.0", TestName = "StrictDefaultVerifierNet10")]
    public async Task OnePackageReferenceRunsStrictVerificationWithoutSharpProofProperties(string packageId, string framework)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(framework);
        using var directory = new TempDirectory("sharpproof-verifier-defaults-");
        await WriteConsumerAsync(directory.FullName, framework, profile: null, Source, packageId);
        await RestoreAsync(directory.FullName);
        await AssertPropertiesAsync(directory.FullName, "strict", verify: true, packageId);
        var build = await RunDotNetAsync(directory.FullName, ["build", "Consumer.csproj", "-c", "Release", "--no-restore", "--nologo"]);
        Assert.That(build.ExitCode, Is.Zero, build.CombinedOutput);
        var resultPath = Path.Combine(directory.FullName, "obj", "Release", framework, "SharpProof", "result.json");
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
        Assert.That(result.RootElement.GetProperty("runStatus").GetString(), Is.EqualTo("Complete"));
        Assert.That(result.RootElement.GetProperty("claimResults").GetArrayLength(), Is.EqualTo(1));
        Assert.That(result.RootElement.GetProperty("claimResults")[0].GetProperty("outcome").GetString(), Is.EqualTo("Proven"));
        Assert.That(result.RootElement.GetProperty("claimResults")[0].GetProperty("vacuity").GetString(), Is.EqualTo("None"));
    }

    [TestCase("SharpProof", "advisory", TestName = "ExplicitProfileSharpProofAdvisory")]
    [TestCase("SharpProof", "off", TestName = "ExplicitProfileSharpProofOff")]
    [TestCase("SharpProof.Verifier", "advisory", TestName = "ExplicitProfileVerifierAdvisory")]
    [TestCase("SharpProof.Verifier", "off", TestName = "ExplicitProfileVerifierOff")]
    public async Task ExplicitProfileOverridesCompletePackageDefaults(string packageId, string profile)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(profile);
        using var directory = new TempDirectory("sharpproof-verifier-profile-");
        await WriteConsumerAsync(directory.FullName, "net10.0", profile, Source, packageId);
        await RestoreAsync(directory.FullName);
        await AssertPropertiesAsync(directory.FullName, profile, verify: false, packageId);
        var build = await RunDotNetAsync(directory.FullName, ["build", "Consumer.csproj", "-c", "Release", "--no-restore", "--nologo"]);
        Assert.That(build.ExitCode, Is.Zero, build.CombinedOutput);
        Assert.That(File.Exists(Path.Combine(directory.FullName, "obj", "Release", "net10.0", "SharpProof", "result.json")), Is.False);
    }

    [TestCase("SharpProof", "net461", TestName = "RefutedDefaultSharpProofNet461")]
    [TestCase("SharpProof", "net472", TestName = "RefutedDefaultSharpProofNet472")]
    [TestCase("SharpProof", "net481", TestName = "RefutedDefaultSharpProofNet481")]
    [TestCase("SharpProof", "netstandard2.0", TestName = "RefutedDefaultSharpProofNetStandard20")]
    [TestCase("SharpProof", "netcoreapp2.0", TestName = "RefutedDefaultSharpProofNetCoreApp20")]
    [TestCase("SharpProof", "net10.0", TestName = "RefutedDefaultSharpProofNet10")]
    [TestCase("SharpProof.Verifier", "net10.0", TestName = "RefutedDefaultVerifierNet10")]
    public async Task OnePackageReferenceFailsTheBuildForARefutedClaimByDefault(string packageId, string framework)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(framework);
        using var directory = new TempDirectory("sharpproof-verifier-refuted-");
        await WriteConsumerAsync(directory.FullName, framework, profile: null,
            Source.Replace("return value;", "return 0;", StringComparison.Ordinal), packageId);
        await RestoreAsync(directory.FullName);
        var build = await RunDotNetAsync(directory.FullName, ["build", "Consumer.csproj", "-c", "Release", "--no-restore", "--nologo"]);
        Assert.That(build.ExitCode, Is.Not.Zero, build.CombinedOutput);
        Assert.That(File.Exists(Path.Combine(directory.FullName, "obj", "Release", framework, "SharpProof", "result.json")),
            Is.True, build.CombinedOutput);
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(directory.FullName, "obj", "Release", framework, "SharpProof", "result.json")));
        Assert.That(result.RootElement.GetProperty("claimResults")[0].GetProperty("outcome").GetString(), Is.EqualTo("Refuted"));
    }

    [TestCase("SharpProof")]
    [TestCase("SharpProof.Verifier")]
    public async Task ExplicitSwitchAndPoliciesAreRetainedAndConflictingStrictOptOutFailsClosed(string packageId)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        using var directory = new TempDirectory("sharpproof-verifier-explicit-");
        await WriteConsumerAsync(directory.FullName, "net10.0", profile: null, Source, packageId);
        var path = Path.Combine(directory.FullName, "Consumer.csproj");
        var project = XDocument.Load(path);
        project.Root!.Element("PropertyGroup")!.Add(
            new XElement("SharpProofVerify", "false"),
            new XElement("SharpProofVerifyPolicy", "advisory"),
            new XElement("SharpProofAssumptionPolicy", "allow"));
        await File.WriteAllTextAsync(path, project.ToString(), new UTF8Encoding(false));
        await RestoreAsync(directory.FullName);
        var evaluation = await RunDotNetAsync(directory.FullName, ["msbuild", "Consumer.csproj", "--nologo", "-getProperty:SharpProofProfile,SharpProofVerify,SharpProofVerifyPolicy,SharpProofAssumptionPolicy"]);
        Assert.That(evaluation.ExitCode, Is.Zero, evaluation.CombinedOutput);
        using var document = JsonDocument.Parse(evaluation.Output);
        var properties = document.RootElement.GetProperty("Properties");
        Assert.That(properties.GetProperty("SharpProofProfile").GetString(), Is.EqualTo("strict"));
        Assert.That(properties.GetProperty("SharpProofVerify").GetString(), Is.EqualTo("false"));
        Assert.That(properties.GetProperty("SharpProofVerifyPolicy").GetString(), Is.EqualTo("advisory"));
        Assert.That(properties.GetProperty("SharpProofAssumptionPolicy").GetString(), Is.EqualTo("allow"));
        var build = await RunDotNetAsync(directory.FullName, ["build", "Consumer.csproj", "-c", "Release", "--no-restore", "--nologo"]);
        Assert.That(build.ExitCode, Is.Not.Zero, build.CombinedOutput);
        Assert.That(build.CombinedOutput, Does.Contain("SharpProofProfile=strict requires SharpProofVerify=true"));
        Assert.That(File.Exists(Path.Combine(directory.FullName, "obj", "Release", "net10.0", "SharpProof", "result.json")), Is.False);
    }

    // Projects that reach SharpProof only through a library did not ask for
    // verification, so the package's build files stay with the direct consumer.
    [Test]
    public async Task ProjectReferencingAVerifiedLibraryDoesNotInheritVerification()
    {
        using var directory = new TempDirectory("sharpproof-verifier-transitive-");
        var library = Directory.CreateDirectory(Path.Combine(directory.FullName, "Library")).FullName;
        var application = Directory.CreateDirectory(Path.Combine(directory.FullName, "Application")).FullName;
        await WriteConsumerAsync(library, "net10.0", profile: null, Source, "SharpProof");
        IsolatedPackageFeedConfiguration.Write(directory.FullName, (await PackagedProductFeed.GetAsync()).Source);
        await WriteApplicationAsync(application, new XElement("ProjectReference", new XAttribute("Include", "../Library/Consumer.csproj")));
        var restore = await RunDotNetAsync(application, ["restore", "Application.csproj", "--packages", s_packageCache.FullName, "--nologo"]);
        Assert.That(restore.ExitCode, Is.Zero, restore.CombinedOutput);
        await AssertPropertiesAsync(library, "strict", verify: true, "SharpProof");
        await AssertNoSharpProofBuildAsync(application);
        var libraryResult = Path.Combine(library, "obj", "Release", "net10.0", "SharpProof", "result.json");
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(libraryResult));
        Assert.That(result.RootElement.GetProperty("claimResults")[0].GetProperty("outcome").GetString(), Is.EqualTo("Proven"));
    }

    [Test]
    public async Task PackageDependingOnSharpProofDoesNotPassOnVerification()
    {
        using var directory = new TempDirectory("sharpproof-verifier-packed-library-");
        var library = Directory.CreateDirectory(Path.Combine(directory.FullName, "Library")).FullName;
        var application = Directory.CreateDirectory(Path.Combine(directory.FullName, "Application")).FullName;
        var libraryFeed = Directory.CreateDirectory(Path.Combine(directory.FullName, "feed")).FullName;
        await WriteConsumerAsync(library, "net10.0", profile: null, Source, "SharpProof");
        var libraryProject = Path.Combine(library, "Consumer.csproj");
        var project = XDocument.Load(libraryProject);
        project.Root!.Element("PropertyGroup")!.Add(new XElement("PackageId", "TransitiveLibrary"), new XElement("Version", "1.0.0"));
        project.Save(libraryProject);
        await RestoreAsync(library);
        var pack = await RunDotNetAsync(library, ["pack", "Consumer.csproj", "-c", "Release", "--no-restore", "--nologo", "-o", libraryFeed]);
        Assert.That(pack.ExitCode, Is.Zero, pack.CombinedOutput);
        await WriteLibraryFeedConfigurationAsync(application, (await PackagedProductFeed.GetAsync()).Source, libraryFeed);
        await WriteApplicationAsync(application, new XElement("PackageReference", new XAttribute("Include", "TransitiveLibrary"), new XAttribute("Version", "1.0.0")));
        var restore = await RunDotNetAsync(application, ["restore", "Application.csproj", "--packages", s_packageCache.FullName, "--nologo"]);
        Assert.That(restore.ExitCode, Is.Zero, restore.CombinedOutput);
        // The contract runtime still flows; only the build integration stops.
        var assets = await File.ReadAllTextAsync(Path.Combine(application, "obj", "project.assets.json"));
        Assert.That(assets, Does.Contain("\"SharpProof.Attributes/"));
        await AssertNoSharpProofBuildAsync(application);
    }

    private static async Task WriteApplicationAsync(string root, XElement reference)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(reference);
        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("NuGetAudit", "false")),
            new XElement("ItemGroup", reference));
        await File.WriteAllTextAsync(Path.Combine(root, "Application.csproj"), project.ToString(), new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(root, "Program.cs"), "public static class Program { public static int Main() => Subject.Identity(1) == 1 ? 0 : 1; }\n", new UTF8Encoding(false));
    }

    private static async Task WriteLibraryFeedConfigurationAsync(string root, string productSource, string librarySource)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(productSource);
        ArgumentNullException.ThrowIfNull(librarySource);
        var product = PackageTestXml.EscapeOrThrow(Path.GetFullPath(productSource), "Failed to escape the product source.");
        var library = PackageTestXml.EscapeOrThrow(Path.GetFullPath(librarySource), "Failed to escape the library source.");
        await File.WriteAllTextAsync(Path.Combine(root, "NuGet.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="SharpProofLocal" value="{product}" />
                <add key="LibraryLocal" value="{library}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="SharpProofLocal"><package pattern="SharpProof*" /></packageSource>
                <packageSource key="LibraryLocal"><package pattern="TransitiveLibrary" /></packageSource>
                <packageSource key="nuget.org">
                  <package pattern="Microsoft.*" />
                  <package pattern="NETStandard.*" />
                  <package pattern="runtime.*" />
                  <package pattern="System.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """, new UTF8Encoding(false));
    }

    private static async Task AssertNoSharpProofBuildAsync(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var evaluation = await RunDotNetAsync(root, ["msbuild", "Application.csproj", "--nologo",
            "-getProperty:SharpProofProfile,SharpProofVerify,_SharpProofVerifierPackagePresent"]);
        Assert.That(evaluation.ExitCode, Is.Zero, evaluation.CombinedOutput);
        using var document = JsonDocument.Parse(evaluation.Output);
        foreach (var property in document.RootElement.GetProperty("Properties").EnumerateObject())
        {
            Assert.That(property.Value.GetString(), Is.Empty, property.Name);
        }
        var build = await RunDotNetAsync(root, ["build", "Application.csproj", "-c", "Release", "--no-restore", "--nologo"]);
        Assert.That(build.ExitCode, Is.Zero, build.CombinedOutput);
        Assert.That(Directory.Exists(Path.Combine(root, "obj", "Release", "net10.0", "SharpProof")), Is.False, build.CombinedOutput);
    }

    private static async Task WriteConsumerAsync(string root, string framework, string? profile, string source, string packageId)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(framework);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(packageId);
        var feed = await PackagedProductFeed.GetAsync();
        IsolatedPackageFeedConfiguration.Write(root, feed.Source);
        var properties = new XElement("PropertyGroup", new XElement("TargetFramework", framework), new XElement("NuGetAudit", "false"));
        if (profile != null)
        {
            properties.Add(new XElement("SharpProofProfile", profile));
        }
        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties,
            new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", packageId),
                new XAttribute("Version", feed.Version))));
        await File.WriteAllTextAsync(Path.Combine(root, "Consumer.csproj"), project.ToString(), new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(root, "Subject.cs"), source, new UTF8Encoding(false));
    }

    private static async Task RestoreAsync(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        // The compiler server may load dependencies lazily. Keep this cache
        // alive for every build sharing its server, then dispose it with the fixture.
        var restore = await RunDotNetAsync(root, ["restore", "Consumer.csproj", "--packages", s_packageCache.FullName, "--nologo"]);
        Assert.That(restore.ExitCode, Is.Zero, restore.CombinedOutput);
    }

    private static async Task AssertPropertiesAsync(string root, string profile, bool verify, string packageId)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(packageId);
        var evaluation = await RunDotNetAsync(root, ["msbuild", "Consumer.csproj", "--nologo", "-getProperty:SharpProofProfile,SharpProofVerify,SharpProofVerifyPolicy,SharpProofAssumptionPolicy", "-getItem:PackageReference"]);
        Assert.That(evaluation.ExitCode, Is.Zero, evaluation.CombinedOutput);
        using var document = JsonDocument.Parse(evaluation.Output);
        var properties = document.RootElement.GetProperty("Properties");
        Assert.That(properties.GetProperty("SharpProofProfile").GetString(), Is.EqualTo(profile));
        Assert.That(properties.GetProperty("SharpProofVerify").GetString(), Is.EqualTo(verify ? "true" : "false"));
        if (verify)
        {
            Assert.That(properties.GetProperty("SharpProofVerifyPolicy").GetString(), Is.EqualTo("require-proven"));
            Assert.That(properties.GetProperty("SharpProofAssumptionPolicy").GetString(), Is.EqualTo("error"));
        }
        // Older framework SDKs add their own implicit framework packages. Only
        // user-authored references count toward the one-package contract.
        var references = document.RootElement.GetProperty("Items").GetProperty("PackageReference").EnumerateArray()
            .Where(static reference => !reference.TryGetProperty("IsImplicitlyDefined", out var implicitReference) ||
                !string.Equals(implicitReference.GetString(), "true", StringComparison.OrdinalIgnoreCase))
            .ToImmutableArray();
        Assert.That(references, Has.Length.EqualTo(1));
        Assert.That(references[0].GetProperty("Identity").GetString(), Is.EqualTo(packageId));
    }

    private static Task<ProcessRunnerResult> RunDotNetAsync(string root, ImmutableArray<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (arguments.IsDefault)
        { throw new ArgumentNullException(nameof(arguments)); }
        var effectiveArguments = ImmutableArray.CreateBuilder<string>();
        effectiveArguments.AddRange(arguments);
        if (arguments.FirstOrDefault() == "build")
        {
            effectiveArguments.Add("-p:ReportAnalyzer=true");
        }
        var startInfo = ProcessRunner.CreateStartInfo(root, "dotnet", effectiveArguments.ToImmutable());
        startInfo.Environment["SharedCompilationId"] = s_sharedCompilationServerId;
        return ProcessRunner.RunCapturedAsync(startInfo, TestContext.CurrentContext.CancellationToken);
    }
}
