using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
[Platform("Linux")]
public sealed class DiagnosticTransportLinuxPathWorkflowAuditTests
{
    private const string ProjectDirectory =
        "/tmp/sharpproof-diagnostic-transport-linux-path-audit";
    private const string Source =
        "using SharpProof.Attributes;\n" +
        "public static class Subject\n" +
        "{\n" +
        "    [return: Positive]\n" +
        "    public static int Target(int value) => value;\n" +
        "}\n";

    [TestCase(false, TestName = "OrdinaryPhysicalFilenamePreservesPublishedLocations")]
    [TestCase(true, TestName = "LiteralBackslashPhysicalFilenamePreservesPublishedLocations")]
    public async Task PhysicalFilenamePreservesPublishedLocations(bool literalBackslash)
    {
        var caseName = literalBackslash ? "literal-backslash" : "ordinary";
        var sourcePath = ProjectDirectory +
            (literalBackslash ? "/literal\\Subject.cs" : "/Subject.cs");
        var expectedUri = new Uri(
            "file:///tmp/sharpproof-diagnostic-transport-linux-path-audit/" +
            (literalBackslash ? "literal%5CSubject.cs" : "Subject.cs"));
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness",
            "diagnostic-transport-audit", "workflow", caseName);
        Directory.CreateDirectory(evidenceDirectory);
        Directory.CreateDirectory(ProjectDirectory);
        await File.WriteAllTextAsync(sourcePath, Source);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject.cs"), Source);
        Assert.That(File.Exists(sourcePath), Is.True, "The original physical source must exist.");
        Assert.That(expectedUri.LocalPath, Is.EqualTo(sourcePath), "Independent encoded file URI oracle.");
        Assert.That(File.Exists(expectedUri.LocalPath), Is.True);

        var compilation = TestCompilation.Create("DiagnosticTransportLinuxPathAudit",
            OutputKind.DynamicallyLinkedLibrary, [(sourcePath, Source)]);
        var tree = compilation.SyntaxTrees.Single();
        var attribute = (await tree.GetRootAsync()).DescendantNodes().OfType<AttributeSyntax>().Single();
        var compilerLocation = attribute.GetLocation().GetMappedLineSpan();
        Assert.That(tree.FilePath, Is.EqualTo(sourcePath));
        Assert.That(compilerLocation.Path, Is.EqualTo(sourcePath));
        Assert.That(compilerLocation.StartLinePosition.Line + 1, Is.EqualTo(4));
        Assert.That(compilerLocation.StartLinePosition.Character + 1, Is.EqualTo(14));

