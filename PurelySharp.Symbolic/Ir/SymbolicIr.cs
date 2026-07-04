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

    internal sealed record SymbolicStringContentTerm(SymbolicTerm Reference) : SymbolicTerm(SmtValueKind.String);

    internal sealed record SymbolicStringConcatTerm(SymbolicTerm Left, SymbolicTerm Right) : SymbolicTerm(SmtValueKind.String);

    internal sealed record SymbolicNullableHasValueTerm(string NullableName) : SymbolicTerm(SmtValueKind.Bool);

    internal sealed record SymbolicLengthTerm(SymbolicTerm Value) : SymbolicTerm(SmtValueKind.Int);

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
            var normalizedConditions = DeduplicateConditions(pathConditions?.ToImmutableArray() ?? ImmutableArray<SymbolicCondition>.Empty);
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
            var normalizedConditions = DeduplicateConditions(PathConditions);
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

        private static ImmutableArray<SymbolicCondition> DeduplicateConditions(ImmutableArray<SymbolicCondition> conditions)
        {
            if (conditions.IsDefaultOrEmpty)
            {
                return ImmutableArray<SymbolicCondition>.Empty;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var builder = ImmutableArray.CreateBuilder<SymbolicCondition>(conditions.Length);
            foreach (var condition in conditions)
            {
                if (condition == null)
                {
                    continue;
                }

                if (seen.Add(CreateConditionKey(condition)))
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
            foreach (var fact in facts)
            {
                if (HasOppositePolarity(polarities, fact))
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
                }
            }

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
            var key = CreateFactAtomKey(fact);
            if (polarities.TryGetValue(key, out var existingPolarity))
            {
                return existingPolarity != fact.Polarity;
            }

            polarities.Add(key, fact.Polarity);
            return false;
        }

        private static bool ContainsFalseConstant(SymbolicCondition condition)
        {
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
                case SymbolicNotCondition { Operand: SymbolicFactCondition factCondition }:
                    yield return factCondition.Fact.Negate();
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
            var parts = new List<string>
            {
                isContradictory ? "contradictory:true" : "contradictory:false",
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

        private static string CreateFactKey(SymbolicFact fact)
        {
            return string.Join(
                "|",
                CreateFactAtomKey(fact),
                fact.Polarity ? "true" : "false",
                fact.Confidence.ToString());
        }

        private static string CreateFactAtomKey(SymbolicFact fact)
        {
            return fact.Atom.ToString() ?? string.Empty;
        }

        private static string CreateConditionKey(SymbolicCondition condition)
        {
            switch (condition)
            {
                case SymbolicConstantCondition constant:
                    return "const:" + (constant.Value ? "true" : "false");
                case SymbolicFactCondition factCondition:
                    return "fact-condition:" + CreateFactKey(factCondition.Fact);
                case SymbolicNotCondition { Operand: SymbolicConstantCondition constantCondition }:
                    return "const:" + (constantCondition.Value ? "false" : "true");
                case SymbolicNotCondition { Operand: SymbolicFactCondition factCondition }:
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
                    var operands = new List<string>();
                    CollectBinaryConditionOperandKeys(binaryCondition, binaryCondition.Operator, operands);
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

                    operands.RemoveAll(operand => string.Equals(operand, identityOperand, StringComparison.Ordinal));
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

        private static void CollectBinaryConditionOperandKeys(
            SymbolicCondition condition,
            SymbolicConditionOperator binaryOperator,
            ICollection<string> operands)
        {
            if (condition is SymbolicBinaryCondition nested &&
                nested.Operator == binaryOperator)
            {
                CollectBinaryConditionOperandKeys(nested.Left, binaryOperator, operands);
                CollectBinaryConditionOperandKeys(nested.Right, binaryOperator, operands);
                return;
            }

            operands.Add(CreateConditionKey(condition));
        }

        private static SymbolicConditionOperator NegateConditionOperator(SymbolicConditionOperator conditionOperator)
        {
            return conditionOperator == SymbolicConditionOperator.And
                ? SymbolicConditionOperator.Or
                : SymbolicConditionOperator.And;
        }
    }
}
