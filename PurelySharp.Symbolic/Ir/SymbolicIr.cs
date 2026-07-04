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
            var key = CreateFactCoreKey(fact);
            if (polarities.TryGetValue(key.AtomKey, out var existingPolarity))
            {
                return existingPolarity != key.Polarity;
            }

            polarities.Add(key.AtomKey, key.Polarity);
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
                    return "alias:" + (alias.MayAlias ? "may" : "no") + "(" +
                        CreateTermKey(alias.Source) + "," +
                        CreateTermKey(alias.Target) + ")";
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
                case SymbolicStringContentTerm content:
                    return "string-content:" + CreateTermKey(content.Reference);
                case SymbolicStringConcatTerm concat:
                    return CreateStringConcatTermKey(concat);
                case SymbolicNullableHasValueTerm nullableHasValue:
                    return "nullable-has-value:" + nullableHasValue.NullableName;
                case SymbolicLengthTerm length:
                    return "length:" + CreateTermKey(length.Value);
                case SymbolicCountTerm count:
                    return "count:" + CreateTermKey(count.Value);
                case SymbolicBinaryTerm binary:
                    return CreateBinaryTermKey(binary);
                case SymbolicConditionalTerm conditional:
                    return "conditional(" +
                        CreateConditionKey(conditional.Condition) + "," +
                        CreateTermKey(conditional.WhenTrue) + "," +
                        CreateTermKey(conditional.WhenFalse) + ")";
                default:
                    return term.ToString() ?? string.Empty;
            }
        }

        private static string CreateStringConcatTermKey(SymbolicStringConcatTerm concat)
        {
            var terms = new List<string>();
            CollectStringConcatTermKeys(concat, terms);
            return "string-concat(" + string.Join(",", terms) + ")";
        }

        private static void CollectStringConcatTermKeys(SymbolicTerm term, ICollection<string> terms)
        {
            if (term is SymbolicStringConcatTerm concat)
            {
                CollectStringConcatTermKeys(concat.Left, terms);
                CollectStringConcatTermKeys(concat.Right, terms);
                return;
            }

            terms.Add(CreateTermKey(term));
        }

        private static string CreateBinaryTermKey(SymbolicBinaryTerm binary)
        {
            var left = CreateTermKey(binary.Left);
            var right = CreateTermKey(binary.Right);
            if (IsCommutativeBinaryTermOperator(binary.Operator) &&
                string.CompareOrdinal(left, right) > 0)
            {
                (left, right) = (right, left);
            }

            return "binary-term:" + binary.Operator + "(" + left + "," + right + ")";
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
