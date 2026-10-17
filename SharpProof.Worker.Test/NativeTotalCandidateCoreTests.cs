using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeTotalCandidateCoreTests
{
    private static readonly int[] TwoOrdinals = [0, 1];
    private static readonly int[] InitialAndFinalOrdinals = [0, 1, 0, 1];

    [TestCase("boolean", false)]
    [TestCase("boolean", true)]
    [TestCase("integer", false)]
    [TestCase("integer", true)]
    [TestCase("reference", false)]
    [TestCase("reference", true)]
    public async Task EveryReturnKindRetainsItsActualAdmissionAndAllUncheckedOrdinals(string kind, bool matched)
    {
        System.ArgumentNullException.ThrowIfNull(kind);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        IrTerm returned = kind switch
        {
            "boolean" => factory.Boolean(true),
            "integer" => factory.Integer(7),
            _ => factory.Null(factory.ObjectType)
        };
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("total-module-return");
        builder.Return(block, site, returned);
        var resultType = matched ? returned.Type : kind == "boolean" ? factory.IntegerType : factory.BooleanType;
        var resultVariable = factory.CreateVariable("result", resultType);
        var candidate = new PassiveCallableCandidate("total-module-return", builder.Build(), [],
            resultVariable, [],
            [new(factory.Boolean(true), factory.Boolean(true), site), new(factory.Boolean(true), factory.Boolean(true), site)]);
        var execution = new IrProgramInterpreter(factory).Execute(candidate.Program);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.Unsupported, Is.Null);
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.Exception, Is.Null);
        var run = await RunAsync(candidate);
        if (matched)
        {
            AssertInitialAndProven(run, 2);
            Assert.That(run.Consumed, Is.GreaterThan(0));
        }
        else
        {
            // The typed owned program is legal builder input, but its declared
            // callable result cannot be coupled to the actual return type.
            Assert.That(run.Checks.Select(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Ordinal)), Is.EqualTo(TwoOrdinals));
            Assert.That(run.Checks.All(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (!check.Enrolled && !check.Checked && !check.ProjectAssumptions &&
                check.Evidence.Outcome == null && check.Evidence.Reason == WorkerClaimReason.UnsupportedBody)), Is.True);
            Assert.That(run.Entries, Is.Empty);
            Assert.That(run.Consumed, Is.Zero);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualNativePostconditionsProveOrReplayTheRealBooleanReturn(bool satisfied)
    {
        var candidate = BooleanCandidate(satisfied);
        var run = await RunAsync(candidate);
        Assert.That(run.Checks, Has.Count.EqualTo(2));
        var initial = run.Checks[0];
        Assert.That(initial.Enrolled, Is.True);
        Assert.That(initial.Checked, Is.False);
        Assert.That(initial.Evidence.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        var final = run.Checks[1];
        Assert.That(final.Ordinal, Is.Zero);
        Assert.That(final.Checked, Is.True);
        Assert.That(final.ProjectAssumptions, Is.True);
        Assert.That(final.Evidence.Outcome, satisfied ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>());
        Assert.That(final.Evidence.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(final.Feasibility, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That(final.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        Assert.That(run.Consumed, Is.GreaterThan(0));
        var replay = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, final.Evidence.EntryModel);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.Boolean, Is.True);
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.Unsupported, Is.Null);
        Assert.That(replay.Exception, Is.Null);
        Assert.That(((IrBooleanTerm)candidate.Ensures[0].Value).Value, Is.EqualTo(satisfied));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RealContradictionAndExceptionalExitRetainTheirDistinctVacuity(bool exceptional)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("total-module-vacuity");
        if (exceptional)
        {
            var exit = builder.CreateBlock();
            builder.Throw(block, site, IrExceptionKind.Overflow, exit);
            builder.ExceptionalExit(exit, site);
        }
        else
        { builder.Return(block, site); }
        var candidate = new PassiveCallableCandidate("total-module-vacuity", builder.Build(), [], null,
            [new(factory.Boolean(exceptional), factory.Boolean(true), site)],
            [new(factory.Boolean(false), factory.Boolean(true), site)]);
        var run = await RunAsync(candidate);
        Assert.That(run.Checks, Has.Count.EqualTo(2));
        var final = run.Checks[1];
        Assert.That(final.Checked, Is.True);
        Assert.That(final.Evidence.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(final.Evidence.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(final.Feasibility, Is.EqualTo(exceptional
            ? PassiveCallableFeasibilityKind.NoModeledNormalReturn : PassiveCallableFeasibilityKind.ContradictoryEntry));
        Assert.That(final.Vacuity, Is.EqualTo(exceptional
            ? WorkerVacuityKind.NoModeledNormalReturn : WorkerVacuityKind.ContradictoryPreconditions));
        var execution = new IrProgramInterpreter(factory).Execute(candidate.Program);
        Assert.That(execution.Status, Is.EqualTo(exceptional ? IrProgramExecutionStatus.Exception : IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.Unsupported, Is.Null);
        if (exceptional)
        {
            Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
            Assert.That(execution.Exception.Site, Is.EqualTo(site));
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task BothRequiresTermsRespectTheUnchangedDepthLimit(bool safety, bool beyond)
    {
        var candidate = DepthCandidate(safety, beyond, requires: true);
        var run = await RunAsync(candidate);
        if (beyond)
        {
            Assert.That(run.Entries, Has.Count.EqualTo(1));
            Assert.That(run.Entries[0].Outcome, Is.Null);
            Assert.That(run.Entries[0].Reason, Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
            Assert.That(run.Checks.Select(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Ordinal)), Is.EqualTo(TwoOrdinals));
            Assert.That(run.Checks.All(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (!check.Enrolled && !check.Checked &&
                check.Evidence.Outcome == null && check.Evidence.Reason == WorkerClaimReason.UnsupportedExpression)), Is.True);
            Assert.That(run.Consumed, Is.Zero);
        }
        else
        { AssertInitialAndProven(run, 2); }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task DeepEnsureMaskPreservesTheOtherOrdinalAndItsRealProof(bool safety, bool beyond)
    {
        var candidate = DepthCandidate(safety, beyond, requires: false);
        var run = await RunAsync(candidate);
        Assert.That(run.Checks.Select(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Ordinal)), Is.EqualTo(InitialAndFinalOrdinals));
        Assert.That(run.Checks.Take(2).All(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Enrolled && !check.Checked &&
            check.Evidence.Reason == WorkerClaimReason.UnsupportedBody)), Is.True);
        Assert.That(run.Checks[2].Checked, Is.True);
        Assert.That(run.Checks[2].Evidence.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(run.Checks[2].Evidence.Reason, Is.EqualTo(WorkerClaimReason.None));
        var last = run.Checks[3];
        Assert.That(last.Checked, Is.EqualTo(!beyond));
        Assert.That(last.Enrolled, Is.EqualTo(!beyond));
        Assert.That(last.Evidence.Outcome, beyond ? Is.Null : Is.TypeOf<ProvenOutcome>());
        Assert.That(last.Evidence.Reason, Is.EqualTo(beyond ? WorkerClaimReason.DeepPostcondition : WorkerClaimReason.None));
        Assert.That(run.Consumed, Is.GreaterThan(0));
    }

    [TestCase("candidate")]
    [TestCase("budgets")]
    [TestCase("publish")]
    public void OrdinaryRequiredArgumentsFailBeforeAnyNativeWorkflow(string argument)
    {
        System.ArgumentNullException.ThrowIfNull(argument);
        var candidate = BooleanCandidate(true);
        Action<TotalCandidateCheck> publish = static _ => { };
        var error = Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(() => TotalCallableVerifier.VerifyCandidateAsync(
            argument == "candidate" ? null! : candidate, argument == "budgets" ? null! : new WorkerBudgets(),
            argument == "publish" ? null! : publish, null, CancellationToken.None)));
        Assert.That(error!.ParamName, Is.EqualTo(argument));
    }

    [TestCase("before")]
    [TestCase("enrolled")]
    [TestCase("checked")]
    public void CallerCancellationKeepsExactlyTheAlreadyPublishedModuleEvidence(string stage)
    {
        System.ArgumentNullException.ThrowIfNull(stage);
        var candidate = BooleanCandidate(true, 2);
        var budgets = new WorkerBudgets();
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(candidate.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
        var resource = new MethodResourceBudget(() => session.ConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
        using var cancellation = new CancellationTokenSource();
        var checks = new List<TotalCandidateCheck>();
        if (stage == "before")
        { cancellation.Cancel(); }
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(() => TotalCallableVerifier.VerifyCandidateAsync(candidate, budgets, check =>
        {
            System.ArgumentNullException.ThrowIfNull(check);
            checks.Add(check);
            if (stage == "enrolled" || stage == "checked" && check.Checked)
            { cancellation.Cancel(); }
        }, null, cancellation.Token, session, resource, () => session.ConsumedResourceCount)));
        Assert.That(checks, Has.Count.EqualTo(stage == "before" ? 0 : stage == "enrolled" ? 1 : 3));
        Assert.That(checks.Take(stage == "checked" ? 2 : checks.Count).All(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Enrolled && !check.Checked &&
            check.Evidence.Reason == WorkerClaimReason.UnsupportedBody)), Is.True);
        if (stage == "checked")
        {
            Assert.That(checks[2].Ordinal, Is.Zero);
            Assert.That(checks[2].Evidence.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(checks[2].Evidence.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(session.ConsumedResourceCount, Is.GreaterThan(0));
        }
        else
        { Assert.That(session.ConsumedResourceCount, Is.Zero); }
    }

    [Test]
    public void ActualCompilerEnrollmentPreservesEveryClauseOrdinalAndTermReference()
    {
        var preparation = Prepare("using SharpProof.Attributes; public static class Subject { public static int Target(int x, int divisor) { " +
            "Contract.Requires(divisor != 0); Contract.Requires(x >= 0 && x <= 100); Contract.Requires(divisor >= 1 && divisor <= 10); " +
            "Contract.Ensures(Contract.Result<int>() == x / divisor); Contract.Ensures(Contract.Result<int>() >= 0); return x / divisor; } }");
        var total = preparation.Total!;
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        var requires = total.Clauses.Where(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Requires)).ToArray();
        var ensures = total.Clauses.Where(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Ensures)).ToArray();
        Assert.That(requires, Has.Length.EqualTo(3));
        Assert.That(ensures, Has.Length.EqualTo(2));
        Assert.That(candidate.Requires, Has.Length.EqualTo(requires.Length));
        Assert.That(candidate.Ensures, Has.Length.EqualTo(ensures.Length));
        foreach (var pair in candidate.Requires.Zip(requires).Concat(candidate.Ensures.Zip(ensures)))
        {
            Assert.That(pair.First.Value, Is.SameAs(pair.Second.Value));
            Assert.That(pair.First.Safe, Is.SameAs(pair.Second.Safe));
            Assert.That(pair.First.Operation, Is.EqualTo(pair.Second.Operation));
        }
        Assert.That(candidate.Program, Is.SameAs(total.Program));
        Assert.That(candidate.Factory, Is.SameAs(total.Program.Factory));
    }

    [Test]
    public void ActualCompilerProjectionRetainsItsFirstProofWhenCallerCancels()
    {
        var preparation = Prepare("using SharpProof.Attributes; public static class Subject { public static int Target(int x) { " +
            "Contract.Requires(x >= 0 && x <= 3); Contract.Ensures(Contract.Result<int>() == x); " +
            "Contract.Ensures(Contract.Result<int>() >= 0); return x; } }");
        var budgets = new WorkerBudgets();
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(preparation.Total!.Program.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
        var resource = new MethodResourceBudget(() => session.ConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
        using var cancellation = new CancellationTokenSource();
        var checks = new List<TotalCallableClaimCheck>();
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(() => TotalCallableVerifier.VerifyAsync(preparation, budgets, check =>
        {
            System.ArgumentNullException.ThrowIfNull(check);
            checks.Add(check);
            if (check.Checked)
            { cancellation.Cancel(); }
        }, null, cancellation.Token, session, resource, () => session.ConsumedResourceCount)));
        Assert.That(checks, Has.Count.EqualTo(3));
        Assert.That(checks.Take(2).Select(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.ClaimId)), Is.EqualTo(preparation.Entry.ClaimIds));
        Assert.That(checks.Take(2).All(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Enrolled && !check.Checked &&
            check.Evidence.Reason == WorkerClaimReason.UnsupportedBody)), Is.True);
        Assert.That(checks[2].ClaimId, Is.EqualTo(preparation.Entry.ClaimIds[0]));
        Assert.That(checks[2].Evidence.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(checks[2].Evidence.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(checks[2].Assumptions.Select(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Id)),
            Is.EqualTo(preparation.Entry.Assumptions.OrderBy(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Id), StringComparer.Ordinal).Select(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Id))));
        Assert.That(session.ConsumedResourceCount, Is.GreaterThan(0));
    }

    private static PassiveCallableCandidate BooleanCandidate(bool satisfied, int count = 1)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("total-module-postcondition");
        builder.Return(block, site, factory.Boolean(true));
        var result = factory.CreateVariable("result", factory.BooleanType);
        return new("total-module-postcondition", builder.Build(), [], result, [],
            [.. Enumerable.Range(0, count).Select(_ => new PassiveContractClause(factory.Boolean(satisfied), factory.Boolean(true), site))]);
    }

    private static PassiveCallableCandidate DepthCandidate(bool safety, bool beyond, bool requires)
    {
        var budgets = new WorkerBudgets();
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(WorkerBudgets.DefaultMaximumExpressionDepth));
        var expected = budgets.MaximumExpressionDepth + (beyond ? 1 : 0);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("entry", factory.IntegerType),
            factory.CreateVariable("current", factory.IntegerType), factory.CreateVariable("old", factory.IntegerType));
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("total-module-depth");
        builder.Return(block, site);
        IrTerm predicate = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, factory.Variable(parameter.Entry), factory.Integer(0));
        var bound = 1;
        while (IrTermAnalysis.GetDepth(predicate) < expected)
        {
            predicate = factory.Binary(IrBinaryOperator.AndAlso, predicate,
                factory.Binary(IrBinaryOperator.GreaterThanOrEqual, factory.Variable(parameter.Entry), factory.Integer(bound++)));
        }
        Assert.That(IrTermAnalysis.GetDepth(predicate), Is.EqualTo(expected));
        var deep = new PassiveContractClause(safety ? factory.Boolean(true) : predicate,
            safety ? predicate : factory.Boolean(true), site);
        var plain = new PassiveContractClause(factory.Boolean(true), factory.Boolean(true), site);
        var entry = new PassiveContractClause(factory.Binary(IrBinaryOperator.GreaterThanOrEqual,
            factory.Variable(parameter.Entry), factory.Integer(bound)), factory.Boolean(true), site);
        return new("total-module-depth", builder.Build(), [parameter], null,
            requires ? [deep] : [entry], requires ? [plain, plain] : [plain, deep]);
    }

    private static async Task<CoreRun> RunAsync(PassiveCallableCandidate candidate)
    {
        System.ArgumentNullException.ThrowIfNull(candidate);
        var budgets = new WorkerBudgets();
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(candidate.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
        var resource = new MethodResourceBudget(() => session.ConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
        var checks = new List<TotalCandidateCheck>();
        var entries = new List<PassiveCallableCheckResult>();
        await TotalCallableVerifier.VerifyCandidateAsync(candidate, budgets, checks.Add, entries.Add,
            CancellationToken.None, session, resource, () => session.ConsumedResourceCount);
        return new(checks, entries, session.ConsumedResourceCount);
    }

    private static void AssertInitialAndProven(CoreRun run, int count)
    {
        System.ArgumentNullException.ThrowIfNull(run);
        Assert.That(run.Checks.Select(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Ordinal)), Is.EqualTo(Enumerable.Range(0, count).Concat(Enumerable.Range(0, count))));
        Assert.That(run.Checks.Take(count).All(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Enrolled && !check.Checked &&
            check.Evidence.Reason == WorkerClaimReason.UnsupportedBody)), Is.True);
        Assert.That(run.Checks.Skip(count).All(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.Enrolled && check.Checked && check.ProjectAssumptions &&
            check.Evidence.Outcome is ProvenOutcome && check.Evidence.Reason == WorkerClaimReason.None &&
            check.Feasibility == PassiveCallableFeasibilityKind.Feasible && check.Vacuity == WorkerVacuityKind.None)), Is.True);
        Assert.That(run.Entries, Has.Count.EqualTo(1));
        Assert.That(run.Entries[0].Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(run.Entries[0].Reason, Is.EqualTo(WorkerClaimReason.None));
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        return preparation;
    }

    private sealed record CoreRun(List<TotalCandidateCheck> Checks, List<PassiveCallableCheckResult> Entries, long Consumed)
    {
        public List<TotalCandidateCheck> Checks { get; init; } = Checks ?? throw new System.ArgumentNullException(nameof(Checks));
        public List<PassiveCallableCheckResult> Entries { get; init; } = Entries ?? throw new System.ArgumentNullException(nameof(Entries));
    }
}
