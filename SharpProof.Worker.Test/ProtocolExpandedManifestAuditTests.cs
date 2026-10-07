using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;
using LauncherProgram = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ProtocolExpandedManifestAuditTests
{
    [TestCase("manifest")]
    [TestCase("response")]
    [TestCase("launcher")]
    public async Task CompactResponseWithOversizedCanonicalManifestIsRejected(string boundary)
    {
        var response = CreateResponse(new string('<', WorkerProtocolJson.MaximumJsonBytes / 6 + 1));
        var options = WorkerProtocolJson.Options;
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        var json = JsonSerializer.Serialize(response, options);
        Assert.That(Encoding.UTF8.GetByteCount(json), Is.LessThan(WorkerProtocolJson.MaximumJsonBytes));
        var decoded = WorkerProtocolJson.DeserializeResponse(json)!;
        Assert.That(decoded.Manifest.Callables.Single().Location.Path.Length,
            Is.EqualTo(WorkerProtocolJson.MaximumJsonBytes / 6 + 1));
        await TestContext.Out.WriteLineAsync($"compactBytes={Encoding.UTF8.GetByteCount(json)} sha256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(json))}");

        if (boundary == "launcher")
        {
            using var directory = new TempDirectory("expanded-manifest-");
            var path = Path.Combine(directory.FullName, "response.json");
            await File.WriteAllTextAsync(path, json);
            var exitCode = LauncherProgram.ValidateAndReport(path, CreateRequest(), null, null, null,
                out var accepted, out var validatedResponse);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exitCode, Is.EqualTo(3));
                Assert.That(accepted, Is.False);
                Assert.That(validatedResponse, Is.Null);
            }
        }
        else
        {
            var validation = boundary == "manifest"
                ? WorkerProtocolJson.ValidateManifest(decoded.Manifest)
                : WorkerProtocolJson.Validate(decoded);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(validation.IsValid, Is.False);
                Assert.That(validation.Errors.Select(static error => error.Code),
                    Does.Contain("manifest.hash_payload_size"));
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SmallResponseRetainsHashValidationAndLauncherResult(bool invalidHash)
    {
        var response = CreateResponse("<Subject>.cs");
        WorkerProtocolJson.SealManifest(response.Manifest);
        if (invalidHash)
        {
            response.Manifest.Hash = WorkerProtocolVersions.EmptySha256;
        }
        var json = WorkerProtocolJson.SerializeResponse(response);
        var decoded = WorkerProtocolJson.DeserializeResponse(json)!;
        var validation = WorkerProtocolJson.Validate(decoded);
        Assert.That(validation.IsValid, Is.EqualTo(!invalidHash));
        if (invalidHash)
        {
            Assert.That(validation.Errors.Select(static error => error.Code), Does.Contain("manifest.hash"));
        }
        using var directory = new TempDirectory("small-expanded-manifest-");
        var path = Path.Combine(directory.FullName, "response.json");
        await File.WriteAllTextAsync(path, json);
        var exitCode = LauncherProgram.ValidateAndReport(path, CreateRequest(), null, null, null,
            out var accepted, out var validatedResponse);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(invalidHash ? 3 : 0));
            Assert.That(accepted, Is.EqualTo(!invalidHash));
            Assert.That(validatedResponse != null, Is.EqualTo(!invalidHash));
        }
    }

    private static WorkerVerifyRequest CreateRequest()
    {
        return new()
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = "compiler.manifest.json",
                Sha256 = WorkerProtocolVersions.EmptySha256
            },
            Cache = new WorkerCacheOptions { Enabled = false }
        };
    }

    private static WorkerVerifyResponse CreateResponse(string path)
    {
        const string callableId = "M:Subject.Target";
        return new WorkerVerifyResponse
        {
            InputHash = WorkerProtocolVersions.EmptySha256,
            RequestHash = WorkerProtocolVersions.EmptySha256,
            Manifest = new WorkerClaimManifest
            {
                Hash = WorkerProtocolVersions.EmptySha256,
                Callables = [new WorkerCallableManifestEntry
                {
                    CallableId = callableId,
                    SelectedFeatures = [WorkerSelectedFeature.Contracts],
                    SelectionReasons = [WorkerSelectionReason.ExplicitAnnotation],
                    Location = new WorkerSourceLocation { Path = path, Start = 1, Length = 1, Line = 1, Column = 1 }
                }]
            },
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [new WorkerCallableResult
            {
                CallableId = callableId,
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None
            }],
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "test-worker",
                    ApiSpecVersion = "test-spec",
                    WorkerBinarySha256 = WorkerProtocolVersions.EmptySha256,
                    ApiSpecContentSha256 = WorkerProtocolVersions.EmptySha256
                }
            }
        };
    }
}
