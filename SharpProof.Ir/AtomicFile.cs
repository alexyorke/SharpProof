using System.Reflection;

namespace SharpProof.Ir;
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly MethodInfo? GetUnixFileMode = FindUnixModeMethod(
        "GetUnixFileMode", parameterCount: 1);
    private static readonly MethodInfo? SetUnixFileMode = FindUnixModeMethod(
        "SetUnixFileMode", parameterCount: 2);

    private static MethodInfo? FindUnixModeMethod(string name, int parameterCount)
    {
        return typeof(File).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == name &&
                method.GetParameters().Length == parameterCount);
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
        if (File.Exists(destination))
        {
            PreserveDestinationMode(temporary, destination);
            File.Replace(temporary, destination, null);
        }
        else
        {
            File.Move(temporary, destination);
        }
    }

    private static void PreserveDestinationMode(string temporary, string destination)
    {
        // SharpProof.Ir targets netstandard2.0, while the Unix mode APIs are
        // available only on newer runtimes. Invoke them when present so the
        // replacement inode inherits the administrator-selected mode.
        if (GetUnixFileMode == null || SetUnixFileMode == null)
        {
            return;
        }

        try
        {
            var mode = GetUnixFileMode.Invoke(null, new object[] { destination });
            SetUnixFileMode.Invoke(null, new[] { temporary, mode! });
        }
        catch (TargetInvocationException exception) when (
            exception.InnerException is PlatformNotSupportedException ||
            exception.InnerException is IOException ||
            exception.InnerException is UnauthorizedAccessException)
        {
            // Non-Unix runtimes and filesystems without mode support retain
            // their platform-native replacement behavior.
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
        var destination = Path.GetFullPath(path);
        var temporary = PrepareStaged(destination);
        try
        {
            File.WriteAllText(temporary, content, Utf8);
            PublishStaged(temporary, destination);
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
        var destination = Path.GetFullPath(path);
        var temporary = PrepareStaged(destination);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(content, 0, content.Length, cancellationToken)
                    .ConfigureAwait(false);
            }

            PublishStaged(temporary, destination);
        }
        finally
        {
            TryDeleteStaged(temporary);
        }
    }
}
