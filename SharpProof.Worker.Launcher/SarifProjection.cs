using System.Text.Json;
using System.Text.Json.Nodes;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Launcher;

internal static class SarifProjection
{
    internal static string Serialize(
        WorkerVerifyRequest request, WorkerVerifyResponse response)
    {
        return Serialize(request, response, Environment.CurrentDirectory);
    }

    internal static string Serialize(
        WorkerVerifyRequest request, WorkerVerifyResponse response,
        string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
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
            var assumptionLocation = AssumptionEvidenceLocation.Find(response);
            notifications.Add(Notification(
                "SP0048",
                "User assumption/trusted evidence declared: total=" +
                    (assumptions.User + assumptions.Trusted) + ", user=" +
                    assumptions.User + ", trusted=" + assumptions.Trusted + ".",
                LauncherPresentation.Level(request.AssumptionPolicy, "note"),
                assumptionLocation));
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
                    uri = ProjectRootUri(projectDirectory)
                }
            },
            invocations = new[] { new {
                executionSuccessful = runStatus == WorkerRunStatus.Complete &&
                    errors.Length == 0 &&
                    !claimResults.Any(static result =>
                        result.Outcome == WorkerClaimOutcome.Refuted),
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

    internal static string MergeRuns(string existing, string current)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existing);
        ArgumentException.ThrowIfNullOrWhiteSpace(current);

        var existingDocument = JsonNode.Parse(existing)?.AsObject();
        var currentDocument = JsonNode.Parse(current)?.AsObject();
        if (existingDocument == null || currentDocument == null ||
            existingDocument["runs"] is not JsonArray existingRuns ||
            currentDocument["runs"] is not JsonArray { Count: 1 } currentRuns ||
            currentRuns[0] is not JsonObject currentRun)
        {
            throw new InvalidDataException("The existing SARIF document is malformed.");
        }

        var currentRoot = ProjectRoot(currentRun);
        if (string.IsNullOrEmpty(currentRoot))
        {
            throw new InvalidDataException("The current SARIF run has no project root.");
        }

        var replacement = -1;
        for (var index = 0; index < existingRuns.Count; index++)
        {
            if (existingRuns[index] is JsonObject run &&
                string.Equals(ProjectRoot(run), currentRoot,
                    StringComparison.Ordinal))
            {
                replacement = index;
                break;
            }
        }

        var runs = existingRuns
            .Where(static run => run != null)
            .Select(static run => run!.DeepClone())
            .ToList();
        var currentClone = currentRun.DeepClone();
        if (replacement >= 0)
        {
            runs[replacement] = currentClone;
        }
        else
        {
            runs.Add(currentClone);
        }

        existingDocument["runs"] = new JsonArray(
            [.. runs.OrderBy(ProjectRoot, StringComparer.Ordinal)]);
        return existingDocument.ToJsonString(WorkerProtocolJson.Options);
    }

    private static string? ProjectRoot(JsonNode? run)
    {
        return run?["originalUriBaseIds"]?["PROJECTROOT"]?["uri"]?.GetValue<string>();
    }

    private static string ProjectRootUri(string projectDirectory)
    {
        var path = Path.GetFullPath(projectDirectory);
        if (!Path.EndsInDirectorySeparator(path))
        {
            path += Path.DirectorySeparatorChar;
        }
        return new UriBuilder(Uri.UriSchemeFile, string.Empty)
        {
            Path = path
        }.Uri.AbsoluteUri;
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
                outcome == WorkerClaimOutcome.Refuted ? "error" : "none",
            outcome + " " + LauncherPresentation.ClaimKind(claim) + " " +
                result.ClaimId + " for " + claim.CallableId + reason + witness,
            effectWitness?.Location ?? claim.Location,
            result.ClaimId,
            new
            {
                claim,
                result,
                sharpProofPolicyLevel = LauncherPresentation.Level(
                    request.VerifyPolicy,
                    "note")
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
            "none",
            "Selected analysis is incomplete for " + callableId +
                " (" + reason + ").",
            callable.Location, callableId,
            new
            {
                callable,
                result,
                sharpProofPolicyLevel = LauncherPresentation.Level(
                    request.VerifyPolicy,
                    "note")
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

    private static Dictionary<string, object> Notification(
        string id, string message, string level = "error",
        WorkerSourceLocation? location = null)
    {
        var notification = new Dictionary<string, object>
        {
            ["descriptor"] = new
            {
                id
            },
            ["level"] = level,
            ["message"] = new
            {
                text = message
            }
        };
        if (location != null)
        {
            notification["locations"] = new[] { new { physicalLocation = new
            {
                artifactLocation = new
                {
                    uri = LocationUri(location.Path),
                    uriBaseId = "PROJECTROOT"
                },
                region = new
                {
                    startLine = location.Line,
                    startColumn = location.Column
                }
            }}};
        }

        return notification;
    }

    private static string LocationUri(string path)
    {
        if (IsAbsoluteFilePath(path) &&
            Uri.TryCreate(path, UriKind.Absolute, out var uri))
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

    private static bool IsAbsoluteFilePath(string path)
    {
        return Path.IsPathFullyQualified(path) ||
            path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Length >= 3 &&
            char.IsLetter(path[0]) &&
            path[1] == ':' &&
            (path[2] == '/' || path[2] == '\\');
    }
}
