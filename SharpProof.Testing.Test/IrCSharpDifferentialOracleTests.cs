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
    public void GeneratedOracleAssembliesAreCollectible()
    {
        var factory = new IrFactory();
        var oracle = new IrCSharpDifferentialOracle(factory);
        var before = CountGeneratedOracleAssemblies();

        for (var index = 0; index < 20; index++)
        {
            var term = factory.Integer(index);
            var result = oracle.Compare(
                term,
                new Dictionary<IrVarId, IrValue>());
            Assert.That(result.Status, Is.EqualTo(DifferentialStatus.Agreement));
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.That(CountGeneratedOracleAssemblies(), Is.EqualTo(before));
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
    public void NullVariableBindingAbstainsWithoutEscaping()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);

        var result = new IrCSharpDifferentialOracle(factory).Compare(
            factory.Variable(value),
            new Dictionary<IrVarId, IrValue>
            {
                [value] = null!
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(DifferentialStatus.Abstained));
            Assert.That(
                result.Interpreted.Status,
                Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(
                result.Interpreted.Unsupported!.Reason,
                Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
        }
    }

    [Test]
    public void WrongTypeVariableBindingAbstainsWithoutEscaping()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);

        var result = new IrCSharpDifferentialOracle(factory).Compare(
            factory.Variable(value),
            new Dictionary<IrVarId, IrValue>
            {
                [value] = factory.CreateBooleanValue(true)
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(DifferentialStatus.Abstained));
            Assert.That(
                result.Interpreted.Status,
                Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(
                result.Interpreted.Unsupported!.Reason,
                Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
        }
    }

    [Test]
    public void RuntimeCompatibleWrongTypeBindingAbstainsInsteadOfMismatching()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.ObjectType);

        var result = new IrCSharpDifferentialOracle(factory).Compare(
            factory.Variable(value),
            new Dictionary<IrVarId, IrValue>
            {
                [value] = factory.CreateStringValue("text")
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(DifferentialStatus.Abstained));
            Assert.That(
                result.Interpreted.Status,
                Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(
                result.Interpreted.Unsupported!.Reason,
                Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
        }
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

    [Test]
    public void SequenceResultsCompareRecursively()
    {
        var factory = new IrFactory();
        var integers = factory.GetOrCreateSequenceType(factory.IntegerType);
        var nested = factory.GetOrCreateSequenceType(integers);
        var strings = factory.GetOrCreateSequenceType(factory.StringType);
        var integerValues = factory.CreateVariable("integers", integers);
        var nestedValues = factory.CreateVariable("nested", nested);
        var stringValues = factory.CreateVariable("strings", strings);
        var oracle = new IrCSharpDifferentialOracle(factory);

        var empty = oracle.Compare(
            factory.Variable(integerValues),
            new Dictionary<IrVarId, IrValue>
            {
                [integerValues] = factory.CreateSequenceValue(integers, [])
            });
        var scalarElements = oracle.Compare(
            factory.Variable(integerValues),
            new Dictionary<IrVarId, IrValue>
            {
                [integerValues] = factory.CreateSequenceValue(
                    integers,
                    [factory.CreateIntegerValue(17)])
            });
        var nestedElements = oracle.Compare(
            factory.Variable(nestedValues),
            new Dictionary<IrVarId, IrValue>
            {
                [nestedValues] = factory.CreateSequenceValue(
                    nested,
                    [
                        factory.CreateSequenceValue(
                            integers,
                            [factory.CreateIntegerValue(1)]),
                        factory.CreateSequenceValue(integers, [])
                    ])
            });
        var nullElement = oracle.Compare(
            factory.Variable(stringValues),
            new Dictionary<IrVarId, IrValue>
            {
                [stringValues] = factory.CreateSequenceValue(
                    strings,
                    [factory.CreateNullValue(factory.StringType)])
            });

        Assert.That(
            new[]
            {
                empty.Status,
                scalarElements.Status,
                nestedElements.Status,
                nullElement.Status
            },
            Is.All.EqualTo(DifferentialStatus.Agreement));
    }

    [Test]
    public void SharedSequenceResultsDoNotExpandExponentially()
    {
        const int sharedDepth = 18;
        var factory = new IrFactory();
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        IrValue interpreted = factory.CreateSequenceValue(
            sequenceType,
            [factory.CreateIntegerValue(1), factory.CreateIntegerValue(1)]);
        object actual = new long[] { 1, 1 };
        var runtimeType = typeof(long[]);

        for (var depth = 0; depth < sharedDepth; depth++)
        {
            sequenceType = factory.GetOrCreateSequenceType(sequenceType);
            interpreted = factory.CreateSequenceValue(
                sequenceType,
                [interpreted, interpreted]);
            var shared = Array.CreateInstance(runtimeType, 2);
            shared.SetValue(actual, 0);
            shared.SetValue(actual, 1);
            actual = shared;
            runtimeType = runtimeType.MakeArrayType();
        }

        var valuesAgree = typeof(IrCSharpDifferentialOracle).GetMethod(
            "ValuesAgree",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static)!;
        _ = valuesAgree.Invoke(
            null,
            [factory.CreateIntegerValue(1), 1L]);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        var agrees = (bool)valuesAgree.Invoke(null, [interpreted, actual])!;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(agrees, Is.True);
            Assert.That(allocated, Is.LessThan(1_000_000));
        }
    }

    private static int CountGeneratedOracleAssemblies()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Count(static assembly =>
                assembly.GetName().Name?.StartsWith(
                    "SharpProofOracle_",
                    StringComparison.Ordinal) == true);
    }
}
