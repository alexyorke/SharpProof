using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Testing.Test;

[TestFixture]
public sealed class IrUnboxingDifferentialRegressionTests
{
    [Test]
    public void InterpreterUsesCSharpObjectUnboxingSemantics()
    {
        var fixture = CreateFixture();
        var interpreter = new IrInterpreter(fixture.Factory);
        var results = fixture.Cases.ToDictionary(
            @case => @case.Name,
            @case => Evaluate(
                interpreter,
                @case.Term,
                fixture.Value,
                @case.Value),
            StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                results["null integer"].Exception?.Kind,
                Is.EqualTo(IrExceptionKind.NullReference));
            Assert.That(
                results["null boolean"].Exception?.Kind,
                Is.EqualTo(IrExceptionKind.NullReference));
            Assert.That(
                results["wrong integer box"].Exception?.Kind,
                Is.EqualTo(IrExceptionKind.InvalidCast));
            Assert.That(
                results["wrong boolean box"].Exception?.Kind,
                Is.EqualTo(IrExceptionKind.InvalidCast));
            Assert.That(results["integer"].Value!.Integer, Is.EqualTo(17));
            Assert.That(results["boolean"].Value!.Boolean, Is.True);
        }
    }

    [Test]
    public void DifferentialOracleAgreesWithCompiledCSharpObjectUnboxing()
    {
        var fixture = CreateFixture();
        var oracle = new IrCSharpDifferentialOracle(fixture.Factory);

        foreach (var @case in fixture.Cases)
        {
            var result = oracle.Compare(
                @case.Term,
                new Dictionary<IrVarId, IrValue>
                {
                    [fixture.Value] = @case.Value
                });

            Assert.That(
                result.Status,
                Is.EqualTo(DifferentialStatus.Agreement),
                @case.Name + ": " + result.Detail);
        }
    }

    private static UnboxingFixture CreateFixture()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.ObjectType);
        var variable = factory.Variable(value);
        var unboxInteger = factory.Cast(factory.IntegerType, variable);
        var unboxBoolean = factory.Cast(factory.BooleanType, variable);
        return new(
            factory,
            value,
            [
                new(
                    "null integer",
                    unboxInteger,
                    factory.CreateNullValue(factory.ObjectType)),
                new(
                    "null boolean",
                    unboxBoolean,
                    factory.CreateNullValue(factory.ObjectType)),
                new(
                    "wrong integer box",
                    unboxInteger,
                    factory.CreateReferenceValue(factory.ObjectType, true)),
                new(
                    "wrong boolean box",
                    unboxBoolean,
                    factory.CreateReferenceValue(factory.ObjectType, 17L)),
                new(
                    "integer",
                    unboxInteger,
                    factory.CreateReferenceValue(factory.ObjectType, 17L)),
                new(
                    "boolean",
                    unboxBoolean,
                    factory.CreateReferenceValue(factory.ObjectType, true))
            ]);
    }

    private static IrEvaluationResult Evaluate(
        IrInterpreter interpreter,
        IrTerm term,
        IrVarId variable,
        IrValue value)
    {
        return interpreter.Evaluate(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [variable] = value
            });
    }

    private sealed record UnboxingFixture(
        IrFactory Factory,
        IrVarId Value,
        IReadOnlyList<UnboxingCase> Cases);

    private sealed record UnboxingCase(
        string Name,
        IrTerm Term,
        IrValue Value);
}
