using System.Text.RegularExpressions;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class AnalyzerReleaseTrackingTests
{
    private static readonly Regex ReleaseRuleRow = new(
        @"^(SP\d{4})\s*\|",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    [Test]
    public void SupportedDiagnosticsAndReleaseFiles_StayInSync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var analyzerDirectory = Path.Combine(repositoryRoot, "SharpProof.Analyzer");
        var newRuleIds = Directory
            .EnumerateFiles(analyzerDirectory, "AnalyzerReleases.*.md")
            .SelectMany(GetNewRuleIds)
            .ToArray();
        var duplicateNewRuleIds = newRuleIds
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var supportedIds = new SharpProofAnalyzer().SupportedDiagnostics
            .Select(descriptor => descriptor.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.That(duplicateNewRuleIds, Is.Empty);
        Assert.That(newRuleIds.OrderBy(id => id, StringComparer.Ordinal), Is.EqualTo(supportedIds));
        Assert.That(newRuleIds, Does.Not.Contain("SP0001"));
    }

    [Test]
    public void AnalyzerProject_DoesNotSuppressReleaseTrackingRules()
    {
        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SharpProof.Analyzer",
            "SharpProof.Analyzer.csproj"));

        Assert.That(project, Does.Not.Contain("RS2007"));
        Assert.That(project, Does.Not.Contain("RS2008"));
    }

    private static IEnumerable<string> GetNewRuleIds(string path)
    {
        var inNewRulesSection = false;
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal)) inNewRulesSection = false;

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                inNewRulesSection = trimmed == "### New Rules";
                continue;
            }

            if (!inNewRulesSection) continue;

            var match = ReleaseRuleRow.Match(trimmed);
            if (match.Success) yield return match.Groups[1].Value;
        }
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
}