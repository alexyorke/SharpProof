namespace SharpProof.Analyzer;

// The constraints one OpenAPI parameter schema declares. Minimum and Maximum are
// null when the schema sets no bound. Unknown marks a schema this reader cannot
// read exactly, such as a $ref, a non-integer bound, or a boolean exclusive flag.
internal sealed record OpenApiSchema(long? Minimum, long? Maximum, bool Nullable, bool Unknown);

// The parameter schemas of an OpenAPI 3 document, keyed by HTTP verb and route.
internal sealed class OpenApiDocument
{
    private readonly ImmutableDictionary<string, ImmutableDictionary<string, OpenApiSchema>> _operations;

    internal OpenApiDocument(ImmutableDictionary<string, ImmutableDictionary<string, OpenApiSchema>> operations)
    {
        _operations = RepositoryParameterGuard.NotNull(operations, nameof(operations));
    }

    internal bool TryGetParameter(string verb, string route, string name, out OpenApiSchema schema)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        if (_operations.TryGetValue(Key(verb, route), out var parameters) &&
            parameters.TryGetValue(name, out var found))
        {
            schema = found;
            return true;
        }

        schema = new OpenApiSchema(null, null, false, true);
        return false;
    }

    internal static string Key(string verb, string route)
    {
        verb = RepositoryParameterGuard.NotNull(verb, nameof(verb));
        route = RepositoryParameterGuard.NotNull(route, nameof(route));
        return verb.ToUpperInvariant() + " " + route;
    }
}

// Finds an OpenAPI 3 document among the build's additional files. The reader
// takes the first JSON file, in path order, whose root declares an openapi 3.x
// version. A file that does not parse is skipped, so nothing is compared with it.
internal static class OpenApiDocumentReader
{
    private static readonly ImmutableArray<string> Verbs =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    internal static OpenApiDocument? Find(ImmutableArray<AdditionalText> files, CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.RequireInitialized(files.IsDefault, nameof(files), "Array must be initialized.");
        foreach (var file in files.OrderBy(static file => file.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                file.GetText(cancellationToken)?.ToString() is not { } text ||
                !JsonDocumentParser.TryParse(text, out var root) ||
                root is not Dictionary<string, object?> document ||
                !IsOpenApi3(document))
            {
                continue;
            }

            return Read(document);
        }

        return null;
    }

    private static bool IsOpenApi3(Dictionary<string, object?> document)
    {
        return document.TryGetValue("openapi", out var version) &&
            version is string versionText &&
            versionText.StartsWith("3.", StringComparison.Ordinal);
    }

    private static OpenApiDocument Read(Dictionary<string, object?> document)
    {
        var operations = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, OpenApiSchema>>(StringComparer.Ordinal);
        if (document.TryGetValue("paths", out var paths) && paths is Dictionary<string, object?> pathItems)
        {
            foreach (var entry in pathItems)
            {
                var path = entry.Key;
                if (entry.Value is not Dictionary<string, object?> item)
                {
                    continue;
                }

                foreach (var verb in Verbs)
                {
                    if (item.TryGetValue(verb, out var operation) && operation is Dictionary<string, object?> body)
                    {
                        operations[OpenApiDocument.Key(verb, HttpEntryCatalog.NormalizeRoute(path))] = Parameters(body);
                    }
                }
            }
        }

        return new OpenApiDocument(operations.ToImmutable());
    }

    private static ImmutableDictionary<string, OpenApiSchema> Parameters(Dictionary<string, object?> operation)
    {
        var parameters = ImmutableDictionary.CreateBuilder<string, OpenApiSchema>(StringComparer.OrdinalIgnoreCase);
        if (!operation.TryGetValue("parameters", out var list) || list is not List<object?> items)
        {
            return parameters.ToImmutable();
        }

        foreach (var item in items)
        {
            if (item is not Dictionary<string, object?> parameter ||
                !parameter.TryGetValue("name", out var name) ||
                name is not string parameterName)
            {
                continue;
            }

            parameters[parameterName] = parameter.TryGetValue("schema", out var schema)
                ? ReadSchema(schema)
                : new OpenApiSchema(null, null, false, true);
        }

        return parameters.ToImmutable();
    }

    private static OpenApiSchema ReadSchema(object? value)
    {
        if (value is not Dictionary<string, object?> schema || schema.ContainsKey("$ref"))
        {
            return new OpenApiSchema(null, null, false, true);
        }

        long? minimum = null;
        long? maximum = null;
        var minimumRead = TryBound(schema, "minimum", ref minimum);
        var maximumRead = TryBound(schema, "maximum", ref maximum);
        var unknown = !minimumRead || !maximumRead;

        // OpenAPI 3.1 gives an exclusive bound as a number. In 3.0 a boolean
        // modifies the sibling bound: false changes nothing, and true is not modeled.
        if (schema.TryGetValue("exclusiveMinimum", out var exclusiveMinimum) && !IsFalse(exclusiveMinimum))
        {
            unknown |= !TryExclusive(exclusiveMinimum, ref minimum, 1);
        }

        if (schema.TryGetValue("exclusiveMaximum", out var exclusiveMaximum) && !IsFalse(exclusiveMaximum))
        {
            unknown |= !TryExclusive(exclusiveMaximum, ref maximum, -1);
        }

        var nullable = (schema.TryGetValue("nullable", out var nullableFlag) && nullableFlag is true) ||
            (schema.TryGetValue("type", out var type) && type is List<object?> types && types.Contains("null"));
        return new OpenApiSchema(minimum, maximum, nullable, unknown);
    }

    private static bool TryBound(Dictionary<string, object?> schema, string key, ref long? bound)
    {
        if (!schema.TryGetValue(key, out var value))
        {
            return true;
        }

        if (value is JsonNumber number && TryInteger(number, out var integer))
        {
            bound = integer;
            return true;
        }

        return false;
    }

    private static bool IsFalse(object? value)
    {
        return value is false;
    }

    private static bool TryExclusive(object? value, ref long? bound, long adjustment)
    {
        if (value is not JsonNumber number || !TryInteger(number, out var integer))
        {
            return false;
        }

        // An exclusive bound at the edge of long has no adjacent integer to admit.
        if (adjustment > 0 ? integer == long.MaxValue : integer == long.MinValue)
        {
            return false;
        }

        bound = integer + adjustment;
        return true;
    }

    private static bool TryInteger(JsonNumber number, out long value)
    {
        value = 0;
        return number.Text.IndexOfAny(['.', 'e', 'E']) < 0 &&
            long.TryParse(number.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }
}
