using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class SarifProjectionTests
{
    [Test]
    public void RelativeCompilerMappedPathIsEscapedAndAnchoredToProjectRoot()
    {
        const string projectDirectory = "/workspace/consumer project";
        const string mappedPath =
            "generated/mapped#source?\\Identity %.cs";
        var location = new WorkerSourceLocation
        {
            Path = mappedPath,
            Start = 0,
            Length = 1,
            Line = 17,
            Column = 5
        };
        var manifest = new WorkerClaimManifest
        {
            Callables = [new WorkerCallableManifestEntry
            {
                CallableId = "Consumer.Subject.Identity()",
                Location = location,
                ClaimIds = ["claim-1"]
            }],
            Claims = [new WorkerClaimManifestEntry
            {
                ClaimId = "claim-1",
                CallableId = "Consumer.Subject.Identity()",
                Ordinal = 0,
                Kind = WorkerClaimKind.Postcondition,
                Evidence = WorkerClaimEvidence.DirectClause,
                Location = location
            }]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            ClaimResults = [new WorkerClaimResult
            {
                ClaimId = "claim-1",
                Outcome = WorkerClaimOutcome.Refuted,
                Reason = WorkerClaimReason.None
            }],
            Summary = new WorkerVerificationSummary
            {
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "1.0.0-test"
                }
            }
        };

        using var document = JsonDocument.Parse(
            SarifProjection.Serialize(
                new WorkerVerifyRequest(),
                response,
                projectDirectory));
        var run = document.RootElement.GetProperty("runs")[0];
        var sourceRoot = run.GetProperty("originalUriBaseIds")
            .GetProperty("%SRCROOT%")
            .GetProperty("uri")
            .GetString();
        var artifactLocation = run.GetProperty("results")[0]
            .GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("artifactLocation");
        var relativeUri = artifactLocation.GetProperty("uri").GetString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                sourceRoot,
                Is.EqualTo("file:///workspace/consumer%20project/"));
            Assert.That(
                artifactLocation.GetProperty("uriBaseId").GetString(),
                Is.EqualTo("%SRCROOT%"));
            Assert.That(
                relativeUri,
                Is.EqualTo(
                    "generated/mapped%23source%3F%5CIdentity%20%25.cs"));
        }

        var resolved = new Uri(new Uri(sourceRoot!), relativeUri!);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Fragment, Is.Empty);
            Assert.That(resolved.Query, Is.Empty);
            Assert.That(
                resolved.LocalPath,
                Is.EqualTo(projectDirectory + "/" + mappedPath));
        }
    }
}
