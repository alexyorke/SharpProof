using System.Collections.Immutable;
using System.Text.Json;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static partial class CompilerForkJoinProjectionCodec
{
    private static void WriteRuntimePremises(Utf8JsonWriter writer, CompilerTaskRuntimePremiseCapture? capture,
        CompilerForkJoinProjection projection, CancellationToken cancellation)
    {
        writer = ArgumentNullGuard.NotNull(writer, nameof(writer));
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        cancellation.ThrowIfCancellationRequested();
        if (capture is null)
        { writer.WriteNullValue(); return; }
        Require((capture.Inventory is null) != (capture.UnknownReason is null), "Runtime premises require a value or Unknown.");
        writer.WriteStartObject();
        writer.WriteString("unknown", capture.UnknownReason);
        writer.WritePropertyName("value");
        if (capture.Inventory is not { } inventory)
        { writer.WriteNullValue(); }
        else
        {
            Require(OwnsRuntimePremises(inventory, projection), "Runtime premise ownership is invalid.");
            var declarations = inventory.Declarations;
            writer.WriteStartObject();
            writer.WriteString("root", inventory.RootCallableId);
            writer.WriteString("module", inventory.ModuleSha256);
            writer.WriteString("compiler", inventory.CompilerIdentity);
            writer.WriteString("framework", inventory.FrameworkAssemblyIdentity);
            writer.WriteNumber("profile", (int)declarations.Profile);
            writer.WriteString("deployment", declarations.DeploymentManifestSha256);
            writer.WriteString("evidence", declarations.Evidence);
            writer.WriteStartArray("quantities");
            foreach (var declaration in declarations.Quantities)
            {
                cancellation.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteNumber("quantity", (int)declaration.Quantity);
                writer.WriteNumber("upper", declaration.UpperBound);
                writer.WriteString("evidence", declaration.Evidence);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static CompilerTaskRuntimePremiseCapture? ReadRuntimePremises(JsonElement value,
        CompilerForkJoinProjection projection, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        cancellation.ThrowIfCancellationRequested();
        if (value.ValueKind == JsonValueKind.Null)
        { return null; }
        Properties(value, ["unknown", "value"]);
        var unknown = value.GetProperty("unknown");
        var encoded = value.GetProperty("value");
        Require((unknown.ValueKind == JsonValueKind.Null) != (encoded.ValueKind == JsonValueKind.Null),
            "Runtime premises require a value or Unknown.");
        if (encoded.ValueKind == JsonValueKind.Null)
        {
            var reason = Text(unknown);
            Require(!string.IsNullOrWhiteSpace(reason) && reason.Length <= 512 && !reason.Any(char.IsControl), "Runtime premise Unknown is invalid.");
            return new(null, reason);
        }
        Properties(encoded, ["root", "module", "compiler", "framework", "profile", "deployment", "evidence", "quantities"]);
        var quantities = encoded.GetProperty("quantities");
        Require(quantities.ValueKind == JsonValueKind.Array && quantities.GetArrayLength() == 11, "Runtime premise quantities are incomplete.");
        var declarations = ImmutableArray.CreateBuilder<WorkerResourceTaskRuntimeDeclaration>(11);
        foreach (var quantity in quantities.EnumerateArray())
        {
            cancellation.ThrowIfCancellationRequested();
            Properties(quantity, ["quantity", "upper", "evidence"]);
            var upper = quantity.GetProperty("upper");
            Require(upper.ValueKind == JsonValueKind.Number && upper.TryGetInt64(out _), "Runtime premise upper bound is invalid.");
            declarations.Add(new((WorkerResourceTaskRuntimeQuantity)Integer(quantity.GetProperty("quantity")),
                upper.GetInt64(), Text(quantity.GetProperty("evidence"))));
        }
        var owner = Text(encoded.GetProperty("root"));
        var declarationSet = new WorkerResourceTaskRuntimeDeclarations(owner,
            (WorkerResourceTaskDeploymentProfile)Integer(encoded.GetProperty("profile")),
            Text(encoded.GetProperty("deployment")), Text(encoded.GetProperty("evidence")), declarations.MoveToImmutable());
        var inventory = new CompilerTaskRuntimePremiseInventory(owner, Text(encoded.GetProperty("module")),
            Text(encoded.GetProperty("compiler")), Text(encoded.GetProperty("framework")), declarationSet);
        Require(OwnsRuntimePremises(inventory, projection), "Runtime premise ownership or declaration is invalid.");
        return new(inventory, null);
    }

    private static bool OwnsRuntimePremises(CompilerTaskRuntimePremiseInventory inventory, CompilerForkJoinProjection projection)
    {
        inventory = ArgumentNullGuard.NotNull(inventory, nameof(inventory));
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        return projection.Carriers?.Shapes is { } shapes && inventory.RootCallableId == projection.RootCallableId &&
            inventory.RootCallableId == shapes.RootCallableId && inventory.ModuleSha256 == shapes.ModuleSha256 &&
            inventory.CompilerIdentity == shapes.CompilerIdentity && inventory.FrameworkAssemblyIdentity == projection.FrameworkAssemblyIdentity &&
            WorkerProtocolJson.IsSha256(inventory.ModuleSha256) && !string.IsNullOrWhiteSpace(inventory.CompilerIdentity) &&
            inventory.CompilerIdentity.Length <= 512 && inventory.Declarations is { } declarations &&
            declarations.RootCallableId == inventory.RootCallableId && ResourceTaskRuntimePremiseIdentity.Validate(declarations) is null;
    }
}
