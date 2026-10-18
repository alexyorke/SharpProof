namespace SharpProof.Contracts;

// Transfers all requested source slots to a compiler-bound destination frame.
// Whole-frame callers may include unused declared slots. Failure publishes no
// map; earlier pre-state callback/factory allocations are not rolled back.
internal static class ContractVariableTransfer
{
    internal static ImmutableDictionary<IrVarId, IrTerm>? CreateSubstitutions(
        IrFactory factory,
        IMethodSymbol source,
        bool usesCompanion,
        ImmutableArray<FrontendVariableBinding> sourceParameters,
        IrVarId? sourceResult,
        ImmutableArray<IrVarId> sourceReceivers,
        ImmutableArray<KeyValuePair<IrVarId, IrVarId>> sourcePreStates,
        ImmutableArray<BoundContractVariable> destinationVariables,
        Func<IrVarId, IrVarId> createPreState)
    {




        RepositoryParameterGuard.RequireInitialized(destinationVariables.IsDefault, nameof(destinationVariables), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(sourceParameters.IsDefault, nameof(sourceParameters), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(sourcePreStates.IsDefault, nameof(sourcePreStates), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(sourceReceivers.IsDefault, nameof(sourceReceivers), "Array must be initialized.");
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        source = ArgumentNullGuard.NotNull(source, nameof(source));
        createPreState = ArgumentNullGuard.NotNull(createPreState, nameof(createPreState));
        // Actual compiler-bound snapshots have unique receiver/result roles
        // and parameter ordinals, as guaranteed by their binding producer.
        var receiver = destinationVariables
            .Where(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.Receiver))
            .Select(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : ((IrVarId?)variable.Variable)).SingleOrDefault();
        var result = destinationVariables
            .Where(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.Result))
            .Select(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : ((IrVarId?)variable.Variable)).SingleOrDefault();
        var parameters = destinationVariables
            .Where(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Role == BoundContractVariableRole.Parameter))
            .OrderBy(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Ordinal)).Select(variable => variable is null ? throw new System.ArgumentNullException(nameof(variable)) : (variable.Variable)).ToImmutableArray();
        var replacements = ImmutableDictionary<IrVarId, IrVariableTerm>.Empty;
        foreach (var binding in sourceParameters)
        {
            IrVarId? canonicalVariable = null;
            if (binding.Symbol is INamedTypeSymbol instanceType &&
                !usesCompanion && !source.IsStatic &&
                SymbolEqualityComparer.Default.Equals(instanceType.OriginalDefinition,
                    source.ContainingType.OriginalDefinition))
            {
                canonicalVariable = receiver;
            }
            else if (binding.Symbol is IParameterSymbol parameter &&
                parameter.ContainingSymbol is IMethodSymbol owner &&
                ContractClauseInventoryBuilder.HaveSameDefinition(owner, source))
            {
                var ordinal = parameter.Ordinal - (usesCompanion && receiver.HasValue ? 1 : 0);
                canonicalVariable = ordinal < 0
                    ? receiver
                    : ordinal < parameters.Length ? parameters[ordinal] : null;
            }
            if (!canonicalVariable.HasValue)
            {
                return null;
            }
            replacements = replacements.SetItem(binding.Variable, factory.Variable(canonicalVariable.Value));
        }
        if (sourceResult.HasValue)
        {
            if (!result.HasValue)
            {
                return null;
            }
            replacements = replacements.SetItem(sourceResult.Value, factory.Variable(result.Value));
        }
        foreach (var sourceReceiver in sourceReceivers)
        {
            if (usesCompanion || !receiver.HasValue)
            {
                return null;
            }
            replacements = replacements.SetItem(sourceReceiver, factory.Variable(receiver.Value));
        }
        foreach (var sourcePreState in sourcePreStates)
        {
            if (!replacements.TryGetValue(sourcePreState.Key, out var current))
            {
                return null;
            }
            var canonicalPre = createPreState(current.Variable);
            replacements = replacements.SetItem(sourcePreState.Value, factory.Variable(canonicalPre));
        }
        // Each insertion path stores factory.Variable's statically
        // guaranteed IrVariableTerm. No arbitrary replacement term is admitted.
        return replacements.ToImmutableDictionary(pair => pair.Key, pair => (IrTerm)pair.Value);
    }
}
