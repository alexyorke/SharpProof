namespace SharpProof.Worker;

internal sealed class VerificationCache(
    string directory,
    long maximumBytes) {
    private readonly string _directory =
        Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
    private readonly long _maximumBytes = maximumBytes > 0
        ? maximumBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumBytes));
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async Task<WorkerVerifyResponse?> TryReadAsync(
        string inputHash,
        CancellationToken cancellationToken) {
        var path = GetPath(inputHash);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (!File.Exists(path)) return null;
            string json;
            try {
                json = await File.ReadAllTextAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                var envelope = JsonSerializer.Deserialize<CacheEnvelope>(
                    json,
                    WorkerProtocolJson.Options);
                if (envelope == null ||
                    envelope.SchemaVersion !=
                    WorkerCacheVersions.Current ||
                    !string.Equals(envelope.InputHash, inputHash, StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(envelope.Payload) ||
                    !string.Equals(
                        envelope.PayloadHash,
                        HashText(envelope.Payload),
                        StringComparison.Ordinal)) {
                    return null;
                }
                var response = WorkerProtocolJson.DeserializeResponse(
                    envelope.Payload);
                if (response == null ||
                    !CacheableWorkerResponse.TryCreate(
                        response,
                        inputHash,
                        out _))
                    return null;
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return response;
            }
            catch (Exception exception) when (exception is
                JsonException or
                IOException or
                UnauthorizedAccessException) {
                return null;
            }
        }
        finally {
            _gate.Release();
        }
    }

    internal async Task TryWriteAsync(
        CacheableWorkerResponse response,
        CancellationToken cancellationToken) {
        if (response == null)
            throw new ArgumentNullException(nameof(response));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            Directory.CreateDirectory(_directory);
            var envelope = new CacheEnvelope {
                SchemaVersion = WorkerCacheVersions.Current,
                InputHash = response.InputHash,
                Payload = response.Payload,
                PayloadHash = HashText(response.Payload)
            };
            var json = JsonSerializer.Serialize(
                envelope,
                WorkerProtocolJson.Options);
            var path = GetPath(response.InputHash);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                await File.WriteAllTextAsync(
                    temporary,
                    json,
                    new UTF8Encoding(false),
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
            }
            finally {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            Evict();
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException) {
            // Cache failures never change semantic verifier outcomes.
        }
        finally {
            _gate.Release();
        }
    }

    private void Evict() {
        var files = new DirectoryInfo(_directory)
            .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file.LastWriteTimeUtc)
            .ThenBy(static file => file.Name, StringComparer.Ordinal)
            .ToArray();
        var total = files.Sum(static file => file.Length);
        foreach (var file in files) {
            if (total <= _maximumBytes) break;
            var length = file.Length;
            try {
                file.Delete();
                total -= length;
            }
            catch (IOException) {
            }
            catch (UnauthorizedAccessException) {
            }
        }
    }

    private string GetPath(string inputHash) {
        if (inputHash.Length != 64 ||
            inputHash.Any(static character =>
                !Uri.IsHexDigit(character)))
            throw new ArgumentException("A SHA-256 input hash is required.", nameof(inputHash));
        return Path.Combine(_directory, inputHash.ToLowerInvariant() + ".json");
    }

    private static string HashText(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class CacheEnvelope {
        public int SchemaVersion { get; set; }
        public string InputHash { get; set; } = string.Empty;
        public string PayloadHash { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }
}
