using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornAffineStateTests
{
    [Test]
    public async Task TwoStateCounterPreservesItsDoubledAffineRelation()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateVariable("first", factory.IntegerType);
        var second = factory.CreateVariable("second", factory.IntegerType);
        var x = factory.Variable(first);
        var y = factory.Variable(second);
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:affine", factory.BooleanType, isStatic: true, factory.IntegerType, factory.IntegerType);
        var atom = factory.PureOpaque(relation, null, x, y);
        var initialX = factory.Binary(IrBinaryOperator.Equal, x, factory.Integer(0));
        var initialY = factory.Binary(IrBinaryOperator.Equal, y, factory.Integer(0));
        var guard = factory.Binary(IrBinaryOperator.LessThan, x, factory.Integer(3));
        var nextX = factory.Binary(IrBinaryOperator.Add, x, factory.Integer(1));
        var nextY = factory.Binary(IrBinaryOperator.Add, y, factory.Integer(2));
        var safety = factory.Binary(IrBinaryOperator.Equal, y,
            factory.Binary(IrBinaryOperator.Multiply, x, factory.Integer(2)));
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>> { [relation] = [x, y] };
        VerificationQuery[] clauses =
        [
            Query(factory, [initialX, initialY], atom),
            Query(factory, [atom, guard], factory.PureOpaque(relation, null, nextX, nextY)),
            Query(factory, [atom], safety)
        ];
        var proposals = HornInvariantSearch.Propose(factory, clauses, states,
            IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);
        Assert.That(proposals, Does.ContainKey(relation));
        var invariant = proposals[relation];
        var preserved = IrSubstitution.Substitute(factory,
            IrSubstitution.Substitute(factory, invariant, first, nextX), second, nextY);
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        Assert.That(await kernel.VerifyAsync(Query(factory, [initialX, initialY], invariant), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant, guard], preserved), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant], safety), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
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
