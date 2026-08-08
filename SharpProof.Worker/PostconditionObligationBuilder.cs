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

            var interval = IntervalDomain.Instance.Range(sourceInterval.Minimum, sourceInterval.Maximum);
            if (interval.IsBottom)
            {
                return false;
            }

            IEnumerable<(IrTerm? Term, IrTerm? Guard)> values =
                variable.Role == CompilerVariableRole.Result
                ? returns.Select(static path => (path.ReturnTerm, Guard: (IrTerm?)path.Predicate))
                : [(factory.Variable(variable.Variable), Guard: (IrTerm?)null)];
            foreach (var (term, guard) in values)
            {
                if (term == null || term.Type != factory.IntegerType ||
                    !SpecResultDomainProjection.TryCreateIntervalPredicate(
                        factory, term, interval, out var predicate) || predicate == null)
                {
                    return false;
                }

                AddDomainAssumption(guard == null ? predicate : Guard(factory,
                    SpecResultDomainProjection.Rewrite(factory, guard, projections), predicate), variable);
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
        if (predicate is IrBooleanTerm { Value: true } || assumptions.Any(assumption =>
                assumption.Predicate.Id == predicate.Id && assumption.Justification is not UserAssumedJustification))
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
