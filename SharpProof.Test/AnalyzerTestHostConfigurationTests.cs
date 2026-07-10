using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
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
        public async Task InvalidEffectSummaryJson_ReportsSp0032()
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

            var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty], Is.EqualTo("malformed effect-summary JSON"));
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
        public async Task MalformedEffectSummaryAdditionalFile_ReportsSp0032()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                "public sealed class TestClass { }",
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new AnalyzerTestHost.InMemoryAdditionalText(
                        "runtime.SharpProof.EffectSummary.json",
                        "{ invalid json")));

            var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFilePathProperty], Is.EqualTo("runtime.SharpProof.EffectSummary.json"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty], Is.EqualTo("malformed effect-summary JSON"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("malformed effect-summary JSON"));
        }

        [Test]
        public async Task EmptyBaselineAdditionalFile_ReportsSp0032()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                "public sealed class TestClass { }",
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new AnalyzerTestHost.InMemoryAdditionalText(
                        "SharpProof.Baseline.json",
                        "  ")));

            var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty], Is.EqualTo("file is empty"));
        }

        [Test]
        public async Task UnsupportedEffectSummarySchema_ReportsSp0032()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                "public sealed class TestClass { }",
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new AnalyzerTestHost.InMemoryAdditionalText(
                        "SharpProof.EffectSummary.json",
                        "{\"SchemaVersion\":99,\"Assemblies\":[]}")));

            var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty], Does.Contain("unsupported effect-summary SchemaVersion '99'"));
        }

        [Test]
        public async Task PartiallyMalformedBaselineAdditionalFile_ReportsSp0032()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
                "public sealed class TestClass { }",
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new AnalyzerTestHost.InMemoryAdditionalText(
                        "SharpProof.Baseline.json",
                        "[{\"id\":\"SP0002\",\"symbol\":\"M:TestClass.Method\",\"path\":\"input.cs\"},{\"id\":\"SP0002\"}]")));

            var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty], Does.Contain("partially ignored"));
        }

        [Test]
        public void AnalyzerConfigurationOptionRegistry_CoversEveryConfigKey()
        {
            var configKeys = GetConfigKeys();
            var registeredKeys = GetRegisteredOptionKeys();

            Assert.That(registeredKeys, Is.EquivalentTo(configKeys));
        }

        [Test]
        public void AnalyzerConfigurationOptionRegistry_IsReflectedInContractsDocumentation()
        {
            var contractsDoc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "contracts.md"));
            var referenceDoc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "configuration-reference.md"));
            var missingKeys = GetRegisteredOptionKeys()
                .Where(key => !contractsDoc.Contains(key, StringComparison.Ordinal) &&
                              !referenceDoc.Contains("| `" + key + "` |", StringComparison.Ordinal))
                .ToArray();

            Assert.That(missingKeys, Is.Empty);
        }

        [Test]
        public void AnalyzerConfigurationReference_ContainsGeneratedMetadataAndSamples()
        {
            var referenceDoc = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "configuration-reference.md"));

            Assert.That(
                referenceDoc,
                Does.Contain("Generated from ConfigKeys.cs and AnalyzerConfigurationOptionRegistry.cs"));
            Assert.That(referenceDoc, Does.Contain("is_global = true"));
            Assert.That(referenceDoc, Does.Contain("[src/**/*.cs]"));
            Assert.That(referenceDoc, Does.Contain("Global-only"));
            Assert.That(referenceDoc, Does.Contain("Global and per-tree"));
            Assert.That(referenceDoc, Does.Contain("SP0025 for invalid values"));

            foreach (var key in GetRegisteredOptionKeys())
            {
                Assert.That(
                    referenceDoc,
                    Does.Contain("| `" + key + "` |"),
                    "The generated configuration reference is missing " + key + ".");
            }
        }

        [Test]
        public void AnalyzerConfiguration_DoesNotExposeInertDebugLogging()
        {
            const string removedKey = "sharpproof_enable_debug_logging";
            var repositoryRoot = FindRepositoryRoot();
            var analyzerDirectory = Path.Combine(repositoryRoot, "SharpProof.Analyzer");
            var configurationType = typeof(SharpProofAnalyzer).Assembly
                .GetType("SharpProof.Analyzer.Configuration.AnalyzerConfiguration", throwOnError: true)!;
            var logCallFiles = Directory
                .EnumerateFiles(analyzerDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                               !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                               File.ReadAllText(path).Contains("LogDebug(", StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
                .ToArray();
            var contractsDoc = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "contracts.md"));

            Assert.That(GetConfigKeys(), Does.Not.Contain(removedKey));
            Assert.That(GetRegisteredOptionKeys(), Does.Not.Contain(removedKey));
            Assert.That(configurationType.GetProperty("EnableDebugLogging"), Is.Null);
            Assert.That(contractsDoc, Does.Not.Contain(removedKey));
            Assert.That(logCallFiles, Is.Empty);
        }

        [Test]
        public void AnalyzerConfigurationOptions_AreNotConsumedAsAdHocStringLiterals()
        {
            var repositoryRoot = FindRepositoryRoot();
            var analyzerDirectory = Path.Combine(repositoryRoot, "SharpProof.Analyzer");
            var allowedFiles = new HashSet<string>(StringComparer.Ordinal)
            {
                "SharpProof.Analyzer/Configuration/AnalyzerConfigurationOptionRegistry.cs",
                "SharpProof.Analyzer/Configuration/ConfigKeys.cs",
                "SharpProof.Analyzer/SharpProofDiagnostics.cs",
            };

            var offenders = Directory
                .EnumerateFiles(analyzerDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                    Source = File.ReadAllText(path),
                })
                .Where(file => !allowedFiles.Contains(file.Path) &&
                               file.Source.Contains("sharpproof_", StringComparison.Ordinal))
                .Select(file => file.Path)
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void AnalyzerHostFileIo_IsolatedToDocumentedEffectSummaryBoundary()
        {
            var repositoryRoot = FindRepositoryRoot();
            var analyzerDirectory = Path.Combine(repositoryRoot, "SharpProof.Analyzer");
            var supportPath = Path.Combine(analyzerDirectory, "EffectSummaryMetadataSupport.cs");
            var supportSource = File.ReadAllText(supportPath);
            var projectSource = File.ReadAllText(Path.Combine(analyzerDirectory, "SharpProof.Analyzer.csproj"));
            var effectSummaryDoc = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "effect-summary.md"));

            var fileIoFiles = Directory
                .EnumerateFiles(analyzerDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                               !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\b(?:File|Directory)\s*\."))
                .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
                .ToArray();

            Assert.That(projectSource, Does.Not.Contain("RS1035"));
            Assert.That(fileIoFiles, Is.EqualTo(new[] { "SharpProof.Analyzer/EffectSummaryMetadataSupport.cs" }));
            Assert.That(supportSource, Does.Contain("#pragma warning disable RS1035"));
            Assert.That(supportSource, Does.Contain("file I/O isolated here"));
            Assert.That(effectSummaryDoc, Does.Contain("RS1035"));
            Assert.That(effectSummaryDoc, Does.Contain("Roslyn metadata references"));
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

        private static string[] GetConfigKeys()
        {
            var configKeysType = typeof(SharpProofAnalyzer).Assembly
                .GetType("SharpProof.Analyzer.Configuration.ConfigKeys", throwOnError: true)!;
            return configKeysType
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => field.IsLiteral &&
                                field.FieldType == typeof(string) &&
                                field.GetRawConstantValue() is string value &&
                                value.StartsWith("sharpproof_", StringComparison.Ordinal))
                .Select(field => (string)field.GetRawConstantValue()!)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetRegisteredOptionKeys()
        {
            var registryType = typeof(SharpProofAnalyzer).Assembly
                .GetType("SharpProof.Analyzer.Configuration.AnalyzerConfigurationOptionRegistry", throwOnError: true)!;
            var options = (System.Collections.IEnumerable)registryType
                .GetProperty("All", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            return options
                .Cast<object>()
                .Select(option => (string)option.GetType().GetProperty("Key")!.GetValue(option)!)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PLAN.md")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }

        private readonly record struct SmtOptionsSnapshot(
            int TimeoutMs,
            int MethodBudgetMs,
            int MaxPathConditions,
            int MaxExpressionNodes);
    }
}
