using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornSigned64BoundaryStateTests
{
    [Test]
    public async Task CountupFromSigned64MinimumHasKernelCheckedBound()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(64, true);
        var variable = factory.CreateVariable("current", type);
        var current = factory.Variable(variable);
        var initial = factory.Binary(IrBinaryOperator.Equal, current, factory.Integer(type, long.MinValue));
        var limit = factory.Integer(type, long.MinValue + 2);
        var guard = factory.Binary(IrBinaryOperator.LessThan, current, limit);
        var next = factory.Binary(IrBinaryOperator.Add, current, factory.Integer(type, 1));
        var safety = factory.Binary(IrBinaryOperator.LessThanOrEqual, current, limit);
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:signed64-boundary", factory.BooleanType, isStatic: true, type);
        var atom = factory.PureOpaque(relation, null, current);
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>> { [relation] = [current] };
        VerificationQuery[] clauses =
        [
            Query(factory, [initial], atom),
            Query(factory, [atom, guard], factory.PureOpaque(relation, null, next)),
            Query(factory, [atom], safety)
        ];
        var proposals = HornInvariantSearch.Propose(factory, clauses, states,
            IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);
        Assert.That(proposals, Does.ContainKey(relation));
        var invariant = proposals[relation];
        var preserved = IrSubstitution.Substitute(factory, invariant, variable, next);
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        var established = await kernel.VerifyAsync(Query(factory, [initial], invariant), TestContext.CurrentContext.CancellationToken);
        Assert.That(established, Is.TypeOf<ProvenOutcome>(), OutcomeDescription(established));
        var preservedOutcome = await kernel.VerifyAsync(Query(factory, [invariant, guard], preserved), TestContext.CurrentContext.CancellationToken);
        Assert.That(preservedOutcome, Is.TypeOf<ProvenOutcome>(), OutcomeDescription(preservedOutcome));
        var safe = await kernel.VerifyAsync(Query(factory, [invariant], safety), TestContext.CurrentContext.CancellationToken);
        Assert.That(safe, Is.TypeOf<ProvenOutcome>(), OutcomeDescription(safe));
    }

    private static string? OutcomeDescription(ProofOutcome outcome)
    {
        System.ArgumentNullException.ThrowIfNull(outcome);
        return outcome is UnknownOutcome unknown ? unknown.Reason.ToString() : outcome.ToString();
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
