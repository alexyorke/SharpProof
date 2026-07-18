using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Test;

[TestFixture]
public sealed class DiagnosticDescriptorMetadataTests
{
    private const string HelpLinkBaseUri =
        "https://github.com/alexyorke/SharpProof/blob/main/docs/diagnostic-examples.md#";

    [Test]
    public void SupportedDiagnostics_MatchReleaseMetadataAndGeneratedRuleAnchors()
    {
        var repositoryRoot = FindRepositoryRoot();
        var releaseMetadata = ReadCurrentReleaseMetadata(repositoryRoot);
        var documentation = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "diagnostic-examples.md"));
        var descriptors = new SharpProofAnalyzer().SupportedDiagnostics.ToArray();
        var duplicateIds = descriptors
            .GroupBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.That(duplicateIds, Is.Empty);
        Assert.That(releaseMetadata.Keys, Is.EquivalentTo(descriptors.Select(descriptor => descriptor.Id)));
        foreach (var descriptor in descriptors)
        {
            var expected = releaseMetadata[descriptor.Id];
            Assert.Multiple(() =>
            {
                Assert.That(descriptor.Category, Is.EqualTo(expected.Category), descriptor.Id);
                Assert.That(descriptor.DefaultSeverity, Is.EqualTo(expected.Severity), descriptor.Id);
                var isProfileEnabledCommonBugRule =
                    string.CompareOrdinal(descriptor.Id, SharpProofDiagnostics.AwaitNullConditionalId) >= 0 &&
                    string.CompareOrdinal(descriptor.Id, SharpProofDiagnostics.UnconsumedDeferredQueryId) <= 0;
                Assert.That(descriptor.IsEnabledByDefault, Is.EqualTo(!isProfileEnabledCommonBugRule), descriptor.Id);
                Assert.That(
                    descriptor.HelpLinkUri,
                    Is.EqualTo(HelpLinkBaseUri + descriptor.Id.ToLowerInvariant()),
                    descriptor.Id);
                Assert.That(descriptor.CustomTags, Does.Contain(WellKnownDiagnosticTags.Telemetry), descriptor.Id);
                Assert.That(descriptor.CustomTags, Does.Not.Contain(WellKnownDiagnosticTags.NotConfigurable),
                    descriptor.Id);
                Assert.That(
                    documentation,
                    Does.Contain($"<a id=\"{descriptor.Id.ToLowerInvariant()}\"></a>"),
                    descriptor.Id);
            });
        }
    }

    [Test]
    public void DiagnosticCatalog_OwnsSupportedDescriptorsAndTypedMetadata()
    {
        var definitions = AnalyzerDiagnosticCatalog.All;

        Assert.That(
            definitions.Select(static definition => definition.Descriptor),
            Is.EqualTo(new SharpProofAnalyzer().SupportedDiagnostics));
        Assert.That(
            definitions.Select(static definition => definition.Descriptor.Id),
            Is.Unique);
        Assert.Multiple(() =>
        {
            Assert.That(definitions, Has.All.Property(nameof(AnalyzerDiagnosticDefinition.OwningFeature))
                .Not.EqualTo(AnalyzerFeatures.None));
            Assert.That(definitions, Has.All.Property(nameof(AnalyzerDiagnosticDefinition.DocumentationUri))
                .Not.Empty);
            Assert.That(
                definitions,
                Has.All.Matches<AnalyzerDiagnosticDefinition>(definition =>
                    string.Equals(
                        definition.DocumentationUri,
                        definition.Descriptor.HelpLinkUri,
                        StringComparison.Ordinal)));
            Assert.That(
                definitions
                    .Select(static definition => definition.ConfigurationKey)
                    .Where(static key => key != null)
                    .Distinct(StringComparer.Ordinal),
                Is.SubsetOf(AnalyzerConfigurationOptionRegistry.All.Select(static option => option.Key)));
        });
    }

    private static Dictionary<string, RuleMetadata> ReadCurrentReleaseMetadata(string repositoryRoot)
    {
        var metadata = new Dictionary<string, RuleMetadata>(StringComparer.Ordinal);
        ApplyReleaseFile(
            Path.Combine(repositoryRoot, "SharpProof.Analyzer", "AnalyzerReleases.Shipped.md"),
            metadata);
        ApplyReleaseFile(
            Path.Combine(repositoryRoot, "SharpProof.Analyzer", "AnalyzerReleases.Unshipped.md"),
            metadata);
        return metadata;
    }

    private static void ApplyReleaseFile(string path, Dictionary<string, RuleMetadata> metadata)
    {
        var section = string.Empty;
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                section = trimmed.Substring("### ".Length);
                continue;
            }

            if (!trimmed.StartsWith("SP", StringComparison.Ordinal)) continue;

            var columns = trimmed.Split('|').Select(column => column.Trim()).ToArray();
            if (section == "New Rules")
            {
                Assert.That(columns, Has.Length.GreaterThanOrEqualTo(4), path);
                Assert.That(metadata.ContainsKey(columns[0]), Is.False, columns[0]);
                metadata.Add(columns[0], new RuleMetadata(columns[1], ParseSeverity(columns[2])));
            }
            else if (section == "Changed Rules")
            {
                Assert.That(columns, Has.Length.GreaterThanOrEqualTo(6), path);
                Assert.That(metadata.TryGetValue(columns[0], out var previous), Is.True, columns[0]);
                Assert.That(previous.Category, Is.EqualTo(columns[3]), columns[0]);
                Assert.That(previous.Severity, Is.EqualTo(ParseSeverity(columns[4])), columns[0]);
                metadata[columns[0]] = new RuleMetadata(columns[1], ParseSeverity(columns[2]));
            }
        }
    }

    private static DiagnosticSeverity ParseSeverity(string value)
    {
        return value switch
        {
            "Hidden" => DiagnosticSeverity.Hidden,
            "Info" => DiagnosticSeverity.Info,
            "Warning" => DiagnosticSeverity.Warning,
            "Error" => DiagnosticSeverity.Error,
            _ => throw new AssertionException($"Unsupported release severity '{value}'.")
        };
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

    private readonly record struct RuleMetadata(string Category, DiagnosticSeverity Severity);
}
