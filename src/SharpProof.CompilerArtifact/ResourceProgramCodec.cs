using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SharpProof.Ir;

namespace SharpProof.CompilerArtifact;

// Resource programs are compiler-lowered semantics, not producer verdicts.
// Decode establishes shape/budgets only; the native checker derives costs.
internal static class ResourceProgramCodec
{
    internal const int MaximumPayloadBytes = 1024 * 1024;
    internal const string CostModel = "loop-entries-and-primitive-array-elements-v3";

    internal static byte[] Encode(ResourceProgram program, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        cancellation.ThrowIfCancellationRequested();
        // Immutable construction already bounds expanded node count and depth.
        // The writer additionally bounds the aggregate encoded payload.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", ResourceProgram.ModelVersion);
            writer.WriteNumber("expressionModel", ResourceExpression.ModelVersion);
            writer.WriteString("costModel", CostModel);
            writer.WritePropertyName("program");
            Write(writer, program, cancellation);
            writer.WriteEndObject();
            writer.Flush();
        }
        return stream.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, ResourceProgram program, CancellationToken cancellation)
    {
        writer = ArgumentNullGuard.NotNull(writer, nameof(writer));
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        cancellation.ThrowIfCancellationRequested();

        writer.WriteStartObject();
        writer.WriteNumber("kind", (int)program.Kind);
        writer.WritePropertyName("length");
        if (program.Length is { } length)
        { writer.WriteBase64StringValue(ResourceExpressionCodec.Encode(length, cancellation)); }
        else
        { writer.WriteNullValue(); }
        writer.WritePropertyName("rank");
        if (program.Rank is { } rank)
        {
            writer.WriteStartObject();
            writer.WriteNumber("kind", (int)rank.Kind);
            writer.WriteBase64String("initial", ResourceExpressionCodec.Encode(rank.Initial, cancellation));
            writer.WriteBase64String("limit", ResourceExpressionCodec.Encode(rank.Limit, cancellation));
            writer.WriteString("step", rank.Step.ToString(CultureInfo.InvariantCulture));
            writer.WriteNumber("width", rank.CounterWidth);
            writer.WriteBoolean("checked", rank.CheckedStep);
            writer.WriteEndObject();
        }
        else
        { writer.WriteNullValue(); }
        writer.WritePropertyName("metadata");
        if (program.Metadata is { } metadata)
        {
            writer.WriteStartObject();
            writer.WriteString("witness", metadata.WitnessIdentifier);
            writer.WriteString("catalog", metadata.CatalogSha256);
            writer.WriteString("assembly", metadata.AssemblyIdentity);
            writer.WriteEndObject();
        }
        else
        { writer.WriteNullValue(); }
        writer.WriteNumber("repeatKind", (int)program.RepeatKind);
        writer.WriteStartArray("children");
        foreach (var child in program.Children)
        { Write(writer, child, cancellation); }
        writer.WriteEndArray();
        writer.WriteEndObject();
        // Reserve the final byte for the enclosing envelope object.
        if (writer.BytesCommitted + writer.BytesPending >= MaximumPayloadBytes)
        { throw new ResourceExpressionLimitException(); }
    }

    internal static ResourceProgram Decode(byte[] payload, CancellationToken cancellation)
    {
        payload = ArgumentNullGuard.NotNull(payload, nameof(payload));
        cancellation.ThrowIfCancellationRequested();
        if (payload.Length == 0 || payload.Length > MaximumPayloadBytes)
        { throw new InvalidDataException("Resource program payload size is invalid."); }
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = ResourceExpression.MaximumDepth * 3 + 8 });
            var root = document.RootElement;
            Fields(root, "version", "expressionModel", "costModel", "program");
            if (Integer(root.GetProperty("version")) != ResourceProgram.ModelVersion ||
                Integer(root.GetProperty("expressionModel")) != ResourceExpression.ModelVersion ||
                Text(root.GetProperty("costModel")) != CostModel)
            { throw new InvalidDataException("Resource program model is unsupported."); }
            var remaining = ResourceExpression.MaximumNodes;
            return Read(root.GetProperty("program"), 0, ref remaining, cancellation);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException or ResourceExpressionLimitException)
        { throw new InvalidDataException("Resource program payload is malformed.", exception); }
    }

    private static ResourceProgram Read(JsonElement node, int depth, ref int remaining, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0 || depth >= ResourceExpression.MaximumDepth)
        { throw new ResourceExpressionLimitException(); }
        Fields(node, "kind", "length", "rank", "metadata", "repeatKind", "children");
        var kind = (ResourceProgramKind)Integer(node.GetProperty("kind"));
        if (!Enum.IsDefined(typeof(ResourceProgramKind), kind))
        { throw new InvalidDataException("Resource program kind is undefined."); }
        var repeat = (ResourceRepeatKind)Integer(node.GetProperty("repeatKind"));
        if (!Enum.IsDefined(typeof(ResourceRepeatKind), repeat) || kind != ResourceProgramKind.Repeat && repeat != ResourceRepeatKind.Loop)
        { throw new InvalidDataException("Resource repeat kind is invalid."); }
        var children = node.GetProperty("children");
        var arity = kind is ResourceProgramKind.Zero or ResourceProgramKind.AllocateArray or ResourceProgramKind.MetadataBoundary or ResourceProgramKind.MetadataCall ? 0 : kind == ResourceProgramKind.Repeat ? 1 : 2;
        if (children.ValueKind != JsonValueKind.Array || children.GetArrayLength() != arity)
        { throw new InvalidDataException("Resource program arity is invalid."); }
        var length = node.GetProperty("length");
        var rank = node.GetProperty("rank");
        var metadataNode = node.GetProperty("metadata");
        if (kind != ResourceProgramKind.AllocateArray && length.ValueKind != JsonValueKind.Null ||
            kind != ResourceProgramKind.Repeat && rank.ValueKind != JsonValueKind.Null ||
            kind != ResourceProgramKind.MetadataCall && metadataNode.ValueKind != JsonValueKind.Null)
        { throw new InvalidDataException("Resource program carries an unrelated payload."); }
        if (kind == ResourceProgramKind.MetadataCall)
        {
            Fields(metadataNode, "witness", "catalog", "assembly");
            return ResourceProgram.MetadataCall(new ResourceMetadataReference(Text(metadataNode.GetProperty("witness")),
                Text(metadataNode.GetProperty("catalog")), Text(metadataNode.GetProperty("assembly"))));
        }
        if (kind == ResourceProgramKind.MetadataBoundary)
        { return ResourceProgram.MetadataBoundary; }
        if (kind == ResourceProgramKind.Zero)
        { return ResourceProgram.Zero; }
        if (kind == ResourceProgramKind.AllocateArray)
        { return ResourceProgram.Array(Expression(length, cancellation)); }
        if (kind == ResourceProgramKind.Repeat)
        {
            Fields(rank, "kind", "initial", "limit", "step", "width", "checked");
            var rankKind = (ResourceLoopKind)Integer(rank.GetProperty("kind"));
            var width = Integer(rank.GetProperty("width"));
            var stepText = Text(rank.GetProperty("step"));
            if (!Enum.IsDefined(typeof(ResourceLoopKind), rankKind) || width is not (32 or 64) ||
                !long.TryParse(stepText, NumberStyles.None, CultureInfo.InvariantCulture, out var step) || step <= 0 ||
                step.ToString(CultureInfo.InvariantCulture) != stepText)
            { throw new InvalidDataException("Resource rank shape is invalid."); }
            var checkedNode = rank.GetProperty("checked");
            if (checkedNode.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            { throw new InvalidDataException("Resource rank overflow mode is invalid."); }
            var decodedRank = new ResourceLoopRank(rankKind, Expression(rank.GetProperty("initial"), cancellation),
                Expression(rank.GetProperty("limit"), cancellation), new BigInteger(step), width, checkedNode.GetBoolean());
            return ResourceProgram.Repeat(decodedRank, repeat, Read(children[0], depth + 1, ref remaining, cancellation));
        }
        var left = Read(children[0], depth + 1, ref remaining, cancellation);
        var right = Read(children[1], depth + 1, ref remaining, cancellation);
        return kind == ResourceProgramKind.Sequence ? ResourceProgram.Sequence(left, right) : ResourceProgram.Choice(left, right);
    }

    private static ResourceExpression Expression(JsonElement value, CancellationToken cancellation)
    {
        var text = Text(value);
        var bytes = Convert.FromBase64String(text);
        if (Convert.ToBase64String(bytes) != text)
        { throw new InvalidDataException("Resource expression encoding is not canonical."); }
        return ResourceExpressionCodec.Decode(bytes, cancellation);
    }

    private static void Fields(JsonElement value, params string[] names)
    {
        names = ArgumentNullGuard.NotNull(names, nameof(names));
        if (value.ValueKind != JsonValueKind.Object)
        { throw new InvalidDataException("Resource program record must be an object."); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            { throw new InvalidDataException("Resource program has unknown or duplicate properties."); }
        }
        if (seen.Count != names.Length)
        { throw new InvalidDataException("Resource program properties are missing."); }
    }

    private static int Integer(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        { throw new InvalidDataException("Resource program integer is invalid."); }
        return result;
    }

    private static string Text(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        { throw new InvalidDataException("Resource program text is invalid."); }
        return value.GetString()!;
    }
}
