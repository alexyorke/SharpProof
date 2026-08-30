using static SharpProof.Ir.IrSemanticTerms;

namespace SharpProof.Worker;

internal static class PostconditionObligationBuilder
{
    internal static bool TryAddSourceDomainAssumptions(
        IrFactory factory, ImmutableArray<CompilerCanonicalVariable> variables, ImmutableArray<SymbolicReturn> returns,
        ImmutableDictionary<IrVarId, SpecResultProjection> projections,
        ImmutableArray<Assumption>.Builder assumptions,
        ImmutableArray<Assumption>.Builder entryDomainAssumptions,
        Dictionary<ProofJustification, string> assumptionLabels)
    {
        var seenPredicates = assumptions.Select(static assumption => assumption.Predicate.Id).ToHashSet();
        foreach (var variable in variables
                     .Where(static variable => variable.Role is CompilerVariableRole.Receiver
                         or CompilerVariableRole.Parameter or CompilerVariableRole.Result)
                     .OrderBy(static variable => Domain(variable).Order)
                     .ThenBy(static variable => variable.Ordinal))
        {
            if (variable.SourceIntegerInterval is not { } sourceInterval)
            {
                continue;
            }

            IEnumerable<(IrTerm? Term, IrTerm? Guard)> values =
                variable.Role == CompilerVariableRole.Result
                ? returns.Select(static path => (path.ReturnTerm, Guard: (IrTerm?)path.Predicate))
                : [(factory.Variable(variable.Variable), Guard: (IrTerm?)null)];
            foreach (var (term, guard) in values)
            {
                var projectedTerm = term == null ? null :
                    SpecResultDomainProjection.Rewrite(
                        factory,
                        term,
                        projections);
                if (!TryCreateSourceDomainPredicate(
                        factory,
                        projectedTerm,
                        sourceInterval,
                        out var predicate))
                {
                    return false;
                }
                if (predicate == null)
                {
                    continue;
                }

                var projectedGuard = guard == null ? null :
                    SpecResultDomainProjection.Rewrite(
                        factory,
                        guard,
                        projections);
                AddDomainAssumption(
                    projectedGuard == null
                        ? predicate
                        : Guard(factory, projectedGuard, predicate),
                    variable);
            }
        }
        return true;

        void AddDomainAssumption(IrTerm predicate, CompilerCanonicalVariable variable)
        {
            if (predicate is IrBooleanTerm { Value: true } || !seenPredicates.Add(predicate.Id))
            {
                return;
            }

            var label = Domain(variable).Label;
            ProofJustification justification =
                new LoweredJustification(factory.CreateOperation("source-" + label));
            var assumption = new Assumption(factory, predicate, justification);
            assumptions.Add(assumption);
            if (variable.Role is
                CompilerVariableRole.Receiver or
                CompilerVariableRole.Parameter)
            {
                entryDomainAssumptions.Add(assumption);
            }
            assumptionLabels.Add(justification, label);
        }
    }

    internal static bool TryCreateSourceDomainPredicate(
        IrFactory factory,
        IrTerm? term,
        CompilerIntegerInterval sourceInterval,
        out IrTerm? predicate)
    {
        var interval = IntervalDomain.Instance.Range(
            sourceInterval.Minimum,
            sourceInterval.Maximum);
        if (interval.IsBottom)
        {
            predicate = null;
            return false;
        }
        if (interval.Equals(IntervalValue.Top))
        {
            predicate = null;
            return true;
        }
        if (term == null || term.Type != factory.IntegerType)
        {
            predicate = null;
            return false;
        }

        return SpecResultDomainProjection.TryCreateIntervalPredicate(
            factory,
            term,
            interval,
            out predicate);
    }

