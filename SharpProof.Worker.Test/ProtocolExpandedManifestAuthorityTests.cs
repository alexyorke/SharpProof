using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;
using LauncherProgram = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ProtocolExpandedManifestAuthorityTests
{
    [TestCase("small")]
    [TestCase("ascii")]
    [TestCase("escaped")]
    public async Task BoundResponsePreservesAuthorityAndRejectsOnlyExpandedPayload(string kind)
    {
        var rejected = kind == "escaped";
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference { Path = "compiler.manifest.json", Sha256 = WorkerProtocolVersions.EmptySha256 },
            Cache = new WorkerCacheOptions { Enabled = false }
        };
        var versions = new WorkerVersionSummary
        {
            WorkerVersion = "test-worker",
            ApiSpecVersion = "test-spec",
            WorkerBinarySha256 = WorkerProtocolVersions.EmptySha256,
            ApiSpecContentSha256 = WorkerProtocolVersions.EmptySha256
        };
        var manifest = new WorkerClaimManifest
        {
            Callables = [new WorkerCallableManifestEntry
            {
                CallableId = "M:Subject.Target",
                SelectedFeatures = [WorkerSelectedFeature.Contracts],
                SelectionReasons = [WorkerSelectionReason.ExplicitAnnotation],
                Location = new WorkerSourceLocation
                {
                    Path = kind == "ascii" ? new string('x', WorkerProtocolJson.MaximumJsonBytes / 6 + 1) : "Subject.cs",
                    Start = 1, Length = 1, Line = 1, Column = 1
                }
            }]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var requestHash = WorkerProtocolJson.ComputeRequestHash(request);
        var response = WorkerResultAssembler.Create(WorkerProtocolVersions.EmptySha256, manifest,
            WorkerRunStatus.Complete, WorkerRunFailureReason.None,
            [new WorkerCallableResult { CallableId = "M:Subject.Target", Coverage = WorkerCallableCoverage.Complete, Reason = WorkerCallableCoverageReason.None }],
            [], request.Budgets, WorkerCacheStatus.Disabled, 0, requestHash: requestHash, versions: versions);
        response = WorkerProtocolJson.DeserializeResponse(WorkerProtocolJson.SerializeResponse(response))!;
        if (rejected)
        {
            response.Manifest.Callables.Single().Location.Path = new string('<', WorkerProtocolJson.MaximumJsonBytes / 6 + 1);
        }
        var options = WorkerProtocolJson.Options;
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        var json = JsonSerializer.Serialize(response, options);
        Assert.That(Encoding.UTF8.GetByteCount(json), Is.LessThan(WorkerProtocolJson.MaximumJsonBytes));
        var decoded = WorkerProtocolJson.DeserializeResponse(json)!;
        var validation = WorkerProtocolJson.ValidateForRequest(decoded, requestHash, response.InputHash,
            manifest, request, versions);
        Assert.That(validation.IsValid, Is.EqualTo(!rejected));
        if (rejected)
        {
            Assert.That(validation.Errors.Select(static error => error.Code), Does.Contain("manifest.hash_payload_size"));
            Assert.That((Action)(() => WorkerProtocolJson.ComputeManifestHash(decoded.Manifest)), Throws.TypeOf<InvalidDataException>());
        }
        using var directory = new TempDirectory("bound-expanded-manifest-");
        var path = Path.Combine(directory.FullName, "response.json");
        await File.WriteAllTextAsync(path, json);
        var exitCode = LauncherProgram.ValidateAndReport(path, request, response.InputHash, manifest, versions,
            out var accepted, out var validatedResponse);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(rejected ? 3 : 0));
            Assert.That(accepted, Is.EqualTo(!rejected));
            Assert.That(validatedResponse != null, Is.EqualTo(!rejected));
        }
    }
}
