using System.Globalization;
using System.Numerics;
using System.Text.Json;
using SharpProof.Ir;

namespace SharpProof.CompilerArtifact;

// This validates arithmetic syntax and budgets, not a resource claim. Native
// callers must bind every input to the owned callable and verify its proof.
internal static class ResourceExpressionCodec
{
    internal const int MaximumPayloadBytes = 1024 * 1024;
    internal static ResourceExpression DecodeForCallable(byte[] payload, string callableId,
        CompilerTotalEntryPreparation preparation, CancellationToken cancellation)
    {
        payload = ArgumentNullGuard.NotNull(payload, nameof(payload));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        preparation = ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        var expression = Decode(payload, cancellation);
        ResourceExpressionOwnership.Validate(expression, callableId, preparation, cancellation);
        return expression;
    }

    internal static ResourceExpression DecodeForCallable(byte[] payload, string callableId,
        CompilerTotalCallablePreparation preparation, CancellationToken cancellation)
    {
        payload = ArgumentNullGuard.NotNull(payload, nameof(payload));
        callableId = ArgumentNullGuard.NotNull(callableId, nameof(callableId));
        preparation = ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        var expression = Decode(payload, cancellation);
        ResourceExpressionOwnership.Validate(expression, callableId, preparation, cancellation);
        return expression;
    }

