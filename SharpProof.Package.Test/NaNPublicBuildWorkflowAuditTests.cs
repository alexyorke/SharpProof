using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class NaNPublicBuildWorkflowAuditTests
{
    private const string SourceSha256 = "b7f9af5db0507cc2159dd55108a4cf9edf3906c3111cf2d9e869580ec3898118";
    private const string Source = """
        #undef SHARPPROOF_CONTRACTS
        using SharpProof.Attributes;
        public static class Subject {
            public static bool Target(double value) {
                Contract.Ensures(Contract.Result<bool>());
                var copy = value;
                return value == copy;
            }
        }
        """ + "\n";

    [Test]
    public async Task StrictPublicPackageBuildRejectsOpaqueDoubleEqualityProof()
    {
        TestRepository.RequireCanonicalContainer();
        _ = ContainerContract.ValidateRequired();
        var repository = TestRepository.FindRoot();
        var evidence = Path.Combine(repository, "artifacts", "correctness",
            "nan-cli-workflow-audit", "public-consumer-" + Guid.NewGuid().ToString("N"));
        Assert.That(Directory.Exists(evidence), Is.False,
            "A public workflow observation must not replace earlier evidence.");
        Directory.CreateDirectory(evidence);
        var sourceBytes = Encoding.UTF8.GetBytes(Source);
        Assert.That(WorkerProtocolJson.ComputeSha256(sourceBytes), Is.EqualTo(SourceSha256));
        await File.WriteAllBytesAsync(Path.Combine(evidence, "Subject.cs"), sourceBytes);

        var feed = await PackagedProductFeed.GetAsync();
        using var temporary = TempDirectory.CreateOwned("SharpProof.NaNPublicBuildWorkflow", "consumer-");
        var consumer = temporary.FullName;
        var projectPath = Path.Combine(consumer, "Consumer.csproj");
        var packageCache = Path.Combine(consumer, "packages");
        var version = PackageTestXml.EscapeOrThrow(feed.Version, "Package version cannot be escaped.");
        var project = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <LangVersion>12.0</LangVersion>
                <ImplicitUsings>disable</ImplicitUsings>
                <NuGetAudit>false</NuGetAudit>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="SharpProof.Attributes" Version="{version}" />
                <PackageReference Include="SharpProof" Version="{version}" PrivateAssets="all" />
                <PackageReference Include="SharpProof.Verifier" Version="{version}" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """ + "\n";
        await File.WriteAllTextAsync(projectPath, project, new UTF8Encoding(false));
        await File.WriteAllBytesAsync(Path.Combine(consumer, "Subject.cs"), sourceBytes);
        await File.WriteAllBytesAsync(Path.Combine(consumer, "global.json"),
            await File.ReadAllBytesAsync(Path.Combine(repository, "global.json")));
        var nugetConfig = IsolatedPackageFeedConfiguration.Write(consumer, feed.Source);
        foreach (var name in new[] { "Consumer.csproj", "global.json", "NuGet.Config" })
        {
            await SaveFileAsync(Path.Combine(consumer, name), Path.Combine(evidence, name));
        }
        string[] restoreArguments = [
            "restore", projectPath, "--nologo", "/m:1", "/nodeReuse:false",
            "--configfile", nugetConfig, "--packages", packageCache, "-p:NuGetAudit=false"
        ];
        string[] buildArguments = [
            "build", projectPath, "-c", "Release", "--no-restore", "--nologo",
            "--verbosity", "minimal", "/m:1", "/nodeReuse:false",
            "-p:SharpProofProfile=strict", "-p:SharpProofFeatures=contracts",
            "-p:SharpProofVerify=true", "-p:SharpProofVerifyPolicy=require-proven",
            "-p:SharpProofAssumptionPolicy=error", "-p:SharpProofVerifyCacheEnabled=false"
        ];
        await File.WriteAllTextAsync(Path.Combine(evidence, "commands.json"), JsonSerializer.Serialize(
            new { WorkingDirectory = consumer, FileName = "dotnet", Restore = restoreArguments, Build = buildArguments }));
        var restore = await ProcessRunner.RunCapturedAsync(consumer, "dotnet", restoreArguments);
        await File.WriteAllTextAsync(Path.Combine(evidence, "restore.log"), restore.CombinedOutput);
        Assert.That(restore.ExitCode, Is.Zero, "Consumer restore setup failed.\n" + restore.CombinedOutput);
        var build = await ProcessRunner.RunCapturedAsync(consumer, "dotnet", buildArguments);
        await File.WriteAllTextAsync(Path.Combine(evidence, "build.log"), build.CombinedOutput);
        await TestContext.Out.WriteLineAsync("PUBLIC CONSUMER BUILD\n" + build.CombinedOutput);

        var publication = Path.Combine(consumer, "obj", "Release", "net8.0", "SharpProof");
        foreach (var name in new[] { "request.json", "result.json", "compiler-manifest.json" })
        {
            Assert.That(File.Exists(Path.Combine(publication, name)), Is.True,
                "Missing public publication artifact " + name + ".\n" + build.CombinedOutput);
            await SaveFileAsync(Path.Combine(publication, name), Path.Combine(evidence, name));
        }
        var assemblyPath = Path.Combine(consumer, "obj", "Release", "net8.0", "Consumer.dll");
        Assert.That(File.Exists(assemblyPath), Is.True, "The ordinary build must compile the exact consumer.");
        await SaveFileAsync(assemblyPath, Path.Combine(evidence, "Consumer.dll"));
        Assert.That(WorkerProtocolJson.ComputeSha256(
            await File.ReadAllBytesAsync(Path.Combine(consumer, "Subject.cs"))), Is.EqualTo(SourceSha256));
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(publication, "compiler-manifest.json"));
        using var manifestDocument = JsonDocument.Parse(manifestBytes);
        var manifestRoot = manifestDocument.RootElement;
        var manifest = manifestRoot.GetProperty("manifest")
            .Deserialize<WorkerClaimManifest>(WorkerProtocolJson.Options)!;
        var request = WorkerProtocolJson.DeserializeRequest(
            await File.ReadAllTextAsync(Path.Combine(publication, "request.json")))!;
        var response = WorkerProtocolJson.DeserializeResponse(
            await File.ReadAllTextAsync(Path.Combine(publication, "result.json")))!;
        var packageRoot = Path.Combine(packageCache, "sharpproof.verifier", feed.Version);
        var packageWorker = Path.Combine(packageRoot, "tools", "net9", "SharpProof.Worker.dll");
        var packageNative = Path.Combine(packageRoot, "tools", "native", "linux-x64", "libz3.so");
        var nativeSha256 = WorkerProtocolJson.ComputeSha256(await File.ReadAllBytesAsync(packageNative));
        var workerSha256 = WorkerBinaryIdentity.ComputeSha256(packageWorker, nativeSha256);
        var identity = WorkerCacheIdentity.Current;
        var expectedInputHash = CompilerArtifactInputHash.Compute(
            request, ArtifactDigest.Compute(manifestBytes), identity.ToolIdentity,
            identity.ToolVersion, workerSha256, identity.ApiSpecIdentity,
            identity.ApiSpecVersion, identity.ApiSpecContentSha256);
        var validation = WorkerProtocolJson.ValidateForRequest(
            response, WorkerProtocolJson.ComputeRequestHash(request), expectedInputHash,
            manifest, request, response.Summary.Versions);
        Assert.That(validation.IsValid, Is.True,
            string.Join("\n", validation.Errors.Select(static error => error.Code + ": " + error.Message)));
        Assert.That(manifest.Callables, Has.Length.EqualTo(1));
        Assert.That(manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
        Assert.That(response.CallableResults, Has.Length.EqualTo(1));
        var callable = manifest.Callables.Single();
        var claim = manifest.Claims.Single();
        var result = response.ClaimResults.Single();
        await File.WriteAllTextAsync(Path.Combine(evidence, "observation.json"), JsonSerializer.Serialize(new
        {
            BuildExitCode = build.ExitCode,
            SourceSha256,
            AssemblySha256 = WorkerProtocolJson.ComputeSha256(await File.ReadAllBytesAsync(assemblyPath)),
            CompilerManifestSha256 = WorkerProtocolJson.ComputeSha256(manifestBytes),
            WorkerSha256 = workerSha256,
            response.RequestHash,
            response.InputHash,
            response.RunStatus,
            response.FailureReason,
            Claim = result,
            Callable = response.CallableResults.Single()
        }, WorkerProtocolJson.Options));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.CompilerManifest.Sha256, Is.EqualTo(WorkerProtocolJson.ComputeSha256(manifestBytes)));
            Assert.That(request.VerifyPolicy, Is.EqualTo(WorkerVerifyPolicy.RequireProven));
            Assert.That(request.AssumptionPolicy, Is.EqualTo(WorkerAssumptionPolicy.Error));
            Assert.That(request.Cache.Enabled, Is.False);
            Assert.That(manifestRoot.GetProperty("features").Deserialize<WorkerFeatureSet>(WorkerProtocolJson.Options),
                Is.EqualTo(WorkerFeatureSet.Contracts));
            Assert.That(manifestRoot.GetProperty("compilation").GetProperty("projectDirectory").GetString(),
                Is.EqualTo(consumer));
            Assert.That(callable.CallableId, Does.Contain("Subject.Target"));
            Assert.That(callable.Location.Path, Is.EqualTo(Path.Combine(consumer, "Subject.cs")));
            Assert.That(claim.Kind, Is.EqualTo(WorkerClaimKind.Postcondition));
            Assert.That(claim.Evidence, Is.EqualTo(WorkerClaimEvidence.DirectClause));
            Assert.That(claim.CallableId, Is.EqualTo(callable.CallableId));
            Assert.That(result.ClaimId, Is.EqualTo(claim.ClaimId));
            Assert.That(response.Summary.Versions.WorkerBinarySha256, Is.EqualTo(workerSha256));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown),
                "NaN disproves value == copy; opaque double operators must abstain.");
            Assert.That(result.Reason, Is.Not.EqualTo(WorkerClaimReason.None));
            Assert.That(response.ClaimResults.Any(static item => item.Outcome == WorkerClaimOutcome.Proven), Is.False);
            Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(build.ExitCode, Is.EqualTo(1), "Strict public build must reject an incomplete proof.");
            Assert.That(build.CombinedOutput, Does.Contain("SharpProof Unknown " + callable.CallableId));
            Assert.That(build.CombinedOutput, Does.Contain("error SP0047"));
            Assert.That(build.CombinedOutput, Does.Not.Contain("SharpProof Proven " + callable.CallableId));
            Assert.That(build.CombinedOutput, Does.Not.Contain("SharpProof verifier failed with exit code"));
        }
    }

    private static async Task SaveFileAsync(string source, string destination)
    {
        await File.WriteAllBytesAsync(destination, await File.ReadAllBytesAsync(source));
    }
}
