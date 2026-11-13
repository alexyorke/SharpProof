using System.Globalization;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// docs/release-constants.md lists the version numbers people look up. Each row
// is checked against the constant that owns it, so the page cannot go stale.
[TestFixture]
public sealed class ReleaseConstantsDocumentationTests
{
    private static readonly (string Row, string Value)[] s_expected =
    [
        ("Worker protocol", WorkerProtocolVersions.Current),
        ("Worker manifest schema", Text(WorkerManifestVersions.Current)),
        ("Response cache schema", Text(WorkerCacheVersions.Current)),
        ("Compiler artifact schema", Text(CompilerManifestArtifactVersions.Current)),
        ("Relational summary schema", Text(CompilerRelationalSummaryVersions.Current)),
        ("Specification pack schema", Text(CompilerSpecificationPackVersions.Current)),
        ("Specification pack catalog version", Text(CompilerSpecificationPackCatalogVersions.Current)),
        ("Fork/join projection codec", Text(CompilerForkJoinProjectionCodec.Version)),
        ("Resource program model", Text(ResourceProgram.ModelVersion)),
        ("Resource expression model", Text(ResourceExpression.ModelVersion)),
        ("Default API table version", ApiSpecTable.DefaultTableVersion),
    ];

    [Test]
    public void DocumentedVersionsMatchTheirOwningConstants()
    {
        var path = Path.Combine(TestRepository.FindRoot(), "docs", "release-constants.md");
        var documented = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            // | Value | `current` | Owner |
            var cells = line.Split('|');
            if (cells.Length >= 4 && cells[2].Trim() is { Length: > 2 } value && value[0] == '`' && value[^1] == '`')
            {
                documented[cells[1].Trim()] = value[1..^1];
            }
        }

        using (Assert.EnterMultipleScope())
        {
            foreach (var (row, value) in s_expected)
            {
                Assert.That(documented.TryGetValue(row, out var actual) ? actual : null, Is.EqualTo(value),
                    "docs/release-constants.md row '" + row + "'");
            }
        }
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
