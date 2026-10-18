using System;
using System.Collections.Immutable;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Dataflow;
using SharpProof.Ir;
using CoreTransfer = SharpProof.Advisory.Core.CoreIrScalarIntervalTransfer;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AdvisoryScalarIntervalBoundaryTests
{
    [Test]
    public void CoreBinaryPropagatesBottom()
    {
        BindCore(nameof(CoreBinaryPropagatesBottom));
        Assert.That(CoreTransfer.Binary(IrBinaryOperator.Add, IntegerType(8, signed: true),
            IntervalValue.Bottom, IntervalValue.Constant(0)), Is.EqualTo(IntervalValue.Bottom));
    }

    [Test]
    public void CoreDivisionByZeroMatchesTotalSemantics()
    {
        BindCore(nameof(CoreDivisionByZeroMatchesTotalSemantics));
        AssertCoreSingleton(IrBinaryOperator.Divide, signed: true, -3, 0, 1);
        AssertCoreSingleton(IrBinaryOperator.Divide, signed: true, 3, 0, -1);
        AssertCoreSingleton(IrBinaryOperator.Divide, signed: false, 3, 0, 255);
    }

    [Test]
    public void CoreRemainderByZeroPreservesDividend()
    {
        BindCore(nameof(CoreRemainderByZeroPreservesDividend));
        AssertCoreSingleton(IrBinaryOperator.Remainder, signed: true, -3, 0, -3);
    }

    [Test]
    public void CoreRemainderMatchesTotalBoundarySemantics()
    {
        BindCore(nameof(CoreRemainderMatchesTotalBoundarySemantics));
        AssertCoreSingleton(IrBinaryOperator.Remainder, signed: true, -7, 3, -1);
        AssertCoreSingleton(IrBinaryOperator.Remainder, signed: true, -128, -1, 0);
    }

    [Test]
    public void CoreSubtractionKeepsTightBoundsAndWidensUnderflow()
    {
        BindCore(nameof(CoreSubtractionKeepsTightBoundsAndWidensUnderflow));
        var type = IntegerType(8, signed: true);
        Assert.That(CoreTransfer.Binary(IrBinaryOperator.Subtract, type,
            IntervalValue.Range(-4, 2), IntervalValue.Range(3, 5)),
            Is.EqualTo(IntervalValue.Range(-9, -1)));
        Assert.That(CoreTransfer.Binary(IrBinaryOperator.Subtract, type,
            IntervalValue.Range(-128, -127), IntervalValue.Range(1, 2)),
            Is.EqualTo(IntervalValue.Range(-128, 127)));
        AssertTotalBinary(IrBinaryOperator.Subtract, signed: true, -4, 5, -9);
        AssertTotalBinary(IrBinaryOperator.Subtract, signed: true, 2, 3, -1);
        AssertTotalBinary(IrBinaryOperator.Subtract, signed: true, -128, 1, 127);
        AssertTotalBinary(IrBinaryOperator.Subtract, signed: true, -128, 2, 126);
        AssertTotalBinary(IrBinaryOperator.Subtract, signed: true, -127, 1, -128);
    }

    [Test]
    public void CoreNegationKeepsTightBoundsAndWidensWrapping()
    {
        BindCore(nameof(CoreNegationKeepsTightBoundsAndWidensWrapping));
        var type = IntegerType(8, signed: true);
        Assert.That(CoreTransfer.Negate(type, IntervalValue.Range(-5, 7)),
            Is.EqualTo(IntervalValue.Range(-7, 5)));
        Assert.That(CoreTransfer.Negate(type, IntervalValue.Range(-128, -127)),
            Is.EqualTo(IntervalValue.Range(-128, 127)));
        AssertTotalNegate(-5, 5);
        AssertTotalNegate(7, -7);
        AssertTotalNegate(-128, -128);
        AssertTotalNegate(-127, 127);
    }

    [Test]
    public void CoreUnsupportedIntegerOperatorIsRejected()
    {
        BindCore(nameof(CoreUnsupportedIntegerOperatorIsRejected));
        AssertTotalBinary(IrBinaryOperator.BitwiseAnd, signed: true, 1, 1, 1);
        var type = IntegerType(8, signed: true);
        var exception = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
        {
            _ = CoreTransfer.Binary(IrBinaryOperator.BitwiseAnd, type,
                IntervalValue.Constant(1), IntervalValue.Constant(1));
        }));
        Assert.That(exception!.ParamName, Is.EqualTo("operation"));
    }

    [Test]
    public void CoreNegationPropagatesBottom()
    {
        BindCore(nameof(CoreNegationPropagatesBottom));
        var type = IntegerType(8, signed: true);
        Assert.That(CoreTransfer.Negate(type, IntervalValue.Bottom), Is.EqualTo(IntervalValue.Bottom));
        Assert.That(CoreTransfer.Negate(type, IntervalValue.Constant(1)),
            Is.EqualTo(IntervalValue.Constant(-1)));
        AssertTotalNegate(1, -1);
    }

    [Test]
    public void CoreCastPropagatesBottomBeforeWrapping()
    {
        BindCore(nameof(CoreCastPropagatesBottomBeforeWrapping));
        var source = IntegerType(16, signed: true);
        var target = IntegerType(8, signed: true);
        Assert.That(CoreTransfer.Cast(source, target, IntervalValue.Bottom),
            Is.EqualTo(IntervalValue.Bottom));
        Assert.That(CoreTransfer.Cast(source, target, IntervalValue.Constant(256)),
            Is.EqualTo(IntervalValue.Constant(0)));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sourceId = factory.GetOrCreateIntegerType(16, signed: true);
        var targetId = factory.GetOrCreateIntegerType(8, signed: true);
        var input = factory.CreateVariable("input", sourceId);
        var result = new IrInterpreter(factory).Evaluate(factory.Cast(targetId, factory.Variable(input)),
            ImmutableDictionary<IrVarId, IrValue>.Empty.Add(input, factory.CreateIntegerValue(sourceId, 256)));
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(0));
    }

    private static IrTypeInfo IntegerType(int width, bool signed)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        return factory.GetTypeInfo(factory.GetOrCreateIntegerType(width, signed));
    }

    private static void AssertCoreSingleton(
        IrBinaryOperator operation, bool signed, long left, long right, long expected)
    {
        AssertTotalBinary(operation, signed, left, right, expected);
        Assert.That(CoreTransfer.Binary(operation, IntegerType(8, signed),
            IntervalValue.Constant(left), IntervalValue.Constant(right)),
            Is.EqualTo(IntervalValue.Constant(expected)));
    }

    private static void AssertTotalBinary(
        IrBinaryOperator operation, bool signed, long left, long right, long expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(8, signed);
        var first = factory.CreateVariable("first", type);
        var second = factory.CreateVariable("second", type);
        var term = factory.Binary(operation, factory.Variable(first), factory.Variable(second));
        var values = ImmutableDictionary<IrVarId, IrValue>.Empty
            .Add(first, factory.CreateIntegerValue(type, left))
            .Add(second, factory.CreateIntegerValue(type, right));
        var result = new IrInterpreter(factory).Evaluate(term, values);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(expected));
    }

    private static void AssertTotalNegate(long input, long expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(8, signed: true);
        var variable = factory.CreateVariable("input", type);
        var term = factory.Unary(IrUnaryOperator.Negate, factory.Variable(variable));
        var result = new IrInterpreter(factory).Evaluate(term,
            ImmutableDictionary<IrVarId, IrValue>.Empty.Add(variable, factory.CreateIntegerValue(type, input)));
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(expected));
    }

    private static void BindCore(string scenario)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        RecordBinding(scenario, typeof(CoreTransfer), "SharpProof.Analyzer.Core");
    }

    private static void RecordBinding(string scenario, Type implementationType, string expectedAssembly)
    {
        System.ArgumentNullException.ThrowIfNull(expectedAssembly);
        System.ArgumentNullException.ThrowIfNull(implementationType);
        System.ArgumentNullException.ThrowIfNull(scenario);
        var assembly = implementationType.Assembly;
        Assert.That(assembly.GetName().Name, Is.EqualTo(expectedAssembly));
        Assert.That(assembly, Is.Not.SameAs(typeof(AdvisoryScalarIntervalBoundaryTests).Assembly));
        TestContext.WriteLine("SP_LINKED_TRANSFER_V1 " + JsonSerializer.Serialize(new
        {
            Scenario = scenario,
            ImplementationType = implementationType.FullName,
            AssemblyName = assembly.GetName().Name,
            ModuleMvid = assembly.ManifestModule.ModuleVersionId
        }));
    }
}
