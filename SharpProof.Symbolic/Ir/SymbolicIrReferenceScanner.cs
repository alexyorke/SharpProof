namespace SharpProof.Symbolic.Ir;

internal static class SymbolicIrReferenceScanner
{
    internal static bool ContainsVariablePrefix(SymbolicFact fact, string variablePrefix)
    {
        return ContainsVariable(fact.Atom, name => MatchesVariablePrefix(name, variablePrefix));
    }

    internal static bool ContainsVariablePrefix(SymbolicCondition condition, string variablePrefix)
    {
        return ContainsVariable(condition, name => MatchesVariablePrefix(name, variablePrefix));
    }

    internal static bool ContainsVariableOrMember(SymbolicFact fact, string variableName)
    {
        return ContainsVariable(fact.Atom, name => MatchesVariableOrMember(name, variableName));
    }

    internal static bool ContainsVariableOrMember(SymbolicCondition condition, string variableName)
    {
        return ContainsVariable(condition, name => MatchesVariableOrMember(name, variableName));
    }

    internal static SymbolicState RemoveVariableReferences(SymbolicState state, string variablePrefix)
    {
        var remainingFacts = state.Facts
            .Where(fact => !ContainsVariablePrefix(fact, variablePrefix));
        var remainingConditions = state.PathConditions
            .Where(condition => !ContainsVariablePrefix(condition, variablePrefix));
        return new SymbolicState(
            remainingFacts,
            remainingConditions,
            state.SymbolVersions).Normalize();
    }

    internal static SymbolicState RemoveVariableOrMemberReferences(SymbolicState state, string variableName)
    {
        var remainingFacts = state.Facts
            .Where(fact => !ContainsVariableOrMember(fact, variableName));
        var remainingConditions = state.PathConditions
            .Where(condition => !ContainsVariableOrMember(condition, variableName));
        return new SymbolicState(
            remainingFacts,
            remainingConditions,
            state.SymbolVersions).Normalize();
    }

    private static bool ContainsVariable(SymbolicAtom atom, Func<string, bool> match)
    {
        return atom switch
        {
            SymbolicTruthAtom truth => ContainsVariable(truth.Condition, match),
            SymbolicRelationAtom relation => ContainsVariable(relation.Left, match) ||
                                             ContainsVariable(relation.Right, match),
            SymbolicStringPredicateAtom predicate => ContainsVariable(predicate.Value, match) ||
                                                     ContainsVariable(predicate.Argument, match),
            SymbolicBoundsAtom bounds => ContainsVariable(bounds.Index, match) ||
                                         ContainsVariable(bounds.Length, match),
            SymbolicFreshnessAtom freshness => ContainsVariable(freshness.Value, match),
            SymbolicOwnershipAtom ownership => ContainsVariable(ownership.Value, match),
            SymbolicAliasAtom alias => ContainsVariable(alias.Source, match) ||
                                       ContainsVariable(alias.Target, match),
            SymbolicBorrowAtom borrow => ContainsVariable(borrow.Owner, match) ||
                                         ContainsVariable(borrow.Borrow, match),
            SymbolicEscapeAtom escape => ContainsVariable(escape.Value, match),
            SymbolicReturnedOwnershipAtom returnedOwnership => ContainsVariable(returnedOwnership.Value, match),
            SymbolicMutationAtom mutation => ContainsVariable(mutation.Target, match),
            SymbolicDisposalAtom disposal => ContainsVariable(disposal.Resource, match),
            SymbolicResourceLifetimeAtom lifetime => ContainsVariable(lifetime.Resource, match),
            SymbolicTypeTestAtom typeTest => ContainsVariable(typeTest.Value, match),
            SymbolicExceptionPreconditionAtom exceptionPrecondition =>
                (exceptionPrecondition.Subject != null &&
                 ContainsVariable(exceptionPrecondition.Subject, match)) ||
                ContainsVariable(exceptionPrecondition.Trigger, match),
            _ => false
        };
    }

    private static bool ContainsVariable(SymbolicCondition condition, Func<string, bool> match)
    {
        return condition switch
        {
            SymbolicConstantCondition => false,
            SymbolicFactCondition factCondition => ContainsVariable(factCondition.Fact.Atom, match),
            SymbolicNotCondition notCondition => ContainsVariable(notCondition.Operand, match),
            SymbolicBinaryCondition binaryCondition => ContainsVariable(binaryCondition.Left, match) ||
                                                       ContainsVariable(binaryCondition.Right, match),
            _ => false
        };
    }

    private static bool ContainsVariable(SymbolicTerm term, Func<string, bool> match)
    {
        return term switch
        {
            SymbolicBooleanConstantTerm or
                SymbolicIntegerConstantTerm or
                SymbolicStringConstantTerm or
                SymbolicNullTerm => false,
            SymbolicVariableTerm variable => match(variable.Name),
            SymbolicMemberTerm member => ContainsVariable(member.Receiver, match),
            SymbolicElementTerm element => ContainsVariable(element.Receiver, match) ||
                                           ContainsVariable(element.Index, match),
            SymbolicMultiElementTerm element => ContainsVariable(element.Receiver, match) ||
                                                element.Indices.Any(index => ContainsVariable(index, match)),
            SymbolicFromEndIndexTerm fromEnd => ContainsVariable(fromEnd.Value, match),
            SymbolicStringContentTerm content => ContainsVariable(content.Reference, match),
            SymbolicStringConcatTerm concat => ContainsVariable(concat.Left, match) ||
                                               ContainsVariable(concat.Right, match),
            SymbolicNullableHasValueTerm nullableHasValue => match(nullableHasValue.NullableName),
            SymbolicNullableValueTerm nullableValue => match(nullableValue.NullableName),
            SymbolicLengthTerm length => ContainsVariable(length.Value, match),
            SymbolicArrayDimensionLengthTerm arrayLength => ContainsVariable(arrayLength.Value, match),
            SymbolicCountTerm count => ContainsVariable(count.Value, match),
            SymbolicBinaryTerm binary => ContainsVariable(binary.Left, match) ||
                                         ContainsVariable(binary.Right, match),
            SymbolicConditionalTerm conditional => ContainsVariable(conditional.Condition, match) ||
                                                   ContainsVariable(conditional.WhenTrue, match) ||
                                                   ContainsVariable(conditional.WhenFalse, match),
            _ => false
        };
    }

    private static bool MatchesVariablePrefix(string candidate, string variablePrefix)
    {
        if (MatchesVariableOrMember(candidate, variablePrefix)) return true;

        var versionPrefix = variablePrefix + "@v";
        if (!candidate.StartsWith(versionPrefix, StringComparison.Ordinal)) return false;

        var index = versionPrefix.Length;
        var digitStart = index;
        while (index < candidate.Length && char.IsDigit(candidate[index])) index++;

        return index > digitStart &&
               (index == candidate.Length || candidate[index] is '.' or '[');
    }

    private static bool MatchesVariableOrMember(string candidate, string variableName)
    {
        return string.Equals(candidate, variableName, StringComparison.Ordinal) ||
               candidate.StartsWith(variableName + ".", StringComparison.Ordinal) ||
               candidate.StartsWith(variableName + "[", StringComparison.Ordinal);
    }
}
