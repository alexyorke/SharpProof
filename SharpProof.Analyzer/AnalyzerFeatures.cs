namespace SharpProof.Analyzer;

[Flags]
internal enum AnalyzerFeatures
{
    None = 0,
    PurityCore = 1 << 0,
    Purity = 1 << 1,
    Allocation = 1 << 2,
    Capability = 1 << 3,
    Requires = 1 << 4,
    Ensures = 1 << 5,
    Complexity = 1 << 6,
    Exceptions = 1 << 7,
    Placement = 1 << 8,
    Suggestions = 1 << 9,

    Callable = Purity | Allocation | Capability | Requires | Ensures | Complexity | Exceptions | Suggestions,
    All = PurityCore | Callable | Placement
}

internal static class AnalyzerFeatureDependencies
{
    internal static AnalyzerFeatures Expand(AnalyzerFeatures features)
    {
        if ((features & (AnalyzerFeatures.Purity |
                         AnalyzerFeatures.Requires |
                         AnalyzerFeatures.Ensures |
                         AnalyzerFeatures.Exceptions |
                         AnalyzerFeatures.Suggestions)) != 0)
            features |= AnalyzerFeatures.PurityCore;

        return features;
    }

    internal static bool Includes(
        this AnalyzerFeatures features,
        AnalyzerFeatures feature)
    {
        return (features & feature) == feature;
    }
}
