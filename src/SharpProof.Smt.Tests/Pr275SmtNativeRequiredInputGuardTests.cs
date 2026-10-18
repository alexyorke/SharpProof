using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class Pr275SmtNativeRequiredInputGuardTests
{
    [Test]
    public void ReadResourceCountRejectsNullSolverAndReadsAnActualSolver()
    {
        var error = Assert.Throws<ArgumentNullException>(
            (Action)(() => SmtNativeUtilities.ReadResourceCount(null!)));

        Assert.That(error!.ParamName, Is.EqualTo("solver"));

        using var context = new Context();
        using var solver = context.MkSolver();
        Assert.That(solver.Check(), Is.EqualTo(Status.SATISFIABLE));
        Assert.That(SmtNativeUtilities.ReadResourceCount(solver), Is.Not.Null);
    }

    [Test]
    public void AddOwnedParameterRejectsNullNameAndConfiguresActualNativeObjects()
    {
        using var context = new Context();
        using var parameters = context.MkParams();
        var error = Assert.Throws<ArgumentNullException>(
            (Action)(() => SmtNativeUtilities.AddOwnedParameter(parameters, null!, 1_000_000)));

        Assert.That(error!.ParamName, Is.EqualTo("name"));
        AssertOwnedParameterAllowsAnActualSolverCheck(context);
    }

    [Test]
    public void AddOwnedParameterRejectsNullParametersWithAnActualOwnedSymbol()
    {
        using var context = new Context();
        var error = Assert.Throws<ArgumentNullException>(
            (Action)(() => SmtNativeUtilities.AddOwnedParameter(null!, context.MkSymbol("rlimit"), 1_000_000)));

        Assert.That(error!.ParamName, Is.EqualTo("parameters"));
        AssertOwnedParameterAllowsAnActualSolverCheck(context);
    }

    [Test]
    public void CreateBooleanValueRejectsNullFactoryAndDecodesAnActualBoolean()
    {
        using var context = new Context();
        using var expression = context.MkTrue();
        var error = Assert.Throws<ArgumentNullException>(
            (Action)(() => SmtNativeUtilities.CreateBooleanValue(null!, expression)));

        Assert.That(error!.ParamName, Is.EqualTo("factory"));

        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = SmtNativeUtilities.CreateBooleanValue(factory, expression);
        Assert.That(value, Is.Not.Null);
        Assert.That(value!.Boolean, Is.True);
    }

    [Test]
    public void NativeCheckRejectsNullAssumptionsAndChecksAnActualQuery()
    {
        using var context = new Context();
        using var solver = context.MkSolver();
        var meter = new SmtQueryResourceMeter(1_000_000, TestContext.CurrentContext.CancellationToken);
        var error = Assert.Throws<ArgumentNullException>(
            (Action)(() => SmtNativeCheck.Run(solver, null!, meter)));

        Assert.That(error!.ParamName, Is.EqualTo("assumptions"));
        AssertNativeCheckAcceptsActualZ3Inputs(context, solver);
    }

    [Test]
    public void NativeCheckRejectsNullMeterAndChecksAnActualQuery()
    {
        using var context = new Context();
        using var solver = context.MkSolver();
        using var assumption = context.MkTrue();
        var error = Assert.Throws<ArgumentNullException>(
            (Action)(() => SmtNativeCheck.Run(solver, [assumption], null!)));

        Assert.That(error!.ParamName, Is.EqualTo("meter"));
        AssertNativeCheckAcceptsActualZ3Inputs(context, solver);
    }

    private static void AssertOwnedParameterAllowsAnActualSolverCheck(Context context)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        using var solver = context.MkSolver();
        using var parameters = context.MkParams();
        SmtNativeUtilities.AddOwnedParameter(parameters, context.MkSymbol("rlimit"), 1_000_000);
        solver.Parameters = parameters;
        using var truth = context.MkTrue();
        solver.Assert(truth);

        Assert.That(solver.Check(), Is.EqualTo(Status.SATISFIABLE));
    }

    private static void AssertNativeCheckAcceptsActualZ3Inputs(Context context, Solver solver)
    {
        System.ArgumentNullException.ThrowIfNull(context);
        System.ArgumentNullException.ThrowIfNull(solver);
        using var assumption = context.MkTrue();
        var meter = new SmtQueryResourceMeter(1_000_000, TestContext.CurrentContext.CancellationToken);

        Assert.That(SmtNativeCheck.Run(solver, [assumption], meter), Is.EqualTo(Status.SATISFIABLE));
        Assert.That(meter.Consumed, Is.GreaterThan(0));
    }
}
