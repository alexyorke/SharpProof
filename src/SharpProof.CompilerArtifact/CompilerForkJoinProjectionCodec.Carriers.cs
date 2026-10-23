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
            writer.WritePropertyName("activations");
            WriteCallbackActivations(writer, shapes.CallbackActivations, cancellation);
            writer.WritePropertyName("bodies");
            WriteSourceBodies(writer, shapes.SourceBodies, cancellation);
            writer.WritePropertyName("faultBodies");
            WriteEmittedFaultBodies(writer, shapes.EmittedFaultBodies, cancellation);
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

    private static CompilerTaskCarrierCapture? ReadCarriers(JsonElement value, string owner, string framework, int children,
        bool configured, CancellationToken cancellation)
    {
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        framework = ArgumentNullGuard.NotNull(framework, nameof(framework));
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
        Properties(shapes, ["root", "module", "compiler", "callbacks", "frames", "state", "boundary", "activations", "bodies", "faultBodies"]);
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
        var activations = ReadCallbackActivations(shapes.GetProperty("activations"), owner, module, callbacks, children, cancellation);
        var result = new CompilerTaskCarrierShapes(owner, module, compiler, callbacks, frames.MoveToImmutable(), state)
        {
            RootExceptionBoundary = ReadBoundary(shapes.GetProperty("boundary"), owner, module, state, cancellation),
            CallbackActivations = activations,
            SourceBodies = ReadSourceBodies(shapes.GetProperty("bodies"), owner, module, activations?.Activations, cancellation)
        };
        return new(result with
        {
            EmittedFaultBodies = ReadEmittedFaultBodies(shapes.GetProperty("faultBodies"), result, framework, cancellation)
        }, null);
    }

    private static void WriteSourceBodies(Utf8JsonWriter writer, CompilerTaskSourceBodyCapture? capture, CancellationToken cancellation)
    {
        writer = ArgumentNullGuard.NotNull(writer, nameof(writer));
        cancellation.ThrowIfCancellationRequested();
        if (capture is null)
        { writer.WriteNullValue(); return; }
        Require((capture.Inventory is null) != (capture.UnknownReason is null), "Source bodies require an inventory or Unknown.");
        writer.WriteStartObject();
        writer.WriteString("unknown", capture.UnknownReason);
        writer.WritePropertyName("value");
        if (capture.Inventory is not { } inventory)
        { writer.WriteNullValue(); }
        else
        {
            Require(!inventory.MethodTokens.IsDefault && inventory.MethodTokens.Length is >= 1 and <= 16, "Source body count is invalid.");
            writer.WriteStartObject();
            writer.WriteString("root", inventory.RootCallableId);
            writer.WriteString("module", inventory.ModuleSha256);
            writer.WriteStartArray("methods");
            foreach (var token in inventory.MethodTokens)
            { cancellation.ThrowIfCancellationRequested(); writer.WriteNumberValue(token); }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static CompilerTaskSourceBodyCapture? ReadSourceBodies(JsonElement value, string owner, string module,
        CompilerTaskCallbackActivation? activations, CancellationToken cancellation)
    {
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        module = ArgumentNullGuard.NotNull(module, nameof(module));
        cancellation.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Null)
        { return null; }
        Properties(value, ["unknown", "value"]);
        var unknown = value.GetProperty("unknown");
        var encoded = value.GetProperty("value");
        Require((unknown.ValueKind == JsonValueKind.Null) != (encoded.ValueKind == JsonValueKind.Null), "Source bodies require an inventory or Unknown.");
        if (encoded.ValueKind == JsonValueKind.Null)
        {
            var reason = Text(unknown);
            Require(!string.IsNullOrWhiteSpace(reason) && reason.Length <= 512, "Source body Unknown reason is invalid.");
            return new(null, reason);
        }
        Properties(encoded, ["root", "module", "methods"]);
        Require(Text(encoded.GetProperty("root")) == owner && Text(encoded.GetProperty("module")) == module, "Source body identity is unowned.");
        var list = encoded.GetProperty("methods");
        Require(list.ValueKind == JsonValueKind.Array && list.GetArrayLength() is >= 1 and <= 16 && activations is not null,
            "Source body coverage is unavailable.");
        var methods = ImmutableArray.CreateBuilder<int>(list.GetArrayLength());
        var unique = new HashSet<int>();
        foreach (var item in list.EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            var token = Integer(item);
            Require(MethodDefinitionToken(token) && unique.Add(token), "Source body token is invalid or duplicated.");
            methods.Add(token);
        }
        Require(unique.SetEquals(activations!.Forwarders.Select(static forwarder =>
            forwarder is null ? throw new ArgumentNullException(nameof(forwarder)) : forwarder.ChildMethodToken)), "Source bodies do not cover the forwarded targets.");
        return new(new(owner, module, methods.MoveToImmutable()), null);
    }

    private static void WriteCallbackActivations(Utf8JsonWriter writer, CompilerTaskCallbackActivationCapture? capture, CancellationToken cancellation)
    {
        writer = ArgumentNullGuard.NotNull(writer, nameof(writer));
        cancellation.ThrowIfCancellationRequested();
        if (capture is null)
        { writer.WriteNullValue(); return; }
        Require((capture.Activations is null) != (capture.UnknownReason is null), "Callback evidence requires activations or Unknown.");
        writer.WriteStartObject();
        writer.WriteString("unknown", capture.UnknownReason);
        writer.WritePropertyName("value");
        if (capture.Activations is not { } activations)
        { writer.WriteNullValue(); }
        else
        {
            Require(!activations.Forwarders.IsDefault && activations.Forwarders.Length <= 64, "Callback activation count is invalid.");
            writer.WriteStartObject();
            writer.WriteString("root", activations.RootCallableId);
            writer.WriteString("module", activations.ModuleSha256);
            writer.WriteStartArray("forwarders");
            foreach (var forwarder in activations.Forwarders)
            {
                cancellation.ThrowIfCancellationRequested();
                Require(forwarder is not null, "Callback forwarder is missing.");
                writer.WriteStartObject();
                writer.WriteNumber("method", forwarder!.MethodToken);
                writer.WriteNumber("child", forwarder.ChildMethodToken);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static CompilerTaskCallbackActivationCapture? ReadCallbackActivations(JsonElement value, string owner, string module,
        int callbacks, int sourceChildren, CancellationToken cancellation)
    {
        owner = ArgumentNullGuard.NotNull(owner, nameof(owner));
        module = ArgumentNullGuard.NotNull(module, nameof(module));
        cancellation.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Null)
        { return null; }
        Properties(value, ["unknown", "value"]);
        var unknown = value.GetProperty("unknown");
        var encoded = value.GetProperty("value");
        Require((unknown.ValueKind == JsonValueKind.Null) != (encoded.ValueKind == JsonValueKind.Null), "Callback evidence requires activations or Unknown.");
        if (encoded.ValueKind == JsonValueKind.Null)
        {
            var reason = Text(unknown);
            Require(!string.IsNullOrWhiteSpace(reason) && reason.Length <= 512, "Callback Unknown reason is invalid.");
            return new(null, reason);
        }
        Properties(encoded, ["root", "module", "forwarders"]);
        Require(Text(encoded.GetProperty("root")) == owner && Text(encoded.GetProperty("module")) == module, "Callback activation identity is unowned.");
        var list = encoded.GetProperty("forwarders");
        Require(list.ValueKind == JsonValueKind.Array && list.GetArrayLength() == callbacks && callbacks <= 64, "Callback activation count is invalid.");
        var methods = new HashSet<int>();
        var children = new HashSet<int>();
        var forwarders = ImmutableArray.CreateBuilder<CompilerTaskCallbackForwarder>(callbacks);
        foreach (var item in list.EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            Properties(item, ["method", "child"]);
            var method = Integer(item.GetProperty("method"));
            var child = Integer(item.GetProperty("child"));
            Require(MethodDefinitionToken(method) && MethodDefinitionToken(child) && methods.Add(method), "Callback method token is invalid or duplicated.");
            children.Add(child);
            forwarders.Add(new(method, child));
        }
        Require(children.Count <= sourceChildren && !methods.Overlaps(children), "Callback targets are not owned source children.");
        return new(new(owner, module, forwarders.MoveToImmutable()), null);
    }

    private static bool MethodDefinitionToken(int token)
    { return token >> 24 == 6 && (token & 0x00ffffff) != 0; }

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
