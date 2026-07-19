namespace SharpProof.Analyzer;

internal sealed class EffectSummaryArtifactSource
{
    private EffectSummaryArtifactSource(
        string kind,
        string? framework,
        string? packageId,
        string? packageVersion,
        string? packageAssemblyRelativePath)
    {
        Kind = kind;
        Framework = framework;
        PackageId = packageId;
        PackageVersion = packageVersion;
        PackageAssemblyRelativePath = packageAssemblyRelativePath;
    }

    private string Kind { get; }

    private string? Framework { get; }

    private string? PackageId { get; }

    private string? PackageVersion { get; }

    private string? PackageAssemblyRelativePath { get; }

    internal static EffectSummaryArtifactSource? FromJson(JsonElement element)
    {
        if (!element.TryGetProperty("ArtifactSource", out var sourceElement) ||
            sourceElement.ValueKind != JsonValueKind.Object)
            return null;

        var kind = AnalyzerJsonElementReader.GetTrimmedStringProperty(sourceElement, "Kind");
        if (string.IsNullOrWhiteSpace(kind)) return null;

        return new EffectSummaryArtifactSource(
            kind!,
            AnalyzerJsonElementReader.GetTrimmedStringProperty(sourceElement, "Framework"),
            AnalyzerJsonElementReader.GetTrimmedStringProperty(sourceElement, "PackageId"),
            AnalyzerJsonElementReader.GetTrimmedStringProperty(sourceElement, "PackageVersion"),
            AnalyzerJsonElementReader.GetTrimmedStringProperty(sourceElement, "PackageAssemblyRelativePath"));
    }

    internal EffectSummaryCompatibility GetCompatibility(ActualAssemblyIdentity actualAssemblyIdentity)
    {
        if (string.Equals(Kind, "framework", StringComparison.OrdinalIgnoreCase))
            return GetFrameworkCompatibility(actualAssemblyIdentity.AssemblyPath);

        if (string.Equals(Kind, "package", StringComparison.OrdinalIgnoreCase))
            return GetPackageCompatibility(actualAssemblyIdentity.AssemblyPath);

        return EffectSummaryCompatibility.Incompatible(
            "effect_summary_artifact_source_unsupported",
            $"artifact source kind '{Kind}' is unsupported");
    }

    private EffectSummaryCompatibility GetFrameworkCompatibility(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(Framework))
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_artifact_source_incomplete",
                "its artifact framework source is missing");

        var actualFramework = InferFrameworkFromPath(assemblyPath);
        if (actualFramework == null)
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_framework_source_unavailable",
                "the actual framework source could not be established");

        if (string.Equals(
                NormalizeFramework(Framework!),
                NormalizeFramework(actualFramework),
                StringComparison.OrdinalIgnoreCase))
            return EffectSummaryCompatibility.Compatible;

        return EffectSummaryCompatibility.Incompatible(
            "effect_summary_framework_source_mismatch",
            $"artifact framework source '{Framework}' does not match '{actualFramework}'");
    }

    private EffectSummaryCompatibility GetPackageCompatibility(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(PackageId) ||
            string.IsNullOrWhiteSpace(PackageVersion) ||
            string.IsNullOrWhiteSpace(PackageAssemblyRelativePath))
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_artifact_source_incomplete",
                "its artifact package source is incomplete");

        if (!TryReadPackagePath(assemblyPath, out var actualPackageId, out var actualVersion, out var actualRelativePath))
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_package_source_unavailable",
                "the actual package source could not be established");

        var expectedRelativePath = NormalizePath(PackageAssemblyRelativePath!);
        if (string.Equals(PackageId, actualPackageId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                NormalizePackageVersion(PackageVersion!),
                NormalizePackageVersion(actualVersion),
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(expectedRelativePath, actualRelativePath, StringComparison.OrdinalIgnoreCase))
            return EffectSummaryCompatibility.Compatible;

        return EffectSummaryCompatibility.Incompatible(
            "effect_summary_package_source_mismatch",
            $"artifact package source '{PackageId} {PackageVersion} {expectedRelativePath}' does not match " +
            $"'{actualPackageId} {actualVersion} {actualRelativePath}'");
    }

    private static string? InferFrameworkFromPath(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath)) return null;

        var segments = NormalizePath(assemblyPath!).Split('/');
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            var segment = segments[index];
            if (IsTargetFrameworkSegment(segment))
                return segment;

            if (index > 0 &&
                (string.Equals(segments[index - 1], "Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(segments[index - 1], "Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase)) &&
                Version.TryParse(segment, out var runtimeVersion))
                return $"net{runtimeVersion.Major}.0";
        }

        return null;
    }

    private static bool IsTargetFrameworkSegment(string segment)
    {
        if (!segment.StartsWith("net", StringComparison.OrdinalIgnoreCase)) return false;

        var digitIndex = 3;
        while (digitIndex < segment.Length && char.IsLetter(segment[digitIndex])) digitIndex++;
        return digitIndex < segment.Length && char.IsDigit(segment[digitIndex]);
    }

    private static bool TryReadPackagePath(
        string? assemblyPath,
        out string packageId,
        out string packageVersion,
        out string relativePath)
    {
        packageId = string.Empty;
        packageVersion = string.Empty;
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(assemblyPath)) return false;

        var segments = NormalizePath(assemblyPath!).Split('/');
        for (var index = segments.Length - 4; index >= 0; index--)
        {
            if (!string.Equals(segments[index], "packages", StringComparison.OrdinalIgnoreCase)) continue;

            packageId = segments[index + 1];
            packageVersion = segments[index + 2];
            relativePath = string.Join("/", segments.Skip(index + 3));
            return true;
        }

        return false;
    }

    private static string NormalizeFramework(string framework)
    {
        var normalized = framework.Trim().ToLowerInvariant();
        var platformSeparator = normalized.IndexOf('-');
        return platformSeparator < 0 ? normalized : normalized.Substring(0, platformSeparator);
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().Replace('\\', '/').Trim('/');
    }

    private static string NormalizePackageVersion(string version)
    {
        var normalized = version.Trim();
        var metadataSeparator = normalized.IndexOf('+');
        if (metadataSeparator >= 0) normalized = normalized.Substring(0, metadataSeparator);

        var prereleaseSeparator = normalized.IndexOf('-');
        var release = prereleaseSeparator >= 0 ? normalized.Substring(0, prereleaseSeparator) : normalized;
        var prerelease = prereleaseSeparator >= 0
            ? normalized.Substring(prereleaseSeparator + 1)
            : string.Empty;
        var parts = release.Split('.').ToList();
        while (parts.Count > 1 && string.Equals(parts[parts.Count - 1], "0", StringComparison.Ordinal))
            parts.RemoveAt(parts.Count - 1);

        var normalizedRelease = string.Join(".", parts);
        return prerelease.Length == 0
            ? normalizedRelease
            : normalizedRelease + "-" + prerelease.ToLowerInvariant();
    }
}
