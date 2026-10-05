using System.Globalization;
using System.Text.Json;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerDiagnosticArtifactOrdering
{
    private static readonly IComparer<CompilerDiagnosticArtifact> Comparer =
        System.Collections.Generic.Comparer<CompilerDiagnosticArtifact>.Create(Compare);

    internal static CompilerDiagnosticArtifact[] Canonicalize(
        IEnumerable<CompilerDiagnosticArtifact> diagnostics)
    {
        return [.. diagnostics
            .OrderBy(static item => item, Comparer)];
    }

    internal static bool IsCanonical(CompilerDiagnosticArtifact[] diagnostics)
    {
        return diagnostics.Zip(
                diagnostics.Skip(1),
                static (left, right) => Compare(left, right) <= 0)
            .All(static ordered => ordered);
    }

    internal static int Compare(
        CompilerDiagnosticArtifact left,
        CompilerDiagnosticArtifact right)
    {
        var result = StringComparer.Ordinal.Compare(
            left.Location.Path, right.Location.Path);
        if (result != 0)
        {
            return result;
        }

        result = left.Location.Start.CompareTo(right.Location.Start);
        if (result != 0)
        {
            return result;
        }

        result = left.Location.Length.CompareTo(right.Location.Length);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(left.Code, right.Code);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(left.Message, right.Message);
        if (result != 0)
        {
            return result;
        }

        result = left.Location.Line.CompareTo(right.Location.Line);
        if (result != 0)
        {
            return result;
        }
        result = left.Location.Column.CompareTo(right.Location.Column);
        if (result != 0)
        {
            return result;
        }
        result = left.SourceTreeOrdinal.CompareTo(right.SourceTreeOrdinal);
        if (result != 0)
        {
            return result;
        }
        result = StringComparer.Ordinal.Compare(left.SourceTreePath, right.SourceTreePath);
        if (result != 0)
        {
            return result;
        }
        result = StringComparer.Ordinal.Compare(left.SourceTreeSha256, right.SourceTreeSha256);
        return result != 0 ? result :
            StringComparer.Ordinal.Compare(left.SourceLineMapSha256, right.SourceLineMapSha256);
    }
}
