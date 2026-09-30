using System.Collections.Immutable;
using Microsoft.Z3;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class TotalBitVectorOracleTests
{
    [TestCase(8, false)]
    [TestCase(8, true)]
    [TestCase(16, false)]
    [TestCase(16, true)]
    [TestCase(32, false)]
    [TestCase(32, true)]
    [TestCase(64, false)]
    [TestCase(64, true)]
    public void TotalDivisionAndRemainderAgreeWithIndependentZ3Completion(int width, bool isSigned)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, isSigned);
        var x = factory.CreateVariable("x", type);
        var y = factory.CreateVariable("y", type);
        var mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        using var context = new Context();
        foreach (var left in new[] { 0UL, 1UL, mask, 1UL << (width - 1) })
        {
            foreach (var right in new[] { 0UL, 1UL, mask })
            {
                using var first = context.MkBV(left, (uint)width);
                using var second = context.MkBV(right, (uint)width);
                foreach (var operation in new[] { IrBinaryOperator.Divide, IrBinaryOperator.Remainder })
                {
                    using var expression = (operation, isSigned) switch
                    {
                        (IrBinaryOperator.Divide, true) => context.MkBVSDiv(first, second),
                        (IrBinaryOperator.Divide, false) => context.MkBVUDiv(first, second),
                        (IrBinaryOperator.Remainder, true) => context.MkBVSRem(first, second),
                        _ => context.MkBVURem(first, second)
                    };
                    using var simplified = expression.Simplify();
                    var expected = ((BitVecNum)simplified).UInt64;
                    var term = factory.Binary(operation, factory.Variable(x), factory.Variable(y));
                    var values = ImmutableDictionary<IrVarId, IrValue>.Empty
                        .Add(x, factory.CreateIntegerValueFromBits(type, left))
                        .Add(y, factory.CreateIntegerValueFromBits(type, right));
                    var actual = new IrInterpreter(factory).Evaluate(term, values);
                    var folded = (IrIntegerTerm)factory.Binary(operation,
                        factory.IntegerBits(type, left), factory.IntegerBits(type, right));
                    Assert.That(actual.Status, Is.EqualTo(IrEvaluationStatus.Value));
                    Assert.That(actual.Value!.IntegerBits, Is.EqualTo(expected), $"{width}/{isSigned}: {left} {operation} {right}");
                    Assert.That(folded.Bits, Is.EqualTo(expected));
                }
            }
        }
    }
}
