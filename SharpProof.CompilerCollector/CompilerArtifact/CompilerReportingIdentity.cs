using System.Globalization;
using System.Text.Json;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerReportingIdentity
{
    private const string SyntaxTreeSnapshotDomain =
        "SharpProof.CompilerSyntaxTreeSnapshot";
    private const int SyntaxTreeSnapshotVersion = 2;
    private const string SourceLineMapDomain =
        "SharpProof.CompilerSourceLineMap";
    private const int SourceLineMapVersion = 2;

    internal static string ComputeLineMapSha256(
        CompilerSourceLineMapEntry[] entries)
    {
        entries = ArgumentNullGuard.NotNull(entries, nameof(entries));

        using var hash = new CanonicalHashWriter();
        hash.Add(SourceLineMapDomain)
            .Add(SourceLineMapVersion)
            .Add(JsonSerializer.SerializeToUtf8Bytes(
                entries,
                WorkerProtocolJson.Options));
        return hash.Finish();
    }

    internal static string ComputeSyntaxTreeSnapshotSha256(
        CompilerSyntaxTreeSnapshot snapshot)
    {
        snapshot = ArgumentNullGuard.NotNull(snapshot, nameof(snapshot));

        using var hash = new CanonicalHashWriter();
        hash.Add(SyntaxTreeSnapshotDomain)
            .Add(SyntaxTreeSnapshotVersion)
            .Add(JsonSerializer.SerializeToUtf8Bytes(
                snapshot,
                WorkerProtocolJson.Options));
        return hash.Finish();
    }

}
