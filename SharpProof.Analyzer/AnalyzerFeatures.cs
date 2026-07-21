namespace SharpProof.Analyzer;

[Flags]
internal enum AnalyzerFeatures {
    None = 0,
    Purity = 1 << 0,
    Allocation = 1 << 1,
    Capability = 1 << 2,
    Requires = 1 << 3,
    Ensures = 1 << 4,
    Complexity = 1 << 5,
    Exceptions = 1 << 6,
    Nullability = 1 << 7,

    Callable = Purity | Allocation | Capability | Requires | Ensures | Complexity | Exceptions | Nullability,
    All = Callable
}

internal static class AnalyzerFeatureDependencies {
    internal static bool Includes(
        this AnalyzerFeatures features,
        AnalyzerFeatures feature) {
        return (features & feature) == feature;
    }
}
