using Microsoft.CodeAnalysis;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerSourceLocationProjection
{
    internal static WorkerSourceLocation Create(Location location)
    {
        if (!location.IsInSource)
        {
            return new WorkerSourceLocation();
        }

        var mapped = location.GetMappedLineSpan();
        return new WorkerSourceLocation
        {
            Path = MappedPath(location.SourceTree, mapped),
            Start = location.SourceSpan.Start,
            Length = location.SourceSpan.Length,
            Line = mapped.StartLinePosition.Line + 1,
            Column = mapped.StartLinePosition.Character + 1
        };
    }

    internal static string MappedPath(
        SyntaxTree? tree, FileLinePositionSpan mapped)
    {
        var path = mapped.Path;
        if (string.IsNullOrEmpty(path))
        {
            path = tree?.FilePath ?? string.Empty;
        }
        if (string.IsNullOrEmpty(path))
        {
            return "<compiler-generated>";
        }

        var normalizedPath = path.Replace(
            '\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedPath) ||
            IsWindowsDriveAbsolute(path))
        {
            return IsWindowsDriveAbsolute(path) &&
                !Path.IsPathRooted(normalizedPath)
                ? path.Replace('\\', '/')
                : Path.GetFullPath(normalizedPath);
        }

        var sourcePath = tree?.FilePath ?? string.Empty;
        var sourceDirectory = string.IsNullOrEmpty(sourcePath)
            ? null
            : Path.GetDirectoryName(sourcePath.Replace(
                '\\', Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(sourceDirectory)
            ? normalizedPath
            : Path.GetFullPath(Path.Combine(sourceDirectory, normalizedPath));
    }

    private static bool IsWindowsDriveAbsolute(string path)
    {
        return path.Length >= 3 &&
            (path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') &&
            path[1] == ':' &&
            path[2] is '/' or '\\';
    }
}
