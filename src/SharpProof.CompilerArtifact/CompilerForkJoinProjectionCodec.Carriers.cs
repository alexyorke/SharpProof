using System.Collections.Immutable;
using System.Text.Json;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static partial class CompilerForkJoinProjectionCodec
{
    private static void WriteCarriers(Utf8JsonWriter writer, CompilerTaskCarrierCapture? capture, CancellationToken cancellation)
    {
        writer = ArgumentNullGuard.NotNull(writer, nameof(writer));
        cancellation.ThrowIfCancellationRequested();
        if (capture == null)
        { writer.WriteNullValue(); return; }
        Require((capture.Shapes == null) != (capture.UnknownReason == null), "Carrier evidence requires shapes or an Unknown reason.");
        writer.WriteStartObject();
        writer.WriteString("unknown", capture.UnknownReason);
        writer.WritePropertyName("shapes");
        if (capture.Shapes is not { } shapes)
        { writer.WriteNullValue(); }
        else
        {
            Require(!shapes.CallbackFrames.IsDefault && shapes.CallbackFrames.Length <= 64, "Carrier frame count is invalid.");
            writer.WriteStartObject();
            writer.WriteString("root", shapes.RootCallableId);
            writer.WriteString("module", shapes.ModuleSha256);
            writer.WriteString("compiler", shapes.CompilerIdentity);
            writer.WriteNumber("callbacks", shapes.MatchedCallbacks);
            writer.WriteStartArray("frames");
            foreach (var frame in shapes.CallbackFrames)
            { cancellation.ThrowIfCancellationRequested(); WriteCarrierType(writer, frame, cancellation); }
            writer.WriteEndArray();
            writer.WritePropertyName("state");
            if (shapes.StateMachine == null)
            { writer.WriteNullValue(); }
            else
            { WriteCarrierType(writer, shapes.StateMachine, cancellation); }
            writer.WritePropertyName("boundary");
            WriteBoundary(writer, shapes.RootExceptionBoundary, cancellation);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void WriteCarrierType(Utf8JsonWriter writer, CompilerTaskCarrierType type, CancellationToken cancellation)
    {
        writer = ArgumentNullGuard.NotNull(writer, nameof(writer));
        type = ArgumentNullGuard.NotNull(type, nameof(type));
        Require(!type.Fields.IsDefault && type.Fields.Length <= ResourceAllocationShape.MaximumFields, "Carrier fields are invalid.");
        writer.WriteStartObject();
        writer.WriteString("name", type.MetadataName);
        writer.WriteBoolean("value", type.IsValueType);
        writer.WriteStartArray("fields");
        foreach (var field in type.Fields)
        { cancellation.ThrowIfCancellationRequested(); writer.WriteNumberValue((int)field); }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static CompilerTaskCarrierCapture? ReadCarriers(JsonElement value, string owner, int children,
        bool configured, CancellationToken cancellation)
    {
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        cancellation.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Null)
        { return null; }
        Properties(value, ["unknown", "shapes"]);
        var unknown = value.GetProperty("unknown");
        var shapes = value.GetProperty("shapes");
        Require((unknown.ValueKind == JsonValueKind.Null) != (shapes.ValueKind == JsonValueKind.Null),
            "Carrier evidence requires shapes or an Unknown reason.");
        if (shapes.ValueKind == JsonValueKind.Null)
        {
            var reason = Text(unknown);
            Require(!string.IsNullOrWhiteSpace(reason) && reason.Length <= 512, "Carrier Unknown reason is too long.");
            return new(null, reason);
        }
        Properties(shapes, ["root", "module", "compiler", "callbacks", "frames", "state", "boundary"]);
        Require(Text(shapes.GetProperty("root")) == owner, "Carrier root is unowned.");
        var module = Text(shapes.GetProperty("module"));
        Require(WorkerProtocolJson.IsSha256(module), "Carrier module hash is invalid.");
        var compiler = Text(shapes.GetProperty("compiler"));
        Require(!string.IsNullOrWhiteSpace(compiler) && compiler.Length <= 512, "Carrier compiler identity is too long.");
        var callbacks = Integer(shapes.GetProperty("callbacks"));
        Require(callbacks >= children && callbacks <= 64, "Carrier callback count is invalid.");
        var encodedFrames = shapes.GetProperty("frames");
        Require(encodedFrames.ValueKind == JsonValueKind.Array && encodedFrames.GetArrayLength() <= callbacks,
            "Carrier frame count is invalid.");
        var remaining = ResourceExpression.MaximumNodes;
        var frames = ImmutableArray.CreateBuilder<CompilerTaskCarrierType>(encodedFrames.GetArrayLength());
        foreach (var encoded in encodedFrames.EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            var frame = ReadCarrierType(encoded, ref remaining, cancellation);
            Require(!frame.IsValueType, "Carrier callback frame must be a reference type.");
            frames.Add(frame);
        }
        var encodedState = shapes.GetProperty("state");
        Require((encodedState.ValueKind != JsonValueKind.Null) == configured, "Carrier state-machine kind is inconsistent.");
        var state = configured ? ReadCarrierType(encodedState, ref remaining, cancellation) : null;
        return new(new(owner, module, compiler, callbacks, frames.MoveToImmutable(), state)
        { RootExceptionBoundary = ReadBoundary(shapes.GetProperty("boundary"), owner, module, state, cancellation) }, null);
    }

    private static void WriteBoundary(Utf8JsonWriter writer, CompilerTaskRootExceptionBoundaryCapture? capture, CancellationToken cancellation)
    {
        writer = ArgumentNullGuard.NotNull(writer, nameof(writer));
        cancellation.ThrowIfCancellationRequested();
        if (capture is null)
        { writer.WriteNullValue(); return; }
        Require((capture.Boundary is null) != (capture.UnknownReason is null), "Boundary evidence requires a value or Unknown reason.");
        writer.WriteStartObject();
        writer.WriteString("unknown", capture.UnknownReason);
        writer.WritePropertyName("value");
        if (capture.Boundary is not { } boundary)
        { writer.WriteNullValue(); }
        else
        {
            writer.WriteStartObject();
            writer.WriteString("root", boundary.RootCallableId);
            writer.WriteString("module", boundary.ModuleSha256);
            writer.WriteString("state", boundary.StateMachineMetadataName);
            writer.WriteNumber("method", boundary.MethodToken);
            writer.WriteNumber("offset", boundary.TryOffset);
            writer.WriteNumber("length", boundary.TryLength);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static CompilerTaskRootExceptionBoundaryCapture? ReadBoundary(JsonElement value, string owner, string module,
        CompilerTaskCarrierType? state, CancellationToken cancellation)
    {
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        module = ArgumentNullGuard.NotNull(module, nameof(module));
        cancellation.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Null)
        { return null; }
        Properties(value, ["unknown", "value"]);
        var unknown = value.GetProperty("unknown");
        var encoded = value.GetProperty("value");
        Require((unknown.ValueKind == JsonValueKind.Null) != (encoded.ValueKind == JsonValueKind.Null), "Boundary evidence requires a value or Unknown reason.");
        if (encoded.ValueKind == JsonValueKind.Null)
        {
            var reason = Text(unknown);
            Require(!string.IsNullOrWhiteSpace(reason) && reason.Length <= 512, "Boundary Unknown reason is invalid.");
            return new(null, reason);
        }
        Properties(encoded, ["root", "module", "state", "method", "offset", "length"]);
        Require(Text(encoded.GetProperty("root")) == owner && Text(encoded.GetProperty("module")) == module &&
            state is not null && Text(encoded.GetProperty("state")) == state.MetadataName, "Boundary source identity is unowned.");
        var method = Integer(encoded.GetProperty("method"));
        var offset = Integer(encoded.GetProperty("offset"));
        var length = Integer(encoded.GetProperty("length"));
        Require(method >> 24 == 6 && (method & 0x00ffffff) != 0, "Boundary method token is invalid.");
        Require(offset >= 0 && offset < 4096 && length > 0 && length <= 4096 - offset, "Boundary range is invalid.");
        return new(new(owner, module, state!.MetadataName, method, offset, length), null);
    }

    private static CompilerTaskCarrierType ReadCarrierType(JsonElement value, ref int remaining, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Properties(value, ["name", "value", "fields"]);
        var name = Text(value.GetProperty("name"));
        Require(!string.IsNullOrWhiteSpace(name) && name.Length <= 4096, "Carrier metadata identity is too long.");
        var kind = value.GetProperty("value");
        Require(kind.ValueKind is JsonValueKind.True or JsonValueKind.False, "Carrier type kind is invalid.");
        var encodedFields = value.GetProperty("fields");
        Require(encodedFields.ValueKind == JsonValueKind.Array && encodedFields.GetArrayLength() <= ResourceAllocationShape.MaximumFields,
            "Carrier fields are invalid.");
        remaining -= encodedFields.GetArrayLength() + 1;
        Require(remaining >= 0, "Carrier field budget is exceeded.");
        var fields = ImmutableArray.CreateBuilder<ResourceStorageKind>(encodedFields.GetArrayLength());
        foreach (var encoded in encodedFields.EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            var field = (ResourceStorageKind)Integer(encoded);
            Require(field is ResourceStorageKind.Byte or ResourceStorageKind.Word16 or ResourceStorageKind.Word32 or
                ResourceStorageKind.Word64 or ResourceStorageKind.Reference or ResourceStorageKind.NativeInteger, "Carrier storage kind is invalid.");
            fields.Add(field);
        }
        return new(name, kind.GetBoolean(), fields.MoveToImmutable());
    }
}
