using System.Text;
using SharpProof.Analyzer.Configuration;

internal static class ConfigurationReferenceCommand
{
    private const string Command = "--generate-configuration-reference";

    public static bool TryRun(string[] args, out int exitCode)
    {
        if (args.Length != 2 || !string.Equals(args[0], Command, StringComparison.Ordinal))
        {
            exitCode = 0;
            return false;
        }

        File.WriteAllText(args[1], Render(), new UTF8Encoding(false));
        exitCode = 0;
        return true;
    }

    internal static string Render()
    {
        var options = AnalyzerConfigurationOptionRegistry.All.OrderBy(static option => option.Key);
        var builder = new StringBuilder()
            .AppendLine("# Analyzer configuration reference")
            .AppendLine()
            .AppendLine("<!-- Generated from ConfigKeys.cs and AnalyzerConfigurationOptionRegistry.cs by scripts/Generate-ConfigurationReference.ps1. -->")
            .AppendLine()
            .AppendLine("SharpProof reads these `sharpproof_*` analyzer options from global AnalyzerConfig and, where noted, per-tree `.editorconfig` sections. Invalid values are reported as `SP0025`; they do not silently change the effective configuration.")
            .AppendLine()
            .AppendLine("Options that alter purity classification policy, plus non-configuration trust sources and precedence, are audited in [Purity Classification Policy](purity-policy.md).")
            .AppendLine()
            .AppendLine("## Option reference")
            .AppendLine()
            .AppendLine("| Key | Scope | Valid values | Default | Related diagnostics | Description |")
            .AppendLine("| --- | --- | --- | --- | --- | --- |");

        foreach (var option in options)
            builder.Append("| `").Append(option.Key).Append("` | ")
                .Append(GetScope(option.Scope)).Append(" | ")
                .Append(GetValueDescription(option)).Append(" | `")
                .Append(option.DefaultValue).Append("` | ")
                .Append(GetRelatedDiagnostics(option.Key)).Append(" | ")
                .Append(option.Description.Replace("|", "\\|", StringComparison.Ordinal)).AppendLine(" |");

        AppendExample(builder, "Global AnalyzerConfig example",
            "Global-only options must be set in a global AnalyzerConfig file. Global-and-tree options can also be set here as defaults before a matching `.editorconfig` override.",
            "is_global = true", options.Where(static option => option.IsGlobal));
        AppendExample(builder, "Per-tree `.editorconfig` example",
            "Only global-and-tree options can be overridden in a per-tree section. Global-only options placed in such a section are invalid and produce `SP0025`.",
            "root = true\n\n[src/**/*.cs]",
            options.Where(static option => option.Scope == AnalyzerConfigurationScope.GlobalAndTree));
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void AppendExample(
        StringBuilder builder,
        string heading,
        string description,
        string preamble,
        IEnumerable<AnalyzerConfigurationOption> options)
    {
        builder.AppendLine().Append("## ").AppendLine(heading).AppendLine()
            .AppendLine(description).AppendLine().AppendLine("```ini").AppendLine(preamble);
        foreach (var option in options)
            builder.Append(option.Key).Append(" = ").AppendLine(GetSampleValue(option));
        builder.AppendLine("```");
    }

    private static string GetScope(AnalyzerConfigurationScope scope) => scope switch
    {
        AnalyzerConfigurationScope.GlobalOnly => "Global-only",
        AnalyzerConfigurationScope.GlobalAndTree => "Global and per-tree",
        AnalyzerConfigurationScope.TreeOnly => "Per-tree",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    private static string GetValueDescription(AnalyzerConfigurationOption option) => option.ValueKind switch
    {
        AnalyzerConfigurationValueKind.Bool => "boolean (`true` or `false`)",
        AnalyzerConfigurationValueKind.StringList => "`;`, `,`, or newline-delimited values",
        AnalyzerConfigurationValueKind.StructuralMemberKeyList =>
            "canonical `spm1\\|...` keys delimited by `;`, `,`, or newlines; property keys end in `.get` or `.set`",
        AnalyzerConfigurationValueKind.NonNegativeInteger => "non-negative integer",
        AnalyzerConfigurationValueKind.PositiveInteger => "positive integer",
        _ when option.AllowedValues.Length != 0 => string.Join(", ", option.AllowedValues.Select(static value => $"`{value}`")),
        _ => "value accepted by the analyzer parser"
    };

    private static string GetRelatedDiagnostics(string key)
    {
        var feature = key switch
        {
            ConfigKeys.KnownImpureMethods or ConfigKeys.KnownPureMethods or
            ConfigKeys.KnownImpureNamespaces or ConfigKeys.KnownImpureTypes or ConfigKeys.PurityProfile
                => "SP0002",
            ConfigKeys.TrustedBoundaryReviewMode => "SP0040",
            ConfigKeys.EmitExplanations => "SP0009",
            ConfigKeys.ReportBclFallbackGuesses => "SP0012",
            ConfigKeys.RuntimeHazardMode => "SP0010, SP0011, SP0033",
            ConfigKeys.SuppressProvenDiagnostics or ConfigKeys.SuppressionDiagnosticIds => "SPS0001-SPS0018",
            ConfigKeys.ReportExceptions => "SP0010",
            ConfigKeys.CheckedExceptions => "SP0011",
            ConfigKeys.EnableEffectSummaryJson => "SP0002, SP0010, SP0011",
            _ when key.StartsWith(ConfigKeys.SuggestMissingEnforcePure, StringComparison.Ordinal) => "SP0004",
            _ when key.StartsWith("sharpproof_smt_", StringComparison.Ordinal) => "SMT-backed proof results",
            _ => "configuration consumers"
        };
        return feature + "; SP0025 for invalid values";
    }

    private static string GetSampleValue(AnalyzerConfigurationOption option) => option.ValueKind switch
    {
        AnalyzerConfigurationValueKind.StringList when option.Key == ConfigKeys.AttributeStubNamespaces =>
            "SharpProof.Attributes; My.Contracts",
        AnalyzerConfigurationValueKind.StringList => "Demo.Namespace.Member",
        AnalyzerConfigurationValueKind.StructuralMemberKeyList =>
            "spm1|RGVtby5OYW1lc3BhY2UuVHlwZQ==|b3JkaW5hcnk=|TWVtYmVy|0|0|bm9uZQ==|bmFtZWQ6U3lzdGVtLlZvaWQ=",
        AnalyzerConfigurationValueKind.NonNegativeInteger => "3",
        AnalyzerConfigurationValueKind.PositiveInteger => "1000",
        AnalyzerConfigurationValueKind.PurityProfile => "balanced",
        AnalyzerConfigurationValueKind.MissingPuritySuggestionScope => "public",
        AnalyzerConfigurationValueKind.RuntimeHazardMode => "all",
        AnalyzerConfigurationValueKind.SmtMode => "deep",
        _ => option.DefaultValue
    };
}
