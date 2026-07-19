using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class AnalyzerTestHostConfigurationTests
{
    [Test]
    public void InheritedGlobalAndTreeInvalidValue_IsNotReportedPerTree()
    {
        var values = ImmutableDictionary<string, string>.Empty.Add(
            "sharpproof_emit_explanations",
            "bogus");
        var global = new DictionaryAnalyzerConfigOptions(values);
        var inheritedTree = new DictionaryAnalyzerConfigOptions(values);

        var invalid = AnalyzerConfiguration.GetInvalidTreeConfigurationValues(inheritedTree, global);

        Assert.That(invalid, Is.Empty);
    }

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
                string.Join(
                    "\n",
                    ConfiguredMemberKeyTestFactory.Method("TestClass", "First"),
                    ConfiguredMemberKeyTestFactory.Method("TestClass", "Second"))));

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
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Is.EqualTo("malformed effect-summary JSON"));
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
            .OrderBy(diagnostic => diagnostic.Properties[SharpProofDiagnostics.ConfigurationKeyProperty],
                StringComparer.Ordinal)
            .ToArray();

        Assert.That(configurationDiagnostics, Has.Length.EqualTo(3));
        Assert.That(
            configurationDiagnostics.Select(diagnostic =>
                diagnostic.Properties[SharpProofDiagnostics.ConfigurationKeyProperty]),
            Is.EqualTo(new[]
            {
                "sharpproof_smt_mode",
                "sharpproof_smt_timeout_ms",
                "sharpproof_suggest_missing_enforce_pure"
            }));
        Assert.That(configurationDiagnostics[0].Properties[SharpProofDiagnostics.ConfigurationValueProperty],
            Is.EqualTo("turbo"));
        Assert.That(configurationDiagnostics[0].Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty],
            Does.Contain("expected one of"));
        Assert.That(configurationDiagnostics[1].Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty],
            Is.EqualTo("expected a positive integer"));
        Assert.That(configurationDiagnostics[2].Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty],
            Is.EqualTo("expected a boolean value"));
    }

    [Test]
    public async Task SuggestionScopeAliases_AreAcceptedByRegistryValidation()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure_scope", "public-only")
                .Add("sharpproof_suggest_inferred_contracts_scope", "none"));

        Assert.That(
            diagnostics.Any(diagnostic =>
                diagnostic.Id == SharpProofDiagnostics.InvalidAnalyzerConfigurationId),
            Is.False);
    }

    [Test]
    public async Task ConfigurationModeAliases_AreRejected()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_smt_mode", "off")
                .Add("sharpproof_runtime_hazard_mode", "true"));

        var invalid = diagnostics
            .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.InvalidAnalyzerConfigurationId)
            .OrderBy(diagnostic => diagnostic.Properties[SharpProofDiagnostics.ConfigurationKeyProperty],
                StringComparer.Ordinal)
            .ToArray();

        Assert.That(invalid, Has.Length.EqualTo(2));
        Assert.That(
            invalid.Select(diagnostic => diagnostic.Properties[SharpProofDiagnostics.ConfigurationKeyProperty]),
            Is.EqualTo(new[] { "sharpproof_runtime_hazard_mode", "sharpproof_smt_mode" }));
        Assert.That(
            invalid[0].Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty],
            Does.Contain("none, sites, summaries, all, unknowns, sites-and-unknowns, all-and-unknowns"));
        Assert.That(
            invalid[1].Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty],
            Does.Contain("disabled, bounded, deep"));
    }

    [Test]
    public async Task LegacyAndAmbiguousConfiguredMemberKeys_AreRejected()
    {
        var getterWithoutSuffix = ConfiguredMemberKeyTestFactory.Getter(
            "TestClass",
            "Value",
            "named:System.Int32");
        getterWithoutSuffix = getterWithoutSuffix.Substring(
            0,
            getterWithoutSuffix.Length - ConfiguredMemberKey.GetterSuffix.Length);
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { public int Value => 1; }",
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_known_pure_methods", "TestClass.Value;" + getterWithoutSuffix));

        var diagnostic = diagnostics.Single(item =>
            item.Id == SharpProofDiagnostics.InvalidAnalyzerConfigurationId &&
            item.Properties[SharpProofDiagnostics.ConfigurationKeyProperty] ==
            "sharpproof_known_pure_methods");
        Assert.That(
            diagnostic.Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty],
            Does.Contain("canonical structural method keys"));
        Assert.That(
            diagnostic.Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty],
            Does.Contain(".get or .set"));
    }

    [Test]
    public void SmtModeDependentDefaults_AreTypedProviders()
    {
        var timeout = AnalyzerConfigurationOptionRegistry.Get(ConfigKeys.SmtTimeoutMs).Default;
        var expressionNodes = AnalyzerConfigurationOptionRegistry.Get(ConfigKeys.SmtMaxExpressionNodes).Default;

        Assert.That(timeout.IsModeDependent, Is.True);
        Assert.That(timeout.Resolve(SmtAnalysisMode.Off), Is.EqualTo("750"));
        Assert.That(timeout.Resolve(SmtAnalysisMode.Bounded), Is.EqualTo("750"));
        Assert.That(timeout.Resolve(SmtAnalysisMode.Deep), Is.EqualTo("2000"));
        Assert.That(expressionNodes.Resolve(SmtAnalysisMode.Bounded), Is.EqualTo("2048"));
        Assert.That(expressionNodes.Resolve(SmtAnalysisMode.Deep), Is.EqualTo("8192"));
        Assert.That(timeout.DocumentationValue, Does.Not.Contain("mode default"));
    }

    [Test]
    public async Task InvalidSuppressionDiagnosticId_ReportsSp0025()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            ImmutableDictionary<string, string>.Empty.Add(
                "sharpproof_suppression_diagnostic_ids",
                "CS8602, UNKNOWN0001"));

        var diagnostic = diagnostics.Single(item =>
            item.Id == SharpProofDiagnostics.InvalidAnalyzerConfigurationId &&
            item.Properties[SharpProofDiagnostics.ConfigurationKeyProperty] ==
            "sharpproof_suppression_diagnostic_ids");
        Assert.That(
            diagnostic.Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty],
            Does.Contain("unknown values: unknown0001"));
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
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFilePathProperty],
            Is.EqualTo("runtime.SharpProof.EffectSummary.json"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Is.EqualTo("malformed effect-summary JSON"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("malformed effect-summary JSON"));
    }

    [TestCase("[]")]
    [TestCase("null")]
    [TestCase("\"text\"")]
    public async Task NonObjectEffectSummaryAdditionalFile_IsRejectedWithoutAnalyzerFailure(string json)
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "runtime.SharpProof.EffectSummary.json",
                    json)));

        var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
        Assert.Multiple(() =>
        {
            Assert.That(
                diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
                Is.EqualTo("unsupported effect-summary root; expected an object"));
            Assert.That(diagnostics.Select(static item => item.Id), Does.Not.Contain("AD0001"));
        });
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
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Is.EqualTo("file is empty"));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(99)]
    public async Task UnsupportedEffectSummarySchema_ReportsSp0032(int schemaVersion)
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "SharpProof.EffectSummary.json",
                    "{\"SchemaVersion\":" + schemaVersion + ",\"Assemblies\":[]}")));

        var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Does.Contain("unsupported effect-summary SchemaVersion '" + schemaVersion + "'"));
    }

    [Test]
    public async Task UnsupportedEffectSummaryEvidenceSchema_ReportsSp0032()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "SharpProof.EffectSummary.json",
                    "{\"SchemaVersion\":5,\"EvidenceSchemaVersion\":99,\"Assemblies\":[]}")));

        var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Does.Contain("unsupported effect-summary EvidenceSchemaVersion '99'"));
    }

    [Test]
    public async Task MismatchedEffectSummaryEvidenceCompatibility_ReportsSp0032()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "SharpProof.EffectSummary.json",
                    "{\"SchemaVersion\":5,\"EvidenceSchemaVersion\":2," +
                    "\"EvidenceSchemaCompatibility\":\"breaking\",\"Assemblies\":[]}")));

        var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Does.Contain("EvidenceSchemaCompatibility must be 'exact-v2'"));
    }

    [TestCase(
        "AssemblySha256",
        "0000000000000000000000000000000000000000000000000000000000000000",
        "effect_summary_assembly_hash_mismatch")]
    [TestCase(
        "ModuleVersionId",
        "00000000-0000-0000-0000-000000000000",
        "effect_summary_module_version_mismatch")]
    [TestCase("MetadataToken", "0x06000001", "effect_summary_metadata_token_mismatch")]
    [TestCase(
        "MethodBodySha256",
        "0000000000000000000000000000000000000000000000000000000000000000",
        "effect_summary_method_body_hash_mismatch")]
    public async Task StaleEffectSummaryEntry_ReportsPreciseSp0032(
        string propertyName,
        string staleValue,
        string expectedReasonCode)
    {
        var summary = GeneratedPurityTestSupport.CreatePuritySummaryJson(
            typeof(string).Assembly.Location,
            "System.String.get_Length()",
            "pure",
            "[]");
        summary = ReplaceJsonStringProperty(summary, propertyName, staleValue);

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public sealed class TestClass
            {
                [EnforcePure]
                public int Length(string value) => value.Length;
            }
            """,
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "stale.SharpProof.EffectSummary.json",
                    summary)));

        var diagnostic = diagnostics.Single(item =>
            item.Id == SharpProofDiagnostics.InvalidAdditionalFileId &&
            item.Properties[SharpProofDiagnostics.AdditionalFileReasonCodeProperty] == expectedReasonCode);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFilePathProperty],
            Is.EqualTo("stale.SharpProof.EffectSummary.json"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Does.Contain("get_Length"));
        Assert.That(diagnostic.GetMessage(), Does.Contain("was ignored because"));
    }

    [Test]
    public async Task MatchingEffectSummaryEntry_DoesNotReportStaleSp0032()
    {
        var summary = GeneratedPurityTestSupport.CreatePuritySummaryJson(
            typeof(string).Assembly.Location,
            "System.String.get_Length()",
            "pure",
            "[]");

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public sealed class TestClass
            {
                [EnforcePure]
                public int Length(string value) => value.Length;
            }
            """,
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "matching.SharpProof.EffectSummary.json",
                    summary)));

        Assert.That(
            diagnostics.Any(item =>
                item.Id == SharpProofDiagnostics.InvalidAdditionalFileId &&
                item.Properties.ContainsKey(SharpProofDiagnostics.AdditionalFileReasonCodeProperty) &&
                item.Properties[SharpProofDiagnostics.AdditionalFileReasonCodeProperty]!.StartsWith(
                    "effect_summary_",
                    StringComparison.Ordinal)),
            Is.False);
    }

    [TestCase("net8.0", false)]
    [TestCase("net7.0", true)]
    public async Task EffectSummaryArtifactFrameworkSource_ReportsOnlyWhenStale(
        string framework,
        bool expectStaleDiagnostic)
    {
        var summary = GeneratedPurityTestSupport.CreatePuritySummaryJson(
            typeof(string).Assembly.Location,
            "System.String.get_Length()",
            "pure",
            "[]");
        summary = AddArtifactFrameworkSource(summary, framework);

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public sealed class TestClass
            {
                [EnforcePure]
                public int Length(string value) => value.Length;
            }
            """,
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "framework.SharpProof.EffectSummary.json",
                    summary)));

        Assert.That(
            diagnostics.Any(item =>
                item.Id == SharpProofDiagnostics.InvalidAdditionalFileId &&
                item.Properties[SharpProofDiagnostics.AdditionalFileReasonCodeProperty] ==
                "effect_summary_framework_source_mismatch"),
            Is.EqualTo(expectStaleDiagnostic));
    }

    [Test]
    public async Task PartiallyMalformedBaselineAdditionalFile_ReportsSp0032()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "SharpProof.Baseline.json",
                    "{\"evidenceSchemaVersion\":2,\"evidenceSchemaCompatibility\":\"exact-v2\"," +
                    "\"diagnostics\":[{\"id\":\"SP0002\",\"symbol\":\"M:TestClass.Method\"," +
                    "\"path\":\"input.cs\",\"evidenceSchemaVersion\":2," +
                    "\"evidenceSchemaCompatibility\":\"exact-v2\"},{\"id\":\"SP0002\"}]}")));

        var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Does.Contain("baseline entry is missing required evidenceSchemaVersion"));
    }

    [Test]
    public async Task BaselineStructuralPropertyCasing_IsRejected()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "SharpProof.Baseline.json",
                    "{\"EvidenceSchemaVersion\":2,\"EvidenceSchemaCompatibility\":\"exact-v2\"," +
                    "\"Diagnostics\":[{\"ID\":\"SP0002\",\"Symbol\":\"M:TestClass.Method\"," +
                    "\"Path\":\"input.cs\",\"EvidenceSchemaVersion\":2," +
                    "\"EvidenceSchemaCompatibility\":\"exact-v2\"}]}")));

        var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Does.Contain("baseline contains no diagnostic entries"));
    }

    [Test]
    public async Task UnsupportedBaselineEvidenceSchema_ReportsSp0032()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "SharpProof.Baseline.json",
                    "{\"version\":1,\"evidenceSchemaVersion\":2," +
                    "\"evidenceSchemaCompatibility\":\"exact-v2\",\"diagnostics\":[{\"id\":\"SP0002\"," +
                    "\"symbol\":\"M:TestClass.Method\",\"path\":\"input.cs\"," +
                    "\"evidenceSchemaVersion\":99,\"evidenceSchemaCompatibility\":\"future\"}]}")));

        var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Does.Contain("unsupported baseline entry evidenceSchemaVersion '99'"));
    }

    [Test]
    public async Task NestedBaselineDocumentWithoutEvidenceSchema_ReportsSp0032()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "SharpProof.Baseline.json",
                    "{\"evidenceSchemaVersion\":2,\"evidenceSchemaCompatibility\":\"exact-v2\"," +
                    "\"groups\":[{\"diagnostics\":[{\"id\":\"SP0002\"," +
                    "\"symbol\":\"M:TestClass.Method\",\"path\":\"input.cs\"," +
                    "\"evidenceSchemaVersion\":2,\"evidenceSchemaCompatibility\":\"exact-v2\"}]}]}")));

        var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
        Assert.That(
            diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Does.Contain("baseline contains no diagnostic entries"));
    }

    [Test]
    public async Task LegacyBaselineEvidenceSchema_IsRejectedByAnalyzer()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class TestClass { }",
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "SharpProof.Baseline.json",
                    "{\"version\":1,\"evidenceSchemaVersion\":1," +
                    "\"evidenceSchemaCompatibility\":\"additive-v1\",\"diagnostics\":[{" +
                    "\"id\":\"SP0002\",\"symbol\":\"M:TestClass.Method\",\"path\":\"input.cs\"," +
                    "\"evidenceSchemaVersion\":1,\"evidenceSchemaCompatibility\":\"additive-v1\"}]}")));

        var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.InvalidAdditionalFileId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.AdditionalFileReasonProperty],
            Does.Contain("unsupported baseline evidenceSchemaVersion '1'"));
    }

    [Test]
    public void AnalyzerConfigurationOptionRegistry_CoversEveryConfigKey()
    {
        var configKeys = GetConfigKeys();
        var registeredKeys = GetRegisteredOptionKeys();

        Assert.That(registeredKeys, Is.EquivalentTo(configKeys));
    }

    private static string ReplaceJsonStringProperty(string json, string propertyName, string value)
    {
        var pattern = "(\\\"" + Regex.Escape(propertyName) + "\\\"\\s*:\\s*\\\")[^\\\"]*(\\\")";
        return Regex.Replace(
            json,
            pattern,
            match => match.Groups[1].Value + value + match.Groups[2].Value,
            RegexOptions.CultureInvariant);
    }

    private static string AddArtifactFrameworkSource(string json, string framework)
    {
        return json.Replace(
            "\"AssemblyPath\":",
            $"\"ArtifactSource\": {{ \"Kind\": \"framework\", \"Framework\": \"{framework}\" }},\n" +
            "                       \"AssemblyPath\":",
            StringComparison.Ordinal);
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
            Assert.That(
                referenceDoc,
                Does.Contain("| `" + key + "` |"),
                "The generated configuration reference is missing " + key + ".");
    }

    [Test]
    public void AnalyzerConfiguration_DoesNotExposeInertDebugLogging()
    {
        const string removedKey = "sharpproof_enable_debug_logging";
        var repositoryRoot = FindRepositoryRoot();
        var analyzerDirectory = Path.Combine(repositoryRoot, "SharpProof.Analyzer");
        var configurationType = typeof(SharpProofAnalyzer).Assembly
            .GetType("SharpProof.Analyzer.Configuration.AnalyzerConfiguration", true)!;
        var logCallFiles = Directory
            .EnumerateFiles(analyzerDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) &&
                !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) &&
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
            "SharpProof.Analyzer/SharpProofDiagnostics.cs"
        };

        var offenders = Directory
            .EnumerateFiles(analyzerDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Source = File.ReadAllText(path)
            })
            .Where(file => !allowedFiles.Contains(file.Path) &&
                           !file.Path.StartsWith(
                               "SharpProof.Analyzer/SharpProofDiagnostics.",
                               StringComparison.Ordinal) &&
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
            .Where(path =>
                !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) &&
                !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
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

    [Test]
    public void AnalysisLimitConfiguration_ParsesEveryPositiveOverride()
    {
        var options = ReadAnalysisLimits(
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_analysis_max_merged_if_else_facts", "1")
                .Add("sharpproof_analysis_max_merged_switch_facts", "2")
                .Add("sharpproof_analysis_max_merged_try_facts", "3")
                .Add("sharpproof_analysis_max_try_completion_branches", "4")
                .Add("sharpproof_analysis_max_finite_foreach_element_facts", "5")
                .Add("sharpproof_analysis_max_scoped_block_completion_statements", "6")
                .Add("sharpproof_analysis_max_structural_null_state_depth", "7")
                .Add("sharpproof_analysis_max_merged_path_conditions", "8")
                .Add("sharpproof_analysis_max_mergeable_facts_per_target_per_state", "9")
                .Add("sharpproof_analysis_max_fact_choice_combinations_per_target", "10")
                .Add("sharpproof_analysis_max_guard_facts_per_target_per_state", "11"));

        Assert.That(
            options,
            Is.EqualTo(new AnalysisLimitsSnapshot(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)));
    }

    [Test]
    public void SmtLifecycleConfiguration_ParsesGlobalOverrides()
    {
        var lifecycle = ReadSmtLifecycle(
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_smt_transient_retry_count", "3")
                .Add("sharpproof_smt_recycle_context_on_transient_failure", "false")
                .Add("sharpproof_smt_dispose_thread_context_on_service_dispose", "true"));

        Assert.That(lifecycle, Is.EqualTo(new SmtLifecycleSnapshot(3, false, true)));
    }

    private static SmtOptionsSnapshot ReadSmtOptions(ImmutableDictionary<string, string> globalOptions)
    {
        var smtOptions = AnalyzerConfigurationTestAccessor.Read(globalOptions).SmtOptions;

        return new SmtOptionsSnapshot(
            (int)smtOptions.QueryTimeout.TotalMilliseconds,
            (int)smtOptions.MethodBudget.TotalMilliseconds,
            smtOptions.MaxPathConditions,
            smtOptions.MaxExpressionNodes);
    }

    private static AnalysisLimitsSnapshot ReadAnalysisLimits(ImmutableDictionary<string, string> globalOptions)
    {
        var limits = AnalyzerConfigurationTestAccessor.Read(globalOptions).AnalysisLimits;

        return new AnalysisLimitsSnapshot(
            limits.MaxMergedIfElseFacts,
            limits.MaxMergedSwitchFacts,
            limits.MaxMergedTryFacts,
            limits.MaxTryCompletionBranches,
            limits.MaxFiniteForeachElementFacts,
            limits.MaxScopedBlockCompletionStatements,
            limits.MaxStructuralNullStateDepth,
            limits.MaxMergedPathConditions,
            limits.MaxMergeableFactsPerTargetPerState,
            limits.MaxFactChoiceCombinationsPerTarget,
            limits.MaxGuardFactsPerTargetPerState);
    }

    private static SmtLifecycleSnapshot ReadSmtLifecycle(ImmutableDictionary<string, string> globalOptions)
    {
        var lifecycle = AnalyzerConfigurationTestAccessor.Read(globalOptions).SmtOptions.Lifecycle;
        return new SmtLifecycleSnapshot(
            lifecycle.MaxTransientRetries,
            lifecycle.RecycleContextOnTransientFailure,
            lifecycle.DisposeCurrentThreadContextOnServiceDispose);
    }

    private readonly record struct AnalysisLimitsSnapshot(
        int MaxMergedIfElseFacts,
        int MaxMergedSwitchFacts,
        int MaxMergedTryFacts,
        int MaxTryCompletionBranches,
        int MaxFiniteForeachElementFacts,
        int MaxScopedBlockCompletionStatements,
        int MaxStructuralNullStateDepth,
        int MaxMergedPathConditions,
        int MaxMergeableFactsPerTargetPerState,
        int MaxFactChoiceCombinationsPerTarget,
        int MaxGuardFactsPerTargetPerState);

    private readonly record struct SmtLifecycleSnapshot(
        int MaxTransientRetries,
        bool RecycleContextOnTransientFailure,
        bool DisposeCurrentThreadContextOnServiceDispose);

    private static string[] GetConfigKeys()
    {
        var configKeysType = typeof(SharpProofAnalyzer).Assembly
            .GetType("SharpProof.Analyzer.Configuration.ConfigKeys", true)!;
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
            .GetType("SharpProof.Analyzer.Configuration.AnalyzerConfigurationOptionRegistry", true)!;
        var options = (IEnumerable)registryType
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
            if (File.Exists(Path.Combine(directory.FullName, "PLAN.md"))) return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private readonly record struct SmtOptionsSnapshot(
        int TimeoutMs,
        int MethodBudgetMs,
        int MaxPathConditions,
        int MaxExpressionNodes);
    private sealed class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly ImmutableDictionary<string, string> _values;

        internal DictionaryAnalyzerConfigOptions(ImmutableDictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, out string value)
        {
            return _values.TryGetValue(key, out value!);
        }
    }
}
