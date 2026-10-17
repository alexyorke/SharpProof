using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SharpProof.Testing;

internal static class ApiTestEvidence
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal sealed record Row(string Api, string Facet, string Test, string EdgeInputs, string Status, string Detail);

    internal static Row Run(string api, string facet, string test, string edges,
        Func<bool> acceptsDeclared, Func<bool> acceptsWrongClaim)
    {
        if (string.IsNullOrWhiteSpace(edges))
        { return new(api, facet, test, edges, "missing", "No named edge inputs."); }
        try
        {
            if (!acceptsDeclared())
            { return new(api, facet, test, edges, "failed", "Declared behavior failed on edge inputs."); }
            if (acceptsWrongClaim())
            { return new(api, facet, test, edges, "failed", "The behavioral check accepted a deliberately wrong claim."); }
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
        var rows = evidence.OrderBy(row => row.Api, StringComparer.Ordinal)
            .ThenBy(row => row.Facet, StringComparer.Ordinal).ToArray();
        var directory = Path.Combine(root, "artifacts", "api-coverage");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(rows, JsonOptions) + "\n");
        var report = new StringBuilder("# API facet test evidence: ").Append(name).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"Required: {rows.Length}; passed: {rows.Count(row => row.Status == "passed")}; missing: {rows.Count(row => row.Status == "missing")}; failed: {rows.Count(row => row.Status == "failed")}.\n\n")
            .Append("Each passing row executed its declared-behavior check and rejected a deliberately wrong claim on the named inputs. This is bounded test evidence, not a proof for all inputs.\n\n")
            .Append("| API | Facet | Test method | Edge inputs | Status | Detail |\n| --- | --- | --- | --- | --- | --- |\n");
        foreach (var row in rows)
        {
            report.Append("| ").Append(Escape(row.Api)).Append(" | ").Append(Escape(row.Facet))
                .Append(" | ").Append(Escape(row.Test)).Append(" | ").Append(Escape(row.EdgeInputs))
                .Append(" | ").Append(row.Status).Append(" | ").Append(Escape(row.Detail)).Append(" |\n");
        }
        File.WriteAllText(Path.Combine(directory, name + ".md"), report.ToString());
    }

    private static string Escape(string value)
    {
        return value.Replace("|", "&#124;", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }
}
