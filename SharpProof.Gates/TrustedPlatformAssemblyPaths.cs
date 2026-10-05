namespace SharpProof.Gates;

internal static class TrustedPlatformAssemblyPaths
{
    internal static string[] Get()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        return trustedPlatformAssemblies.Split(Path.PathSeparator);
    }
}
