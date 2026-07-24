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
    private static readonly string[] BoundTexts =
        ["O(1)", "O(n)", "O(n^2)", "O(log n)", "O(n log n)", "O(n * m)", "O(max(n, m))"];

    internal static bool IsDefinedBound(int value) => Enum.IsDefined(typeof(ComplexityKind), value);
    internal static string GetBoundText(int value) =>
        IsDefinedBound(value) ? BoundTexts[value] : value.ToString(CultureInfo.InvariantCulture);

    internal static SymbolicComplexityComparison Compare(SymbolicComplexityKind actual, int declaredValue) {
        var bound = actual switch {
            SymbolicComplexityKind.Constant => ComplexityKind.Constant,
            SymbolicComplexityKind.Linear => ComplexityKind.Linear,
            SymbolicComplexityKind.Quadratic => ComplexityKind.Quadratic,
            SymbolicComplexityKind.Product => ComplexityKind.Product,
            SymbolicComplexityKind.Max => ComplexityKind.Max,
            _ => (ComplexityKind?)null
        };
        if (bound is not { } actualBound || !IsDefinedBound(declaredValue))
            return SymbolicComplexityComparison.Incomparable;
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

internal sealed record ComplexityValue(
    ImmutableSortedDictionary<string, int>? Factors = null,
    ImmutableArray<ComplexityValue> Alternatives = default,
    SymbolicComplexityUnknownReason UnknownReason = SymbolicComplexityUnknownReason.None,
    bool IsRecursive = false) {
    internal static readonly ComplexityValue Constant = new(
        ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal));
    internal bool IsUnknown => IsRecursive || UnknownReason != SymbolicComplexityUnknownReason.None;
    internal bool IsConstant => !IsUnknown && Alternatives.IsDefaultOrEmpty && Factors?.Count == 0;
    internal SymbolicComplexityKind Kind => IsRecursive ? SymbolicComplexityKind.RecursiveUnknown :
        UnknownReason != SymbolicComplexityUnknownReason.None ? SymbolicComplexityKind.Unknown :
        !Alternatives.IsDefaultOrEmpty ? SymbolicComplexityKind.Max :
        Factors?.Count == 0 ? SymbolicComplexityKind.Constant :
        Factors?.Count == 1 && Factors.Single().Value == 1 ? SymbolicComplexityKind.Linear :
        Factors?.Count == 1 && Factors.Single().Value == 2 ? SymbolicComplexityKind.Quadratic :
        SymbolicComplexityKind.Product;

    internal static ComplexityValue Variable(string key) => new(Constant.Factors!.Add(key, 1));
    internal static ComplexityValue Unknown(SymbolicComplexityUnknownReason reason) => new(UnknownReason: reason);
    internal static ComplexityValue Recursive() =>
        new(UnknownReason: SymbolicComplexityUnknownReason.RecursiveCycle, IsRecursive: true);

    internal static ComplexityValue Max(ComplexityValue left, ComplexityValue right) {
        if (left.IsRecursive || right.IsRecursive) return Recursive();
        if (left.IsUnknown || right.IsUnknown) return left.IsUnknown ? left : right;
        if (Dominates(left, right)) return left;
        if (Dominates(right, left)) return right;
        var alternatives = Expand(left).Concat(Expand(right)).Distinct().ToImmutableArray();
        return alternatives.Length == 1 ? alternatives[0] : new ComplexityValue(Alternatives: alternatives);
    }

    internal static ComplexityValue Multiply(ComplexityValue left, ComplexityValue right) {
        if (left.IsRecursive || right.IsRecursive) return Recursive();
        if (left.IsUnknown || right.IsUnknown) return left.IsUnknown ? left : right;
        if (!left.Alternatives.IsDefaultOrEmpty)
            return left.Alternatives.Select(item => Multiply(item, right)).Aggregate(Max);
        if (!right.Alternatives.IsDefaultOrEmpty)
            return right.Alternatives.Select(item => Multiply(left, item)).Aggregate(Max);
        var factors = left.Factors ?? Constant.Factors!;
        foreach (var pair in right.Factors ?? Constant.Factors!)
            factors = factors.SetItem(pair.Key,
                factors.TryGetValue(pair.Key, out var exponent) ? exponent + pair.Value : pair.Value);
        return new ComplexityValue(factors);
    }

    internal ComplexityValue Substitute(Func<string, ComplexityValue?> resolve) {
        if (IsUnknown) return this;
        if (!Alternatives.IsDefaultOrEmpty)
            return Alternatives.Select(item => item.Substitute(resolve)).Aggregate(Max);
        var result = Constant;
        foreach (var pair in Factors ?? Constant.Factors!) {
            var factor = resolve(pair.Key) ?? Variable(pair.Key);
            for (var index = 0; index < pair.Value; index++) result = Multiply(result, factor);
        }
        return result;
    }

    internal string Text(IMethodSymbol? method) => "O(" + Term(method) + ")";

    internal static bool TryParseParameterKey(string key, out int ordinal) {
        var colon = key.IndexOf(':');
        return int.TryParse(key.StartsWith("$p", StringComparison.Ordinal) && colon > 2
            ? key.Substring(2, colon - 2)
            : null, out ordinal);
    }

    private string Term(IMethodSymbol? method) {
        if (IsRecursive) return "RecursiveUnknown";
        if (UnknownReason != SymbolicComplexityUnknownReason.None) return "Unknown";
        if (!Alternatives.IsDefaultOrEmpty)
            return "max(" + string.Join(", ", Alternatives.Select(item => item.Term(method))) + ")";
        if (Factors?.Count == 0) return "1";
        return string.Join(" * ", Factors!.Select(pair => Render(pair.Key, method) +
            (pair.Value == 1 ? string.Empty : "^" + pair.Value.ToString(CultureInfo.InvariantCulture))));
    }

    private static string Render(string key, IMethodSymbol? method) {
        if (TryParseParameterKey(key, out var ordinal)) {
            var name = method != null && ordinal < method.Parameters.Length
                ? method.Parameters[ordinal].Name
                : "p" + ordinal.ToString(CultureInfo.InvariantCulture);
            return key.EndsWith(":length", StringComparison.Ordinal) ? name + ".Length" : name;
        }
        return key.StartsWith("name:", StringComparison.Ordinal) ? key.Substring(5) : key;
    }

    private static IEnumerable<ComplexityValue> Expand(ComplexityValue value) =>
        value.Alternatives.IsDefaultOrEmpty ? [value] : value.Alternatives;

    private static bool Dominates(ComplexityValue left, ComplexityValue right) {
        if (!left.Alternatives.IsDefaultOrEmpty || !right.Alternatives.IsDefaultOrEmpty) return false;
        if (left.Factors?.Count == 0) return right.Factors?.Count == 0;
        if (right.Factors?.Count == 0) return true;
        return right.Factors!.All(pair =>
            left.Factors!.TryGetValue(pair.Key, out var exponent) && exponent >= pair.Value);
    }
}

