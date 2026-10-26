using System.Collections.Immutable;
namespace SharpProof.Gates;

internal static class TrustedPlatformAssemblyPaths
{
    internal static ImmutableArray<string> Get()
    {
        var trustedPlatformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        return [.. trustedPlatformAssemblies.Split(Path.PathSeparator)];
    }
}
