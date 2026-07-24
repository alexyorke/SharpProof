namespace SharpProof.Symbolic.Ir;
internal enum SymbolicFactConfidence {
    Exact,
    Unsupported
}
internal enum SymbolicBinaryTermOperator {
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder
}
internal enum SymbolicRelationOperator {
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}
internal enum SymbolicStringPredicateKind {
    Contains,
    StartsWith,
    EndsWith,
    RegexMatch
}
internal enum SymbolicExceptionPreconditionKind {
    DivideByZero,
    NullDereference,
    ArgumentNull,
    IndexOutOfRange,
    ArgumentOutOfRange,
    NegativeLength,
    NegativeStackAllocLength,
    CheckedOverflow,
    InvalidCast,
    ArrayTypeMismatch,
    UnboxNull,
    NullableValueWithoutValue,
    DynamicNullBinding,
    SwitchExpressionNoMatch,
    DirectThrow,
    InvalidCollectionCardinality
}
internal abstract record SymbolicTerm(SmtValueKind Kind);
internal sealed record SymbolicBooleanConstantTerm(bool Value) : SymbolicTerm(SmtValueKind.Bool);
internal sealed record SymbolicIntegerConstantTerm(long Value) : SymbolicTerm(SmtValueKind.Int);
internal sealed record SymbolicStringConstantTerm(string Value) : SymbolicTerm(SmtValueKind.String);
internal sealed record SymbolicNullTerm() : SymbolicTerm(SmtValueKind.Reference);
internal sealed record SymbolicVariableTerm(string Name, SmtValueKind ValueKind) : SymbolicTerm(ValueKind);
internal sealed record SymbolicMemberTerm(SymbolicTerm Receiver, string MemberName, SmtValueKind ValueKind)
    : SymbolicTerm(ValueKind);
internal sealed record SymbolicElementTerm(SymbolicTerm Receiver, SymbolicTerm Index, SmtValueKind ValueKind)
    : SymbolicTerm(ValueKind);
internal sealed record SymbolicMultiElementTerm(SymbolicTerm Receiver, ImmutableArray<SymbolicTerm> Indices,
    SmtValueKind ValueKind) : SymbolicTerm(ValueKind);
internal sealed record SymbolicFromEndIndexTerm(SymbolicTerm Value) : SymbolicTerm(SmtValueKind.Int);
internal sealed record SymbolicStringContentTerm(SymbolicTerm Reference) : SymbolicTerm(SmtValueKind.String);
internal sealed record SymbolicStringConcatTerm(SymbolicTerm Left, SymbolicTerm Right)
    : SymbolicTerm(SmtValueKind.String);
/// <summary>
/// A slice of a string, carried as a string value rather than collapsed to its length, so
/// that content facts about it reach the solver's substring theory.
/// </summary>
/// <remarks>
/// <see cref="Length" /> is the slice's requested length. It is retained on the node
/// because the solver's substring is total and would otherwise only yield
/// <c>min(Length, len(Value) - Offset)</c>; a slice only exists here on a path where the
/// call completed, so the requested length is the observed one. See
/// <c>SymbolicStringLengthLowerer.CreateStringResultLengthTerm</c>, which projects it.
/// </remarks>
internal sealed record SymbolicStringSliceTerm(SymbolicTerm Value, SymbolicTerm Offset, SymbolicTerm Length)
    : SymbolicTerm(SmtValueKind.String);
internal sealed record SymbolicNullableHasValueTerm(string NullableName) : SymbolicTerm(SmtValueKind.Bool);
internal sealed record SymbolicNullableValueTerm(string NullableName, SmtValueKind ValueKind) : SymbolicTerm(ValueKind);
internal sealed record SymbolicLengthTerm(SymbolicTerm Value) : SymbolicTerm(SmtValueKind.Int);
internal sealed record SymbolicArrayDimensionLengthTerm(SymbolicTerm Value, int Dimension)
    : SymbolicTerm(SmtValueKind.Int);
internal sealed record SymbolicCountTerm(SymbolicTerm Value) : SymbolicTerm(SmtValueKind.Int);
internal sealed record SymbolicBinaryTerm(
    SymbolicBinaryTermOperator Operator,
    SymbolicTerm Left,
    SymbolicTerm Right,
    bool MayOverflow = false) : SymbolicTerm(SmtValueKind.Int);
