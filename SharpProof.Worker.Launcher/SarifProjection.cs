using System.Text.Json;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static class SarifProjection {
    internal static string Serialize(
        WorkerVerifyRequest request, WorkerVerifyResponse response) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        WorkerProtocolJson.Canonicalize(response);
        var claims = response.Manifest.Claims.ToDictionary(
            static claim => claim.ClaimId, StringComparer.Ordinal);
        var callables = response.Manifest.Callables.ToDictionary(
            static callable => callable.CallableId, StringComparer.Ordinal);
        var results = response.ClaimResults
            .Select(result => ClaimResult(
                request, result, claims[result.ClaimId]))
            .Concat(response.CallableResults
                .Where(static result =>
                    result.Coverage == WorkerCallableCoverage.Incomplete)
                .Select(result => IncompleteResult(
                    request, result, callables[result.CallableId])))
            .ToList();
        var notifications = response.Errors.Select(
            static error => Notification(error.Code, error.Message)).ToList();
        var assumptions = response.Summary.Assumptions;
        if (assumptions.User + assumptions.Trusted != 0)
            notifications.Add(Notification(
                "SP0048",
                "User assumption/trusted evidence declared: total=" +
                    (assumptions.User + assumptions.Trusted) + ", user=" +
                    assumptions.User + ", trusted=" + assumptions.Trusted + ".",
                PolicyLevel(
                    request.AssumptionPolicy == WorkerAssumptionPolicy.Error,
                    request.AssumptionPolicy == WorkerAssumptionPolicy.Warn)));
        if (response.RunStatus != WorkerRunStatus.Complete &&
            notifications.Count == 0)
            notifications.Add(Notification(
                "worker." + response.RunStatus,
                "SharpProof worker run " + response.RunStatus +
                    " (" + response.FailureReason + ")."));
        var run = new {
            tool = new {
                driver = new {
                    name = "SharpProof",
                    informationUri = "https://github.com/alexyorke/SharpProof",
                    version = response.Summary.Versions.WorkerVersion
                }
            },
            automationDetails = new { id = response.Manifest.Hash },
            invocations = new[] { new {
                executionSuccessful = response.RunStatus ==
                    WorkerRunStatus.Complete && response.Errors.Length == 0,
                properties = new {
                    response.RunStatus, response.FailureReason
                },
                toolExecutionNotifications = notifications
            }},
            results,
            properties = response.Summary
        };
        var document = new Dictionary<string, object> {
            ["$schema"] =
                "https://json.schemastore.org/sarif-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new[] { run }
        };
        return JsonSerializer.Serialize(document, WorkerProtocolJson.Options);
    }

    private static object ClaimResult(
        WorkerVerifyRequest request, WorkerClaimResult result,
        WorkerClaimManifestEntry claim) {
        var reason = result.Reason == WorkerClaimReason.None
            ? string.Empty : " (" + result.Reason + ")";
        return Result(
            "SharpProof." + result.Outcome,
            result.Outcome == WorkerClaimOutcome.Proven ? "pass" :
                result.Outcome == WorkerClaimOutcome.Refuted ? "fail" : "review",
            result.Outcome == WorkerClaimOutcome.Proven ? "none" :
                result.Outcome == WorkerClaimOutcome.Refuted ? "error" :
                PolicyLevel(
                    request.VerifyPolicy == WorkerVerifyPolicy.RequireProven,
                    request.VerifyPolicy == WorkerVerifyPolicy.WarnOnUnknown),
            result.Outcome + " postcondition " + result.ClaimId +
                " for " + claim.CallableId + reason,
            claim.Location, result.ClaimId,
            new { claim, result });
    }

    private static object IncompleteResult(
        WorkerVerifyRequest request, WorkerCallableResult result,
        WorkerCallableManifestEntry callable) =>
        Result(
            "SP0047", "review", PolicyLevel(
                request.VerifyPolicy == WorkerVerifyPolicy.RequireProven,
                request.VerifyPolicy == WorkerVerifyPolicy.WarnOnUnknown),
            "Selected analysis is incomplete for " + result.CallableId +
                " (" + result.Reason + ").",
            callable.Location, result.CallableId,
            new { callable, result });

    private static string PolicyLevel(bool error, bool warning) =>
        error ? "error" : warning ? "warning" : "note";

    private static object Result(
        string ruleId, string kind, string level, string message,
        WorkerSourceLocation location, string semanticId, object properties) =>
        new {
            ruleId,
            kind,
            level,
            message = new { text = message },
            locations = new[] { new { physicalLocation = new {
                artifactLocation = new { uri = LocationUri(location.Path) },
                region = new {
                    startLine = location.Line, startColumn = location.Column
                }
            }}},
            partialFingerprints = new Dictionary<string, string> {
                ["sharpProofSemanticId/v1"] = semanticId
            },
            properties
        };

    private static object Notification(
        string id, string message, string level = "error") =>
        new {
            descriptor = new { id },
            level,
            message = new { text = message }
        };

    private static string LocationUri(string path) =>
        Uri.TryCreate(path, UriKind.Absolute, out var uri)
            ? uri.AbsoluteUri : path.Replace('\\', '/');
}
