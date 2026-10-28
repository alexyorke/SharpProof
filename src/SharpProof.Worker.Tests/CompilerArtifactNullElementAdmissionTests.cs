using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerArtifactNullElementAdmissionTests
{
    [TestCase("callables")]
    [TestCase("parameterTypes")]
    [TestCase("callees")]
    public async Task NullArtifactElementsRejectAsManifestMismatch(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var json = MalformedArtifact(field);
        Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.Deserialize(
            json, TestContext.CurrentContext.CancellationToken)));

        using var directory = new TempDirectory("SharpProof.NullArtifactElement-");
        var path = Path.Combine(directory.FullName, "manifest.json");
        var bytes = Encoding.UTF8.GetBytes(json).ToImmutableArray();
        await File.WriteAllBytesAsync(path, bytes.AsMemory(), TestContext.CurrentContext.CancellationToken);
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
        var response = await worker.VerifyAsync(request, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.CompilerManifestMismatch));
            Assert.That(response.Errors.Select(error =>
                error is null ? throw new ArgumentNullException(nameof(error)) : error.Code),
                Does.Contain("compiler_manifest.invalid"));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    private static string MalformedArtifact(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            static class Subject {
                [ZeroAllocations] public static int Root(int value) => Identity(value);
                static int Identity(int value) => value;
            }
            """);
        var document = JsonNode.Parse(CompilerManifestArtifactJson.Serialize(
            artifact, TestContext.CurrentContext.CancellationToken))!;
        if (field == "callables")
        {
            document[field]![0] = null;
        }
        else
        {
            var body = document["reachableSource"]!["bodies"]!.AsArray().First(body =>
                body != null && body["callees"]!.AsArray().Count > 0)!;
            body[field]![0] = null;
        }
        return document.ToJsonString();
    }
}
