using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;

internal static class TestRepository
{
    internal static string Relative(string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        return Path.GetRelativePath(FindRoot(), path).Replace('\\', '/');
    }

    internal static string FindRoot(string? start = null)
    {
        return RepositoryRoot.Find(
            start,
            "SharpProof.slnx",
            "eng/Versions.props") ??
            throw new DirectoryNotFoundException(
                "Could not locate the SharpProof repository root.");
    }

    internal static JsonDocument ReadSchema(
        string projectDirectory,
        string schemaFileName)
    {
        System.ArgumentNullException.ThrowIfNull(projectDirectory);
        System.ArgumentNullException.ThrowIfNull(schemaFileName);
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

        // This is the repository test boundary, not the product support matrix in
        // docs/preview-support.md: consumers also verify on native hosts.
        Assert.Ignore(
            "This test needs the canonical Linux amd64 tooling container.");
    }

}