internal sealed record ComplexitySummary(
    ComplexityValue Cost,
    IReadOnlyList<SymbolicComplexityDriverInfo> Drivers,
    IReadOnlyList<SymbolicComplexityUnknownReason> Reasons,
    IReadOnlyList<SymbolicComplexityCalleeInfo> Callees) {
    internal static readonly ComplexitySummary Constant = new(ComplexityValue.Constant, [], [], []);
    internal ComplexitySummary WithDriver(SymbolicComplexityDriverInfo driver) =>
        this with { Drivers = [.. Drivers, driver] };

    internal static ComplexitySummary Sequence(params ComplexitySummary[] parts) => Sequence(parts.AsEnumerable());
    internal static ComplexitySummary Sequence(IEnumerable<ComplexitySummary> parts) {
        var values = parts.ToArray();
        return new(
            values.Select(static part => part.Cost).Prepend(ComplexityValue.Constant).Aggregate(ComplexityValue.Max),
            [.. values.SelectMany(static part => part.Drivers)],
            [.. values.SelectMany(static part => part.Reasons)],
            [.. values.SelectMany(static part => part.Callees)]);
    }

    internal static ComplexitySummary Multiply(ComplexityValue factor, ComplexitySummary value) =>
        value with { Cost = ComplexityValue.Multiply(factor, value.Cost) };

    internal static ComplexitySummary Unknown(
        SymbolicComplexityUnknownReason reason,
        SyntaxNode syntax,
        params ComplexitySummary[] parts) {
        var combined = Sequence(parts);
        return new(ComplexityValue.Unknown(reason),
            [.. combined.Drivers, Driver("Unknown", reason.ToString(), syntax)],
            [reason, .. combined.Reasons], combined.Callees);
    }

    internal static ComplexitySummary UnknownCallee(
        IMethodSymbol method,
        SymbolicComplexityUnknownReason reason,
        SyntaxNode syntax,
        bool includeUnknownCallee = false) =>
        new(ComplexityValue.Unknown(reason), [Driver("Unknown", reason.ToString(), syntax)],
            includeUnknownCallee ? [reason, SymbolicComplexityUnknownReason.UnknownCallee] : [reason],
            [Callee(method, ComplexityValue.Unknown(reason), reason)]);

    internal static SymbolicComplexityDriverInfo Driver(string kind, string description, SyntaxNode syntax) =>
        new(kind, description, syntax.SpanStart, syntax.Span.Length);

    internal static SymbolicComplexityCalleeInfo Callee(
        IMethodSymbol method,
        ComplexityValue cost,
        SymbolicComplexityUnknownReason reason) =>
        new(method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            cost.Text(method), cost.Kind, cost.IsUnknown, reason);

    internal SymbolicComplexityResult ToResult(IMethodSymbol method) => new(
        new(Cost.Text(method), Cost.Kind, Cost.IsUnknown, Cost.IsUnknown, Cost.IsRecursive),
        Drivers.Distinct().ToArray(),
        Reasons.Where(static reason => reason != SymbolicComplexityUnknownReason.None).Distinct().ToArray(),
        Callees.Distinct().ToArray());
}

internal sealed record ComplexityLoopModel(
    ComplexitySummary Prefix,
    ComplexitySummary Iteration,
    ComplexityValue Bound,
    string DriverKind,
    string Label,
    string Description,
    SyntaxNode Syntax) {
    internal ComplexitySummary Apply(IMethodSymbol method) =>
        ComplexitySummary.Sequence(Prefix, ComplexitySummary.Multiply(Bound, Iteration).WithDriver(
            ComplexitySummary.Driver(DriverKind,
                Label + " bound " + Bound.Text(method) + " from " + Description,
                Syntax)));
}
