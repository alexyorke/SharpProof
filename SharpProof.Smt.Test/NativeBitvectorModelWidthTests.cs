using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeBitvectorModelWidthTests
{
    [TestCase(1)]
    [TestCase(8)]
    [TestCase(16)]
    [TestCase(32)]
    [TestCase(64)]
    public void ActualNativeModelNumeralRequiresASupportedMatchingIrWidth(int width)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width == 1 ? 32 : width, true);
        var bits = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        using var context = new Context();
        using var solver = context.MkSolver();
        using var variable = context.MkBVConst("native-model-value", (uint)width);
        using var literal = context.MkBV(bits, (uint)width);
        using var equation = context.MkEq(variable, literal);
        solver.Assert(equation);
        Assert.That(solver.Check(), Is.EqualTo(Status.SATISFIABLE));
        using var model = solver.Model;
        using var evaluated = model.Evaluate(variable, completion: true);
        var value = BvEncoder.CreateValue(factory, type, evaluated);
        if (width == 1)
        {
            Assert.That(value, Is.Null);
        }
        else
        {
            Assert.That(value, Is.Not.Null);
            Assert.That(value!.Type, Is.EqualTo(type));
            Assert.That(value.IntegerBits, Is.EqualTo(bits));
        }
    }
}
