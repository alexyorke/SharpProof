using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzUnsupportedScenarioTests
{
    [Test]
    public void PartialOracleRejectsAStringScenarioOutsideItsBooleanIntegerContract()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("text", factory.StringType);
        const string text = "proof";
        var formula = factory.Binary(IrBinaryOperator.Equal,
            factory.Variable(variable), factory.String(text));
        var scenario = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty
            .Add(variable, factory.CreateStringValue(text));
        var generated = new PartialTermSmtCase(formula, factory.Boolean(true),
            [scenario], [string.Equals(text, "proof", StringComparison.Ordinal)
                ? PartialTermSemanticOutcome.DefinedTrue : PartialTermSemanticOutcome.DefinedFalse]);
        Func<Task> compare = async () =>
            await PartialTermSmtDifferentialOracle.CompareAsync(factory, generated);

        var exception = Assert.ThrowsAsync<ArgumentException>(compare);
        Assert.That(exception!.ParamName, Is.EqualTo("value"));
        Assert.That(exception.Message, Does.Contain("only Boolean and integer values"));
    }
}
