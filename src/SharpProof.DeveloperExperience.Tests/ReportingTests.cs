using NUnit.Framework;
using SharpProof.Reporting;
using SharpProof.Tool;
using SharpProof.Worker.Protocol;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
internal sealed class ReportingTests
{
    [Test]
    public void EveryEmittedUnknownReasonHasAnAction()
    {
        foreach (var reason in Enum.GetValues<WorkerClaimReason>().Where(static reason =>
        {
            global::SharpProof.Internal.InputValidation.Require(reason, nameof(reason));
            return reason is not (WorkerClaimReason.None or WorkerClaimReason.Unspecified);
        }))
        {
            var explanation = ReasonCatalog.Explain(reason);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(explanation.Description, Is.Not.Empty, reason.ToString());
                Assert.That(explanation.NextAction, Is.Not.Empty, reason.ToString());
            }
        }

        Assert.That(ReasonCatalog.Explain(WorkerClaimReason.UnsupportedExpression).Category, Is.EqualTo("Model limit"));
        Assert.That(ReasonCatalog.Explain(WorkerClaimReason.MethodTimeout).Category, Is.EqualTo("Resource limit"));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
        {
            _ = ReasonCatalog.Explain(WorkerClaimReason.None);
        }));
    }

    [Test]
    public void ComparisonCannotHideRemovedClaimsOrCallables()
    {
        var baseline = Response(WorkerClaimOutcome.Proven);
        var current = Response(WorkerClaimOutcome.Proven);
        current.Manifest.Claims = [];
        current.Manifest.Callables = [];
        current.ClaimResults = [];
        current.CallableResults = [];
        var changes = VerificationComparison.Compare(baseline, current);
        Assert.That(changes.Count(static change =>
        {
            global::SharpProof.Internal.InputValidation.Require(change, nameof(change));
            return change.IsRegression;
        }), Is.EqualTo(2));
    }

    [TestCase(WorkerClaimOutcome.Unknown)]
    [TestCase(WorkerClaimOutcome.Refuted)]
    public void LostProofIsARegression(WorkerClaimOutcome next)
    {
        global::SharpProof.Internal.InputValidation.Require(next, nameof(next));
        var changes = VerificationComparison.Compare(Response(WorkerClaimOutcome.Proven), Response(next));
        Assert.That(changes.Any(static change =>
        {
            global::SharpProof.Internal.InputValidation.Require(change, nameof(change));
            return change.IsRegression;
        }), Is.True);
    }

    [Test]
    public void VacuityAndEntryAssumptionsNeedReviewEvenWhenOutcomeRemainsProven()
    {
        var baseline = Response(WorkerClaimOutcome.Proven);
        var current = Response(WorkerClaimOutcome.Proven);
        current.ClaimResults = [current.ClaimResults[0] with { Vacuity = WorkerVacuityKind.ContradictoryPreconditions }];
        current.CallableResults[0].Assumptions = [new()
        {
            Id = "requires:new",
            Kind = WorkerAssumptionKind.Precondition
        }

        ];
        var changes = VerificationComparison.Compare(baseline, current);
        Assert.That(changes.Count(static change =>
        {
            global::SharpProof.Internal.InputValidation.Require(change, nameof(change));
            return change.IsRegression;
        }), Is.EqualTo(2));
    }

    [Test]
    public void IdenticalReportsHaveNoChanges()
    {
        Assert.That(VerificationComparison.Compare(Response(WorkerClaimOutcome.Proven), Response(WorkerClaimOutcome.Proven)), Is.Empty);
    }

    [Test]
    public void SummaryDisclosesVacuityAndIncompleteSelection()
    {
        var response = Response(WorkerClaimOutcome.Unknown);
        response.CallableResults = [response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }];
        var text = VerificationReport.Summary(response);
        Assert.That(text, Does.Contain("1 Unknown").And.Contain("1 incomplete").And.Contain("Unselected methods are not proved"));
    }

    [Test]
    public void CounterexampleUsesSourceParameterNamesWithoutChangingModelIdentity()
    {
        var response = Response(WorkerClaimOutcome.Refuted);
        response.ClaimResults = [response.ClaimResults[0] with { Presentation = new() { Name = "Example.Next", ParameterNames = ["value"] } }];
        response.ClaimResults[0].Model = [new() { Variable = "parameter:0", Kind = "Integer", Value = "0" }];
        using var writer = new StringWriter();
        VerificationReport.Write(writer, response);
        Assert.That(writer.ToString(), Does.Contain("Example.Next(value)").And.Contain("Counterexample value: 0"));
        Assert.That(response.ClaimResults[0].Model[0].Variable, Is.EqualTo("parameter:0"));
    }

    [Test]
    public async Task InvalidCommandsFailBeforeReadingEvidence()
    {
        string[][] cases = [["explain", "--result"], ["explain", "--result", "a", "--result", "b"], ["explain", "--typo", "a"]];
        foreach (var arguments in cases)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.That(await DeveloperCommands.RunAsync(arguments, output, error, TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
            Assert.That(error.ToString(), Does.StartWith("SharpProof:"));
        }
    }

    internal static WorkerVerifyResponse Response(WorkerClaimOutcome outcome)
    {
        global::SharpProof.Internal.InputValidation.Require(outcome, nameof(outcome));
        return new()
        {
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            Manifest = new()
            {
                Callables = [new()
                {
                    CallableId = "M:Example.Next(System.Int32)",
                    ClaimIds = ["claim:1"]
                }

                ],
                Claims = [new()
                {
                    ClaimId = "claim:1",
                    CallableId = "M:Example.Next(System.Int32)",
                    Kind = WorkerClaimKind.Postcondition
                }

                ]
            },
            ClaimResults = [new()
            {
                ClaimId = "claim:1",
                Outcome = outcome,
                Reason = outcome == WorkerClaimOutcome.Unknown ? WorkerClaimReason.UnsupportedExpression : WorkerClaimReason.None
            }

            ],
            CallableResults = [new()
            {
                CallableId = "M:Example.Next(System.Int32)",
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None
            }

            ]
        };
    }
}
