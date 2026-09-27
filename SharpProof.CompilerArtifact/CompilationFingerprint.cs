using System.Globalization;
using System.Text.Json;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilationFingerprint
{
    private const string SyntaxTreeSnapshotDomain =
        "SharpProof.CompilerSyntaxTreeSnapshot";
    private const int SyntaxTreeSnapshotVersion = 2;
    private const string SourceLineMapDomain =
        "SharpProof.CompilerSourceLineMap";
    private const int SourceLineMapVersion = 2;

    private sealed class SummaryEvidenceIndex
    {
        internal SummaryEvidenceIndex(
            Dictionary<(string Path, string Sha256), int> sourceTextLengths,
            Dictionary<(string Name, string Sha256), HashSet<string>>
                implementationModuleMvids)
        {
            SourceTextLengths = sourceTextLengths;
            ImplementationModuleMvids = implementationModuleMvids;
        }

        internal Dictionary<(string Path, string Sha256), int>
            SourceTextLengths
        {
            get;
        }

        internal Dictionary<(string Name, string Sha256), HashSet<string>>
            ImplementationModuleMvids
        {
            get;
        }
    }

    internal static string ComputeLineMapSha256(
        CompilerSourceLineMapEntry[] entries)
    {
        entries = ArgumentNullGuard.NotNull(entries, nameof(entries));

        using var hash = new CanonicalHashWriter();
        hash.Add(SourceLineMapDomain)
            .Add(SourceLineMapVersion)
            .Add(JsonSerializer.SerializeToUtf8Bytes(
                entries,
                WorkerProtocolJson.SharedOptions));
        return hash.Finish();
    }

    internal static string ComputeSyntaxTreeSnapshotSha256(
        CompilerSyntaxTreeSnapshot snapshot)
    {
        snapshot = ArgumentNullGuard.NotNull(snapshot, nameof(snapshot));

        using var hash = new CanonicalHashWriter();
        hash.Add(SyntaxTreeSnapshotDomain)
            .Add(SyntaxTreeSnapshotVersion)
            .Add(JsonSerializer.SerializeToUtf8Bytes(
                snapshot,
                WorkerProtocolJson.SharedOptions));
        return hash.Finish();
    }

    internal static string ComputeSha256(
        CompilerCompilationSnapshot snapshot,
        CompilerDiagnosticArtifact[] diagnostics,
        int maximumExpressionDepth = WorkerBudgets.DefaultMaximumExpressionDepth)
    {
        snapshot = ArgumentNullGuard.NotNull(snapshot, nameof(snapshot));

        using var hash = new CanonicalHashWriter();
        hash.Add("SharpProof.CompilerCompilationSnapshot")
            .Add(10)
            .Add("budget.expression_depth")
            .Add(maximumExpressionDepth)
            .Add(JsonSerializer.Serialize(snapshot, WorkerProtocolJson.SharedOptions))
            .Add(JsonSerializer.Serialize(
                CompilerDiagnosticArtifactOrdering.Canonicalize(
                    ArgumentNullGuard.NotNull(diagnostics, nameof(diagnostics))),
                WorkerProtocolJson.SharedOptions));
        return hash.Finish();
    }

    internal static bool ValidSummaryEvidenceRow(
        CompilerSummaryEvidenceSnapshot row,
        CompilerCompilationSnapshot snapshot,
        bool authorityMode = false,
        bool identityAlreadyValidated = false)
    {
        return ValidSummaryEvidenceRowCore(
            row,
            snapshot,
            authorityMode,
            identityAlreadyValidated,
            evidenceIndex: null);
    }

    private static bool ValidSummaryEvidenceRowCore(
        CompilerSummaryEvidenceSnapshot row,
        CompilerCompilationSnapshot snapshot,
        bool authorityMode,
        bool identityAlreadyValidated,
        SummaryEvidenceIndex? evidenceIndex)
    {
        // JSON deserialization can populate non-nullable string properties with
        // null. Validate the complete shape before the branch-specific checks
        // below, so malformed evidence is rejected rather than throwing while
        // reading Length or comparing a field.
        if (row.CallIdentity is null ||
            row.EvidenceIdentity is null ||
            row.EvidenceSha256 is null ||
            row.SourcePath is null && row.SourceTreeSha256 is null ||
            row.OwningModuleName is null ||
            row.OwningModuleMvid is null ||
            row.OwningModuleSha256 is null ||
            !identityAlreadyValidated &&
            (!WorkerProtocolJson.IsSha256(row.EvidenceSha256) ||
             !ValidIdentity(row.CallIdentity)) ||
            authorityMode &&
            (!WorkerProtocolJson.IsSha256(row.EvidenceSha256) ||
             !ValidIdentity(row.CallIdentity)))
        {
            return false;
        }

        switch (row.Origin)
        {
            case CompilerSummaryOrigin.Source:
                return row.EvidenceIdentity is { Length: 0 } &&
                    row.SourcePath is { Length: > 0 } &&
                    (!authorityMode || row.SourceTreeSha256.Length == 64) &&
                    WorkerProtocolJson.IsSha256(row.SourceTreeSha256) &&
                    row.SourceStart >= 0 &&
                    row.SourceLength > 0 &&
                    row.OwningModuleName.Length == 0 &&
                    row.OwningModuleMvid.Length == 0 &&
                    row.OwningModuleSha256.Length == 0 &&
                    row.MethodMetadataToken == -1 &&
                    ValidSourceEvidenceLocation(
                        row,
                        snapshot,
                        evidenceIndex);

            case CompilerSummaryOrigin.ImplementationIl:
                return row.EvidenceIdentity is { Length: 0 } &&
                    row.SourcePath is { Length: 0 } &&
                    row.SourceTreeSha256 is { Length: 0 } &&
                    row.SourceStart == -1 &&
                    row.SourceLength == -1 &&
                    row.OwningModuleName.Length > 0 &&
                    (authorityMode
                        ? Guid.TryParse(row.OwningModuleMvid, out _)
                        : Guid.TryParseExact(row.OwningModuleMvid, "D", out _)) &&
                    row.OwningModuleSha256 == row.EvidenceSha256 &&
                    row.MethodMetadataToken > 0 &&
                    HasUniqueImplementationModule(
                        row,
                        snapshot,
                        evidenceIndex);

            case CompilerSummaryOrigin.SpecificationPack:
                return row.SourcePath is { Length: 0 } &&
                    row.SourceTreeSha256 is { Length: 0 } &&
                    row.SourceStart == -1 &&
                    row.SourceLength == -1 &&
                    row.OwningModuleName.Length == 0 &&
                    row.OwningModuleMvid.Length == 0 &&
                    row.OwningModuleSha256.Length == 0 &&
                    row.MethodMetadataToken == -1 &&
                    row.EvidenceSha256 == snapshot.SpecificationPackCatalogSha256 &&
                    (!authorityMode ||
                     CompilerSpecificationPackAuthorityValidation.IsValidPackIdentity(
                         row.EvidenceIdentity,
                         snapshot.SpecificationPackIds));

            default:
                return false;
        }
    }

    private static bool ValidSourceEvidenceLocation(
        CompilerSummaryEvidenceSnapshot row,
        CompilerCompilationSnapshot snapshot,
        SummaryEvidenceIndex? evidenceIndex)
    {
        if (evidenceIndex != null)
        {
            return evidenceIndex.SourceTextLengths.TryGetValue(
                       (row.SourcePath, row.SourceTreeSha256),
                       out var textLength) &&
                row.SourceStart <= textLength - row.SourceLength;
        }

        return (snapshot.SyntaxTrees ?? []).Count(tree =>
            tree != null &&
            tree.Path == row.SourcePath &&
            tree.Sha256 == row.SourceTreeSha256 &&
            row.SourceStart <= tree.TextLength - row.SourceLength) == 1;
    }

    private static bool HasUniqueImplementationModule(
        CompilerSummaryEvidenceSnapshot row,
        CompilerCompilationSnapshot snapshot,
        SummaryEvidenceIndex? evidenceIndex)
    {
        if (evidenceIndex != null)
        {
            return evidenceIndex.ImplementationModuleMvids.TryGetValue(
                       (row.OwningModuleName, row.OwningModuleSha256),
                       out var mvids) &&
                mvids.Count == 1 &&
                mvids.Contains(row.OwningModuleMvid);
        }

        var matchingModules = (snapshot.References ?? []).SelectMany(
                static reference => reference?.Modules ?? [])
            .Where(module => module != null &&
                module.Name == row.OwningModuleName &&
                module.Sha256 == row.OwningModuleSha256)
            .ToArray();
        return matchingModules.Length > 0 &&
            matchingModules.All(module =>
                module.Mvid == row.OwningModuleMvid);
    }

    private static bool ValidIdentity(string? value)
    {
        return value is { Length: > 0 and <= 512 } &&
            value.All(static character => !char.IsControl(character));
    }


}

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
