using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornUnknownDataflowTests
{
    [Test]
    public async Task RepeatedMaskedInputEstablishesTheSameBoundedCounterState()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var counter = factory.CreateVariable("counter", factory.IntegerType);
        var input = factory.CreateVariable("input", factory.IntegerType);
        var current = factory.Variable(counter);
        var masked = factory.Binary(IrBinaryOperator.BitwiseAnd, factory.Variable(input), factory.Integer(1));
        var assigned = factory.Binary(IrBinaryOperator.Equal, current, masked);
        var zero = factory.Binary(IrBinaryOperator.Equal, masked, factory.Integer(0));
        var belowThree = factory.Binary(IrBinaryOperator.LessThan, current, factory.Integer(3));
        var next = factory.Binary(IrBinaryOperator.Add, current, factory.Integer(1));
        var nonnegative = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, current, factory.Integer(0));
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:masked-input", factory.BooleanType, isStatic: true, factory.IntegerType);
        var atom = factory.PureOpaque(relation, null, current);
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>> { [relation] = [current] };
        VerificationQuery[] clauses =
        [
            Query(factory, [assigned, zero], atom),
            Query(factory, [atom, belowThree], factory.PureOpaque(relation, null, next)),
            Query(factory, [atom], nonnegative)
        ];

        var proposals = HornInvariantSearch.Propose(factory, clauses, states,
            IrSmtBackendOptions.DefaultQueryRlimit, CancellationToken.None);

        Assert.That(proposals, Does.ContainKey(relation));
        var invariant = proposals[relation];
        var preserved = IrSubstitution.Substitute(factory, invariant, counter, next);
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        Assert.That(await kernel.VerifyAsync(Query(factory, [assigned, zero], invariant)), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant, belowThree], preserved)), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant], nonnegative)), Is.TypeOf<ProvenOutcome>());
    }

    private static VerificationQuery Query(IrFactory factory, IEnumerable<IrTerm> assumptions, IrTerm goal)
    {
        return new(factory,
            assumptions.Select(predicate => new Assumption(factory, predicate,
                new LoweredJustification(factory.CreateOperation()))),
            new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
    }
}
