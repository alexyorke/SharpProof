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
    public async Task TotalIntegerOperatorsAgreeWithIndependentZ3Completion(int width, bool isSigned)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, isSigned);
        var x = factory.CreateVariable("x", type);
        var y = factory.CreateVariable("y", type);
        var mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        using var context = new Context();
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        foreach (var left in new[] { 0UL, 1UL, mask, 1UL << (width - 1), 0xAAAAAAAAAAAAAAAAUL & mask })
        {
            foreach (var right in new[] { 0UL, 1UL, mask, 0x5555555555555555UL & mask })
            {
                using var first = context.MkBV(left, (uint)width);
                using var second = context.MkBV(right, (uint)width);
                foreach (var operation in new[] { IrBinaryOperator.Divide, IrBinaryOperator.Remainder, IrBinaryOperator.BitwiseAnd })
                {
                    using var expression = (operation, isSigned) switch
                    {
                        (IrBinaryOperator.Divide, true) => context.MkBVSDiv(first, second),
                        (IrBinaryOperator.Divide, false) => context.MkBVUDiv(first, second),
                        (IrBinaryOperator.Remainder, true) => context.MkBVSRem(first, second),
                        (IrBinaryOperator.BitwiseAnd, _) => context.MkBVAND(first, second),
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
                    var assumptions = new[]
                    {
                        new Assumption(factory, factory.Binary(IrBinaryOperator.Equal, factory.Variable(x), factory.IntegerBits(type, left)),
                            new LoweredJustification(factory.CreateOperation())),
                        new Assumption(factory, factory.Binary(IrBinaryOperator.Equal, factory.Variable(y), factory.IntegerBits(type, right)),
                            new LoweredJustification(factory.CreateOperation()))
                    };
                    var query = new VerificationQuery(factory, assumptions,
                        new SharpProof.Verify.Goal(factory, factory.Binary(IrBinaryOperator.AndAlso,
                            factory.Binary(IrBinaryOperator.Equal, term, factory.IntegerBits(type, expected)),
                            factory.Binary(IrBinaryOperator.Equal, factory.Binary(operation, factory.IntegerBits(type, left), factory.Variable(y)),
                                factory.IntegerBits(type, expected))),
                            ProofDiagnosticKind.InternalConsistency, new SourceLocationId(0)));
                    var solved = await session.CheckAsync(query, CancellationToken.None);
                    Assert.That(solved.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable),
                        $"encoder {width}/{isSigned}: {left} {operation} {right}");
                }
            }
        }
    }

    // Expected values come from the CLR's own shift operators, which mask the
    // count to the low five (32-bit) or six (64-bit) bits.
    [TestCase(32, false)]
    [TestCase(32, true)]
    [TestCase(64, false)]
    [TestCase(64, true)]
    public async Task TotalShiftsAgreeWithClrShiftOperators(int width, bool isSigned)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, isSigned);
        var x = factory.CreateVariable("x", type);
        var y = factory.CreateVariable("y", type);
        var mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        foreach (var left in new[] { 0UL, 1UL, mask, 1UL << (width - 1), 0xAAAAAAAAAAAAAAAAUL & mask, 0x0123456789ABCDEFUL & mask })
        {
            foreach (var count in new[] { 0, 1, 7, 31, 32, 33, 63, 64, 65, -1, -32, -33, int.MinValue, int.MaxValue })
            {
                var right = unchecked((ulong)(long)count) & mask;
                foreach (var operation in new[] { IrBinaryOperator.ShiftLeft, IrBinaryOperator.ShiftRight })
                {
                    var left32 = unchecked((uint)left);
                    var expected = (width, isSigned, operation == IrBinaryOperator.ShiftLeft) switch
                    {
                        (32, true, true) => unchecked((uint)((int)left32 << count)),
                        (32, true, false) => unchecked((uint)((int)left32 >> count)),
                        (32, false, true) => left32 << count,
                        (32, false, false) => left32 >> count,
                        (_, true, true) => unchecked((ulong)((long)left << count)),
                        (_, true, false) => unchecked((ulong)((long)left >> count)),
                        (_, false, true) => left << count,
                        _ => left >> count
                    };
                    var term = factory.Binary(operation, factory.Variable(x), factory.Variable(y));
                    var values = ImmutableDictionary<IrVarId, IrValue>.Empty
                        .Add(x, factory.CreateIntegerValueFromBits(type, left))
                        .Add(y, factory.CreateIntegerValueFromBits(type, right));
                    var actual = new IrInterpreter(factory).Evaluate(term, values);
                    var folded = (IrIntegerTerm)factory.Binary(operation,
                        factory.IntegerBits(type, left), factory.IntegerBits(type, right));
                    Assert.That(actual.Status, Is.EqualTo(IrEvaluationStatus.Value));
                    Assert.That(actual.Value!.IntegerBits, Is.EqualTo(expected), $"{width}/{isSigned}: {left} {operation} {count}");
                    Assert.That(folded.Bits, Is.EqualTo(expected));
                    var assumptions = new[]
                    {
                        new Assumption(factory, factory.Binary(IrBinaryOperator.Equal, factory.Variable(x), factory.IntegerBits(type, left)),
                            new LoweredJustification(factory.CreateOperation())),
                        new Assumption(factory, factory.Binary(IrBinaryOperator.Equal, factory.Variable(y), factory.IntegerBits(type, right)),
                            new LoweredJustification(factory.CreateOperation()))
                    };
                    var query = new VerificationQuery(factory, assumptions,
                        new SharpProof.Verify.Goal(factory, factory.Binary(IrBinaryOperator.Equal, term, factory.IntegerBits(type, expected)),
                            ProofDiagnosticKind.InternalConsistency, new SourceLocationId(0)));
                    var solved = await session.CheckAsync(query, CancellationToken.None);
                    Assert.That(solved.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable),
                        $"encoder {width}/{isSigned}: {left} {operation} {count}");
                }
            }
        }
    }

    [TestCase(8)]
    [TestCase(16)]
    public void NarrowShiftsHaveNoClrMeaning(int width)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, true);
        var x = factory.CreateVariable("x", type);
        var term = factory.Binary(IrBinaryOperator.ShiftLeft, factory.Variable(x), factory.IntegerBits(type, 1));
        var values = ImmutableDictionary<IrVarId, IrValue>.Empty.Add(x, factory.CreateIntegerValueFromBits(type, 1));
        Assert.That(new IrInterpreter(factory).Evaluate(term, values).Status, Is.Not.EqualTo(IrEvaluationStatus.Value));
    }
}
