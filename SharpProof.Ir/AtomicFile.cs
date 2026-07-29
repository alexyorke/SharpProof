namespace SharpProof.Ir;
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8 = new(false);
    internal static void WriteUtf8(string path, string content)
    {
        var (destination, temporary) = Prepare(path);
        try
        {
            File.WriteAllText(temporary, content, Utf8);
            Publish(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
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
            }

            Publish(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
    private static (string Destination, string Temporary) Prepare(string path)
    {
        var destination = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(destination) ??
            throw new InvalidOperationException("The output path has no directory.");
        Directory.CreateDirectory(directory);
        return (destination, destination + "." + Guid.NewGuid().ToString("N") + ".tmp");
    }
    private static void Publish(string temporary, string destination)
    {
        if (File.Exists(destination))
        {
            File.Replace(temporary, destination, null);
        }
        else
        {
            File.Move(temporary, destination);
        }
    }
}
