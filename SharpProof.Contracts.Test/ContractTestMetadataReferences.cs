using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Attributes;

namespace SharpProof.Contracts.Test;

internal static class ContractTestMetadataReferences
{
    internal static ImmutableArray<MetadataReference> Platform { get; } =
        CreatePlatformReferences();

    internal static ImmutableArray<MetadataReference> WithSharpProof { get; } =
        AddSharpProofReference(Platform);

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

    private static ImmutableArray<MetadataReference> AddSharpProofReference(
        ImmutableArray<MetadataReference> platform)
    {
        var location = typeof(Contract).Assembly.Location;
        if (platform.Any(reference => string.Equals(
                reference.Display,
                location,
                StringComparison.OrdinalIgnoreCase)))
        {
            return platform;
        }

        return platform.Add(MetadataReference.CreateFromFile(location));
    }
}
