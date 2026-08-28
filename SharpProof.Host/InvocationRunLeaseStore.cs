using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace SharpProof.Host;

/// <summary>
/// Records the owning MSBuild process for a verifier invocation. A lease is
/// only reclaimed when the recorded process is gone or its start time no
/// longer matches, which prevents a reused PID from deleting a live run.
/// </summary>
internal static class InvocationRunLeaseStore
{
    internal const string LeaseFileName = ".owner.json";
    internal const int MaximumDirectoriesPerSweep = 128;

    private const string Schema = "SharpProof.InvocationRunLease";
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

        public int ProcessId
        {
            get;
            set;
        }

        public long ProcessStartUtcTicks
        {
            get;
            set;
        }

        public long CreatedUtcTicks
        {
            get;
            set;
        }
    }

    internal static void Write(string invocationDirectory)
    {
        using var process = Process.GetCurrentProcess();
        WriteCore(
            invocationDirectory,
            process.Id,
            process.StartTime.ToUniversalTime(),
            DateTimeOffset.UtcNow);
    }

    internal static void WriteForTest(
        string invocationDirectory,
        int processId,
        DateTimeOffset processStartUtc)
    {
        WriteCore(
            invocationDirectory,
            processId,
            processStartUtc,
            DateTimeOffset.UtcNow);
    }

    internal static int Reclaim(
        string runsDirectory,
        string? currentInvocationId)
    {
        var root = NormalizeDirectory(runsDirectory);
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var reclaimed = 0;
        foreach (var child in Directory.EnumerateDirectories(root)
                     .OrderBy(static path => path, StringComparer.Ordinal)
                     .Take(MaximumDirectoriesPerSweep))
        {
            var invocationId = Path.GetFileName(child);
            if (!IsSafeInvocationId(invocationId) ||
                string.Equals(
                    invocationId,
                    currentInvocationId,
                    StringComparison.Ordinal) ||
                IsReparsePoint(child))
            {
                continue;
            }

            if (!TryRead(child, out var lease) || IsOwnerAlive(lease))
            {
                continue;
            }

            try
            {
                Directory.Delete(child, recursive: true);
                reclaimed++;
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException)
            {
                // Reclamation is best effort. A concurrent cleanup or a
                // transient filesystem failure must not affect the build.
            }
        }

        if (reclaimed > 0)
        {
            LinuxPathIdentity.SyncDirectory(root);
        }

        return reclaimed;
    }

    private static void WriteCore(
        string invocationDirectory,
        int processId,
        DateTimeOffset processStartUtc,
        DateTimeOffset createdUtc)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        var directory = NormalizeDirectory(invocationDirectory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }

        var document = new Document
        {
            SchemaName = Schema,
            VersionNumber = Version,
            ProcessId = processId,
            ProcessStartUtcTicks = processStartUtc.UtcTicks,
            CreatedUtcTicks = createdUtc.UtcTicks
        };
        var path = Path.Combine(directory, LeaseFileName);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(document);
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
                       new System.Text.UTF8Encoding(
                           encoderShouldEmitUTF8Identifier: false)))
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

    private static bool TryRead(
        string invocationDirectory,
        out Document lease)
    {
        lease = null!;
        var path = Path.Combine(invocationDirectory, LeaseFileName);
        if (!File.Exists(path) || IsReparsePoint(path))
        {
            return false;
        }

        try
        {
            lease = JsonSerializer.Deserialize<Document>(
                File.ReadAllText(path))!;
            return lease != null &&
                string.Equals(
                    lease.SchemaName,
                    Schema,
                    StringComparison.Ordinal) &&
                lease.VersionNumber == Version &&
                lease.ProcessId > 0 &&
                lease.ProcessStartUtcTicks > 0 &&
                lease.CreatedUtcTicks > 0;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or JsonException or
            NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsOwnerAlive(Document lease)
    {
        try
        {
            using var process = Process.GetProcessById(lease.ProcessId);
            var actualStart = process.StartTime.ToUniversalTime();
            var expectedStart = new DateTimeOffset(
                new DateTime(lease.ProcessStartUtcTicks, DateTimeKind.Utc));
            return Math.Abs((actualStart - expectedStart).TotalSeconds) <= 5;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException)
        {
            return false;
        }
        catch (Exception exception) when (exception is
            Win32Exception or UnauthorizedAccessException or
            NotSupportedException)
        {
            // An inability to authenticate liveness is not proof of death.
            return true;
        }
    }

    private static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Invocation run directory is required.",
                nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(fullPath))
        {
            throw new ArgumentException(
                "Invocation run directory must be absolute.",
                nameof(path));
        }

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static bool IsSafeInvocationId(string? value)
    {
        return value is { Length: 32 } &&
            value.All(static character =>
                character is >= '0' and <= '9' or
                >= 'a' and <= 'f');
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
