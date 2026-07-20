namespace SharpProof.Symbolic.Ir;

internal enum SymbolicFactConfidence {
    Exact,
    Approximate,
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

internal enum SymbolicBorrowKind {
    Shared,
    Mutable
}

internal enum SymbolicEscapeKind {
    Unknown,
    Return,
    Argument,
    Field,
    Property,
    DelegateCapture,
    CollectionElement,
    RefAlias
}

internal enum SymbolicDisposalState {
    NotDisposed,
    Disposed,
    MaybeDisposed
}

internal enum SymbolicResourceLifetimeState {
    Owned,
    Borrowed,
    Escaped,
    Returned,
    Released
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

internal sealed record SymbolicMultiElementTerm(
    SymbolicTerm Receiver,
    ImmutableArray<SymbolicTerm> Indices,
    SmtValueKind ValueKind) : SymbolicTerm(ValueKind);

internal sealed record SymbolicFromEndIndexTerm(SymbolicTerm Value) : SymbolicTerm(SmtValueKind.Int);

internal sealed record SymbolicStringContentTerm(SymbolicTerm Reference) : SymbolicTerm(SmtValueKind.String);

internal sealed record SymbolicStringConcatTerm(SymbolicTerm Left, SymbolicTerm Right)
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

internal sealed record SymbolicConditionalTerm(
    SymbolicCondition Condition,
    SymbolicTerm WhenTrue,
    SymbolicTerm WhenFalse) : SymbolicTerm(WhenTrue.Kind);

internal sealed record SymbolicNumericConversionTerm(
    string OperandIdentity,
    SpecialType SourceType,
    SpecialType TargetType,
    bool IsChecked) : SymbolicTerm(SmtValueKind.Int);

internal abstract record SymbolicAtom;

internal sealed record SymbolicTruthAtom(SymbolicTerm Condition) : SymbolicAtom;

internal sealed record SymbolicRelationAtom(
    SymbolicRelationOperator Operator,
    SymbolicTerm Left,
    SymbolicTerm Right) : SymbolicAtom;

internal sealed record SymbolicStringPredicateAtom(
    SymbolicStringPredicateKind Predicate,
    SymbolicTerm Value,
    SymbolicTerm Argument,
    RegexOptions RegexOptions = RegexOptions.None) : SymbolicAtom;

internal sealed record SymbolicBoundsAtom(
    SymbolicTerm Index,
    SymbolicTerm Length,
    bool IncludeLowerBound,
    bool IncludeUpperBound) : SymbolicAtom;

internal sealed record SymbolicFreshnessAtom(SymbolicTerm Value) : SymbolicAtom;

internal sealed record SymbolicOwnershipAtom(SymbolicTerm Value, bool Escaped) : SymbolicAtom;

internal sealed record SymbolicAliasAtom(SymbolicTerm Source, SymbolicTerm Target, bool MayAlias) : SymbolicAtom;

internal sealed record SymbolicBorrowAtom(
    SymbolicTerm Owner,
    SymbolicTerm Borrow,
    SymbolicBorrowKind Kind) : SymbolicAtom;

internal sealed record SymbolicEscapeAtom(SymbolicTerm Value, SymbolicEscapeKind Kind) : SymbolicAtom;

internal sealed record SymbolicReturnedOwnershipAtom(SymbolicTerm Value) : SymbolicAtom;

internal sealed record SymbolicMutationAtom(SymbolicTerm Target, bool CallerVisible) : SymbolicAtom;

internal sealed record SymbolicDisposalAtom(SymbolicTerm Resource, SymbolicDisposalState State) : SymbolicAtom;

internal sealed record SymbolicResourceLifetimeAtom(
    SymbolicTerm Resource,
    SymbolicResourceLifetimeState State) : SymbolicAtom;

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
        string? evidenceKey = null) => new SymbolicFact(atom, true, SymbolicFactConfidence.Exact, provenance, node.Span, symbol, evidenceKey);

    public SymbolicFact Negate() =>
        this with { Polarity = !Polarity };
}

internal abstract record SymbolicCondition;

internal sealed record SymbolicConstantCondition(bool Value) : SymbolicCondition;

internal sealed record SymbolicFactCondition(SymbolicFact Fact) : SymbolicCondition;

internal sealed record SymbolicNotCondition(SymbolicCondition Operand) : SymbolicCondition;

internal sealed record SymbolicBinaryCondition(
    SymbolicConditionOperator Operator,
    SymbolicCondition Left,
    SymbolicCondition Right) : SymbolicCondition;

internal enum SymbolicConditionOperator {
    And,
    Or
}

internal sealed class SymbolicState {
    public SymbolicState(
        IEnumerable<SymbolicFact>? facts = null,
        IEnumerable<SymbolicCondition>? pathConditions = null,
        IEnumerable<KeyValuePair<string, int>>? symbolVersions = null,
        bool isContradictory = false) {
        var normalizedFacts = DeduplicateFacts(facts?.ToImmutableArray() ?? ImmutableArray<SymbolicFact>.Empty);
        var normalizedConditions = DeduplicateConditions(
            pathConditions?.ToImmutableArray() ?? ImmutableArray<SymbolicCondition>.Empty,
            normalizedFacts);
        SymbolVersions = symbolVersions?.ToImmutableDictionary(
                             static pair => pair.Key,
                             static pair => pair.Value,
                             StringComparer.Ordinal) ??
                         ImmutableDictionary.Create<string, int>(StringComparer.Ordinal);
        Facts = normalizedFacts;
        PathConditions = normalizedConditions;
        IsContradictory = isContradictory ||
                          ContainsContradiction(Facts, PathConditions);
        NormalizedProofKey = CreateProofKey(Facts, PathConditions, SymbolVersions, IsContradictory);
    }

    public ImmutableArray<SymbolicFact> Facts { get; }

    public ImmutableArray<SymbolicCondition> PathConditions { get; }

    public ImmutableDictionary<string, int> SymbolVersions { get; }

    public bool IsContradictory { get; }

    public string NormalizedProofKey { get; }

    public SymbolicState MarkContradictory() =>
        new(Facts, PathConditions, SymbolVersions, isContradictory: true);

