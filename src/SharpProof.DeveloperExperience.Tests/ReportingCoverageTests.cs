extern alias ToolAssembly;
using System.Collections.Immutable;
using System.Globalization;
using ReportingInputValidation = ToolAssembly::SharpProof.Internal.InputValidation;
using NUnit.Framework;
using SharpProof.Reporting;
using SharpProof.Worker.Protocol;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
internal sealed class ReportingCoverageTests
{
    [Test]
    public void SharedInputBoundaryDistinguishesMissingNamesFromNullValues()
    {
        ReportingInputValidation.Require(0, "number");
        ReportingInputValidation.Require(false, "flag");
        ReportingInputValidation.Require(string.Empty, "text");
        var missingValue = Assert.Throws<ArgumentNullException>(new Action(() => ReportingInputValidation.Require(null, "value")));
        Assert.That(missingValue!.ParamName, Is.EqualTo("value"));
        foreach (var name in new string?[] { null, string.Empty, " " })
        {
            var missingName = Assert.Throws<ArgumentException>(new Action(() => ReportingInputValidation.Require("present", name!)));
            Assert.That(missingName!.ParamName, Is.EqualTo("parameterName"));
        }
        var bothMissing = Assert.Throws<ArgumentException>(new Action(() => ReportingInputValidation.Require(null, null!)));
        Assert.That(bothMissing!.ParamName, Is.EqualTo("parameterName"));
    }
    [Test]
    public void ReportingBoundariesRequireTheirArguments()
    {
        var response = ReportingTests.Response(WorkerClaimOutcome.Proven);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = VerificationReport.Summary(null!); }));
        Assert.Throws<ArgumentNullException>(new Action(() => VerificationReport.Write(null!, response)));
        Assert.Throws<ArgumentNullException>(new Action(() => VerificationReport.Write(writer, null!)));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = VerificationReport.VariableName(null!, "parameter:0"); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = VerificationReport.VariableName(response.ClaimResults[0], null!); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = VerificationComparison.Compare(null!, response); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = VerificationComparison.Compare(response, null!); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new VerificationChange(null!, "claim", false); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new VerificationChange("change", null!, false); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new ReasonExplanation(null!, "description", "action"); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new ReasonExplanation("category", null!, "action"); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new ReasonExplanation("category", "description", null!); }));
    }

    [TestCase(WorkerCallableCoverageReason.UnsupportedCallable)]
    [TestCase(WorkerCallableCoverageReason.UnsupportedContract)]
    [TestCase(WorkerCallableCoverageReason.SemanticUnknown)]
    [TestCase(WorkerCallableCoverageReason.MissingClaimResult)]
    [TestCase(WorkerCallableCoverageReason.MethodTimeout)]
    [TestCase(WorkerCallableCoverageReason.ProjectTimeout)]
    [TestCase(WorkerCallableCoverageReason.Canceled)]
    [TestCase(WorkerCallableCoverageReason.InfrastructureFailure)]
    public void EveryIncompleteCallableReasonHasActionableGuidance(WorkerCallableCoverageReason reason)
    {
        var explanation = ReasonCatalog.Explain(reason);
        Assert.That(explanation.Category, Is.Not.Empty);
        Assert.That(explanation.Description, Is.Not.Empty);
        Assert.That(explanation.NextAction, Is.Not.Empty);
    }

    [TestCase(WorkerCallableCoverageReason.None)]
    [TestCase(WorkerCallableCoverageReason.Unspecified)]
    [TestCase((WorkerCallableCoverageReason)999)]
    public void CompleteOrUnknownCoverageEnumsCannotGetIncompleteGuidance(WorkerCallableCoverageReason reason)
    {
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = ReasonCatalog.Explain(reason); }));
    }

    [TestCase(WorkerClaimReason.Unspecified)]
    [TestCase((WorkerClaimReason)999)]
    public void InvalidClaimReasonsCannotGetUnknownGuidance(WorkerClaimReason reason)
    {
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = ReasonCatalog.Explain(reason); }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void IncompatibleProtocolOrSpecificationIdentityStopsComparison(bool protocol)
    {
        var baseline = ReportingTests.Response(WorkerClaimOutcome.Proven);
        var current = ReportingTests.Response(WorkerClaimOutcome.Proven);
        current = protocol ? current with { ProtocolVersion = "incompatible" } : current with
        {
            Summary = current.Summary with { Versions = current.Summary.Versions with { ApiSpecContentSha256 = new string('a', 64) } }
        };
        Assert.Throws<InvalidDataException>(new Action(() => { _ = VerificationComparison.Compare(baseline, current); }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AClaimIdentifierCannotChangeKindOrEffectContract(bool claimKind)
    {
        var baseline = ReportingTests.Response(WorkerClaimOutcome.Proven);
        var current = ReportingTests.Response(WorkerClaimOutcome.Proven);
        current.Manifest.Claims = [claimKind ? current.Manifest.Claims[0] with { Kind = WorkerClaimKind.Effect } : current.Manifest.Claims[0] with { EffectContractKind = WorkerEffectContractKind.EnforcePure }];
        Assert.Throws<InvalidDataException>(new Action(() => { _ = VerificationComparison.Compare(baseline, current); }));
    }

    [TestCase(WorkerClaimOutcome.Proven, WorkerClaimOutcome.Proven, false)]
    [TestCase(WorkerClaimOutcome.Proven, WorkerClaimOutcome.Unknown, true)]
    [TestCase(WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted, true)]
    [TestCase(WorkerClaimOutcome.Unknown, WorkerClaimOutcome.Proven, false)]
    [TestCase(WorkerClaimOutcome.Unknown, WorkerClaimOutcome.Unknown, false)]
    [TestCase(WorkerClaimOutcome.Unknown, WorkerClaimOutcome.Refuted, true)]
    [TestCase(WorkerClaimOutcome.Refuted, WorkerClaimOutcome.Proven, false)]
    [TestCase(WorkerClaimOutcome.Refuted, WorkerClaimOutcome.Unknown, false)]
    [TestCase(WorkerClaimOutcome.Refuted, WorkerClaimOutcome.Refuted, false)]
    public void OutcomeChangesHaveExplicitRegressionSemantics(WorkerClaimOutcome previous, WorkerClaimOutcome next, bool regression)
    {
        var changes = VerificationComparison.Compare(ReportingTests.Response(previous), ReportingTests.Response(next));
        Assert.That(changes.Count, Is.EqualTo(previous == next ? 0 : 1));
        Assert.That(changes.Any(static change =>
        {
            ArgumentNullException.ThrowIfNull(change);
            return change.IsRegression;
        }), Is.EqualTo(regression));
        if (previous != next)
        {
            Assert.That(changes[0].Kind, Is.EqualTo(previous + " -> " + next));
            Assert.That(changes[0].Identity, Is.EqualTo("claim:1"));
        }
    }

    [TestCase(WorkerClaimOutcome.Proven, WorkerVacuityKind.None, false)]
    [TestCase(WorkerClaimOutcome.Unknown, WorkerVacuityKind.None, true)]
    [TestCase(WorkerClaimOutcome.Refuted, WorkerVacuityKind.None, true)]
    [TestCase(WorkerClaimOutcome.Proven, WorkerVacuityKind.NoModeledNormalReturn, true)]
    public void AddedClaimDistinguishesAnActualProofFromUnresolvedOrVacuousEvidence(WorkerClaimOutcome outcome, WorkerVacuityKind vacuity, bool regression)
    {
        var current = ReportingTests.Response(outcome);
        current.ClaimResults = [current.ClaimResults[0] with { Vacuity = vacuity }];
        var changes = VerificationComparison.Compare(EmptyResponse(), current);
        Assert.That(changes, Has.Count.EqualTo(1));
        Assert.That(changes[0].Kind, Does.StartWith("Added "));
        Assert.That(changes[0].IsRegression, Is.EqualTo(regression));
    }

    [TestCase(WorkerAssumptionKind.Precondition, true)]
    [TestCase(WorkerAssumptionKind.UserAssume, true)]
    [TestCase(WorkerAssumptionKind.TrustedBoundary, true)]
    [TestCase(WorkerAssumptionKind.ApiSpecification, true)]
    [TestCase(WorkerAssumptionKind.SourceDomain, false)]
    [TestCase(WorkerAssumptionKind.NormalCompletion, false)]
    public void ComparisonReviewsOnlyEntryAndExternalAssumptionKinds(WorkerAssumptionKind kind, bool reviewed)
    {
        var baseline = ReportingTests.Response(WorkerClaimOutcome.Proven);
        var current = ReportingTests.Response(WorkerClaimOutcome.Proven);
        current.CallableResults[0].Assumptions = [new() { Id = "entry:added", Kind = kind }];
        var changes = VerificationComparison.Compare(baseline, current);
        Assert.That(changes.Count, Is.EqualTo(reviewed ? 1 : 0));
        Assert.That(changes.Any(static change =>
        {
            ArgumentNullException.ThrowIfNull(change);
            return change.IsRegression;
        }), Is.EqualTo(reviewed));
    }

    [Test]
    public void ExistingAssumptionsAreStableAndNewCallableAssumptionsAreChanges()
    {
        var baseline = ReportingTests.Response(WorkerClaimOutcome.Proven);
        var current = ReportingTests.Response(WorkerClaimOutcome.Proven);
        baseline.CallableResults[0].Assumptions = [new() { Id = "entry", Kind = WorkerAssumptionKind.UserAssume }];
        current.CallableResults[0].Assumptions = [new() { Id = "entry", Kind = WorkerAssumptionKind.UserAssume }];
        Assert.That(VerificationComparison.Compare(baseline, current), Is.Empty);
        var changes = VerificationComparison.Compare(EmptyResponse(), current);
        Assert.That(changes, Has.Count.EqualTo(2));
        Assert.That(changes.All(static change =>
        {
            ArgumentNullException.ThrowIfNull(change);
            return !change.IsRegression;
        }), Is.True);
    }

    [Test]
    public void NewIncompleteCoverageIsReportedOnceAndVacuityChangesAreSorted()
    {
        var baseline = ReportingTests.Response(WorkerClaimOutcome.Proven);
        var current = ReportingTests.Response(WorkerClaimOutcome.Unknown);
        current.CallableResults = [current.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete, Reason = WorkerCallableCoverageReason.SemanticUnknown }];
        current.ClaimResults = [current.ClaimResults[0] with { Vacuity = WorkerVacuityKind.NoModeledNormalReturn }];
        var changes = VerificationComparison.Compare(baseline, current);
        Assert.That(changes.Select(static change =>
        {
            ArgumentNullException.ThrowIfNull(change);
            return change.Kind;
        }), Is.EqualTo(ImmutableArray.Create("New incomplete coverage: SemanticUnknown", "New vacuity: NoModeledNormalReturn", "Proven -> Unknown")));
        Assert.That(VerificationComparison.Compare(current, current), Is.Empty);
        Assert.That(VerificationComparison.Compare(EmptyResponse(), current).Any(static change =>
        {
            ArgumentNullException.ThrowIfNull(change);
            return change.Kind.StartsWith("New incomplete", StringComparison.Ordinal) && change.IsRegression;
        }), Is.True);
    }

    [TestCase(WorkerRunStatus.TimedOut)]
    [TestCase(WorkerRunStatus.Canceled)]
    [TestCase(WorkerRunStatus.Failed)]
    public void LeavingCompleteRunStateIsARegression(WorkerRunStatus status)
    {
        var baseline = ReportingTests.Response(WorkerClaimOutcome.Proven);
        var current = ReportingTests.Response(WorkerClaimOutcome.Proven) with { RunStatus = status, FailureReason = WorkerRunFailureReason.InfrastructureFailure };
        var changes = VerificationComparison.Compare(baseline, current);
        Assert.That(changes, Has.Count.EqualTo(1));
        Assert.That(changes[0].Kind, Is.EqualTo("Run became " + status));
        Assert.That(changes[0].IsRegression, Is.True);
        Assert.That(VerificationComparison.Compare(current, baseline), Is.Empty);
    }

    [Test]
    public void SourceMovementAndDescriptiveNamesDoNotChangeProofIdentity()
    {
        var baseline = ReportingTests.Response(WorkerClaimOutcome.Proven);
        var current = ReportingTests.Response(WorkerClaimOutcome.Proven);
        current.Manifest.Claims = [current.Manifest.Claims[0] with { Location = new() { Path = "Moved.cs", Line = 30, Column = 2 }, Ordinal = 12 }];
        current.ClaimResults = [current.ClaimResults[0] with { Presentation = new() { Name = "NewDisplayName", ParameterNames = ["renamed"] } }];
        Assert.That(VerificationComparison.Compare(baseline, current), Is.Empty);
    }

    [Test]
    public void SummaryCountsOutcomesVacuityCoverageAndDistinctDeclaredTrust()
    {
        var response = ReportingTests.Response(WorkerClaimOutcome.Proven);
        response.ClaimResults = [response.ClaimResults[0] with { Vacuity = WorkerVacuityKind.ContradictoryPreconditions }, new() { ClaimId = "refuted", Outcome = WorkerClaimOutcome.Refuted }, new() { ClaimId = "unknown", Outcome = WorkerClaimOutcome.Unknown }];
        response.CallableResults[0].Assumptions = [new() { Id = "same", Kind = WorkerAssumptionKind.UserAssume }, new() { Id = "same", Kind = WorkerAssumptionKind.TrustedBoundary }, new() { Id = "same", Kind = WorkerAssumptionKind.UserAssume }, new() { Id = "entry", Kind = WorkerAssumptionKind.Precondition }];
        response.CallableResults = [response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }];
        var text = VerificationReport.Summary(response);
        Assert.That(text, Does.Contain("1 Proven, 1 Refuted, 1 Unknown").And.Contain("1 incomplete callables, 1 vacuous claims, 2 declared user/trust assumptions"));
    }

    [Test]
    public void MissingClaimFilterProducesNoPartialReport()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(new Action(() => VerificationReport.Write(writer, ReportingTests.Response(WorkerClaimOutcome.Proven), "missing")));
        Assert.That(writer.ToString(), Is.Empty);
    }

    [Test]
    public void ClaimFilterKeepsOnlyOwnedClaimAndIncompleteCallableDetails()
    {
        var response = ReportingTests.Response(WorkerClaimOutcome.Unknown);
        response.Manifest.Claims = [response.Manifest.Claims[0], response.Manifest.Claims[0] with { ClaimId = "other", CallableId = "M:Other.Run", Location = new() { Path = "Other.cs", Line = 9, Column = 2 } }];
        response.Manifest.Callables = [response.Manifest.Callables[0], response.Manifest.Callables[0] with { CallableId = "M:Other.Run", ClaimIds = ["other"] }];
        response.ClaimResults = [response.ClaimResults[0], response.ClaimResults[0] with { ClaimId = "other" }];
        response.CallableResults = [response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete, Reason = WorkerCallableCoverageReason.SemanticUnknown }, response.CallableResults[0] with { CallableId = "M:Other.Run", Coverage = WorkerCallableCoverage.Incomplete, Reason = WorkerCallableCoverageReason.SemanticUnknown }];
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        VerificationReport.Write(writer, response, "claim:1");
        Assert.That(writer.ToString(), Does.Contain("claim:1").And.Contain("Incomplete M:Example.Next").And.Not.Contain("Other.cs").And.Not.Contain("Incomplete M:Other.Run"));
    }

    [TestCase("spm1:structural", "spm1:structural")]
    [TestCase("M:Example.Convert~System.Int32", "Example.Convert -> System.Int32")]
    public void CapturedCallableIdentityHasAnHonestFallbackDisplay(string callableId, string expected)
    {
        ArgumentNullException.ThrowIfNull(callableId);
        ArgumentNullException.ThrowIfNull(expected);
        var response = ReportingTests.Response(WorkerClaimOutcome.Proven);
        response.Manifest.Claims = [response.Manifest.Claims[0] with { CallableId = callableId }];
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        VerificationReport.Write(writer, response);
        Assert.That(writer.ToString(), Does.Contain(expected));
    }

    [Test]
    public void FailureExplanationsExposeCapturedOriginsDeclaredAssumptionsAndRunErrors()
    {
        var response = ReportingTests.Response(WorkerClaimOutcome.Unknown) with { RunStatus = WorkerRunStatus.Failed, FailureReason = WorkerRunFailureReason.InfrastructureFailure };
        response.ClaimResults = [response.ClaimResults[0] with
        {
            Reason = WorkerClaimReason.InfrastructureFailure,
            Diagnostic = new() { Code = "lowering.TEST", Location = new() { Path = "Origin.cs", Line = 3, Column = 4 } },
            Assumptions = [new() { Id = "declared", Kind = WorkerAssumptionKind.UserAssume }]
        }];
        response.CallableResults = [response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete, Reason = WorkerCallableCoverageReason.InfrastructureFailure }];
        response.Errors = [new() { Code = "worker.infrastructure", Message = "Retained failure" }];
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        VerificationReport.Write(writer, response);
        Assert.That(writer.ToString(), Does.Contain("Failed (InfrastructureFailure)").And.Contain("Origin: CompilerLowering/lowering.TEST at Origin.cs(3,4)").And.Contain("declared (declared)").And.Contain("Run error worker.infrastructure: Retained failure"));
    }

    [Test]
    public void RefutedEffectsExposeTheirDefiniteViolationWitness()
    {
        var response = ReportingTests.Response(WorkerClaimOutcome.Refuted);
        response.Manifest.Claims = [response.Manifest.Claims[0] with { Kind = WorkerClaimKind.Effect, Evidence = WorkerClaimEvidence.Attribute, EffectContractKind = WorkerEffectContractKind.EnforcePure }];
        response.ClaimResults = [response.ClaimResults[0] with
        {
            EffectCertainty = WorkerEffectEvidenceCertainty.DefiniteViolation,
            EffectWitness = new() { Kind = "Write", Detail = "Writes state", Effects = WorkerEffectSet.WritesStaticState, Location = new() { Path = "Witness.cs", Line = 5, Column = 6 } }
        }];
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        VerificationReport.Write(writer, response);
        Assert.That(writer.ToString(), Does.Contain("Effect evidence: DefiniteViolation").And.Contain("Violation witness Write: Writes state at Witness.cs(5,6)"));
    }

    [Test]
    public void VacuousProofDisclosesItsUsedEntryAssumption()
    {
        var response = ReportingTests.Response(WorkerClaimOutcome.Proven);
        response.ClaimResults = [response.ClaimResults[0] with
        {
            Vacuity = WorkerVacuityKind.ContradictoryPreconditions,
            ProofCore = ["requires:0"],
            Assumptions = [new() { Id = "entry", Kind = WorkerAssumptionKind.Precondition, Used = true }]
        }];
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        VerificationReport.Write(writer, response);
        Assert.That(writer.ToString(), Does.Contain("entry (used)").And.Contain("Review vacuity: ContradictoryPreconditions"));
    }
    [Test]
    public void EffectEvidenceWithoutWitnessStillShowsItsCertainty()
    {
        var response = ReportingTests.Response(WorkerClaimOutcome.Proven);
        response.Manifest.Claims = [response.Manifest.Claims[0] with { Kind = WorkerClaimKind.Effect }];
        response.ClaimResults = [response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.CompleteMayEffectSummary }];
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        VerificationReport.Write(writer, response);
        Assert.That(writer.ToString(), Does.Contain("Effect evidence: CompleteMayEffectSummary").And.Not.Contain("Violation witness"));
    }

    [Test]
    public void IncompleteCallableWithoutClaimsStillGetsAnExplanation()
    {
        var response = ReportingTests.Response(WorkerClaimOutcome.Proven);
        response.Manifest.Claims = [];
        response.ClaimResults = [];
        response.CallableResults = [response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete, Reason = WorkerCallableCoverageReason.UnsupportedCallable }];
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        VerificationReport.Write(writer, response);
        Assert.That(writer.ToString(), Does.Contain("Incomplete M:Example.Next").And.Contain("outside the verifier").And.Contain("Next:").And.Contain(ReasonExplanation.Documentation));
    }

    [TestCase("parameter:0", "first")]
    [TestCase("parameter:1", "second")]
    [TestCase("parameter:2", "parameter:2")]
    [TestCase("parameter:-1", "parameter:-1")]
    [TestCase("parameter:2147483648", "parameter:2147483648")]
    [TestCase("parameter:", "parameter:")]
    [TestCase("parameter:abc", "parameter:abc")]
    [TestCase("local:0", "local:0")]
    public void ModelVariableNamesUseOnlyAdmittedParameterOrdinals(string variable, string expected)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(expected);
        var result = new WorkerClaimResult { Presentation = new() { Name = "Target", ParameterNames = ["first", "second"] } };
        Assert.That(VerificationReport.VariableName(result, variable), Is.EqualTo(expected));
        Assert.That(VerificationReport.VariableName(new WorkerClaimResult(), variable), Is.EqualTo(variable));
    }

    private static WorkerVerifyResponse EmptyResponse()
    {
        var response = ReportingTests.Response(WorkerClaimOutcome.Proven);
        response.Manifest.Claims = [];
        response.Manifest.Callables = [];
        response.ClaimResults = [];
        response.CallableResults = [];
        return response;
    }
}
