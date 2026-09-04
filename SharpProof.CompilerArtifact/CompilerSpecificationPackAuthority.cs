using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerSpecificationPackAuthorityValidation
{
    private static readonly char[] PackIdentitySeparators = [';'];
    private static readonly string[] KnownPackIds =
        CompilerSpecificationPackCatalogVersions.PackIds.Split(
            PackIdentitySeparators,
            StringSplitOptions.RemoveEmptyEntries);
    private static readonly string[] KnownPackIdentities =
        CompilerSpecificationPackCatalogVersions.PackIdentities.Split(
            PackIdentitySeparators,
            StringSplitOptions.RemoveEmptyEntries);

    internal static string? GetSummaryPrefix(CompilerSummaryOrigin origin)
    {
        return origin switch
        {
            CompilerSummaryOrigin.Source => "source-summary",
            CompilerSummaryOrigin.ImplementationIl => "il-summary",
            CompilerSummaryOrigin.SpecificationPack => "spec-pack",
            _ => null
        };
    }

    internal static bool IsValid(
        string[]? packIds,
        int catalogVersion,
        string? catalogSha256)
    {
        if (packIds == null ||
            catalogVersion != CompilerSpecificationPackCatalogVersions.Current ||
            catalogSha256 != CompilerSpecificationPackCatalogVersions.Sha256 ||
            !WorkerProtocolJson.IsSha256(catalogSha256!) ||
            !packIds.All(ValidPackId) ||
            !IsCanonical(packIds))
        {
            return false;
        }

        return packIds.All(packId =>
            KnownPackIds.Contains(packId, StringComparer.Ordinal));
    }

    internal static bool Matches(
        CompilerManifestArtifact artifact)
    {
        return artifact != null &&
            Matches(
                artifact.SpecificationPackIds,
                artifact.SpecificationPackCatalogVersion,
                artifact.SpecificationPackCatalogSha256,
                artifact.Compilation);
    }

    internal static bool IsValidPackIdentity(
        string? identity,
        string[]? selectedPackIds)
    {
        if (identity is not { Length: > 0 and <= 128 } ||
            selectedPackIds == null ||
            !KnownPackIdentities.Contains(identity, StringComparer.Ordinal))
        {
            return false;
        }

        var separator = identity.LastIndexOf('@');
        return separator > 0 && selectedPackIds.Contains(
            identity.Substring(0, separator),
            StringComparer.Ordinal);
    }

    private static bool Matches(
        string[]? outerPackIds,
        int outerCatalogVersion,
        string? outerCatalogSha256,
        CompilerCompilationSnapshot? compilation)
    {
        return compilation != null &&
            IsValid(outerPackIds, outerCatalogVersion, outerCatalogSha256) &&
            IsValid(
                compilation.SpecificationPackIds,
                compilation.SpecificationPackCatalogVersion,
                compilation.SpecificationPackCatalogSha256) &&
            outerPackIds!.SequenceEqual(
                compilation.SpecificationPackIds,
                StringComparer.Ordinal) &&
            outerCatalogVersion == compilation.SpecificationPackCatalogVersion &&
            outerCatalogSha256 == compilation.SpecificationPackCatalogSha256;
    }

    private static bool ValidPackId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.All(static character =>
                character is >= 'a' and <= 'z' or
                >= '0' and <= '9' or '.' or '-');
    }

    private static bool IsCanonical(string[] values)
    {
        return values.Zip(values.Skip(1), static (left, right) =>
            StringComparer.Ordinal.Compare(left, right) < 0).All(
                static ordered => ordered);
    }
}
