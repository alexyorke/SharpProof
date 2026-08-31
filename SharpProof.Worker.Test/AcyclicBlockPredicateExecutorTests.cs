using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class AcyclicBlockPredicateExecutorTests
{
    [Test]
    public void DiamondProducesOneJoinedReturnInsteadOfTwoPaths()
    {
        var factory = new IrFactory();
        var condition = factory.CreateVariable("condition", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var whenTrue = builder.CreateBlock("true");
        var whenFalse = builder.CreateBlock("false");
        var join = builder.CreateBlock("join");
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(condition), whenTrue, whenFalse);
        builder.Goto(whenTrue, factory.CreateOperation(), join);
        builder.Goto(whenFalse, factory.CreateOperation(), join);
        builder.Return(join, factory.CreateOperation(), factory.Integer(7));

        var execution = Execute(factory, builder.Build(), [condition]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.IsSuccess, Is.True);
            Assert.That(execution.Returns, Has.Length.EqualTo(1));
            Assert.That(execution.Returns[0].ReturnTerm, Is.SameAs(factory.Integer(7)));
        }
    }

    [Test]
    public async Task SequentialDiamondsRepresentMoreThanSixtyFourPathsWithoutEnumeration()
    {
        const int diamondCount = 7;
        var factory = new IrFactory();
        var conditions = Enumerable.Range(0, diamondCount)
            .Select(index => factory.CreateVariable("condition-" + index, factory.BooleanType))
            .ToArray();
        var builder = new IrProgramBuilder(factory);
        var current = builder.CreateBlock("entry");
        for (var index = 0; index < diamondCount; index++)
        {
            var whenTrue = builder.CreateBlock("true-" + index);
            var whenFalse = builder.CreateBlock("false-" + index);
            var join = builder.CreateBlock("join-" + index);
            builder.Branch(current, factory.CreateOperation(), factory.Variable(conditions[index]), whenTrue, whenFalse);
            builder.Goto(whenTrue, factory.CreateOperation(), join);
            builder.Goto(whenFalse, factory.CreateOperation(), join);
            current = join;
        }
        builder.Return(current, factory.CreateOperation(), factory.Integer(42));

        var program = builder.Build();
        var execution = Execute(factory, program, conditions);
        var variables = conditions.Select((variable, ordinal) =>
            new CompilerCanonicalVariable(
                CompilerVariableRole.Parameter,
                ordinal,
                variable,
                null,
                null,
                "parameter:" + ordinal)).ToImmutableArray();
        var bindings = conditions.ToImmutableDictionary(static value => value, static value => value);
        var backend = new CompletionThenProofBackend();
        var verifier = new CallableVerifier(backend, WorkerBudgets.DefaultMaximumExpressionDepth);
        var results = await verifier.VerifyAsync(
            CreateTarget(factory, program, variables, bindings),
            new MethodResourceBudget(
                null,
                WorkerBudgets.DefaultQueryRlimit,
                WorkerBudgets.DefaultMethodRlimit),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(1 << diamondCount, Is.GreaterThan(64));
            Assert.That(execution.IsSuccess, Is.True);
            Assert.That(execution.Returns, Has.Length.EqualTo(1));
            Assert.That(execution.Returns[0].ReturnTerm, Is.SameAs(factory.Integer(42)));
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.None));
        }
    }

    [Test]
    public void ReassignmentUsesGuardedPhiValuesAtNestedJoins()
    {
        var factory = new IrFactory();
        var firstCondition = factory.CreateVariable("first", factory.BooleanType);
        var secondCondition = factory.CreateVariable("second", factory.BooleanType);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var firstTrue = builder.CreateBlock("first-true");
        var firstFalse = builder.CreateBlock("first-false");
        var firstJoin = builder.CreateBlock("first-join");
        var secondTrue = builder.CreateBlock("second-true");
        var secondFalse = builder.CreateBlock("second-false");
        var secondJoin = builder.CreateBlock("second-join");
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(firstCondition), firstTrue, firstFalse);
        builder.Assign(firstTrue, factory.CreateOperation(), value, factory.Integer(1));
        builder.Goto(firstTrue, factory.CreateOperation(), firstJoin);
        builder.Assign(firstFalse, factory.CreateOperation(), value, factory.Integer(2));
        builder.Goto(firstFalse, factory.CreateOperation(), firstJoin);
        builder.Branch(
            firstJoin,
            factory.CreateOperation(),
            factory.Variable(secondCondition),
            secondTrue,
            secondFalse);
        builder.Assign(secondTrue, factory.CreateOperation(), value, factory.Integer(3));
        builder.Goto(secondTrue, factory.CreateOperation(), secondJoin);
        builder.Goto(secondFalse, factory.CreateOperation(), secondJoin);
        builder.Return(secondJoin, factory.CreateOperation(), factory.Variable(value));

        var execution = Execute(factory, builder.Build(), [firstCondition, secondCondition]);
        var returned = execution.Returns.Single().ReturnTerm!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.IsSuccess, Is.True);
            Assert.That(returned, Is.TypeOf<IrConditionalTerm>());
            Assert.That(Evaluate(factory, returned, firstCondition, true, secondCondition, false), Is.EqualTo(1));
            Assert.That(Evaluate(factory, returned, firstCondition, false, secondCondition, false), Is.EqualTo(2));
            Assert.That(Evaluate(factory, returned, firstCondition, true, secondCondition, true), Is.EqualTo(3));
            Assert.That(Evaluate(factory, returned, firstCondition, false, secondCondition, true), Is.EqualTo(3));
        }
    }

    [Test]
    public void MultipleReturnsRetainSeparateBlockPredicates()
    {
        var factory = new IrFactory();
        var condition = factory.CreateVariable("condition", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var whenTrue = builder.CreateBlock("true");
        var whenFalse = builder.CreateBlock("false");
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(condition), whenTrue, whenFalse);
        builder.Return(whenTrue, factory.CreateOperation(), factory.Integer(1));
        builder.Return(whenFalse, factory.CreateOperation(), factory.Integer(2));

        var execution = Execute(factory, builder.Build(), [condition]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.IsSuccess, Is.True);
            Assert.That(execution.Returns, Has.Length.EqualTo(2));
            Assert.That(execution.Returns.Select(static value => ((IrIntegerTerm)value.ReturnTerm!).Value),
                Is.EquivalentTo(new long[] { 1, 2 }));
            Assert.That(execution.Returns.Select(static value => value.Predicate.Id).Distinct().Count(), Is.EqualTo(2));
        }
    }

    [Test]
    public void BranchPredicatesRequireTheConditionToCompleteNormally()
    {
        var factory = new IrFactory();
        var divisor = factory.CreateVariable("divisor", factory.IntegerType);
        var condition = factory.Binary(
            IrBinaryOperator.Equal,
            factory.Binary(
                IrBinaryOperator.Divide,
                factory.Integer(1),
                factory.Variable(divisor)),
            factory.Integer(1));
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var whenTrue = builder.CreateBlock("true");
        var whenFalse = builder.CreateBlock("false");
        builder.Branch(
            entry,
            factory.CreateOperation(),
            condition,
            whenTrue,
            whenFalse);
        builder.Return(whenTrue, factory.CreateOperation(), factory.Integer(1));
        builder.Return(whenFalse, factory.CreateOperation(), factory.Integer(2));

        var execution = Execute(factory, builder.Build(), [divisor]);
        var completion = IrSemanticTerms.ConstrainSuccessfulEvaluation(
            factory,
            factory.Boolean(true),
            condition);
        var expectedTrue = factory.Binary(
            IrBinaryOperator.AndAlso,
            completion,
            condition);
        var expectedFalse = factory.Binary(
            IrBinaryOperator.AndAlso,
            completion,
            factory.Unary(IrUnaryOperator.Not, condition));
        var predicatesByResult = execution.Returns.ToDictionary(
            static returned => ((IrIntegerTerm)returned.ReturnTerm!).Value,
            static returned => returned.Predicate);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.IsSuccess, Is.True);
            Assert.That(execution.Returns, Has.Length.EqualTo(2));
            Assert.That(predicatesByResult[1], Is.SameAs(expectedTrue));
            Assert.That(predicatesByResult[2], Is.SameAs(expectedFalse));
        }
    }

    [Test]
    public async Task CycleProducesTypedUnknownWithoutInvokingTheBackend()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("cycle");
        builder.Goto(entry, factory.CreateOperation(), entry);
        var program = builder.Build();
        var execution = Execute(factory, program, []);
        var backend = new UnexpectedBackend();
        var verifier = new CallableVerifier(backend, WorkerBudgets.DefaultMaximumExpressionDepth);

        var results = await verifier.VerifyAsync(
            CreateTarget(
                factory,
                program,
                [],
                ImmutableDictionary<IrVarId, IrVarId>.Empty),
            new MethodResourceBudget(
                null,
                WorkerBudgets.DefaultQueryRlimit,
                WorkerBudgets.DefaultMethodRlimit),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    [Test]
    public void CancellationAfterEntryCheckInterruptsSynchronousBodyExecution()
    {
        var factory = new IrFactory();
        var condition = factory.CreateVariable(
            "condition",
            factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("cycle");
        builder.Goto(entry, factory.CreateOperation(), entry);
        var target = new CompilerCallablePreparation(
            factory,
            new WorkerCallableManifestEntry
            {
                CallableId = "M:Test.Subject.Canceled",
                ClaimIds = ["claim"]
            },
            [
                new CompilerPreparedClause(
                    CompilerContractKind.Requires,
                    factory.Variable(condition),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    null,
                    null),
                new CompilerPreparedClause(
                    CompilerContractKind.Ensures,
                    factory.Boolean(true),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    "claim",
                    null)
            ],
            [new CompilerCanonicalVariable(
                CompilerVariableRole.Parameter,
                0,
                condition,
                null,
                null,
                "parameter:0")],
            WorkerClaimReason.None,
            CompilerPreparedBody.ProgramBody(
                builder.Build(),
                ImmutableDictionary<IrVarId, IrVarId>.Empty,
                ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty,
                ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty));
        using var cancellation = new CancellationTokenSource();
        var resourceReads = 0;
        var resourceBudget = new MethodResourceBudget(
            () =>
            {
                if (Interlocked.Increment(ref resourceReads) == 3)
                {
                    cancellation.Cancel();
                }
                return 0;
            },
            WorkerBudgets.DefaultQueryRlimit,
            WorkerBudgets.DefaultMethodRlimit);
        var verifier = new CallableVerifier(
            new CompletionThenProofBackend(),
            WorkerBudgets.DefaultMaximumExpressionDepth);

        Assert.ThrowsAsync<OperationCanceledException>(
            (Func<Task>)(async () => await verifier.VerifyAsync(
                target,
                resourceBudget,
                cancellation.Token)));
        Assert.That(cancellation.IsCancellationRequested, Is.True);
    }

    [Test]
    public void PreCanceledTokenStopsExecutorAndEvidenceConstruction()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(entry, factory.CreateOperation(), factory.Integer(0));
        var program = builder.Build();
        var target = CreateTarget(
            factory,
            program,
            [],
            ImmutableDictionary<IrVarId, IrVarId>.Empty);
        var body = Execute(factory, program, []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Multiple((Action)(() =>
        {
            Assert.Throws<OperationCanceledException>((Action)(() =>
                new AcyclicBlockPredicateExecutor(
                    WorkerBudgets.DefaultMaximumExpressionDepth).Execute(
                    [],
                    factory,
                    program,
                    ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty,
                    ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty,
                    ImmutableDictionary<IrVarId, IrTerm>.Empty,
                    ImmutableDictionary<IrVarId, IrVarId>.Empty,
                    cancellation.Token)));
            Assert.Throws<OperationCanceledException>((Action)(() =>
                CallableEvidenceBuilder.Build(
                    target,
                    body,
                    WorkerBudgets.DefaultMaximumExpressionDepth,
                    cancellation.Token)));
            Assert.Throws<OperationCanceledException>((Action)(() =>
                CallableEvidenceBuilder.BuildEntry(
                    target,
                    WorkerBudgets.DefaultMaximumExpressionDepth,
                    cancellation.Token)));
        }));
    }

    [Test]
    public void SymbolicOperationBudgetExhaustionIsTypedResourceLimit()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(entry, factory.CreateOperation(), factory.Integer(0));
        var executor = new AcyclicBlockPredicateExecutor(
            WorkerBudgets.DefaultMaximumExpressionDepth,
            maximumSymbolicOperations: 1);

        var execution = executor.Execute(
            [],
            factory,
            builder.Build(),
            ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty,
            ImmutableDictionary<IrVarId, IrTerm>.Empty,
            ImmutableDictionary<IrVarId, IrVarId>.Empty);

        Assert.That(execution.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [Test]
    public void AssignmentDefinednessConsumesDeterministicSymbolicOperationBudget()
    {
        var factory = new IrFactory();
        var divisor = factory.CreateVariable("divisor", factory.IntegerType);
        var unused = factory.CreateVariable("unused", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Assign(
            entry,
            factory.CreateOperation(),
            unused,
            factory.Binary(
                IrBinaryOperator.Divide,
                factory.Integer(1),
                factory.Variable(divisor)));
        builder.Return(entry, factory.CreateOperation(), factory.Integer(7));
        var program = builder.Build();
        var environment = ImmutableDictionary<IrVarId, IrTerm>.Empty.Add(
            divisor,
            factory.Variable(divisor));

        var limited = new AcyclicBlockPredicateExecutor(
            WorkerBudgets.DefaultMaximumExpressionDepth,
            maximumSymbolicOperations: 4).Execute(
            [],
            factory,
            program,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty,
            environment,
            ImmutableDictionary<IrVarId, IrVarId>.Empty);
        var exact = new AcyclicBlockPredicateExecutor(
            WorkerBudgets.DefaultMaximumExpressionDepth,
            maximumSymbolicOperations: 6).Execute(
            [],
            factory,
            program,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty,
            environment,
            ImmutableDictionary<IrVarId, IrVarId>.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(limited.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(exact.IsSuccess, Is.True);
            Assert.That(exact.Returns, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public void SequentialSpecCallsDoNotConflateReusedArtifactTarget()
    {
        var factory = new IrFactory();
        var result = factory.CreateVariable("result", factory.IntegerType);
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "Abs",
            factory.IntegerType,
            true,
            factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var firstCall = builder.Call(
            entry,
            factory.CreateOperation(),
            result,
            member,
            null,
            factory.Integer(-1));
        var secondCall = builder.Call(
            entry,
            factory.CreateOperation(),
            result,
            member,
            null,
            factory.Integer(-2));
        builder.Return(
            entry,
            factory.CreateOperation(),
            factory.Variable(result));
        var specCalls = ImmutableDictionary<
                IrInstructionId,
                CompilerPreparedSpecCall>.Empty
            .Add(firstCall.Id, Prepared(firstCall))
            .Add(secondCall.Id, Prepared(secondCall));

        var execution = new AcyclicBlockPredicateExecutor(
            WorkerBudgets.DefaultMaximumExpressionDepth).Execute(
                [],
                factory,
                builder.Build(),
                specCalls,
                ImmutableDictionary<
                    IrInstructionId,
                    CompilerPreparedSummaryCall>.Empty,
                ImmutableDictionary<IrVarId, IrTerm>.Empty,
                ImmutableDictionary<IrVarId, IrVarId>.Empty);
        var firstResult = IrTermAnalysis.CollectVariables(
            execution.SpecAssumptions[0].Predicate).Single();
        var secondResult = IrTermAnalysis.CollectVariables(
            execution.SpecAssumptions[1].Predicate).Single();
        var returned = (IrVariableTerm)execution.Returns.Single().ReturnTerm!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.IsSuccess, Is.True);
            Assert.That(execution.SpecAssumptions, Has.Length.EqualTo(2));
            Assert.That(firstResult, Is.Not.EqualTo(secondResult));
            Assert.That(returned.Variable, Is.EqualTo(secondResult));
        }

        static CompilerPreparedSpecCall Prepared(IrCallInstruction call)
        {
            return new CompilerPreparedSpecCall(
                call.Id,
                "M:System.Math.Abs(System.Int32)",
                "bcl.math.abs.int32",
                false);
        }
    }

    [Test]
    public void SpecCallArgumentDefinednessConstrainsSubsequentFlow()
    {
        var factory = new IrFactory();
        var divisor = factory.CreateVariable("divisor", factory.IntegerType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "Abs",
            factory.IntegerType,
            true,
            factory.IntegerType);
        var call = builder.Call(
            entry,
            factory.CreateOperation(),
            result,
            member,
            null,
            factory.Binary(
                IrBinaryOperator.Divide,
                factory.Integer(1),
                factory.Variable(divisor)));
        builder.Return(
            entry,
            factory.CreateOperation(),
            factory.Variable(result));
        var program = builder.Build();
        var specCalls =
            ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty.Add(
                call.Id,
                new CompilerPreparedSpecCall(
                    call.Id,
                    "M:System.Math.Abs(System.Int32)",
                    "bcl.math.abs.int32",
                    false));
        var environment = ImmutableDictionary<IrVarId, IrTerm>.Empty.Add(
            divisor,
            factory.Variable(divisor));
        var limited = new AcyclicBlockPredicateExecutor(
            WorkerBudgets.DefaultMaximumExpressionDepth,
            maximumSymbolicOperations: 4).Execute(
            [],
            factory,
            program,
            specCalls,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty,
            environment,
            ImmutableDictionary<IrVarId, IrVarId>.Empty);
        var execution = new AcyclicBlockPredicateExecutor(
            WorkerBudgets.DefaultMaximumExpressionDepth).Execute(
            [],
            factory,
            program,
            specCalls,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty,
            environment,
            ImmutableDictionary<IrVarId, IrVarId>.Empty);

        Assert.That(
            limited.Reason,
            Is.EqualTo(WorkerClaimReason.ResourceLimit));
        AssertCallGuardRequiresNormalEvaluation(
            factory,
            execution,
            divisor);
    }

    [Test]
    public void SpecCallReceiverDefinednessConstrainsSubsequentFlow()
    {
        var factory = new IrFactory();
        var divisor = factory.CreateVariable("divisor", factory.IntegerType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.StringType,
            "Length",
            factory.IntegerType,
            false);
        var operation = factory.CreateOperation();
        var receiver = factory.Conditional(
            factory.Binary(
                IrBinaryOperator.Equal,
                factory.Binary(
                    IrBinaryOperator.Divide,
                    factory.Integer(1),
                    factory.Variable(divisor)),
                factory.Integer(0)),
            factory.String("empty"),
            factory.String("nonempty"));
        var call = builder.Call(
            entry,
            operation,
            result,
            member,
            receiver);
        builder.Havoc(
            entry,
            operation,
            IrHavocKind.Memory);
        builder.Return(
            entry,
            factory.CreateOperation(),
            factory.Variable(result));
        var program = builder.Build();
        var specCalls =
            ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty.Add(
                call.Id,
                new CompilerPreparedSpecCall(
                    call.Id,
                    "P:System.String.Length",
                    "bcl.string.length",
                    true));
        var environment = ImmutableDictionary<IrVarId, IrTerm>.Empty.Add(
            divisor,
            factory.Variable(divisor));
        var limited = new AcyclicBlockPredicateExecutor(
            WorkerBudgets.DefaultMaximumExpressionDepth,
            maximumSymbolicOperations: 4).Execute(
            [],
            factory,
            program,
            specCalls,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty,
            environment,
            ImmutableDictionary<IrVarId, IrVarId>.Empty);
        var execution = new AcyclicBlockPredicateExecutor(
            WorkerBudgets.DefaultMaximumExpressionDepth).Execute(
            [],
            factory,
            program,
            specCalls,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty,
            environment,
            ImmutableDictionary<IrVarId, IrVarId>.Empty);

        Assert.That(
            limited.Reason,
            Is.EqualTo(WorkerClaimReason.ResourceLimit));
        AssertCallGuardRequiresNormalEvaluation(
            factory,
            execution,
            divisor);
    }

    [Test]
    public void SourceCallContributesItsGuardedRelationalAssumption()
    {
        var factory = new IrFactory();
        var input = factory.CreateVariable("input", factory.IntegerType);
        var callTarget = factory.CreateVariable(
            "call-target",
            factory.IntegerType);
        var summaryResult = factory.CreateVariable(
            "summary-result",
            factory.IntegerType);
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "Increment",
            factory.IntegerType,
            true,
            factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var call = builder.Call(
            entry,
            factory.CreateOperation(),
            callTarget,
            member,
            null,
            factory.Variable(input));
        builder.Return(
            entry,
            factory.CreateOperation(),
            factory.Variable(callTarget));
        var relation = factory.Binary(
            IrBinaryOperator.Equal,
            factory.Variable(summaryResult),
            factory.Binary(
                IrBinaryOperator.Add,
                factory.Variable(input),
                factory.Integer(1)));
        var sourceCalls = ImmutableDictionary<
            IrInstructionId,
            CompilerPreparedSummaryCall>.Empty.Add(
                call.Id,
                new CompilerPreparedSummaryCall(
                    call.Id,
                    "M:Subject.Increment(System.Int32)",
                    CompilerSummaryOrigin.Source,
                    summaryResult,
                    [],
                    relation,
                    new string('a', 64),
                    string.Empty,
                    []));

        var execution = new AcyclicBlockPredicateExecutor(
            WorkerBudgets.DefaultMaximumExpressionDepth).Execute(
                [],
                factory,
                builder.Build(),
                ImmutableDictionary<
                    IrInstructionId,
                    CompilerPreparedSpecCall>.Empty,
                sourceCalls,
                ImmutableDictionary<IrVarId, IrTerm>.Empty.Add(
                    input,
                    factory.Variable(input)),
                ImmutableDictionary<IrVarId, IrVarId>.Empty);

        var assumption = execution.SummaryAssumptions.Single();
        var result = execution.Returns.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.IsSuccess, Is.True);
            Assert.That(execution.SpecAssumptions, Is.Empty);
            Assert.That(assumption.CallIdentity, Does.Contain("Increment"));
            Assert.That(
                assumption.Origin,
                Is.EqualTo(CompilerSummaryOrigin.Source));
            Assert.That(assumption.Predicate, Is.SameAs(relation));
            Assert.That(result.ReturnTerm, Is.SameAs(factory.Variable(summaryResult)));
        }
    }

    private static SymbolicBodyExecution Execute(
        IrFactory factory,
        IrProgram program,
        IReadOnlyCollection<IrVarId> inputs)
    {
        var environment = inputs.ToImmutableDictionary(
            static variable => variable,
            variable => (IrTerm)factory.Variable(variable));
        return new AcyclicBlockPredicateExecutor(WorkerBudgets.DefaultMaximumExpressionDepth).Execute(
            [],
            factory,
            program,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty,
            ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty,
            environment,
            ImmutableDictionary<IrVarId, IrVarId>.Empty);
    }

    private static void AssertCallGuardRequiresNormalEvaluation(
        IrFactory factory,
        SymbolicBodyExecution execution,
        IrVarId divisor)
    {
        var predicate = execution.Returns.Single().Predicate;
        var interpreter = new IrInterpreter(factory);
        var exceptional = interpreter.Evaluate(
            predicate,
            ImmutableDictionary<IrVarId, IrValue>.Empty.Add(
                divisor,
                factory.CreateIntegerValue(0)));
        var normal = interpreter.Evaluate(
            predicate,
            ImmutableDictionary<IrVarId, IrValue>.Empty.Add(
                divisor,
                factory.CreateIntegerValue(1)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.IsSuccess, Is.True);
            Assert.That(execution.SpecAssumptions, Has.Length.EqualTo(1));
            Assert.That(
                execution.SpecAssumptions.Single().Guard,
                Is.SameAs(predicate));
            Assert.That(
                exceptional.Status,
                Is.EqualTo(IrEvaluationStatus.Exception));
            Assert.That(normal.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(normal.Value!.Boolean, Is.True);
        }
    }

    private static long Evaluate(
        IrFactory factory,
        IrTerm term,
        IrVarId first,
        bool firstValue,
        IrVarId second,
        bool secondValue)
    {
        var result = new IrInterpreter(factory).Evaluate(
            term,
            ImmutableDictionary<IrVarId, IrValue>.Empty
                .Add(first, factory.CreateBooleanValue(firstValue))
                .Add(second, factory.CreateBooleanValue(secondValue)));
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        return result.Value!.Integer;
    }

    private static CompilerCallablePreparation CreateTarget(
        IrFactory factory,
        IrProgram program,
        ImmutableArray<CompilerCanonicalVariable> variables,
        ImmutableDictionary<IrVarId, IrVarId> parameterBindings)
    {
        return new(
            factory,
            new WorkerCallableManifestEntry
            {
                CallableId = "M:Test.Subject.Cycle",
                ClaimIds = ["claim"]
            },
            [new CompilerPreparedClause(
                CompilerContractKind.Ensures,
                factory.Boolean(true),
                CompilerContractEvidence.CompilerBoundInvocation,
                "claim",
                null)],
            variables,
            WorkerClaimReason.None,
            CompilerPreparedBody.ProgramBody(
                program,
                parameterBindings,
                ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty,
                ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall>.Empty));
    }

    private sealed class CompletionThenProofBackend : ISmtBackend
    {
        private int _callCount;
        internal int CallCount => Volatile.Read(ref _callCount);
        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _callCount) != 1)
            {
                return Task.FromResult(
                    BackendCheckResult.Unsatisfiable([]));
            }

            var assignments = query.ModelVariables.Select(variable =>
                KeyValuePair.Create(
                    variable,
                    query.Factory.CreateBooleanValue(false)));
            return Task.FromResult(
                BackendCheckResult.Satisfiable(
                    new BackendModel(assignments)));
        }
    }

    private sealed class UnexpectedBackend : ISmtBackend
    {
        private int _callCount;
        internal int CallCount => Volatile.Read(ref _callCount);
        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            throw new AssertionException("A cyclic body reached the backend.");
        }
    }
}
