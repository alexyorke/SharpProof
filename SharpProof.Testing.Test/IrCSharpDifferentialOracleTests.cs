using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Testing.Test;

[TestFixture]
public sealed class IrCSharpDifferentialOracleTests
{
    [Test]
    public void GeneratedTermsAgreeWithCompiledCSharp()
    {
        var factory = new IrFactory();
        var generator = new WellSortedIrGenerator(factory, seed: 0x5A17);
        var oracle = new IrCSharpDifferentialOracle(factory);
        var categories = new HashSet<GeneratedIrCategory>();

        for (var index = 0; index < 200; index++)
        {
            var generated = generator.Next(maximumDepth: 4);
            categories.Add(generated.Category);
            var result = oracle.Compare(generated.Term, generated.Variables);
            Assert.That(
                result.Status,
                Is.EqualTo(DifferentialStatus.Agreement),
                $"case {index}: {result.Detail}; term " +
                new IrPrinter(factory).Print(generated.Term));
        }
        Assert.That(
            categories,
            Is.EquivalentTo(Enum.GetValues<GeneratedIrCategory>()));
    }

    [Test]
    public void ShortCircuitSkipsDivisionByZero()
    {
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
    public void OpaqueTermsAbstain()
    {
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

    [Test]
    public void RenderableSequenceAccessChecksValueAndExceptionEdges()
    {
        var factory = new IrFactory();
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var values = factory.CreateVariable("values", sequenceType);
        var index = factory.CreateVariable("index", factory.IntegerType);
        var term = factory.SequenceAccess(
            factory.Variable(values),
            factory.Variable(index));
        var oracle = new IrCSharpDifferentialOracle(factory);

        var value = oracle.Compare(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [values] = factory.CreateSequenceValue(
                    sequenceType,
                    [factory.CreateIntegerValue(17)]),
                [index] = factory.CreateIntegerValue(0)
            });
        var outOfRange = oracle.Compare(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [values] = factory.CreateSequenceValue(sequenceType, []),
                [index] = factory.CreateIntegerValue(0)
            });
        var nullReceiver = oracle.Compare(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [values] = factory.CreateNullValue(sequenceType),
                [index] = factory.CreateIntegerValue(0)
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value.Status, Is.EqualTo(DifferentialStatus.Agreement));
            Assert.That(value.Interpreted.Value!.Integer, Is.EqualTo(17));
            Assert.That(
                outOfRange.Status,
                Is.EqualTo(DifferentialStatus.Agreement));
            Assert.That(
                outOfRange.Interpreted.Exception!.Kind,
                Is.EqualTo(IrExceptionKind.IndexOutOfRange));
            Assert.That(
                nullReceiver.Status,
                Is.EqualTo(DifferentialStatus.Agreement));
            Assert.That(
                nullReceiver.Interpreted.Exception!.Kind,
                Is.EqualTo(IrExceptionKind.NullReference));
        }
    }
}
