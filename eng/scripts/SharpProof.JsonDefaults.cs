using System.Text.Json;

namespace SharpProof;

internal static class SharpProofJsonDefaults
{
    internal static JsonSerializerOptions Indented { get; } = CreateIndented();

    private static JsonSerializerOptions CreateIndented()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
