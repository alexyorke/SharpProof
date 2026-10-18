namespace SharpProof.Host;

// Keep contract readers and the trusted child-process allowlist in agreement.
internal static class HostEnvironmentVariables
{
    internal const string Container = "SHARPPROOF_CONTAINER";
    internal const string ContainerContract = "SHARPPROOF_CONTAINER_CONTRACT";
    internal const string NativeRoot = "SHARPPROOF_NATIVE_ROOT";
}
