using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class TacticStatisticsLifecycleTests
{
    [Test]
    public void FreshTacticSolverCanOmitTheZeroResourceCounter()
    {
        using var context = new Context();
        using var tactic = context.MkTactic("skip");
        using var solver = context.MkSolver(tactic);

        Assert.That(SmtNativeUtilities.ReadResourceCount(solver), Is.Null);
        Assert.That(solver.Check(), Is.EqualTo(Status.SATISFIABLE));
    }
}