        using var assemblyImage = new MemoryStream();
        var emitted = compilation.Emit(assemblyImage);
        Assert.That(emitted.Success, Is.True,
            string.Join(Environment.NewLine, emitted.Diagnostics.Select(static item => item.ToString())));
        RuntimeAssemblyTestHost.WithRuntimeAssembly("DiagnosticTransportLinuxPathAudit", assemblyImage,
            assembly =>
            {
                var target = assembly.GetType("Subject")!.GetMethod("Target")!
                    .CreateDelegate<Func<int, int>>();
                Assert.That(target(-1), Is.EqualTo(-1), "CLR contradicts the positive return contract.");
            });
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "subject.dll"), assemblyImage.ToArray());

        var artifact = CompilerManifestArtifactProducer.Create(compilation, ProjectDirectory, "net9.0",
            WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Callables, Has.Length.EqualTo(1));
        Assert.That(artifact.Manifest.Callables.Single().CallableId,
            Is.EqualTo("M:Subject.Target(System.Int32)~System.Int32"));
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(artifact.Callables.Single().Total, Is.Not.Null);
        var artifactBytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        var artifactPath = Path.Combine(evidenceDirectory, "input-manifest.json");
        await File.WriteAllBytesAsync(artifactPath, artifactBytes);
        var publishedRequestPath = Path.Combine(evidenceDirectory, "published-request.json");
        var publishedResultPath = Path.Combine(evidenceDirectory, "published-result.json");
        var publishedManifestPath = Path.Combine(evidenceDirectory, "published-manifest.json");
        var sarifPath = Path.Combine(evidenceDirectory, "published.sarif");
        string[] arguments = [
            "verify", "--worker", typeof(SharpProofWorker).Assembly.Location,
            "--request", Path.Combine(evidenceDirectory, "private-request.json"),
            "--result", Path.Combine(evidenceDirectory, "private-result.json"),
            "--compiler-manifest", artifactPath,
            "--verify-policy", "advisory", "--assumption-policy", "allow", "--cache-enabled", "false",
            "--publish-request", publishedRequestPath, "--publish-result", publishedResultPath,
            "--publish-compiler-manifest", publishedManifestPath, "--publish-sarif", sarifPath
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
        var request = WorkerProtocolJson.DeserializeRequest(await File.ReadAllTextAsync(publishedRequestPath))!;
        var response = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(publishedResultPath))!;
        var publishedBytes = await File.ReadAllBytesAsync(publishedManifestPath);
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
                Is.EqualTo(JsonSerializer.Serialize(new WorkerBudgets(), WorkerProtocolJson.SharedOptions)),
                "No proof budget overrides are used.");
        }

        var diagnosticLines = errors.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(diagnosticLines, Has.Length.EqualTo(1), errors.ToString());
        Assert.That(VerifierDiagnosticTransport.TryDeserialize(diagnosticLines.Single().TrimEnd('\r'),
            out var diagnostic), Is.True);
        Assert.That(diagnostic.Code, Is.EqualTo("SP0051"));
        using var sarif = JsonDocument.Parse(await File.ReadAllTextAsync(sarifPath));
        var run = sarif.RootElement.GetProperty("runs")[0];
        var results = run.GetProperty("results");
        Assert.That(results.GetArrayLength(), Is.EqualTo(1));
        var physicalLocation = results[0].GetProperty("locations")[0].GetProperty("physicalLocation");
        var artifactLocation = physicalLocation.GetProperty("artifactLocation");
        var uriText = artifactLocation.GetProperty("uri").GetString()!;
        var actualUri = artifactLocation.TryGetProperty("uriBaseId", out var uriBaseId)
            ? new Uri(new Uri(run.GetProperty("originalUriBaseIds").GetProperty(uriBaseId.GetString()!)
                .GetProperty("uri").GetString()!), uriText)
            : new Uri(uriText);
        var region = physicalLocation.GetProperty("region");
        var producerClaim = artifact.Manifest.Claims.Single();
        var publishedClaim = response.Manifest.Claims.Single();
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "observations.json"),
            JsonSerializer.Serialize(new
            {
                caseName,
                sourcePath,
                originalFileExists = File.Exists(sourcePath),
                expectedUri = expectedUri.AbsoluteUri,
                compilerPath = compilerLocation.Path,
                capturedPath = artifact.Compilation.SyntaxTrees.Single().Path,
                projectedPath = producerClaim.Location.Path,
                publishedPath = publishedClaim.Location.Path,
                transportedPath = diagnostic.File,
                sarifUri = actualUri.AbsoluteUri,
                sarifLocalPath = actualUri.LocalPath,
                sarifFileExists = File.Exists(actualUri.LocalPath),
                exitCode,
                response.RunStatus,
                response.FailureReason,
                outcome = response.ClaimResults.Single().Outcome,
                diagnosticCount = diagnosticLines.Length,
                budgets = request.Budgets,
                clrResult = -1
            }, WorkerProtocolJson.SharedOptions));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Compilation.SyntaxTrees.Single().Path, Is.EqualTo(sourcePath), "Raw compiler capture.");
            Assert.That(producerClaim.Location.Path, Is.EqualTo(sourcePath), "Compiler claim projection.");
            Assert.That(publishedClaim.Location.Path, Is.EqualTo(sourcePath), "Published worker manifest.");
            Assert.That(diagnostic.File, Is.EqualTo(sourcePath), "Consumer diagnostic transport.");
            Assert.That(diagnostic.Line, Is.EqualTo(4));
            Assert.That(diagnostic.Column, Is.EqualTo(14));
            Assert.That(region.GetProperty("startLine").GetInt32(), Is.EqualTo(4));
            Assert.That(region.GetProperty("startColumn").GetInt32(), Is.EqualTo(14));
            Assert.That(actualUri.AbsoluteUri, Is.EqualTo(expectedUri.AbsoluteUri), "SARIF file URI.");
            Assert.That(actualUri.LocalPath, Is.EqualTo(sourcePath), "Independent file URI round trip.");
            Assert.That(File.Exists(actualUri.LocalPath), Is.True, "SARIF must target the existing source file.");
            Assert.That(actualUri.Query, Is.Empty);
            Assert.That(actualUri.Fragment, Is.Empty);
        }
    }
}
