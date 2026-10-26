using System.Collections.Immutable;
using SharpProof.Ir;

namespace SharpProof.Worker;

// Cache identity of the worker runtime: the SharpProof and Z3 managed
// assemblies shipped beside the worker plus the native Z3 library digest.
internal static class WorkerBinaryIdentity
{
    internal static string ComputeSha256(
        string workerPath,
        string? z3LibrarySha256 = null)
    {
        workerPath = RepositoryParameterGuard.NotNull(workerPath, nameof(workerPath));
        var path = Path.GetFullPath(workerPath);
        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "The managed worker binary must be an existing .dll.",
                path);
        }

        var directory = Path.GetDirectoryName(path) ??
            throw new ArgumentException(
                "The managed worker binary path must include a directory.",
                nameof(workerPath));
        var files = Directory.EnumerateFiles(directory, "SharpProof.*.dll")
            .Append(Path.Combine(directory, "Microsoft.Z3.dll"))
            .Where(File.Exists)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static file => file is null ? throw new System.ArgumentNullException(nameof(file)) : (Path.GetFileName(file)), StringComparer.Ordinal);
        using var hash = new CanonicalHashWriter();
        foreach (var file in files)
        {
            hash.Add(Path.GetFileName(file)).Add(File.ReadAllBytes(file).ToImmutableArray());
        }
        return hash.Add("z3.native").Add(z3LibrarySha256 ?? string.Empty).Finish();
    }
}
