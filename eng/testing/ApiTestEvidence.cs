using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SharpProof.Testing;

internal static class ApiTestEvidence
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    internal sealed record Row(string Api, string Facet, string Test, string EdgeInputs, string Status, string Detail)
    {
        public string Api { get; init; } = Api ?? throw new global::System.ArgumentNullException(nameof(Api));
        public string Facet { get; init; } = Facet ?? throw new global::System.ArgumentNullException(nameof(Facet));
        public string Test { get; init; } = Test ?? throw new global::System.ArgumentNullException(nameof(Test));
        public string EdgeInputs { get; init; } = EdgeInputs ?? throw new global::System.ArgumentNullException(nameof(EdgeInputs));
        public string Status { get; init; } = Status ?? throw new global::System.ArgumentNullException(nameof(Status));
        public string Detail { get; init; } = Detail ?? throw new global::System.ArgumentNullException(nameof(Detail));
    }

    internal static Row Run(string api, string facet, string test, string edges, Func<bool> acceptsDeclared, Func<bool> acceptsWrongClaim)
    {
        global::System.ArgumentNullException.ThrowIfNull(api);
        global::System.ArgumentNullException.ThrowIfNull(facet);
        global::System.ArgumentNullException.ThrowIfNull(test);
        if (string.IsNullOrWhiteSpace(edges))
        {
            return new(api, facet, test, edges, "missing", "No named edge inputs.");
        }

        try
        {
            if (!acceptsDeclared())
            {
                return new(api, facet, test, edges, "failed", "Declared behavior failed on edge inputs.");
            }

            if (acceptsWrongClaim())
            {
                return new(api, facet, test, edges, "failed", "The behavioral check accepted a deliberately wrong claim.");
            }

            return new(api, facet, test, edges, "passed", "Declared behavior accepted; wrong claim rejected.");
        }
#pragma warning disable CA1031 // Report every failed witness before failing the evidence gate.

        catch (Exception exception)
        {
            return new(api, facet, test, edges, "failed", exception.GetType().Name + ": " + exception.Message);
        }
#pragma warning restore CA1031
    }

    internal static void Write(string root, string name, IEnumerable<Row> evidence)
    {
        global::System.ArgumentNullException.ThrowIfNull(root);
        global::System.ArgumentNullException.ThrowIfNull(name);
        global::System.ArgumentNullException.ThrowIfNull(evidence);
        var rows = evidence.OrderBy(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Api), StringComparer.Ordinal).ThenBy(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Facet), StringComparer.Ordinal).ToImmutableArray();
        var directory = Path.Combine(root, "artifacts", "api-coverage");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(rows, JsonOptions) + "\n");
        var report = new StringBuilder("# API facet test evidence: ").Append(name).Append("\n\n").Append(CultureInfo.InvariantCulture, $"Required: {rows.Length}; passed: {rows.Count(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status == "passed"))}; missing: {rows.Count(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status == "missing"))}; failed: {rows.Count(row => row is null ? throw new global::System.ArgumentNullException(nameof(row)) : (row.Status == "failed"))}.\n\n").Append("Each passing row executed its declared-behavior check and rejected a deliberately wrong claim on the named inputs. This is bounded test evidence, not a proof for all inputs.\n\n").Append("| API | Facet | Test method | Edge inputs | Status | Detail |\n| --- | --- | --- | --- | --- | --- |\n");
        foreach (var row in rows)
        {
            report.Append("| ").Append(Escape(row.Api)).Append(" | ").Append(Escape(row.Facet)).Append(" | ").Append(Escape(row.Test)).Append(" | ").Append(Escape(row.EdgeInputs)).Append(" | ").Append(row.Status).Append(" | ").Append(Escape(row.Detail)).Append(" |\n");
        }

        File.WriteAllText(Path.Combine(directory, name + ".md"), report.ToString());
    }

    private static string Escape(string value)
    {
        global::System.ArgumentNullException.ThrowIfNull(value);
        return value.Replace("|", "&#124;", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    }
}
