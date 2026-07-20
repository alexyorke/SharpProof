internal static class RuntimeAssemblyResolver {
    public static string Resolve(string framework, string assemblyName) {
        foreach (var assemblyPath in EnumerateCandidateAssemblyPaths(framework, assemblyName))
            if (File.Exists(assemblyPath))
                return assemblyPath;

        throw new FileNotFoundException(
            $"Runtime assembly '{assemblyName}' was not found for {framework}. Checked the current runtime directory, TRUSTED_PLATFORM_ASSEMBLIES, and shared runtime locations.",
            assemblyName);
    }

    public static string[] ResolveSystemRuntimeAssemblies(string framework) {
        var coreLibPath = Resolve(framework, "System.Private.CoreLib.dll");
        var runtimeDirectory = Path.GetDirectoryName(coreLibPath)
                               ?? throw new DirectoryNotFoundException(
                                   $"Unable to resolve runtime directory from '{coreLibPath}'.");
        return Directory
            .EnumerateFiles(runtimeDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .Where(IsSystemRuntimeAssemblyFile)
            .Where(HasManagedMetadata)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSystemRuntimeAssemblyFile(string path) {
        var fileName = Path.GetFileName(path);
        return fileName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("System.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasManagedMetadata(string path) {
        try {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.HasMetadata;
        }
        catch (BadImageFormatException) {
            return false;
        }
        catch (IOException) {
            return false;
        }
        catch (UnauthorizedAccessException) {
            return false;
        }
    }

    private static int ParseMajorFrameworkVersion(string framework) {
        if (!framework.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported framework moniker '{framework}'. Expected netX.Y.");

        var digits = new string(framework.Skip(3).TakeWhile(char.IsDigit).ToArray());
        if (digits.Length == 0)
            throw new ArgumentException($"Unsupported framework moniker '{framework}'. Expected netX.Y.");

        return int.Parse(digits);
    }

    private static Version? TryParseVersion(string text) =>
        Version.TryParse(text, out var version) ? version : null;

    private static IEnumerable<string> EnumerateCandidateAssemblyPaths(string framework, string assemblyName) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCurrentRuntimeCandidates(assemblyName))
            if (seen.Add(candidate))
                yield return candidate;

        foreach (var candidate in EnumerateTrustedPlatformAssemblyCandidates(assemblyName))
            if (seen.Add(candidate))
                yield return candidate;

        foreach (var candidate in EnumerateSharedRuntimeCandidates(framework, assemblyName))
            if (seen.Add(candidate))
                yield return candidate;
    }

    private static IEnumerable<string> EnumerateCurrentRuntimeCandidates(string assemblyName) {
        var directories = new[] {
            Path.GetDirectoryName(typeof(object).Assembly.Location),
            RuntimeEnvironment.GetRuntimeDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var directory in directories)
            if (!string.IsNullOrWhiteSpace(directory))
                yield return Path.Combine(directory, assemblyName);
    }

    private static IEnumerable<string> EnumerateTrustedPlatformAssemblyCandidates(string assemblyName) {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies)) yield break;

        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            if (string.Equals(Path.GetFileName(path), assemblyName, StringComparison.OrdinalIgnoreCase))
                yield return path;
    }

    private static IEnumerable<string> EnumerateSharedRuntimeCandidates(string framework, string assemblyName) {
        var major = ParseMajorFrameworkVersion(framework);
        foreach (var runtimeRoot in EnumerateSharedRuntimeRoots()) {
            if (!Directory.Exists(runtimeRoot)) continue;

            var versionDirectory = Directory
                .EnumerateDirectories(runtimeRoot)
                .Select(path => (Path: path, Version: TryParseVersion(Path.GetFileName(path))))
                .Where(item => item.Version is not null && item.Version.Major == major)
                .OrderByDescending(item => item.Version)
                .Select(item => item.Path)
                .FirstOrDefault();
            if (versionDirectory is not null) yield return Path.Combine(versionDirectory, assemblyName);
        }
    }

    private static IEnumerable<string> EnumerateSharedRuntimeRoots() {
        var sharedDirectories = new[] {
            CombineIfRooted(Environment.GetEnvironmentVariable("DOTNET_ROOT"), "shared"),
            CombineIfRooted(Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)"), "shared"),
            CombineIfRooted(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared"),
            CombineIfRooted(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "shared"),
            Path.Combine(Path.DirectorySeparatorChar.ToString(), "usr", "share", "dotnet", "shared"),
            Path.Combine(Path.DirectorySeparatorChar.ToString(), "usr", "local", "share", "dotnet", "shared")
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sharedDirectory in sharedDirectories) {
            if (string.IsNullOrWhiteSpace(sharedDirectory) || !Directory.Exists(sharedDirectory)) continue;

            foreach (var runtimeRoot in Directory.EnumerateDirectories(sharedDirectory))
                if (seen.Add(runtimeRoot))
                    yield return runtimeRoot;
        }
    }

    private static string? CombineIfRooted(string? root, params string[] segments) {
        if (string.IsNullOrWhiteSpace(root)) return null;

        var combined = root;
        foreach (var segment in segments) combined = Path.Combine(combined, segment);

        return combined;
    }
}
