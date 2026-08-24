using System.Text.Json;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static class SarifProjection
{
    internal static string Serialize(
        WorkerVerifyRequest request, WorkerVerifyResponse response)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
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
            .Select(result => ClaimResult(
                request, result, claims[result.ClaimId]))
            .ToList();
        results.AddRange(callableResults
            .Where(static result => result.Coverage == WorkerCallableCoverage.Incomplete)
            .Select(result => IncompleteResult(
                request, result, callables[result.CallableId])));
        var notifications = errors.Select(
            static error => Notification(error.Code, error.Message)).ToList();
        var assumptions = summary.Assumptions;
        if (assumptions.User + assumptions.Trusted != 0)
        {
            notifications.Add(Notification(
                "SP0048",
                "User assumption/trusted evidence declared: total=" +
                    (assumptions.User + assumptions.Trusted) + ", user=" +
                    assumptions.User + ", trusted=" + assumptions.Trusted + ".",
                LauncherPresentation.Level(request.AssumptionPolicy, "note")));
        }

        if (runStatus != WorkerRunStatus.Complete)
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
                ["PROJECTROOT"] = new
                {
                    uri = new Uri(
                        Path.GetFullPath(Environment.CurrentDirectory) +
                        Path.DirectorySeparatorChar).AbsoluteUri
                }
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
            "SP0047", "review",
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
        WorkerSourceLocation location, string semanticId,
        object properties)
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
                artifactLocation = new
                {
                    uri = LocationUri(location.Path),
                    uriBaseId = "PROJECTROOT"
                },
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

    private static string LocationUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            return uri.AbsoluteUri;
        }

        // Compiler locations are intentionally project-relative (and may be
        // mapped virtual paths). Preserve that identity in SARIF while
        // escaping each physical path segment for URI consumers.
        return string.Join(
            "/",
            path.Replace('\\', '/')
                .Split('/')
                .Select(Uri.EscapeDataString));
    }
}
