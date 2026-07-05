using System;
using System.Collections.Immutable;
using System.Globalization;

namespace SharpProof.Symbolic.Ir
{
    internal static class SymbolicIrVersionRewriter
    {
        internal static SymbolicTerm RewriteToCurrentVersions(
            SymbolicTerm term,
            ImmutableDictionary<string, int> symbolVersions)
        {
            if (term == null)
            {
                throw new ArgumentNullException(nameof(term));
            }

            if (symbolVersions.IsEmpty)
            {
                return term;
            }

            return term switch
            {
                SymbolicBooleanConstantTerm or
                    SymbolicIntegerConstantTerm or
                    SymbolicStringConstantTerm or
                    SymbolicNullTerm => term,
                SymbolicVariableTerm variable => RewriteVariableTerm(variable, symbolVersions),
                SymbolicMemberTerm member => new SymbolicMemberTerm(
                    RewriteToCurrentVersions(member.Receiver, symbolVersions),
                    member.MemberName,
                    member.Kind),
                SymbolicElementTerm element => new SymbolicElementTerm(
                    RewriteToCurrentVersions(element.Receiver, symbolVersions),
                    RewriteToCurrentVersions(element.Index, symbolVersions),
                    element.Kind),
                SymbolicStringContentTerm content => new SymbolicStringContentTerm(
                    RewriteToCurrentVersions(content.Reference, symbolVersions)),
                SymbolicStringConcatTerm concat => new SymbolicStringConcatTerm(
                    RewriteToCurrentVersions(concat.Left, symbolVersions),
                    RewriteToCurrentVersions(concat.Right, symbolVersions)),
                SymbolicNullableHasValueTerm nullableHasValue => RewriteNullableHasValueTerm(nullableHasValue, symbolVersions),
                SymbolicNullableValueTerm nullableValue => RewriteNullableValueTerm(nullableValue, symbolVersions),
                SymbolicLengthTerm length => new SymbolicLengthTerm(
                    RewriteToCurrentVersions(length.Value, symbolVersions)),
                SymbolicArrayDimensionLengthTerm dimensionLength => new SymbolicArrayDimensionLengthTerm(
                    RewriteToCurrentVersions(dimensionLength.Value, symbolVersions),
                    dimensionLength.Dimension),
                SymbolicCountTerm count => new SymbolicCountTerm(
                    RewriteToCurrentVersions(count.Value, symbolVersions)),
                SymbolicBinaryTerm binary => new SymbolicBinaryTerm(
                    binary.Operator,
                    RewriteToCurrentVersions(binary.Left, symbolVersions),
                    RewriteToCurrentVersions(binary.Right, symbolVersions)),
                SymbolicConditionalTerm conditional => new SymbolicConditionalTerm(
                    RewriteToCurrentVersions(conditional.Condition, symbolVersions),
                    RewriteToCurrentVersions(conditional.WhenTrue, symbolVersions),
                    RewriteToCurrentVersions(conditional.WhenFalse, symbolVersions)),
                _ => term,
            };
        }

        internal static SymbolicCondition RewriteToCurrentVersions(
            SymbolicCondition condition,
            ImmutableDictionary<string, int> symbolVersions)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            if (symbolVersions.IsEmpty)
            {
                return condition;
            }

            return condition switch
            {
                SymbolicConstantCondition => condition,
                SymbolicFactCondition factCondition => new SymbolicFactCondition(
                    RewriteToCurrentVersions(factCondition.Fact, symbolVersions)),
                SymbolicNotCondition notCondition => new SymbolicNotCondition(
                    RewriteToCurrentVersions(notCondition.Operand, symbolVersions)),
                SymbolicBinaryCondition binaryCondition => new SymbolicBinaryCondition(
                    binaryCondition.Operator,
                    RewriteToCurrentVersions(binaryCondition.Left, symbolVersions),
                    RewriteToCurrentVersions(binaryCondition.Right, symbolVersions)),
                _ => condition,
            };
        }

        internal static SymbolicFact RewriteToCurrentVersions(
            SymbolicFact fact,
            ImmutableDictionary<string, int> symbolVersions)
        {
            if (fact == null)
            {
                throw new ArgumentNullException(nameof(fact));
            }

            if (symbolVersions.IsEmpty)
            {
                return fact;
            }

            return fact with
            {
                Atom = RewriteToCurrentVersions(fact.Atom, symbolVersions),
            };
        }

