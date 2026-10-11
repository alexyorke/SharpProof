using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;
using System.Collections.Immutable;
using System.Globalization;
using NUnit.Framework;
using SharpProof.Dataflow;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

// SP-BOOL-001: a decisive Boolean operand can refute a call precondition.
[TestFixture]
public sealed class AdvisoryBooleanDominanceRegressionTests
{
    [TestCase("flag && false", true)]
    [TestCase("false && flag", true)]
    [TestCase("flag && true", false)]
    [TestCase("flag || false", false)]
    [TestCase("flag || true", false)]
    [TestCase("true || flag", false)]
    public async Task DecisiveBooleanOperandControlsPreconditionDiagnostic(string clause, bool violation)
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "using SharpProof.Attributes; public static class Subject { " +
            "private static bool Need(bool flag) { Contract.Requires(" + clause + "); return flag; } " +
            "public static bool Caller(bool flag) { return Need(flag); } }",
            "contracts", ["SP0027"]);
        var expected = "Call to 'Need' violates precondition '" + clause + "'";
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            violation ? Is.EqualTo([expected]) : Is.Empty);
    }

    [Test]
    public void UnavailableRightOperandIsObservedBeforeDecisiveFalse()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var resultVariable = factory.CreateVariable("result", factory.BooleanType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "Unavailable", factory.BooleanType, true, []);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var right = factory.Binary(IrBinaryOperator.AndAlso, factory.PureOpaque(member, null), factory.Boolean(false));
        builder.Assign(entry, factory.CreateOperation(), resultVariable,
            factory.Binary(IrBinaryOperator.AndAlso, factory.Variable(flag), right));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(resultVariable));

        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [flag, resultVariable])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Gaps, Is.Not.Empty);
        Assert.That(result.Outputs[0].Values[resultVariable], Is.EqualTo(IntervalValue.Constant(0)));
    }

    [Test]
    public async Task PotentiallyThrowingRightOperandDoesNotInventViolation()
    {
        const string source = "using SharpProof.Attributes; public static class Subject { " +
            "private static bool Need(bool flag, int divisor) { " +
            "Contract.Requires(flag && 1 / divisor == 0); return flag; } " +
            "public static bool Caller(bool flag, int divisor) { return Need(flag, divisor); } }";
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(source, "contracts", ["SP0027"]);
        Assert.That(diagnostics, Is.Empty);
    }
}
