using System.Collections.Immutable;
using System.Text.Json;
using SharpProof.Ir;

namespace SharpProof.CompilerArtifact;

// Transports storage facts, never computed byte bounds or runtime assertions.
internal static class ResourceAllocationShapeCodec
{
    internal const int MaximumPayloadBytes = 1024 * 1024;

    internal static byte[] Encode(ResourceAllocationShape shape, CancellationToken cancellation)
    {
        shape = ArgumentNullGuard.NotNull(shape, nameof(shape));
        cancellation.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", ResourceAllocationShape.ModelVersion);
            writer.WriteNumber("expressionModel", ResourceExpression.ModelVersion);
            writer.WriteNumber("kind", (int)shape.Kind);
            writer.WriteNumber("storage", (int)shape.Storage);
            writer.WriteBase64String("extent", ResourceExpressionCodec.Encode(shape.Extent, cancellation));
            writer.WriteStartArray("fields");
            foreach (var field in shape.Fields)
            {
                cancellation.ThrowIfCancellationRequested();
                writer.WriteNumberValue((int)field);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        if (stream.Length > MaximumPayloadBytes)
        { throw new ResourceExpressionLimitException(); }
        return stream.ToArray();
    }

    internal static ResourceAllocationShape Decode(byte[] payload, CancellationToken cancellation)
    {
        payload = ArgumentNullGuard.NotNull(payload, nameof(payload));
        cancellation.ThrowIfCancellationRequested();
        if (payload.Length == 0 || payload.Length > MaximumPayloadBytes)
        { throw new InvalidDataException("Allocation shape payload size is invalid."); }
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            RequireFields(root);
            if (Integer(root.GetProperty("version")) != ResourceAllocationShape.ModelVersion ||
                Integer(root.GetProperty("expressionModel")) != ResourceExpression.ModelVersion)
            { throw new InvalidDataException("Allocation shape model is unsupported."); }
            var kind = (ResourceAllocationKind)Integer(root.GetProperty("kind"));
            var storage = (ResourceStorageKind)Integer(root.GetProperty("storage"));
            if (!Enum.IsDefined(typeof(ResourceAllocationKind), kind) || !Enum.IsDefined(typeof(ResourceStorageKind), storage))
            { throw new InvalidDataException("Allocation shape kind is undefined."); }
            var encodedExtent = root.GetProperty("extent");
            if (encodedExtent.ValueKind != JsonValueKind.String)
            { throw new InvalidDataException("Allocation extent must be encoded arithmetic."); }
            var text = encodedExtent.GetString()!;
            var bytes = Convert.FromBase64String(text);
            if (Convert.ToBase64String(bytes) != text)
            { throw new InvalidDataException("Allocation extent encoding is not canonical."); }
            var extent = ResourceExpressionCodec.Decode(bytes, cancellation);
            var encodedFields = root.GetProperty("fields");
            if (encodedFields.ValueKind != JsonValueKind.Array || encodedFields.GetArrayLength() > ResourceAllocationShape.MaximumFields)
            { throw new InvalidDataException("Allocation field storage is invalid."); }
            var fields = ImmutableArray.CreateBuilder<ResourceStorageKind>(encodedFields.GetArrayLength());
            foreach (var field in encodedFields.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested();
                var value = (ResourceStorageKind)Integer(field);
                if (value == ResourceStorageKind.Unspecified || !Enum.IsDefined(typeof(ResourceStorageKind), value))
                { throw new InvalidDataException("Allocation field kind is undefined."); }
                fields.Add(value);
            }
            var entries = fields.MoveToImmutable();
            if (kind is not (ResourceAllocationKind.Object or ResourceAllocationKind.Closure) && !entries.IsEmpty ||
                kind is not (ResourceAllocationKind.Array or ResourceAllocationKind.String) &&
                    extent.Constant != (kind == ResourceAllocationKind.Unspecified ? 0 : 1) ||
                kind is ResourceAllocationKind.Object or ResourceAllocationKind.Closure or ResourceAllocationKind.Delegate or ResourceAllocationKind.Unspecified &&
                    storage != ResourceStorageKind.Unspecified ||
                kind == ResourceAllocationKind.String && storage != ResourceStorageKind.Word16)
            { throw new InvalidDataException("Allocation shape carries an unrelated payload."); }
            return kind switch
            {
                ResourceAllocationKind.Unspecified => ResourceAllocationShape.Unsupported,
                ResourceAllocationKind.Array => ResourceAllocationShape.Array(storage, extent),
                ResourceAllocationKind.Object => ResourceAllocationShape.Object(entries),
                ResourceAllocationKind.String => ResourceAllocationShape.String(extent),
                ResourceAllocationKind.Box => ResourceAllocationShape.Box(storage),
                ResourceAllocationKind.Delegate => ResourceAllocationShape.Delegate(),
                ResourceAllocationKind.Closure => ResourceAllocationShape.Closure(entries),
                _ => throw new InvalidDataException("Allocation shape kind is unsupported.")
            };
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException or ResourceExpressionLimitException)
        { throw new InvalidDataException("Allocation shape payload is malformed.", exception); }
    }

    private static void RequireFields(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        { throw new InvalidDataException("Allocation shape must be an object."); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("version" or "expressionModel" or "kind" or "storage" or "extent" or "fields") || !names.Add(property.Name))
            { throw new InvalidDataException("Allocation shape has unknown or duplicate fields."); }
        }
        if (names.Count != 6)
        { throw new InvalidDataException("Allocation shape fields are missing."); }
    }

    private static int Integer(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        { throw new InvalidDataException("Allocation shape integer is invalid."); }
        return result;
    }
}
