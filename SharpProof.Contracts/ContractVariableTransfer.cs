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
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        source = ArgumentNullGuard.NotNull(source, nameof(source));
        createPreState = ArgumentNullGuard.NotNull(createPreState, nameof(createPreState));
        // Actual compiler-bound snapshots have unique receiver/result roles
        // and parameter ordinals, as guaranteed by their binding producer.
        var receiver = destinationVariables
            .Where(variable => variable.Role == BoundContractVariableRole.Receiver)
            .Select(variable => (IrVarId?)variable.Variable).SingleOrDefault();
        var result = destinationVariables
            .Where(variable => variable.Role == BoundContractVariableRole.Result)
            .Select(variable => (IrVarId?)variable.Variable).SingleOrDefault();
        var parameters = destinationVariables
            .Where(variable => variable.Role == BoundContractVariableRole.Parameter)
            .OrderBy(variable => variable.Ordinal).Select(variable => variable.Variable).ToImmutableArray();
        var replacements = new Dictionary<IrVarId, IrVariableTerm>();
        foreach (var binding in sourceParameters)
        {
            IrVarId? canonicalVariable = null;
            if (binding.Symbol is IParameterSymbol parameter &&
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
            replacements[binding.Variable] = factory.Variable(canonicalVariable.Value);
        }
        if (sourceResult.HasValue)
        {
            if (!result.HasValue)
            {
                return null;
            }
            replacements[sourceResult.Value] = factory.Variable(result.Value);
        }
        foreach (var sourceReceiver in sourceReceivers)
        {
            if (usesCompanion || !receiver.HasValue)
            {
                return null;
            }
            replacements[sourceReceiver] = factory.Variable(receiver.Value);
        }
        foreach (var sourcePreState in sourcePreStates)
        {
            if (!replacements.TryGetValue(sourcePreState.Key, out var current))
            {
                return null;
            }
            var canonicalPre = createPreState(current.Variable);
            replacements[sourcePreState.Value] = factory.Variable(canonicalPre);
        }
        // Each of the four insertion paths stores factory.Variable's statically
        // guaranteed IrVariableTerm. No arbitrary replacement term is admitted.
        return replacements.ToImmutableDictionary(pair => pair.Key, pair => (IrTerm)pair.Value);
    }
}
