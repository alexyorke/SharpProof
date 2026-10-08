using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
[Platform("Linux")]
public sealed class DiagnosticTransportPathMappingControlsAuditTests
{
    private const string Root = "/tmp/sharpproof-diagnostic-transport-mapping-control-audit";
    private const string Header = "using SharpProof.Attributes;\npublic static class Subject\n{\n";
    private const string Tail =
        "    [return: Positive]\n    public static int Target(int value) => value;\n}\n";

    [TestCase("physical-directory", TestName = "PhysicalDirectoryBackslashRetainsItsLiteralCharacter")]
    [TestCase("relative-map-physical-directory", TestName = "RelativeMappedNameUsesOriginalPhysicalDirectory")]
    [TestCase("relative-map-legacy", TestName = "RelativeMappedBackslashKeepsEstablishedSeparatorConvention")]
    [TestCase("windows-absolute-map", TestName = "ForeignWindowsAbsoluteMappingKeepsPortableDriveUri")]
    [TestCase("unix-absolute-map", TestName = "OrdinaryAbsoluteUnixMappingRemainsUnchanged")]
    [TestCase("unix-absolute-literal-map", TestName = "AbsoluteUnixMappedBackslashTargetsExistingLiteralFile")]
    public async Task CompilerAndPublishedLocationsFollowPathContract(string caseName)
    {
        var input = Input(caseName);
        var source = Header + (input.MappedPath is null
            ? string.Empty
            : "#line 73 \"" + input.MappedPath + "\"\n") + Tail;
        var expectedLine = input.MappedPath is null ? 4 : 73;
        var expectedUri = new Uri(input.ExpectedUri);
        var evidenceDirectory = EvidenceDirectory(caseName);
        Directory.CreateDirectory(input.ProjectDirectory);
        await File.WriteAllTextAsync(input.SourcePath, source);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject.cs"), source);
        Assert.That(File.Exists(input.SourcePath), Is.True);
        if (input.NativeMappedTarget)
        {
            if (input.ExpectedPath != input.SourcePath)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(input.ExpectedPath)!);
                await File.WriteAllTextAsync(input.ExpectedPath, Header + new string('\n', 69) + Tail);
            }
            Assert.That(expectedUri.LocalPath, Is.EqualTo(input.ExpectedPath), "Independent literal file URI oracle.");
            Assert.That(File.Exists(expectedUri.LocalPath), Is.True, "The expected diagnostic target exists.");
            var targetLines = await File.ReadAllLinesAsync(expectedUri.LocalPath);
            Assert.That(targetLines[expectedLine - 1], Is.EqualTo("    [return: Positive]"));
        }

        var compilation = TestCompilation.Create("DiagnosticTransportPathMappingAudit",
            OutputKind.DynamicallyLinkedLibrary, [(input.SourcePath, source)]);
        var tree = compilation.SyntaxTrees.Single();
        var attribute = (await tree.GetRootAsync()).DescendantNodes().OfType<AttributeSyntax>().Single();
        var roslynLocation = attribute.GetLocation().GetMappedLineSpan();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tree.FilePath, Is.EqualTo(input.SourcePath));
            Assert.That(roslynLocation.Path, Is.EqualTo(input.MappedPath ?? input.SourcePath));
            Assert.That(roslynLocation.HasMappedPath, Is.EqualTo(input.MappedPath is not null));
            Assert.That(roslynLocation.StartLinePosition.Line + 1, Is.EqualTo(expectedLine));
            Assert.That(roslynLocation.StartLinePosition.Character + 1, Is.EqualTo(14));
        }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True,
            string.Join(Environment.NewLine, emitted.Diagnostics.Select(static item => item.ToString())));
        RuntimeAssemblyTestHost.WithRuntimeAssembly("DiagnosticTransportPathMappingAudit", image, assembly =>
        {
            var target = assembly.GetType("Subject")!.GetMethod("Target")!.CreateDelegate<Func<int, int>>();
            Assert.That(target(-1), Is.EqualTo(-1));
        });
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "subject.dll"), image.ToArray());
        var artifact = CompilerManifestArtifactProducer.Create(compilation, input.ProjectDirectory, "net9.0",
            WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Callables, Has.Length.EqualTo(1));
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(artifact.Callables.Single().Total, Is.Not.Null);
        var artifactBytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        var artifactPath = Path.Combine(evidenceDirectory, "input-manifest.json");
        await File.WriteAllBytesAsync(artifactPath, artifactBytes);
        var requestPath = Path.Combine(evidenceDirectory, "published-request.json");
        var resultPath = Path.Combine(evidenceDirectory, "published-result.json");
        var manifestPath = Path.Combine(evidenceDirectory, "published-manifest.json");
        var sarifPath = Path.Combine(evidenceDirectory, "published.sarif");
        string[] arguments = [
            "verify", "--worker", typeof(SharpProofWorker).Assembly.Location,
            "--request", Path.Combine(evidenceDirectory, "private-request.json"),
            "--result", Path.Combine(evidenceDirectory, "private-result.json"),
            "--compiler-manifest", artifactPath,
            "--verify-policy", "advisory", "--assumption-policy", "allow", "--cache-enabled", "false",
            "--publish-request", requestPath, "--publish-result", resultPath,
            "--publish-compiler-manifest", manifestPath, "--publish-sarif", sarifPath
        ];
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "launcher-arguments.json"),
            JsonSerializer.Serialize(arguments, WorkerProtocolJson.SharedOptions));
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var previousOutput = Console.Out;
        var previousError = Console.Error;
        int exitCode;
        try
        {
            Console.SetOut(output);
            Console.SetError(errors);
            exitCode = await Program.RunMain(arguments);
        }
        finally
        {
            Console.SetOut(previousOutput);
            Console.SetError(previousError);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "launcher-stdout.txt"), output.ToString());
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "launcher-stderr.txt"), errors.ToString());
        }
        Assert.That(exitCode, Is.EqualTo(5), errors.ToString());
        var request = WorkerProtocolJson.DeserializeRequest(await File.ReadAllTextAsync(requestPath))!;
        var response = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(resultPath))!;
        var publishedBytes = await File.ReadAllBytesAsync(manifestPath);
        var publishedArtifact = CompilerManifestArtifactJson.Deserialize(Encoding.UTF8.GetString(publishedBytes))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerProtocolJson.ValidateForRequest(response,
                WorkerProtocolJson.ComputeRequestHash(request),
                Program.ComputeExpectedInputHash(request, publishedBytes), publishedArtifact.Manifest,
                request, Program.ExpectedVersions()).IsValid, Is.True);
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Disabled));
            Assert.That(JsonSerializer.Serialize(request.Budgets, WorkerProtocolJson.SharedOptions),
                Is.EqualTo(JsonSerializer.Serialize(new WorkerBudgets(), WorkerProtocolJson.SharedOptions)));
        }
        var diagnosticLines = errors.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(diagnosticLines, Has.Length.EqualTo(1));
        Assert.That(VerifierDiagnosticTransport.TryDeserialize(diagnosticLines.Single().TrimEnd('\r'),
            out var diagnostic), Is.True);
        Assert.That(diagnostic.Code, Is.EqualTo("SP0051"));
        using var sarif = JsonDocument.Parse(await File.ReadAllTextAsync(sarifPath));
        var run = sarif.RootElement.GetProperty("runs")[0];
        Assert.That(run.GetProperty("results").GetArrayLength(), Is.EqualTo(1));
        var physicalLocation = run.GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation");
        var artifactLocation = physicalLocation.GetProperty("artifactLocation");
        var uriText = artifactLocation.GetProperty("uri").GetString()!;
        var rootUri = run.GetProperty("originalUriBaseIds").GetProperty("%SRCROOT%")
            .GetProperty("uri").GetString()!;
        var actualUri = artifactLocation.TryGetProperty("uriBaseId", out _)
            ? new Uri(new Uri(rootUri), uriText)
            : new Uri(uriText);
        var region = physicalLocation.GetProperty("region");
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "observations.json"),
            JsonSerializer.Serialize(new
            {
                caseName,
                input.SourcePath,
                input.MappedPath,
                input.ExpectedPath,
                input.ExpectedUri,
                expectedLine,
                roslynPath = roslynLocation.Path,
                roslynLocation.HasMappedPath,
                capturedPath = artifact.Compilation.SyntaxTrees.Single().Path,
                projectedPath = artifact.Manifest.Claims.Single().Location.Path,
                publishedPath = response.Manifest.Claims.Single().Location.Path,
                transportedPath = diagnostic.File,
                sarifRootUri = rootUri,
                sarifUri = actualUri.AbsoluteUri,
                sarifLocalPath = actualUri.LocalPath,
                expectedNativeTargetExists = input.NativeMappedTarget && File.Exists(expectedUri.LocalPath),
                actualNativeTargetExists = input.NativeMappedTarget && File.Exists(actualUri.LocalPath),
                exitCode,
                response.RunStatus,
                response.FailureReason,
                outcome = response.ClaimResults.Single().Outcome,
                diagnosticCount = diagnosticLines.Length,
                clrResult = -1,
                budgets = request.Budgets
            }, WorkerProtocolJson.SharedOptions));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Compilation.SyntaxTrees.Single().Path, Is.EqualTo(input.SourcePath));
            Assert.That(artifact.Manifest.Claims.Single().Location.Path, Is.EqualTo(input.ExpectedPath), "Compiler projection.");
            Assert.That(response.Manifest.Claims.Single().Location.Path, Is.EqualTo(input.ExpectedPath), "Published manifest.");
            Assert.That(diagnostic.File, Is.EqualTo(input.ExpectedPath), "Transported diagnostic.");
            Assert.That(diagnostic.Line, Is.EqualTo(expectedLine));
            Assert.That(diagnostic.Column, Is.EqualTo(14));
            Assert.That(region.GetProperty("startLine").GetInt32(), Is.EqualTo(expectedLine));
            Assert.That(region.GetProperty("startColumn").GetInt32(), Is.EqualTo(14));
            Assert.That(actualUri.AbsoluteUri, Is.EqualTo(input.ExpectedUri), "Published SARIF URI.");
            Assert.That(rootUri, Is.EqualTo(input.ExpectedRootUri), "Source-root URI.");
            Assert.That(actualUri.Query, Is.Empty);
            Assert.That(actualUri.Fragment, Is.Empty);
            if (input.NativeMappedTarget)
            {
                Assert.That(actualUri.LocalPath, Is.EqualTo(input.ExpectedPath), "Literal URI round trip.");
                Assert.That(File.Exists(actualUri.LocalPath), Is.True, "Published URI targets the actual existing file.");
            }
        }
    }

    [Test]
    public async Task LegacyRelativeSarifFallbackKeepsEstablishedSeparatorAndEscapingConvention()
    {
        const string mappedPath = "generated/mapped#source?\\Identity %.cs";
        var location = new WorkerSourceLocation { Path = mappedPath, Start = 0, Length = 1, Line = 73, Column = 14 };
        var manifest = new WorkerClaimManifest
        {
            Callables = [new WorkerCallableManifestEntry { CallableId = "legacy-relative", Location = location, ClaimIds = ["claim"] }],
            Claims = [new WorkerClaimManifestEntry { ClaimId = "claim", CallableId = "legacy-relative",
                Kind = WorkerClaimKind.Postcondition, Evidence = WorkerClaimEvidence.DirectClause, Location = location }]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            ClaimResults = [new WorkerClaimResult { ClaimId = "claim", Outcome = WorkerClaimOutcome.Refuted, Reason = WorkerClaimReason.None }],
            Summary = new WorkerVerificationSummary { Versions = new WorkerVersionSummary { WorkerVersion = "1.0.0-test" } }
        };
        var json = SarifProjection.Serialize(new WorkerVerifyRequest(), response, Root + "/relative-fallback");
        var evidenceDirectory = EvidenceDirectory("relative-fallback");
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "published.sarif"), json);
        using var document = JsonDocument.Parse(json);
        var run = document.RootElement.GetProperty("runs")[0];
        var artifactLocation = run.GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation").GetProperty("artifactLocation");
        var rootUri = run.GetProperty("originalUriBaseIds").GetProperty("%SRCROOT%").GetProperty("uri").GetString()!;
        var relativeUri = artifactLocation.GetProperty("uri").GetString()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(relativeUri, Is.EqualTo("generated/mapped%23source%3F/Identity%20%25.cs"));
            Assert.That(artifactLocation.GetProperty("uriBaseId").GetString(), Is.EqualTo("%SRCROOT%"));
            Assert.That(new Uri(new Uri(rootUri), relativeUri).LocalPath,
                Is.EqualTo(Root + "/relative-fallback/generated/mapped#source?/Identity %.cs"));
        }
    }

    private static string EvidenceDirectory(string caseName)
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var directory = Path.Combine(repositoryRoot!, "artifacts", "correctness",
            "diagnostic-transport-bcdf-qualification", "controls", caseName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static MappingInput Input(string caseName)
    {
        return caseName switch
        {
            "physical-directory" => new(Root + "/physical\\directory", Root + "/physical\\directory/Subject.cs", null,
                Root + "/physical\\directory/Subject.cs", "file://" + Root + "/physical%5Cdirectory/Subject.cs",
                "file://" + Root + "/physical%5Cdirectory/", true),
            "relative-map-physical-directory" => new(Root + "/relative\\directory", Root + "/relative\\directory/Subject.cs",
                "generated/mapped\\Identity.cs", Root + "/relative\\directory/generated/mapped/Identity.cs",
                "file://" + Root + "/relative%5Cdirectory/generated/mapped/Identity.cs",
                "file://" + Root + "/relative%5Cdirectory/", true),
            "relative-map-legacy" => new(Root + "/relative-legacy", Root + "/relative-legacy/Subject.cs",
                "generated/mapped\\Identity.cs", Root + "/relative-legacy/generated/mapped/Identity.cs",
                "file://" + Root + "/relative-legacy/generated/mapped/Identity.cs",
                "file://" + Root + "/relative-legacy/", true),
            "windows-absolute-map" => new(Root + "/windows-absolute", Root + "/windows-absolute/Subject.cs",
                "C:\\source\\MappedIdentity.cs", "C:/source/MappedIdentity.cs", "file:///C:/source/MappedIdentity.cs",
                "file://" + Root + "/windows-absolute/", false),
            "unix-absolute-map" => new(Root + "/unix-absolute", Root + "/unix-absolute/Subject.cs",
                Root + "/mapped/Identity.cs", Root + "/mapped/Identity.cs", "file://" + Root + "/mapped/Identity.cs",
                "file://" + Root + "/unix-absolute/", true),
            "unix-absolute-literal-map" => new(Root + "/unix-absolute-literal", Root + "/unix-absolute-literal/Subject.cs",
                Root + "/mapped/literal\\Identity.cs", Root + "/mapped/literal\\Identity.cs",
                "file://" + Root + "/mapped/literal%5CIdentity.cs", "file://" + Root + "/unix-absolute-literal/", true),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName))
        };
    }

    private sealed record MappingInput(string ProjectDirectory, string SourcePath, string? MappedPath,
        string ExpectedPath, string ExpectedUri, string ExpectedRootUri, bool NativeMappedTarget);
}
