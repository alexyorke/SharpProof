using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class Pr275HornStateArgumentTests
{
    [Test]
    public void ARealSafeCounterRequiresAStateMapWithMatchingArity()
    {
        var (factory, _, relation, states, clauses) = CounterSystem();
        const uint rlimit = 1_000_000;
        Assert.That(HornInvariantSearch.Propose(factory, clauses, states, rlimit, TestContext.CurrentContext.CancellationToken),
            Does.ContainKey(relation));
        var wrongStates = new Dictionary<IrMemberId, ImmutableArray<IrTerm>>
        {
            [relation] = [.. states[relation], states[relation][0]]
        };
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            HornInvariantSearch.Propose(factory, clauses, wrongStates, rlimit, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Is.EqualTo("An invariant state must match its relation's arity."));
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
        if (member.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(member));
        }
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(state);
        return factory.PureOpaque(member, receiver: null, state);
    }

    private static IrTerm Equal(IrFactory factory, IrTerm left, IrTerm right)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(left);
        System.ArgumentNullException.ThrowIfNull(right);
        return factory.Binary(IrBinaryOperator.Equal, left, right);
    }

    private static Assumption Assume(IrFactory factory, IrTerm predicate)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(predicate);
        return new(factory, predicate, new LoweredJustification(factory.CreateOperation()));
    }

    private static VerificationQuery Query(IrFactory factory, IEnumerable<Assumption> assumptions, IrTerm goal)
    {
        System.ArgumentNullException.ThrowIfNull(assumptions);
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(goal);
        return new(factory, assumptions,
            new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
    }
}
