using System.Text.Json.Serialization;

namespace SharpProof.Worker.Protocol;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkerVerifyRequest))]
[JsonSerializable(typeof(WorkerVerifyResponse))]
[JsonSerializable(typeof(WorkerClaimManifest))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext
{
}
