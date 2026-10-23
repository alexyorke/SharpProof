using System.Collections.Immutable;
using System.Text.Json;

namespace SharpProof.CompilerArtifact;

internal static partial class CompilerForkJoinProjectionCodec
{
    private static void WriteEmittedFaultBodies(Utf8JsonWriter writer, CompilerTaskEmittedFaultBodyCapture? capture,
        CancellationToken cancellation)
    {
        writer = ArgumentNullGuard.NotNull(writer, nameof(writer));
        cancellation.ThrowIfCancellationRequested();
        if (capture is null)
        { writer.WriteNullValue(); return; }
        Require((capture.Inventory is null) != (capture.UnknownReason is null), "Emitted fault bodies require an inventory or Unknown.");
        writer.WriteStartObject();
        writer.WriteString("unknown", capture.UnknownReason);
        writer.WritePropertyName("value");
        if (capture.Inventory is not { } inventory)
        { writer.WriteNullValue(); }
        else
        {
            Require(!inventory.Bodies.IsDefault && inventory.Bodies.Length is >= 1 and <= 16, "Emitted fault body count is invalid.");
            writer.WriteStartObject();
            writer.WriteString("root", inventory.RootCallableId);
            writer.WriteString("module", inventory.ModuleSha256);
            writer.WriteString("compiler", inventory.CompilerIdentity);
            writer.WriteString("framework", inventory.FrameworkAssemblyIdentity);
            writer.WriteStartArray("bodies");
            foreach (var body in inventory.Bodies)
            {
                cancellation.ThrowIfCancellationRequested();
                Require(body is not null && !body.Code.IsDefault && body.Code.Length is >= 1 and <= CompilerTaskPrimitiveIlScanner.MaximumCodeBytes &&
                    !body.ArrayTypeOperands.IsDefault && body.ArrayTypeOperands.Length <= body.Code.Length, "Emitted fault body is invalid.");
                writer.WriteStartObject();
                writer.WriteNumber("method", body!.MethodToken);
                writer.WriteBase64String("code", body.Code.AsSpan());
                writer.WriteString("sha256", body.CodeSha256);
                writer.WriteStartArray("arrays");
                foreach (var operand in body.ArrayTypeOperands)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Require(operand is not null, "Emitted array operand is invalid.");
                    writer.WriteStartObject();
                    writer.WriteNumber("offset", operand!.IlOffset);
                    writer.WriteNumber("token", operand.TypeToken);
                    writer.WriteNumber("element", (int)operand.ElementKind);
                    writer.WriteString("assembly", operand.AssemblyIdentity);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static CompilerTaskEmittedFaultBodyCapture? ReadEmittedFaultBodies(JsonElement value,
        CompilerTaskCarrierShapes shapes, string framework, CancellationToken cancellation)
    {
        shapes = ArgumentNullGuard.NotNull(shapes, nameof(shapes));
        framework = ArgumentNullGuard.NotNull(framework, nameof(framework));
        cancellation.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Null)
        { return null; }
        Properties(value, ["unknown", "value"]);
        var unknown = value.GetProperty("unknown");
        var encoded = value.GetProperty("value");
        Require((unknown.ValueKind == JsonValueKind.Null) != (encoded.ValueKind == JsonValueKind.Null), "Emitted fault bodies require an inventory or Unknown.");
        if (encoded.ValueKind == JsonValueKind.Null)
        {
            var reason = Text(unknown);
            Require(!string.IsNullOrWhiteSpace(reason) && reason.Length <= 512, "Emitted fault body Unknown reason is invalid.");
            return new(null, reason);
        }
        Properties(encoded, ["root", "module", "compiler", "framework", "bodies"]);
        var bodies = encoded.GetProperty("bodies");
        Require(bodies.ValueKind == JsonValueKind.Array && bodies.GetArrayLength() is >= 1 and <= 16, "Emitted fault body count is invalid.");
        var result = ImmutableArray.CreateBuilder<CompilerTaskEmittedFaultBody>(bodies.GetArrayLength());
        foreach (var body in bodies.EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            Properties(body, ["method", "code", "sha256", "arrays"]);
            var codeValue = body.GetProperty("code");
            Require(codeValue.ValueKind == JsonValueKind.String, "Emitted fault body code is invalid.");
            var codeText = codeValue.GetString()!;
            Require(codeText.Length > 0 && codeText.Length <= (CompilerTaskPrimitiveIlScanner.MaximumCodeBytes + 2) / 3 * 4,
                "Emitted fault body code exceeds its budget.");
            ImmutableArray<byte> code = [.. codeValue.GetBytesFromBase64()];
            var arrays = body.GetProperty("arrays");
            Require(arrays.ValueKind == JsonValueKind.Array && arrays.GetArrayLength() <= code.Length, "Emitted array operand count is invalid.");
            var operands = ImmutableArray.CreateBuilder<CompilerTaskEmittedArrayTypeOperand>(arrays.GetArrayLength());
            foreach (var operand in arrays.EnumerateArray())
            {
                cancellation.ThrowIfCancellationRequested();
                Properties(operand, ["offset", "token", "element", "assembly"]);
                var element = Integer(operand.GetProperty("element"));
                Require(element == (int)CompilerTaskEmittedArrayElementKind.FrameworkInt32, "Emitted array element kind is unsupported.");
                operands.Add(new(Integer(operand.GetProperty("offset")), Integer(operand.GetProperty("token")),
                    CompilerTaskEmittedArrayElementKind.FrameworkInt32, Text(operand.GetProperty("assembly"))));
            }
            result.Add(new(Integer(body.GetProperty("method")), code, Text(body.GetProperty("sha256")), operands.MoveToImmutable()));
        }
        var inventory = new CompilerTaskEmittedFaultBodyInventory(Text(encoded.GetProperty("root")), Text(encoded.GetProperty("module")),
            Text(encoded.GetProperty("compiler")), Text(encoded.GetProperty("framework")), result.MoveToImmutable());
        Require(CompilerTaskEmittedFaultBodyOwnership.Validate(inventory, shapes, framework, cancellation) is null,
            "Emitted fault body ownership or completeness is invalid.");
        return new(inventory, null);
    }
}
