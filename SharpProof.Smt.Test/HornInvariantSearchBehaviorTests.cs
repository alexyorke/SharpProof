using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornInvariantSearchBehaviorTests
{
    [Test]
    public async Task BoundedCounterProducesKernelProvableSafetyProposal()
    {
        var (factory, counter, relation, states, clauses) = CounterSystem();
        var proposals = HornInvariantSearch.Propose(factory, clauses, states, 1_000_000, CancellationToken.None);

        Assert.That(proposals, Does.ContainKey(relation));
        var invariant = proposals[relation];
        using var solver = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(solver);
        var nonnegative = factory.Binary(IrBinaryOperator.GreaterThanOrEqual,
            factory.Variable(counter), factory.Integer(0));
        var initialization = Query(factory,
            [Assume(factory, Equal(factory, factory.Variable(counter), factory.Integer(0)))], invariant);
        var safety = Query(factory, [Assume(factory, invariant)], nonnegative);

        Assert.That(await kernel.VerifyAsync(initialization), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(safety), Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public void ReachableCounterexampleCannotYieldAnInvariantProposal()
    {
        var (factory, counter, relation, states, clauses) = CounterSystem();
        var falseGoal = factory.Binary(IrBinaryOperator.LessThan,
            factory.Variable(counter), factory.Integer(3));
        var unsafeClauses = clauses.Take(2).Append(Query(factory,
            [Assume(factory, Relation(factory, relation, factory.Variable(counter)))], falseGoal)).ToArray();

        var proposals = HornInvariantSearch.Propose(factory, unsafeClauses, states, 1_000_000, CancellationToken.None);

        Assert.That(proposals, Is.Empty);
    }

    private static (IrFactory Factory, IrVarId Counter, IrMemberId Relation,
        Dictionary<IrMemberId, ImmutableArray<IrTerm>> States, VerificationQuery[] Clauses) CounterSystem()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var counter = factory.CreateVariable("counter", factory.IntegerType);
        var current = factory.Variable(counter);
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:counter", factory.BooleanType, isStatic: true, factory.IntegerType);
        var state = new Dictionary<IrMemberId, ImmutableArray<IrTerm>>
        {
            [relation] = [current]
        };
        var atZero = Equal(factory, current, factory.Integer(0));
        var belowThree = factory.Binary(IrBinaryOperator.LessThan, current, factory.Integer(3));
        var next = factory.Binary(IrBinaryOperator.Add, current, factory.Integer(1));
        var nonnegative = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, current, factory.Integer(0));
        VerificationQuery[] clauses =
        [
            Query(factory, [Assume(factory, atZero)], Relation(factory, relation, current)),
            Query(factory, [Assume(factory, Relation(factory, relation, current)), Assume(factory, belowThree)],
                Relation(factory, relation, next)),
            Query(factory, [Assume(factory, Relation(factory, relation, current))], nonnegative)
        ];
        return (factory, counter, relation, state, clauses);
    }

    private static IrOpaqueTerm Relation(IrFactory factory, IrMemberId member, IrTerm state)
    {
        return factory.PureOpaque(member, receiver: null, state);
    }

    private static IrTerm Equal(IrFactory factory, IrTerm left, IrTerm right)
    {
        return factory.Binary(IrBinaryOperator.Equal, left, right);
    }

    private static Assumption Assume(IrFactory factory, IrTerm predicate)
    {
        return new(factory, predicate, new LoweredJustification(factory.CreateOperation()));
    }

    private static VerificationQuery Query(IrFactory factory, IEnumerable<Assumption> assumptions, IrTerm goal)
    {
        return new(factory, assumptions,
            new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
    }
}
