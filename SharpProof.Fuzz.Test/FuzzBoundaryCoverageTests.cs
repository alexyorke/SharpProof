using System.Globalization;
using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzBoundaryCoverageTests
{
    [Test]
    public void OptionsUseDefaultsAndAcceptExplicitBoundaries()
    {
        Assert.That(FuzzOptions.Parse([]), Is.EqualTo(new FuzzOptions(
            FuzzOptions.DefaultCases,
            FuzzOptions.DefaultSeed,
            FuzzOptions.DefaultMaximumParallelism)));
        Assert.That(FuzzOptions.Parse([
            "--cases", FuzzOptions.MaximumCases.ToString(CultureInfo.InvariantCulture),
            "--seed", int.MinValue.ToString(CultureInfo.InvariantCulture),
            "--max-parallelism", "1"]), Is.EqualTo(new FuzzOptions(
            FuzzOptions.MaximumCases, int.MinValue, 1)));
    }

    [TestCase("--cases")]
    [TestCase("--seed")]
    [TestCase("--max-parallelism")]
    public void OptionsRejectMissingValues(string option)
    {
        Action parse = () => FuzzOptions.Parse([option]);
        var error = Assert.Throws<FuzzUsageException>(parse);
        Assert.That(error!.Message, Does.Contain("Missing value"));
    }

    [TestCase("--cases", "abc")]
    [TestCase("--cases", "-1")]
    [TestCase("--seed", "2147483648")]
    [TestCase("--max-parallelism", "0")]
    [TestCase("--max-parallelism", "abc")]
    public void OptionsRejectInvalidNumbers(string option, string value)
    {
        Action parse = () => FuzzOptions.Parse([option, value]);
        Assert.Throws<FuzzUsageException>(parse);
    }

    [Test]
    public void OptionsRejectNullAndHelp()
    {
        Action parseNull = () => FuzzOptions.Parse(null!);
        Action parseHelp = () => FuzzOptions.Parse(["--help"]);
        Action parseShortHelp = () => FuzzOptions.Parse(["-h"]);
        Assert.Throws<ArgumentNullException>(parseNull);
        Assert.That(Assert.Throws<FuzzUsageException>(parseHelp)!.Message,
            Is.Empty);
        Assert.That(Assert.Throws<FuzzUsageException>(parseShortHelp)!.Message,
            Is.Empty);
    }

    [Test]
    public async Task FiniteDomainRejectsUnsupportedVariableAndInvalidFormula()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var text = factory.CreateVariable("text", factory.StringType);
        var formula = factory.Binary(IrBinaryOperator.Equal,
            factory.Variable(text), factory.String("a"));

        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken),
            Is.False);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory, formula,
            TestContext.CurrentContext.CancellationToken, out var prepared), Is.False);
        Assert.That(prepared, Is.Null);
        var result = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Abstained));
        Assert.That(result.Detail, Does.Contain("outside"));

        Action nonBoolean = () =>
            FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, factory.Integer(1), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Action nullFactory = () =>
            FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(null!, formula, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Action nullFormula = () =>
            FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, null!, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.Throws<ArgumentException>(nonBoolean);
        Assert.Throws<ArgumentNullException>(nullFactory);
        Assert.Throws<ArgumentNullException>(nullFormula);
    }

    [Test]
    public void FiniteDomainRecognizesDefinedAssignments()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.CreateVariable("x", factory.IntegerType);
        var defined = factory.Binary(IrBinaryOperator.Equal,
            factory.Variable(x), factory.Integer(1));

        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory,
            defined, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory,
            defined, TestContext.CurrentContext.CancellationToken, out var prepared), Is.True);
        Assert.That(prepared, Is.Not.Null);
    }

    [Test]
    public async Task FiniteDomainAbstainsWhenRealFormulaExceedsAssignmentLimit()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        IrTerm formula = factory.Boolean(true);
        for (var index = 0; index < 17; index++)
        {
            var variable = factory.CreateVariable("b" + index.ToString(
                CultureInfo.InvariantCulture), factory.BooleanType);
            formula = factory.Binary(IrBinaryOperator.AndAlso, formula,
                factory.Variable(variable));
        }

        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(
            factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.False);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory,
            formula, TestContext.CurrentContext.CancellationToken, out var prepared), Is.False);
        Assert.That(prepared, Is.Null);
        var result = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Abstained));
        Assert.That(result.Actual, Is.Null);
        Assert.That(result.FiniteDomainAssumptions, Is.Zero);
        Assert.That(result.Detail, Does.Contain("assignment limit"));
    }
}
