using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;
using SharpProof.Dataflow;
using SharpProof.Ir;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCorePublicGuardCoverageTests
{
    [Test]
    public void AdvisoryConstructorRejectsNullProgram()
    {
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = new CoreIrAdvisoryInterpreter(null!, [])));
        Assert.That(error!.ParamName, Is.EqualTo("program"));
    }

    [Test]
    public void AdvisoryConstructorRejectsDefaultVariables()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            _ = new CoreIrAdvisoryInterpreter(ConstantProgram(factory), default)));
        Assert.That(error!.ParamName, Is.EqualTo("variables"));
    }

    [Test]
    public void AdvisoryConstructorAcceptsInitializedEmptyVariables()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var program = ConstantProgram(factory);
        var result = new CoreIrAdvisoryInterpreter(program, []).Run(
            ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.Outputs, Is.Not.Empty);
        Assert.That(new IrProgramInterpreter(factory).Execute(program).ReturnValue!.Integer, Is.EqualTo(1));
    }

    [Test]
    public void AnalyzerOptionsRejectNullAndAcceptInitializedEmptyOptions()
    {
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
            AnalyzerConfiguration.FromOptions((AnalyzerOptions)null!)));
        Assert.That(error!.ParamName, Is.EqualTo("options"));
        var configuration = AnalyzerConfiguration.FromOptions(new AnalyzerOptions([]));
        Assert.That(configuration.InvalidConfigurationValues, Is.Empty);
    }

    private static IrProgram ConstantProgram(IrFactory factory)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation(), factory.Integer(1));
        return builder.Build();
    }
}
