using System.Reflection;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.V2.Test;

[TestFixture]
public sealed class AnalyzerArchitectureTests {
    [Test]
    public async Task ConcurrentRunsProduceTheSameDiagnostics() {
        const string source = """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int state;

                [EnforcePure]
                public static void First() {
                    state = 1;
                }

                [ZeroAllocations]
                public static object Second() => new object();
            }
            """;
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => AnalyzerV2TestHost.AnalyzeAsync(
                source,
                "effects",
                ["SP0002", "SP0045"]))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        var expected = Snapshot(results[0]);

        Assert.That(
            results.Select(static result => Snapshot(result)),
            Is.All.EqualTo(expected));
    }

    [Test]
    public void AnalyzerAssemblyHasOnlyV2SharpProofReferences() {
        var references = typeof(SharpProofAnalyzer).Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name)
            .Where(static name => name != null &&
                                  name.StartsWith(
                                      "SharpProof.",
                                      StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            references,
            Is.SubsetOf(
                new[] {
                    "SharpProof.Attributes",
                    "SharpProof.Contracts",
                    "SharpProof.Effects",
                    "SharpProof.Frontend",
                    "SharpProof.Ir",
                    "SharpProof.Specs"
                }));
        Assert.That(
            references,
            Does.Not.Contain("SharpProof.Symbolic")
                .And.Not.Contain("SharpProof.ProofCore")
                .And.Not.Contain("SharpProof.Smt")
                .And.Not.Contain("SharpProof.Verify"));
    }

    [Test]
    public void AnalyzerOutputContainsNoSolverOrRetiredEngine() {
        var output = Path.GetDirectoryName(
            typeof(SharpProofAnalyzer).Assembly.Location) ??
            throw new InvalidOperationException("Analyzer output path is unavailable.");
        var names = Directory.EnumerateFiles(output, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        Assert.That(
            names,
            Does.Not.Contain("Microsoft.Z3")
                .And.Not.Contain("SharpProof.Symbolic")
                .And.Not.Contain("SharpProof.ProofCore")
                .And.Not.Contain("SharpProof.Smt")
                .And.Not.Contain("SharpProof.Verify"));
    }

    [Test]
    public void ProjectGraphContainsNoRetiredAnalyzerDependency() {
        var root = AnalyzerV2TestHost.FindRepositoryRoot();
        var project = File.ReadAllText(
            Path.Combine(
                root,
                "SharpProof.Analyzer",
                "SharpProof.Analyzer.csproj"));

        Assert.That(project, Does.Not.Contain("Microsoft.Z3"));
        Assert.That(project, Does.Not.Contain("SharpProof.Symbolic"));
        Assert.That(project, Does.Not.Contain("SharpProof.ProofCore"));
        Assert.That(project, Does.Not.Contain("SharpProof.Smt"));
        Assert.That(project, Does.Not.Contain("SharpProof.Verify"));
        Assert.That(project, Does.Contain("SharpProof.Meta.Analyzers"));
    }

    [Test]
    public void OperationKindGateIsExhaustiveAndFutureKindsFailClosed() {
        var runtimeKinds = Enum.GetValues<OperationKind>().Distinct().ToArray();

        Assert.That(
            LanguageSubsetGate.OperationKindDecisions.Keys,
            Is.EquivalentTo(runtimeKinds));
        Assert.That(
            LanguageSubsetGate.OperationKindDecisions.TryGetValue(
                (OperationKind)int.MaxValue,
                out _),
            Is.False);
    }

    [Test]
    public void SubsetAbstentionsUseAClosedTypedReason() {
        Assert.That(
            Enum.GetValues<LanguageSubsetAbstentionReason>(),
            Is.EquivalentTo(new[] {
                LanguageSubsetAbstentionReason.None,
                LanguageSubsetAbstentionReason.UnsupportedCallable,
                LanguageSubsetAbstentionReason.MissingOperationRoot,
                LanguageSubsetAbstentionReason.UnsupportedOperationKind,
                LanguageSubsetAbstentionReason.UnsupportedType,
                LanguageSubsetAbstentionReason.UnsupportedOperationShape
            }));
        var abstention = LanguageSubsetDecision.Abstain(
            LanguageSubsetAbstentionReason.UnsupportedOperationKind,
            OperationKind.DynamicInvocation);

        using (Assert.EnterMultipleScope()) {
            Assert.That(abstention.IsSupported, Is.False);
            Assert.That(
                abstention.Reason,
                Is.EqualTo(
                    LanguageSubsetAbstentionReason.UnsupportedOperationKind));
            Assert.That(
                abstention.OperationKind,
                Is.EqualTo(OperationKind.DynamicInvocation));
        }
    }

    [Test]
    public void ReleaseTrackingMatchesSupportedDescriptorsAndRemovals() {
        var root = AnalyzerV2TestHost.FindRepositoryRoot();
        var analyzerDirectory = Path.Combine(
            root,
            "SharpProof.Analyzer");
        var shipped = ReadReleaseSection(
            Path.Combine(
                analyzerDirectory,
                "AnalyzerReleases.Shipped.md"),
            "New Rules");
        var unshipped = ReadReleaseSection(
            Path.Combine(
                analyzerDirectory,
                "AnalyzerReleases.Unshipped.md"),
            "New Rules");
        var removed = ReadReleaseSection(
            Path.Combine(
                analyzerDirectory,
                "AnalyzerReleases.Unshipped.md"),
            "Removed Rules");
        var changed = ReadChangedReleaseSection(
            Path.Combine(
                analyzerDirectory,
                "AnalyzerReleases.Unshipped.md"));
        Assert.That(
            shipped.Keys.Intersect(
                unshipped.Keys,
                StringComparer.Ordinal),
            Is.Empty,
            "A rule cannot be both shipped and newly unshipped.");
        Assert.That(
            changed.Keys.Intersect(
                removed.Keys,
                StringComparer.Ordinal),
            Is.Empty,
            "A rule cannot be both changed and removed.");
        var tracked = shipped.Values
            .Concat(unshipped.Values)
            .ToDictionary(
                static rule => rule.Id,
                StringComparer.Ordinal);
        foreach (var rule in changed.Values) {
            Assert.That(
                tracked.ContainsKey(rule.Id),
                Is.True,
                rule.Id + " is marked changed without a prior release entry.");
            tracked[rule.Id] = new ReleaseRule(
                rule.Id,
                rule.NewCategory,
                rule.NewSeverity);
        }
        foreach (var rule in removed.Values)
            Assert.That(
                tracked.Remove(rule.Id),
                Is.True,
                rule.Id + " is marked removed without a prior release entry.");

        var descriptors = new SharpProofAnalyzer()
            .SupportedDiagnostics
            .ToDictionary(
                static descriptor => descriptor.Id,
                StringComparer.Ordinal);
        Assert.That(
            tracked.Keys,
            Is.EquivalentTo(descriptors.Keys));
        foreach (var descriptor in descriptors.Values) {
            var rule = tracked[descriptor.Id];
            using (Assert.EnterMultipleScope()) {
                Assert.That(
                    rule.Category,
                    Is.EqualTo(descriptor.Category),
                    descriptor.Id);
                Assert.That(
                    rule.Severity,
                    Is.EqualTo(ReleaseSeverity(descriptor)),
                    descriptor.Id);
            }
        }

        var expectedChangedRules = shipped.Values
            .Where(rule =>
                descriptors.TryGetValue(rule.Id, out var descriptor) &&
                (!string.Equals(
                    rule.Category,
                    descriptor.Category,
                    StringComparison.Ordinal) ||
                 !string.Equals(
                    rule.Severity,
                    ReleaseSeverity(descriptor),
                    StringComparison.Ordinal)))
            .Select(static rule => rule.Id)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.That(
            changed.Keys,
            Is.EquivalentTo(expectedChangedRules));
        foreach (var id in expectedChangedRules) {
            var rule = changed[id];
            var descriptor = descriptors[id];
            using (Assert.EnterMultipleScope()) {
                Assert.That(
                    rule.OldCategory,
                    Is.EqualTo(shipped[id].Category),
                    id);
                Assert.That(
                    rule.OldSeverity,
                    Is.EqualTo(shipped[id].Severity),
                    id);
                Assert.That(
                    rule.NewCategory,
                    Is.EqualTo(descriptor.Category),
                    id);
                Assert.That(
                    rule.NewSeverity,
                    Is.EqualTo(ReleaseSeverity(descriptor)),
                    id);
            }
        }

        var retiredShippedRules = shipped.Keys
            .Except(descriptors.Keys, StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.That(
            removed.Keys,
            Is.EquivalentTo(retiredShippedRules));
        foreach (var id in retiredShippedRules) {
            using (Assert.EnterMultipleScope()) {
                Assert.That(
                    removed[id].Category,
                    Is.EqualTo(shipped[id].Category),
                    id);
                Assert.That(
                    removed[id].Severity,
                    Is.EqualTo(shipped[id].Severity),
                    id);
            }
        }
    }

    private static IReadOnlyDictionary<string, ReleaseRule>
        ReadReleaseSection(
            string path,
            string section) {
        var result = new Dictionary<string, ReleaseRule>(
            StringComparer.Ordinal);
        var inSection = false;
        foreach (var line in File.ReadLines(path)) {
            if (line.StartsWith("### ", StringComparison.Ordinal)) {
                inSection = string.Equals(
                    line.Substring("### ".Length).Trim(),
                    section,
                    StringComparison.Ordinal);
                continue;
            }
            if (!inSection) continue;
            var columns = line
                .Split('|')
                .Select(static column => column.Trim())
                .ToArray();
            if (columns.Length < 4 ||
                columns[0].Length != 6 ||
                !columns[0].StartsWith("SP", StringComparison.Ordinal) ||
                !int.TryParse(columns[0].Substring(2), out _))
                continue;
            var rule = new ReleaseRule(
                columns[0],
                columns[1],
                columns[2]);
            Assert.That(
                result.TryAdd(rule.Id, rule),
                Is.True,
                "Duplicate release row " + rule.Id + " in " + path + ".");
        }
        return result;
    }

    private static IReadOnlyDictionary<string, ChangedReleaseRule>
        ReadChangedReleaseSection(string path) {
        var result = new Dictionary<string, ChangedReleaseRule>(
            StringComparer.Ordinal);
        var inSection = false;
        foreach (var line in File.ReadLines(path)) {
            if (line.StartsWith("### ", StringComparison.Ordinal)) {
                inSection = string.Equals(
                    line.Substring("### ".Length).Trim(),
                    "Changed Rules",
                    StringComparison.Ordinal);
                continue;
            }
            if (!inSection) continue;
            var columns = line
                .Split('|')
                .Select(static column => column.Trim())
                .ToArray();
            if (columns.Length < 6 ||
                columns[0].Length != 6 ||
                !columns[0].StartsWith("SP", StringComparison.Ordinal) ||
                !int.TryParse(columns[0].Substring(2), out _))
                continue;
            var rule = new ChangedReleaseRule(
                columns[0],
                columns[1],
                columns[2],
                columns[3],
                columns[4]);
            Assert.That(
                result.TryAdd(rule.Id, rule),
                Is.True,
                "Duplicate changed-rule row " + rule.Id + " in " + path + ".");
        }
        return result;
    }

    private static string ReleaseSeverity(
        DiagnosticDescriptor descriptor) =>
        descriptor.IsEnabledByDefault
            ? descriptor.DefaultSeverity.ToString()
            : "Disabled";

    private static string Snapshot(IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics) =>
        string.Join(
            "\n",
            diagnostics.Select(diagnostic =>
                diagnostic.Id + "|" +
                diagnostic.Location.SourceSpan.Start + "|" +
                diagnostic.GetMessage()));

    private sealed record ReleaseRule(
        string Id,
        string Category,
        string Severity);

    private sealed record ChangedReleaseRule(
        string Id,
        string NewCategory,
        string NewSeverity,
        string OldCategory,
        string OldSeverity);
}
