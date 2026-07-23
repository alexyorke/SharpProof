using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Tools.Fuzz;
namespace SharpProof.Test;
[TestFixture]
public class RoslynShapeManifestCoverageTests {
    [Test]
    public void AllSyntaxKindsHaveCoverageDecision() {
        var syntaxShapeIds = RoslynShapeManifest.SyntaxEntries
            .Select(entry => entry.ShapeId)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var missing = Enum.GetValues<SyntaxKind>()
            .Where(kind => !syntaxShapeIds.Contains(RoslynShapeManifest.SyntaxShapeId(kind)))
            .Select(kind => kind.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.That(missing, Is.Empty, "SyntaxKind values without coverage decisions: " + string.Join(", ", missing));
    }
    [Test]
    public void EveryGeneratorBackedShapeHasRegistryEntry() {
        var registryShapeIds = FuzzCaseGenerator.RegistryEntries
            .SelectMany(entry => entry.PrimaryShapeIds)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var missing = RoslynShapeManifest.GeneratorBackedShapeIds
            .Where(shapeId => !registryShapeIds.Contains(shapeId))
            .OrderBy(shapeId => shapeId, StringComparer.Ordinal)
            .ToArray();
        Assert.That(missing, Is.Empty, "Generator-backed manifest shapes without registry entries: " + string.Join(", ", missing));
    }
    [Test]
    public void EveryRegistryEntryReferencesKnownManifestShapes() {
        var knownShapeIds = RoslynShapeManifest.EntriesByShapeId.Keys.ToImmutableHashSet(StringComparer.Ordinal);
        var unknown = FuzzCaseGenerator.RegistryEntries
            .SelectMany(entry => entry.PrimaryShapeIds
                .Where(shapeId => !knownShapeIds.Contains(shapeId))
                .Select(shapeId => entry.Id + ":" + shapeId))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.That(unknown, Is.Empty, "Registry entries reference unknown manifest shapes: " + string.Join(", ", unknown));
    }
    [Test]
    public void EveryRegistryEntryReferencesKnownRoslynKinds() {
        var unknownOperationKinds = FindUnknownRoslynKinds(syntax: false);
        var unknownSyntaxKinds = FindUnknownRoslynKinds(syntax: true);
        Assert.That(unknownOperationKinds, Is.Empty, "Registry entries reference unknown OperationKind values: " + string.Join(", ",
            unknownOperationKinds));
        Assert.That(unknownSyntaxKinds, Is.Empty, "Registry entries reference unknown SyntaxKind values: " + string.Join(", ",
            unknownSyntaxKinds));
    }
    [TestCase(false, TestName = "EveryRegistryEntryEmitsDeclaredOperationKinds")]
    [TestCase(true, TestName = "EveryRegistryEntryEmitsDeclaredSyntaxKinds")]
    public async Task EveryRegistryEntryEmitsDeclaredRoslynKinds(bool syntax) {
        var analyses = await AnalyzeRegistryEntriesAsync();
        foreach (var entry in FuzzCaseGenerator.RegistryEntries) {
            var expected = syntax ? entry.ExpectedSyntaxKinds : entry.ExpectedOperationKinds;
            if (expected.IsDefaultOrEmpty) continue;
            var analysis = analyses[entry.Id];
            Assert.That(analysis.CompilationErrors, Is.Empty, entry.Id);
            var observed = syntax ? analysis.SyntaxKinds : analysis.OperationKinds;
            foreach (var kind in expected)
                Assert.That(observed.ContainsKey(kind), Is.True,
                    entry.Id + " missing " + (syntax ? "syntax" : "operation") + " kind " + kind);
        }
    }
    [Test]
    public async Task WithExpressionRegistryExpectationMatchesAnalysis() {
        var analyses = await AnalyzeRegistryEntriesAsync();
        var analysis = analyses["WithExpression"];
        Assert.That(
            analysis.Findings,
            Is.Empty,
            string.Join(" | ", analysis.Findings.Select(finding =>
                finding.Category + ":" + finding.Description)));
    }
    [Test]
    public void DeterministicSampler_CoversAllGeneratorBackedShapesWithoutRandomSearch() {
        var generator = new FuzzCaseGenerator(20260614);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < RoslynShapeManifest.GeneratorBackedShapeIds.Length; index++) {
            var fuzzCase = generator.Next(index);
            Assert.That(fuzzCase.PrimaryShapeIds.IsDefaultOrEmpty, Is.False, fuzzCase.Family);
            foreach (var shapeId in fuzzCase.PrimaryShapeIds) observed.Add(shapeId);
        }
        var missing = RoslynShapeManifest.GeneratorBackedShapeIds
            .Where(shapeId => !observed.Contains(shapeId))
            .OrderBy(shapeId => shapeId, StringComparer.Ordinal)
            .ToArray();
        Assert.That(missing, Is.Empty, "Deterministic sampler missed generator-backed shapes: " + string.Join(", ", missing));
    }
    private static async Task<ImmutableDictionary<string, FuzzCaseAnalysis>> AnalyzeRegistryEntriesAsync()
        => await ToolingFuzzAnalysisCache.GetRegistryEntryAnalysesAsync();
    private static string[] FindUnknownRoslynKinds(bool syntax) {
        var known = (syntax ? Enum.GetNames<SyntaxKind>() : Enum.GetNames<OperationKind>())
            .ToImmutableHashSet(StringComparer.Ordinal);
        return [
            .. FuzzCaseGenerator.RegistryEntries
                .SelectMany(entry => (syntax ? entry.ExpectedSyntaxKinds : entry.ExpectedOperationKinds)
                    .Where(kind => !known.Contains(kind)).Select(kind => entry.Id + ":" + kind))
                .OrderBy(value => value, StringComparer.Ordinal)
        ];
    }
}
