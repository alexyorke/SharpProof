using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeResourceNormalDomainTests
{
    [TestCase(10)]
    [TestCase(11)]
    public void BranchSnapshotFoldingCannotFollowEdgesAfterTheSharedBudgetIsExhausted(int reserve)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var condition = factory.CreateVariable("condition", factory.BooleanType);
        builder.Assign(entry, factory.CreateOperation(), condition, factory.Boolean(true));
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(condition), yes, no);
        builder.Return(yes, factory.CreateOperation());
        builder.Return(no, factory.CreateOperation());
        var clause = new CompilerTotalClause(CompilerContractKind.Requires, factory.Boolean(true), factory.Boolean(true),
            factory.CreateOperation(), null, null);
        var owner = new CompilerTotalCallablePreparation("owner", builder.Build(), [], null,
            Enumerable.Repeat(clause, ResourceExpression.MaximumNodes - reserve).ToImmutableArray());
        Assert.That(NativeResourceNormalDomain.Check(ResourceProgram.Zero, owner,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo("ResourceNormalDomainBudgetExceeded"));
    }

    [TestCase(16, true)]
    [TestCase(96, false)]
    public void DeepArrayExtentSnapshotsRemainUnknownWithoutInventingNormalCompletion(int aliases, bool admitted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var current = factory.CreateVariable("extent", factory.IntegerType);
        builder.Assign(entry, factory.CreateOperation(), current, factory.Integer(0));
        for (var index = 0; index < aliases; index++)
        {
            var next = factory.CreateVariable("snapshot", factory.IntegerType);
            builder.Assign(entry, factory.CreateOperation(), next, factory.Variable(current));
            current = next;
        }
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var target = factory.CreateVariable("array", type);
        var allocation = builder.Allocate(entry, factory.CreateOperation(), type, target, factory.Variable(current));
        builder.Return(entry, factory.CreateOperation(), factory.Integer(7));
        var program = builder.Build();
        Assert.That(Check(program), admitted ? Is.Null : Is.EqualTo("ResourceNormalDomainInstructionUnsupported:" + allocation.Kind));
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000,
            new IrProgramReplayOptions(static _ => null), TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.ReturnValue!.Integer, Is.EqualTo(7));
    }

    [TestCase(IrBinaryOperator.Add)]
    [TestCase(IrBinaryOperator.Subtract)]
    [TestCase(IrBinaryOperator.Multiply)]
    [TestCase(IrBinaryOperator.Divide)]
    public void NumericSnapshotExpressionsCannotBecomeBooleanFacts(IrBinaryOperator operation)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var left = factory.CreateVariable("left", factory.IntegerType);
        var right = factory.CreateVariable("right", factory.IntegerType);
        var definitions = new Dictionary<IrVarId, IrTerm>
        { [left] = factory.Integer(2), [right] = factory.Integer(3) };
        var term = factory.Binary(operation, factory.Variable(left), factory.Variable(right));
        var method = typeof(NativeResourceNormalDomain).GetMethod("Boolean",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        object?[] arguments = [term, factory, definitions, new HashSet<IrVarId>(), ResourceExpression.MaximumNodes,
            TestContext.CurrentContext.CancellationToken, 0];
        Assert.That(method!.Invoke(null, arguments), Is.Null);
    }

    [TestCase(1, false)]
    [TestCase(4, true)]
    public void AllocationSuccessRecognitionCannotBorrowBudgetFromItsContinuation(int budget, bool admitted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var failure = builder.CreateBlock();
        var success = builder.CreateBlock();
        var failureExit = builder.CreateBlock();
        var choice = factory.CreateVariable("choice", factory.BooleanType);
        builder.Havoc(entry, factory.CreateOperation(IrOpaqueCallSite.Describe(IrOpaqueCallEffects.Throws,
            "array:beyond-maximum-length")), IrHavocKind.Variables, IrHavocOrigin.Approximation, choice);
        var site = factory.CreateOperation("array-site");
        builder.Branch(entry, site, factory.Variable(choice), failure, success);
        builder.Throw(failure, site, IrExceptionKind.Unknown, failureExit);
        builder.Return(failureExit, factory.CreateOperation());
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        builder.Allocate(success, site, type, factory.CreateVariable("array", type), factory.Integer(1));
        builder.Return(success, factory.CreateOperation());
        var program = builder.Build();
        var method = typeof(NativeResourceNormalDomain).GetMethod("AllocationSuccessContinuation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        object?[] arguments = [program, program.GetBlock(entry), budget, TestContext.CurrentContext.CancellationToken];
        var continuation = method!.Invoke(null, arguments);
        Assert.That(continuation, admitted ? Is.EqualTo(success) : Is.Null);
        if (!admitted)
        { Assert.That(arguments[2], Is.LessThan(0)); }
    }

    [TestCase(IrBinaryOperator.Equal, false)]
    [TestCase(IrBinaryOperator.NotEqual, true)]
    [TestCase(IrBinaryOperator.LessThan, true)]
    [TestCase(IrBinaryOperator.LessThanOrEqual, true)]
    [TestCase(IrBinaryOperator.GreaterThan, false)]
    [TestCase(IrBinaryOperator.GreaterThanOrEqual, false)]
    public void ImmutableIntegerSnapshotsSelectOnlyTheProvedNormalEdge(IrBinaryOperator operation, bool taken)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var left = factory.CreateVariable("left", factory.IntegerType);
        var right = factory.CreateVariable("right", factory.IntegerType);
        builder.Assign(entry, factory.CreateOperation(), left, factory.Integer(2));
        builder.Assign(entry, factory.CreateOperation(), right, factory.Integer(3));
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        builder.Branch(entry, factory.CreateOperation(), factory.Binary(operation, factory.Variable(left), factory.Variable(right)), yes, no);
        builder.Return(taken ? yes : no, factory.CreateOperation());
        builder.Assume(taken ? no : yes, factory.CreateOperation(), factory.Boolean(false));
        builder.Return(taken ? no : yes, factory.CreateOperation());
        Assert.That(Check(builder.Build()), Is.Null);
    }

    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, true)]
    public void BooleanSnapshotsPreserveNegationAndBothShortCircuitOperators(bool first, bool second, bool disjunction)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var left = factory.CreateVariable("left", factory.BooleanType);
        var right = factory.CreateVariable("right", factory.BooleanType);
        builder.Assign(entry, factory.CreateOperation(), left, factory.Boolean(first));
        builder.Assign(entry, factory.CreateOperation(), right, factory.Boolean(second));
        var combined = factory.Binary(disjunction ? IrBinaryOperator.OrElse : IrBinaryOperator.AndAlso,
            factory.Variable(left), factory.Variable(right));
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        builder.Branch(entry, factory.CreateOperation(), factory.Unary(IrUnaryOperator.Not, combined), yes, no);
        var taken = !(disjunction ? first || second : first && second);
        builder.Return(taken ? yes : no, factory.CreateOperation());
        builder.Assume(taken ? no : yes, factory.CreateOperation(), factory.Boolean(false));
        builder.Return(taken ? no : yes, factory.CreateOperation());
        Assert.That(Check(builder.Build()), Is.Null);
    }

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(2, true)]
    public void DivisionNeedsAnImmutablePositiveDivisor(int divisor, bool admitted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var denominator = factory.CreateVariable("denominator", factory.IntegerType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        builder.Assign(entry, factory.CreateOperation(), denominator, factory.Integer(divisor));
        builder.Assign(entry, factory.CreateOperation(), result,
            factory.Binary(IrBinaryOperator.Divide, factory.Integer(7), factory.Variable(denominator)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(result));
        Assert.That(Check(builder.Build()), admitted ? Is.Null : Is.EqualTo("ResourceNormalDomainTermUnsupported"));
    }

    [TestCase(32, 2, true)]
    [TestCase(8, 2, true)]
    [TestCase(8, 256, false)]
    public void ConvertedDivisorsRequireAValuePreservingConstant(int width, int divisor, bool admitted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var wide = factory.GetOrCreateIntegerType(64, signed: true);
        var target = factory.GetOrCreateIntegerType(width, signed: true);
        var denominator = factory.CreateVariable("denominator", wide);
        builder.Assign(entry, factory.CreateOperation(), denominator, factory.Integer(wide, divisor));
        builder.Return(entry, factory.CreateOperation(), factory.Binary(IrBinaryOperator.Divide,
            factory.Integer(target, 7), factory.Cast(target, factory.Variable(denominator))));
        Assert.That(Check(builder.Build()), admitted ? Is.Null : Is.EqualTo("ResourceNormalDomainTermUnsupported"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void PreconditionsCannotSilentlyRestrictTheNormalDomain(int mode)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        builder.Return(builder.CreateBlock(), factory.CreateOperation());
        var clause = new CompilerTotalClause(CompilerContractKind.Requires, factory.Boolean(mode != 1), factory.Boolean(mode != 2),
            factory.CreateOperation(), null, null);
        var clauses = Enumerable.Repeat(clause, mode == 3 ? ResourceExpression.MaximumNodes + 1 : 1).ToImmutableArray();
        var owner = new CompilerTotalCallablePreparation("owner", builder.Build(), [], null, clauses);
        var reason = NativeResourceNormalDomain.Check(ResourceProgram.Zero, owner, TestContext.CurrentContext.CancellationToken);
        Assert.That(reason, mode == 0 ? Is.Null : Is.EqualTo(mode == 3 ? "ResourceNormalDomainBudgetExceeded" : "ResourceNormalDomainPreconditionsUnsupported"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExhaustedInstructionBudgetsAndAbsentReturnsDoNotEstablishNormalCompletion(bool exhausted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        if (exhausted)
        {
            var value = factory.CreateVariable("value", factory.IntegerType);
            for (var index = 0; index <= ResourceExpression.MaximumNodes; index++)
            { builder.Assign(entry, factory.CreateOperation(), value, factory.Integer(index)); }
            builder.Return(entry, factory.CreateOperation());
        }
        else
        { builder.Goto(entry, factory.CreateOperation(), entry); }
        Assert.That(Check(builder.Build()), Is.EqualTo(exhausted ? "ResourceNormalDomainBudgetExceeded" : "ResourceNormalDomainReturnUnproved"));
    }

    [Test]
    public void BodyAbstractionsAndCancellationCannotPublishNormalCompletionEvidence()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        builder.Return(builder.CreateBlock(), factory.CreateOperation());
        var owner = new CompilerTotalCallablePreparation("owner", builder.Build(), [], null, []) { IsBodyAbstraction = true };
        Assert.That(NativeResourceNormalDomain.Check(ResourceProgram.Zero, owner, TestContext.CurrentContext.CancellationToken),
            Is.EqualTo("ResourceNormalDomainCallableUnsupported"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () => { _ = NativeResourceNormalDomain.Check(ResourceProgram.Zero, owner, cancellation.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [TestCase(30, true)]
    [TestCase(31, false)]
    public void PositiveDivisorSnapshotsRespectTheResolutionDepthBoundary(int aliases, bool admitted)
    {
        if (aliases is not (30 or 31))
        { throw new ArgumentOutOfRangeException(nameof(aliases)); }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var current = factory.CreateVariable("divisor", factory.IntegerType);
        builder.Assign(entry, factory.CreateOperation(), current, factory.Integer(1));
        for (var index = 0; index < aliases; index++)
        {
            var next = factory.CreateVariable("snapshot", factory.IntegerType);
            builder.Assign(entry, factory.CreateOperation(), next, factory.Variable(current));
            current = next;
        }
        builder.Return(entry, factory.CreateOperation(),
            factory.Binary(IrBinaryOperator.Divide, factory.Integer(7), factory.Variable(current)));
        var program = builder.Build();
        Assert.That(Check(program), admitted ? Is.Null : Is.EqualTo("ResourceNormalDomainTermUnsupported"));
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000,
            new IrProgramReplayOptions(static _ => null), TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.ReturnValue!.Integer, Is.EqualTo(7));
    }
    [TestCase(4096, "ResourceNormalDomainBudgetExceeded")]
    [TestCase(4094, "ResourceNormalDomainBudgetExceeded")]
    [TestCase(4092, "ResourceNormalDomainTermUnsupported")]
    public void OneSharedBudgetCoversClausesRegionsInstructionsAndReturnTerms(int clauses, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        builder.Return(builder.CreateBlock(), factory.CreateOperation(), factory.Integer(7));
        var clause = new CompilerTotalClause(CompilerContractKind.Requires, factory.Boolean(true), factory.Boolean(true),
            factory.CreateOperation(), null, null);
        var owner = new CompilerTotalCallablePreparation("owner", builder.Build(), [], null,
            Enumerable.Repeat(clause, clauses).ToImmutableArray());
        Assert.That(NativeResourceNormalDomain.Check(ResourceProgram.Zero, owner,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(expected));
    }

    [TestCase(29, true)]
    [TestCase(32, false)]
    public void BooleanSnapshotDepthCannotDiscardAnUnprovedExceptionalEdge(int aliases, bool admitted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var current = factory.CreateVariable("condition", factory.BooleanType);
        builder.Assign(entry, factory.CreateOperation(), current, factory.Boolean(true));
        for (var index = 0; index < aliases; index++)
        {
            var next = factory.CreateVariable("snapshot", factory.BooleanType);
            builder.Assign(entry, factory.CreateOperation(), next, factory.Variable(current));
            current = next;
        }
        var yes = builder.CreateBlock();
        var no = builder.CreateBlock();
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(current), yes, no);
        builder.Return(yes, factory.CreateOperation());
        builder.Assume(no, factory.CreateOperation(), factory.Boolean(false));
        builder.Return(no, factory.CreateOperation());
        var program = builder.Build();
        Assert.That(Check(program), admitted ? Is.Null : Is.EqualTo("ResourceNormalDomainInstructionUnsupported:Assume"));
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000,
            new IrProgramReplayOptions(static _ => null), TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.ReturnValue, Is.Null);
    }

    private static string? Check(IrProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var owner = new CompilerTotalCallablePreparation("owner", program, [], null, []) { EffectsCompleteAtEntry = true };
        return NativeResourceNormalDomain.Check(ResourceProgram.Zero, owner, TestContext.CurrentContext.CancellationToken);
    }
}
