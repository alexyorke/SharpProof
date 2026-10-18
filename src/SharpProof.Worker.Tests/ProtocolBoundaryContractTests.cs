using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ProtocolBoundaryContractTests
{
    [Test]
    public void BoundedStreamPreservesReadOnlyContractAndOwnsInput()
    {
        using var input = new MemoryStream([42]);
        using var bounded = new BoundedReadStream(input, 1, "payload too large");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bounded.CanRead, Is.True);
            Assert.That(bounded.CanSeek, Is.False);
            Assert.That(bounded.CanWrite, Is.False);
            Assert.Throws<NotSupportedException>((Action)(() => _ = bounded.Length));
            Assert.Throws<NotSupportedException>((Action)(() => _ = bounded.Position));
            Assert.Throws<NotSupportedException>((Action)(() => bounded.Position = 0));
            Assert.Throws<NotSupportedException>((Action)(() => bounded.Seek(0, SeekOrigin.Begin)));
            Assert.Throws<NotSupportedException>((Action)(() => bounded.SetLength(0)));
            Assert.Throws<NotSupportedException>((Action)(() => bounded.Write([1], 0, 1)));
        }
        bounded.Flush();
        Assert.That(bounded.ReadByte(), Is.EqualTo(42));
        Assert.That(bounded.ReadByte(), Is.EqualTo(-1));
        bounded.Dispose();
        Assert.That(input.CanRead, Is.False);
    }

    [Test]
    public void BoundedStreamRejectsNegativeLimitAndStopsAtEarlyEof()
    {
        using var input = new MemoryStream();
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
        {
            using var invalid = new BoundedReadStream(input, -1, "payload too large");
        }))!.ParamName,
            Is.EqualTo("maximumBytes"));
        using var bounded = new BoundedReadStream(input, 1, "payload too large");
        Assert.That(bounded.ReadByte(), Is.EqualTo(-1));
    }

    [TestCase(WorkerSelectedFeature.Unspecified)]
    [TestCase(WorkerSelectedFeature.Contracts)]
    [TestCase(WorkerSelectedFeature.Effects)]
    public void ManifestCanonicalizationSortsFeatureNames(WorkerSelectedFeature feature)
    {
        var manifest = Manifest();
        manifest.Callables[0].SelectedFeatures = [feature, WorkerSelectedFeature.Contracts];
        WorkerProtocolJson.Canonicalize(manifest);
        Assert.That(manifest.Callables[0].SelectedFeatures,
            Is.EqualTo(new[] { feature, WorkerSelectedFeature.Contracts }
                .OrderBy(value => value.ToString(), StringComparer.Ordinal)));
    }

    [TestCase(WorkerSelectionReason.Unspecified)]
    [TestCase(WorkerSelectionReason.ExplicitAnnotation)]
    [TestCase(WorkerSelectionReason.DiscoveredPostcondition)]
    public void ManifestCanonicalizationSortsSelectionNames(WorkerSelectionReason reason)
    {
        var manifest = Manifest();
        manifest.Callables[0].SelectionReasons = [reason, WorkerSelectionReason.ExplicitAnnotation];
        WorkerProtocolJson.Canonicalize(manifest);
        Assert.That(manifest.Callables[0].SelectionReasons,
            Is.EqualTo(new[] { reason, WorkerSelectionReason.ExplicitAnnotation }
                .OrderBy(value => value.ToString(), StringComparer.Ordinal)));
    }

    [Test]
    public void ManifestHashOrdersEveryAssumptionKindAndRejectsUnknownKinds()
    {
        var manifest = Manifest();
        var kinds = Enum.GetValues<WorkerAssumptionKind>().Reverse().ToArray();
        manifest.Callables[0].Assumptions = kinds.Select(kind => new WorkerAssumptionEvidence
        {
            Id = "assumption." + kind,
            Kind = kind
        }).ToArray();
        WorkerProtocolJson.Canonicalize(manifest);
        Assert.That(manifest.Callables[0].Assumptions.Select(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Kind)),
            Is.EqualTo(Enum.GetValues<WorkerAssumptionKind>()));
        var hash = WorkerProtocolJson.ComputeManifestHash(manifest);
        manifest.Callables[0].Assumptions = manifest.Callables[0].Assumptions.Reverse().ToArray();
        Assert.That(WorkerProtocolJson.ComputeManifestHash(manifest), Is.EqualTo(hash));
        manifest.Callables[0].Assumptions[0].Kind = (WorkerAssumptionKind)int.MaxValue;
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => WorkerProtocolJson.ComputeManifestHash(manifest)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ManifestValidationRejectsAbsentFeatureDeclarations(bool nullArray)
    {
        var manifest = Manifest();
        manifest.Callables[0].SelectedFeatures = nullArray ? null! : [];
        Assert.That(WorkerProtocolJson.ValidateManifest(manifest).Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)),
            Does.Contain("manifest.selected_features"));
    }

    [Test]
    public void ManifestValidationRejectsReorderedClaimMembership()
    {
        var manifest = Manifest();
        manifest.Callables[0].ClaimIds = ["claim.different"];
        Assert.That(WorkerProtocolJson.ValidateManifest(manifest).Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)),
            Does.Contain("manifest.claim_membership"));
    }

    [Test]
    public void ResponseValidationRejectsMissingSummaryAgainstRequest()
    {
        var request = Request();
        var response = Response();
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var versions = response.Summary.Versions;
        response.Summary = null!;
        Assert.That(WorkerProtocolJson.ValidateForRequest(response, response.RequestHash,
            response.InputHash, response.Manifest, request, versions).Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)),
            Does.Contain("response.summary"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ResponseValidationRejectsUnknownOutcomeEnums(bool callable)
    {
        var response = Response();
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        if (callable)
        {
            response.CallableResults[0].Coverage = (WorkerCallableCoverage)int.MaxValue;
        }
        else
        {
            response.ClaimResults[0].Outcome = (WorkerClaimOutcome)int.MaxValue;
        }
        var codes = WorkerProtocolJson.Validate(response).Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)).ToArray();
        Assert.That(codes, Does.Contain(callable ? "response.callable_coverage" : "response.claim_outcome"));
        Assert.That(codes, Does.Contain(callable ? "response.callable_reason" : "response.claim_reason"));
    }

    private static WorkerVerifyRequest Request()
    {
        return new()
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = "compiler.manifest.json",
                Sha256 = WorkerProtocolVersions.EmptySha256
            }
        };
    }

    private static WorkerClaimManifest Manifest()
    {
        var location = new WorkerSourceLocation { Path = "Subject.cs", Line = 1, Column = 1, Length = 1 };
        var manifest = new WorkerClaimManifest
        {
            Callables = [new WorkerCallableManifestEntry
            {
                CallableId = "M:Subject.Check",
                SelectedFeatures = [WorkerSelectedFeature.Contracts],
                SelectionReasons = [WorkerSelectionReason.ExplicitAnnotation],
                ClaimIds = ["claim.check"],
                Location = location
            }],
            Claims = [new WorkerClaimManifestEntry
            {
                CallableId = "M:Subject.Check",
                ClaimId = "claim.check",
                Kind = WorkerClaimKind.Postcondition,
                Evidence = WorkerClaimEvidence.DirectClause,
                Location = location
            }]
        };
        WorkerProtocolJson.SealManifest(manifest);
        return manifest;
    }

    private static WorkerVerifyResponse Response()
    {
        return new()
        {
            InputHash = WorkerProtocolVersions.EmptySha256,
            Manifest = Manifest(),
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "boundary-control-worker",
                    ApiSpecVersion = "boundary-control-spec"
                }
            },
            CallableResults = [new WorkerCallableResult
            {
                CallableId = "M:Subject.Check",
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None
            }],
            ClaimResults = [new WorkerClaimResult
            {
                ClaimId = "claim.check",
                Outcome = WorkerClaimOutcome.Proven,
                Reason = WorkerClaimReason.None
            }]
        };
    }
}
