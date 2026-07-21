using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

internal sealed class SymbolicSourceQueryTestSession : IDisposable
{
    private readonly Compilation _compilation;
    private readonly SymbolicQueryExecutor _service = new();
    private readonly SmtAnalysisService _smtAnalysis;
    private readonly SyntaxTree _syntaxTree;

    public SymbolicSourceQueryTestSession(
        string source,
        string filePath,
        bool allowUnsafe = false,
        SmtAnalysisOptions? smtOptions = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _syntaxTree = CSharpSyntaxTree.ParseText(
            Source,
            new CSharpParseOptions(LanguageVersion.Preview),
            FilePath);
        _compilation = CSharpCompilation.Create(
            "SharpProof.Test.SymbolicSourceQuery",
            new[] { _syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));
        _smtAnalysis = new SmtAnalysisService(smtOptions ?? SmtAnalysisOptions.Default);
    }

    public string Source { get; }

    public string FilePath { get; }

    public void Dispose()
    {
        _smtAnalysis.Dispose();
    }

    public SymbolicConditionProofResult ProveAtMarker((int Line, int Column, int Position) marker, string condition)
    {
        return _service.ProveConditionAtSyntaxTree(
            _syntaxTree,
            _compilation,
            marker.Line,
            marker.Column,
            condition,
            _smtAnalysis);
    }

    public int FindLine(string text)
    {
        var lines = Source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            if (lines[index].Contains(text, StringComparison.Ordinal))
                return index + 1;

        throw new InvalidOperationException("Text not found: " + text);
    }

    public (int Line, int Column, int Position) FindMarker(string marker)
    {
        return FindMarker(Source, marker);
    }

    internal static (int Line, int Column, int Position) FindMarker(string source, string marker)
    {
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        if (position < 0) throw new InvalidOperationException("Marker was not found in source.");

        var lines = source.Split('\n');
        var currentPosition = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var nextPosition = currentPosition + lines[index].Length + 1;
            if (position < nextPosition) return (index + 1, position - currentPosition + 1, position);

            currentPosition = nextPosition;
        }

        throw new InvalidOperationException("Marker line was not found in source.");
    }
}
