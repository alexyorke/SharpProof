using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornNumericStateTests
{
    [TestCase(8)]
    [TestCase(16)]
    [TestCase(32)]
    public async Task SignedWidenedCountdownPreservesItsNegatedSafetyBound(int width)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, true);
        var wideType = factory.GetOrCreateIntegerType(64, true);
        var variable = factory.CreateVariable("remaining", type);
        var current = factory.Variable(variable);
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:countdown", factory.BooleanType, isStatic: true, type);
        var atom = factory.PureOpaque(relation, null, current);
        var initial = factory.Binary(IrBinaryOperator.Equal, current, factory.Integer(type, 3));
        var guard = factory.Binary(IrBinaryOperator.GreaterThanOrEqual,
            factory.Cast(wideType, current), factory.Integer(wideType, 1));
        var next = factory.Binary(IrBinaryOperator.Subtract, current, factory.Integer(type, 1));
        var safety = factory.Binary(IrBinaryOperator.LessThanOrEqual,
            factory.Unary(IrUnaryOperator.Negate, current), factory.Integer(type, 0));
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>> { [relation] = [current] };
        VerificationQuery[] clauses =
        [
            Query(factory, [initial], atom),
            Query(factory, [atom, guard], factory.PureOpaque(relation, null, next)),
            Query(factory, [atom], safety)
        ];

        var proposals = HornInvariantSearch.Propose(factory, clauses, states,
            IrSmtBackendOptions.DefaultQueryRlimit, CancellationToken.None);

        Assert.That(proposals, Does.ContainKey(relation));
        var invariant = proposals[relation];
        var preserved = IrSubstitution.Substitute(factory, invariant, variable, next);
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        Assert.That(await kernel.VerifyAsync(Query(factory, [initial], invariant)), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant, guard], preserved)), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant], safety)), Is.TypeOf<ProvenOutcome>());
    }

    private static VerificationQuery Query(IrFactory factory, IEnumerable<IrTerm> assumptions, IrTerm goal)
    {
        return new(factory,
            assumptions.Select(predicate => new Assumption(factory, predicate,
                new LoweredJustification(factory.CreateOperation()))),
            new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
    }
}