        private static SymbolicAtom RewriteToCurrentVersions(
            SymbolicAtom atom,
            ImmutableDictionary<string, int> symbolVersions)
        {
            return atom switch
            {
                SymbolicTruthAtom truth => new SymbolicTruthAtom(
                    RewriteToCurrentVersions(truth.Condition, symbolVersions)),
                SymbolicRelationAtom relation => new SymbolicRelationAtom(
                    relation.Operator,
                    RewriteToCurrentVersions(relation.Left, symbolVersions),
                    RewriteToCurrentVersions(relation.Right, symbolVersions)),
                SymbolicStringPredicateAtom predicate => new SymbolicStringPredicateAtom(
                    predicate.Predicate,
                    RewriteToCurrentVersions(predicate.Value, symbolVersions),
                    RewriteToCurrentVersions(predicate.Argument, symbolVersions),
                    predicate.RegexOptions),
                SymbolicBoundsAtom bounds => new SymbolicBoundsAtom(
                    RewriteToCurrentVersions(bounds.Index, symbolVersions),
                    RewriteToCurrentVersions(bounds.Length, symbolVersions),
                    bounds.IncludeLowerBound,
                    bounds.IncludeUpperBound),
                SymbolicFreshnessAtom freshness => new SymbolicFreshnessAtom(
                    RewriteToCurrentVersions(freshness.Value, symbolVersions)),
                SymbolicOwnershipAtom ownership => new SymbolicOwnershipAtom(
                    RewriteToCurrentVersions(ownership.Value, symbolVersions),
                    ownership.Escaped),
                SymbolicAliasAtom alias => new SymbolicAliasAtom(
                    RewriteToCurrentVersions(alias.Source, symbolVersions),
                    RewriteToCurrentVersions(alias.Target, symbolVersions),
                    alias.MayAlias),
                SymbolicBorrowAtom borrow => new SymbolicBorrowAtom(
                    RewriteToCurrentVersions(borrow.Owner, symbolVersions),
                    RewriteToCurrentVersions(borrow.Borrow, symbolVersions),
                    borrow.Kind),
                SymbolicEscapeAtom escape => new SymbolicEscapeAtom(
                    RewriteToCurrentVersions(escape.Value, symbolVersions),
                    escape.Kind),
                SymbolicReturnedOwnershipAtom returnedOwnership => new SymbolicReturnedOwnershipAtom(
                    RewriteToCurrentVersions(returnedOwnership.Value, symbolVersions)),
                SymbolicMutationAtom mutation => new SymbolicMutationAtom(
                    RewriteToCurrentVersions(mutation.Target, symbolVersions),
                    mutation.CallerVisible),
                SymbolicDisposalAtom disposal => new SymbolicDisposalAtom(
                    RewriteToCurrentVersions(disposal.Resource, symbolVersions),
                    disposal.State),
                SymbolicResourceLifetimeAtom resourceLifetime => new SymbolicResourceLifetimeAtom(
                    RewriteToCurrentVersions(resourceLifetime.Resource, symbolVersions),
                    resourceLifetime.State),
                SymbolicTypeTestAtom typeTest => new SymbolicTypeTestAtom(
                    RewriteToCurrentVersions(typeTest.Value, symbolVersions),
                    typeTest.TypeKey),
                SymbolicExceptionPreconditionAtom precondition => new SymbolicExceptionPreconditionAtom(
                    precondition.Kind,
                    precondition.Subject != null
                        ? RewriteToCurrentVersions(precondition.Subject, symbolVersions)
                        : null,
                    RewriteToCurrentVersions(precondition.Trigger, symbolVersions)),
                _ => atom,
            };
        }

        private static SymbolicVariableTerm RewriteVariableTerm(
            SymbolicVariableTerm variable,
            ImmutableDictionary<string, int> symbolVersions)
        {
            var rewrittenName = RewriteVariableLikeName(variable.Name, symbolVersions);
            return string.Equals(rewrittenName, variable.Name, StringComparison.Ordinal)
                ? variable
                : new SymbolicVariableTerm(rewrittenName, variable.Kind);
        }

        private static SymbolicNullableHasValueTerm RewriteNullableHasValueTerm(
            SymbolicNullableHasValueTerm term,
            ImmutableDictionary<string, int> symbolVersions)
        {
            var rewrittenName = RewriteVariableLikeName(term.NullableName, symbolVersions);
            return string.Equals(rewrittenName, term.NullableName, StringComparison.Ordinal)
                ? term
                : new SymbolicNullableHasValueTerm(rewrittenName);
        }

        private static SymbolicNullableValueTerm RewriteNullableValueTerm(
            SymbolicNullableValueTerm term,
            ImmutableDictionary<string, int> symbolVersions)
        {
            var rewrittenName = RewriteVariableLikeName(term.NullableName, symbolVersions);
            return string.Equals(rewrittenName, term.NullableName, StringComparison.Ordinal)
                ? term
                : new SymbolicNullableValueTerm(rewrittenName, term.Kind);
        }

        private static string RewriteVariableLikeName(
            string name,
            ImmutableDictionary<string, int> symbolVersions)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            var (baseName, currentVersion) = SplitVersionedName(name);
            if (!symbolVersions.TryGetValue(baseName, out var targetVersion) ||
                currentVersion == targetVersion)
            {
                return name;
            }

            return targetVersion > 0
                ? baseName + "@v" + targetVersion.ToString(CultureInfo.InvariantCulture)
                : baseName;
        }

        private static (string BaseName, int Version) SplitVersionedName(string name)
        {
            var markerIndex = name.LastIndexOf("@v", StringComparison.Ordinal);
            if (markerIndex < 0 ||
                markerIndex + 2 >= name.Length ||
                !int.TryParse(
                    name.Substring(markerIndex + 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var version))
            {
                return (name, 0);
            }

            return (name.Substring(0, markerIndex), version);
        }
    }
}
