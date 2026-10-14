using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzInputValidationTests
{
    [Test]
    public void UsageExceptionPreservesCauseAndSupportsDefaultConstruction()
    {
        var cause = new InvalidOperationException("input conversion failed");
        var exception = new FuzzUsageException("invalid input", cause);

        Assert.That(exception.Message, Is.EqualTo("invalid input"));
        Assert.That(exception.InnerException, Is.SameAs(cause));
        Assert.That(new FuzzUsageException(), Is.InstanceOf<Exception>());
    }

    [Test]
    public void RunnerRejectsNullOptions()
    {
        var exception = ThrowsAsync<ArgumentNullException>(
            async () => await FuzzRunner.RunAsync(null!));
        Assert.That(exception!.ParamName, Is.EqualTo("options"));
    }

    [TestCase(0, 1)]
    [TestCase(1_000_001, 1)]
    [TestCase(1, 0)]
    [TestCase(1, 5)]
    public void RunnerRejectsOutOfRangeOptions(int cases, int parallelism)
    {
        var exception = ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await FuzzRunner.RunAsync(new(cases, 12345, parallelism)));
        Assert.That(exception!.ParamName, Is.EqualTo("options"));
    }

    [Test]
    public void PartialTermGeneratorRequiresTotalFactory()
    {
        Assert.That(Throws<ArgumentNullException>(() =>
            PartialTermSmtCaseGenerator.Create(null!, 12345))!.ParamName,
            Is.EqualTo("factory"));
        Assert.That(Throws<ArgumentException>(() =>
            PartialTermSmtCaseGenerator.Create(new IrFactory(IrExecutionSemantics.Legacy),
                12345))!.ParamName, Is.EqualTo("factory"));
    }

    [Test]
    public void PartialTermOracleRejectsInvalidPublicInputs()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var generated = PartialTermSmtCaseGenerator.Create(factory, 12345);

        Assert.That(ThrowsAsync<ArgumentNullException>(async () =>
            await PartialTermSmtDifferentialOracle.CompareAsync(null!, generated))!
            .ParamName, Is.EqualTo("factory"));
        Assert.That(ThrowsAsync<ArgumentNullException>(async () =>
            await PartialTermSmtDifferentialOracle.CompareAsync(factory, null!))!
            .ParamName, Is.EqualTo("generated"));
        Assert.That(ThrowsAsync<ArgumentException>(async () =>
            await PartialTermSmtDifferentialOracle.CompareAsync(factory,
                generated with { Formula = factory.Integer(1) }))!.ParamName,
            Is.EqualTo("generated"));
        Assert.That(ThrowsAsync<ArgumentException>(async () =>
            await PartialTermSmtDifferentialOracle.CompareAsync(factory,
                generated with { Scenarios = [] }))!.ParamName,
            Is.EqualTo("generated"));
        Assert.That(ThrowsAsync<ArgumentException>(async () =>
            await PartialTermSmtDifferentialOracle.CompareAsync(factory,
                generated with { ExpectedOutcomes = [] }))!.ParamName,
            Is.EqualTo("generated"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void PartialTermOracleRejectsIncompleteOrMistypedPublicAssignments(int invalidKind)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var generated = PartialTermSmtCaseGenerator.Create(factory, 12345);
        var scenario = generated.Scenarios[0];
        var variable = scenario.Keys.OrderBy(item => item.Value).First();
        var wrongValue = factory.GetVariableInfo(variable).Type == factory.BooleanType
            ? factory.CreateIntegerValue(factory.IntegerType, 1)
            : factory.CreateBooleanValue(true);
        var invalid = invalidKind switch
        {
            0 => scenario.Remove(variable),
            1 => scenario.SetItem(variable, null!),
            _ => scenario.SetItem(variable, wrongValue)
        };
        var exception = ThrowsAsync<ArgumentException>(async () =>
            await PartialTermSmtDifferentialOracle.CompareAsync(factory,
                generated with { Scenarios = generated.Scenarios.SetItem(0, invalid) }));

        Assert.That(exception.ParamName, Is.EqualTo("scenario"));
        Assert.That(exception.Message, Does.Contain(invalidKind == 0
            ? "does not assign every variable" : "wrong type"));
    }

    [Test]
    public void StructuralShrinkerRejectsMissingInputs()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var term = factory.Boolean(true);
        Func<IrTerm, CancellationToken, Task<bool>> preserve =
            static (_, _) => Task.FromResult(false);

        Assert.That(ThrowsAsync<ArgumentNullException>(async () =>
            await IrStructuralShrinker.MinimizeAsync(null!, term, preserve))!.ParamName,
            Is.EqualTo("factory"));
        Assert.That(ThrowsAsync<ArgumentNullException>(async () =>
            await IrStructuralShrinker.MinimizeAsync(factory, null!, preserve))!.ParamName,
            Is.EqualTo("term"));
        Assert.That(ThrowsAsync<ArgumentNullException>(async () =>
            await IrStructuralShrinker.MinimizeAsync(factory, term, null!))!.ParamName,
            Is.EqualTo("preservesMismatch"));
        Assert.That(Throws<ArgumentNullException>(() =>
            IrStructuralShrinker.GetCandidates(null!, term))!.ParamName, Is.EqualTo("factory"));
        Assert.That(Throws<ArgumentNullException>(() =>
            IrStructuralShrinker.GetCandidates(factory, null!))!.ParamName, Is.EqualTo("term"));
        Assert.That(Throws<ArgumentNullException>(() =>
            IrStructuralShrinker.StructuralSize(null!))!.ParamName, Is.EqualTo("term"));
    }
    private static TException Throws<TException>(Action action)
        where TException : Exception
    {
        return Assert.Throws<TException>(action)!;
    }

    private static TException ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        return Assert.ThrowsAsync<TException>(action)!;
    }
}
