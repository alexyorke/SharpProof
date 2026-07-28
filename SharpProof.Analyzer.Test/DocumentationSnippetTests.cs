using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "NUnit instantiates test fixtures through reflection.")]
internal sealed class DocumentationSnippetTests {
    private static readonly Regex CSharpFence = new(
        "^```csharp[ \t]*\n(?<code>.*?)^```[ \t]*$",
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline |
        RegexOptions.Singleline);

    [TestCaseSource(nameof(GetCSharpSnippets))]
    public void MaintainedCSharpFenceCompiles(
        string relativePath,
        int ordinal,
        string source) {
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.That(
            errors,
            Is.Empty,
            relativePath + " C# fence " + ordinal + " did not compile:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(
                static diagnostic => diagnostic.ToString())));
    }

    private static IEnumerable<TestCaseData> GetCSharpSnippets() {
        var root = AnalyzerTestHost.FindRepositoryRoot();
        var paths = new[] { Path.Combine(root, "README.md") }
            .Concat(Directory.EnumerateFiles(
                Path.Combine(root, "docs"),
                "*.md",
                SearchOption.AllDirectories))
            .Concat([
                Path.Combine(root, "samples", "README.md")
            ])
            .OrderBy(static path => path, StringComparer.Ordinal);
        foreach (var path in paths) {
            var content = File.ReadAllText(path).Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal);
            var relativePath = Path.GetRelativePath(root, path)
                .Replace('\\', '/');
            var ordinal = 0;
            foreach (Match match in CSharpFence.Matches(content)) {
                ordinal++;
                yield return new TestCaseData(
                        relativePath,
                        ordinal,
                        match.Groups["code"].Value)
                    .SetName(
                        "CSharpFence_" +
                        Regex.Replace(relativePath, "[^A-Za-z0-9]+", "_") +
                        "_" + ordinal);
            }
        }
    }
}
