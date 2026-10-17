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

    [TestCase("SharpProof", "net8.0")]
    [TestCase("SharpProof", "net10.0")]
    [TestCase("SharpProof.Verifier", "net8.0")]
    [TestCase("SharpProof.Verifier", "net10.0")]
    public async Task OnePackageReferenceRunsStrictVerificationWithoutSharpProofProperties(string packageId, string framework)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(framework);
        using var directory = new TempDirectory("sharpproof-verifier-defaults-");
        await WriteConsumerAsync(directory.FullName, framework, profile: null, Source, packageId);
        await RestoreAsync(directory.FullName);
        await AssertPropertiesAsync(directory.FullName, "strict", verify: true, packageId);
        var build = await RunDotNetAsync(directory.FullName, "build", "Consumer.csproj", "-c", "Release", "--no-restore", "--nologo");
        Assert.That(build.ExitCode, Is.Zero, build.CombinedOutput);
        var resultPath = Path.Combine(directory.FullName, "obj", "Release", framework, "SharpProof", "result.json");
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
        Assert.That(result.RootElement.GetProperty("runStatus").GetString(), Is.EqualTo("Complete"));
        Assert.That(result.RootElement.GetProperty("claimResults").GetArrayLength(), Is.EqualTo(1));
        Assert.That(result.RootElement.GetProperty("claimResults")[0].GetProperty("outcome").GetString(), Is.EqualTo("Proven"));
        Assert.That(result.RootElement.GetProperty("claimResults")[0].GetProperty("vacuity").GetString(), Is.EqualTo("None"));
    }

    [TestCase("SharpProof", "advisory")]
    [TestCase("SharpProof", "off")]
    [TestCase("SharpProof.Verifier", "advisory")]
    [TestCase("SharpProof.Verifier", "off")]
    public async Task ExplicitProfileOverridesCompletePackageDefaults(string packageId, string profile)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(profile);
        using var directory = new TempDirectory("sharpproof-verifier-profile-");
        await WriteConsumerAsync(directory.FullName, "net10.0", profile, Source, packageId);
        await RestoreAsync(directory.FullName);
        await AssertPropertiesAsync(directory.FullName, profile, verify: false, packageId);
        var build = await RunDotNetAsync(directory.FullName, "build", "Consumer.csproj", "-c", "Release", "--no-restore", "--nologo");
        Assert.That(build.ExitCode, Is.Zero, build.CombinedOutput);
        Assert.That(File.Exists(Path.Combine(directory.FullName, "obj", "Release", "net10.0", "SharpProof", "result.json")), Is.False);
    }

    [TestCase("SharpProof")]
    [TestCase("SharpProof.Verifier")]
    public async Task OnePackageReferenceFailsTheBuildForARefutedClaimByDefault(string packageId)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        using var directory = new TempDirectory("sharpproof-verifier-refuted-");
        await WriteConsumerAsync(directory.FullName, "net10.0", profile: null,
            Source.Replace("return value;", "return 0;", StringComparison.Ordinal), packageId);
        await RestoreAsync(directory.FullName);
        var build = await RunDotNetAsync(directory.FullName, "build", "Consumer.csproj", "-c", "Release", "--no-restore", "--nologo");
        Assert.That(build.ExitCode, Is.Not.Zero, build.CombinedOutput);
        Assert.That(File.Exists(Path.Combine(directory.FullName, "obj", "Release", "net10.0", "SharpProof", "result.json")),
            Is.True, build.CombinedOutput);
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(directory.FullName, "obj", "Release", "net10.0", "SharpProof", "result.json")));
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
        var evaluation = await RunDotNetAsync(directory.FullName, "msbuild", "Consumer.csproj", "--nologo",
            "-getProperty:SharpProofProfile,SharpProofVerify,SharpProofVerifyPolicy,SharpProofAssumptionPolicy");
        Assert.That(evaluation.ExitCode, Is.Zero, evaluation.CombinedOutput);
        using var document = JsonDocument.Parse(evaluation.Output);
        var properties = document.RootElement.GetProperty("Properties");
        Assert.That(properties.GetProperty("SharpProofProfile").GetString(), Is.EqualTo("strict"));
        Assert.That(properties.GetProperty("SharpProofVerify").GetString(), Is.EqualTo("false"));
        Assert.That(properties.GetProperty("SharpProofVerifyPolicy").GetString(), Is.EqualTo("advisory"));
        Assert.That(properties.GetProperty("SharpProofAssumptionPolicy").GetString(), Is.EqualTo("allow"));
        var build = await RunDotNetAsync(directory.FullName, "build", "Consumer.csproj", "-c", "Release", "--no-restore", "--nologo");
        Assert.That(build.ExitCode, Is.Not.Zero, build.CombinedOutput);
        Assert.That(build.CombinedOutput, Does.Contain("SharpProofProfile=strict requires SharpProofVerify=true"));
        Assert.That(File.Exists(Path.Combine(directory.FullName, "obj", "Release", "net10.0", "SharpProof", "result.json")), Is.False);
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
        var restore = await RunDotNetAsync(root, "restore", "Consumer.csproj", "--packages", s_packageCache.FullName, "--nologo");
        Assert.That(restore.ExitCode, Is.Zero, restore.CombinedOutput);
    }

    private static async Task AssertPropertiesAsync(string root, string profile, bool verify, string packageId)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(packageId);
        var evaluation = await RunDotNetAsync(root, "msbuild", "Consumer.csproj", "--nologo",
            "-getProperty:SharpProofProfile,SharpProofVerify,SharpProofVerifyPolicy,SharpProofAssumptionPolicy", "-getItem:PackageReference");
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
        var references = document.RootElement.GetProperty("Items").GetProperty("PackageReference");
        Assert.That(references.GetArrayLength(), Is.EqualTo(1));
        Assert.That(references[0].GetProperty("Identity").GetString(), Is.EqualTo(packageId));
    }

    private static Task<ProcessRunnerResult> RunDotNetAsync(string root, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(arguments);
        var effectiveArguments = arguments.ToList();
        if (arguments.FirstOrDefault() == "build")
        {
            effectiveArguments.Add("-p:ReportAnalyzer=true");
        }
        var startInfo = ProcessRunner.CreateStartInfo(root, "dotnet", effectiveArguments);
        startInfo.Environment["SharedCompilationId"] = s_sharedCompilationServerId;
        return ProcessRunner.RunCapturedAsync(startInfo, TestContext.CurrentContext.CancellationToken);
    }
}
