using System.Text.Json;
using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static class SarifProjection
{
    private const string SourceRootUriBaseId = "%SRCROOT%";

    internal static string Serialize(
        WorkerVerifyRequest request, WorkerVerifyResponse response,
        string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        var projectDirectoryUri = DirectoryUri(projectDirectory);
        WorkerProtocolJson.Canonicalize(response);
        var manifest = response.Manifest;
        var summary = response.Summary;
        var runStatus = response.RunStatus;
        var failureReason = response.FailureReason;
        var claimResults = response.ClaimResults;
        var callableResults = response.CallableResults;
        var errors = response.Errors;
        var claims = manifest.Claims.ToDictionary(
            static claim => claim.ClaimId, StringComparer.Ordinal);
        var callables = manifest.Callables.ToDictionary(
            static callable => callable.CallableId, StringComparer.Ordinal);
        var results = claimResults
            .Select(result => ClaimResult(request, result, claims[result.ClaimId]))
            .ToList();
        results.AddRange(callableResults
            .Where(static result => result.Coverage == WorkerCallableCoverage.Incomplete)
            .Select(result => IncompleteResult(request, result, callables[result.CallableId])));
        var notifications = errors.Select(
            static error => Notification(error.Code, error.Message)).ToList();
        var assumptions = summary.Assumptions;
        if (assumptions.User + assumptions.Trusted != 0)
        {
            notifications.Add(Notification(
                VerifierDiagnosticCodes.AssumptionsDeclared,
                LauncherPresentation.AssumptionsDeclaredMessage(assumptions),
                LauncherPresentation.Level(request.AssumptionPolicy, "note")));
        }

        if (runStatus != WorkerRunStatus.Complete &&
            notifications.Count == 0)
        {
            notifications.Add(Notification(
                "worker." + runStatus,
                "SharpProof worker run " + runStatus +
                    " (" + failureReason + ")."));
        }

        var run = new
        {
            tool = new
            {
                driver = new
                {
                    name = "SharpProof",
                    informationUri = "https://github.com/alexyorke/SharpProof",
                    version = summary.Versions.WorkerVersion
                }
            },
            automationDetails = new
            {
                id = manifest.Hash
            },
            originalUriBaseIds = new Dictionary<string, object>
            {
                [SourceRootUriBaseId] = new { uri = projectDirectoryUri }
            },
            invocations = new[] { new {
                executionSuccessful = runStatus == WorkerRunStatus.Complete && errors.Length == 0,
                properties = new { RunStatus = runStatus, FailureReason = failureReason },
                toolExecutionNotifications = notifications
            }},
            results,
            properties = summary
        };
        var document = new Dictionary<string, object>
        {
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new[] { run }
        };
        return JsonSerializer.Serialize(document, WorkerProtocolJson.Options);
    }

    private static object ClaimResult(
        WorkerVerifyRequest request, WorkerClaimResult result,
        WorkerClaimManifestEntry claim)
    {
        var outcome = result.Outcome;
        var reasonValue = result.Reason;
        var effectWitness = result.EffectWitness;
        var reason = reasonValue == WorkerClaimReason.None ? string.Empty : " (" + reasonValue + ")";
        var witness = effectWitness == null
            ? string.Empty
            : " [concrete " + effectWitness.Kind + ": " + effectWitness.Detail +
                " at " + effectWitness.Location.Path + ":" + effectWitness.Location.Line +
                ":" + effectWitness.Location.Column + "]";
        return Result(
            "SharpProof." + outcome,
            outcome == WorkerClaimOutcome.Proven ? "pass" :
                outcome == WorkerClaimOutcome.Refuted ? "fail" : "review",
            outcome == WorkerClaimOutcome.Proven ? "none" :
                outcome == WorkerClaimOutcome.Refuted ? "error" :
                LauncherPresentation.Level(request.VerifyPolicy, "note"),
            outcome + " " + LauncherPresentation.ClaimKind(claim) + " " +
                result.ClaimId + " for " + claim.CallableId + reason + witness,
            effectWitness?.Location ?? claim.Location,
            result.ClaimId,
            new
            {
                claim,
                result
            });
    }

    private static object IncompleteResult(
        WorkerVerifyRequest request, WorkerCallableResult result,
        WorkerCallableManifestEntry callable)
    {
        var callableId = result.CallableId;
        var reason = result.Reason;
        return Result(
            VerifierDiagnosticCodes.IncompleteSelectedCallable, "review",
            LauncherPresentation.Level(request.VerifyPolicy, "note"),
            "Selected analysis is incomplete for " + callableId +
                " (" + reason + ").",
            callable.Location, callableId,
            new
            {
                callable,
                result
            });
    }

    private static object Result(
        string ruleId, string kind, string level, string message,
        WorkerSourceLocation location, string semanticId, object properties)
    {
        return new
        {
            ruleId,
            kind,
            level,
            message = new
            {
                text = message
            },
            locations = new[] { new { physicalLocation = new {
                artifactLocation = ArtifactLocation(location.Path),
                region = new {
                    startLine = location.Line, startColumn = location.Column
                }
            }}},
            partialFingerprints = new Dictionary<string, string>
            {
                ["sharpProofSemanticId/v1"] = semanticId
            },
            properties
        };
    }

    private static object Notification(
        string id, string message, string level = "error")
    {
        return new
        {
            descriptor = new
            {
                id
            },
            level,
            message = new
            {
                text = message
            }
        };
    }

    private static object ArtifactLocation(string path)
    {
        return TryAbsolutePathUri(path, out var uri)
            ? new { uri }
            : new
            {
                uri = EscapePath(path),
                uriBaseId = SourceRootUriBaseId
            };
    }

    private static string DirectoryUri(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var windowsPath = IsWindowsDriveAbsolute(path);
        var directory = windowsPath
            ? path.TrimEnd('/', '\\') + '\\'
            : path.TrimEnd('/') + '/';
        if (!TryAbsolutePathUri(directory, out var uri))
        {
            throw new ArgumentException(
                "The SARIF project directory must be an absolute path.",
                nameof(path));
        }

        return uri;
    }

    private static bool TryAbsolutePathUri(
        string path, out string uri)
    {
        if (path.Length != 0 && path[0] == '/')
        {
            uri = "file://" + EscapePath(path);
            return true;
        }
        if (IsWindowsDriveAbsolute(path))
        {
            uri = "file:///" + path[..2] +
                EscapePath(path[2..].Replace('\\', '/'));
            return true;
        }

        uri = string.Empty;
        return false;
    }

    private static bool IsWindowsDriveAbsolute(string path)
    {
        return path.Length >= 3 &&
            char.IsAsciiLetter(path[0]) &&
            path[1] == ':' &&
            path[2] is '/' or '\\';
    }

    private static string EscapePath(string path)
    {
        return string.Join(
            "/",
            path.Split('/').Select(
                static segment => Uri.EscapeDataString(segment)));
    }
}
