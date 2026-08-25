namespace SharpProof.Ir;
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly TimeSpan StagingLifetime = TimeSpan.FromHours(1);

    internal static string PrepareStaged(string path)
    {
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException("The output path has no directory.");
        Directory.CreateDirectory(directory);
        SweepStaged(directory);
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
        if (content == null)
        {
            throw new ArgumentNullException(nameof(content));
        }
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
        Publish(temporary, Path.GetFullPath(destination));
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

    internal static void SweepStaged(
        string directory,
        TimeSpan? maximumAge = null)
    {
        directory = Path.GetFullPath(
            ArgumentNullGuard.NotNull(directory, nameof(directory)));
        var age = maximumAge ?? StagingLifetime;
        if (age < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        DateTime cutoff = DateTime.UtcNow - age;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, ".sharpproof-*.tmp"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
        }
    }

    internal static void WriteUtf8(string path, string content)
    {
        var (destination, temporary) = Prepare(path);
        try
        {
            WriteStagedBytes(temporary, Utf8.GetBytes(content));
            Publish(temporary, destination);
        }
        finally
        {
            TryDeleteStaged(temporary);
        }
    }
    internal static Task WriteUtf8Async(
        string path, string content, CancellationToken cancellationToken = default)
    {
        return WriteBytesAsync(path, Utf8.GetBytes(content), cancellationToken);
    }

    internal static async Task WriteBytesAsync(
        string path, byte[] content, CancellationToken cancellationToken = default)
    {
        var (destination, temporary) = Prepare(path);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(content, 0, content.Length, cancellationToken)
                    .ConfigureAwait(false);
#pragma warning disable CA1849 // Flush(true) is required for atomic durability.
                stream.Flush(true);
#pragma warning restore CA1849
            }

            Publish(temporary, destination);
        }
        finally
        {
            TryDeleteStaged(temporary);
        }
    }
    private static (string Destination, string Temporary) Prepare(string path)
    {
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException("The output path has no directory.");
        Directory.CreateDirectory(directory);
        SweepStaged(directory);
        return (destination, Path.Combine(
            directory,
            ".sharpproof-" + Guid.NewGuid().ToString("N") + ".tmp"));
    }
    private static void Publish(string temporary, string destination)
    {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
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
            catch (IOException exception) when (File.Exists(temporary))
            {
                // A concurrent publisher can change the destination between
                // the existence check and the rename. The staged file remains
                // valid, so retry using the new destination state.
                lastFailure = exception;
            }
        }

        throw lastFailure ?? new IOException(
            "The staged file disappeared before publication completed.");
    }
}
