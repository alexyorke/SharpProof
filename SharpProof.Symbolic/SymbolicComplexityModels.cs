using SharpProof.Attributes;
namespace SharpProof.Symbolic;
internal enum SymbolicComplexityKind {
    Constant,
    Linear,
    Product,
    Quadratic,
    Max,
    Unknown,
    RecursiveUnknown
}
internal enum SymbolicComplexityComparison {
    Within,
    Exceeds,
    Incomparable
}
internal static class SymbolicComplexityFacts {
    internal static bool IsDefinedBound(int value) => Enum.IsDefined(typeof(ComplexityKind), value);
    internal static string GetBoundText(int value) => ((ComplexityKind)value) switch {
        ComplexityKind.Constant => "O(1)",
        ComplexityKind.Logarithmic => "O(log n)",
        ComplexityKind.Linear => "O(n)",
        ComplexityKind.Linearithmic => "O(n log n)",
        ComplexityKind.Quadratic => "O(n^2)",
        ComplexityKind.Product => "O(n * m)",
        ComplexityKind.Max => "O(max(n, m))",
        _ => value.ToString(CultureInfo.InvariantCulture)
    };
    private static bool TryGetBound(SymbolicComplexityKind kind, out ComplexityKind bound) {
        bound = kind switch {
            SymbolicComplexityKind.Constant => ComplexityKind.Constant,
            SymbolicComplexityKind.Linear => ComplexityKind.Linear,
            SymbolicComplexityKind.Quadratic => ComplexityKind.Quadratic,
            SymbolicComplexityKind.Product => ComplexityKind.Product,
            SymbolicComplexityKind.Max => ComplexityKind.Max,
            _ => default
        };
        return kind is SymbolicComplexityKind.Constant or SymbolicComplexityKind.Linear or
            SymbolicComplexityKind.Quadratic or SymbolicComplexityKind.Product or SymbolicComplexityKind.Max;
    }
    internal static SymbolicComplexityComparison Compare(SymbolicComplexityKind actual, int declaredValue) {
        if (!TryGetBound(actual, out var actualBound)) return SymbolicComplexityComparison.Incomparable;
        if (!IsDefinedBound(declaredValue)) return SymbolicComplexityComparison.Incomparable;
        var declared = (ComplexityKind)declaredValue;
        if (actualBound == declared || actualBound == ComplexityKind.Constant)
            return SymbolicComplexityComparison.Within;
        if (declared == ComplexityKind.Constant) return SymbolicComplexityComparison.Exceeds;
        var actualRank = GetChainRank(actualBound);
        var declaredRank = GetChainRank(declared);
        if (actualRank >= 0 && declaredRank >= 0)
            return actualRank <= declaredRank
                ? SymbolicComplexityComparison.Within
                : SymbolicComplexityComparison.Exceeds;
        return SymbolicComplexityComparison.Incomparable;
    }
    private static int GetChainRank(ComplexityKind kind) => kind switch {
        ComplexityKind.Constant => 0,
        ComplexityKind.Logarithmic => 1,
        ComplexityKind.Linear => 2,
        ComplexityKind.Linearithmic => 3,
        ComplexityKind.Quadratic => 4,
        _ => -1
    };
}
internal enum SymbolicComplexityUnknownReason {
    None,
    UnsupportedTarget,
    NoContainingMethodLikeBody,
    UnsupportedLoopShape,
    UnsupportedWhileLoop,
    UnknownCallee,
    ExternalCallee,
    DynamicDispatch,
    RecursiveCycle,
    UnsupportedOperation,
    CancellationRequested,
    Unknown
}
internal sealed record SymbolicComplexityInfo(
    string Text,
    SymbolicComplexityKind Kind,
    bool IsConservative,
    bool IsUnknown,
    bool IsRecursiveUnknown);
internal sealed record SymbolicComplexityDriverInfo(string Kind, string Description, int SourceSpanStart, int SourceSpanLength);
internal sealed record SymbolicComplexityCalleeInfo(
    string MethodDisplayName,
    string ComplexityText,
    SymbolicComplexityKind Kind,
    bool IsConservative,
    SymbolicComplexityUnknownReason UnknownReason);
internal sealed record SymbolicComplexityResult(
    SymbolicComplexityInfo Complexity,
    IReadOnlyList<SymbolicComplexityDriverInfo> Drivers,
    IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons,
    IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries);
