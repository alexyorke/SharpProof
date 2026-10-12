using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeStatisticsLifecycleTests
{
    [Test]
    public void FreshSolverMayOmitItsZeroResourceCounterUntilTheFirstCheck()
    {
        using var context = new Context();
        using var solver = context.MkSolver();

        Assert.That(SmtNativeUtilities.ReadResourceCount(solver), Is.Null);
        Assert.That(solver.Check(), Is.EqualTo(Status.SATISFIABLE));
        Assert.That(SmtNativeUtilities.ReadResourceCount(solver), Is.GreaterThan(0));
    }
}
