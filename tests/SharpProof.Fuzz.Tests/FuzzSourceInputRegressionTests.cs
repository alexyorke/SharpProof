using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzSourceInputRegressionTests
{
    private const string Finite = "public static bool Target(long value) => value >= 0;";
    private const string Partial = "public static bool Target(long value) => value >= 0; public static bool NormalCompletion(long value) => true;";
    private const string Opaque = "public static bool Target(long value) => Helper(value); private static bool Helper(long value) => value >= 0;";

    [Test]
    public async Task UnsignedPartialInputsRetainAllSixtyFourBits()
    {
        const string partial = "public static bool Target(ulong value) => value == ulong.MaxValue; public static bool NormalCompletion(ulong value) => true;";
        var frontend = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        var result = await FuzzRunner.ReplaySourceCasesAsync(
            [new(frontend, Finite, [0L], partial, [[ulong.MaxValue], [0UL]])],
            TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Failures, Is.Empty);
        Assert.That(result.Agreements, Is.EqualTo(1));
        Assert.That(result.PartialResults[0].DefinedTrueCount, Is.EqualTo(1));
        Assert.That(result.PartialResults[0].DefinedFalseCount, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RetainedSourceFailureCanBeReplayedFromItsTypedReceipt(bool partialFailure)
    {
        var frontend = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        var source = new FuzzSourceCampaignCase(frontend, partialFailure ? Finite : Opaque, [7L],
            partialFailure ? Opaque + " public static bool NormalCompletion(long value) => true;" : Partial, [[7L]]);
        var result = await FuzzRunner.ReplaySourceCasesAsync([source], TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Failures, Has.Length.EqualTo(1));
        using var receipt = JsonDocument.Parse(result.Failures[0].Original);
        var root = receipt.RootElement;
        Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("FiniteArguments")[0].GetProperty("Type").GetString(), Is.EqualTo("System.Int64"));
        Assert.That(root.GetProperty("PartialScenarios")[0][0].GetProperty("Type").GetString(), Is.EqualTo("System.Int64"));
        if (partialFailure)
        {
            Assert.That(root.GetProperty("NormalCompletion").GetString(), Is.Not.Empty);
            Assert.That(root.GetProperty("ExpectedOutcomes")[0].GetString(), Is.EqualTo("DefinedTrue"));
        }
        else
        {
            Assert.That(root.GetProperty("ObservedValue").GetBoolean(), Is.True);
        }
        var replaySource = new FuzzSourceCampaignCase(frontend,
            root.GetProperty("FiniteMembers").GetString()!,
            [long.Parse(root.GetProperty("FiniteArguments")[0].GetProperty("Value").GetString()!, CultureInfo.InvariantCulture)],
            root.GetProperty("PartialMembers").GetString()!,
            [[long.Parse(root.GetProperty("PartialScenarios")[0][0].GetProperty("Value").GetString()!, CultureInfo.InvariantCulture)]]);
        var replay = await FuzzRunner.ReplaySourceCasesAsync([replaySource], TestContext.CurrentContext.CancellationToken);
        Assert.That(replay.Failures[0].Oracle, Is.EqualTo(result.Failures[0].Oracle));
        Assert.That(replay.Failures[0].Detail, Is.EqualTo(result.Failures[0].Detail));
        Assert.That(replay.Failures[0].Original, Is.EqualTo(result.Failures[0].Original));
    }

    [TestCase("a")]
    [TestCase("b")]
    public async Task TotalSourceStringEqualityUsesActualContents(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        const string member = "public static bool Target0(string value) => value == \"a\";";
        var replay = new TotalProgramSourceReplay(7231);
        await replay.CompareBatchAsync([new(member, [value], false)], 0, TestContext.CurrentContext.CancellationToken);
        Assert.That(replay.Result.Failures, Is.Empty);
        Assert.That(replay.Result.Agreements, Is.EqualTo(1));
        Assert.That(replay.Result.NativeProofs, Is.EqualTo(1));
        Assert.That(replay.Result.NativeRefutations, Is.EqualTo(1));
    }

    [Test]
    public void MalformedStringAbstractionRequiresAnExplicitLengthOnlyCase()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var aliases = TotalProgramDifferentialOracle.CreateAliases();
        const string malformed = "a\ud800b";
        Assert.Throws<ArgumentException>((Action)(() => TotalProgramDifferentialOracle.Value(factory,
            factory.StringType, malformed, aliases)));
        var abstracted = TotalProgramDifferentialOracle.Value(factory, factory.StringType,
            malformed, aliases, abstractStringContents: true);
        Assert.That(abstracted.String.Length, Is.EqualTo(malformed.Length));
    }
}
