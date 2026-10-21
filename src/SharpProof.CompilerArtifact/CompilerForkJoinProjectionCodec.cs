using System.Collections.Immutable;
using System.Text.Json;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

// Detached transport and ownership validation. The containing compiler envelope
// authenticates source provenance; runtime premises are checked separately.
internal static partial class CompilerForkJoinProjectionCodec
{
    internal const int Version = 4;
    internal const int MaximumPayloadBytes = 1024 * 1024;

    internal static byte[] Encode(CompilerForkJoinProjection projection, CompilerTotalEntryPreparation root, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        cancellation.ThrowIfCancellationRequested();
        Require(!projection.RootParameterNames.IsDefault && !projection.Children.IsDefault && projection.Children.Length is >= 2 and <= 16,
            "Fork-join projection shape is invalid.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("root", projection.RootCallableId);
            writer.WriteString("framework", projection.FrameworkAssemblyIdentity);
            writer.WriteBoolean("configuredAwait", projection.ConfiguredAwait);
            writer.WritePropertyName("carriers");
            WriteCarriers(writer, projection.Carriers, cancellation);
            writer.WriteStartArray("inputs");
            foreach (var name in projection.RootParameterNames)
            { cancellation.ThrowIfCancellationRequested(); writer.WriteStringValue(name); }
            writer.WriteEndArray();
            writer.WriteStartArray("children");
            foreach (var child in projection.Children)
            {
                cancellation.ThrowIfCancellationRequested();
                if (child == null || child.Owner == null || child.Cost == null || child.Arguments.IsDefault)
                { throw new InvalidDataException("Fork-join child is malformed."); }
                writer.WriteStartObject();
                writer.WriteString("owner", child.Owner.CallableId);
                writer.WritePropertyName("body");
                JsonSerializer.Serialize(writer, CompilerTotalCallableArtifactCodec.Encode(child.Owner, cancellation), WorkerProtocolJson.SharedOptions);
                writer.WriteBase64String("cost", ResourceProgramCodec.Encode(child.Cost, cancellation));
                writer.WriteStartArray("arguments");
                foreach (var argument in child.Arguments)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Require(argument != null, "Fork-join argument is malformed.");
                    writer.WriteStartObject();
                    writer.WriteNumber("parameter", argument!.ParameterOrdinal);
                    if (argument.RootParameterOrdinal is { } ordinal)
                    { writer.WriteNumber("input", ordinal); }
                    else
                    { writer.WriteNull("input"); }
                    if (argument.Constant is { } constant)
                    { writer.WriteNumber("constant", constant); }
                    else
                    { writer.WriteNull("constant"); }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
                Require(stream.Length <= MaximumPayloadBytes, "Fork-join projection payload exceeds its budget.");
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        Require(stream.Length <= MaximumPayloadBytes, "Fork-join projection payload exceeds its budget.");
        var payload = stream.ToArray();
        _ = Decode(payload, root, cancellation);
        return payload;
    }

    internal static CompilerForkJoinProjection Decode(byte[] payload, CompilerTotalEntryPreparation root, CancellationToken cancellation)
    {
        payload = ArgumentNullGuard.NotNull(payload, nameof(payload));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        cancellation.ThrowIfCancellationRequested();
        try
        { return DecodeCore(payload, root, cancellation); }
        catch (JsonException exception)
        { throw new InvalidDataException("Fork-join projection JSON is malformed.", exception); }
        catch (FormatException exception)
        { throw new InvalidDataException("Fork-join projection payload is malformed.", exception); }
    }

    private static CompilerForkJoinProjection DecodeCore(byte[] payload, CompilerTotalEntryPreparation root, CancellationToken cancellation)
    {
        payload = ArgumentNullGuard.NotNull(payload, nameof(payload));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        Require(payload.Length is > 0 and <= MaximumPayloadBytes, "Fork-join projection payload exceeds its budget.");
        Require(!string.IsNullOrWhiteSpace(root.CallableId) && !root.HasReceiver && !root.Parameters.IsDefault &&
            root.Parameters.Length <= ResourceExpression.MaximumNodes, "Fork-join root ownership is invalid.");
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = ResourceExpression.MaximumDepth });
        var value = document.RootElement;
        Properties(value, ["version", "root", "framework", "configuredAwait", "carriers", "inputs", "children"]);
        Require(Integer(value.GetProperty("version")) == Version && Text(value.GetProperty("root")) == root.CallableId,
            "Fork-join projection version or root identity is invalid.");
        var framework = Text(value.GetProperty("framework"));
        var configuredAwait = value.GetProperty("configuredAwait");
        Require(configuredAwait.ValueKind is JsonValueKind.True or JsonValueKind.False, "Fork-join execution kind is invalid.");
        var inputs = value.GetProperty("inputs");
        Require(inputs.ValueKind == JsonValueKind.Array && inputs.GetArrayLength() == root.Parameters.Length,
            "Fork-join root input map is invalid.");
        var names = ImmutableArray.CreateBuilder<string>(root.Parameters.Length);
        foreach (var input in inputs.EnumerateArray())
        { cancellation.ThrowIfCancellationRequested(); names.Add(Text(input)); }
        var encodedChildren = value.GetProperty("children");
        Require(encodedChildren.ValueKind == JsonValueKind.Array && encodedChildren.GetArrayLength() is >= 2 and <= 16,
            "Fork-join child count is invalid.");
        var children = ImmutableArray.CreateBuilder<CompilerForkJoinChild>(encodedChildren.GetArrayLength());
        var remaining = ResourceExpression.MaximumNodes;
        foreach (var encoded in encodedChildren.EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            Properties(encoded, ["owner", "body", "cost", "arguments"]);
            var identity = Text(encoded.GetProperty("owner"));
            Require(identity == root.CallableId + ":fork-child:" + children.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Fork-join child identity is invalid.");
            var bodyJson = encoded.GetProperty("body");
            Require(bodyJson.ValueKind == JsonValueKind.Object, "Fork-join child body is missing.");
            var bodyArtifact = JsonSerializer.Deserialize<CompilerTotalCallableArtifact>(bodyJson.GetRawText(), WorkerProtocolJson.SharedOptions)
                ?? throw new InvalidDataException("Fork-join child body is missing.");
            var body = CompilerTotalCallableArtifactCodec.Decode(bodyArtifact, new WorkerCallableManifestEntry { CallableId = identity }, [], cancellation)
                ?? throw new InvalidDataException("Fork-join child body is missing.");
            Require(!body.IsBodyAbstraction && body.EffectsCompleteAtEntry && !body.HasReceiver && body.Result == null &&
                body.Clauses.IsEmpty && body.CallPreconditions.IsEmpty && body.ValidEffectClaimIds.IsEmpty,
                "Fork-join child carries an invalid proof role.");
            foreach (var block in body.Program.Blocks)
            { remaining -= block.Instructions.Length; Require(remaining >= 0, "Fork-join projection exceeds its instruction budget."); }
            var encodedCost = encoded.GetProperty("cost");
            Require(encodedCost.ValueKind == JsonValueKind.String, "Fork-join cost program is missing.");
            var cost = ResourceProgramCodec.Decode(encodedCost.GetBytesFromBase64(), cancellation);
            ResourceProgramOwnership.Validate(cost, identity, body, cancellation);
            var encodedArguments = encoded.GetProperty("arguments");
            Require(encodedArguments.ValueKind == JsonValueKind.Array && encodedArguments.GetArrayLength() == body.Parameters.Length,
                "Fork-join formal argument map is invalid.");
            var arguments = ImmutableArray.CreateBuilder<CompilerForkJoinArgument>(body.Parameters.Length);
            var ordinals = new HashSet<int>();
            foreach (var argument in encodedArguments.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested();
                Properties(argument, ["parameter", "input", "constant"]);
                var ordinal = Integer(argument.GetProperty("parameter"));
                Require(ordinal >= 0 && ordinal < body.Parameters.Length && ordinals.Add(ordinal), "Fork-join formal argument map is invalid.");
                var input = argument.GetProperty("input");
                var constant = argument.GetProperty("constant");
                Require((input.ValueKind == JsonValueKind.Null) != (constant.ValueKind == JsonValueKind.Null), "Fork-join argument source is ambiguous.");
                int? inputOrdinal = input.ValueKind == JsonValueKind.Null ? null : Integer(input);
                Require(inputOrdinal == null || inputOrdinal >= 0 && inputOrdinal < root.Parameters.Length, "Fork-join argument input is unowned.");
                arguments.Add(new(ordinal, inputOrdinal, constant.ValueKind == JsonValueKind.Null ? null : Integer(constant)));
            }
            children.Add(new(body, cost, arguments.MoveToImmutable()));
        }
        return new(root.CallableId, framework, names.MoveToImmutable(), children.MoveToImmutable())
        {
            ConfiguredAwait = configuredAwait.GetBoolean(),
            Carriers = ReadCarriers(value.GetProperty("carriers"), root.CallableId,
            encodedChildren.GetArrayLength(), configuredAwait.GetBoolean(), cancellation)
        };
    }

    private static void Properties(JsonElement value, string[] expected)
    {
        expected = ArgumentNullGuard.NotNull(expected, nameof(expected));
        Require(value.ValueKind == JsonValueKind.Object, "Fork-join projection object is malformed.");
        var names = new HashSet<string>(expected, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        { Require(names.Remove(property.Name), "Fork-join projection property is unknown or duplicated."); }
        Require(names.Count == 0, "Fork-join projection property is missing.");
    }

    private static string Text(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.String, "Fork-join projection text is malformed.");
        var text = value.GetString()!;
        Require(text.Length is > 0 and <= ResourceExpression.MaximumTextLength && !text.Any(static character => char.IsControl(character)),
            "Fork-join projection text is malformed.");
        return text;
    }

    private static int Integer(JsonElement value)
    { if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number)) { throw new InvalidDataException("Fork-join projection integer is malformed."); } return number; }

    private static void Require(bool condition, string message)
    { message = ArgumentNullGuard.NotNull(message, nameof(message)); if (!condition) { throw new InvalidDataException(message); } }
}
