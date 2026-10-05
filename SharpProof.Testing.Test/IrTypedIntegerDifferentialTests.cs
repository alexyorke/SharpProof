using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Testing.Test;

[TestFixture]
public sealed class IrTypedIntegerDifferentialTests
{
    [TestCase(8, true)]
    [TestCase(8, false)]
    [TestCase(16, true)]
    [TestCase(16, false)]
    [TestCase(32, true)]
    [TestCase(32, false)]
    [TestCase(64, true)]
    [TestCase(64, false)]
    public void WidthBoundariesAgreeWithCompiledCSharp(int width, bool isSigned)
    {
        var factory = new IrFactory();
        var type = factory.GetOrCreateIntegerType(width, isSigned);
        var left = factory.CreateVariable("left", type);
        var right = factory.CreateVariable("right", type);
        var oracle = new IrCSharpDifferentialOracle(factory);
        var mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        var highBit = 1UL << (width - 1);
        (ulong Left, ulong Right)[] pairs = [(mask, 1), (highBit, mask), (0, 0),
            (highBit, 1), (mask >> 1, 1), (mask, 2), (mask, mask)];
        IrBinaryOperator[] operations = [IrBinaryOperator.Add, IrBinaryOperator.Subtract,
            IrBinaryOperator.Multiply, IrBinaryOperator.Divide, IrBinaryOperator.Remainder,
            IrBinaryOperator.LessThan, IrBinaryOperator.GreaterThanOrEqual, IrBinaryOperator.Equal];
        foreach (var operation in operations)
        {
            var term = factory.Binary(operation, factory.Variable(left), factory.Variable(right));
            foreach (var pair in pairs)
            {
                var values = new Dictionary<IrVarId, IrValue>
                {
                    [left] = factory.CreateIntegerValueFromBits(type, pair.Left),
                    [right] = factory.CreateIntegerValueFromBits(type, pair.Right)
                };
                var result = oracle.Compare(term, values);
                Assert.That(result.Status, Is.EqualTo(DifferentialStatus.Agreement),
                    $"{width}/{isSigned} {operation} ({pair.Left},{pair.Right}): {result.Detail}");
                var folded = factory.Binary(operation, factory.IntegerBits(type, pair.Left),
                    factory.IntegerBits(type, pair.Right));
                AssertSameResult(new IrInterpreter(factory).Evaluate(folded), result.Interpreted);
            }
        }
        var negation = factory.Unary(IrUnaryOperator.Negate, factory.Variable(left));
        foreach (var bits in new[] { 0UL, highBit, mask })
        {
            var result = oracle.Compare(negation,
                new Dictionary<IrVarId, IrValue> { [left] = factory.CreateIntegerValueFromBits(type, bits) });
            Assert.That(result.Status, Is.EqualTo(DifferentialStatus.Agreement), result.Detail);
        }
    }

    [Test]
    public void SignedExtensionAndUnsignedTruncationAgreeWithCompiledCSharp()
    {
        var factory = new IrFactory();
        var oracle = new IrCSharpDifferentialOracle(factory);
        foreach (var source in new[] { factory.GetOrCreateIntegerType(8, true),
                     factory.GetOrCreateIntegerType(64, false) })
        {
            var variable = factory.CreateVariable("source", source);
            var bits = factory.GetTypeInfo(source).Width == 8 ? 0xffUL : ulong.MaxValue;
            var values = new Dictionary<IrVarId, IrValue>
            {
                [variable] = factory.CreateIntegerValueFromBits(source, bits)
            };
            foreach (var width in new[] { 8, 16, 32, 64 })
            {
                foreach (var isSigned in new[] { false, true })
                {
                    var target = factory.GetOrCreateIntegerType(width, isSigned);
                    var result = oracle.Compare(factory.Cast(target, factory.Variable(variable)), values);
                    Assert.That(result.Status, Is.EqualTo(DifferentialStatus.Agreement),
                        $"cast to {width}/{isSigned}: {result.Detail}");
                    AssertSameResult(new IrInterpreter(factory).Evaluate(
                        factory.Cast(target, factory.IntegerBits(source, bits))), result.Interpreted);
                }
            }
        }
    }

    [Test]
    public void TypedRuntimeSmokeBoundaryCases()
    {
        using var assertions = Assert.EnterMultipleScope();
        var factory = new IrFactory();
        var oracle = new IrCSharpDifferentialOracle(factory);
        foreach (var width in new[] { 32, 64 })
        {
            var type = factory.GetOrCreateIntegerType(width, true);
            var left = factory.CreateVariable("left", type);
            var right = factory.CreateVariable("right", type);
            var mask = width == 64 ? ulong.MaxValue : uint.MaxValue;
            var values = new Dictionary<IrVarId, IrValue>
            {
                [left] = factory.CreateIntegerValueFromBits(type, 1UL << (width - 1)),
                [right] = factory.CreateIntegerValueFromBits(type, mask)
            };
            foreach (var operation in new[] { IrBinaryOperator.Divide, IrBinaryOperator.Remainder })
            {
                var result = oracle.Compare(factory.Binary(operation,
                    factory.Variable(left), factory.Variable(right)), values);
                Assert.That(result.Status, Is.EqualTo(DifferentialStatus.Agreement),
                    $"signed {width} minimum {operation} -1: {result.Detail}");
            }
        }
        var unsigned = factory.GetOrCreateIntegerType(64, false);
        var input = factory.CreateVariable("input", unsigned);
        var wrap = oracle.Compare(factory.Binary(IrBinaryOperator.Add,
                factory.Variable(input), factory.Integer(unsigned, 1UL)),
            new Dictionary<IrVarId, IrValue>
            {
                [input] = factory.CreateIntegerValue(unsigned, ulong.MaxValue)
            });
        Assert.That(wrap.Status, Is.EqualTo(DifferentialStatus.Agreement), wrap.Detail);
    }

    private static void AssertSameResult(IrEvaluationResult folded, IrEvaluationResult evaluated)
    {
        Assert.That(folded.Status, Is.EqualTo(evaluated.Status));
        if (folded.Status == IrEvaluationStatus.Exception)
        {
            Assert.That(folded.Exception!.Kind, Is.EqualTo(evaluated.Exception!.Kind));
        }
        else if (folded.Value!.Kind == IrValueKind.Integer)
        {
            Assert.That(folded.Value.Type, Is.EqualTo(evaluated.Value!.Type));
            Assert.That(folded.Value.IntegerBits, Is.EqualTo(evaluated.Value.IntegerBits));
        }
        else
        {
            Assert.That(folded.Value.Boolean, Is.EqualTo(evaluated.Value!.Boolean));
        }
    }
}
