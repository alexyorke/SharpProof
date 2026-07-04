using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal enum SymbolicFactConfidence
    {
        Exact,
        Approximate,
        Unsupported,
    }

    internal enum SymbolicBinaryTermOperator
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder,
    }

    internal enum SymbolicRelationOperator
    {
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,
    }

    internal enum SymbolicStringPredicateKind
    {
        Contains,
        StartsWith,
        EndsWith,
        RegexMatch,
    }

    internal enum SymbolicExceptionPreconditionKind
    {
        DivideByZero,
        NullDereference,
        ArgumentNull,
        IndexOutOfRange,
        ArgumentOutOfRange,
        NegativeLength,
        NegativeStackAllocLength,
        CheckedOverflow,
        InvalidCast,
        UnboxNull,
        NullableValueWithoutValue,
        DynamicNullBinding,
        SwitchExpressionNoMatch,
        DirectThrow,
    }

    internal enum SymbolicBorrowKind
    {
        Shared,
        Mutable,
    }

    internal enum SymbolicEscapeKind
    {
        Unknown,
        Return,
        Argument,
        Field,
        Property,
        DelegateCapture,
        CollectionElement,
        RefAlias,
    }

    internal enum SymbolicDisposalState
    {
        NotDisposed,
        Disposed,
        MaybeDisposed,
    }

    internal enum SymbolicResourceLifetimeState
    {
        Owned,
        Borrowed,
        Escaped,
        Returned,
        Released,
    }

    internal abstract record SymbolicTerm(SmtValueKind Kind);

    internal sealed record SymbolicBooleanConstantTerm(bool Value) : SymbolicTerm(SmtValueKind.Bool);

    internal sealed record SymbolicIntegerConstantTerm(long Value) : SymbolicTerm(SmtValueKind.Int);

    internal sealed record SymbolicStringConstantTerm(string Value) : SymbolicTerm(SmtValueKind.String);

    internal sealed record SymbolicNullTerm() : SymbolicTerm(SmtValueKind.Reference);

    internal sealed record SymbolicVariableTerm(string Name, SmtValueKind ValueKind) : SymbolicTerm(ValueKind);

    internal sealed record SymbolicMemberTerm(SymbolicTerm Receiver, string MemberName, SmtValueKind ValueKind) : SymbolicTerm(ValueKind);

    internal sealed record SymbolicElementTerm(SymbolicTerm Receiver, SymbolicTerm Index, SmtValueKind ValueKind) : SymbolicTerm(ValueKind);

    internal sealed record SymbolicStringContentTerm(SymbolicTerm Reference) : SymbolicTerm(SmtValueKind.String);

    internal sealed record SymbolicStringConcatTerm(SymbolicTerm Left, SymbolicTerm Right) : SymbolicTerm(SmtValueKind.String);

    internal sealed record SymbolicNullableHasValueTerm(string NullableName) : SymbolicTerm(SmtValueKind.Bool);

    internal sealed record SymbolicNullableValueTerm(string NullableName, SmtValueKind ValueKind) : SymbolicTerm(ValueKind);

    internal sealed record SymbolicLengthTerm(SymbolicTerm Value) : SymbolicTerm(SmtValueKind.Int);

    internal sealed record SymbolicArrayDimensionLengthTerm(SymbolicTerm Value, int Dimension) : SymbolicTerm(SmtValueKind.Int);

    internal sealed record SymbolicCountTerm(SymbolicTerm Value) : SymbolicTerm(SmtValueKind.Int);

    internal sealed record SymbolicBinaryTerm(
        SymbolicBinaryTermOperator Operator,
        SymbolicTerm Left,
        SymbolicTerm Right) : SymbolicTerm(SmtValueKind.Int);

    internal sealed record SymbolicConditionalTerm(
        SymbolicCondition Condition,
        SymbolicTerm WhenTrue,
        SymbolicTerm WhenFalse) : SymbolicTerm(WhenTrue.Kind);

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

    internal sealed record SymbolicTypeTestAtom(SymbolicTerm Value, string TypeKey) : SymbolicAtom;

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
        string? EvidenceKey)
    {
        public static SymbolicFact Exact(SymbolicAtom atom, SyntaxNode node, string provenance, ISymbol? symbol = null, string? evidenceKey = null)
        {
            return new SymbolicFact(atom, true, SymbolicFactConfidence.Exact, provenance, node.Span, symbol, evidenceKey);
        }

        public SymbolicFact Negate()
        {
            return this with { Polarity = !Polarity };
        }
    }

    internal abstract record SymbolicCondition;

    internal sealed record SymbolicConstantCondition(bool Value) : SymbolicCondition;

    internal sealed record SymbolicFactCondition(SymbolicFact Fact) : SymbolicCondition;

    internal sealed record SymbolicNotCondition(SymbolicCondition Operand) : SymbolicCondition;

    internal sealed record SymbolicBinaryCondition(
        SymbolicConditionOperator Operator,
        SymbolicCondition Left,
        SymbolicCondition Right) : SymbolicCondition;

    internal enum SymbolicConditionOperator
    {
        And,
        Or,
    }

    internal sealed class SymbolicState
    {
        public SymbolicState(
            IEnumerable<SymbolicFact>? facts = null,
            IEnumerable<SymbolicCondition>? pathConditions = null,
            IEnumerable<KeyValuePair<string, int>>? symbolVersions = null,
            bool isContradictory = false)
        {
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

        public SymbolicState AddFact(SymbolicFact fact)
        {
            if (fact == null)
            {
                throw new ArgumentNullException(nameof(fact));
            }

            return new SymbolicState(Facts.Add(fact), PathConditions, SymbolVersions, IsContradictory);
        }

        public SymbolicState AddPathCondition(SymbolicCondition condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            return new SymbolicState(Facts, PathConditions.Add(condition), SymbolVersions, IsContradictory);
        }

        public SymbolicState WithSymbolVersion(string symbolKey, int version)
        {
            if (string.IsNullOrWhiteSpace(symbolKey))
            {
                throw new ArgumentException("Symbol key is required.", nameof(symbolKey));
            }

            return new SymbolicState(
                Facts,
                PathConditions,
                SymbolVersions.SetItem(symbolKey, version),
                IsContradictory);
        }

        public SymbolicState Normalize()
        {
            var normalizedFacts = DeduplicateFacts(Facts);
            var normalizedConditions = DeduplicateConditions(PathConditions, normalizedFacts);
            var contradictory = IsContradictory ||
                ContainsContradiction(normalizedFacts, normalizedConditions);

            if (normalizedFacts.SequenceEqual(Facts) &&
                normalizedConditions.SequenceEqual(PathConditions) &&
                contradictory == IsContradictory)
            {
                return this;
            }

            return new SymbolicState(
                normalizedFacts,
                normalizedConditions,
                SymbolVersions,
                contradictory);
        }

        private static ImmutableArray<SymbolicFact> DeduplicateFacts(ImmutableArray<SymbolicFact> facts)
        {
            if (facts.IsDefaultOrEmpty)
            {
                return ImmutableArray<SymbolicFact>.Empty;
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var builder = ImmutableArray.CreateBuilder<SymbolicFact>(facts.Length);
            foreach (var fact in facts)
            {
                if (fact == null)
                {
                    continue;
                }

                if (TryEvaluateFact(fact, out var factValue) &&
                    factValue)
                {
                    continue;
                }

                var key = CreateFactKey(fact);
                if (seen.TryGetValue(key, out var existingIndex))
                {
                    builder[existingIndex] = SelectCanonicalFact(builder[existingIndex], fact);
                }
                else
                {
                    seen.Add(key, builder.Count);
                    builder.Add(fact);
                }
            }

            return builder.ToImmutable();
        }

        private static SymbolicFact SelectCanonicalFact(SymbolicFact left, SymbolicFact right)
        {
            if (right.Provenance.Length < left.Provenance.Length)
            {
                return right;
            }

            if (right.Provenance.Length == left.Provenance.Length &&
                string.CompareOrdinal(right.Provenance, left.Provenance) < 0)
            {
                return right;
            }

            return left;
        }

        private static ImmutableArray<SymbolicCondition> DeduplicateConditions(
            ImmutableArray<SymbolicCondition> conditions,
            ImmutableArray<SymbolicFact> facts)
        {
            if (conditions.IsDefaultOrEmpty)
            {
                return ImmutableArray<SymbolicCondition>.Empty;
            }

            var factConditionKeys = new HashSet<string>(
                facts.Select(static fact => "fact-condition:" + CreateFactKey(fact)),
                StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var builder = ImmutableArray.CreateBuilder<SymbolicCondition>(conditions.Length);
            foreach (var condition in conditions)
            {
                if (condition == null)
                {
                    continue;
                }

                var key = CreateConditionKey(condition);
                if (string.Equals(key, "const:true", StringComparison.Ordinal) ||
                    factConditionKeys.Contains(key))
                {
                    continue;
                }

                if (seen.Add(key))
                {
                    builder.Add(condition);
                }
            }

            return builder.ToImmutable();
        }

        private static bool ContainsContradiction(
            ImmutableArray<SymbolicFact> facts,
            ImmutableArray<SymbolicCondition> conditions)
        {
            var polarities = new Dictionary<string, bool>(StringComparer.Ordinal);
            var disposalStates = new Dictionary<string, SymbolicDisposalState>(StringComparer.Ordinal);
            var resourceLifetimeStates = new Dictionary<string, SymbolicResourceLifetimeState>(StringComparer.Ordinal);
            foreach (var fact in facts)
            {
                if (TryEvaluateFact(fact, out var factValue) &&
                    !factValue)
                {
                    return true;
                }

                if (HasOppositePolarity(polarities, fact))
                {
                    return true;
                }

                if (HasExclusiveResourceStateContradiction(disposalStates, resourceLifetimeStates, fact))
                {
                    return true;
                }
            }

            foreach (var condition in conditions)
            {
                if (ContainsFalseConstant(condition))
                {
                    return true;
                }

                if (ContainsConjunctionContradiction(condition))
                {
                    return true;
                }

                foreach (var fact in EnumerateConditionFacts(condition))
                {
                    if (HasOppositePolarity(polarities, fact))
                    {
                        return true;
                    }

                    if (HasExclusiveResourceStateContradiction(disposalStates, resourceLifetimeStates, fact))
                    {
                        return true;
                    }

                    if (TryEvaluateFact(fact, out var factValue) &&
                        !factValue)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasExclusiveResourceStateContradiction(
            IDictionary<string, SymbolicDisposalState> disposalStates,
            IDictionary<string, SymbolicResourceLifetimeState> resourceLifetimeStates,
            SymbolicFact fact)
        {
            if (!fact.Polarity ||
                fact.Confidence != SymbolicFactConfidence.Exact)
            {
                return false;
            }

            switch (fact.Atom)
            {
                case SymbolicDisposalAtom { State: not SymbolicDisposalState.MaybeDisposed } disposal:
                    return HasExclusiveStateContradiction(
                        disposalStates,
                        CreateTermKey(disposal.Resource),
                        disposal.State);
                case SymbolicResourceLifetimeAtom resourceLifetime:
                    return HasExclusiveStateContradiction(
                        resourceLifetimeStates,
                        CreateTermKey(resourceLifetime.Resource),
                        resourceLifetime.State);
                default:
                    return false;
            }
        }

        private static bool HasExclusiveStateContradiction<TState>(
            IDictionary<string, TState> states,
            string resourceKey,
            TState state)
            where TState : struct, Enum
        {
            if (states.TryGetValue(resourceKey, out var existingState))
            {
                return !EqualityComparer<TState>.Default.Equals(existingState, state);
            }

            states.Add(resourceKey, state);
            return false;
        }

        private static bool ContainsConjunctionContradiction(SymbolicCondition condition)
        {
            if (condition is not SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And })
            {
                return false;
            }

            var polarities = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var fact in EnumerateConditionFacts(condition))
            {
                if (HasOppositePolarity(polarities, fact))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsDisjunctionTautology(SymbolicCondition condition)
        {
            if (condition is not SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or })
            {
                return false;
            }

            var polarities = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var fact in EnumerateDisjunctionFacts(condition))
            {
                if (HasOppositePolarity(polarities, fact))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasOppositePolarity(
            IDictionary<string, bool> polarities,
            SymbolicFact fact)
        {
            var key = CreateFactCoreKey(fact);
            if (polarities.TryGetValue(key.AtomKey, out var existingPolarity))
            {
                return existingPolarity != key.Polarity;
            }

            polarities.Add(key.AtomKey, key.Polarity);
            return false;
        }

        private static bool TryEvaluateFact(SymbolicFact fact, out bool value)
        {
            if (fact.Confidence != SymbolicFactConfidence.Exact)
            {
                value = false;
                return false;
            }

            if (fact.Atom is SymbolicTruthAtom { Condition: var truthCondition } &&
                TryEvaluateBooleanTerm(truthCondition, out var truthValue))
            {
                value = fact.Polarity ? truthValue : !truthValue;
                return true;
            }

            if (fact.Atom is SymbolicRelationAtom relation &&
                (TryEvaluateSelfRelation(relation, out value) ||
                    TryEvaluateConstantRelation(relation, out value)))
            {
                value = fact.Polarity ? value : !value;
                return true;
            }

            if (fact.Atom is SymbolicBoundsAtom bounds &&
                TryEvaluateConstantBounds(bounds, out value))
            {
                value = fact.Polarity ? value : !value;
                return true;
            }

            if (fact.Atom is SymbolicStringPredicateAtom stringPredicate &&
                TryEvaluateConstantStringPredicate(stringPredicate, out value))
            {
                value = fact.Polarity ? value : !value;
                return true;
            }

            if (fact.Atom is SymbolicAliasAtom alias &&
                TryEvaluateSelfAlias(alias, out value))
            {
                value = fact.Polarity ? value : !value;
                return true;
            }

            if (fact.Atom is SymbolicTypeTestAtom typeTest &&
                TryEvaluateNullTypeTest(typeTest, out value))
            {
                value = fact.Polarity ? value : !value;
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryEvaluateSelfRelation(SymbolicRelationAtom relation, out bool value)
        {
            if (!string.Equals(
                    CreateTermKey(relation.Left),
                    CreateTermKey(relation.Right),
                    StringComparison.Ordinal))
            {
                value = false;
                return false;
            }

            value = relation.Operator switch
            {
                SymbolicRelationOperator.Equal => true,
                SymbolicRelationOperator.NotEqual => false,
                SymbolicRelationOperator.LessThan => false,
                SymbolicRelationOperator.LessThanOrEqual => true,
                SymbolicRelationOperator.GreaterThan => false,
                SymbolicRelationOperator.GreaterThanOrEqual => true,
                _ => false,
            };
            return true;
        }

        private static bool TryEvaluateConstantRelation(SymbolicRelationAtom relation, out bool value)
        {
            if (TryEvaluateIntegerTerm(relation.Left, out var leftInteger) &&
                TryEvaluateIntegerTerm(relation.Right, out var rightInteger))
            {
                value = relation.Operator switch
                {
                    SymbolicRelationOperator.Equal => leftInteger == rightInteger,
                    SymbolicRelationOperator.NotEqual => leftInteger != rightInteger,
                    SymbolicRelationOperator.LessThan => leftInteger < rightInteger,
                    SymbolicRelationOperator.LessThanOrEqual => leftInteger <= rightInteger,
                    SymbolicRelationOperator.GreaterThan => leftInteger > rightInteger,
                    SymbolicRelationOperator.GreaterThanOrEqual => leftInteger >= rightInteger,
                    _ => false,
                };
                return true;
            }

            if (relation.Operator is SymbolicRelationOperator.Equal or SymbolicRelationOperator.NotEqual &&
                TryGetConstantEqualityKey(relation.Left, out var leftKey) &&
                TryGetConstantEqualityKey(relation.Right, out var rightKey))
            {
                var equal = string.Equals(leftKey, rightKey, StringComparison.Ordinal);
                value = relation.Operator == SymbolicRelationOperator.Equal
                    ? equal
                    : !equal;
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryEvaluateConstantBounds(SymbolicBoundsAtom bounds, out bool value)
        {
            if (!TryEvaluateIntegerTerm(bounds.Index, out var index) ||
                !TryEvaluateIntegerTerm(bounds.Length, out var length) ||
                (!bounds.IncludeLowerBound && !bounds.IncludeUpperBound))
            {
                value = false;
                return false;
            }

            value = (!bounds.IncludeLowerBound || index >= 0) &&
                (!bounds.IncludeUpperBound || index < length);
            return true;
        }

        private static bool TryEvaluateConstantStringPredicate(
            SymbolicStringPredicateAtom predicate,
            out bool value)
        {
            if (predicate.Predicate is
                    SymbolicStringPredicateKind.Contains or
                    SymbolicStringPredicateKind.StartsWith or
                    SymbolicStringPredicateKind.EndsWith)
            {
                if (predicate.Argument is SymbolicStringConstantTerm { Value.Length: 0 } ||
                    string.Equals(
                        CreateTermKey(predicate.Value),
                        CreateTermKey(predicate.Argument),
                        StringComparison.Ordinal))
                {
                    value = true;
                    return true;
                }
            }

            if (!TryEvaluateStringTerm(predicate.Value, out var valueText) ||
                !TryEvaluateStringTerm(predicate.Argument, out var argumentText))
            {
                value = false;
                return false;
            }

            value = predicate.Predicate switch
            {
                SymbolicStringPredicateKind.Contains => valueText.Contains(argumentText, StringComparison.Ordinal),
                SymbolicStringPredicateKind.StartsWith => valueText.StartsWith(argumentText, StringComparison.Ordinal),
                SymbolicStringPredicateKind.EndsWith => valueText.EndsWith(argumentText, StringComparison.Ordinal),
                _ => false,
            };

            return predicate.Predicate is
                SymbolicStringPredicateKind.Contains or
                SymbolicStringPredicateKind.StartsWith or
                SymbolicStringPredicateKind.EndsWith;
        }

        private static bool TryGetConstantEqualityKey(SymbolicTerm term, out string key)
        {
            switch (term)
            {
                case SymbolicBooleanConstantTerm:
                case SymbolicNullTerm:
                    key = CreateTermKey(term);
                    return true;
                default:
                    if (TryEvaluateBooleanTerm(term, out var booleanValue))
                    {
                        key = CreateTermKey(new SymbolicBooleanConstantTerm(booleanValue));
                        return true;
                    }

                    if (TryEvaluateIntegerTerm(term, out var integerValue))
                    {
                        key = CreateTermKey(new SymbolicIntegerConstantTerm(integerValue));
                        return true;
                    }

                    if (TryEvaluateStringTerm(term, out var stringValue))
                    {
                        key = CreateTermKey(new SymbolicStringConstantTerm(stringValue));
                        return true;
                    }

                    key = string.Empty;
                    return false;
            }
        }

        private static bool TryEvaluateIntegerTerm(SymbolicTerm term, out long value)
        {
            switch (term)
            {
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

        private static bool TryEvaluateStringTerm(SymbolicTerm term, out string value)
        {
            switch (term)
            {
                case SymbolicStringConstantTerm stringConstant:
                    value = stringConstant.Value;
                    return true;
                case SymbolicStringConcatTerm concat:
                    if (TryEvaluateStringTerm(concat.Left, out var left) &&
                        TryEvaluateStringTerm(concat.Right, out var right))
                    {
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

        private static bool TryEvaluateBooleanTerm(SymbolicTerm term, out bool value)
        {
            switch (term)
            {
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
            out SymbolicTerm selected)
        {
            var conditionKey = CreateConditionKey(conditional.Condition);
            if (string.Equals(conditionKey, "const:true", StringComparison.Ordinal))
            {
                selected = conditional.WhenTrue;
                return true;
            }

            if (string.Equals(conditionKey, "const:false", StringComparison.Ordinal))
            {
                selected = conditional.WhenFalse;
                return true;
            }

            selected = conditional.WhenTrue;
            return false;
        }

        private static bool TryEvaluateSelfAlias(SymbolicAliasAtom alias, out bool value)
        {
            if (!string.Equals(
                    CreateTermKey(alias.Source),
                    CreateTermKey(alias.Target),
                    StringComparison.Ordinal))
            {
                value = false;
                return false;
            }

            value = alias.MayAlias;
            return true;
        }

        private static bool TryEvaluateNullTypeTest(SymbolicTypeTestAtom typeTest, out bool value)
        {
            if (typeTest.Value is not SymbolicNullTerm)
            {
                value = false;
                return false;
            }

            value = false;
            return true;
        }

        private static bool ContainsFalseConstant(SymbolicCondition condition)
        {
            if (string.Equals(
                    CreateConditionKey(condition),
                    "const:false",
                    StringComparison.Ordinal))
            {
                return true;
            }

            switch (condition)
            {
                case SymbolicConstantCondition { Value: false }:
                    return true;
                case SymbolicNotCondition { Operand: SymbolicConstantCondition { Value: true } }:
                    return true;
                case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } binary:
                    return ContainsFalseConstant(binary.Left) || ContainsFalseConstant(binary.Right);
                case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } binary:
                    return ContainsFalseConstant(binary.Left) && ContainsFalseConstant(binary.Right);
                case SymbolicNotCondition { Operand: var operand }:
                    return ContainsTrueConstant(operand);
                default:
                    return false;
            }
        }

        private static bool ContainsTrueConstant(SymbolicCondition condition)
        {
            if (string.Equals(
                    CreateConditionKey(condition),
                    "const:true",
                    StringComparison.Ordinal))
            {
                return true;
            }

            switch (condition)
            {
                case SymbolicConstantCondition { Value: true }:
                    return true;
                case SymbolicNotCondition { Operand: SymbolicConstantCondition { Value: false } }:
                    return true;
                case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } binary:
                    return ContainsTrueConstant(binary.Left) && ContainsTrueConstant(binary.Right);
                case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } binary:
                    return ContainsTrueConstant(binary.Left) ||
                        ContainsTrueConstant(binary.Right) ||
                        ContainsDisjunctionTautology(condition);
                case SymbolicNotCondition { Operand: var operand }:
                    return ContainsFalseConstant(operand);
                default:
                    return false;
            }
        }

        private static IEnumerable<SymbolicFact> EnumerateConditionFacts(SymbolicCondition condition)
        {
            switch (condition)
            {
                case SymbolicFactCondition factCondition:
                    yield return factCondition.Fact;
                    break;
                case SymbolicNotCondition { Operand: var operand }:
                    foreach (var fact in EnumerateNegatedConditionFacts(operand))
                    {
                        yield return fact;
                    }

                    break;
                case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } binary:
                    foreach (var fact in EnumerateConditionFacts(binary.Left))
                    {
                        yield return fact;
                    }

                    foreach (var fact in EnumerateConditionFacts(binary.Right))
                    {
                        yield return fact;
                    }

                    break;
            }
        }

        private static IEnumerable<SymbolicFact> EnumerateNegatedConditionFacts(SymbolicCondition condition)
        {
            switch (condition)
            {
                case SymbolicFactCondition factCondition:
                    yield return factCondition.Fact.Negate();
                    break;
                case SymbolicNotCondition { Operand: var operand }:
                    foreach (var fact in EnumerateConditionFacts(operand))
                    {
                        yield return fact;
                    }

                    break;
                case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } binary:
                    foreach (var fact in EnumerateNegatedConditionFacts(binary.Left))
                    {
                        yield return fact;
                    }

                    foreach (var fact in EnumerateNegatedConditionFacts(binary.Right))
                    {
                        yield return fact;
                    }

                    break;
            }
        }

        private static IEnumerable<SymbolicFact> EnumerateDisjunctionFacts(SymbolicCondition condition)
        {
            switch (condition)
            {
                case SymbolicFactCondition factCondition:
                    yield return factCondition.Fact;
                    break;
                case SymbolicNotCondition { Operand: SymbolicFactCondition factCondition }:
                    yield return factCondition.Fact.Negate();
                    break;
                case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } binary:
                    foreach (var fact in EnumerateDisjunctionFacts(binary.Left))
                    {
                        yield return fact;
                    }

                    foreach (var fact in EnumerateDisjunctionFacts(binary.Right))
                    {
                        yield return fact;
                    }

                    break;
            }
        }

        private static string CreateProofKey(
            ImmutableArray<SymbolicFact> facts,
            ImmutableArray<SymbolicCondition> conditions,
            ImmutableDictionary<string, int> symbolVersions,
            bool isContradictory)
        {
            if (isContradictory)
            {
                return "contradictory:true";
            }

            var parts = new List<string>
            {
                "contradictory:false",
            };

            parts.AddRange(symbolVersions
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => "version:" + pair.Key + "=" + pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            parts.AddRange(facts.Select(static fact => "fact:" + CreateFactKey(fact)).OrderBy(static key => key, StringComparer.Ordinal));
            parts.AddRange(conditions.Select(static condition => "condition:" + CreateConditionKey(condition)).OrderBy(static key => key, StringComparer.Ordinal));
            return string.Join("\n", parts);
        }

        internal static string CreateProofFactKey(SymbolicFact fact)
        {
            return CreateFactKey(fact);
        }

        internal static string CreateProofConditionKey(SymbolicCondition condition)
        {
            return CreateConditionKey(condition);
        }

        internal static IEnumerable<string> EnumerateProofConditionFactKeys(SymbolicCondition condition)
        {
            return EnumerateConditionFacts(condition).Select(CreateFactKey);
        }

        internal static bool TryEvaluateProofFact(SymbolicFact fact, out bool value)
        {
            return TryEvaluateFact(fact, out value);
        }

        private static string CreateFactKey(SymbolicFact fact)
        {
            var key = CreateFactCoreKey(fact);
            return string.Join(
                "|",
                key.AtomKey,
                key.Polarity ? "true" : "false",
                fact.Confidence.ToString());
        }

        private static (string AtomKey, bool Polarity) CreateFactCoreKey(SymbolicFact fact)
        {
            return fact.Atom is SymbolicRelationAtom relation
                ? CreateRelationFactCoreKey(relation, fact.Polarity)
                : (CreateAtomKey(fact.Atom), fact.Polarity);
        }

        private static string CreateAtomKey(SymbolicAtom atom)
        {
            switch (atom)
            {
                case SymbolicTruthAtom truth:
                    return "truth:" + CreateTermKey(truth.Condition);
                case SymbolicRelationAtom relation:
                    return CreateRelationAtomKey(relation);
                case SymbolicStringPredicateAtom predicate:
                    return "string-predicate:" + predicate.Predicate + ":" +
                        predicate.RegexOptions + "(" +
                        CreateTermKey(predicate.Value) + "," +
                        CreateTermKey(predicate.Argument) + ")";
                case SymbolicBoundsAtom bounds:
                    return "bounds:" +
                        (bounds.IncludeLowerBound ? "lower-inclusive" : "lower-exclusive") + ":" +
                        (bounds.IncludeUpperBound ? "upper-inclusive" : "upper-exclusive") + "(" +
                        CreateTermKey(bounds.Index) + "," +
                        CreateTermKey(bounds.Length) + ")";
                case SymbolicFreshnessAtom freshness:
                    return "fresh:" + CreateTermKey(freshness.Value);
                case SymbolicOwnershipAtom ownership:
                    return "ownership:" + (ownership.Escaped ? "escaped" : "owned") + ":" + CreateTermKey(ownership.Value);
                case SymbolicAliasAtom alias:
                    return CreateAliasAtomKey(alias);
                case SymbolicBorrowAtom borrow:
                    return "borrow:" + borrow.Kind + "(" +
                        CreateTermKey(borrow.Owner) + "," +
                        CreateTermKey(borrow.Borrow) + ")";
                case SymbolicEscapeAtom escape:
                    return "escape:" + escape.Kind + ":" + CreateTermKey(escape.Value);
                case SymbolicReturnedOwnershipAtom returnedOwnership:
                    return "returned-ownership:" + CreateTermKey(returnedOwnership.Value);
                case SymbolicMutationAtom mutation:
                    return "mutation:" + (mutation.CallerVisible ? "caller-visible" : "local") + ":" + CreateTermKey(mutation.Target);
                case SymbolicDisposalAtom disposal:
                    return "disposal:" + disposal.State + ":" + CreateTermKey(disposal.Resource);
                case SymbolicResourceLifetimeAtom resourceLifetime:
                    return "resource-lifetime:" + resourceLifetime.State + ":" + CreateTermKey(resourceLifetime.Resource);
                case SymbolicTypeTestAtom typeTest:
                    return "type-test:" + typeTest.TypeKey + ":" + CreateTermKey(typeTest.Value);
                case SymbolicExceptionPreconditionAtom precondition:
                    return "exception-precondition:" + precondition.Kind + ":" +
                        (precondition.Subject != null ? CreateTermKey(precondition.Subject) : "none") + ":" +
                        CreateConditionKey(precondition.Trigger);
                default:
                    return atom.ToString() ?? string.Empty;
            }
        }

        private static string CreateAliasAtomKey(SymbolicAliasAtom alias)
        {
            var source = CreateTermKey(alias.Source);
            var target = CreateTermKey(alias.Target);
            if (string.CompareOrdinal(source, target) > 0)
            {
                (source, target) = (target, source);
            }

            return "alias:" + (alias.MayAlias ? "may" : "no") + "(" + source + "," + target + ")";
        }

        private static (string AtomKey, bool Polarity) CreateRelationFactCoreKey(
            SymbolicRelationAtom relation,
            bool polarity)
        {
            var left = CreateTermKey(relation.Left);
            var right = CreateTermKey(relation.Right);
            var relationOperator = relation.Operator;

            switch (relationOperator)
            {
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
            {
                (left, right) = (right, left);
            }

            return ("relation:" + relationOperator + "(" + left + "," + right + ")", polarity);
        }

        private static string CreateRelationAtomKey(SymbolicRelationAtom relation)
        {
            var left = CreateTermKey(relation.Left);
            var right = CreateTermKey(relation.Right);
            var relationOperator = relation.Operator;

            switch (relationOperator)
            {
                case SymbolicRelationOperator.Equal:
                case SymbolicRelationOperator.NotEqual:
                    if (string.CompareOrdinal(left, right) > 0)
                    {
                        (left, right) = (right, left);
                    }

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

        private static string CreateTermKey(SymbolicTerm term)
        {
            switch (term)
            {
                case SymbolicBooleanConstantTerm boolean:
                    return "bool:" + (boolean.Value ? "true" : "false");
                case SymbolicIntegerConstantTerm integer:
                    return "int:" + integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case SymbolicStringConstantTerm stringConstant:
                    return "string:" + stringConstant.Value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + stringConstant.Value;
                case SymbolicNullTerm:
                    return "null";
                case SymbolicVariableTerm variable:
                    return "var:" + variable.ValueKind + ":" + variable.Name;
                case SymbolicMemberTerm member:
                    return "member:" + member.ValueKind + ":" + CreateTermKey(member.Receiver) + "." + member.MemberName;
                case SymbolicElementTerm element:
                    return "element:" + element.ValueKind + ":" + CreateTermKey(element.Receiver) + "[" + CreateTermKey(element.Index) + "]";
                case SymbolicStringContentTerm content:
                    return "string-content:" + CreateTermKey(content.Reference);
                case SymbolicStringConcatTerm concat:
                    return CreateStringConcatTermKey(concat);
                case SymbolicNullableHasValueTerm nullableHasValue:
                    return "nullable-has-value:" + nullableHasValue.NullableName;
                case SymbolicNullableValueTerm nullableValue:
                    return "nullable-value:" + nullableValue.NullableName + ":" + nullableValue.Kind;
                case SymbolicLengthTerm length:
                    return "length:" + CreateTermKey(length.Value);
                case SymbolicArrayDimensionLengthTerm dimensionLength:
                    return "array-dimension-length:" +
                        dimensionLength.Dimension.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ":" +
                        CreateTermKey(dimensionLength.Value);
                case SymbolicCountTerm count:
                    return "count:" + CreateTermKey(count.Value);
                case SymbolicBinaryTerm binary:
                    return CreateBinaryTermKey(binary);
                case SymbolicConditionalTerm conditional:
                    return CreateConditionalTermKey(conditional);
                default:
                    return term.ToString() ?? string.Empty;
            }
        }

        private static string CreateConditionalTermKey(SymbolicConditionalTerm conditional)
        {
            var conditionKey = CreateConditionKey(conditional.Condition);
            var whenTrueKey = CreateTermKey(conditional.WhenTrue);
            var whenFalseKey = CreateTermKey(conditional.WhenFalse);
            if (string.Equals(whenTrueKey, whenFalseKey, StringComparison.Ordinal))
            {
                return whenTrueKey;
            }

            if (string.Equals(conditionKey, "const:true", StringComparison.Ordinal))
            {
                return whenTrueKey;
            }

            if (string.Equals(conditionKey, "const:false", StringComparison.Ordinal))
            {
                return whenFalseKey;
            }

            return "conditional(" +
                conditionKey + "," +
                whenTrueKey + "," +
                whenFalseKey + ")";
        }

        private static string CreateStringConcatTermKey(SymbolicStringConcatTerm concat)
        {
            var terms = new List<SymbolicTerm>();
            CollectStringConcatTerms(concat, terms);
            var termKeys = CreateNormalizedStringConcatTermKeys(terms);
            if (termKeys.Count == 1)
            {
                return termKeys[0];
            }

            return "string-concat(" + string.Join(",", termKeys) + ")";
        }

        private static void CollectStringConcatTerms(SymbolicTerm term, ICollection<SymbolicTerm> terms)
        {
            if (term is SymbolicStringConcatTerm concat)
            {
                CollectStringConcatTerms(concat.Left, terms);
                CollectStringConcatTerms(concat.Right, terms);
                return;
            }

            terms.Add(term);
        }

        private static List<string> CreateNormalizedStringConcatTermKeys(IEnumerable<SymbolicTerm> terms)
        {
            var termKeys = new List<string>();
            var pendingLiteral = string.Empty;
            foreach (var term in terms)
            {
                if (term is SymbolicStringConstantTerm stringConstant)
                {
                    pendingLiteral += stringConstant.Value;
                    continue;
                }

                AddPendingStringLiteralKey(termKeys, ref pendingLiteral);
                termKeys.Add(CreateTermKey(term));
            }

            AddPendingStringLiteralKey(termKeys, ref pendingLiteral);
            if (termKeys.Count == 0)
            {
                termKeys.Add(CreateTermKey(new SymbolicStringConstantTerm(string.Empty)));
            }

            return termKeys;
        }

        private static void AddPendingStringLiteralKey(ICollection<string> termKeys, ref string pendingLiteral)
        {
            if (pendingLiteral.Length == 0)
            {
                return;
            }

            termKeys.Add(CreateTermKey(new SymbolicStringConstantTerm(pendingLiteral)));
            pendingLiteral = string.Empty;
        }

        private static string CreateBinaryTermKey(SymbolicBinaryTerm binary)
        {
            if (IsAssociativeCommutativeBinaryTermOperator(binary.Operator))
            {
                var terms = new List<SymbolicTerm>();
                CollectAssociativeBinaryTerms(binary, binary.Operator, terms);
                var operands = CreateNormalizedAssociativeBinaryTermKeys(binary.Operator, terms);
                if (operands.Count == 1)
                {
                    return operands[0];
                }

                operands.Sort(StringComparer.Ordinal);
                return "binary-term:" + binary.Operator + "(" + string.Join(",", operands) + ")";
            }

            var left = CreateTermKey(binary.Left);
            var right = CreateTermKey(binary.Right);
            if (IsRightIdentityBinaryTerm(binary.Operator, binary.Right))
            {
                return left;
            }

            if (IsCommutativeBinaryTermOperator(binary.Operator) &&
                string.CompareOrdinal(left, right) > 0)
            {
                (left, right) = (right, left);
            }

            return "binary-term:" + binary.Operator + "(" + left + "," + right + ")";
        }

        private static void CollectAssociativeBinaryTerms(
            SymbolicTerm term,
            SymbolicBinaryTermOperator binaryOperator,
            ICollection<SymbolicTerm> terms)
        {
            if (term is SymbolicBinaryTerm nested &&
                nested.Operator == binaryOperator)
            {
                CollectAssociativeBinaryTerms(nested.Left, binaryOperator, terms);
                CollectAssociativeBinaryTerms(nested.Right, binaryOperator, terms);
                return;
            }

            terms.Add(term);
        }

        private static List<string> CreateNormalizedAssociativeBinaryTermKeys(
            SymbolicBinaryTermOperator binaryOperator,
            IEnumerable<SymbolicTerm> terms)
        {
            var operands = terms
                .Where(term => !IsIdentityOperand(binaryOperator, term))
                .Select(CreateTermKey)
                .ToList();
            if (operands.Count == 0)
            {
                operands.Add(CreateTermKey(new SymbolicIntegerConstantTerm(
                    binaryOperator == SymbolicBinaryTermOperator.Add ? 0 : 1)));
            }

            return operands;
        }

        private static bool IsIdentityOperand(
            SymbolicBinaryTermOperator binaryOperator,
            SymbolicTerm term)
        {
            return binaryOperator switch
            {
                SymbolicBinaryTermOperator.Add => IsIntegerConstant(term, 0),
                SymbolicBinaryTermOperator.Multiply => IsIntegerConstant(term, 1),
                _ => false,
            };
        }

        private static bool IsRightIdentityBinaryTerm(
            SymbolicBinaryTermOperator binaryOperator,
            SymbolicTerm right)
        {
            return binaryOperator switch
            {
                SymbolicBinaryTermOperator.Subtract => IsIntegerConstant(right, 0),
                SymbolicBinaryTermOperator.Divide => IsIntegerConstant(right, 1),
                _ => false,
            };
        }

        private static bool IsIntegerConstant(SymbolicTerm term, long value)
        {
            return term is SymbolicIntegerConstantTerm integer &&
                integer.Value == value;
        }

        private static bool IsAssociativeCommutativeBinaryTermOperator(SymbolicBinaryTermOperator binaryOperator)
        {
            return binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Multiply;
        }

        private static bool IsCommutativeBinaryTermOperator(SymbolicBinaryTermOperator binaryOperator)
        {
            return binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Multiply;
        }

        private static string CreateConditionKey(SymbolicCondition condition)
        {
            switch (condition)
            {
                case SymbolicConstantCondition constant:
                    return "const:" + (constant.Value ? "true" : "false");
                case SymbolicFactCondition factCondition:
                    if (TryEvaluateFact(factCondition.Fact, out var factValue))
                    {
                        return "const:" + (factValue ? "true" : "false");
                    }

                    return "fact-condition:" + CreateFactKey(factCondition.Fact);
                case SymbolicNotCondition { Operand: SymbolicConstantCondition constantCondition }:
                    return "const:" + (constantCondition.Value ? "false" : "true");
                case SymbolicNotCondition { Operand: SymbolicFactCondition factCondition }:
                    if (TryEvaluateFact(factCondition.Fact, out var negatedFactValue))
                    {
                        return "const:" + (negatedFactValue ? "false" : "true");
                    }

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
                    {
                        return absorbingOperand;
                    }

                    if (ContainsComplementaryConditionOperands(binaryCondition))
                    {
                        return absorbingOperand;
                    }

                    operands.RemoveAll(operand => string.Equals(operand, identityOperand, StringComparison.Ordinal));
                    operands = operands.Distinct(StringComparer.Ordinal).ToList();
                    operands = RemoveAbsorbedConditionOperands(binaryCondition.Operator, operandConditions, operands);
                    if (operands.Count == 0)
                    {
                        return identityOperand;
                    }

                    if (operands.Count == 1)
                    {
                        return operands[0];
                    }

                    operands.Sort(StringComparer.Ordinal);
                    return "binary:" + binaryCondition.Operator + "(" + string.Join(",", operands) + ")";
                default:
                    return condition.ToString() ?? string.Empty;
            }
        }

        private static bool ContainsComplementaryConditionOperands(SymbolicBinaryCondition condition)
        {
            var operands = new List<SymbolicCondition>();
            CollectBinaryConditionOperands(condition, condition.Operator, operands);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operand in operands)
            {
                var key = CreateConditionKey(operand);
                var negatedKey = CreateConditionKey(new SymbolicNotCondition(operand));
                if (seen.Contains(negatedKey))
                {
                    return true;
                }

                seen.Add(key);
            }

            return false;
        }

        private static void CollectBinaryConditionOperands(
            SymbolicCondition condition,
            SymbolicConditionOperator binaryOperator,
            ICollection<SymbolicCondition> operands)
        {
            if (condition is SymbolicBinaryCondition nested &&
                nested.Operator == binaryOperator)
            {
                CollectBinaryConditionOperands(nested.Left, binaryOperator, operands);
                CollectBinaryConditionOperands(nested.Right, binaryOperator, operands);
                return;
            }

            operands.Add(condition);
        }

        private static List<string> RemoveAbsorbedConditionOperands(
            SymbolicConditionOperator conditionOperator,
            IReadOnlyCollection<SymbolicCondition> operandConditions,
            List<string> operandKeys)
        {
            if (operandKeys.Count < 2)
            {
                return operandKeys;
            }

            var keySet = new HashSet<string>(operandKeys, StringComparer.Ordinal);
            var absorbedKeys = new HashSet<string>(StringComparer.Ordinal);
            var oppositeOperator = NegateConditionOperator(conditionOperator);
            foreach (var operandCondition in operandConditions)
            {
                if (operandCondition is not SymbolicBinaryCondition nested ||
                    nested.Operator != oppositeOperator)
                {
                    continue;
                }

                var nestedOperands = new List<SymbolicCondition>();
                CollectBinaryConditionOperands(nested, oppositeOperator, nestedOperands);
                if (nestedOperands
                    .Select(CreateConditionKey)
                    .Any(keySet.Contains))
                {
                    absorbedKeys.Add(CreateConditionKey(operandCondition));
                }
            }

            return absorbedKeys.Count == 0
                ? operandKeys
                : operandKeys
                    .Where(key => !absorbedKeys.Contains(key))
                    .ToList();
        }

        private static SymbolicConditionOperator NegateConditionOperator(SymbolicConditionOperator conditionOperator)
        {
            return conditionOperator == SymbolicConditionOperator.And
                ? SymbolicConditionOperator.Or
                : SymbolicConditionOperator.And;
        }
    }
}