    internal static IrTerm AddNormalCompletionAssumption(
        IrFactory factory, ImmutableArray<SymbolicReturn> returns,
        ImmutableDictionary<IrVarId, SpecResultProjection> projections, ImmutableArray<Assumption>.Builder assumptions,
        Dictionary<ProofJustification, string> assumptionLabels)
    {
        var completions = ImmutableArray.CreateBuilder<IrTerm>(returns.Length);
        foreach (var path in returns)
        {
            var completion = ConstrainSuccessfulEvaluation(
                factory,
                path.Predicate,
                path.ReturnTerm);
            completions.Add(SpecResultDomainProjection.Rewrite(factory, completion, projections));
        }
        var predicate = Disjoin(factory, completions);
        if (predicate is IrBooleanTerm { Value: true })
        {
            return predicate;
        }

        var hasNonUserDuplicate = false;
        var promotedResultDomain = false;
        for (var index = assumptions.Count - 1; index >= 0; index--)
        {
            var existing = assumptions[index];
            if (existing.Predicate.Id != predicate.Id ||
                existing.Justification is UserAssumedJustification)
            {
                continue;
            }

            hasNonUserDuplicate = true;
            if (!assumptionLabels.TryGetValue(
                    existing.Justification,
                    out var label) ||
                label != "domain:result")
            {
                continue;
            }

            assumptions.RemoveAt(index);
            assumptionLabels.Remove(existing.Justification);
            promotedResultDomain = true;
        }
        if (hasNonUserDuplicate && !promotedResultDomain)
        {
            return predicate;
        }

        ProofJustification justification =
            new LoweredJustification(factory.CreateOperation("body:normal-completion"));
        assumptions.Add(new Assumption(factory, predicate, justification));
        assumptionLabels.Add(justification, "body:normal-completion");
        return predicate;
    }

    internal static bool IsSupportedProofDomain(IrFactory factory, IrTerm root)
    {
        var pending = new Stack<IrTerm>();
        var visited = new HashSet<IrId>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var term = pending.Pop();
            if (!visited.Add(term.Id))
            {
                continue;
            }

            if (term is IrVariableTerm variable &&
                factory.GetTypeInfo(variable.Type).Kind is not (IrTypeKind.Boolean or IrTypeKind.Integer))
            {
                return false;
            }

            if (term is IrBinaryTerm { Operator: IrBinaryOperator.StringConcat })
            {
                return false;
            }

            if (term is IrLengthTerm length && length.Value.Type == factory.StringType)
            {
                return false;
            }

            foreach (var child in IrTraversal.GetChildren(term))
            {
                pending.Push(child);
            }
        }
        return true;
    }

    internal static IrTerm? ApplyBodySubstitutions(
        IrFactory factory, IrTerm term, ImmutableArray<CompilerCanonicalVariable> variables,
        IrTerm? returnTerm, IReadOnlyDictionary<IrVarId, IrTerm> currentStates,
        bool allowMissingResult = false)
    {
        var replacements = new Dictionary<IrVarId, IrTerm>();
        foreach (var variable in variables)
        {
            if (variable.Role == CompilerVariableRole.PreState &&
                variable.CurrentStateVariable.HasValue)
            {
                replacements[variable.Variable] = factory.Variable(variable.CurrentStateVariable.Value);
            }
            else if (variable.Role == CompilerVariableRole.Result && returnTerm != null)
            {
                replacements[variable.Variable] = returnTerm;
            }
            else if (variable.Role == CompilerVariableRole.Result &&
                     !allowMissingResult &&
                     IrTraversal.CollectVariables(term).Contains(variable.Variable))
            {
                return null;
            }
        }
        foreach (var currentState in currentStates)
        {
            replacements[currentState.Key] = currentState.Value;
        }

        try
        {
            return IrSubstitution.Substitute(factory, term, replacements);
        }
        catch (ArgumentException) { return null; }
    }

    private static (int Order, string Label) Domain(
        CompilerCanonicalVariable variable)
    {
        return variable.Role switch
        {
            CompilerVariableRole.Receiver => (0, "domain:receiver"),
            CompilerVariableRole.Parameter => (1, "domain:parameter:" +
                variable.Ordinal.ToString(CultureInfo.InvariantCulture)),
            CompilerVariableRole.Result => (2, "domain:result"),
            _ => throw new ArgumentOutOfRangeException(nameof(variable))
        };
    }
}
