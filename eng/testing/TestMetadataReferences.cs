using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
#if !SHARPPROOF_PLATFORM_REFERENCES_ONLY
using SharpProof.Attributes;
#endif

internal static class TestMetadataReferences
{
    internal static ImmutableArray<MetadataReference> Platform { get; } =
        CreatePlatformReferences();

    internal static ImmutableArray<MetadataReference> SortedPlatform { get; } =
        [.. Platform.OrderBy(
            static reference => reference.Display ?? string.Empty,
            StringComparer.Ordinal)];

    internal static ImmutableArray<MetadataReference> SortedDistinctPlatform { get; } =
        WithAdditionalPaths([], sort: true);

#if !SHARPPROOF_PLATFORM_REFERENCES_ONLY
    internal static ImmutableArray<MetadataReference> WithSharpProof { get; } =
        WithAdditionalPaths([typeof(Contract).Assembly.Location], sort: false);

    internal static ImmutableArray<MetadataReference> WithoutSharpProof { get; } =
        [.. Platform.Where(static reference => !string.Equals(
            Path.GetFileNameWithoutExtension(reference.Display),
            "SharpProof.Attributes",
            StringComparison.OrdinalIgnoreCase))];

    internal static ImmutableArray<MetadataReference> CoreLibraryOnly { get; } =
        [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

    internal static ImmutableArray<MetadataReference> ForFileNames(
        IEnumerable<string> fileNames,
        bool sort)
    {
        var names = new HashSet<string>(
            fileNames,
            StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> paths = Platform
            .Select(static reference => reference.Display)
            .Where(path => path != null &&
                names.Contains(Path.GetFileName(path)!))
            .Select(static path => path!);
        paths = paths.Append(typeof(Contract).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (sort)
        {
            paths = paths.OrderBy(static path => path, StringComparer.Ordinal);
        }

        return [.. paths.Select(static path =>
            (MetadataReference)MetadataReference.CreateFromFile(path))];
    }
#endif

    internal static ImmutableArray<MetadataReference> WithAdditionalPaths(
        IEnumerable<string> additionalPaths,
        bool sort)
    {
        IEnumerable<string> paths = Platform
            .Select(static reference => reference.Display!)
            .Concat(additionalPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (sort)
        {
            paths = paths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
        }

        return [.. paths.Select(static path =>
            (MetadataReference)MetadataReference.CreateFromFile(path))];
    }

    private static ImmutableArray<MetadataReference> CreatePlatformReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        return [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))];
    }

}
