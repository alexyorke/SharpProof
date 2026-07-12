using System.Collections.Immutable;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicIrSubstitution
{
    internal static SymbolicFact ReplaceTerm(
        SymbolicFact fact,
        SymbolicTerm source,
        SymbolicTerm replacement)
    {
        var sourceKey = SymbolicState.CreateProofTermKey(source);
        return fact with { Atom = ReplaceTerm(fact.Atom, sourceKey, replacement) };
    }

    internal static SymbolicCondition ReplaceTerm(
        SymbolicCondition condition,
        SymbolicTerm source,
        SymbolicTerm replacement)
    {
        return ReplaceTerm(condition, SymbolicState.CreateProofTermKey(source), replacement);
    }

    private static SymbolicCondition ReplaceTerm(
        SymbolicCondition condition,
        string sourceKey,
        SymbolicTerm replacement)
    {
        return condition switch
        {
            SymbolicConstantCondition => condition,
            SymbolicFactCondition factCondition => new SymbolicFactCondition(
                factCondition.Fact with
                {
                    Atom = ReplaceTerm(factCondition.Fact.Atom, sourceKey, replacement)
                }),
            SymbolicNotCondition notCondition => new SymbolicNotCondition(
                ReplaceTerm(notCondition.Operand, sourceKey, replacement)),
            SymbolicBinaryCondition binaryCondition => new SymbolicBinaryCondition(
                binaryCondition.Operator,
                ReplaceTerm(binaryCondition.Left, sourceKey, replacement),
                ReplaceTerm(binaryCondition.Right, sourceKey, replacement)),
            _ => condition
        };
    }

    private static SymbolicAtom ReplaceTerm(
        SymbolicAtom atom,
        string sourceKey,
        SymbolicTerm replacement)
    {
        return atom switch
        {
            SymbolicTruthAtom truth => new SymbolicTruthAtom(
                ReplaceTerm(truth.Condition, sourceKey, replacement)),
            SymbolicRelationAtom relation => new SymbolicRelationAtom(
                relation.Operator,
                ReplaceTerm(relation.Left, sourceKey, replacement),
                ReplaceTerm(relation.Right, sourceKey, replacement)),
            SymbolicStringPredicateAtom predicate => new SymbolicStringPredicateAtom(
                predicate.Predicate,
                ReplaceTerm(predicate.Value, sourceKey, replacement),
                ReplaceTerm(predicate.Argument, sourceKey, replacement),
                predicate.RegexOptions),
            SymbolicBoundsAtom bounds => new SymbolicBoundsAtom(
                ReplaceTerm(bounds.Index, sourceKey, replacement),
                ReplaceTerm(bounds.Length, sourceKey, replacement),
                bounds.IncludeLowerBound,
                bounds.IncludeUpperBound),
            SymbolicFreshnessAtom freshness => new SymbolicFreshnessAtom(
                ReplaceTerm(freshness.Value, sourceKey, replacement)),
            SymbolicOwnershipAtom ownership => new SymbolicOwnershipAtom(
                ReplaceTerm(ownership.Value, sourceKey, replacement),
                ownership.Escaped),
            SymbolicAliasAtom alias => new SymbolicAliasAtom(
                ReplaceTerm(alias.Source, sourceKey, replacement),
                ReplaceTerm(alias.Target, sourceKey, replacement),
                alias.MayAlias),
            SymbolicBorrowAtom borrow => new SymbolicBorrowAtom(
                ReplaceTerm(borrow.Owner, sourceKey, replacement),
                ReplaceTerm(borrow.Borrow, sourceKey, replacement),
                borrow.Kind),
            SymbolicEscapeAtom escape => new SymbolicEscapeAtom(
                ReplaceTerm(escape.Value, sourceKey, replacement),
                escape.Kind),
            SymbolicReturnedOwnershipAtom returned => new SymbolicReturnedOwnershipAtom(
                ReplaceTerm(returned.Value, sourceKey, replacement)),
            SymbolicMutationAtom mutation => new SymbolicMutationAtom(
                ReplaceTerm(mutation.Target, sourceKey, replacement),
                mutation.CallerVisible),
            SymbolicDisposalAtom disposal => new SymbolicDisposalAtom(
                ReplaceTerm(disposal.Resource, sourceKey, replacement),
                disposal.State),
            SymbolicResourceLifetimeAtom lifetime => new SymbolicResourceLifetimeAtom(
                ReplaceTerm(lifetime.Resource, sourceKey, replacement),
                lifetime.State),
            SymbolicTypeTestAtom typeTest => new SymbolicTypeTestAtom(
                ReplaceTerm(typeTest.Value, sourceKey, replacement),
                typeTest.TypeKey),
            SymbolicExceptionPreconditionAtom precondition => new SymbolicExceptionPreconditionAtom(
                precondition.Kind,
                precondition.Subject == null
                    ? null
                    : ReplaceTerm(precondition.Subject, sourceKey, replacement),
                ReplaceTerm(precondition.Trigger, sourceKey, replacement)),
            _ => atom
        };
    }

    private static SymbolicTerm ReplaceTerm(
        SymbolicTerm term,
        string sourceKey,
        SymbolicTerm replacement)
    {
        if (string.Equals(SymbolicState.CreateProofTermKey(term), sourceKey, StringComparison.Ordinal))
            return replacement;

        return term switch
        {
            SymbolicMemberTerm member => new SymbolicMemberTerm(
                ReplaceTerm(member.Receiver, sourceKey, replacement),
                member.MemberName,
                member.Kind),
            SymbolicElementTerm element => new SymbolicElementTerm(
                ReplaceTerm(element.Receiver, sourceKey, replacement),
                ReplaceTerm(element.Index, sourceKey, replacement),
                element.Kind),
            SymbolicMultiElementTerm element => new SymbolicMultiElementTerm(
                ReplaceTerm(element.Receiver, sourceKey, replacement),
                element.Indices
                    .Select(index => ReplaceTerm(index, sourceKey, replacement))
                    .ToImmutableArray(),
                element.Kind),
            SymbolicFromEndIndexTerm fromEnd => new SymbolicFromEndIndexTerm(
                ReplaceTerm(fromEnd.Value, sourceKey, replacement)),
            SymbolicStringContentTerm content => new SymbolicStringContentTerm(
                ReplaceTerm(content.Reference, sourceKey, replacement)),
            SymbolicStringConcatTerm concat => new SymbolicStringConcatTerm(
                ReplaceTerm(concat.Left, sourceKey, replacement),
                ReplaceTerm(concat.Right, sourceKey, replacement)),
            SymbolicLengthTerm length => new SymbolicLengthTerm(
                ReplaceTerm(length.Value, sourceKey, replacement)),
            SymbolicArrayDimensionLengthTerm length => new SymbolicArrayDimensionLengthTerm(
                ReplaceTerm(length.Value, sourceKey, replacement),
                length.Dimension),
            SymbolicCountTerm count => new SymbolicCountTerm(
                ReplaceTerm(count.Value, sourceKey, replacement)),
            SymbolicBinaryTerm binary => new SymbolicBinaryTerm(
                binary.Operator,
                ReplaceTerm(binary.Left, sourceKey, replacement),
                ReplaceTerm(binary.Right, sourceKey, replacement),
                binary.MayOverflow),
            SymbolicConditionalTerm conditional => new SymbolicConditionalTerm(
                ReplaceTerm(conditional.Condition, sourceKey, replacement),
                ReplaceTerm(conditional.WhenTrue, sourceKey, replacement),
                ReplaceTerm(conditional.WhenFalse, sourceKey, replacement)),
            _ => term
        };
    }
}
