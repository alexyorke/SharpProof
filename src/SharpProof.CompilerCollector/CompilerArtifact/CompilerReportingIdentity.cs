using System.Text.Json;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static class CompilerReportingIdentity
{
    private const string SourceLineMapDomain =
        "SharpProof.CompilerSourceLineMap";
    private const int SourceLineMapVersion = 2;

    internal static string ComputeLineMapSha256(
        ImmutableArray<CompilerSourceLineMapEntry> entries)
    {
        if (entries.IsDefault)
        { throw new ArgumentNullException(nameof(entries)); }

        using var hash = new CanonicalHashWriter();
        hash.Add(SourceLineMapDomain)
            .Add(SourceLineMapVersion)
            .Add(JsonSerializer.SerializeToUtf8Bytes(
                entries,
                WorkerProtocolJson.Options));
        return hash.Finish();
    }

}