    public SymbolicState AddFact(SymbolicFact fact) {
        if (fact == null) throw new ArgumentNullException(nameof(fact));

        return new SymbolicState(Facts.Add(fact), PathConditions, SymbolVersions, IsContradictory);
    }

    public SymbolicState AddPathCondition(SymbolicCondition condition) {
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        return new SymbolicState(Facts, PathConditions.Add(condition), SymbolVersions, IsContradictory);
    }

    public SymbolicState WithSymbolVersion(string symbolKey, int version) {
        if (string.IsNullOrWhiteSpace(symbolKey))
            throw new ArgumentException("Symbol key is required.", nameof(symbolKey));

        return new SymbolicState(
            Facts,
            PathConditions,
            SymbolVersions.SetItem(symbolKey, version),
            IsContradictory);
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

        return new SymbolicState(
            normalizedFacts,
            normalizedConditions,
            SymbolVersions,
            contradictory);
    }

    private static ImmutableArray<SymbolicFact> AddIntrinsicDomainFacts(
        ImmutableArray<SymbolicFact> facts,
        ImmutableArray<SymbolicCondition> conditions) {
        var collector = new IntrinsicDomainTermCollector();

        foreach (var fact in facts)
            if (fact != null)
                collector.Visit(fact);
        foreach (var condition in conditions)
            if (condition != null)
                collector.Visit(condition);

        var domainTerms = collector.Terms;
        if (domainTerms.Count == 0) return facts;

        var builder = facts.ToBuilder();
        foreach (var term in domainTerms.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => pair.Value)) {
            builder.Add(new SymbolicFact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.GreaterThanOrEqual,
                    term,
                    new SymbolicIntegerConstantTerm(0)),
                true,
                SymbolicFactConfidence.Exact,
                "ir.domain.non-negative-size",
                default,
                null,
                "ir.domain.non-negative-size"));
            // Encoding a large upper bound over SMT string lengths makes even
            // simple concatenation identities pathologically expensive. String
            // result operations use an overflow-aware total extension instead.
            if (term is not SymbolicLengthTerm { Value.Kind: SmtValueKind.String })
                builder.Add(new SymbolicFact(
                    new SymbolicRelationAtom(
                        SymbolicRelationOperator.LessThanOrEqual,
                        term,
                        new SymbolicIntegerConstantTerm(int.MaxValue)),
                    true,
                    SymbolicFactConfidence.Exact,
                    "ir.domain.bounded-size",
                    default,
                    null,
                    "ir.domain.bounded-size"));
        }

        return builder.ToImmutable();
    }

    private sealed class IntrinsicDomainTermCollector : SymbolicIrVisitor {
        internal Dictionary<string, SymbolicTerm> Terms { get; } =
            new(StringComparer.Ordinal);

        protected override void OnTerm(SymbolicTerm term) {
            if (term is not (SymbolicLengthTerm or SymbolicArrayDimensionLengthTerm or SymbolicCountTerm))
                return;

            var termKey = CreateTermKey(term);
            if (!Terms.ContainsKey(termKey)) Terms.Add(termKey, term);
        }
    }

    private static ImmutableArray<SymbolicFact> DeduplicateFacts(ImmutableArray<SymbolicFact> facts) {
        if (facts.IsDefaultOrEmpty) return ImmutableArray<SymbolicFact>.Empty;

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<SymbolicFact>(facts.Length);
        foreach (var fact in facts) {
            if (fact == null) continue;

            if (TryEvaluateFact(fact, out var factValue) &&
                factValue)
                continue;

            var key = CreateFactKey(fact);
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
        if (conditions.IsDefaultOrEmpty) return ImmutableArray<SymbolicCondition>.Empty;

        var factConditionKeys = new HashSet<string>(
            facts.Select(static fact => "fact-condition:" + CreateFactKey(fact)),
            StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<SymbolicCondition>(conditions.Length);
        foreach (var condition in conditions) {
            if (condition == null) continue;

            var key = CreateConditionKey(condition);
            if (string.Equals(key, "const:true", StringComparison.Ordinal) ||
                factConditionKeys.Contains(key))
                continue;

            if (seen.Add(key)) builder.Add(condition);
        }

        return builder.ToImmutable();
    }

    private static bool ContainsContradiction(
        ImmutableArray<SymbolicFact> facts,
        ImmutableArray<SymbolicCondition> conditions) {
        var polarities = new Dictionary<string, bool>(StringComparer.Ordinal);
        var ownershipStates = new Dictionary<string, SymbolicExclusiveOwnershipState>(StringComparer.Ordinal);
        var disposalStates = new Dictionary<string, SymbolicDisposalState>(StringComparer.Ordinal);
        var resourceLifetimeStates = new Dictionary<string, SymbolicResourceLifetimeState>(StringComparer.Ordinal);
        foreach (var fact in facts) {
            if (TryEvaluateFact(fact, out var factValue) &&
                !factValue)
                return true;

            if (HasOppositePolarity(polarities, fact)) return true;

            if (HasExclusiveResourceStateContradiction(ownershipStates, disposalStates, resourceLifetimeStates, fact))
                return true;
        }

        foreach (var condition in conditions) {
            if (ContainsConstant(condition, false)) return true;

            if (ContainsPolarityConflict(condition, SymbolicConditionOperator.And)) return true;

            foreach (var fact in EnumerateConditionFacts(condition)) {
                if (HasOppositePolarity(polarities, fact)) return true;

                if (HasExclusiveResourceStateContradiction(ownershipStates, disposalStates, resourceLifetimeStates,
                        fact)) return true;

                if (TryEvaluateFact(fact, out var factValue) &&
                    !factValue)
                    return true;
            }
        }

        return false;
    }

    private static bool HasExclusiveResourceStateContradiction(
        IDictionary<string, SymbolicExclusiveOwnershipState> ownershipStates,
        IDictionary<string, SymbolicDisposalState> disposalStates,
        IDictionary<string, SymbolicResourceLifetimeState> resourceLifetimeStates,
        SymbolicFact fact) {
        if (!fact.Polarity ||
            fact.Confidence != SymbolicFactConfidence.Exact)
            return false;

        return fact.Atom switch {
            SymbolicOwnershipAtom ownership => HasExclusiveStateContradiction(
                                ownershipStates,
                                CreateTermKey(ownership.Value),
                                ownership.Escaped
                                    ? SymbolicExclusiveOwnershipState.Escaped
                                    : SymbolicExclusiveOwnershipState.Owned),
            SymbolicDisposalAtom { State: not SymbolicDisposalState.MaybeDisposed } disposal => HasExclusiveStateContradiction(
                                disposalStates,
                                CreateTermKey(disposal.Resource),
                                disposal.State),
            SymbolicResourceLifetimeAtom resourceLifetime => HasExclusiveStateContradiction(
                                resourceLifetimeStates,
                                CreateTermKey(resourceLifetime.Resource),
                                resourceLifetime.State),
            _ => false,
        };
    }

    private static bool HasExclusiveStateContradiction<TState>(
        IDictionary<string, TState> states,
        string resourceKey,
        TState state)
        where TState : struct, Enum {
        if (states.TryGetValue(resourceKey, out var existingState))
            return !EqualityComparer<TState>.Default.Equals(existingState, state);

        states.Add(resourceKey, state);
        return false;
    }

    private static bool ContainsPolarityConflict(
        SymbolicCondition condition,
        SymbolicConditionOperator conditionOperator) {
        if (condition is not SymbolicBinaryCondition binary || binary.Operator != conditionOperator) return false;

        var polarities = new Dictionary<string, bool>(StringComparer.Ordinal);
        var facts = conditionOperator == SymbolicConditionOperator.And
            ? EnumerateConditionFacts(condition)
            : EnumerateDisjunctionFacts(condition);
        foreach (var fact in facts)
            if (HasOppositePolarity(polarities, fact))
                return true;

        return false;
    }

    private static bool HasOppositePolarity(
        IDictionary<string, bool> polarities,
        SymbolicFact fact) {
        if (fact.Confidence != SymbolicFactConfidence.Exact) return false;

        var key = CreateFactCoreKey(fact);
        if (polarities.TryGetValue(key.AtomKey, out var existingPolarity)) return existingPolarity != key.Polarity;

        polarities.Add(key.AtomKey, key.Polarity);
        return false;
    }

    private static bool TryEvaluateFact(SymbolicFact fact, out bool value) {
        if (fact.Confidence != SymbolicFactConfidence.Exact) {
            value = false;
            return false;
        }

        if (fact.Atom is SymbolicTruthAtom { Condition: var truthCondition } &&
            TryEvaluateBooleanTerm(truthCondition, out var truthValue)) {
            value = fact.Polarity ? truthValue : !truthValue;
            return true;
        }

        if (fact.Atom is SymbolicRelationAtom relation &&
            (TryEvaluateSelfRelation(relation, out value) ||
             TryEvaluateConstantRelation(relation, out value))) {
            value = fact.Polarity ? value : !value;
            return true;
        }

        if (fact.Atom is SymbolicBoundsAtom bounds &&
            TryEvaluateConstantBounds(bounds, out value)) {
            value = fact.Polarity ? value : !value;
            return true;
        }

        if (fact.Atom is SymbolicStringPredicateAtom stringPredicate &&
            TryEvaluateConstantStringPredicate(stringPredicate, out value)) {
            value = fact.Polarity ? value : !value;
            return true;
        }

        if (fact.Atom is SymbolicAliasAtom alias &&
            TryEvaluateSelfAlias(alias, out value)) {
            value = fact.Polarity ? value : !value;
            return true;
        }

        if (fact.Atom is SymbolicTypeTestAtom typeTest &&
            TryEvaluateNullTypeTest(typeTest, out value)) {
            value = fact.Polarity ? value : !value;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryEvaluateSelfRelation(SymbolicRelationAtom relation, out bool value) {
        if (!string.Equals(
                CreateTermKey(relation.Left),
                CreateTermKey(relation.Right),
                StringComparison.Ordinal)) {
            value = false;
            return false;
        }

        value = EvaluateIntegerRelation(relation.Operator, 0, 0);
        return true;
    }

    private static bool TryEvaluateConstantRelation(SymbolicRelationAtom relation, out bool value) {
        if (TryEvaluateIntegerTerm(relation.Left, out var leftInteger) &&
            TryEvaluateIntegerTerm(relation.Right, out var rightInteger)) {
            value = EvaluateIntegerRelation(relation.Operator, leftInteger, rightInteger);
            return true;
        }

        if (relation.Operator is SymbolicRelationOperator.Equal or SymbolicRelationOperator.NotEqual &&
            TryGetConstantEqualityKey(relation.Left, out var leftKey) &&
            TryGetConstantEqualityKey(relation.Right, out var rightKey)) {
            var equal = string.Equals(leftKey, rightKey, StringComparison.Ordinal);
            value = relation.Operator == SymbolicRelationOperator.Equal
                ? equal
                : !equal;
            return true;
        }

        value = false;
        return false;
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

    private static bool TryEvaluateConstantBounds(SymbolicBoundsAtom bounds, out bool value) {
        if (!TryEvaluateIntegerTerm(bounds.Index, out var index) ||
            !TryEvaluateIntegerTerm(bounds.Length, out var length) ||
            (!bounds.IncludeLowerBound && !bounds.IncludeUpperBound)) {
            value = false;
            return false;
        }

        value = (!bounds.IncludeLowerBound || index >= 0) &&
                (!bounds.IncludeUpperBound || index < length);
        return true;
    }

    private static bool TryEvaluateConstantStringPredicate(
        SymbolicStringPredicateAtom predicate,
        out bool value) {
        if (predicate.Predicate is
            SymbolicStringPredicateKind.Contains or
            SymbolicStringPredicateKind.StartsWith or
            SymbolicStringPredicateKind.EndsWith)
            if (predicate.Argument is SymbolicStringConstantTerm { Value.Length: 0 } ||
                string.Equals(
                    CreateTermKey(predicate.Value),
                    CreateTermKey(predicate.Argument),
                    StringComparison.Ordinal)) {
                value = true;
                return true;
            }

        if (!TryEvaluateStringTerm(predicate.Value, out var valueText) ||
            !TryEvaluateStringTerm(predicate.Argument, out var argumentText)) {
            value = false;
            return false;
        }

        value = predicate.Predicate switch {
            SymbolicStringPredicateKind.Contains => valueText.IndexOf(argumentText, StringComparison.Ordinal) >= 0,
            SymbolicStringPredicateKind.StartsWith => valueText.StartsWith(argumentText, StringComparison.Ordinal),
            SymbolicStringPredicateKind.EndsWith => valueText.EndsWith(argumentText, StringComparison.Ordinal),
            _ => false
        };

        return predicate.Predicate is
            SymbolicStringPredicateKind.Contains or
            SymbolicStringPredicateKind.StartsWith or
            SymbolicStringPredicateKind.EndsWith;
    }

    private static bool TryGetConstantEqualityKey(SymbolicTerm term, out string key) {
        switch (term) {
            case SymbolicBooleanConstantTerm:
            case SymbolicNullTerm:
                key = CreateTermKey(term);
                return true;
            default:
                if (TryEvaluateBooleanTerm(term, out var booleanValue)) {
                    key = CreateTermKey(new SymbolicBooleanConstantTerm(booleanValue));
                    return true;
                }

                if (TryEvaluateIntegerTerm(term, out var integerValue)) {
                    key = CreateTermKey(new SymbolicIntegerConstantTerm(integerValue));
                    return true;
                }

                if (TryEvaluateStringTerm(term, out var stringValue)) {
                    key = CreateTermKey(new SymbolicStringConstantTerm(stringValue));
                    return true;
                }

                key = string.Empty;
                return false;
        }
    }

    private static bool TryEvaluateIntegerTerm(SymbolicTerm term, out long value) {
        switch (term) {
            case SymbolicIntegerConstantTerm integer:
                value = integer.Value;
                return true;
            case SymbolicLengthTerm { Value: var lengthValue }
                when TryEvaluateStringTerm(lengthValue, out var stringValue):
                value = stringValue.Length;
                return true;
            case SymbolicConditionalTerm conditional
                when TrySelectConstantConditionalBranch(conditional, out var selected):
                return TryEvaluateIntegerTerm(selected, out value);
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryEvaluateStringTerm(SymbolicTerm term, out string value) {
        switch (term) {
            case SymbolicStringConstantTerm stringConstant:
                value = stringConstant.Value;
                return true;
            case SymbolicStringConcatTerm concat:
                if (TryEvaluateStringTerm(concat.Left, out var left) &&
                    TryEvaluateStringTerm(concat.Right, out var right)) {
                    value = left + right;
                    return true;
                }

                value = string.Empty;
                return false;
            case SymbolicConditionalTerm conditional
                when TrySelectConstantConditionalBranch(conditional, out var selected):
                return TryEvaluateStringTerm(selected, out value);
            default:
                value = string.Empty;
                return false;
        }
    }

    private static bool TryEvaluateBooleanTerm(SymbolicTerm term, out bool value) {
        switch (term) {
            case SymbolicBooleanConstantTerm boolean:
                value = boolean.Value;
                return true;
            case SymbolicConditionalTerm conditional
                when TrySelectConstantConditionalBranch(conditional, out var selected):
                return TryEvaluateBooleanTerm(selected, out value);
            default:
                value = false;
                return false;
        }
    }

    private static bool TrySelectConstantConditionalBranch(
        SymbolicConditionalTerm conditional,
        out SymbolicTerm selected) {
        var conditionKey = CreateConditionKey(conditional.Condition);
        if (string.Equals(conditionKey, "const:true", StringComparison.Ordinal)) {
            selected = conditional.WhenTrue;
            return true;
        }

        if (string.Equals(conditionKey, "const:false", StringComparison.Ordinal)) {
            selected = conditional.WhenFalse;
            return true;
        }

        selected = conditional.WhenTrue;
        return false;
    }

    private static bool TryEvaluateSelfAlias(SymbolicAliasAtom alias, out bool value) {
        if (!string.Equals(
                CreateTermKey(alias.Source),
                CreateTermKey(alias.Target),
                StringComparison.Ordinal)) {
            value = false;
            return false;
        }

        value = alias.MayAlias;
        return true;
    }

    private static bool TryEvaluateNullTypeTest(SymbolicTypeTestAtom typeTest, out bool value) {
        if (typeTest.Value is not SymbolicNullTerm) {
            value = false;
            return false;
        }

        value = false;
        return true;
    }

    private static bool ContainsConstant(SymbolicCondition condition, bool expected) {
        if (string.Equals(
                CreateConditionKey(condition),
                expected ? "const:true" : "const:false",
                StringComparison.Ordinal))
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

    private static IEnumerable<SymbolicFact> EnumerateConjunctiveFacts(
        SymbolicCondition condition,
        bool negate) {
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
        parts.AddRange(facts.Select(static fact => "fact:" + CreateFactKey(fact))
            .OrderBy(static key => key, StringComparer.Ordinal));
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

    internal static IEnumerable<string> EnumerateProofConditionFactKeys(SymbolicCondition condition) =>
        EnumerateConditionFacts(condition).Select(CreateFactKey);

    internal static bool TryEvaluateProofFact(SymbolicFact fact, out bool value) =>
        TryEvaluateFact(fact, out value);

    private static string CreateFactKey(SymbolicFact fact) {
        var key = CreateFactCoreKey(fact);
        return string.Join(
            "|",
            key.AtomKey,
            key.Polarity ? "true" : "false",
            fact.Confidence.ToString());
    }

    private static (string AtomKey, bool Polarity) CreateFactCoreKey(SymbolicFact fact) => fact.Atom is SymbolicRelationAtom relation
            ? CreateRelationFactCoreKey(relation, fact.Polarity)
            : (CreateAtomKey(fact.Atom), fact.Polarity);

    private static string CreateAtomKey(SymbolicAtom atom) => atom switch {
        SymbolicTruthAtom truth => "truth:" + CreateTermKey(truth.Condition),
        SymbolicRelationAtom relation => CreateRelationAtomKey(relation),
        SymbolicStringPredicateAtom predicate => "string-predicate:" + predicate.Predicate + ":" +
                               predicate.RegexOptions + "(" +
                               CreateTermKey(predicate.Value) + "," +
                               CreateTermKey(predicate.Argument) + ")",
        SymbolicBoundsAtom bounds => "bounds:" +
                               (bounds.IncludeLowerBound ? "lower-inclusive" : "lower-exclusive") + ":" +
                               (bounds.IncludeUpperBound ? "upper-inclusive" : "upper-exclusive") + "(" +
                               CreateTermKey(bounds.Index) + "," +
                               CreateTermKey(bounds.Length) + ")",
        SymbolicFreshnessAtom freshness => "fresh:" + CreateTermKey(freshness.Value),
        SymbolicOwnershipAtom ownership => "ownership:" + (ownership.Escaped ? "escaped" : "owned") + ":" + CreateTermKey(ownership.Value),
        SymbolicAliasAtom alias => CreateAliasAtomKey(alias),
        SymbolicBorrowAtom borrow => "borrow:" + borrow.Kind + "(" +
                               CreateTermKey(borrow.Owner) + "," +
                               CreateTermKey(borrow.Borrow) + ")",
        SymbolicEscapeAtom escape => "escape:" + escape.Kind + ":" + CreateTermKey(escape.Value),
        SymbolicReturnedOwnershipAtom returnedOwnership => "returned-ownership:" + CreateTermKey(returnedOwnership.Value),
        SymbolicMutationAtom mutation => "mutation:" + (mutation.CallerVisible ? "caller-visible" : "local") + ":" +
                               CreateTermKey(mutation.Target),
        SymbolicDisposalAtom disposal => "disposal:" + disposal.State + ":" + CreateTermKey(disposal.Resource),
        SymbolicResourceLifetimeAtom resourceLifetime => "resource-lifetime:" + resourceLifetime.State + ":" + CreateTermKey(resourceLifetime.Resource),
        SymbolicExactRuntimeTypeAtom exactRuntimeType => "exact-runtime-type:" + exactRuntimeType.TypeKey + ":" +
                               CreateTermKey(exactRuntimeType.Value),
        SymbolicTypeTestAtom typeTest => "type-test:" + typeTest.TypeKey + ":" + CreateTermKey(typeTest.Value),
        SymbolicExceptionPreconditionAtom precondition => "exception-precondition:" + precondition.Kind + ":" +
                               (precondition.Subject != null ? CreateTermKey(precondition.Subject) : "none") + ":" +
                               CreateConditionKey(precondition.Trigger),
        _ => throw new NotSupportedException("Unsupported symbolic atom type: " + atom.GetType().FullName),
    };

    private static string CreateAliasAtomKey(SymbolicAliasAtom alias) {
        var source = CreateTermKey(alias.Source);
        var target = CreateTermKey(alias.Target);
        if (string.CompareOrdinal(source, target) > 0) (source, target) = (target, source);

        return "alias:" + (alias.MayAlias ? "may" : "no") + "(" + source + "," + target + ")";
    }

    private static (string AtomKey, bool Polarity) CreateRelationFactCoreKey(
        SymbolicRelationAtom relation,
        bool polarity) {
        var left = CreateTermKey(relation.Left);
        var right = CreateTermKey(relation.Right);
        var relationOperator = relation.Operator;

        switch (relationOperator) {
            case SymbolicRelationOperator.NotEqual:
                relationOperator = SymbolicRelationOperator.Equal;
                polarity = !polarity;
                break;
            case SymbolicRelationOperator.LessThanOrEqual:
                relationOperator = SymbolicRelationOperator.LessThan;
                (left, right) = (right, left);
                polarity = !polarity;
                break;
            case SymbolicRelationOperator.GreaterThan:
                relationOperator = SymbolicRelationOperator.LessThan;
                (left, right) = (right, left);
                break;
            case SymbolicRelationOperator.GreaterThanOrEqual:
                relationOperator = SymbolicRelationOperator.LessThan;
                polarity = !polarity;
                break;
        }

        if ((relationOperator == SymbolicRelationOperator.Equal ||
             relationOperator == SymbolicRelationOperator.NotEqual) &&
            string.CompareOrdinal(left, right) > 0)
            (left, right) = (right, left);

        return ("relation:" + relationOperator + "(" + left + "," + right + ")", polarity);
    }

    private static string CreateRelationAtomKey(SymbolicRelationAtom relation) {
        var left = CreateTermKey(relation.Left);
        var right = CreateTermKey(relation.Right);
        var relationOperator = relation.Operator;

        switch (relationOperator) {
            case SymbolicRelationOperator.Equal:
            case SymbolicRelationOperator.NotEqual:
                if (string.CompareOrdinal(left, right) > 0) (left, right) = (right, left);

                break;
            case SymbolicRelationOperator.GreaterThan:
                relationOperator = SymbolicRelationOperator.LessThan;
                (left, right) = (right, left);
                break;
            case SymbolicRelationOperator.GreaterThanOrEqual:
                relationOperator = SymbolicRelationOperator.LessThanOrEqual;
                (left, right) = (right, left);
                break;
        }

        return "relation:" + relationOperator + "(" + left + "," + right + ")";
    }

    private static string CreateTermKey(SymbolicTerm term) => term switch {
        SymbolicBooleanConstantTerm boolean => "bool:" + (boolean.Value ? "true" : "false"),
        SymbolicIntegerConstantTerm integer => "int:" + integer.Value.ToString(CultureInfo.InvariantCulture),
        SymbolicStringConstantTerm stringConstant => "string:" + stringConstant.Value.Length.ToString(CultureInfo.InvariantCulture) + ":" +
                               stringConstant.Value,
        SymbolicNullTerm => "null",
        SymbolicVariableTerm variable => "var:" + variable.ValueKind + ":" + variable.Name,
        SymbolicMemberTerm member => "member:" + member.ValueKind + ":" + CreateTermKey(member.Receiver) + "." + member.MemberName,
        SymbolicElementTerm element => "element:" + element.ValueKind + ":" + CreateTermKey(element.Receiver) + "[" +
                               CreateTermKey(element.Index) + "]",
        SymbolicMultiElementTerm element => "multi-element:" + element.ValueKind + ":" + CreateTermKey(element.Receiver) + "[" +
                               string.Join(",", element.Indices.Select(CreateTermKey)) + "]",
        SymbolicFromEndIndexTerm fromEnd => "from-end-index:" + CreateTermKey(fromEnd.Value),
        SymbolicStringContentTerm content => "string-content:" + CreateTermKey(content.Reference),
        SymbolicStringConcatTerm concat => CreateStringConcatTermKey(concat),
        SymbolicNullableHasValueTerm nullableHasValue => "nullable-has-value:" + nullableHasValue.NullableName,
        SymbolicNullableValueTerm nullableValue => "nullable-value:" + nullableValue.NullableName + ":" + nullableValue.Kind,
        SymbolicLengthTerm length => "length:" + CreateTermKey(length.Value),
        SymbolicArrayDimensionLengthTerm dimensionLength => "array-dimension-length:" +
                               dimensionLength.Dimension.ToString(CultureInfo.InvariantCulture) +
                               ":" +
                               CreateTermKey(dimensionLength.Value),
        SymbolicCountTerm count => "count:" + CreateTermKey(count.Value),
        SymbolicBinaryTerm binary => CreateBinaryTermKey(binary),
        SymbolicConditionalTerm conditional => CreateConditionalTermKey(conditional),
        SymbolicNumericConversionTerm conversion => "numeric-conversion:" +
                               (int)conversion.SourceType + ":" +
                               (int)conversion.TargetType + ":" +
                               (conversion.IsChecked ? "checked:" : "unchecked:") +
                               conversion.OperandIdentity.Length.ToString(CultureInfo.InvariantCulture) + ":" +
                               conversion.OperandIdentity,
        _ => throw new NotSupportedException("Unsupported symbolic term type: " + term.GetType().FullName),
    };

    private static string CreateConditionalTermKey(SymbolicConditionalTerm conditional) {
        var conditionKey = CreateConditionKey(conditional.Condition);
        var whenTrueKey = CreateTermKey(conditional.WhenTrue);
        var whenFalseKey = CreateTermKey(conditional.WhenFalse);
        if (string.Equals(whenTrueKey, whenFalseKey, StringComparison.Ordinal)) return whenTrueKey;

        if (string.Equals(conditionKey, "const:true", StringComparison.Ordinal)) return whenTrueKey;

        if (string.Equals(conditionKey, "const:false", StringComparison.Ordinal)) return whenFalseKey;

        return "conditional(" +
               conditionKey + "," +
               whenTrueKey + "," +
               whenFalseKey + ")";
    }

    private static string CreateStringConcatTermKey(SymbolicStringConcatTerm concat) {
        var terms = new List<SymbolicTerm>();
        CollectStringConcatTerms(concat, terms);
        var termKeys = CreateNormalizedStringConcatTermKeys(terms);
        if (termKeys.Count == 1) return termKeys[0];

        return "string-concat(" + string.Join(",", termKeys) + ")";
    }

    private static void CollectStringConcatTerms(SymbolicTerm term, ICollection<SymbolicTerm> terms) {
        if (term is SymbolicStringConcatTerm concat) {
            CollectStringConcatTerms(concat.Left, terms);
            CollectStringConcatTerms(concat.Right, terms);
            return;
        }

        terms.Add(term);
    }

    private static List<string> CreateNormalizedStringConcatTermKeys(IEnumerable<SymbolicTerm> terms) {
        var termKeys = new List<string>();
        var pendingLiteral = string.Empty;
        foreach (var term in terms) {
            if (term is SymbolicStringConstantTerm stringConstant) {
                pendingLiteral += stringConstant.Value;
                continue;
            }

            AddPendingStringLiteralKey(termKeys, ref pendingLiteral);
            termKeys.Add(CreateTermKey(term));
        }

        AddPendingStringLiteralKey(termKeys, ref pendingLiteral);
        if (termKeys.Count == 0) termKeys.Add(CreateTermKey(new SymbolicStringConstantTerm(string.Empty)));

        return termKeys;
    }

    private static void AddPendingStringLiteralKey(ICollection<string> termKeys, ref string pendingLiteral) {
        if (pendingLiteral.Length == 0) return;

        termKeys.Add(CreateTermKey(new SymbolicStringConstantTerm(pendingLiteral)));
        pendingLiteral = string.Empty;
    }

    private static string CreateBinaryTermKey(SymbolicBinaryTerm binary) {
        var overflowPrefix = binary.MayOverflow ? "overflow-sensitive:" : string.Empty;
        if (IsAssociativeCommutativeBinaryTermOperator(binary.Operator)) {
            var terms = new List<SymbolicTerm>();
            CollectAssociativeBinaryTerms(binary, binary.Operator, binary.MayOverflow, terms);
            var operands = CreateNormalizedAssociativeBinaryTermKeys(binary.Operator, terms);
            if (operands.Count == 1 && !binary.MayOverflow) return operands[0];

            operands.Sort(StringComparer.Ordinal);
            return overflowPrefix + "binary-term:" + binary.Operator + "(" + string.Join(",", operands) + ")";
        }

        var left = CreateTermKey(binary.Left);
        var right = CreateTermKey(binary.Right);
        if (!binary.MayOverflow && IsRightIdentityBinaryTerm(binary.Operator, binary.Right)) return left;

        if (IsCommutativeBinaryTermOperator(binary.Operator) &&
            string.CompareOrdinal(left, right) > 0)
            (left, right) = (right, left);

        return overflowPrefix + "binary-term:" + binary.Operator + "(" + left + "," + right + ")";
    }

    private static void CollectAssociativeBinaryTerms(
        SymbolicTerm term,
        SymbolicBinaryTermOperator binaryOperator,
        bool mayOverflow,
        ICollection<SymbolicTerm> terms) {
        if (term is SymbolicBinaryTerm nested &&
            nested.Operator == binaryOperator &&
            nested.MayOverflow == mayOverflow) {
            CollectAssociativeBinaryTerms(nested.Left, binaryOperator, mayOverflow, terms);
            CollectAssociativeBinaryTerms(nested.Right, binaryOperator, mayOverflow, terms);
            return;
        }

        terms.Add(term);
    }

    private static List<string> CreateNormalizedAssociativeBinaryTermKeys(
        SymbolicBinaryTermOperator binaryOperator,
        IEnumerable<SymbolicTerm> terms) {
        var operands = terms
            .Where(term => !IsIdentityOperand(binaryOperator, term))
            .Select(CreateTermKey)
            .ToList();
        if (operands.Count == 0)
            operands.Add(CreateTermKey(new SymbolicIntegerConstantTerm(
                binaryOperator == SymbolicBinaryTermOperator.Add ? 0 : 1)));

        return operands;
    }

    private static bool IsIdentityOperand(
        SymbolicBinaryTermOperator binaryOperator,
        SymbolicTerm term) => binaryOperator switch {
            SymbolicBinaryTermOperator.Add => IsIntegerConstant(term, 0),
            SymbolicBinaryTermOperator.Multiply => IsIntegerConstant(term, 1),
            _ => false
        };

    private static bool IsRightIdentityBinaryTerm(
        SymbolicBinaryTermOperator binaryOperator,
        SymbolicTerm right) => binaryOperator switch {
            SymbolicBinaryTermOperator.Subtract => IsIntegerConstant(right, 0),
            SymbolicBinaryTermOperator.Divide => IsIntegerConstant(right, 1),
            _ => false
        };

    private static bool IsIntegerConstant(SymbolicTerm term, long value) => term is SymbolicIntegerConstantTerm integer &&
               integer.Value == value;

    private static bool IsAssociativeCommutativeBinaryTermOperator(SymbolicBinaryTermOperator binaryOperator) =>
        binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Multiply;

    private static bool IsCommutativeBinaryTermOperator(SymbolicBinaryTermOperator binaryOperator) =>
        binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Multiply;

    private static string CreateConditionKey(SymbolicCondition condition) {
        switch (condition) {
            case SymbolicConstantCondition constant:
                return "const:" + (constant.Value ? "true" : "false");
            case SymbolicFactCondition factCondition:
                if (TryEvaluateFact(factCondition.Fact, out var factValue))
                    return "const:" + (factValue ? "true" : "false");

                return "fact-condition:" + CreateFactKey(factCondition.Fact);
            case SymbolicNotCondition { Operand: SymbolicConstantCondition constantCondition }:
                return "const:" + (constantCondition.Value ? "false" : "true");
            case SymbolicNotCondition { Operand: SymbolicFactCondition factCondition }:
                if (TryEvaluateFact(factCondition.Fact, out var negatedFactValue))
                    return "const:" + (negatedFactValue ? "false" : "true");

                return "fact-condition:" + CreateFactKey(factCondition.Fact.Negate());
            case SymbolicNotCondition { Operand: SymbolicNotCondition nestedNotCondition }:
                return CreateConditionKey(nestedNotCondition.Operand);
            case SymbolicNotCondition { Operand: SymbolicBinaryCondition binaryCondition }:
                return CreateConditionKey(new SymbolicBinaryCondition(
                    NegateConditionOperator(binaryCondition.Operator),
                    new SymbolicNotCondition(binaryCondition.Left),
                    new SymbolicNotCondition(binaryCondition.Right)));
            case SymbolicNotCondition notCondition:
                return "not(" + CreateConditionKey(notCondition.Operand) + ")";
            case SymbolicBinaryCondition binaryCondition:
                var operandConditions = new List<SymbolicCondition>();
                CollectBinaryConditionOperands(binaryCondition, binaryCondition.Operator, operandConditions);
                var supportsBooleanSimplification =
                    operandConditions.All(static operand => !ContainsPotentiallyExceptionalArithmetic(operand));
                var operands = operandConditions
                    .Select(CreateConditionKey)
                    .ToList();
                var identityOperand = binaryCondition.Operator == SymbolicConditionOperator.And
                    ? "const:true"
                    : "const:false";
                var absorbingOperand = binaryCondition.Operator == SymbolicConditionOperator.And
                    ? "const:false"
                    : "const:true";
                if (operands.Any(operand => string.Equals(operand, absorbingOperand, StringComparison.Ordinal)))
                    return absorbingOperand;

                if (supportsBooleanSimplification &&
                    ContainsComplementaryConditionOperands(binaryCondition))
                    return absorbingOperand;

                operands.RemoveAll(operand => string.Equals(operand, identityOperand, StringComparison.Ordinal));
                operands = operands.Distinct(StringComparer.Ordinal).ToList();
                if (supportsBooleanSimplification)
                    operands = RemoveAbsorbedConditionOperands(binaryCondition.Operator, operandConditions, operands);

                if (operands.Count == 0) return identityOperand;

                if (operands.Count == 1) return operands[0];

                operands.Sort(StringComparer.Ordinal);
                return "binary:" + binaryCondition.Operator + "(" + string.Join(",", operands) + ")";
            default:
                throw new NotSupportedException(
                    "Unsupported symbolic condition type: " + condition.GetType().FullName);
        }
    }

    private static bool ContainsComplementaryConditionOperands(SymbolicBinaryCondition condition) {
        var operands = new List<SymbolicCondition>();
        CollectBinaryConditionOperands(condition, condition.Operator, operands);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var operand in operands) {
            var key = CreateConditionKey(operand);
            var negatedKey = CreateConditionKey(new SymbolicNotCondition(operand));
            if (seen.Contains(negatedKey)) return true;

            seen.Add(key);
        }

        return false;
    }

    private static bool ContainsPotentiallyExceptionalArithmetic(SymbolicCondition condition) => condition switch {
        SymbolicFactCondition factCondition => ContainsPotentiallyExceptionalArithmetic(factCondition.Fact),
        SymbolicNotCondition notCondition => ContainsPotentiallyExceptionalArithmetic(notCondition.Operand),
        SymbolicBinaryCondition binaryCondition => ContainsPotentiallyExceptionalArithmetic(binaryCondition.Left) ||
                               ContainsPotentiallyExceptionalArithmetic(binaryCondition.Right),
        _ => false,
    };

    private static bool ContainsPotentiallyExceptionalArithmetic(SymbolicFact fact) =>
        ContainsPotentiallyExceptionalArithmetic(fact.Atom);

    private static bool ContainsPotentiallyExceptionalArithmetic(SymbolicAtom atom) => atom switch {
        SymbolicTruthAtom truth => ContainsPotentiallyExceptionalArithmetic(truth.Condition),
        SymbolicRelationAtom relation => ContainsPotentiallyExceptionalArithmetic(relation.Left) ||
                               ContainsPotentiallyExceptionalArithmetic(relation.Right),
        SymbolicStringPredicateAtom predicate => ContainsPotentiallyExceptionalArithmetic(predicate.Value) ||
                               ContainsPotentiallyExceptionalArithmetic(predicate.Argument),
        SymbolicBoundsAtom bounds => ContainsPotentiallyExceptionalArithmetic(bounds.Index) ||
                               ContainsPotentiallyExceptionalArithmetic(bounds.Length),
        SymbolicFreshnessAtom freshness => ContainsPotentiallyExceptionalArithmetic(freshness.Value),
        SymbolicOwnershipAtom ownership => ContainsPotentiallyExceptionalArithmetic(ownership.Value),
        SymbolicAliasAtom alias => ContainsPotentiallyExceptionalArithmetic(alias.Source) ||
                               ContainsPotentiallyExceptionalArithmetic(alias.Target),
        SymbolicBorrowAtom borrow => ContainsPotentiallyExceptionalArithmetic(borrow.Owner) ||
                               ContainsPotentiallyExceptionalArithmetic(borrow.Borrow),
        SymbolicEscapeAtom escape => ContainsPotentiallyExceptionalArithmetic(escape.Value),
        SymbolicReturnedOwnershipAtom returnedOwnership => ContainsPotentiallyExceptionalArithmetic(returnedOwnership.Value),
        SymbolicMutationAtom mutation => ContainsPotentiallyExceptionalArithmetic(mutation.Target),
        SymbolicDisposalAtom disposal => ContainsPotentiallyExceptionalArithmetic(disposal.Resource),
        SymbolicResourceLifetimeAtom lifetime => ContainsPotentiallyExceptionalArithmetic(lifetime.Resource),
        SymbolicTypeTestAtom typeTest => ContainsPotentiallyExceptionalArithmetic(typeTest.Value),
        SymbolicExceptionPreconditionAtom precondition => (precondition.Subject != null &&
                                ContainsPotentiallyExceptionalArithmetic(precondition.Subject)) ||
                               ContainsPotentiallyExceptionalArithmetic(precondition.Trigger),
        _ => false,
    };

    private static bool ContainsPotentiallyExceptionalArithmetic(SymbolicTerm term) => term switch {
        SymbolicBinaryTerm {
            Operator: SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder
        } => true,
        SymbolicBinaryTerm binary => ContainsPotentiallyExceptionalArithmetic(binary.Left) ||
                   ContainsPotentiallyExceptionalArithmetic(binary.Right),
        SymbolicConditionalTerm conditional => ContainsPotentiallyExceptionalArithmetic(conditional.Condition) ||
                   ContainsPotentiallyExceptionalArithmetic(conditional.WhenTrue) ||
                   ContainsPotentiallyExceptionalArithmetic(conditional.WhenFalse),
        SymbolicMemberTerm member => ContainsPotentiallyExceptionalArithmetic(member.Receiver),
        SymbolicElementTerm element => ContainsPotentiallyExceptionalArithmetic(element.Receiver) ||
                   ContainsPotentiallyExceptionalArithmetic(element.Index),
        SymbolicMultiElementTerm element => ContainsPotentiallyExceptionalArithmetic(element.Receiver) ||
                   element.Indices.Any(ContainsPotentiallyExceptionalArithmetic),
        SymbolicFromEndIndexTerm fromEnd => ContainsPotentiallyExceptionalArithmetic(fromEnd.Value),
        SymbolicStringContentTerm stringContent => ContainsPotentiallyExceptionalArithmetic(stringContent.Reference),
        SymbolicStringConcatTerm stringConcat => ContainsPotentiallyExceptionalArithmetic(stringConcat.Left) ||
                   ContainsPotentiallyExceptionalArithmetic(stringConcat.Right),
        SymbolicLengthTerm length => ContainsPotentiallyExceptionalArithmetic(length.Value),
        SymbolicArrayDimensionLengthTerm arrayLength => ContainsPotentiallyExceptionalArithmetic(arrayLength.Value),
        SymbolicCountTerm count => ContainsPotentiallyExceptionalArithmetic(count.Value),
        _ => false,
    };

    private static void CollectBinaryConditionOperands(
        SymbolicCondition condition,
        SymbolicConditionOperator binaryOperator,
        ICollection<SymbolicCondition> operands) {
        if (condition is SymbolicBinaryCondition nested &&
            nested.Operator == binaryOperator) {
            CollectBinaryConditionOperands(nested.Left, binaryOperator, operands);
            CollectBinaryConditionOperands(nested.Right, binaryOperator, operands);
            return;
        }

        operands.Add(condition);
    }

    private static List<string> RemoveAbsorbedConditionOperands(
        SymbolicConditionOperator conditionOperator,
        IReadOnlyCollection<SymbolicCondition> operandConditions,
        List<string> operandKeys) {
        if (operandKeys.Count < 2) return operandKeys;

        var keySet = new HashSet<string>(operandKeys, StringComparer.Ordinal);
        var absorbedKeys = new HashSet<string>(StringComparer.Ordinal);
        var oppositeOperator = NegateConditionOperator(conditionOperator);
        foreach (var operandCondition in operandConditions) {
            if (operandCondition is not SymbolicBinaryCondition nested ||
                nested.Operator != oppositeOperator)
                continue;

            var nestedOperands = new List<SymbolicCondition>();
            CollectBinaryConditionOperands(nested, oppositeOperator, nestedOperands);
            if (nestedOperands
                .Select(CreateConditionKey)
                .Any(keySet.Contains))
                absorbedKeys.Add(CreateConditionKey(operandCondition));
        }

        return absorbedKeys.Count == 0
            ? operandKeys
            : operandKeys
                .Where(key => !absorbedKeys.Contains(key))
                .ToList();
    }

    private static SymbolicConditionOperator NegateConditionOperator(SymbolicConditionOperator conditionOperator) => conditionOperator == SymbolicConditionOperator.And
            ? SymbolicConditionOperator.Or
            : SymbolicConditionOperator.And;

    private enum SymbolicExclusiveOwnershipState {
        Owned,
        Escaped
    }
}
