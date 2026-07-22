using System.Text.Json;
using System.Text.Json.Serialization;
using SharpProof.Symbolic;
const string usage = """
Usage:
  sharpproof analyze --file <path> --target <line:N[:column]|position:N|span:start:end|all-lines>
    --facets <effects,proofs,hazards,complexity> [--condition <text>] [--format <text|json>]
    [--fail-on-unknown] [--fail-on-disproven]
""";
try {
    if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal)) {
        Console.WriteLine(usage);
        return args.Length == 0 ? 2 : 0;
    }
    if (!string.Equals(args[0], "analyze", StringComparison.Ordinal)) return Usage("Expected 'analyze'.");
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    var flags = new HashSet<string>(StringComparer.Ordinal);
    for (var index = 1; index < args.Length; index++) {
        var argument = args[index];
        if (argument is "--fail-on-unknown" or "--fail-on-disproven") {
            flags.Add(argument);
            continue;
        }
        if (argument is not ("--file" or "--target" or "--facets" or "--condition" or "--format"))
            return Usage("Unknown option: " + argument);
        if (++index >= args.Length) return Usage("Missing value for " + argument + ".");
        values[argument] = args[index];
    }
    if (!values.TryGetValue("--file", out var file) || string.IsNullOrWhiteSpace(file))
        return Usage("--file is required.");
    file = Path.GetFullPath(file);
    if (!File.Exists(file)) return Usage("Input file does not exist: " + file);
    if (!values.TryGetValue("--target", out var targetText) || !TryParseTarget(targetText, out var target))
        return Usage("--target must be line:N[:column], position:N, span:start:end, or all-lines.");
    if (!values.TryGetValue("--facets", out var facetsText) || !TryParseFacets(facetsText, out var facets))
        return Usage("--facets must contain effects, proofs, hazards, or complexity.");
    var format = values.TryGetValue("--format", out var selectedFormat) ? selectedFormat : "text";
    if (format is not ("text" or "json")) return Usage("--format must be text or json.");
    using var session = SharpProofAnalysisSession.FromFile(file);
    var result = session.Analyze(new SharpProofAnalysisRequest(target, facets, values.GetValueOrDefault("--condition")));
    if (result.Status is SharpProofQueryStatus.Failed or SharpProofQueryStatus.Canceled) {
        Console.Error.WriteLine(result.Error?.Message ?? "Analysis failed.");
        return 3;
    }
    if (format == "json") {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        }));
    }
    else {
        Console.WriteLine($"Status: {result.Status}");
        if (result.MethodEffects != null) {
            Console.WriteLine($"Effects: {result.MethodEffects.Effects}");
            Console.WriteLine($"Capabilities: {result.MethodEffects.Capabilities}");
            Console.WriteLine($"Purity: {result.MethodEffects.Purity}");
            Console.WriteLine($"Allocation-free: {result.MethodEffects.AllocationFree}");
            Console.WriteLine($"Does-not-throw: {result.MethodEffects.DoesNotThrow}");
            foreach (var site in result.MethodEffects.Sites)
                Console.WriteLine($"  effect {site.Reason} at {site.SpanStart}: {site.Operation}");
        }
        foreach (var fact in result.ProofFacts)
            Console.WriteLine($"Proof: {fact.Condition}: {fact.Status} ({fact.Reason})");
        foreach (var hazard in result.Hazards)
            Console.WriteLine($"Hazard: {hazard.Kind} {hazard.Status}: {hazard.Operation}");
        if (result.Complexity != null) Console.WriteLine("Complexity: " + result.Complexity);
        foreach (var reason in result.UnknownReasons)
            Console.WriteLine($"Unknown: {reason.Code}: {reason.Message}");
    }
    var verdicts = result.MethodEffects == null
        ? Array.Empty<SharpProofVerdict>()
        : [
            result.MethodEffects.Purity,
            result.MethodEffects.AllocationFree,
            result.MethodEffects.DoesNotThrow
        ];
    if (flags.Contains("--fail-on-disproven") && verdicts.Contains(SharpProofVerdict.Disproven))
        return 5;
    if (flags.Contains("--fail-on-unknown") &&
        (result.Status == SharpProofQueryStatus.Unknown || !result.UnknownReasons.IsDefaultOrEmpty ||
         verdicts.Contains(SharpProofVerdict.Unknown)))
        return 4;
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException) {
    Console.Error.WriteLine(exception.Message);
    return 3;
}
int Usage(string message) {
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(usage);
    return 2;
}
bool TryParseTarget(string value, out SharpProofTarget target) {
    target = null!;
    if (value == "all-lines") {
        target = new SharpProofTarget(SharpProofTargetKind.AllLines);
        return true;
    }
    var parts = value.Split(':');
    if (parts.Length is 2 or 3 && parts[0] == "line" && int.TryParse(parts[1], out var line) && line > 0) {
        var column = 1;
        if (parts.Length == 3 && (!int.TryParse(parts[2], out column) || column <= 0)) return false;
        target = new SharpProofTarget(
            parts.Length == 3 ? SharpProofTargetKind.Point : SharpProofTargetKind.Line,
            Line: line,
            Column: column);
        return true;
    }
    if (parts.Length == 2 && parts[0] == "position" && int.TryParse(parts[1], out var position) && position >= 0) {
        target = new SharpProofTarget(SharpProofTargetKind.Position, Position: position);
        return true;
    }
    if (parts.Length == 3 && parts[0] == "span" &&
        int.TryParse(parts[1], out var start) && int.TryParse(parts[2], out var end) && start >= 0 && end >= start) {
        target = new SharpProofTarget(SharpProofTargetKind.Span, SpanStart: start, SpanEnd: end);
        return true;
    }
    return false;
}
bool TryParseFacets(string value, out SharpProofAnalysisFacet facets) {
    facets = SharpProofAnalysisFacet.None;
    foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
        facets |= item switch {
            "effects" => SharpProofAnalysisFacet.Effects,
            "proofs" => SharpProofAnalysisFacet.ProofFacts,
            "hazards" => SharpProofAnalysisFacet.RuntimeHazards,
            "complexity" => SharpProofAnalysisFacet.Complexity,
            _ => SharpProofAnalysisFacet.None
        };
        if (item is not ("effects" or "proofs" or "hazards" or "complexity")) return false;
    }
    return facets != SharpProofAnalysisFacet.None;
}
