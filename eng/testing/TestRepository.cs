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
        var directory = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")) &&
                File.Exists(Path.Combine(
                    directory.FullName,
                    "SharpProof.Release.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

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

    internal static void DeleteOwnedTemporaryDirectory(
        string path,
        string rootName,
        string errorMessage = "Refusing to remove an unexpected test directory.")
    {
        var resolved = Path.GetFullPath(path);
        var expectedRoot = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), rootName));
        if (!resolved.StartsWith(
                expectedRoot + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(errorMessage);
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }
}
