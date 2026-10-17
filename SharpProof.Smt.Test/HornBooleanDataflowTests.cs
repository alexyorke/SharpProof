using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornBooleanDataflowTests
{
    [Test]
    public async Task OneStepBooleanPhaseAgreesWithItsCounterInEveryReachableState()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var phase = factory.CreateVariable("done", factory.BooleanType);
        var counter = factory.CreateVariable("counter", factory.IntegerType);
        var done = factory.Variable(phase);
        var current = factory.Variable(counter);
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:phase", factory.BooleanType, isStatic: true, factory.BooleanType, factory.IntegerType);
        var atom = factory.PureOpaque(relation, null, done, current);
        var notDone = factory.Unary(IrUnaryOperator.Not, done);
        var zero = factory.Binary(IrBinaryOperator.Equal, current, factory.Integer(0));
        var next = factory.Binary(IrBinaryOperator.Add, current, factory.Integer(1));
        var one = factory.Binary(IrBinaryOperator.Equal, current, factory.Integer(1));
        var safety = factory.Binary(IrBinaryOperator.Equal, done, one);
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>> { [relation] = [done, current] };
        VerificationQuery[] clauses =
        [
            Query(factory, [notDone, zero], atom),
            Query(factory, [atom, notDone], factory.PureOpaque(relation, null, factory.Boolean(true), next)),
            Query(factory, [atom], safety)
        ];
        var proposals = HornInvariantSearch.Propose(factory, clauses, states,
            IrSmtBackendOptions.DefaultQueryRlimit, CancellationToken.None);
        Assert.That(proposals, Does.ContainKey(relation));
        var invariant = proposals[relation];
        var preserved = IrSubstitution.Substitute(factory,
            IrSubstitution.Substitute(factory, invariant, phase, factory.Boolean(true)), counter, next);
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        Assert.That(await kernel.VerifyAsync(Query(factory, [notDone, zero], invariant)), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant, notDone], preserved)), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant], safety)), Is.TypeOf<ProvenOutcome>());
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
