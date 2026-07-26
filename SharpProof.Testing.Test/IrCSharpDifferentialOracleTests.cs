using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Testing.Test;

[TestFixture]
public sealed class IrCSharpDifferentialOracleTests {
    [Test]
    public void GeneratedTermsAgreeWithCompiledCSharp() {
        var factory = new IrFactory();
        var generator = new WellSortedIrGenerator(factory, seed: 0x5A17);
        var oracle = new IrCSharpDifferentialOracle(factory);

        for (var index = 0; index < 100; index++) {
            var generated = generator.Next(maximumDepth: 4);
            var result = oracle.Compare(generated.Term, generated.Variables);
            Assert.That(
                result.Status,
                Is.EqualTo(DifferentialStatus.Agreement),
                $"case {index}: {result.Detail}; term " +
                new IrPrinter(factory).Print(generated.Term));
        }
    }

    [Test]
    public void ShortCircuitSkipsDivisionByZero() {
        var factory = new IrFactory();
        var dangerous = factory.Binary(
            IrBinaryOperator.Equal,
            factory.Binary(
                IrBinaryOperator.Divide,
                factory.Integer(1),
                factory.Integer(0)),
            factory.Integer(0));
        var term = factory.Binary(
            IrBinaryOperator.AndAlso,
            factory.Boolean(false),
            dangerous);

        var result = new IrCSharpDifferentialOracle(factory).Compare(
            term,
            new Dictionary<IrVarId, IrValue>());

        Assert.That(result.Status, Is.EqualTo(DifferentialStatus.Agreement));
        Assert.That(result.Interpreted.Value!.Boolean, Is.False);
    }

    [Test]
    public void OpaqueTermsAbstain() {
        var factory = new IrFactory();
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "Opaque",
            factory.IntegerType,
            isStatic: true);
        var term = factory.PureOpaque(member, receiver: null);

        var result = new IrCSharpDifferentialOracle(factory).Compare(
            term,
            new Dictionary<IrVarId, IrValue>());

        Assert.That(result.Status, Is.EqualTo(DifferentialStatus.Abstained));
    }
}
