using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ExactCounterexampleReplayTests
{
    private const string Source = """
        using SharpProof.Attributes;
        public static class C
        {
            public static int State;
            public static int Other;
            public static int Target(int x, int y, int[]? a, string? s)
            {
                Contract.Requires(x > 0);
                Contract.Requires(y == -987654321);
                Contract.Requires(a == null);
                Contract.Requires(s == null);
                Contract.Ensures(Contract.Result<int>() == 0);
                return x == 123456789 ? 1 : 0;
            }
            public static bool Pre(int x, int y, int[]? a, string? s) =>
                x > 0 && y == -987654321 && a == null && s == null;
            public static bool Post(int r, int x0, int y0, int s0, int st) => r == 0;
        }
        """;

    [Test]
    public async Task ReportedWorkerInputsAreReplayedThroughCompiledCSharp()
    {
        var counts = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        var result = await AnalyzerPipelineLongFuzzTests.RunCase(Source, ContractClaimProgramGenerator.Ensures,
            key => { ArgumentNullException.ThrowIfNull(key); counts[key] = counts.GetValueOrDefault(key) + 1; });
        var observedCounts = counts.ToImmutable();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted.ToString()), result.Reason);
            Assert.That(observedCounts.GetValueOrDefault("exact-replay:Reproduced"), Is.EqualTo(1));
            Assert.That(result.Problems, Is.Empty);
        }
    }

    [TestCase("123456789", "Reproduced")]
    [TestCase("42", "Mismatch")]
    [TestCase("-1", "Mismatch")]
    public void ReplaysOnlyTheReportedInputs(string x, string expected)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(expected);
        var result = Replay(Source, Model(x));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status.ToString(), Is.EqualTo(expected), result.Detail);
            Assert.That(result.Detail, Does.Contain("parameter:0=" + x));
        }
    }

    [Test]
    public void ThrowingDoesNotFalsifyANormalReturnPostcondition()
    {
        var source = Source.Replace("return x == 123456789 ? 1 : 0;", "throw new System.Exception();",
            StringComparison.Ordinal);
        Assert.That(Replay(source, Model()).Status, Is.EqualTo(ExactReplayStatus.Mismatch));
    }

    [TestCase("\"\"")]
    [TestCase("\" \\u0000\"")]
    [TestCase("\"\\ud800\"")]
    [TestCase("\"\\udfff\"")]
    [TestCase("\"\\ud83d\\ude00\"")]
    [TestCase("\"\\\"\\\\\"")]
    public void ReconstructsStringsWithoutChangingUtf16CodeUnits(string literal)
    {
        ArgumentNullException.ThrowIfNull(literal);
        var model = Model().ToBuilder();
        model[3] = model[3] with { Kind = "String" };
        model[3] = model[3] with { Value = literal };
        var source = Source.Replace("Contract.Requires(s == null);", "", StringComparison.Ordinal)
            .Replace(" && s == null", "", StringComparison.Ordinal)
            .Replace("x == 123456789 ? 1 : 0", "s == " + literal + " ? 1 : 0", StringComparison.Ordinal);
        var result = Replay(source, model.ToImmutable());
        Assert.That(result.Status, Is.EqualTo(ExactReplayStatus.Reproduced), result.Detail);
    }

    [TestCase("\"\\u12\"")]
    [TestCase("\"\\uZZZZ\"")]
    [TestCase("\"\\x41\"")]
    [TestCase("\"\\n\"")]
    [TestCase("\"ab\"junk")]
    [TestCase("\"ab\"\"")]
    [TestCase("\"line\nbreak\"")]
    public void MalformedStringDisplaysCannotBecomeReplacementInputs(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        var model = Model().ToBuilder();
        model[3] = model[3] with { Kind = "String" };
        model[3] = model[3] with { Value = encoded };
        Assert.That(Replay(Source, model.ToImmutable()).Status, Is.EqualTo(ExactReplayStatus.InvalidModel));
    }

    [Test]
    public void EntryBindingsAreMatchedByOrdinalInsteadOfDisplayOrder()
    {
        var model = Model().ToBuilder();
        model.Reverse();
        var result = Replay(Source, model.ToImmutable());
        Assert.That(result.Status, Is.EqualTo(ExactReplayStatus.Reproduced), result.Detail);
    }

    [TestCase(0x0085)]
    [TestCase(0x2028)]
    [TestCase(0x2029)]
    public void LiteralUnicodeLineSeparatorsRemainExecutableInputs(int codeUnit)
    {
        if (codeUnit < 0 || codeUnit > char.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(codeUnit));
        }
        var model = Model().ToBuilder();
        model[3] = model[3] with { Kind = "String" };
        model[3] = model[3] with { Value = "\"" + (char)codeUnit + "\"" };
        var literal = "\"\\u" + codeUnit.ToString("x4", System.Globalization.CultureInfo.InvariantCulture) + "\"";
        var source = Source.Replace("Contract.Requires(s == null);", "", StringComparison.Ordinal)
            .Replace(" && s == null", "", StringComparison.Ordinal)
            .Replace("x == 123456789 ? 1 : 0", "s == " + literal + " ? 1 : 0", StringComparison.Ordinal);
        var result = Replay(source, model.ToImmutable());
        Assert.That(result.Status, Is.EqualTo(ExactReplayStatus.Reproduced), result.Detail);
    }

    [TestCase("Reference", "<opaque>")]
    [TestCase("Sequence", "<opaque>")]
    public void OpaqueArraysAreUnsupportedInsteadOfGuessed(string kind, string value)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(value);
        var model = Model().ToBuilder();
        model[2] = model[2] with { Kind = kind };
        model[2] = model[2] with { Value = value };
        Assert.That(Replay(Source, model.ToImmutable()).Status, Is.EqualTo(ExactReplayStatus.Unsupported));
    }

    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("foreign")]
    [TestCase("null-binding")]
    [TestCase("overflow")]
    [TestCase("wrong-kind")]
    [TestCase("bad-string")]
    public void InvalidModelsCannotPassReplay(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var model = Model().ToBuilder();
        switch (mutation)
        {
            case "missing":
                model.RemoveAt(3);
                break;
            case "duplicate":
                model[1] = model[1] with { Variable = model[0].Variable };
                break;
            case "foreign":
                model[1] = model[1] with { Variable = "this" };
                break;
            case "null-binding":
                model[1] = null!;
                break;
            case "overflow":
                model[0] = model[0] with { Value = "2147483648" };
                break;
            case "wrong-kind":
                model[0] = model[0] with { Kind = "Boolean" };
                break;
            case "bad-string":
                model[3] = model[3] with { Kind = "String" };
                model[3] = model[3] with { Value = "unquoted" };
                break;
        }
        Assert.That(Replay(Source, model.ToImmutable()).Status, Is.EqualTo(ExactReplayStatus.InvalidModel));
    }

    [Test]
    public void MissingModelsAndEffectRefutationsRemainUnsupported()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Replay(Source, []).Status, Is.EqualTo(ExactReplayStatus.Unsupported));
            Assert.That(ExactCounterexampleReplay.Run(Source, ContractClaimProgramGenerator.DoesNotThrow, Model()).Status,
                Is.EqualTo(ExactReplayStatus.Unsupported));
        }
    }

    [Test]
    public void CompilerFailuresRemainHarnessErrors()
    {
        Assert.That(Replay("not C#", Model()).Status, Is.EqualTo(ExactReplayStatus.HarnessError));
    }

    private static ExactReplayResult Replay(string source, ImmutableArray<WorkerModelValue> model)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (model.IsDefault)
        { throw new ArgumentException("Array must be initialized.", nameof(model)); }
        return ExactCounterexampleReplay.Run(source, ContractClaimProgramGenerator.Ensures, model);
    }

    private static ImmutableArray<WorkerModelValue> Model(string x = "123456789")
    {
        ArgumentNullException.ThrowIfNull(x);
        return
        [
            new() { Variable = "parameter:0", Kind = "Integer", Value = x },
            new() { Variable = "parameter:1", Kind = "Integer", Value = "-987654321" },
            new() { Variable = "parameter:2", Kind = "Null", Value = "null" },
            new() { Variable = "parameter:3", Kind = "Null", Value = "null" }
        ];
    }
}
