using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class ModernCSharpSurfaceDocumentationTests
{
    private static readonly string[] RequiredColumns =
    {
        "Feature",
        "C#",
        "Analyzer",
        "Symbolic IR",
        "Runtime hazards",
        "Capability",
        "Allocation",
        "Complexity",
        "Ensures"
    };

    private static readonly string[] RequiredFeatures =
    {
        "Primary constructors",
        "Collection expressions",
        "Inline arrays",
        "Ref readonly parameters",
        "Alias any type",
        "Interceptors",
        "Params collections",
        "New lock type and semantics",
        "Ref locals in async or iterator methods",
        "Ref struct interfaces and allows ref struct generics",
        "Partial properties and indexers",
        "Field-backed properties",
        "Extension properties",
        "Extension operators",
        "Static extension members"
    };

    private static readonly string[] SurfaceStatusPrefixes =
    {
        "Covered",
        "Partial",
        "Conservative",
        "Gap"
    };

    [Test]
    public void ModernCSharpSurfaceMatrix_CoversRequiredFeaturesAndSurfaces()
    {
        var repositoryRoot = FindRepositoryRoot();
        var documentPath = Path.Combine(repositoryRoot, "docs", "modern-csharp-surface.md");
        var document = File.ReadAllText(documentPath);
        var rows = ReadMatrixRows(document);

        Assert.That(document, Does.Contain("Status key:"));
        Assert.That(rows.Keys, Is.EquivalentTo(RequiredFeatures));

        foreach (var row in rows)
        {
            Assert.That(row.Value.Length, Is.EqualTo(RequiredColumns.Length), row.Key);
            for (var columnIndex = 0; columnIndex < row.Value.Length; columnIndex++)
            {
                var cell = row.Value[columnIndex];
                Assert.That(cell, Is.Not.Empty, $"{row.Key} {RequiredColumns[columnIndex]}");
                Assert.That(cell, Does.Not.Contain("TBD").IgnoreCase, $"{row.Key} {RequiredColumns[columnIndex]}");
                Assert.That(cell, Does.Not.Contain("TODO").IgnoreCase, $"{row.Key} {RequiredColumns[columnIndex]}");
            }

            foreach (var cell in row.Value.Skip(2))
                Assert.That(
                    SurfaceStatusPrefixes.Any(prefix => cell.StartsWith(prefix, StringComparison.Ordinal)),
                    Is.True,
                    row.Key + " has an unrecognized surface status: " + cell);
        }
    }

    [Test]
    public void ModernCSharpSurfaceMatrix_IsLinkedFromReadme()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));

        Assert.That(readme, Does.Contain("docs/modern-csharp-surface.md"));
    }

    private static IReadOnlyDictionary<string, string[]> ReadMatrixRows(string document)
    {
        var lines = document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var headerIndex = Array.FindIndex(
            lines,
            line => string.Equals(line.Trim(),
                "| Feature | C# | Analyzer | Symbolic IR | Runtime hazards | Capability | Allocation | Complexity | Ensures |",
                StringComparison.Ordinal));

        Assert.That(headerIndex, Is.GreaterThanOrEqualTo(0), "Modern C# matrix header is missing.");
        Assert.That(SplitRow(lines[headerIndex]), Is.EqualTo(RequiredColumns));

        var rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (var index = headerIndex + 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal)) break;

            var cells = SplitRow(line);
            if (cells.All(cell => cell.All(ch => ch == '-'))) continue;

            Assert.That(cells.Length, Is.EqualTo(RequiredColumns.Length), line);
            rows.Add(cells[0], cells);
        }

        return rows;
    }

    private static string[] SplitRow(string line)
    {
        return line
            .Trim()
            .Trim('|')
            .Split('|')
            .Select(static cell => cell.Trim())
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln"))) return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
