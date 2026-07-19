namespace SharpProof.Symbolic;

internal static class SmtConfigurationValueRegistry
{
    internal static ImmutableArray<string> AllowedModes { get; } =
        ImmutableArray.Create("disabled", "bounded", "deep");

    internal static bool TryParseMode(string? value, out SmtAnalysisMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "disabled":
                mode = SmtAnalysisMode.Off;
                return true;
            case "bounded":
                mode = SmtAnalysisMode.Bounded;
                return true;
            case "deep":
                mode = SmtAnalysisMode.Deep;
                return true;
            default:
                mode = default;
                return false;
        }
    }
}
