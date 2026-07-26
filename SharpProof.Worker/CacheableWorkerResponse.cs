namespace SharpProof.Worker;

internal sealed class CacheableWorkerResponse {
    private CacheableWorkerResponse(
        string inputHash,
        string payload) {
        InputHash = inputHash;
        Payload = payload;
    }

    internal string InputHash { get; }
    internal string Payload { get; }

    internal static bool TryCreate(
        WorkerVerifyResponse? response,
        string expectedInputHash,
        [NotNullWhen(true)]
        out CacheableWorkerResponse? cacheable) {
        cacheable = null;
        if (response == null ||
            !IsSha256(expectedInputHash) ||
            !string.Equals(
                response.ProtocolVersion,
                WorkerProtocolVersions.Current,
                StringComparison.Ordinal) ||
            !string.Equals(
                response.InputHash,
                expectedInputHash,
                StringComparison.Ordinal) ||
            response.Errors is not { Length: 0 } ||
            response.Records is not { Length: > 0 } ||
            response.Records.Any(static record => !IsValidRecord(record))) {
            return false;
        }

        var payload = WorkerProtocolJson.SerializeResponse(response);
        cacheable = new CacheableWorkerResponse(
            response.InputHash,
            payload);
        return true;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(static character => Uri.IsHexDigit(character));

    private static bool IsValidRecord(WorkerVerificationRecord? record) =>
        record != null &&
        !string.IsNullOrEmpty(record.CallableId) &&
        record.ContractOrdinal >= 0 &&
        record.SourcePath != null &&
        record.SourceStart >= 0 &&
        record.Status is
            WorkerVerificationStatus.Proven or
            WorkerVerificationStatus.Refuted &&
        record.Reason == WorkerVerificationReason.None &&
        record.ProofCore is not null &&
        record.ProofCore.All(static value => value != null) &&
        record.Model is not null &&
        record.Model.All(static value =>
            value != null &&
            value.Variable != null &&
            value.Kind != null &&
            value.Value != null);
}
