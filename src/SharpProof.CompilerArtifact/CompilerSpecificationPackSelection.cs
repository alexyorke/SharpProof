using System.Collections.Immutable;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerSpecificationPackSelection
{
    private static readonly ImmutableHashSet<string> KnownPackIds =
        ImmutableHashSet.CreateRange(
            StringComparer.Ordinal,
            CompilerSpecificationPackCatalogVersions.PackIds.Split(
                new[] { ';' },
                StringSplitOptions.RemoveEmptyEntries));
    internal static bool IsValid(
        ImmutableArray<string>? packIds,
        int catalogVersion,
        string? catalogSha256)
    {
        if (packIds is not { } snapshot || snapshot.IsDefault ||
            catalogVersion != CompilerSpecificationPackCatalogVersions.Current ||
            catalogSha256 != CompilerSpecificationPackCatalogVersions.Sha256 ||
            !snapshot.All(ValidPackId) ||
            !IsCanonical(snapshot))
        {
            return false;
        }

        return snapshot.All(KnownPackIds.Contains);
    }

    internal static bool IsValid(
        ImmutableArray<string> packIds, int catalogVersion, string? catalogSha256)
    {
        return !packIds.IsDefault &&
            IsValid((ImmutableArray<string>?)packIds, catalogVersion, catalogSha256);
    }

    internal static bool Matches(
        CompilerManifestArtifact artifact)
    {
        artifact = RepositoryParameterGuard.NotNull(artifact, nameof(artifact));
        return artifact != null && artifact.Compilation != null &&
            IsValid(artifact.SpecificationPackIds, artifact.SpecificationPackCatalogVersion,
                artifact.SpecificationPackCatalogSha256);
    }
    private static bool ValidPackId(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.All(static character =>
                character is >= 'a' and <= 'z' or
                >= '0' and <= '9' or '.' or '-');
    }

    private static bool IsCanonical(ImmutableArray<string> values)
    {
        RepositoryParameterGuard.RequireInitialized(values.IsDefault, nameof(values), "Array must be initialized.");
        return values.Zip(values.Skip(1), static (left, right) =>
            left is null ? throw new System.ArgumentNullException(nameof(left)) : right is null ? throw new System.ArgumentNullException(nameof(right)) : (StringComparer.Ordinal.Compare(left, right) < 0)).All(
                static ordered => ordered);
    }
}
