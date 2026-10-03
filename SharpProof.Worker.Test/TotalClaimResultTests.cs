using System.Collections.Immutable;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class TotalClaimResultTests
{
    [TestCase("", "\"\"")]
    [TestCase("   ", "\"   \"")]
    [TestCase("text", "\"text\"")]
    [TestCase("\n\t\0", "\"\\u000a\\u0009\\u0000\"")]
    [TestCase("\"\\", "\"\\\"\\\\\"")]
    [TestCase("\ud83d\ude00", "\"\\ud83d\\ude00\"")]
    public void StringDisplayPreservesEveryValueThroughTheWire(string value, string expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var formatted = WorkerProjections.FormatTotalValue(factory.CreateStringValue(value));
        Assert.That(formatted.Kind, Is.EqualTo("String"));
        Assert.That(formatted.Value, Is.EqualTo(expected));
        Assert.That(JsonSerializer.Deserialize<string>(formatted.Value), Is.EqualTo(value));
        var model = new WorkerModelValue { Variable = "parameter:0", Kind = formatted.Kind, Value = formatted.Value };
        Assert.That(WorkerProtocolJson.AreValidModel([model]), Is.True);
        var roundTrip = JsonSerializer.Deserialize<WorkerModelValue>(JsonSerializer.Serialize(model, WorkerProtocolJson.Options), WorkerProtocolJson.Options)!;
        Assert.That(roundTrip.Value, Is.EqualTo(expected));
    }

    [TestCase("full-ulong")]
    [TestCase("empty-string")]
    [TestCase("contradictory")]
    [TestCase("no-normal-return")]
    [TestCase("unsafe-clause")]
    [TestCase("bounded-search")]
    public async Task KernelEvidenceProjectsIntoAValidatedWorkerResponse(string scenario)
    {
        var source = scenario switch
        {
            "full-ulong" => """
                using SharpProof.Attributes;
                public static class Subject { public static ulong Target(ulong x) {
                    Contract.Requires(x == ulong.MaxValue); Contract.Ensures(Contract.Result<ulong>() == 0UL); return x;
                } }
                """,
            "empty-string" => """
                using SharpProof.Attributes;
                public static class Subject { public static string Target(string x) {
                    Contract.Requires(x != null && x.Length == 0); Contract.Ensures(false); return x;
                } }
                """,
            "contradictory" => """
                using SharpProof.Attributes;
                public static class Subject { public static int Target(int x) {
                    Contract.Requires(x > 0 && x < 0); Contract.Ensures(false); return x;
                } }
                """,
            "no-normal-return" => """
                using SharpProof.Attributes;
                public static class Subject { public static int Target(int x) {
                    Contract.Ensures(false); throw null!;
                } }
                """,
            "bounded-search" => WorkerVcLoopTests.BeyondSearchSource.Replace("Contract.Ensures(Contract.Result<int>() == x);", "", StringComparison.Ordinal),
            _ => WorkerVcCheckedArithmeticTests.UnsafeClauseSource
        };
        using var project = new ShadowTestProject(source);
        var checks = await Checks(project);
        var result = CallableClaimResultAssembler.FromTotal(project.Snapshot.Callables.Single(), checks.Values.Single());
        if (scenario is "full-ulong" or "empty-string")
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(result.Model.Single().Variable, Is.EqualTo("parameter:0"));
            Assert.That(result.Model.Single().Value, Is.EqualTo(scenario == "full-ulong" ? "18446744073709551615" : "\"\""));
        }
        else if (scenario is "unsafe-clause" or "bounded-search")
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(scenario == "unsafe-clause"
                ? WorkerClaimReason.PostconditionMayBeUndefined : WorkerClaimReason.SolverIncomplete));
        }
        else
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Vacuity, Is.EqualTo(scenario == "contradictory"
                ? WorkerVacuityKind.ContradictoryPreconditions : WorkerVacuityKind.NoModeledNormalReturn));
        }
        var preparation = project.Snapshot.Callables.Single();
        var coverageReason = WorkerResultAssembler.ProjectCallableReasons([result]).Reason;
        var callable = new WorkerCallableResult
        {
            CallableId = preparation.Entry.CallableId,
            Coverage = coverageReason == WorkerCallableCoverageReason.None ? WorkerCallableCoverage.Complete : WorkerCallableCoverage.Incomplete,
            Reason = coverageReason,
            Assumptions = preparation.Entry.Assumptions
        };
        Assert.That(WorkerResultAssembler.TryProjectRunState([callable], [result], [], out var runStatus, out var failureReason), Is.True);
        var response = WorkerResultAssembler.Create(project.Bind().InputHash, project.Snapshot.CompilerManifest.Manifest,
            runStatus, failureReason, [callable],
            [result], project.Request.Budgets, WorkerCacheStatus.Disabled, 0);
        var validation = WorkerProtocolJson.Validate(response, response.InputHash, response.Manifest);
        Assert.That(validation.IsValid, Is.True, string.Join(", ", validation.Errors.Select(error => error.Code)));
        var json = WorkerProtocolJson.SerializeResponse(response);
        Assert.That(json, Is.Not.Empty);
    }

    [TestCase("missing-model")]
    [TestCase("foreign-assumption")]
    [TestCase("wrong-type")]
    public async Task MalformedProjectionCannotPublishRefutation(string fault)
    {
        using var project = new ShadowTestProject(CompilerTotalCallableArtifactTests.DiamondSource);
        var checks = await Checks(project);
        var check = checks.Values.Single(item => !item.Evidence.EntryModel.IsEmpty);
        var total = project.Snapshot.Callables.Single().Total!;
        var malformed = fault switch
        {
            "foreign-assumption" => check with { Assumptions = [new WorkerAssumptionEvidence { Id = "foreign", Kind = WorkerAssumptionKind.Precondition }] },
            "wrong-type" => check with { Evidence = check.Evidence with { EntryModel = check.Evidence.EntryModel.SetItem(total.Parameters[0].Entry, total.Program.Factory.CreateBooleanValue(false)) } },
            _ => check with { Evidence = check.Evidence with { EntryModel = ImmutableDictionary<IrVarId, IrValue>.Empty } }
        };
        var result = CallableClaimResultAssembler.FromTotal(project.Snapshot.Callables.Single(), malformed);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.MalformedBackendResult));
        Assert.That(result.Model, Is.Empty);
    }

    [TestCase("unchecked")]
    [TestCase("unenrolled")]
    [TestCase("bad-reason")]
    public async Task IncompletePublicationCannotPublishAProof(string fault)
    {
        using var project = new ShadowTestProject(ShadowTestProject.IdentitySource);
        var check = (await Checks(project)).Values.Single();
        var malformed = fault switch
        {
            "unenrolled" => check with { Enrolled = false },
            "bad-reason" => check with { Evidence = check.Evidence with { Reason = WorkerClaimReason.ResourceLimit } },
            _ => check with { Checked = false }
        };
        var result = CallableClaimResultAssembler.FromTotal(project.Snapshot.Callables.Single(), malformed);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.Assumptions.Any(assumption => assumption.Used), Is.False);
    }

    [Test]
    public async Task ForeignClaimCannotBeProjectedIntoAnotherCallable()
    {
        using var project = new ShadowTestProject(ShadowTestProject.IdentitySource);
        var check = (await Checks(project)).Values.Single();
        Assert.Throws<ArgumentException>(new Action(() => CallableClaimResultAssembler.FromTotal(
            project.Snapshot.Callables.Single(), check with { ClaimId = "foreign" })));
    }

    private static async Task<Dictionary<string, TotalCallableClaimCheck>> Checks(ShadowTestProject project)
    {
        var checks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        await TotalCallableVerifier.VerifyAsync(project.Snapshot.Callables.Single(), project.Request.Budgets,
            check => checks[check.ClaimId] = check, CancellationToken.None);
        return checks;
    }
}
