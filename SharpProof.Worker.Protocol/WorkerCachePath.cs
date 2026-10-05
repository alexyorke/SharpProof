namespace SharpProof.Worker.Protocol;

internal static class WorkerCachePath
{
    internal static string Resolve(
        string? configuredDirectory,
        string projectDirectory)
    {
        return Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine(projectDirectory, "obj", "SharpProof", "cache")
                : Path.Combine(projectDirectory, configuredDirectory!));
    }

}
