using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ActualNativeProjectionAdmissionTests
{
    [Test]
    public async Task EffectProjectionRejectsRealEntryRefutationWithoutEffectWitness()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                [EnforcePure] public static int Target(int value) { return value; }
            }
            """;
        using var project = CreateProject(source, "EffectProjectionAdmission");
        var target = project.Snapshot.Callables.Single();
        var claim = target.EffectClaims.Single();
        var actualEffect = await NativeEffectSiteVerifier.VerifyPurityAsync(target, project.Request.Budgets);
        Assert.That(actualEffect.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(actualEffect.Reason, Is.EqualTo(WorkerClaimReason.None));
        var valid = NativeEffectClaims.Project(target, claim.ClaimId, actualEffect);
        Assert.That(valid.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(valid.Reason, Is.EqualTo(WorkerClaimReason.None));
        ValidateRoundTrip(project, valid);

        var candidate = PassiveCallableArtifactAdapter.Enroll(target);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var failure), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!, project.Request.Budgets.QueryRlimit,
            project.Request.Budgets.MethodRlimit);
        var actualEntry = await solver.VerifyEntryAsync();
        Assert.That(actualEntry.Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(actualEntry.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(actualEntry.ExceptionWitness, Is.Null);
        Assert.That(actualEntry.AllocationWitness, Is.Null);
        Assert.That(actualEntry.LockWitness, Is.Null);
        Assert.That(actualEntry.WriteWitness, Is.Null);
        // Wrong semantic family at the raw projection boundary: never effect evidence.
        var rejected = NativeEffectClaims.Project(target, claim.ClaimId, actualEntry);
        Assert.That(rejected.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(rejected.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        Assert.That(rejected.ProofCore, Is.Empty);
        Assert.That(rejected.Model, Is.Empty);
        Assert.That(rejected.EffectWitness, Is.Null);
        ValidateRoundTrip(project, rejected);
    }

    [Test]
    public async Task EntryProjectionRejectsRealBodyProofWithDisjointInputCore()
    {
        // Existing source-backed sealed-input proof; the native result is not an entry contradiction.
        using var project = CreateProject(NativeAliasingBoundaryTests.DistinctSealedSource,
            "EntryProjectionAdmission");
        var target = project.Snapshot.Callables.Single();
        var candidate = PassiveCallableArtifactAdapter.Enroll(target);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var failure), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!, project.Request.Budgets.QueryRlimit,
            project.Request.Budgets.MethodRlimit);
        var actualEntry = await solver.VerifyEntryAsync();
        Assert.That(actualEntry.Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(actualEntry.Reason, Is.EqualTo(WorkerClaimReason.None));
        var requires = target.Total!.Clauses.Where(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Requires))
            .Select((clause, ordinal) => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : ((Label: "requires:" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Id: clause.AssumptionId!))).ToDictionary(row => row.Label, row => row.Id, StringComparer.Ordinal);
        var valid = TotalCallableVerifier.ProjectEntry(actualEntry, requires);
        Assert.That(valid.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Feasible));
        Assert.That(valid.Reason, Is.EqualTo(WorkerClaimReason.None));
        var actualBody = await solver.VerifyEnsuresAsync(0);
        Assert.That(actualBody.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(actualBody.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(actualBody.Core.Any(label => label is null ? throw new System.ArgumentNullException(nameof(label)) : (label.StartsWith("input:disjoint:", StringComparison.Ordinal))), Is.True);
        // Wrong semantic family: reject the actual body proof rather than enrolling it as an entry proof.
        var rejected = TotalCallableVerifier.ProjectEntry(actualBody, requires);
        Assert.That(rejected.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Unknown));
        Assert.That(rejected.Reason, Is.EqualTo(WorkerClaimReason.MalformedBackendResult));
        Assert.That(rejected.ProofCore, Is.Empty);
        Assert.That(rejected.UsedAssumptionIds, Is.Empty);
    }

    [Test]
    public async Task ClaimProjectionRejectsActualOtherCallablePublication()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int First(int value) { Contract.Ensures(Contract.Result<int>() == value); return value; }
                public static int Second(int value) { Contract.Ensures(Contract.Result<int>() == value + 1); return value; }
            }
            """;
        using var project = CreateProject(source, "CallableProjectionAdmission");
        var targets = project.Snapshot.Callables.ToArray();
        Assert.That(targets, Has.Length.EqualTo(2));
        var checks = new List<(CompilerCallablePreparation Target, TotalCallableClaimCheck Check)>();
        foreach (var target in targets)
        {
            TotalCallableClaimCheck? actual = null;
            await TotalCallableVerifier.VerifyAsync(target, project.Request.Budgets,
                check => { System.ArgumentNullException.ThrowIfNull(check); actual = check; }, CancellationToken.None);
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.Checked, Is.True);
            Assert.That(actual.Enrolled, Is.True);
            Assert.That(actual.Evidence.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(actual.Evidence.Outcome is ProvenOutcome or RefutedOutcome, Is.True);
            var valid = CallableClaimResultAssembler.FromTotal(target, actual);
            Assert.That(valid.Outcome, Is.EqualTo(actual.Evidence.Outcome is ProvenOutcome
                ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted));
            Assert.That(valid.Reason, Is.EqualTo(WorkerClaimReason.None));
            checks.Add((target, actual));
        }
        Assert.That(checks.Select(item => item.Check.Evidence.Outcome!.GetType()),
            Is.EquivalentTo(new[] { typeof(ProvenOutcome), typeof(RefutedOutcome) }));
        Assert.Throws<ArgumentException>(new Action(() => CallableClaimResultAssembler.FromTotal(checks[0].Target, checks[1].Check)));
        Assert.Throws<ArgumentException>(new Action(() => CallableClaimResultAssembler.FromTotal(checks[1].Target, checks[0].Check)));
    }

    private static ShadowTestProject CreateProject(string source, string assemblyName)
    {
        System.ArgumentNullException.ThrowIfNull(assemblyName);
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create(assemblyName, ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        return new ShadowTestProject(artifact);
    }

    private static void ValidateRoundTrip(ShadowTestProject project, WorkerClaimResult result)
    {
        System.ArgumentNullException.ThrowIfNull(project);
        System.ArgumentNullException.ThrowIfNull(result);
        var target = project.Snapshot.Callables.Single();
        var reason = WorkerResultAssembler.ProjectCallableReasons([result]).Reason;
        var callable = new WorkerCallableResult
        {
            CallableId = target.Entry.CallableId,
            Coverage = reason == WorkerCallableCoverageReason.None ? WorkerCallableCoverage.Complete : WorkerCallableCoverage.Incomplete,
            Reason = reason,
            Assumptions = target.Entry.Assumptions
        };
        Assert.That(WorkerResultAssembler.TryProjectRunState([callable], [result], [], out var status, out var failure), Is.True);
        var response = WorkerResultAssembler.Create(project.Bind().InputHash, project.Snapshot.CompilerManifest.Manifest,
            status, failure, [callable], [result], project.Request.Budgets, WorkerCacheStatus.Disabled, 0);
        var validation = WorkerProtocolJson.Validate(response, response.InputHash, response.Manifest);
        Assert.That(validation.IsValid, Is.True, string.Join(", ", validation.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code))));
        var json = WorkerProtocolJson.SerializeResponse(response);
        var roundTrip = WorkerProtocolJson.DeserializeResponse(json)
            ?? throw new AssertionException("The canonical response did not deserialize.");
        Assert.That(WorkerProtocolJson.SerializeResponse(roundTrip), Is.EqualTo(json));
    }
}
