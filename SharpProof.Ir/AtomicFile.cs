namespace SharpProof.Ir;
internal static class AtomicFile
{
    private const int MaxPublicationAttempts = 8;
    private static readonly UTF8Encoding Utf8 = new(false);

    private sealed class StagedFile(string temporary) : IDisposable
    {
        internal FileInfo Temporary { get; } = new(temporary);

        public void Dispose()
        {
            TryDeleteStaged(Temporary.FullName);
        }
    }

    internal static string PrepareStaged(string path)
    {
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException("The output path has no directory.");
        Directory.CreateDirectory(directory);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var temporary = Path.Combine(
                directory,
                ".sharpproof-" + Guid.NewGuid().ToString("N") + ".tmp");
            if (!File.Exists(temporary) && !Directory.Exists(temporary))
            {
                return temporary;
            }
        }

        throw new IOException("Could not allocate a SharpProof staging path.");
    }

    internal static void WriteStagedBytes(string temporary, byte[] content)
    {
        ArgumentNullGuard.NotNull(content, nameof(content));
        using var stream = new FileStream(
            temporary,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            useAsync: false);
        stream.Write(content, 0, content.Length);
        stream.Flush(true);
    }

    internal static void PublishStaged(string temporary, string destination)
    {
        for (var attempt = 0; attempt < MaxPublicationAttempts; attempt++)
        {
            try
            {
                // The existence check is only a hint.  Either operation can
                // race with another publisher, so retry the other operation
                // after a transient IOException.
                if (File.Exists(destination))
                {
                    File.Replace(temporary, destination, null);
                }
                else
                {
                    File.Move(temporary, destination);
                }

                return;
            }
            catch (IOException) when (attempt + 1 < MaxPublicationAttempts)
            {
                // A destination can appear between the check and Move, or
                // disappear between the check and Replace.  Re-evaluate the
                // destination on the next attempt.
            }
        }
    }

    internal static void TryDeleteStaged(string temporary)
    {
        try
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static void WriteUtf8(string path, string content)
    {
        using var staged = new StagedFile(PrepareStaged(path));
        WriteStagedBytes(staged.Temporary.FullName, Utf8.GetBytes(content));
        PublishStaged(staged.Temporary.FullName, path);
    }

    internal static Task WriteUtf8Async(
        string path, string content, CancellationToken cancellationToken = default)
    {
        return WriteBytesAsync(path, Utf8.GetBytes(content), cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849",
        Justification = "Flush(true) is required before publishing staged output.")]
    internal static async Task WriteBytesAsync(
        string path, byte[] content, CancellationToken cancellationToken = default)
    {
        using var staged = new StagedFile(PrepareStaged(path));
        using (var stream = new FileStream(staged.Temporary.FullName, FileMode.CreateNew,
                   FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await stream.WriteAsync(content, 0, content.Length, cancellationToken)
                .ConfigureAwait(false);
            stream.Flush(true);
        }

        PublishStaged(staged.Temporary.FullName, path);
    }
}