internal sealed record SymbolicConditionalTerm(SymbolicCondition Condition, SymbolicTerm WhenTrue,
    SymbolicTerm WhenFalse) : SymbolicTerm(WhenTrue.Kind);
internal sealed record SymbolicNumericConversionTerm(string OperandIdentity, SpecialType SourceType, SpecialType TargetType,
    bool IsChecked) : SymbolicTerm(SmtValueKind.Int);
internal abstract record SymbolicAtom;
internal sealed record SymbolicTruthAtom(SymbolicTerm Condition) : SymbolicAtom;
internal sealed record SymbolicRelationAtom(SymbolicRelationOperator Operator, SymbolicTerm Left, SymbolicTerm Right) : SymbolicAtom;
internal sealed record SymbolicStringPredicateAtom(
    SymbolicStringPredicateKind Predicate,
    SymbolicTerm Value,
    SymbolicTerm Argument,
    RegexOptions RegexOptions = RegexOptions.None) : SymbolicAtom;
internal sealed record SymbolicBoundsAtom(SymbolicTerm Index, SymbolicTerm Length, bool IncludeLowerBound,
    bool IncludeUpperBound) : SymbolicAtom;
internal record SymbolicTypeTestAtom(SymbolicTerm Value, string TypeKey) : SymbolicAtom;
internal sealed record SymbolicExactRuntimeTypeAtom(SymbolicTerm Value, string TypeKey)
    : SymbolicTypeTestAtom(Value, TypeKey);
internal sealed record SymbolicExceptionPreconditionAtom(
    SymbolicExceptionPreconditionKind Kind,
    SymbolicTerm? Subject,
    SymbolicCondition Trigger) : SymbolicAtom;
internal sealed record SymbolicFact(
    SymbolicAtom Atom,
    bool Polarity,
    SymbolicFactConfidence Confidence,
    string Provenance,
    TextSpan SourceSpan,
    ISymbol? Symbol,
    string? EvidenceKey) {
    public static SymbolicFact Exact(SymbolicAtom atom, SyntaxNode node, string provenance, ISymbol? symbol = null,
        string? evidenceKey = null) => new(atom, true, SymbolicFactConfidence.Exact, provenance, node.Span, symbol, evidenceKey);
    public SymbolicFact Negate() =>
        this with { Polarity = !Polarity };
}
internal abstract record SymbolicCondition;
internal sealed record SymbolicConstantCondition(bool Value) : SymbolicCondition;
internal sealed record SymbolicFactCondition(SymbolicFact Fact) : SymbolicCondition;
internal sealed record SymbolicNotCondition(SymbolicCondition Operand) : SymbolicCondition;
internal sealed record SymbolicBinaryCondition(SymbolicConditionOperator Operator, SymbolicCondition Left,
    SymbolicCondition Right) : SymbolicCondition;
