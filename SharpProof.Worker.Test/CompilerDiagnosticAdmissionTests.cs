using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerDiagnosticAdmissionTests
{
    [TestCase("null")]
    [TestCase("[null]")]
    [TestCase("[{\"isSource\":false,\"code\":null,\"message\":\"error\"}]")]
    public void MalformedDiagnosticArraysRejectAsJsonErrors(string diagnostics)
    {
        var json = MalformedArtifact(diagnostics);
        Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.Deserialize(json)));
    }

    [TestCase("null")]
    [TestCase("[null]")]
    [TestCase("[{\"isSource\":false,\"code\":null,\"message\":\"error\"}]")]
    public async Task MalformedDiagnosticArraysReturnManifestMismatch(string diagnostics)
    {
        using var directory = new TempDirectory("SharpProof.DiagnosticAdmission-");
        var path = Path.Combine(directory.FullName, "manifest.json");
        var bytes = Encoding.UTF8.GetBytes(MalformedArtifact(diagnostics));
        await File.WriteAllBytesAsync(path, bytes);
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = path,
                Sha256 = WorkerProtocolJson.ComputeSha256(bytes)
            },
            Cache = new WorkerCacheOptions { Enabled = false },
            Budgets = new WorkerBudgets()
        };
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.CompilerManifestMismatch));
            Assert.That(response.Errors.Select(error => error.Code), Does.Contain("compiler_manifest.invalid"));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    private static string MalformedArtifact(string diagnostics)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("public static class Subject { }");
        var document = JsonNode.Parse(CompilerManifestArtifactJson.Serialize(artifact))!;
        document["compilerDiagnostics"] = JsonNode.Parse(diagnostics);
        return document.ToJsonString();
    }
}
