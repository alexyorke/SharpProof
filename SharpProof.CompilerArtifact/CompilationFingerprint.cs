using System.Globalization;
using System.Text.Json;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilationFingerprint
{
    private const string RuntimeContractEvaluationSymbol =
        "SHARPPROOF_CONTRACTS";
    private const string SyntaxTreeSnapshotDomain =
        "SharpProof.CompilerSyntaxTreeSnapshot";
    private const int SyntaxTreeSnapshotVersion = 2;
    private const string SourceLineMapDomain =
        "SharpProof.CompilerSourceLineMap";
    private const int SourceLineMapVersion = 2;

    internal static string ComputeLineMapSha256(
        CompilerSourceLineMapEntry[] entries)
    {
        entries = ArgumentNullGuard.NotNull(entries, nameof(entries));

        using var hash = new CanonicalHashWriter();
        hash.Add(
            SourceLineMapDomain,
            SourceLineMapVersion,
            JsonSerializer.SerializeToUtf8Bytes(
                entries,
                WorkerProtocolJson.SharedOptions));
        return hash.Finish();
    }

    internal static string ComputeSyntaxTreeSnapshotSha256(
        CompilerSyntaxTreeSnapshot snapshot)
    {
        snapshot = ArgumentNullGuard.NotNull(snapshot, nameof(snapshot));

        using var hash = new CanonicalHashWriter();
        hash.Add(
            SyntaxTreeSnapshotDomain,
            SyntaxTreeSnapshotVersion,
            JsonSerializer.SerializeToUtf8Bytes(
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
        hash.Add(
            "SharpProof.CompilerCompilationSnapshot",
            10,
            "budget.expression_depth", maximumExpressionDepth,
            JsonSerializer.Serialize(snapshot, WorkerProtocolJson.SharedOptions),
            JsonSerializer.Serialize(
                CompilerDiagnosticArtifactOrdering.Canonicalize(
                    ArgumentNullGuard.NotNull(diagnostics, nameof(diagnostics))),
                WorkerProtocolJson.SharedOptions));
        return hash.Finish();
    }

    internal static void ValidateShape(CompilerCompilationSnapshot snapshot)
    {
        if (!ValidSnapshot(snapshot))
        {
            throw new JsonException("The compiler compilation evidence is invalid.");
        }
    }

    private static bool ValidSnapshot(CompilerCompilationSnapshot? value)
    {
        if (value is null)
        {
            return false;
        }

        return CompilerCaptureAuthority.IsCanonicalPath(value.ProjectDirectory) &&
            HasText(value.AssemblyName) &&
            CompilerCaptureAuthority.IsCanonicalAssemblyIdentity(
                value.AssemblyIdentity,
                out var identityName) &&
            string.Equals(
                value.AssemblyName,
                identityName,
                StringComparison.Ordinal) &&
            HasText(value.TargetFramework) &&
            CompilerCaptureAuthority.IsCanonicalVersion(value.CompilerVersion) &&
            CompilerCaptureAuthority.IsCanonicalMvid(value.CompilerMvid) &&
            CompilerCaptureAuthority.IsCanonicalVersion(
                value.CSharpCompilerVersion) &&
            CompilerCaptureAuthority.IsCanonicalMvid(
                value.CSharpCompilerMvid) &&
            CompilerSpecificationPackAuthorityValidation.IsValid(
                value.SpecificationPackIds,
                value.SpecificationPackCatalogVersion,
                value.SpecificationPackCatalogSha256) &&
            ValidOptions(value.Options) &&
            ValidTrees(value.SyntaxTrees) &&
            ValidReferences(value.References) &&
            ValidAdditionalFiles(value.AdditionalFiles) &&
            ValidSummaryEvidence(value.SummaryEvidence, value);
    }

    private static bool ValidSummaryEvidence(
        CompilerSummaryEvidenceSnapshot[]? values,
        CompilerCompilationSnapshot snapshot)
    {
        if (values == null)
        {
            return false;
        }

        string? previous = null;
        foreach (var row in values)
        {
            if (row == null ||
                !Enum.IsDefined(typeof(CompilerSummaryOrigin), row.Origin) ||
                !ValidIdentity(row.CallIdentity) ||
                !WorkerProtocolJson.IsSha256(row.EvidenceSha256) ||
                !CompilerSpecificationPackAuthorityValidation.IsValidPackIdentity(
                    row.Origin == CompilerSummaryOrigin.SpecificationPack
                        ? row.EvidenceIdentity
                        : null,
                    snapshot.SpecificationPackIds) &&
                row.Origin == CompilerSummaryOrigin.SpecificationPack ||
                row.Origin != CompilerSummaryOrigin.SpecificationPack &&
                (row.EvidenceIdentity is null ||
                 row.EvidenceIdentity.Length != 0))
            {
                return false;
            }

            var key = ((int)row.Origin).ToString(CultureInfo.InvariantCulture) + "|" +
                row.CallIdentity + "|" + row.EvidenceIdentity + "|" + row.EvidenceSha256;
            if (previous != null &&
                StringComparer.Ordinal.Compare(previous, key) >= 0 ||
                !ValidSummaryEvidenceRow(row, snapshot))
            {
                return false;
            }

            previous = key;
        }

        return true;
    }

    internal static bool ValidSummaryEvidenceRow(
        CompilerSummaryEvidenceSnapshot row,
        CompilerCompilationSnapshot snapshot,
        bool authorityMode = false)
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
                    (snapshot.SyntaxTrees ?? []).Count(tree =>
                        tree != null &&
                        tree.Path == row.SourcePath &&
                        tree.Sha256 == row.SourceTreeSha256 &&
                        row.SourceStart <= tree.TextLength - row.SourceLength) == 1;

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
                    (snapshot.References ?? []).SelectMany(
                        static reference => reference?.Modules ?? [])
                    .Count(module => module != null &&
                        module.Name == row.OwningModuleName &&
                        module.Mvid == row.OwningModuleMvid &&
                        module.Sha256 == row.OwningModuleSha256) == 1;

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

    private static bool ValidIdentity(string? value)
    {
        return value is { Length: > 0 and <= 512 } &&
            value.All(static character => !char.IsControl(character));
    }

    private static bool ValidReferences(
        CompilerReferenceSnapshot[]? references)
    {
        if (references == null || !All(references, ValidReference))
        {
            return false;
        }

        var count = 0;
        long size = 0;
        foreach (var reference in references)
        {
            foreach (var module in reference.Modules)
            {
                count++;
                size += module.SizeBytes;
                if (count > CompilerReferenceLimits.MaximumModuleCount ||
                    size > CompilerReferenceLimits.MaximumClosureBytes)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool ValidOptions(CompilerCompilationOptionsSnapshot? value)
    {
        return value != null &&
        Enum.IsDefined(typeof(CompilerOutputKind), value.OutputKind) &&
        value.MainTypeName != null &&
        Enum.IsDefined(typeof(CompilerOptimizationLevel), value.OptimizationLevel) &&
        Enum.IsDefined(typeof(CompilerPlatform), value.Platform) &&
        Enum.IsDefined(typeof(CompilerNullableContext), value.NullableContext) &&
        Enum.IsDefined(typeof(CompilerMetadataImportOptions), value.MetadataImportOptions) &&
        value.WarningLevel >= 0 &&
        Enum.IsDefined(
            typeof(CompilerReportDiagnostic), value.GeneralDiagnosticOption) &&
        ValidDiagnosticOptions(value.SpecificDiagnosticOptions) &&
        !value.ReferencesSupersedeLowerVersions &&
        Enum.IsDefined(typeof(CompilerAssemblyIdentityComparer), value.AssemblyIdentityComparer) &&
        All(value.Usings, HasText) &&
        value.ResolverPolicy == CompilerResolverPolicy.EvidenceOnly;
    }

    private static bool ValidDiagnosticOptions(
        CompilerDiagnosticOptionSnapshot[]? values)
    {
        if (values == null)
        {
            return false;
        }

        CompilerDiagnosticOptionSnapshot? previous = null;
        foreach (var value in values)
        {
            if (value == null ||
                !HasText(value.Id) ||
                !Enum.IsDefined(
                    typeof(CompilerReportDiagnostic),
                    value.ReportDiagnostic) ||
                previous != null &&
                StringComparer.Ordinal.Compare(previous.Id, value.Id) >= 0)
            {
                return false;
            }

            previous = value;
        }

        return true;
    }

    private static bool ValidTree(CompilerSyntaxTreeSnapshot? value)
    {
        return value != null &&
        CompilerCaptureAuthority.IsCanonicalPath(value.Path) &&
        WorkerProtocolJson.IsSha256(value.Sha256) &&
        value.Encoding is not null &&
        value.ChecksumAlgorithm is "Sha1" or "Sha256" &&
        IsChecksum(value.RoslynChecksum, value.ChecksumAlgorithm) &&
        WorkerProtocolJson.IsSha256(value.LineMapSha256) &&
        value.TextLength >= 0 &&
        CompilerSourceLocationAuthority.HasValidLineMap(value) &&
        CompilerCaptureAuthority.IsCanonicalLanguageVersion(
            value.LanguageVersion) &&
        value.DocumentationMode is "None" or "Parse" or "Diagnose" &&
        value.Kind is "Regular" or "Script" &&
        All(value.PreprocessorSymbols, HasText) &&
        IsOrdered(value.PreprocessorSymbols, unique: false) &&
        All(value.EffectivePreprocessorSymbols, HasText) &&
        IsOrdered(value.EffectivePreprocessorSymbols, unique: true) &&
        !value.EffectivePreprocessorSymbols.Contains(
            RuntimeContractEvaluationSymbol,
            StringComparer.Ordinal) &&
        CompilerCaptureAuthority.HasValidEmptyTreeRepresentation(value) &&
        All(value.Features, ValidFeature) &&
            IsOrdered(value.Features, static feature => feature.Key, unique: true);
    }

    private static bool ValidTrees(CompilerSyntaxTreeSnapshot[]? values)
    {
        if (values is null)
        {
            return false;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!ValidTree(value) || !paths.Add(value.Path))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsChecksum(string? value, string algorithm)
    {
        var length = algorithm == "Sha1" ? 40 : 64;
        return value is not null && value.Length == length &&
            value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool ValidFeature(CompilerFeatureSnapshot? value)
    {
        return value != null && HasText(value.Key) && value.Value != null;
    }

    private static bool ValidReference(CompilerReferenceSnapshot? value)
    {
        return value != null &&
        value.Kind is "Assembly" or "Module" &&
        All(value.Aliases, HasText) &&
        IsOrdered(value.Aliases, unique: false) &&
        (value.Kind == "Assembly"
            ? CompilerCaptureAuthority.IsCanonicalAssemblyIdentity(
                value.Identity,
                out _)
            : HasText(value.Identity)) &&
        value.Modules is { Length: > 0 } &&
        (value.Kind == "Assembly" || !value.EmbedInteropTypes &&
            value.Aliases.Length == 0 && value.Modules.Length == 1 &&
            string.Equals(value.Identity, value.Modules[0].Name,
                StringComparison.Ordinal)) &&
        All(value.Modules, ValidReferenceModule) &&
        value.Modules.Select(static module => module.Name)
            .Distinct(StringComparer.Ordinal).Count() == value.Modules.Length &&
        value.Modules.Skip(1).Zip(value.Modules.Skip(2),
                static (left, right) => StringComparer.Ordinal.Compare(
                    left.Name, right.Name) < 0)
            .All(static ordered => ordered) &&
        value.Modules.Select(static module => module.Path)
            .Distinct(PathComparer).Count() ==
        value.Modules.Length;
    }

    private static StringComparer PathComparer => StringComparer.Ordinal;

    private static bool ValidReferenceModule(
        CompilerReferenceModuleSnapshot? value)
    {
        return value != null &&
            HasText(value.Name) &&
            CompilerCaptureAuthority.IsCanonicalMvid(value.Mvid) &&
            CompilerCaptureAuthority.IsCanonicalPath(value.Path) &&
            WorkerProtocolJson.IsSha256(value.Sha256) &&
            value.SizeBytes is > 0 and
                <= CompilerReferenceLimits.MaximumModuleBytes;
    }

    private static bool ValidAdditionalFiles(CompilerAdditionalFileSnapshot[]? values)
    {
        return values != null &&
        All(values, ValidAdditionalFile) &&
        values.Select(static value => value.Path).Distinct(PathComparer).Count() == values.Length &&
        values.Zip(values.Skip(1), static (left, right) => Compare(left, right) < 0).All(static ordered => ordered);
    }

    private static bool ValidAdditionalFile(CompilerAdditionalFileSnapshot? value)
    {
        return value != null &&
            CompilerCaptureAuthority.IsCanonicalPath(value.Path) &&
            WorkerProtocolJson.IsSha256(value.Sha256);
    }

    private static bool IsOrdered(string[]? values, bool unique)
    {
        return IsOrdered(values, static value => value, unique);
    }

    private static bool IsOrdered<T>(
        T[]? values,
        Func<T, string> key,
        bool unique)
    {
        return values != null && values.Zip(values.Skip(1), (left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(key(left), key(right));
            return unique ? comparison < 0 : comparison <= 0;
        }).All(static ordered => ordered);
    }

    private static int Compare(
        CompilerAdditionalFileSnapshot left,
        CompilerAdditionalFileSnapshot right)
    {
        var path = StringComparer.Ordinal.Compare(left.Path, right.Path);
        return path != 0 ? path : StringComparer.Ordinal.Compare(left.Sha256, right.Sha256);
    }

    private static bool All<T>(T[]? values, Func<T, bool> predicate)
    {
        return values != null && values.All(predicate);
    }

    private static bool HasText(string value)
    {
        return !string.IsNullOrWhiteSpace(value);
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

    private static int Compare(
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