internal enum SymbolicConditionOperator {
    And,
    Or
}
internal sealed class SymbolicState {
    private sealed class KeyBox(ProofKey value) {
        internal ProofKey Value { get; } = value;
    }
    private static readonly ConditionalWeakTable<SymbolicTerm, KeyBox> s_termKeys = new();
    private static readonly ConditionalWeakTable<SymbolicFact, KeyBox> s_factKeys = new();
    private static readonly ConditionalWeakTable<SymbolicCondition, KeyBox> s_conditionKeys = new();
    public SymbolicState(
        IEnumerable<SymbolicFact>? facts = null,
        IEnumerable<SymbolicCondition>? pathConditions = null,
        IEnumerable<KeyValuePair<string, int>>? symbolVersions = null,
        bool isContradictory = false,
        bool isExact = true,
        SymbolicUnknownReason unknownReason = SymbolicUnknownReason.None,
        IEnumerable<SymbolicLoweringProvenance>? provenance = null) {
        var normalizedFacts = DeduplicateFacts(facts?.ToImmutableArray() ?? []);
        var normalizedConditions = DeduplicateConditions(
            pathConditions?.ToImmutableArray() ?? [],
            normalizedFacts);
        SymbolVersions = symbolVersions?.ToImmutableDictionary(static pair => pair.Key, static pair
            => pair.Value, StringComparer.Ordinal) ??
                         ImmutableDictionary.Create<string, int>(StringComparer.Ordinal);
        Facts = normalizedFacts;
        PathConditions = normalizedConditions;
        IsContradictory = isContradictory ||
                          ContainsContradiction(Facts, PathConditions);
        IsExact = isExact;
        UnknownReason = unknownReason;
        Provenance = provenance?.ToImmutableArray() ?? [];
        ProofIndex = new FactIndex(Facts, PathConditions);
        NormalizedProofKey = CreateProofKey(Facts, PathConditions, SymbolVersions, IsContradictory);
    }
    public ImmutableArray<SymbolicFact> Facts { get; }
    public ImmutableArray<SymbolicCondition> PathConditions { get; }
    public ImmutableDictionary<string, int> SymbolVersions { get; }
    public bool IsContradictory { get; }
    public SymbolicUnknownReason UnknownReason { get; }
    public ImmutableArray<SymbolicLoweringProvenance> Provenance { get; }
    public bool IsExact { get; }
    public string NormalizedProofKey { get; }
    internal FactIndex ProofIndex { get; }
    public SymbolicState MarkContradictory() =>
        new(Facts, PathConditions, SymbolVersions, true, IsExact, UnknownReason, Provenance);
    public SymbolicState AddFact(SymbolicFact fact) {
        if (fact == null) throw new ArgumentNullException(nameof(fact));
        return new SymbolicState(Facts.Add(fact), PathConditions, SymbolVersions, IsContradictory, IsExact, UnknownReason, Provenance);
    }
    public SymbolicState AddPathCondition(SymbolicCondition condition) {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        return new SymbolicState(Facts, PathConditions.Add(condition), SymbolVersions, IsContradictory, IsExact, UnknownReason, Provenance);
    }
    public SymbolicState WithSymbolVersion(string symbolKey, int version) {
        if (string.IsNullOrWhiteSpace(symbolKey))
            throw new ArgumentException("Symbol key is required.", nameof(symbolKey));
        return new SymbolicState(
            Facts,
            PathConditions,
            SymbolVersions.SetItem(symbolKey, version),
            IsContradictory,
            IsExact,
            UnknownReason,
            Provenance);
    }
    public SymbolicState Normalize() {
        var normalizedFacts = DeduplicateFacts(AddIntrinsicDomainFacts(Facts, PathConditions));
        var normalizedConditions = DeduplicateConditions(PathConditions, normalizedFacts);
        var contradictory = IsContradictory ||
                            ContainsContradiction(normalizedFacts, normalizedConditions);
        if (normalizedFacts.SequenceEqual(Facts) &&
            normalizedConditions.SequenceEqual(PathConditions) &&
            contradictory == IsContradictory)
            return this;
        return new SymbolicState(normalizedFacts, normalizedConditions, SymbolVersions, contradictory, IsExact, UnknownReason, Provenance);
    }
    private static ImmutableArray<SymbolicFact> AddIntrinsicDomainFacts(
        ImmutableArray<SymbolicFact> facts,
        ImmutableArray<SymbolicCondition> conditions) {
        var domainTerms = new Dictionary<string, SymbolicTerm>(StringComparer.Ordinal);
        foreach (var fact in facts)
            if (fact != null)
                SymbolicAlgebra.Visit(fact, Collect);
        foreach (var condition in conditions)
            if (condition != null)
                SymbolicAlgebra.Visit(condition, Collect);
        if (domainTerms.Count == 0) return facts;
        var builder = facts.ToBuilder();
        foreach (var term in domainTerms.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => pair.Value)) {
            builder.Add(DomainFact(term, SymbolicRelationOperator.GreaterThanOrEqual, 0, "ir.domain.non-negative-size"));
            // Encoding a large upper bound over SMT string lengths makes even
            // simple concatenation identities pathologically expensive. String
            // result operations use an overflow-aware total extension instead.
            if (term is not SymbolicLengthTerm { Value.Kind: SmtValueKind.String })
                builder.Add(DomainFact(term, SymbolicRelationOperator.LessThanOrEqual, int.MaxValue, "ir.domain.bounded-size"));
        }
        return builder.ToImmutable();
        void Collect(SymbolicTerm term) {
            if (term is not (SymbolicLengthTerm or SymbolicArrayDimensionLengthTerm or SymbolicCountTerm))
                return;
            var termKey = CreateTermKey(term);
            if (!domainTerms.ContainsKey(termKey)) domainTerms.Add(termKey, term);
        }
    }
    private static SymbolicFact DomainFact(
        SymbolicTerm term,
        SymbolicRelationOperator op,
        long value,
        string provenance) => new(
        new SymbolicRelationAtom(op, term, new SymbolicIntegerConstantTerm(value)),
        true,
        SymbolicFactConfidence.Exact,
        provenance,
        default,
        null,
        provenance);
    private static ImmutableArray<SymbolicFact> DeduplicateFacts(ImmutableArray<SymbolicFact> facts) {
        if (facts.IsDefaultOrEmpty) return [];
        var seen = new Dictionary<ProofKey, int>();
        var builder = ImmutableArray.CreateBuilder<SymbolicFact>(facts.Length);
        foreach (var fact in facts) {
            if (fact == null) continue;
            if (EvaluateFact(fact) == true) continue;
            var key = CreateProofFactIndexKey(fact);
            if (seen.TryGetValue(key, out var existingIndex)) {
                builder[existingIndex] = SelectCanonicalFact(builder[existingIndex], fact);
            }
            else {
                seen.Add(key, builder.Count);
                builder.Add(fact);
            }
        }
        return builder.ToImmutable();
    }
    private static SymbolicFact SelectCanonicalFact(SymbolicFact left, SymbolicFact right) {
        if (right.Provenance.Length < left.Provenance.Length) return right;
        if (right.Provenance.Length == left.Provenance.Length &&
            string.CompareOrdinal(right.Provenance, left.Provenance) < 0)
            return right;
        return left;
    }
    private static ImmutableArray<SymbolicCondition> DeduplicateConditions(
        ImmutableArray<SymbolicCondition> conditions,
        ImmutableArray<SymbolicFact> facts) {
        if (conditions.IsDefaultOrEmpty) return [];
        var factConditionKeys = new HashSet<ProofKey>(
            facts.Select(static fact => CreateFactConditionIndexKey(CreateProofFactIndexKey(fact))));
        var seen = new HashSet<ProofKey>();
        var builder = ImmutableArray.CreateBuilder<SymbolicCondition>(conditions.Length);
        foreach (var condition in conditions) {
            if (condition == null) continue;
            var key = CreateProofConditionIndexKey(condition);
            if (key.Value == "const:true" ||
                factConditionKeys.Contains(key))
                continue;
            if (seen.Add(key)) builder.Add(condition);
        }
        return builder.ToImmutable();
    }
    private static bool ContainsContradiction(ImmutableArray<SymbolicFact> facts, ImmutableArray<SymbolicCondition> conditions) {
        var polarities = new Dictionary<ProofKey, bool>();
        foreach (var fact in facts) {
            if (EvaluateFact(fact) == false) return true;
            if (HasOppositePolarity(polarities, fact)) return true;
        }
        foreach (var condition in conditions) {
            if (ContainsConstant(condition, false)) return true;
            if (ContainsPolarityConflict(condition, SymbolicConditionOperator.And)) return true;
            foreach (var fact in EnumerateConditionFacts(condition)) {
                if (HasOppositePolarity(polarities, fact)) return true;
                if (EvaluateFact(fact) == false) return true;
            }
        }
        return false;
    }
    private static bool ContainsPolarityConflict(SymbolicCondition condition, SymbolicConditionOperator conditionOperator) {
        if (condition is not SymbolicBinaryCondition binary || binary.Operator != conditionOperator) return false;
        var polarities = new Dictionary<ProofKey, bool>();
        var facts = conditionOperator == SymbolicConditionOperator.And
            ? EnumerateConditionFacts(condition)
            : EnumerateDisjunctionFacts(condition);
        foreach (var fact in facts)
            if (HasOppositePolarity(polarities, fact))
                return true;
        return false;
    }
    private static bool HasOppositePolarity(IDictionary<ProofKey, bool> polarities, SymbolicFact fact) {
        if (fact.Confidence != SymbolicFactConfidence.Exact) return false;
        var key = CreateFactCoreKey(fact);
        var atomKey = new ProofKey(key.AtomKey);
        if (polarities.TryGetValue(atomKey, out var existingPolarity)) return existingPolarity != key.Polarity;
        polarities.Add(atomKey, key.Polarity);
        return false;
    }
    private static bool? EvaluateFact(SymbolicFact fact) {
        if (fact.Confidence != SymbolicFactConfidence.Exact) return null;
        var value = fact.Atom switch {
            SymbolicTruthAtom truth => EvaluateTerm(truth.Condition) as bool?,
            SymbolicRelationAtom relation => EvaluateRelation(relation),
            SymbolicBoundsAtom bounds => EvaluateBounds(bounds),
            SymbolicStringPredicateAtom predicate => EvaluateStringPredicate(predicate),
            SymbolicTypeTestAtom { Value: SymbolicNullTerm } => false,
            _ => null
        };
        return value.HasValue && !fact.Polarity ? !value.Value : value;
    }
    private static bool? EvaluateRelation(SymbolicRelationAtom relation) {
        if (CreateProofTermIndexKey(relation.Left).Equals(CreateProofTermIndexKey(relation.Right)))
            return EvaluateIntegerRelation(relation.Operator, 0, 0);
        if (TryEvaluateTerm<long>(relation.Left, out var left) &&
            TryEvaluateTerm<long>(relation.Right, out var right))
            return EvaluateIntegerRelation(relation.Operator, left, right);
        if (relation.Operator is not (SymbolicRelationOperator.Equal or SymbolicRelationOperator.NotEqual) ||
            !TryGetConstantEqualityKey(relation.Left, out var leftKey) ||
            !TryGetConstantEqualityKey(relation.Right, out var rightKey))
            return null;
        var equal = string.Equals(leftKey, rightKey, StringComparison.Ordinal);
        return relation.Operator == SymbolicRelationOperator.Equal ? equal : !equal;
    }
    private static bool EvaluateIntegerRelation(SymbolicRelationOperator relation, long left, long right) => relation switch {
        SymbolicRelationOperator.Equal => left == right,
        SymbolicRelationOperator.NotEqual => left != right,
        SymbolicRelationOperator.LessThan => left < right,
        SymbolicRelationOperator.LessThanOrEqual => left <= right,
        SymbolicRelationOperator.GreaterThan => left > right,
        SymbolicRelationOperator.GreaterThanOrEqual => left >= right,
        _ => false
    };
    private static bool? EvaluateBounds(SymbolicBoundsAtom bounds) {
        if (!TryEvaluateTerm<long>(bounds.Index, out var index) ||
            !TryEvaluateTerm<long>(bounds.Length, out var length) ||
            !bounds.IncludeLowerBound && !bounds.IncludeUpperBound)
            return null;
        return (!bounds.IncludeLowerBound || index >= 0) && (!bounds.IncludeUpperBound || index < length);
    }
    private static bool? EvaluateStringPredicate(SymbolicStringPredicateAtom predicate) {
        var supported = predicate.Predicate is SymbolicStringPredicateKind.Contains or
            SymbolicStringPredicateKind.StartsWith or SymbolicStringPredicateKind.EndsWith;
        if (!supported) return null;
        if (predicate.Argument is SymbolicStringConstantTerm { Value.Length: 0 } ||
            CreateProofTermIndexKey(predicate.Value).Equals(CreateProofTermIndexKey(predicate.Argument)))
            return true;
        if (!TryEvaluateTerm<string>(predicate.Value, out var value) ||
            !TryEvaluateTerm<string>(predicate.Argument, out var argument))
            return null;
        return predicate.Predicate switch {
            SymbolicStringPredicateKind.Contains => value.IndexOf(argument, StringComparison.Ordinal) >= 0,
            SymbolicStringPredicateKind.StartsWith => value.StartsWith(argument, StringComparison.Ordinal),
            _ => value.EndsWith(argument, StringComparison.Ordinal)
        };
    }
    private static bool TryGetConstantEqualityKey(SymbolicTerm term, out string key) {
        if (term is SymbolicNullTerm) {
            key = CreateTermKey(term);
            return true;
        }
        var value = EvaluateTerm(term);
        if (value == null) {
            key = string.Empty;
            return false;
        }
        var constant = value switch {
            bool boolean => (SymbolicTerm)new SymbolicBooleanConstantTerm(boolean),
            long integer => new SymbolicIntegerConstantTerm(integer),
            string text => new SymbolicStringConstantTerm(text),
            _ => null
        };
        key = constant == null ? string.Empty : CreateTermKey(constant);
        return constant != null;
    }
    private static object? EvaluateTerm(SymbolicTerm term) => term switch {
        SymbolicIntegerConstantTerm integer => integer.Value,
        SymbolicBooleanConstantTerm boolean => boolean.Value,
        SymbolicStringConstantTerm text => text.Value,
        SymbolicLengthTerm length when EvaluateTerm(length.Value) is string text => (long)text.Length,
        SymbolicStringConcatTerm concat => EvaluateConcat(concat),
        SymbolicConditionalTerm conditional => SelectBranch(conditional) is { } selected
            ? EvaluateTerm(selected)
            : null,
        _ => null
    };
    private static string? EvaluateConcat(SymbolicStringConcatTerm concat) =>
        EvaluateTerm(concat.Left) is string left && EvaluateTerm(concat.Right) is string right
            ? left + right
            : null;
    private static bool TryEvaluateTerm<T>(SymbolicTerm term, out T value) {
        if (EvaluateTerm(term) is T typed) {
            value = typed;
            return true;
        }
        value = default!;
        return false;
    }
    private static SymbolicTerm? SelectBranch(SymbolicConditionalTerm conditional) =>
        CreateProofConditionIndexKey(conditional.Condition).Value switch {
            "const:true" => conditional.WhenTrue,
            "const:false" => conditional.WhenFalse,
            _ => null
        };
    private static bool ContainsConstant(SymbolicCondition condition, bool expected) {
        if (CreateProofConditionIndexKey(condition).Value == (expected ? "const:true" : "const:false"))
            return true;
        return condition switch {
            SymbolicConstantCondition constant => constant.Value == expected,
            SymbolicNotCondition { Operand: var operand } => ContainsConstant(operand, !expected),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } binary => expected
                ? ContainsConstant(binary.Left, true) && ContainsConstant(binary.Right, true)
                : ContainsConstant(binary.Left, false) || ContainsConstant(binary.Right, false),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } binary => expected
                ? ContainsConstant(binary.Left, true) ||
                  ContainsConstant(binary.Right, true) ||
                  ContainsPolarityConflict(condition, SymbolicConditionOperator.Or)
                : ContainsConstant(binary.Left, false) && ContainsConstant(binary.Right, false),
            _ => false
        };
    }
    private static IEnumerable<SymbolicFact> EnumerateConditionFacts(SymbolicCondition condition) =>
        EnumerateConjunctiveFacts(condition, false);
    private static IEnumerable<SymbolicFact> EnumerateConjunctiveFacts(SymbolicCondition condition, bool negate) {
        switch (condition) {
            case SymbolicFactCondition factCondition:
                yield return negate ? factCondition.Fact.Negate() : factCondition.Fact;
                break;
            case SymbolicNotCondition { Operand: var operand }:
                foreach (var fact in EnumerateConjunctiveFacts(operand, !negate)) yield return fact;
                break;
            case SymbolicBinaryCondition binary
                when binary.Operator == (negate ? SymbolicConditionOperator.Or : SymbolicConditionOperator.And):
                foreach (var fact in EnumerateConjunctiveFacts(binary.Left, negate)) yield return fact;
                foreach (var fact in EnumerateConjunctiveFacts(binary.Right, negate)) yield return fact;
                break;
        }
    }
    private static IEnumerable<SymbolicFact> EnumerateDisjunctionFacts(SymbolicCondition condition) {
        switch (condition) {
            case SymbolicFactCondition factCondition:
                yield return factCondition.Fact;
                break;
            case SymbolicNotCondition { Operand: SymbolicFactCondition factCondition }:
                yield return factCondition.Fact.Negate();
                break;
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } binary:
                foreach (var fact in EnumerateDisjunctionFacts(binary.Left)) yield return fact;
                foreach (var fact in EnumerateDisjunctionFacts(binary.Right)) yield return fact;
                break;
        }
    }
    private static string CreateProofKey(
        ImmutableArray<SymbolicFact> facts,
        ImmutableArray<SymbolicCondition> conditions,
        ImmutableDictionary<string, int> symbolVersions,
        bool isContradictory) {
        if (isContradictory) return "contradictory:true";
        var parts = new List<string> {
            "contradictory:false"
        };
        parts.AddRange(symbolVersions
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => "version:" + pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)));
        parts.AddRange(facts.Select(static fact => "fact:" + CreateFactKey(fact)).OrderBy(static key => key, StringComparer.Ordinal));
        parts.AddRange(conditions.Select(static condition => "condition:" + CreateConditionKey(condition))
            .OrderBy(static key => key, StringComparer.Ordinal));
        return string.Join("\n", parts);
    }
    internal static string CreateProofFactKey(SymbolicFact fact) =>
        CreateFactKey(fact);
    internal static string CreateProofTermKey(SymbolicTerm term) =>
        CreateTermKey(term);
    internal static string CreateProofConditionKey(SymbolicCondition condition) =>
        CreateConditionKey(condition);
    internal static ProofKey CreateProofFactIndexKey(SymbolicFact fact) =>
        s_factKeys.GetValue(fact, static value => new KeyBox(new ProofKey(CreateFactKeyCore(value)))).Value;
    internal static ProofKey CreateProofTermIndexKey(SymbolicTerm term) =>
        s_termKeys.GetValue(term, static value => new KeyBox(new ProofKey(CreateTermKeyCore(value)))).Value;
    internal static ProofKey CreateProofConditionIndexKey(SymbolicCondition condition) =>
        s_conditionKeys.GetValue(
            condition,
            static value => new KeyBox(new ProofKey(CreateConditionKeyCore(value)))).Value;
    internal static ProofKey CreateFactConditionIndexKey(ProofKey fact) => new("fact-condition:" + fact.Value);
    internal static IEnumerable<ProofKey> EnumerateProofConditionFactIndexKeys(SymbolicCondition condition) =>
        EnumerateConditionFacts(condition).Select(static fact => new ProofKey(CreateFactKey(fact)));
    internal static bool TryEvaluateProofFact(SymbolicFact fact, out bool value) {
        var evaluated = EvaluateFact(fact);
        value = evaluated.GetValueOrDefault();
        return evaluated.HasValue;
    }
    private static string CreateFactKey(SymbolicFact fact) => CreateProofFactIndexKey(fact).Value;
    private static string CreateFactKeyCore(SymbolicFact fact) {
        var key = CreateFactCoreKey(fact);
        return string.Join("|", key.AtomKey, key.Polarity ? "true" : "false", fact.Confidence.ToString());
    }
    private static (string AtomKey, bool Polarity) CreateFactCoreKey(SymbolicFact fact) =>
        fact.Atom is SymbolicRelationAtom relation
            ? CreateRelationKey(relation, fact.Polarity, true)
            : (CreateAtomKey(fact.Atom), fact.Polarity);
    private static string CreateAtomKey(SymbolicAtom atom) => atom switch {
        SymbolicTruthAtom truth => "truth:" + CreateTermKey(truth.Condition),
        SymbolicRelationAtom relation => CreateRelationKey(relation, true, false).AtomKey,
        SymbolicStringPredicateAtom predicate => "string-predicate:" + predicate.Predicate + ":" +
                               predicate.RegexOptions + "(" +
                               CreateTermKey(predicate.Value) + "," +
                               CreateTermKey(predicate.Argument) + ")",
        SymbolicBoundsAtom bounds => "bounds:" +
                               (bounds.IncludeLowerBound ? "lower-inclusive" : "lower-exclusive") + ":" +
                               (bounds.IncludeUpperBound ? "upper-inclusive" : "upper-exclusive") + "(" +
                               CreateTermKey(bounds.Index) + "," +
                               CreateTermKey(bounds.Length) + ")",
        SymbolicExactRuntimeTypeAtom exactRuntimeType => "exact-runtime-type:" + exactRuntimeType.TypeKey + ":" +
                               CreateTermKey(exactRuntimeType.Value),
        SymbolicTypeTestAtom typeTest => "type-test:" + typeTest.TypeKey + ":" + CreateTermKey(typeTest.Value),
        SymbolicExceptionPreconditionAtom precondition => "exception-precondition:" + precondition.Kind + ":" +
                               (precondition.Subject != null ? CreateTermKey(precondition.Subject) : "none") + ":" +
                               CreateConditionKey(precondition.Trigger),
        _ => throw new NotSupportedException("Unsupported symbolic atom type: " + atom.GetType().FullName),
    };
    private static (string AtomKey, bool Polarity) CreateRelationKey(
        SymbolicRelationAtom relation,
        bool polarity,
        bool normalizePolarity) {
        var left = CreateTermKey(relation.Left);
        var right = CreateTermKey(relation.Right);
        var op = relation.Operator;
        switch (op) {
            case SymbolicRelationOperator.NotEqual when normalizePolarity:
                op = SymbolicRelationOperator.Equal;
                polarity = !polarity;
                break;
            case SymbolicRelationOperator.LessThanOrEqual when normalizePolarity:
                op = SymbolicRelationOperator.LessThan;
                (left, right) = (right, left);
                polarity = !polarity;
                break;
            case SymbolicRelationOperator.GreaterThan:
                op = SymbolicRelationOperator.LessThan;
                (left, right) = (right, left);
                break;
            case SymbolicRelationOperator.GreaterThanOrEqual:
                if (normalizePolarity) {
                    op = SymbolicRelationOperator.LessThan;
                    polarity = !polarity;
                }
                else {
                    op = SymbolicRelationOperator.LessThanOrEqual;
                    (left, right) = (right, left);
                }
                break;
        }
        if (op is SymbolicRelationOperator.Equal or SymbolicRelationOperator.NotEqual &&
            string.CompareOrdinal(left, right) > 0)
            (left, right) = (right, left);
        return ("relation:" + op + "(" + left + "," + right + ")", polarity);
    }
    private static string CreateTermKey(SymbolicTerm term) => CreateProofTermIndexKey(term).Value;
    private static string CreateTermKeyCore(SymbolicTerm term) {
        if (term is SymbolicFromEndIndexTerm fromEnd)
            return "from-end-index:" + CreateTermKey(fromEnd.Value);
        if (term is SymbolicNumericConversionTerm conversion)
            return "numeric-conversion:" + (int)conversion.SourceType + ":" + (int)conversion.TargetType + ":" +
                   (conversion.IsChecked ? "checked:" : "unchecked:") + conversion.OperandIdentity;
        if (SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var formula))
            return SmtFormulaStructuralKey.Create(formula);
        throw new NotSupportedException("Unsupported symbolic term type: " + term.GetType().FullName);
    }
    private static string CreateConditionKey(SymbolicCondition condition) => CreateProofConditionIndexKey(condition).Value;
    private static string CreateConditionKeyCore(SymbolicCondition condition) {
        var value = EvaluateCondition(condition);
        if (value.HasValue) return "const:" + (value.Value ? "true" : "false");
        if (condition is SymbolicFactCondition fact) return "fact-condition:" + CreateFactKey(fact.Fact);
        if (condition is SymbolicNotCondition { Operand: SymbolicFactCondition factOperand })
            return "fact-condition:" + CreateFactKey(factOperand.Fact.Negate());
        if (!SymbolicIrFormulaEncoder.TryEncode(condition, out var formula))
            throw new NotSupportedException("Unsupported symbolic condition type: " + condition.GetType().FullName);
        return SmtFormulaStructuralKey.Create(formula);
    }
    private static bool? EvaluateCondition(SymbolicCondition condition) => condition switch {
        SymbolicConstantCondition constant => constant.Value,
        SymbolicFactCondition fact => EvaluateFact(fact.Fact),
        SymbolicNotCondition not when EvaluateCondition(not.Operand) is { } value => !value,
        _ => null
    };
}
