using System.Collections.Immutable;
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
        var results = fixture.Cases.ToImmutableDictionary(
            @case => @case is null ? throw new System.ArgumentNullException(nameof(@case)) : (@case.Name),
            @case => @case is null ? throw new System.ArgumentNullException(nameof(@case)) : (Evaluate(
                interpreter,
                @case.Term,
                fixture.Value,
                @case.Value)),
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
                global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(fixture.Value, @case.Value));

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
        if (variable.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(variable));
        }
        System.ArgumentNullException.ThrowIfNull(interpreter);
        System.ArgumentNullException.ThrowIfNull(term);
        System.ArgumentNullException.ThrowIfNull(value);
        return interpreter.Evaluate(
            term,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(variable, value));
    }

    private sealed record UnboxingFixture(
        IrFactory Factory,
        IrVarId Value,
        ImmutableArray<UnboxingCase> Cases)
    {
        public IrVarId Value { get; init; } = Value.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(Value)) : Value;
        public IrFactory Factory { get; init; } = Factory ?? throw new System.ArgumentNullException(nameof(Factory));
        public ImmutableArray<UnboxingCase> Cases { get; init; } = Cases.IsDefault ? throw new ArgumentException("Cases must be initialized.", nameof(Cases)) : Cases;
    }

    private sealed record UnboxingCase(
        string Name,
        IrTerm Term,
        IrValue Value)
    {
        public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
        public IrTerm Term { get; init; } = Term ?? throw new System.ArgumentNullException(nameof(Term));
        public IrValue Value { get; init; } = Value ?? throw new System.ArgumentNullException(nameof(Value));
    }
}
