using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Tools.Fuzz;

namespace SharpProof.Test
{
    [TestFixture]
    public class RoslynShapeManifestCoverageTests
    {
        [Test]
        public void AllSyntaxKindsHaveCoverageDecision()
        {
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
        public void EveryGeneratorBackedShapeHasRegistryEntry()
        {
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
        public void EveryRegistryEntryReferencesKnownManifestShapes()
        {
            var knownShapeIds = RoslynShapeManifest.EntriesByShapeId.Keys.ToImmutableHashSet(StringComparer.Ordinal);
            var unknown = FuzzCaseGenerator.RegistryEntries
                .SelectMany(
                    entry => entry.PrimaryShapeIds
                        .Where(shapeId => !knownShapeIds.Contains(shapeId))
                        .Select(shapeId => entry.Id + ":" + shapeId))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            Assert.That(unknown, Is.Empty, "Registry entries reference unknown manifest shapes: " + string.Join(", ", unknown));
        }

        [Test]
        public void EveryRegistryEntryReferencesKnownRoslynKinds()
        {
            var operationKinds = Enum.GetNames<OperationKind>().ToImmutableHashSet(StringComparer.Ordinal);
            var syntaxKinds = Enum.GetNames<SyntaxKind>().ToImmutableHashSet(StringComparer.Ordinal);
            var unknownOperationKinds = FuzzCaseGenerator.RegistryEntries
                .SelectMany(
                    entry => entry.ExpectedOperationKinds
                        .Where(kind => !operationKinds.Contains(kind))
                        .Select(kind => entry.Id + ":" + kind))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var unknownSyntaxKinds = FuzzCaseGenerator.RegistryEntries
                .SelectMany(
                    entry => entry.ExpectedSyntaxKinds
                        .Where(kind => !syntaxKinds.Contains(kind))
                        .Select(kind => entry.Id + ":" + kind))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            Assert.That(unknownOperationKinds, Is.Empty, "Registry entries reference unknown OperationKind values: " + string.Join(", ", unknownOperationKinds));
            Assert.That(unknownSyntaxKinds, Is.Empty, "Registry entries reference unknown SyntaxKind values: " + string.Join(", ", unknownSyntaxKinds));
        }

        [Test]
        public async Task EveryRegistryEntryEmitsDeclaredOperationKinds()
        {
            var analyses = await AnalyzeRegistryEntriesAsync();

            foreach (var entry in FuzzCaseGenerator.RegistryEntries)
            {
                var analysis = analyses[entry.Id];
                Assert.That(analysis.CompilationErrors, Is.Empty, entry.Id);

                foreach (var operationKind in entry.ExpectedOperationKinds)
                {
                    Assert.That(
                        analysis.OperationKinds.ContainsKey(operationKind),
                        Is.True,
                        entry.Id + " missing operation kind " + operationKind);
                }
            }
        }

        [Test]
        public async Task EveryRegistryEntryEmitsDeclaredSyntaxKinds()
        {
            var analyses = await AnalyzeRegistryEntriesAsync();

            foreach (var entry in FuzzCaseGenerator.RegistryEntries)
            {
                if (entry.ExpectedSyntaxKinds.IsDefaultOrEmpty)
                {
                    continue;
                }

                var analysis = analyses[entry.Id];
                Assert.That(analysis.CompilationErrors, Is.Empty, entry.Id);

                foreach (var syntaxKind in entry.ExpectedSyntaxKinds)
                {
                    Assert.That(
                        analysis.SyntaxKinds.ContainsKey(syntaxKind),
                        Is.True,
                        entry.Id + " missing syntax kind " + syntaxKind);
                }
            }
        }

        [Test]
        public void DeterministicSampler_CoversAllGeneratorBackedShapesWithoutRandomSearch()
        {
            var generator = new FuzzCaseGenerator(20260614);
            var observed = new HashSet<string>(StringComparer.Ordinal);

            for (var index = 0; index < RoslynShapeManifest.GeneratorBackedShapeIds.Length; index++)
            {
                var fuzzCase = generator.Next(index);
                Assert.That(fuzzCase.PrimaryShapeIds.IsDefaultOrEmpty, Is.False, fuzzCase.Family);

                foreach (var shapeId in fuzzCase.PrimaryShapeIds)
                {
                    observed.Add(shapeId);
                }
            }

            var missing = RoslynShapeManifest.GeneratorBackedShapeIds
                .Where(shapeId => !observed.Contains(shapeId))
                .OrderBy(shapeId => shapeId, StringComparer.Ordinal)
                .ToArray();

            Assert.That(missing, Is.Empty, "Deterministic sampler missed generator-backed shapes: " + string.Join(", ", missing));
        }

        private static async Task<ImmutableDictionary<string, FuzzCaseAnalysis>> AnalyzeRegistryEntriesAsync()
        {
            return await ToolingFuzzAnalysisCache.GetRegistryEntryAnalysesAsync();
        }
    }
}
