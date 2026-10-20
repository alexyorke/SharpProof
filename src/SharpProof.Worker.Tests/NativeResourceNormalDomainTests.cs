using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeResourceNormalDomainTests
{
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

    private static string? Check(IrProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var owner = new CompilerTotalCallablePreparation("owner", program, [], null, []) { EffectsCompleteAtEntry = true };
        return NativeResourceNormalDomain.Check(ResourceProgram.Zero, owner, TestContext.CurrentContext.CancellationToken);
    }
}
