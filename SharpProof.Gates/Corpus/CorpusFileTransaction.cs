using System.Text;
using System.Text.Json;

namespace SharpProof.Gates.Corpus;

internal sealed record CorpusFileUpdate(string Path, string Content);

internal static class CorpusFileTransaction
{
    private const int SchemaVersion = 1;
    private const string MarkerName =
        ".sharpproof-corpus-transaction.json";
    private static readonly UTF8Encoding Utf8 = new(false);

    internal static async Task WriteAllAsync(
        string transactionRoot,
        IReadOnlyList<CorpusFileUpdate> updates,
        CancellationToken cancellationToken,
        Action<int>? beforePublish = null)
    {
        transactionRoot = Path.GetFullPath(transactionRoot);
        Directory.CreateDirectory(transactionRoot);
        Recover(transactionRoot);
        if (updates.Count == 0)
        {
            return;
        }

        var destinations = new string[updates.Count];
        var seenDestinations = new HashSet<string>(StringComparer.Ordinal);
        var hasDuplicateDestination = false;
        for (var index = 0; index < updates.Count; index++)
        {
            var destination = Path.GetFullPath(updates[index].Path);
            destinations[index] = destination;
            OpenSourceCorpusCatalog.EnsureContained(
                transactionRoot,
                destination);
            if (!seenDestinations.Add(destination))
            {
                hasDuplicateDestination = true;
            }
        }
        if (hasDuplicateDestination)
        {
            throw new ArgumentException(
                "Corpus transaction destinations must be unique.",
                nameof(updates));
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var entries = new List<TransactionEntry>(updates.Count);
        var markerPath = Path.Combine(transactionRoot, MarkerName);
        var markerPublished = false;
        try
        {
            for (var index = 0; index < updates.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = destinations[index];
                var directory = Path.GetDirectoryName(destination) ??
                    throw new InvalidOperationException(
                        "A corpus output has no directory.");
                Directory.CreateDirectory(directory);
                var stem = ".sharpproof-corpus-" + transactionId + "-" +
                    index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                var staged = Path.Combine(directory, stem + ".new");
                var backup = Path.Combine(directory, stem + ".old");
                var existed = File.Exists(destination);
                if (existed)
                {
                    await CopyDurablyAsync(
                            destination,
                            backup,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                await WriteDurablyAsync(
                        staged,
                        Utf8.GetBytes(updates[index].Content),
                        cancellationToken)
                    .ConfigureAwait(false);
                entries.Add(new TransactionEntry(
                    destination,
                    staged,
                    backup,
                    existed));
            }

            cancellationToken.ThrowIfCancellationRequested();
            await WriteMarkerAsync(
                    markerPath,
                    new TransactionMarker(SchemaVersion, entries.ToArray()),
                    cancellationToken)
                .ConfigureAwait(false);
            markerPublished = true;
            for (var index = 0; index < entries.Count; index++)
            {
                beforePublish?.Invoke(index);
                File.Move(
                    entries[index].StagedPath,
                    entries[index].DestinationPath,
                    overwrite: true);
            }

            File.Delete(markerPath);
            markerPublished = false;
        }
        catch
        {
            if (markerPublished)
            {
                Restore(entries);
                File.Delete(markerPath);
                markerPublished = false;
            }
            throw;
        }
        finally
        {
            if (!markerPublished)
            {
                Cleanup(entries);
            }
        }
    }

    internal static void Recover(string transactionRoot)
    {
        transactionRoot = Path.GetFullPath(transactionRoot);
        var markerPath = Path.Combine(transactionRoot, MarkerName);
        if (!File.Exists(markerPath))
        {
            return;
        }

        var marker = JsonSerializer.Deserialize<TransactionMarker>(
            File.ReadAllText(markerPath, Utf8)) ??
            throw new InvalidDataException(
                "The corpus transaction marker is empty.");
        if (marker.SchemaVersion != SchemaVersion ||
            marker.Entries is not { Length: > 0 })
        {
            throw new InvalidDataException(
                "The corpus transaction marker is invalid.");
        }

        Restore(marker.Entries);
        File.Delete(markerPath);
        Cleanup(marker.Entries);
    }

    private static async Task WriteMarkerAsync(
        string markerPath,
        TransactionMarker marker,
        CancellationToken cancellationToken)
    {
        var temporary = markerPath + "." +
            Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await WriteDurablyAsync(
                    temporary,
                    JsonSerializer.SerializeToUtf8Bytes(marker),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, markerPath, overwrite: false);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task CopyDurablyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken)
            .ConfigureAwait(false);
        await WriteDurablyAsync(destination, bytes, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteDurablyAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        using var stream = OpenDurableFile(path, asynchronous: true);
        await stream.WriteAsync(content, cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static FileStream OpenDurableFile(string path, bool asynchronous)
    {
        var options = FileOptions.WriteThrough;
        if (asynchronous)
        {
            options |= FileOptions.Asynchronous;
        }
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            options);
    }

    private static void Restore(IEnumerable<TransactionEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.DestinationExisted)
            {
                if (!File.Exists(entry.BackupPath))
                {
                    throw new InvalidDataException(
                        "A corpus transaction backup is unavailable.");
                }
                var restore = entry.DestinationPath + "." +
                    Guid.NewGuid().ToString("N") + ".restore";
                try
                {
                    var content = File.ReadAllBytes(entry.BackupPath);
                    using (var stream = OpenDurableFile(
                        restore,
                        asynchronous: false))
                    {
                        stream.Write(content, 0, content.Length);
                        stream.Flush(flushToDisk: true);
                    }
                    File.Move(
                        restore,
                        entry.DestinationPath,
                        overwrite: true);
                }
                finally
                {
                    TryDelete(restore);
                }
            }
            else
            {
                TryDelete(entry.DestinationPath);
            }
        }
    }

    private static void Cleanup(IEnumerable<TransactionEntry> entries)
    {
        foreach (var entry in entries)
        {
            TryDelete(entry.StagedPath);
            TryDelete(entry.BackupPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record TransactionMarker(
        int SchemaVersion,
        TransactionEntry[] Entries);

    private sealed record TransactionEntry(
        string DestinationPath,
        string StagedPath,
        string BackupPath,
        bool DestinationExisted);
}
