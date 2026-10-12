using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornUnknownMemoizationTests
{
    [Test]
    public async Task RepeatedMaskedStateRetainsTheSameUnknownAcrossHornClauses()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("counter", factory.IntegerType);
        var masked = factory.Binary(IrBinaryOperator.BitwiseAnd, factory.Variable(variable), factory.Integer(1));
        var zero = factory.Binary(IrBinaryOperator.Equal, masked, factory.Integer(0));
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:masked", factory.BooleanType, isStatic: true, factory.IntegerType);
        var atom = factory.PureOpaque(relation, null, masked);
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>> { [relation] = [masked] };
        VerificationQuery[] clauses =
        [
            Query(factory, [zero], atom),
            Query(factory, [atom], atom),
            Query(factory, [atom], zero)
        ];

        var proposals = HornInvariantSearch.Propose(factory, clauses, states,
            IrSmtBackendOptions.DefaultQueryRlimit, CancellationToken.None);

        Assert.That(proposals, Does.ContainKey(relation));
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        var invariant = proposals[relation];
        Assert.That(await kernel.VerifyAsync(Query(factory, [zero], invariant)), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant], zero)), Is.TypeOf<ProvenOutcome>());
    }

    private static VerificationQuery Query(IrFactory factory, IEnumerable<IrTerm> assumptions, IrTerm goal)
    {
        return new(factory,
            assumptions.Select(predicate => new Assumption(factory, predicate,
                new LoweredJustification(factory.CreateOperation()))),
            new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
    }
}
