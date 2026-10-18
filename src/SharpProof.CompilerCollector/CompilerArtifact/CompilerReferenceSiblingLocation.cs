namespace SharpProof.CompilerArtifact;

#pragma warning disable RS1035 // Build-only compiler evidence resolves final reference paths.

// Resolves ordinary location evidence only when it describes a safe sibling module.
internal sealed record CompilerReferenceSiblingLocation(string Name, string? Directory, string CandidatePath)
{
    public string Name { get; init; } = Name ?? throw new System.ArgumentNullException(nameof(Name));
    public string CandidatePath { get; init; } = CandidatePath ?? throw new System.ArgumentNullException(nameof(CandidatePath));

    internal static string Resolve(string manifestPath, string name)
    {
        RequireSafeName(name);
        var directory = RequireDirectory(Path.GetDirectoryName(Path.GetFullPath(manifestPath)));
        var path = Path.GetFullPath(Path.Combine(directory, name));
        return new CompilerReferenceSiblingLocation(name, directory, path).Resolve();
    }

    internal string Resolve()
    {
        RequireSafeName(Name);
        var directory = RequireDirectory(Directory);
        if (!string.Equals(Path.GetDirectoryName(CandidatePath), directory, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A linked compiler module must be a safe sibling.");
        }
        return CandidatePath;
    }

    private static void RequireSafeName(string name)
    {
        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
            !HasSafeModuleFileName(name))
        {
            throw new InvalidDataException("A linked compiler module must be a safe sibling.");
        }
    }

    private static string RequireDirectory(string? directory)
    {
        return directory ?? throw new InvalidDataException("A compiler reference directory is unavailable.");
    }

    private static bool HasSafeModuleFileName(string name)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        return !string.IsNullOrEmpty(name) && name is not "." and not ".." &&
            name.IndexOf('\0') < 0 && name.IndexOf('/') < 0;
    }
}
