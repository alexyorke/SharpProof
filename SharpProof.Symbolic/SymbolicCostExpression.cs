namespace SharpProof.Symbolic;

internal sealed class SymbolicCostExpression
{
    private SymbolicCostExpression(
        CostNodeKind kind,
        ImmutableSortedDictionary<string, int>? factors = null,
        ImmutableArray<SymbolicCostExpression>? alternatives = null,
        SymbolicComplexityUnknownReason unknownReason = SymbolicComplexityUnknownReason.None)
    {
        Kind = kind;
        Factors = factors ?? ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
        Alternatives = alternatives ?? ImmutableArray<SymbolicCostExpression>.Empty;
        UnknownReason = unknownReason;
    }

    private CostNodeKind Kind { get; }

    private ImmutableSortedDictionary<string, int> Factors { get; }

    private ImmutableArray<SymbolicCostExpression> Alternatives { get; }

    public SymbolicComplexityUnknownReason UnknownReason { get; }

    public bool IsUnknown => Kind == CostNodeKind.Unknown;

    public bool IsRecursiveUnknown => Kind == CostNodeKind.RecursiveUnknown;

    public bool IsConservative => IsUnknown || IsRecursiveUnknown || (Kind == CostNodeKind.Max &&
                                                                      Alternatives.Any(static alternative =>
                                                                          alternative.IsConservative));

    public bool IsConstant => Kind == CostNodeKind.Monomial && Factors.Count == 0;

    public static SymbolicCostExpression Constant()
    {
        return new SymbolicCostExpression(CostNodeKind.Monomial);
    }

