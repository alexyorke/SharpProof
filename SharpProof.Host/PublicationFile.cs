using System.Text;

namespace SharpProof.Host;

internal static class PublicationFile
{
    internal static void Copy(string source, string destination, CancellationToken cancellationToken)
    {
        using var input = File.OpenRead(source);
        Write(destination, stream =>
        {
            var buffer = new byte[81920];
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Write(buffer, 0, count);
            }
        }, cancellationToken);
    }

    internal static void WriteUtf8(string destination, string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Write(destination, stream => stream.Write(bytes), cancellationToken);
    }

    private static void Write(string destination, Action<FileStream> write, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".sharpproof-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                cancellationToken.ThrowIfCancellationRequested();
                write(output);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
