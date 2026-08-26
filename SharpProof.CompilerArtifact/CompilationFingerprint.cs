using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpProof.CompilerArtifact;

internal static class CompilationFingerprint
{
    private const string RuntimeContractEvaluationSymbol =
        "SHARPPROOF_CONTRACTS";
    private const string SyntaxTreeSnapshotDomain =
        "SharpProof.CompilerSyntaxTreeSnapshot";
    private const int SyntaxTreeSnapshotVersion = 1;
    private const string SourceLineMapDomain =
        "SharpProof.CompilerSourceLineMap";
    private const int SourceLineMapVersion = 1;

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
                WorkerProtocolJson.Options));
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
                WorkerProtocolJson.Options));
        return hash.Finish();
    }

    internal static string ComputeSha256(
        CompilerCompilationSnapshot snapshot,
        CompilerDiagnosticArtifact[] diagnostics)
    {
        snapshot = ArgumentNullGuard.NotNull(snapshot, nameof(snapshot));
        diagnostics = ArgumentNullGuard.NotNull(diagnostics, nameof(diagnostics));

        var snapshotIdentity = CreateIdentityNode(snapshot);
        NormalizeIdentityPaths(snapshotIdentity, snapshot.ProjectDirectory);
        var diagnosticsIdentity = CreateIdentityNode(
            CompilerDiagnosticArtifactOrdering.Canonicalize(diagnostics));
        NormalizeIdentityPaths(diagnosticsIdentity, snapshot.ProjectDirectory);

        using var hash = new CanonicalHashWriter();
        hash.Add(
            "SharpProof.CompilerCompilationSnapshot",
            10,
            snapshotIdentity.ToJsonString(WorkerProtocolJson.Options),
            diagnosticsIdentity.ToJsonString(WorkerProtocolJson.Options));
        return hash.Finish();
    }

    private static JsonNode CreateIdentityNode<T>(T value)
    {
        return JsonSerializer.SerializeToNode(value, WorkerProtocolJson.Options) ??
            throw new JsonException("Compiler identity serialization returned null.");
    }

    private static void NormalizeIdentityPaths(
        JsonNode node,
        string projectDirectory)
    {
        if (node is JsonObject objectNode)
        {
            foreach (var property in objectNode.ToArray())
            {
                if (property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    IsPathProperty(property.Key))
                {
                    objectNode[property.Key] = NormalizeIdentityPath(
                        text,
                        projectDirectory);
                }
                else if (property.Value != null)
                {
                    NormalizeIdentityPaths(property.Value, projectDirectory);
                }
            }
            return;
        }

        if (node is JsonArray arrayNode)
        {
            foreach (var child in arrayNode)
            {
                if (child != null)
                {
                    NormalizeIdentityPaths(child, projectDirectory);
                }
            }
        }
    }

    private static bool IsPathProperty(string name)
    {
        return name.Equals("projectDirectory", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("path", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sourcePath", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("mappedPath", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("sourceTreePath", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeIdentityPath(
        string value,
        string projectDirectory)
    {
        if (value.Length == 0 ||
            value.StartsWith("<", StringComparison.Ordinal))
        {
            return value.Replace('\\', '/');
        }

        try
        {
            var root = Path.GetFullPath(projectDirectory);
            var fullPath = Path.GetFullPath(value);
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(
                    root.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    fullPath.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    comparison))
            {
                return ".";
            }
            var rootUri = new Uri(
                root.TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar,
                UriKind.Absolute);
            var pathUri = new Uri(fullPath, UriKind.Absolute);
            var relativeUri = rootUri.MakeRelativeUri(pathUri);
            var relative = Uri.UnescapeDataString(relativeUri.ToString())
                .Replace('/', Path.DirectorySeparatorChar);
            if (!relativeUri.IsAbsoluteUri && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                return relative.Replace('\\', '/');
            }

            return "@external/" + Path.GetFileName(fullPath).Replace('\\', '/');
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException)
        {
            return value.Replace('\\', '/');
        }
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
            All(value.SyntaxTrees, ValidTree) &&
            value.SyntaxTrees.Select(static tree => tree.Path)
                .Distinct(StringComparer.Ordinal).Count() == value.SyntaxTrees.Length &&
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

        CompilerSummaryEvidenceSnapshot? previous = null;
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
                row.EvidenceIdentity.Length != 0)
            {
                return false;
            }

            if (previous != null &&
                CompareSummaryEvidence(previous, row) >= 0 ||
                !ValidSummaryEvidenceRow(row, snapshot))
            {
                return false;
            }

            previous = row;
        }

        return true;
    }

    internal static int CompareSummaryEvidence(
        CompilerSummaryEvidenceSnapshot left,
        CompilerSummaryEvidenceSnapshot right)
    {
        var comparison = ((int)left.Origin).CompareTo((int)right.Origin);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(
            left.CallIdentity,
            right.CallIdentity);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.Ordinal.Compare(
            left.EvidenceIdentity,
            right.EvidenceIdentity);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(
                left.EvidenceSha256,
                right.EvidenceSha256);
    }

    private static bool ValidSummaryEvidenceRow(
        CompilerSummaryEvidenceSnapshot row,
        CompilerCompilationSnapshot snapshot)
    {
        switch (row.Origin)
        {
            case CompilerSummaryOrigin.Source:
                return row.EvidenceIdentity.Length == 0 &&
                    row.SourcePath != null &&
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
                return row.EvidenceIdentity.Length == 0 &&
                    row.SourcePath.Length == 0 &&
                    row.SourceTreeSha256.Length == 0 &&
                    row.SourceStart == -1 &&
                    row.SourceLength == -1 &&
                    row.OwningModuleName.Length > 0 &&
                    Guid.TryParseExact(row.OwningModuleMvid, "D", out _) &&
                    row.OwningModuleSha256 == row.EvidenceSha256 &&
                    row.MethodMetadataToken > 0 &&
                    (snapshot.References ?? []).SelectMany(
                        static reference => reference?.Modules ?? [])
                    .Count(module => module != null &&
                        module.Name == row.OwningModuleName &&
                        module.Mvid == row.OwningModuleMvid &&
                        module.Sha256 == row.OwningModuleSha256) == 1;

            case CompilerSummaryOrigin.SpecificationPack:
                return row.SourcePath.Length == 0 &&
                    row.SourceTreeSha256.Length == 0 &&
                    row.SourceStart == -1 &&
                    row.SourceLength == -1 &&
                    row.OwningModuleName.Length == 0 &&
                    row.OwningModuleMvid.Length == 0 &&
                    row.OwningModuleSha256.Length == 0 &&
                    row.MethodMetadataToken == -1 &&
                    row.EvidenceSha256 == snapshot.SpecificationPackCatalogSha256;

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
        return values != null &&
            values.All(static value => value != null &&
                HasText(value.Id) && Enum.IsDefined(
                    typeof(CompilerReportDiagnostic), value.ReportDiagnostic)) &&
            values.Zip(values.Skip(1), static (left, right) =>
                StringComparer.Ordinal.Compare(left.Id, right.Id) < 0)
                .All(static ordered => ordered);
    }

    private static bool ValidTree(CompilerSyntaxTreeSnapshot? value)
    {
        return value != null &&
        CompilerCaptureAuthority.IsCanonicalPath(value.Path) &&
        WorkerProtocolJson.IsSha256(value.Sha256) &&
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
        CompilerCaptureAuthority.IsCanonicalEmptyTree(value) &&
        All(value.Features, ValidFeature) &&
            IsOrdered(value.Features, static feature => feature.Key, unique: true);
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
        (value.Kind == "Assembly" ||
            !value.EmbedInteropTypes && value.Aliases.Length == 0) &&
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
        value.Modules.Zip(value.Modules.Skip(1),
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
        return values != null && values.Zip(values.Skip(1), (left, right) =>
        {
            var comparison = StringComparer.Ordinal.Compare(left, right);
            return unique ? comparison < 0 : comparison <= 0;
        }).All(static ordered => ordered);
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
    internal static CompilerDiagnosticArtifact[] Canonicalize(
        IEnumerable<CompilerDiagnosticArtifact> diagnostics)
    {
        return [.. diagnostics
            .OrderBy(static item => item.Location.Path, StringComparer.Ordinal)
            .ThenBy(static item => item.Location.Start)
            .ThenBy(static item => item.Location.Length)
            .ThenBy(static item => item.Code, StringComparer.Ordinal)
            .ThenBy(static item => item.Message, StringComparer.Ordinal)
            .ThenBy(static item => item.Location.Line)
            .ThenBy(static item => item.Location.Column)];
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
        return result != 0
            ? result
            : left.Location.Column.CompareTo(right.Location.Column);
    }
}
