using System.Text.Json;

namespace SharpProof.Host;

/// <summary>
/// Stores the last successful publication topology at a stable, project-owned
/// path so that a later clean can find outputs whose command-line paths have
/// changed.  The publication members remain authenticated by their individual
/// LinuxPathIdentity ownership markers; this file is only a discovery index.
/// </summary>
internal static class PublicationTopologyStore
{
    private const string Schema = "SharpProof.PublicationTopology";
    private const int Version = 1;

    private sealed class Document
    {
        public string? SchemaName
        {
            get;
            set;
        }

        public int VersionNumber
        {
            get;
            set;
        }

        public string[]? Paths
        {
            get;
            set;
        }
    }

    internal static string[]? Read(string metadataPath)
    {
        var path = NormalizeMetadataPath(metadataPath);
        if (!File.Exists(path))
        {
            return null;
        }

        if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
        {
            throw new InvalidDataException(
                "SharpProof publication topology metadata must be a regular file.");
        }

        Document document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path)) ??
                throw new InvalidDataException(
                    "SharpProof publication topology metadata is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "SharpProof publication topology metadata is malformed.",
                exception);
        }

        if (!string.Equals(document.SchemaName, Schema, StringComparison.Ordinal) ||
            document.VersionNumber != Version ||
            document.Paths is not { Length: > 0 })
        {
            throw new InvalidDataException(
                "SharpProof publication topology metadata has an unsupported schema.");
        }

        return NormalizePublicationPaths(document.Paths);
    }

    internal static void Write(
        string metadataPath,
        IEnumerable<string> publicationPaths)
    {
        ArgumentNullException.ThrowIfNull(publicationPaths);
        var path = NormalizeMetadataPath(metadataPath);
        var paths = NormalizePublicationPaths(publicationPaths);
        var document = new Document
        {
            SchemaName = Schema,
            VersionNumber = Version,
            Paths = paths
        };
        var json = JsonSerializer.Serialize(document);
        var directory = Path.GetDirectoryName(path) ??
            throw new InvalidDataException(
                "SharpProof publication topology metadata has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       options: FileOptions.SequentialScan))
            using (var writer = new StreamWriter(
                       stream,
                       new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Write('\n');
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            LinuxPathIdentity.SyncDirectory(directory);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    internal static void Delete(string metadataPath)
    {
        var path = NormalizeMetadataPath(metadataPath);
        if (File.Exists(path))
        {
            File.Delete(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                LinuxPathIdentity.SyncDirectory(directory);
            }
        }
    }

    private static string NormalizeMetadataPath(string metadataPath)
    {
        if (string.IsNullOrWhiteSpace(metadataPath))
        {
            throw new ArgumentException(
                "SharpProof publication topology metadata path is required.",
                nameof(metadataPath));
        }

        var path = Path.GetFullPath(metadataPath);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "SharpProof publication topology metadata path must be absolute.",
                nameof(metadataPath));
        }
        return path;
    }

    private static string[] NormalizePublicationPaths(
        IEnumerable<string> publicationPaths)
    {
        var paths = publicationPaths
            .Select(path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new InvalidDataException(
                        "SharpProof publication topology contains an empty path.");
                }
                var fullPath = Path.GetFullPath(path);
                if (!Path.IsPathFullyQualified(fullPath))
                {
                    throw new InvalidDataException(
                        "SharpProof publication topology contains a relative path.");
                }
                return fullPath;
            })
            .ToArray();
        if (paths.Length == 0 ||
            paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
        {
            throw new InvalidDataException(
                "SharpProof publication topology must contain distinct paths.");
        }
        return paths;
    }
}
