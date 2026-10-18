using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeValueAdmissionTests
{
    [Test]
    public void PartialBooleanModelDoesNotBecomeAConcreteWitness()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var solver = context.MkSolver();
        using var predicate = context.MkBoolConst("unassigned");
        Assert.That(solver.Check(), Is.EqualTo(Status.SATISFIABLE));
        using var model = solver.Model;
        using var evaluated = model.Evaluate(predicate, completion: false);

        Assert.That(BvEncoder.CreateValue(factory, factory.BooleanType, evaluated), Is.Null);
    }

    [Test]
    public void ArithmeticIntegerModelCannotMasqueradeAsABitvectorWitness()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var solver = context.MkSolver();
        using var value = context.MkIntConst("arithmetic");
        using var seven = context.MkInt(7);
        using var equation = context.MkEq(value, seven);
        solver.Assert(equation);
        Assert.That(solver.Check(), Is.EqualTo(Status.SATISFIABLE));
        using var model = solver.Model;
        using var evaluated = model.Evaluate(value, completion: true);

        Assert.That(BvEncoder.CreateValue(factory, factory.IntegerType, evaluated), Is.Null);
        Assert.That(BvEncoder.CreateValue(factory, factory.BooleanType, evaluated), Is.Null);
    }

    [Test]
    public void WrongWidthBitvectorCannotBecomeAnIntegerWitness()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var value = context.MkBV(7, 16);

        Assert.That(BvEncoder.CreateValue(factory, factory.IntegerType, value), Is.Null);
    }
}