    public static SymbolicCostExpression Variable(string key)
    {
        return new SymbolicCostExpression(
            CostNodeKind.Monomial,
            ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal).Add(key, 1));
    }

    public static SymbolicCostExpression Unknown(SymbolicComplexityUnknownReason reason)
    {
        return new SymbolicCostExpression(CostNodeKind.Unknown, unknownReason: reason);
    }

    public static SymbolicCostExpression RecursiveUnknown()
    {
        return new SymbolicCostExpression(CostNodeKind.RecursiveUnknown,
            unknownReason: SymbolicComplexityUnknownReason.RecursiveCycle);
    }

    public static SymbolicCostExpression Max(IEnumerable<SymbolicCostExpression> expressions)
    {
        if (expressions == null) return Constant();

        var flattened = new List<SymbolicCostExpression>();
        foreach (var expression in expressions.Where(static expression => expression != null))
        {
            if (expression.IsRecursiveUnknown) return RecursiveUnknown();

            if (expression.IsUnknown) return Unknown(expression.UnknownReason);

            if (expression.Kind == CostNodeKind.Max)
                flattened.AddRange(expression.Alternatives);
            else
                flattened.Add(expression);
        }

        if (flattened.Count == 0) return Constant();

        var reduced = new List<SymbolicCostExpression>();
        foreach (var expression in flattened)
        {
            if (reduced.Any(existing => existing.Equals(expression))) continue;

            if (reduced.Any(existing => Dominates(existing, expression))) continue;

            reduced.RemoveAll(existing => Dominates(expression, existing));
            reduced.Add(expression);
        }

        if (reduced.Count == 1) return reduced[0];

        return new SymbolicCostExpression(CostNodeKind.Max, alternatives: reduced.ToImmutableArray());
    }

    public static SymbolicCostExpression Multiply(SymbolicCostExpression left, SymbolicCostExpression right)
    {
        if (left.IsRecursiveUnknown || right.IsRecursiveUnknown) return RecursiveUnknown();

        if (left.IsUnknown) return Unknown(left.UnknownReason);

        if (right.IsUnknown) return Unknown(right.UnknownReason);

        if (left.Kind == CostNodeKind.Max)
            return Max(left.Alternatives.Select(alternative => Multiply(alternative, right)));

        if (right.Kind == CostNodeKind.Max)
            return Max(right.Alternatives.Select(alternative => Multiply(left, alternative)));

        var factors = left.Factors;
        foreach (var pair in right.Factors)
            factors = factors.SetItem(
                pair.Key,
                factors.TryGetValue(pair.Key, out var exponent) ? exponent + pair.Value : pair.Value);

        return new SymbolicCostExpression(CostNodeKind.Monomial, factors);
    }

    public SymbolicCostExpression Substitute(Func<string, SymbolicCostExpression?> resolver)
    {
        if (resolver == null) throw new ArgumentNullException(nameof(resolver));

        if (Kind == CostNodeKind.Max)
            return Max(Alternatives.Select(alternative => alternative.Substitute(resolver)));

        if (Kind != CostNodeKind.Monomial) return this;

        var preservedFactors = ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal);
        var substituted = Constant();
        foreach (var pair in Factors)
        {
            var resolved = resolver(pair.Key);
            if (resolved == null)
            {
                preservedFactors = preservedFactors.SetItem(pair.Key, pair.Value);
                continue;
            }

            var accumulated = Constant();
            for (var index = 0; index < pair.Value; index++) accumulated = Multiply(accumulated, resolved);

            substituted = Multiply(substituted, accumulated);
        }

        if (preservedFactors.Count != 0)
            substituted = Multiply(substituted,
                new SymbolicCostExpression(CostNodeKind.Monomial, preservedFactors));

        return substituted;
    }

    public string ToBigOText(IMethodSymbol? contextMethod = null)
    {
        return "O(" + ToTermText(contextMethod) + ")";
    }

    public SymbolicComplexityKind ToPublicKind()
    {
        if (IsRecursiveUnknown) return SymbolicComplexityKind.RecursiveUnknown;

        if (IsUnknown) return SymbolicComplexityKind.Unknown;

        if (Kind == CostNodeKind.Max) return SymbolicComplexityKind.Max;

        if (Factors.Count == 0) return SymbolicComplexityKind.Constant;

        if (Factors.Count == 1)
        {
            var factor = Factors.Single();
            return factor.Value switch
            {
                1 => SymbolicComplexityKind.Linear,
                2 => SymbolicComplexityKind.Quadratic,
                _ => SymbolicComplexityKind.Product
            };
        }

        return SymbolicComplexityKind.Product;
    }

    private string ToTermText(IMethodSymbol? contextMethod)
    {
        if (IsRecursiveUnknown) return "RecursiveUnknown";

        if (IsUnknown) return "Unknown";

        if (Kind == CostNodeKind.Max)
            return "max(" + string.Join(", ",
                Alternatives.Select(alternative => alternative.ToTermText(contextMethod))) + ")";

        if (Factors.Count == 0) return "1";

        return string.Join(
            " * ",
            Factors.Select(pair => pair.Value == 1
                ? RenderVariable(pair.Key, contextMethod)
                : RenderVariable(pair.Key, contextMethod) + "^" +
                  pair.Value.ToString(CultureInfo.InvariantCulture)));
    }

    private static string RenderVariable(string key, IMethodSymbol? contextMethod)
    {
        if (key.StartsWith("$p", StringComparison.Ordinal))
        {
            var colonIndex = key.IndexOf(':');
            if (colonIndex > 0)
            {
                var ordinalText = key.Substring(2, colonIndex - 2);
                var suffix = key.Substring(colonIndex + 1);
                if (contextMethod != null &&
                    int.TryParse(ordinalText, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal) &&
                    ordinal >= 0 &&
                    ordinal < contextMethod.Parameters.Length)
                {
                    var parameterName = contextMethod.Parameters[ordinal].Name;
                    return string.Equals(suffix, "length", StringComparison.Ordinal)
                        ? parameterName + ".Length"
                        : parameterName;
                }

                return string.Equals(suffix, "length", StringComparison.Ordinal)
                    ? "p" + ordinalText + ".Length"
                    : "p" + ordinalText;
            }
        }

        if (string.Equals(key, "$this.length", StringComparison.Ordinal)) return "this.Length";

        if (string.Equals(key, "$this", StringComparison.Ordinal)) return "this";

        return key.StartsWith("name:", StringComparison.Ordinal)
            ? key.Substring("name:".Length)
            : key;
    }

    private static bool Dominates(SymbolicCostExpression left, SymbolicCostExpression right)
    {
        if (left.Kind != CostNodeKind.Monomial || right.Kind != CostNodeKind.Monomial) return false;

        if (left.Factors.Count == 0) return right.Factors.Count == 0;

        if (right.Factors.Count == 0) return true;

        foreach (var pair in right.Factors)
            if (!left.Factors.TryGetValue(pair.Key, out var leftExponent) || leftExponent < pair.Value)
                return false;

        return true;
    }

    private enum CostNodeKind
    {
        Monomial,
        Max,
        Unknown,
        RecursiveUnknown
    }
}
