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
                    "generated/mapped%23source%3F/Identity%20%25.cs"));
        }

        var resolved = new Uri(new Uri(sourceRoot!), relativeUri!);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolved.Fragment, Is.Empty);
            Assert.That(resolved.Query, Is.Empty);
            Assert.That(
                resolved.LocalPath,
                Is.EqualTo(
                    projectDirectory + "/" + mappedPath.Replace('\\', '/')));
        }
    }

    [Test]
    public void AbsolutePathsInsideSourceRootAreRelativeAndOutsidePathsStayAbsolute()
    {
        const string projectDirectory = "/workspace/consumer project";
        (string CallableId, string ClaimId, string Path)[] sources =
        [
            ("Consumer.Subject.Root()", "claim-root", projectDirectory),
            ("Consumer.Subject.Inside()", "claim-inside",
                projectDirectory + "/src/Foo #?.cs"),
            ("Consumer.Subject.Sibling()", "claim-sibling",
                projectDirectory + "-backup/Foo.cs"),
            ("Consumer.Subject.Outside()", "claim-outside",
                "/opt/external/Foo.cs")
        ];
        var locations = sources.Select(static source =>
            new WorkerSourceLocation
            {
                Path = source.Path,
                Start = 0,
                Length = 1,
                Line = 1,
                Column = 1
            }).ToArray();
        var manifest = new WorkerClaimManifest
        {
            Callables = [.. sources.Select((source, index) =>
                new WorkerCallableManifestEntry
                {
                    CallableId = source.CallableId,
                    Location = locations[index],
                    ClaimIds = [source.ClaimId]
                })],
            Claims = [.. sources.Select((source, index) =>
                new WorkerClaimManifestEntry
                {
                    ClaimId = source.ClaimId,
                    CallableId = source.CallableId,
                    Ordinal = index,
                    Kind = WorkerClaimKind.Postcondition,
                    Evidence = WorkerClaimEvidence.DirectClause,
                    Location = locations[index]
                })]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            ClaimResults = [.. sources.Select(static source =>
                new WorkerClaimResult
                {
                    ClaimId = source.ClaimId,
                    Outcome = WorkerClaimOutcome.Proven,
                    Reason = WorkerClaimReason.None
                })],
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
        var results = run.GetProperty("results");
        var sourceRoot = run.GetProperty("originalUriBaseIds")
            .GetProperty("%SRCROOT%")
            .GetProperty("uri")
            .GetString();

        JsonElement ArtifactLocationFor(string claimId)
        {
            var result = results.EnumerateArray().Single(candidate =>
                candidate.GetProperty("partialFingerprints")
                    .GetProperty("sharpProofSemanticId/v1")
                    .GetString() == claimId);
            return result.GetProperty("locations")[0]
                .GetProperty("physicalLocation")
                .GetProperty("artifactLocation");
        }

        var rootLocation = ArtifactLocationFor("claim-root");
        Assert.That(rootLocation.GetProperty("uri").GetString(), Is.EqualTo("."));
        Assert.That(
            rootLocation.GetProperty("uriBaseId").GetString(),
            Is.EqualTo("%SRCROOT%"));

        var insideLocation = ArtifactLocationFor("claim-inside");
        Assert.That(
            insideLocation.GetProperty("uri").GetString(),
            Is.EqualTo("src/Foo%20%23%3F.cs"));
        Assert.That(
            insideLocation.GetProperty("uriBaseId").GetString(),
            Is.EqualTo("%SRCROOT%"));

        Assert.That(
            ArtifactLocationFor("claim-sibling").GetProperty("uri").GetString(),
            Is.EqualTo("file:///workspace/consumer%20project-backup/Foo.cs"));
        Assert.That(
            ArtifactLocationFor("claim-outside").GetProperty("uri").GetString(),
            Is.EqualTo("file:///opt/external/Foo.cs"));
        Assert.That(
            ArtifactLocationFor("claim-sibling").TryGetProperty(
                "uriBaseId", out _),
            Is.False);
        Assert.That(sourceRoot, Is.EqualTo("file:///workspace/consumer%20project/"));
    }

    [Test]
    public void ImplementationIlProofMakesRuntimeBinaryAssumptionVisible()
    {
        const string projectDirectory = "/workspace/consumer";
        var location = new WorkerSourceLocation
        {
            Path = "source.cs",
            Start = 0,
            Length = 1,
            Line = 4,
            Column = 9
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
                Outcome = WorkerClaimOutcome.Proven,
                Reason = WorkerClaimReason.None,
                ProofCore = ["il-summary:M:Lib.Max(System.Int32,System.Int32)"]
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
        var message = document.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results")[0]
            .GetProperty("message")
            .GetProperty("text")
            .GetString();

        Assert.That(
            message,
            Does.Contain(
                "implementation-IL proof assumes the compile-time referenced binary is the runtime binary"));
    }

    [TestCase(WorkerVerifyPolicy.Advisory, "review", "none")]
    [TestCase(WorkerVerifyPolicy.WarnOnUnknown, "fail", "warning")]
    [TestCase(WorkerVerifyPolicy.RequireProven, "fail", "error")]
    public void UnknownAndIncompleteResultsUseValidSarifKindAndLevel(
        WorkerVerifyPolicy policy, string expectedKind, string expectedLevel)
    {
        var location = new WorkerSourceLocation
        {
            Path = "source.cs",
            Start = 0,
            Length = 1,
            Line = 4,
            Column = 9
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
                Outcome = WorkerClaimOutcome.Unknown,
                Reason = WorkerClaimReason.UnsupportedBody
            }],
            CallableResults = [new WorkerCallableResult
            {
                CallableId = "Consumer.Subject.Identity()",
                Coverage = WorkerCallableCoverage.Incomplete,
                Reason = WorkerCallableCoverageReason.UnsupportedCallable
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
                new WorkerVerifyRequest { VerifyPolicy = policy },
                response,
                "/workspace/consumer"));
        var results = document.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.GetArrayLength(), Is.EqualTo(2));
            foreach (var result in results.EnumerateArray())
            {
                Assert.That(result.GetProperty("kind").GetString(), Is.EqualTo(expectedKind));
                Assert.That(result.GetProperty("level").GetString(), Is.EqualTo(expectedLevel));
            }
        }

        AssertNonFailResultsUseNoneLevel(results);
    }

    [TestCase(WorkerAssumptionPolicy.Allow, "review", "none")]
    [TestCase(WorkerAssumptionPolicy.Warn, "fail", "warning")]
    [TestCase(WorkerAssumptionPolicy.Error, "fail", "error")]
    public void AssumptionResultsUseSarifValidKindAndLevel(
        WorkerAssumptionPolicy policy, string expectedKind,
        string expectedLevel)
    {
        var location = new WorkerSourceLocation
        {
            Path = "source.cs",
            Start = 0,
            Length = 1,
            Line = 1,
            Column = 1
        };
        var callable = new WorkerCallableManifestEntry
        {
            CallableId = "Consumer.Subject.Identity()",
            Location = location,
            ClaimIds = []
        };
        var manifest = new WorkerClaimManifest
        {
            Callables = [callable],
            Claims = []
        };
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [new WorkerCallableResult
            {
                CallableId = callable.CallableId,
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None,
                Assumptions = [new WorkerAssumptionEvidence
                {
                    Id = "assumption-1",
                    Kind = WorkerAssumptionKind.UserAssume,
                    Used = true
                }]
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
                new WorkerVerifyRequest { AssumptionPolicy = policy },
                response,
                "/workspace/consumer"));
        var results = document.RootElement
            .GetProperty("runs")[0]
            .GetProperty("results");

        Assert.That(results.GetArrayLength(), Is.EqualTo(1));
        var assumptionResult = results[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                assumptionResult.GetProperty("kind").GetString(),
                Is.EqualTo(expectedKind));
            Assert.That(
                assumptionResult.GetProperty("level").GetString(),
                Is.EqualTo(expectedLevel));
        }

        AssertNonFailResultsUseNoneLevel(results);
    }

    private static void AssertNonFailResultsUseNoneLevel(JsonElement results)
    {
        foreach (var result in results.EnumerateArray())
        {
            if (result.TryGetProperty("kind", out var kind) &&
                kind.GetString() != "fail")
            {
                Assert.That(
                    result.GetProperty("level").GetString(),
                    Is.EqualTo("none"),
                    "SARIF results with a non-fail kind must use level none.");
            }
        }
    }
}
