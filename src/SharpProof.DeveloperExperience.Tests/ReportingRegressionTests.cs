using System.Globalization;
using NUnit.Framework;
using SharpProof.Reporting;
using SharpProof.Tool;
using SharpProof.Worker.Protocol;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
internal sealed class ReportingRegressionTests
{
    [Test]
    public void ReusingClaimIdentifierForAnotherOwnerIsRejected()
    {
        var baseline = ReportingTests.Response(WorkerClaimOutcome.Proven);
        var current = ReportingTests.Response(WorkerClaimOutcome.Proven);
        current.Manifest.Claims = [current.Manifest.Claims[0] with { CallableId = "M:Other.Next(System.Int32)" }];
        Assert.Throws<InvalidDataException>(new Action(() => { _ = VerificationComparison.Compare(baseline, current); }));
    }

    [Test]
    public void ChangingAssumptionKindCannotReuseAnIdentifierToHideRegression()
    {
        var baseline = ReportingTests.Response(WorkerClaimOutcome.Proven);
        var current = ReportingTests.Response(WorkerClaimOutcome.Proven);
        baseline.CallableResults[0].Assumptions = [new() { Id = "shared", Kind = WorkerAssumptionKind.Precondition }];
        current.CallableResults[0].Assumptions = [new() { Id = "shared", Kind = WorkerAssumptionKind.TrustedBoundary }];
        Assert.That(VerificationComparison.Compare(baseline, current).Any(static change =>
        {
            ArgumentNullException.ThrowIfNull(change);
            return change.IsRegression;
        }), Is.True);
    }

    [Test]
    public void RemovingSelectedFeatureIsARegressionEvenWithSameClaims()
    {
        var baseline = ReportingTests.Response(WorkerClaimOutcome.Proven);
        var current = ReportingTests.Response(WorkerClaimOutcome.Proven);
        baseline.Manifest.Callables[0].SelectedFeatures = [WorkerSelectedFeature.Contracts, WorkerSelectedFeature.Effects];
        current.Manifest.Callables[0].SelectedFeatures = [WorkerSelectedFeature.Contracts];
        Assert.That(VerificationComparison.Compare(baseline, current).Any(static change =>
        {
            ArgumentNullException.ThrowIfNull(change);
            return change.IsRegression;
        }), Is.True);
    }

    [Test]
    public void UnconfirmedModelIsNeverLabeledCounterexample()
    {
        var response = ReportingTests.Response(WorkerClaimOutcome.Unknown);
        response.ClaimResults[0].Model = [new() { Variable = "parameter:0", Kind = "Integer", Value = "0" }];
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        VerificationReport.Write(output, response);
        Assert.That(output.ToString(), Does.Contain("Candidate model").And.Not.Contain("Counterexample"));
    }

    [Test]
    public void CanceledEvidenceReadDoesNotTouchMissingFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => { _ = EvidenceSet.ReadBounded("missing.json", 1024, cancellation.Token); }));
    }
}