    internal static byte[] Encode(ResourceExpression expression, CancellationToken cancellation)
    {
        expression = ArgumentNullGuard.NotNull(expression, nameof(expression));
        cancellation.ThrowIfCancellationRequested();
        // Immutable construction already bounds expanded node count and depth.
        // The writer additionally bounds the aggregate encoded payload.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", ResourceExpression.ModelVersion);
            writer.WritePropertyName("expression");
            WriteNode(writer, expression, cancellation);
            writer.WriteEndObject();
            writer.Flush();
        }
        return stream.ToArray();
    }

    private static void WriteNode(Utf8JsonWriter writer, ResourceExpression expression, CancellationToken cancellation)
    {
        writer = ArgumentNullGuard.NotNull(writer, nameof(writer));
        expression = ArgumentNullGuard.NotNull(expression, nameof(expression));
        cancellation.ThrowIfCancellationRequested();

        writer.WriteStartObject();
        writer.WriteNumber("kind", (int)expression.Kind);
        writer.WriteString("number", expression.Number.ToString(CultureInfo.InvariantCulture));
        writer.WritePropertyName("input");
        if (expression.Input is { } input)
        {
            writer.WriteStartObject();
            writer.WriteNumber("ordinal", input.Ordinal);
            writer.WriteNumber("kind", (int)input.Kind);
            writer.WriteString("name", input.DisplayName);
            writer.WriteString("maximum", input.Maximum.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }
        else
        { writer.WriteNullValue(); }
        writer.WriteStartArray("operands");
        foreach (var operand in expression.Operands)
        { WriteNode(writer, operand, cancellation); }
        writer.WriteEndArray();
        writer.WriteEndObject();
        // Reserve the final byte for the enclosing envelope object.
        if (writer.BytesCommitted + writer.BytesPending >= MaximumPayloadBytes)
        { throw new ResourceExpressionLimitException(); }
    }

    internal static ResourceExpression Decode(byte[] payload, CancellationToken cancellation)
    {
        payload = ArgumentNullGuard.NotNull(payload, nameof(payload));
        cancellation.ThrowIfCancellationRequested();
        if (payload.Length == 0 || payload.Length > MaximumPayloadBytes)
        { throw new InvalidDataException("Resource expression payload size is invalid."); }
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = ResourceExpression.MaximumDepth * 3 + 8 });
            var root = document.RootElement;
            Properties(root, "version", "expression");
            if (Integer(root.GetProperty("version")) != ResourceExpression.ModelVersion)
            { throw new InvalidDataException("Resource expression model version is unsupported."); }
            var remaining = ResourceExpression.MaximumNodes;
            return ReadNode(root.GetProperty("expression"), 0, ref remaining, cancellation);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException or ResourceExpressionLimitException)
        {
            throw new InvalidDataException("Resource expression payload is malformed.", exception);
        }
    }

    private static ResourceExpression ReadNode(JsonElement node, int depth, ref int remaining, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (--remaining < 0 || depth >= ResourceExpression.MaximumDepth)
        { throw new ResourceExpressionLimitException(); }
        Properties(node, "kind", "number", "input", "operands");
        var kind = (ResourceExpressionKind)Integer(node.GetProperty("kind"));
        if (kind is not (ResourceExpressionKind.Constant or ResourceExpressionKind.Input or ResourceExpressionKind.Add or
            ResourceExpressionKind.Multiply or ResourceExpressionKind.Maximum or ResourceExpressionKind.PositiveDifference or
            ResourceExpressionKind.CeilingDivide or ResourceExpressionKind.Digits or ResourceExpressionKind.CeilingLog or ResourceExpressionKind.Align))
        { throw new InvalidDataException("Resource expression kind is undefined."); }
        var number = Natural(node.GetProperty("number"));
        var operands = node.GetProperty("operands");
        if (operands.ValueKind != JsonValueKind.Array)
        { throw new InvalidDataException("Resource expression operands must be an array."); }
        var expected = kind is ResourceExpressionKind.Constant or ResourceExpressionKind.Input ? 0 :
            kind is ResourceExpressionKind.Add or ResourceExpressionKind.Multiply or ResourceExpressionKind.Maximum ? 2 : 1;
        if (operands.GetArrayLength() != expected)
        { throw new InvalidDataException("Resource expression arity is invalid."); }
        var inputNode = node.GetProperty("input");
        if (kind == ResourceExpressionKind.Input)
        {
            if (number != 0)
            { throw new InvalidDataException("Input nodes cannot carry a numeric operation."); }
            Properties(inputNode, "ordinal", "kind", "name", "maximum");
            return ResourceExpression.InputSize(new ResourceInput(Integer(inputNode.GetProperty("ordinal")),
                (ResourceInputKind)Integer(inputNode.GetProperty("kind")), Text(inputNode.GetProperty("name")), Natural(inputNode.GetProperty("maximum"))));
        }
        if (inputNode.ValueKind != JsonValueKind.Null)
        { throw new InvalidDataException("Only input nodes may carry input identities."); }
        if (kind == ResourceExpressionKind.Constant)
        { return ResourceExpression.Literal(number); }
        if (expected == 2 && number != 0)
        { throw new InvalidDataException("Binary nodes cannot carry a numeric operation."); }
        var left = ReadNode(operands[0], depth + 1, ref remaining, cancellation);
        if (expected == 2)
        {
            var right = ReadNode(operands[1], depth + 1, ref remaining, cancellation);
            return kind switch
            {
                ResourceExpressionKind.Add => ResourceExpression.Add(left, right),
                ResourceExpressionKind.Multiply => ResourceExpression.Multiply(left, right),
                _ => ResourceExpression.Max(left, right)
            };
        }
        return kind switch
        {
            ResourceExpressionKind.PositiveDifference => ResourceExpression.Difference(left, number),
            ResourceExpressionKind.CeilingDivide => ResourceExpression.DivideCeiling(left, number),
            ResourceExpressionKind.Digits => ResourceExpression.Geometric(left, number, true, cancellation),
            ResourceExpressionKind.CeilingLog => ResourceExpression.Geometric(left, number, false, cancellation),
            _ => ResourceExpression.Aligned(left, number)
        };
    }

    private static void Properties(JsonElement value, params string[] names)
    {
        names = ArgumentNullGuard.NotNull(names, nameof(names));
        if (value.ValueKind != JsonValueKind.Object)
        { throw new InvalidDataException("Resource expression record must be an object."); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            { throw new InvalidDataException("Resource expression has unknown or duplicate properties."); }
        }
        if (seen.Count != names.Length)
        { throw new InvalidDataException("Resource expression properties are missing."); }
    }

    private static int Integer(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        { throw new InvalidDataException("Resource expression integer is invalid."); }
        return result;
    }

    private static string Text(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        { throw new InvalidDataException("Resource expression text is invalid."); }
        return value.GetString()!;
    }

    private static BigInteger Natural(JsonElement value)
    {
        var text = Text(value);
        if (text.Length == 0 || text.Length > ResourceExpression.MaximumTextLength ||
            text.Length > 1 && text[0] == '0' || text.Any(static character => character is < '0' or > '9'))
        { throw new InvalidDataException("Resource expression natural number is not canonical."); }
        return BigInteger.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
