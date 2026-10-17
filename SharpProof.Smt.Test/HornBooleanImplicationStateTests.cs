using System.Collections.Immutable;
using IrMemberId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrMemberTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornBooleanImplicationStateTests
{
    [Test]
    public async Task ReadinessRequiresCommitmentAcrossStateSteps()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var readyVariable = factory.CreateVariable("ready", factory.BooleanType);
        var committedVariable = factory.CreateVariable("committed", factory.BooleanType);
        var ready = factory.Variable(readyVariable);
        var committed = factory.Variable(committedVariable);
        var safety = factory.Binary(IrBinaryOperator.OrElse,
            factory.Unary(IrUnaryOperator.Not, ready), committed);
        var nextReady = factory.Unary(IrUnaryOperator.Not, ready);
        var nextCommitted = factory.Binary(IrBinaryOperator.OrElse, committed, nextReady);
        var relation = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "invariant:commitment", factory.BooleanType, isStatic: true, factory.BooleanType, factory.BooleanType);
        var atom = factory.PureOpaque(relation, null, ready, committed);
        var states = new Dictionary<IrMemberId, ImmutableArray<IrTerm>> { [relation] = [ready, committed] };
        VerificationQuery[] clauses =
        [
            Query(factory, [safety], atom),
            Query(factory, [atom], factory.PureOpaque(relation, null, nextReady, nextCommitted)),
            Query(factory, [atom], safety)
        ];
        var proposals = HornInvariantSearch.Propose(factory, clauses, states,
            IrSmtBackendOptions.DefaultQueryRlimit, CancellationToken.None);
        Assert.That(proposals, Does.ContainKey(relation));
        var invariant = proposals[relation];
        var preserved = IrSubstitution.Substitute(factory,
            IrSubstitution.Substitute(factory, invariant, readyVariable, nextReady), committedVariable, nextCommitted);
        using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var kernel = new ProofKernel(backend);
        var established = await kernel.VerifyAsync(Query(factory, [safety], invariant), CancellationToken.None);
        Assert.That(established, Is.TypeOf<ProvenOutcome>(), OutcomeDescription(established));
        var preservedOutcome = await kernel.VerifyAsync(Query(factory, [invariant], preserved), CancellationToken.None);
        Assert.That(preservedOutcome, Is.TypeOf<ProvenOutcome>(), OutcomeDescription(preservedOutcome));
        var safe = await kernel.VerifyAsync(Query(factory, [invariant], safety), CancellationToken.None);
        Assert.That(safe, Is.TypeOf<ProvenOutcome>(), OutcomeDescription(safe));
    }

    private static string? OutcomeDescription(ProofOutcome outcome)
    {
        return outcome is UnknownOutcome unknown ? unknown.Reason.ToString() : outcome.ToString();
    }

    private static VerificationQuery Query(IrFactory factory, IEnumerable<IrTerm> assumptions, IrTerm goal)
    {
        return new(factory, assumptions.Select(predicate => new Assumption(factory, predicate,
            new LoweredJustification(factory.CreateOperation()))),
            new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
    }
}
