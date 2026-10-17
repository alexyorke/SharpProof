using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeConstructionAdmissionTests
{
    [TestCase("blocks")]
    [TestCase("parameters")]
    public async Task DefaultConstructionBoundsRejectLegalOwnedGraphEnlargement(string dimension)
    {
        System.ArgumentNullException.ThrowIfNull(dimension);
        var bounded = CreateCandidate(dimension, 1);
        var enlarged = CreateCandidate(dimension, PassiveCallableVcBuilder.MaximumSteps + 1);
        foreach (var candidate in new[] { bounded, enlarged })
        {
            // The public builder accepts both owned programs, and their entry
            // executes normally. Size admission does not imply malformed IR.
            var actual = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(actual.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(actual.ReturnValue?.Integer, Is.Zero);
                Assert.That(actual.ConsumedApproximation, Is.False);
                Assert.That(actual.Unsupported, Is.Null);
                Assert.That(actual.Exception, Is.Null);
            }
        }
        Assert.That(PassiveLoopCutter.Loops(enlarged, TestContext.CurrentContext.CancellationToken), Is.Empty);
        var cut = PassiveLoopCutter.TryCreate(enlarged, out var proof, out var search, out var reason, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut, Is.False);
            Assert.That(proof, Is.Null);
            Assert.That(search, Is.Null);
            Assert.That(reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(PassiveLoopCutter.TryEncodeWithInvariants(enlarged, [], TestContext.CurrentContext.CancellationToken), Is.Null);
            Assert.That(PassiveCallableVcBuilder.TryBuildWithInvariants(enlarged, [], TestContext.CurrentContext.CancellationToken), Is.Null);
        }
        Assert.That(PassiveCallableVcBuilder.TryBuild(bounded, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(failure, Is.EqualTo(WorkerClaimReason.None));
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync(cancellationToken: TestContext.CurrentContext.CancellationToken)).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var verified = await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(verified.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(verified.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
    }

    [TestCase(1, true)]
    [TestCase(1024, false)]
    public async Task WitnessExpansionRespectsTheDefaultInstructionCap(int writes, bool supported)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var input = new PassiveParameterBinding(factory.CreateVariable("entry", factory.BooleanType),
            factory.CreateVariable("current", factory.BooleanType), factory.CreateVariable("old", factory.BooleanType));
        var site = factory.CreateOperation("bounded-expansion-admission");
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var header = builder.CreateBlock("header");
        var body = builder.CreateBlock("body");
        var exit = builder.CreateBlock("exit");
        builder.Assign(entry, site, input.Current, factory.Variable(input.Entry));
        builder.Goto(entry, site, header);
        builder.Branch(header, site, factory.Variable(input.Current), body, exit);
        for (var index = 0; index < writes; index++)
        { builder.Assign(body, site, input.Current, factory.Boolean(false)); }
        builder.Goto(body, site, header);
        builder.Return(exit, site);
        var ensures = new PassiveContractClause(factory.Binary(IrBinaryOperator.Equal,
            factory.Variable(input.Current), factory.Boolean(false)), factory.Boolean(true), site);
        var candidate = new PassiveCallableCandidate("bounded-expansion-admission", builder.Build(), [input], null, [], [ensures]);
        Assert.That(candidate.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).Count(), Is.LessThan(PassiveCallableVcBuilder.MaximumSteps));
        Assert.That(PassiveLoopCutter.Loops(candidate, TestContext.CurrentContext.CancellationToken), Has.Length.EqualTo(1));
        foreach (var value in new[] { false, true })
        {
            var inputValue = factory.CreateBooleanValue(value);
            var actual = new IrProgramInterpreter(factory).Execute(candidate.Program, new Dictionary<IrVarId, IrValue>
            {
                [input.Entry] = inputValue,
                [input.Current] = inputValue,
                [input.Old] = inputValue
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(actual.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(actual.GetCurrentValue(input.Current)?.Boolean, Is.False);
                Assert.That(actual.ConsumedApproximation, Is.False);
                Assert.That(actual.Unsupported, Is.Null);
                Assert.That(actual.Exception, Is.Null);
            }
        }
        var cut = PassiveLoopCutter.TryCreate(candidate, out var proof, out var search, out var reason, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut, Is.EqualTo(supported));
            Assert.That(proof, Is.Not.Null);
            Assert.That(search == null, Is.EqualTo(!supported));
            Assert.That(reason, Is.EqualTo(supported ? WorkerClaimReason.None : WorkerClaimReason.ResourceLimit));
        }
        var built = PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(built, Is.EqualTo(supported));
            Assert.That(plan == null, Is.EqualTo(!supported));
            Assert.That(failure, Is.EqualTo(supported ? WorkerClaimReason.None : WorkerClaimReason.ResourceLimit));
        }
        if (supported)
        {
            using var solver = new PassiveCallableSolver(plan!);
            Assert.That((await solver.VerifyFeasibilityAsync(cancellationToken: TestContext.CurrentContext.CancellationToken)).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
            var verified = await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(verified.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(verified.Reason, Is.EqualTo(WorkerClaimReason.None));
        }
    }

    private static PassiveCallableCandidate CreateCandidate(string dimension, int count)
    {
        System.ArgumentNullException.ThrowIfNull(dimension);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var site = factory.CreateOperation("default-construction-admission");
        var parameters = new List<PassiveParameterBinding>();
        if (dimension == "parameters")
        {
            for (var index = 0; index < count; index++)
            {
                var suffix = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                parameters.Add(new(factory.CreateVariable("entry:" + suffix, factory.IntegerType),
                    factory.CreateVariable("current:" + suffix, factory.IntegerType),
                    factory.CreateVariable("old:" + suffix, factory.IntegerType)));
            }
        }
        else
        { Assert.That(dimension, Is.EqualTo("blocks")); }
        var blocks = dimension == "blocks" ? count : 1;
        for (var index = 0; index < blocks; index++)
        {
            var block = builder.CreateBlock();
            builder.Return(block, site, factory.Integer(0));
        }
        var result = factory.CreateVariable("result", factory.IntegerType);
        var ensures = new PassiveContractClause(factory.Binary(IrBinaryOperator.Equal,
            factory.Variable(result), factory.Integer(0)), factory.Boolean(true), site);
        return new PassiveCallableCandidate("default-construction-admission", builder.Build(), [.. parameters], result, [], [ensures]);
    }
}
