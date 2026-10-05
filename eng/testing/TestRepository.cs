using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;

internal static class TestRepository
{
    internal static string Relative(string path)
    {
        return Path.GetRelativePath(FindRoot(), path).Replace('\\', '/');
    }

    internal static string FindRoot(string? start = null)
    {
        return RepositoryRoot.Find(
            start,
            "SharpProof.slnx",
            "SharpProof.Release.props") ??
            throw new DirectoryNotFoundException(
                "Could not locate the SharpProof repository root.");
    }

    internal static JsonDocument ReadSchema(
        string projectDirectory,
        string schemaFileName)
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRoot(),
            projectDirectory,
            schemaFileName)));
    }

    internal static void RequireCanonicalContainer()
    {
        if (OperatingSystem.IsLinux() &&
            RuntimeInformation.ProcessArchitecture == Architecture.X64 &&
            RuntimeInformation.OSArchitecture == Architecture.X64 &&
            string.Equals(
                Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        Assert.Ignore(
            "The packaged worker is supported only in the canonical Linux amd64 container.");
    }

}
