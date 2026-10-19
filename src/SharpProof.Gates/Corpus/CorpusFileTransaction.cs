using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace SharpProof.Gates.Corpus;

internal sealed record CorpusFileUpdate(string Path, string Content)
{
    public string Path { get; init; } = Path ?? throw new System.ArgumentNullException(nameof(Path));
    public string Content { get; init; } = Content ?? throw new System.ArgumentNullException(nameof(Content));
}

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
        ArgumentNullException.ThrowIfNull(updates);
        var capturedUpdates = updates.ToImmutableArray();
        transactionRoot = Path.GetFullPath(transactionRoot);
        Directory.CreateDirectory(transactionRoot);
        Recover(transactionRoot);
        if (capturedUpdates.Length == 0)
        {
            return;
        }

        var destinationsBuilder = ImmutableArray.CreateBuilder<string>(capturedUpdates.Length);
        var seenDestinations = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var hasDuplicateDestination = false;
        for (var index = 0; index < capturedUpdates.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(capturedUpdates[index], nameof(updates));
            var destination = Path.GetFullPath(capturedUpdates[index].Path);
            destinationsBuilder.Add(destination);
            OpenSourceCorpusCatalog.EnsureContained(
                transactionRoot,
                destination);
            var duplicateDestination = seenDestinations.Contains(destination);
            seenDestinations.Add(destination);
            if (duplicateDestination)
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

        var destinations = destinationsBuilder.ToImmutable();
        var transactionId = Guid.NewGuid().ToString("N");
        var entriesBuilder = ImmutableArray.CreateBuilder<TransactionEntry>(capturedUpdates.Length);
        ImmutableArray<TransactionEntry> entries = default;
        var markerPath = Path.Combine(transactionRoot, MarkerName);
        var markerPublished = false;
        try
        {
            for (var index = 0; index < capturedUpdates.Length; index++)
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
                entriesBuilder.Add(new TransactionEntry(destination, staged, backup, existed));
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
                        Utf8.GetBytes(capturedUpdates[index].Content),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            entries = entriesBuilder.ToImmutable();
            await WriteMarkerAsync(
                    markerPath,
                    new TransactionMarker(SchemaVersion, entries),
                    cancellationToken)
                .ConfigureAwait(false);
            markerPublished = true;
            for (var index = 0; index < entries.Length; index++)
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
                Cleanup(entries.IsDefault ? entriesBuilder.ToImmutable() : entries);
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
        OpenSourceCorpusCatalog.EnsureContained(transactionRoot, markerPath);

        var marker = JsonSerializer.Deserialize<TransactionMarker>(
            File.ReadAllText(markerPath, Utf8)) ??
            throw new InvalidDataException(
                "The corpus transaction marker is empty.");
        if (marker.SchemaVersion != SchemaVersion ||
            marker.Entries.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The corpus transaction marker is invalid.");
        }

        var paths = ImmutableHashSet.Create(StringComparer.Ordinal,
            OpenSourceCorpusCatalog.ResolvePath(transactionRoot),
            OpenSourceCorpusCatalog.ResolvePath(markerPath)).ToBuilder();
        foreach (var entry in marker.Entries)
        {
            if (entry == null)
            {
                throw new InvalidDataException("A corpus transaction entry is null.");
            }
            foreach (var path in ImmutableArray.Create(entry.DestinationPath, entry.StagedPath, entry.BackupPath))
            {
                if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
                {
                    throw new InvalidDataException("A corpus transaction path is invalid.");
                }
                OpenSourceCorpusCatalog.EnsureContained(transactionRoot, path);
                var resolvedPath = OpenSourceCorpusCatalog.ResolvePath(path);
                var duplicate = paths.Contains(resolvedPath);
                paths.Add(resolvedPath);
                if (duplicate || Directory.Exists(path))
                {
                    throw new InvalidDataException("Corpus transaction paths must name distinct files.");
                }
            }
            if (entry.DestinationExisted && !File.Exists(entry.BackupPath))
            {
                throw new InvalidDataException("A corpus transaction backup is unavailable.");
            }
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
        System.ArgumentNullException.ThrowIfNull(marker);
        System.ArgumentNullException.ThrowIfNull(markerPath);
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
        System.ArgumentNullException.ThrowIfNull(destination);
        System.ArgumentNullException.ThrowIfNull(source);
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
        System.ArgumentNullException.ThrowIfNull(content, nameof(content));
        System.ArgumentNullException.ThrowIfNull(path);
        using var stream = OpenDurableFile(path, asynchronous: true);
        await stream.WriteAsync(content, cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static FileStream OpenDurableFile(string path, bool asynchronous)
    {
        System.ArgumentNullException.ThrowIfNull(path);
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
        System.ArgumentNullException.ThrowIfNull(entries);
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
        System.ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            TryDelete(entry.StagedPath);
            TryDelete(entry.BackupPath);
        }
    }

    private static void TryDelete(string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
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
        ImmutableArray<TransactionEntry> Entries)
    {
        public ImmutableArray<TransactionEntry> Entries { get; init; } = !Entries.IsDefault ? Entries : throw new System.ArgumentNullException(nameof(Entries));
    }

    private sealed record TransactionEntry(
        string DestinationPath,
        string /* SPINT001: all values valid - raw journal paths are validated before mutation */ StagedPath,
        string BackupPath,
        [property: System.Text.Json.Serialization.JsonRequired] bool DestinationExisted)
    {
        public string DestinationPath { get; init; } = DestinationPath ?? throw new System.ArgumentNullException(nameof(DestinationPath));
        public string StagedPath { get; init; } = StagedPath;
        public string BackupPath { get; init; } = BackupPath ?? throw new System.ArgumentNullException(nameof(BackupPath));
    }
}
