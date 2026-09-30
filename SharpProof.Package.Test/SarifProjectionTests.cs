using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class SarifProjectionTests
{
    [TestCase("/../../workspace/consumer/./src/../Subject.cs", "file:///workspace/consumer/Subject.cs")]
    [TestCase("/workspace/consumer/src/../../Subject.cs", "file:///workspace/Subject.cs")]
    public void AbsoluteMappedPathsResolveDotSegments(string path, string expected)
    {
        var response = new WorkerVerifyResponse
        {
            Manifest = new WorkerClaimManifest
            {
                Claims = [new WorkerClaimManifestEntry { ClaimId = "claim", Kind = WorkerClaimKind.Postcondition,
                    Location = new WorkerSourceLocation { Path = path, Line = 1, Column = 1 } }]
            },
            ClaimResults = [new WorkerClaimResult { ClaimId = "claim", Outcome = WorkerClaimOutcome.Proven }]
        };
        using var document = JsonDocument.Parse(SarifProjection.Serialize(new WorkerVerifyRequest(), response, "/workspace/consumer"));
        var uri = document.RootElement.GetProperty("runs")[0].GetProperty("results")[0]
            .GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation")
            .GetProperty("uri").GetString();
        Assert.That(new Uri(new Uri("file:///workspace/consumer/"), uri!).AbsoluteUri, Is.EqualTo(expected));
    }

    [Test]
    public void RelativeProjectRootCannotAnchorSarifSources()
    {
        Assert.That(Assert.Throws<ArgumentException>((Action)(() =>
            SarifProjection.Serialize(new WorkerVerifyRequest(), new WorkerVerifyResponse(), "relative/project")))!.ParamName,
            Is.EqualTo("path"));
    }

    [TestCase(WorkerEffectContractKind.ZeroAllocations)]
    [TestCase(WorkerEffectContractKind.AllowedCapabilities)]
    [TestCase(WorkerEffectContractKind.AllowedExceptions)]
    [TestCase(WorkerEffectContractKind.EffectContract)]
    public void EffectPresentationRetainsItsContractKind(WorkerEffectContractKind kind)
    {
        Assert.That(LauncherPresentation.ClaimKind(new WorkerClaimManifestEntry
        {
            Kind = WorkerClaimKind.Effect,
            EffectContractKind = kind
        }), Is.EqualTo("effect:" + kind));
    }

    [TestCase(WorkerClaimOutcome.Proven, WorkerVerifyPolicy.Advisory, WorkerVacuityKind.None, "pass", "none")]
    [TestCase(WorkerClaimOutcome.Proven, WorkerVerifyPolicy.Advisory, WorkerVacuityKind.ContradictoryPreconditions, "review", "none")]
    [TestCase(WorkerClaimOutcome.Refuted, WorkerVerifyPolicy.Advisory, WorkerVacuityKind.None, "fail", "error")]
    [TestCase(WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.Advisory, WorkerVacuityKind.None, "review", "none")]
    [TestCase(WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.WarnOnUnknown, WorkerVacuityKind.None, "fail", "warning")]
    [TestCase(WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.RequireProven, WorkerVacuityKind.None, "fail", "error")]
    public void ClaimPresentationPreservesPolicyVacuityAndEvidence(
        WorkerClaimOutcome outcome, WorkerVerifyPolicy policy, WorkerVacuityKind vacuity,
        string kind, string level)
    {
        var location = new WorkerSourceLocation { Path = "Subject.cs", Start = 0, Length = 1, Line = 2, Column = 3 };
        var claim = new WorkerClaimManifestEntry
        {
            ClaimId = "claim",
            CallableId = "C.M",
            Kind = WorkerClaimKind.Postcondition,
            Evidence = WorkerClaimEvidence.DirectClause,
            Location = location
        };
        var manifest = new WorkerClaimManifest
        {
            Claims = [claim],
            Callables = [new WorkerCallableManifestEntry { CallableId = "C.M", ClaimIds = ["claim"], Location = location }]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var result = new WorkerClaimResult
        {
            ClaimId = "claim",
            Outcome = outcome,
            Vacuity = vacuity,
            Reason = outcome == WorkerClaimOutcome.Unknown ? WorkerClaimReason.UnsupportedExpression : WorkerClaimReason.None,
            ProofCore = outcome == WorkerClaimOutcome.Proven ? ["il-summary:Lib.M"] : []
        };
        if (outcome == WorkerClaimOutcome.Refuted)
        {
            result.EffectWitness = new WorkerEffectViolationWitness
            {
                Kind = "write",
                Detail = "observable state",
                Location = new WorkerSourceLocation
                {
                    Path = "Witness.cs",
                    Line = 7,
                    Column = 8
                }
            };
        }
        using var document = JsonDocument.Parse(SarifProjection.Serialize(
            new WorkerVerifyRequest { VerifyPolicy = policy },
            new WorkerVerifyResponse { Manifest = manifest, ClaimResults = [result] }, "/source"));
        var projected = document.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(projected.GetProperty("kind").GetString(), Is.EqualTo(kind));
            Assert.That(projected.GetProperty("level").GetString(), Is.EqualTo(level));
            Assert.That(projected.GetProperty("partialFingerprints").GetProperty("sharpProofSemanticId/v1").GetString(), Is.EqualTo("claim"));
            var message = projected.GetProperty("message").GetProperty("text").GetString();
            if (outcome == WorkerClaimOutcome.Proven)
            {
                Assert.That(message, Does.Contain("compile-time referenced binary is the runtime binary"));
            }
            if (vacuity != WorkerVacuityKind.None)
            {
                Assert.That(message, Does.Contain("vacuous: " + vacuity));
            }
            if (result.EffectWitness != null)
            {
                Assert.That(message, Does.Contain("observable state at Witness.cs:7:8"));
                Assert.That(projected.GetProperty("locations")[0].GetProperty("physicalLocation")
                    .GetProperty("region").GetProperty("startLine").GetInt32(), Is.EqualTo(7));
            }
        }
    }

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
