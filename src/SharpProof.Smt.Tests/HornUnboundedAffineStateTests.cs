using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornUnboundedAffineStateTests
{
    [Test]
    public async Task ArbitraryAffineInputsStayRelatedAcrossAnUnboundedWrappedStep()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateVariable("first", factory.IntegerType);
        var second = factory.CreateVariable("second", factory.IntegerType);
        var x = factory.Variable(first);
        var y = factory.Variable(second);
        var factor = factory.Integer(257);
        var affine = factory.Binary(IrBinaryOperator.Equal, y,
            factory.Binary(IrBinaryOperator.Multiply, x, factor));
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:unbounded-affine", factory.BooleanType, isStatic: true, factory.IntegerType, factory.IntegerType);
        var atom = factory.PureOpaque(relation, null, x, y);
        var nextX = factory.Binary(IrBinaryOperator.Add, x, factory.Integer(1));
        var nextY = factory.Binary(IrBinaryOperator.Add, y, factor);
        var statesBuilder = ImmutableDictionary.CreateBuilder<IrMemberId, ImmutableArray<IrTerm>>();
        statesBuilder[relation] = [x, y];
        var states = statesBuilder.ToImmutable();
        ImmutableArray<VerificationQuery> clauses =
        [
            Query(factory, [affine], atom),
            Query(factory, [atom], factory.PureOpaque(relation, null, nextX, nextY)),
            Query(factory, [atom], affine)
        ];
        var proposals = HornInvariantSearch.Propose(factory, clauses, states,
            IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);
        Assert.That(proposals, Does.ContainKey(relation));
        var invariant = proposals[relation];
        var preserved = IrSubstitution.Substitute(factory,
            IrSubstitution.Substitute(factory, invariant, first, nextX), second, nextY);
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        Assert.That(await kernel.VerifyAsync(Query(factory, [affine], invariant), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant], preserved), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant], affine), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
    }
    private static VerificationQuery Query(IrFactory factory, IEnumerable<IrTerm> assumptions, IrTerm goal)
    {
        System.ArgumentNullException.ThrowIfNull(assumptions);
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(goal);
        return new(factory, assumptions.Select(predicate => predicate is null ? throw new System.ArgumentNullException(nameof(predicate)) : (new Assumption(factory, predicate,
            new LoweredJustification(factory.CreateOperation())))),
            new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
    }
}
