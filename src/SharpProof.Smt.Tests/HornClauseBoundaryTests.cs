using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornClauseBoundaryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CompoundRelationHeadsPreserveTheEstablishedCounterBound(bool guarded)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("counter", factory.IntegerType);
        var current = factory.Variable(variable);
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:compound", factory.BooleanType, isStatic: true, factory.IntegerType);
        var atom = factory.PureOpaque(relation, null, current);
        var nonnegative = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, current, factory.Integer(0));
        var zero = factory.Binary(IrBinaryOperator.Equal, current, factory.Integer(0));
        var next = factory.Binary(IrBinaryOperator.Add, current, factory.Integer(1));
        var head = guarded
            ? factory.Binary(IrBinaryOperator.OrElse, factory.Unary(IrUnaryOperator.Not, zero), atom)
            : factory.Binary(IrBinaryOperator.AndAlso, atom, nonnegative);
        var body = factory.Binary(IrBinaryOperator.AndAlso, atom, nonnegative);
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>> { [relation] = [current] };
        VerificationQuery[] clauses =
        [
            Query(factory, [zero], head),
            Query(factory, [body, factory.Binary(IrBinaryOperator.LessThan, current, factory.Integer(3))],
                factory.PureOpaque(relation, null, next)),
            Query(factory, [atom], nonnegative)
        ];

        var proposals = HornInvariantSearch.Propose(factory, clauses, states,
            IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);

        Assert.That(proposals, Does.ContainKey(relation));
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        Assert.That(await kernel.VerifyAsync(Query(factory, [zero], proposals[relation]), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [proposals[relation]], nonnegative), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
        var preserved = IrSubstitution.Substitute(factory, proposals[relation], variable, next);
        Assert.That(await kernel.VerifyAsync(Query(factory,
            [proposals[relation], factory.Binary(IrBinaryOperator.LessThan, current, factory.Integer(3))],
            preserved), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public void NegatedRelationHeadCannotProduceAProposal()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("counter", factory.IntegerType);
        var current = factory.Variable(variable);
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:negative", factory.BooleanType, isStatic: true, factory.IntegerType);
        var atom = factory.PureOpaque(relation, null, current);
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>> { [relation] = [current] };

        var proposals = HornInvariantSearch.Propose(factory,
            [Query(factory, [], factory.Unary(IrUnaryOperator.Not, atom))], states,
            IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);

        Assert.That(proposals, Is.Empty);
    }

    [Test]
    public void ExhaustedSearchBudgetCannotPublishAnUncheckedProposal()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("counter", factory.IntegerType);
        var current = factory.Variable(variable);
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:budget", factory.BooleanType, isStatic: true, factory.IntegerType);
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>> { [relation] = [current] };

        var proposals = HornInvariantSearch.Propose(factory,
            [Query(factory, [], factory.PureOpaque(relation, null, current))], states, 1, TestContext.CurrentContext.CancellationToken);

        Assert.That(proposals, Is.Empty);
    }

    [Test]
    public async Task AffineTwoVariableLoopProposalPassesEstablishmentPreservationAndSafety()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var index = factory.CreateVariable("index", factory.IntegerType);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var currentIndex = factory.Variable(index);
        var currentValue = factory.Variable(value);
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:affine", factory.BooleanType, isStatic: true, factory.IntegerType, factory.IntegerType);
        var atom = factory.PureOpaque(relation, null, currentIndex, currentValue);
        var initialIndex = factory.Binary(IrBinaryOperator.Equal, currentIndex, factory.Integer(0));
        var initialValue = factory.Binary(IrBinaryOperator.Equal, currentValue, factory.Integer(1));
        var nextIndex = factory.Binary(IrBinaryOperator.Add, currentIndex, factory.Integer(1));
        var nextValue = factory.Binary(IrBinaryOperator.Add, currentValue, factory.Integer(2));
        var affine = factory.Binary(IrBinaryOperator.Equal, currentValue,
            factory.Binary(IrBinaryOperator.Add,
                factory.Binary(IrBinaryOperator.Multiply, factory.Integer(2), currentIndex), factory.Integer(1)));
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>>
        {
            [relation] = [currentIndex, currentValue]
        };
        VerificationQuery[] clauses =
        [
            Query(factory, [initialIndex, initialValue], atom),
            Query(factory, [atom], factory.PureOpaque(relation, null, nextIndex, nextValue)),
            Query(factory, [atom], affine)
        ];

        var proposals = HornInvariantSearch.Propose(factory, clauses, states,
            IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);

        Assert.That(proposals, Does.ContainKey(relation));
        var invariant = proposals[relation];
        var preserved = IrSubstitution.Substitute(factory, invariant,
            new Dictionary<IrVarId, IrTerm> { [index] = nextIndex, [value] = nextValue });
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        Assert.That(await kernel.VerifyAsync(Query(factory, [initialIndex, initialValue], invariant), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant], preserved), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
        Assert.That(await kernel.VerifyAsync(Query(factory, [invariant], affine), TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
    }

    private static VerificationQuery Query(IrFactory factory, IEnumerable<IrTerm> assumptions, IrTerm goal)
    {
        System.ArgumentNullException.ThrowIfNull(assumptions);
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(goal);
        return new(factory,
            assumptions.Select(predicate => predicate is null ? throw new System.ArgumentNullException(nameof(predicate)) : (new Assumption(factory, predicate,
                new LoweredJustification(factory.CreateOperation())))),
            new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
    }
}
