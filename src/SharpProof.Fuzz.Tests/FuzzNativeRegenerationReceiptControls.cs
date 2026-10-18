using System.Globalization;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzNativeRegenerationReceiptControls
{
    [Test]
    public async Task ExistingDeterministicCampaignAndSourceRowsRecordActualRegenerationEvidence()
    {
        var summary = await FuzzRunner.RunAsync(new FuzzOptions(24, 12345, 4), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(summary.Passed, Is.True, string.Join(Environment.NewLine, summary.Failures));
        const string healthyFinite = "public static bool Target(long value) => value >= 0;";
        const string opaqueFinite = "public static bool Target(long value) => IsPositive(value + 1); " +
            "private static bool IsPositive(long value) => value > 0;";
        const string healthyPartial = "public static bool Target(long value) => value >= 0; " +
            "public static bool NormalCompletion(long value) => true;";
        const string opaquePartial = "public static bool Target(long value) => IsPositive(value + 1); " +
            "private static bool IsPositive(long value) => value > 0; " +
            "public static bool NormalCompletion(long value) => true;";
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        var surrogate = new string('\uD800', 1);
        var negative = new GeneratedCSharpCase(GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Equal,
            GeneratedCSharpExpression.String(surrogate), GeneratedCSharpExpression.Text()), 0, 0, true)
        {
            Text = surrogate
        };
        FuzzSourceCampaignCase[] sources =
        [
            new(positive, opaqueFinite, [0L], healthyPartial, [[0L]]),
            new(negative, healthyFinite, [0L], healthyPartial, [[0L]]),
            new(positive, healthyFinite, [0L], opaquePartial, [[0L]]),
            new(positive, healthyFinite, [0L], healthyPartial, [[0L]])
        ];
        var source = await FuzzRunner.ReplaySourceCasesAsync(sources, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(source.Cases, Is.EqualTo(4));
        Assert.That(source.Failures, Has.Length.EqualTo(3));
        var references = Enumerable.Range(0, 3).Select(index =>
        {
            var seed = unchecked(12345 + index * 397);
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var generated = new WellSortedIrGenerator(factory, unchecked(seed ^ 0x6C8E9CF5))
                .NextArithmeticOrBoolean(maximumDepth: 3);
            var ordinal = seed % FiniteDomainSmtDifferentialOracle.IntegerDomain.Length;
            if (ordinal < 0)
            {
                ordinal += FiniteDomainSmtDifferentialOracle.IntegerDomain.Length;
            }
            var formula = generated.Term.Type == factory.BooleanType ? generated.Term
                : factory.Binary(IrBinaryOperator.Equal, generated.Term,
                    factory.Integer(FiniteDomainSmtDifferentialOracle.IntegerDomain[ordinal]));
            var partial = PartialTermSmtCaseGenerator.Create(factory, unchecked(seed ^ 0x243F6A88));
            var printer = new IrPrinter(factory);
            return new
            {
                Seed = seed,
                Formula = printer.Print(formula),
                PartialFormula = printer.Print(partial.Formula),
                Completion = printer.Print(partial.NormalCompletion),
                Variables = IrTermAnalysis.CollectVariables(formula)
                    .Concat(IrTermAnalysis.CollectVariables(partial.Formula))
                    .Concat(IrTermAnalysis.CollectVariables(partial.NormalCompletion)).Distinct()
                    .OrderBy(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable.Value))
                    .Select(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (new { Id = variable.Value, Term = printer.Print(factory.Variable(variable)) })).ToArray(),
                Scenarios = partial.Scenarios.Select(scenario => scenario is null ? throw new System.ArgumentNullException(nameof(scenario)) : (scenario.OrderBy(pair => pair.Key.Value)
                    .Select(pair => new
                    {
                        Id = pair.Key.Value,
                        Kind = pair.Value.Kind,
                        Value = pair.Value.Kind == IrValueKind.Boolean ? pair.Value.Boolean.ToString()
                            : pair.Value.Integer.ToString(CultureInfo.InvariantCulture)
                    }).ToArray())).ToArray(),
                partial.ExpectedOutcomes
            };
        }).ToArray();
        var receipt = new
        {
            Schema = 1,
            ActualOwnedRunnerSummary = summary,
            ActualOwnedSourceReplay = source,
            ActualFailureUtf8Base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(source.Failures))),
            ComponentReferencesNotPrivateRunnerObservation = references,
            Exclusions = "Temporary paths and timings only; no fields removed from these receipts."
        };
        await TestContext.Out.WriteLineAsync("FUZZ_REGEN_RECEIPT=" + JsonSerializer.Serialize(receipt));
    }
}
