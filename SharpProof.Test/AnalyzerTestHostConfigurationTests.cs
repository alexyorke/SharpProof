using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class AnalyzerTestHostConfigurationTests
    {
        [Test]
        public async Task CachedGlobalOptions_PreserveNewlineDelimitedValues()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void CallsFirst()
    {
        First();
    }

    [EnforcePure]
    public void CallsSecond()
    {
        Second();
    }

    private void First()
    {
    }

    private void Second()
    {
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "sharpproof_known_impure_methods",
                    "TestClass.First()\nTestClass.Second()"));

            var symbols = diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
                .Select(diagnostic => diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty])
                .ToArray();

            Assert.That(symbols, Has.Some.Contains("TestClass.First"));
            Assert.That(symbols, Has.Some.Contains("TestClass.Second"));
        }

        [Test]
        public async Task InvalidEffectSummaryJson_IsIgnored()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Pure()
    {
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "sharpproof_enable_effect_summary_json",
                    "true"),
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new AnalyzerTestHost.InMemoryAdditionalText(
                        "SharpProof.EffectSummary.json",
                        "{ invalid json")));

            Assert.That(diagnostics, Is.Empty);
        }

        [Test]
        public async Task InvalidGlobalConfigurationValues_ReportDiagnostics()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
public sealed class TestClass
{
}",
                ImmutableDictionary<string, string>.Empty
                    .Add("sharpproof_smt_mode", "turbo")
                    .Add("sharpproof_smt_timeout_ms", "0")
                    .Add("sharpproof_suggest_missing_enforce_pure", "maybe"));

            var configurationDiagnostics = diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.InvalidAnalyzerConfigurationId)
                .OrderBy(diagnostic => diagnostic.Properties[SharpProofDiagnostics.ConfigurationKeyProperty], StringComparer.Ordinal)
                .ToArray();

            Assert.That(configurationDiagnostics, Has.Length.EqualTo(3));
            Assert.That(
                configurationDiagnostics.Select(diagnostic => diagnostic.Properties[SharpProofDiagnostics.ConfigurationKeyProperty]),
                Is.EqualTo(new[]
                {
                    "sharpproof_smt_mode",
                    "sharpproof_smt_timeout_ms",
                    "sharpproof_suggest_missing_enforce_pure",
                }));
            Assert.That(configurationDiagnostics[0].Properties[SharpProofDiagnostics.ConfigurationValueProperty], Is.EqualTo("turbo"));
            Assert.That(configurationDiagnostics[0].Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty], Does.Contain("expected one of"));
            Assert.That(configurationDiagnostics[1].Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty], Is.EqualTo("expected a positive integer"));
            Assert.That(configurationDiagnostics[2].Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty], Is.EqualTo("expected a boolean value"));
        }

        [Test]
        public void SmtNumericConfiguration_ParsesSignedOverridesWithInvariantCulture()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            customCulture.NumberFormat.PositiveSign = "p";

            try
            {
                CultureInfo.CurrentCulture = customCulture;

                var options = ReadSmtOptions(
                    ImmutableDictionary<string, string>.Empty
                        .Add("sharpproof_smt_timeout_ms", "+321")
                        .Add("sharpproof_smt_method_budget_ms", "+4321")
                        .Add("sharpproof_smt_max_path_conditions", "+123")
                        .Add("sharpproof_smt_max_expression_nodes", "+4567"));

                Assert.That(options.TimeoutMs, Is.EqualTo(321));
                Assert.That(options.MethodBudgetMs, Is.EqualTo(4321));
                Assert.That(options.MaxPathConditions, Is.EqualTo(123));
                Assert.That(options.MaxExpressionNodes, Is.EqualTo(4567));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        private static SmtOptionsSnapshot ReadSmtOptions(ImmutableDictionary<string, string> globalOptions)
        {
            var analyzerOptions = AnalyzerTestHost.CreateAnalyzerOptions(globalOptions);
            var configurationType = typeof(SharpProofAnalyzer).Assembly
                .GetType("SharpProof.Analyzer.Configuration.AnalyzerConfiguration", throwOnError: true)!;
            var fromOptions = configurationType.GetMethod(
                "FromOptions",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            var configuration = fromOptions.Invoke(null, new object?[] { analyzerOptions })!;
            var smtOptions = configurationType.GetProperty("SmtOptions")!.GetValue(configuration)!;
            var smtOptionsType = smtOptions.GetType();
            var queryTimeout = (TimeSpan)smtOptionsType.GetProperty("QueryTimeout")!.GetValue(smtOptions)!;
            var methodBudget = (TimeSpan)smtOptionsType.GetProperty("MethodBudget")!.GetValue(smtOptions)!;

            return new SmtOptionsSnapshot(
                (int)queryTimeout.TotalMilliseconds,
                (int)methodBudget.TotalMilliseconds,
                (int)smtOptionsType.GetProperty("MaxPathConditions")!.GetValue(smtOptions)!,
                (int)smtOptionsType.GetProperty("MaxExpressionNodes")!.GetValue(smtOptions)!);
        }

        private readonly record struct SmtOptionsSnapshot(
            int TimeoutMs,
            int MethodBudgetMs,
            int MaxPathConditions,
            int MaxExpressionNodes);
    }
}
