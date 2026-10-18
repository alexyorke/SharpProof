using System.Text.Json;

namespace SharpProof;

internal static class SharpProofJsonDefaults
{
    internal static JsonSerializerOptions Indented { get; } = new()
    {
        WriteIndented = true
    };
}
