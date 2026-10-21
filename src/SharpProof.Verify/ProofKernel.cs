namespace SharpProof.Verify;

internal sealed class ProofKernel(ISmtBackend backend)
{
    private readonly ISmtBackend _backend =
        ArgumentNullGuard.NotNull(backend, nameof(backend));
    internal Func<IrOpaqueTerm, IrValue?>? ConcreteOpaqueValueProvider { get; init; }

    public Task<ProofOutcome> VerifyAsync(VerificationQuery query,
        CancellationToken cancellationToken)
    {
        query = RepositoryParameterGuard.NotNull(query, nameof(query));
        return VerifyCoreAsync(query, null, cancellationToken);
    }

    public Task<ProofOutcome> VerifyCallableAsync(VerificationQuery query,
        CallableReplayContext replayContext, CancellationToken cancellationToken)
    {
        query = RepositoryParameterGuard.NotNull(query, nameof(query));
        return VerifyCoreAsync(query, ArgumentNullGuard.NotNull(replayContext, nameof(replayContext)), cancellationToken);
    }

    private async Task<ProofOutcome> VerifyCoreAsync(VerificationQuery query,
        CallableReplayContext? replayContext, CancellationToken cancellationToken)
    {
        query = ArgumentNullGuard.NotNull(query, nameof(query));

        cancellationToken.ThrowIfCancellationRequested();
        BackendCheckResult result;
        try
        {
            result = await _backend.CheckAsync(query, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AggregateException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException and
            not OutOfMemoryException and
            not StackOverflowException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Unknown(AbstentionReason.InfrastructureFailure);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (result == null)
        {
            return Unknown(AbstentionReason.MalformedBackendResult);
        }

        return result.Status switch
        {
            BackendCheckStatus.Unsatisfiable => CreateProven(query, result, cancellationToken),
            BackendCheckStatus.Satisfiable => ReplayCounterexample(query, result, replayContext, cancellationToken),
            BackendCheckStatus.Unknown => CreateUnknown(result),
            _ => Unknown(AbstentionReason.MalformedBackendResult)
        };
    }
    private static UnknownOutcome CreateUnknown(BackendCheckResult result)
    {
        // An unknown result carries only its failure classification. Accepting
        // a model or core here would hide a malformed backend response behind
        // a typed semantic failure and make the result shape ambiguous.
        if (result is not { Model: null, UnsatCore.IsDefaultOrEmpty: true } ||
            result.FailureReason == BackendFailureReason.None)
        {
            return Unknown(AbstentionReason.MalformedBackendResult);
        }

        return Unknown(VerificationProjections.MapFailure(result.FailureReason));
    }

    private static ProofOutcome CreateProven(
        VerificationQuery query,
        BackendCheckResult result,
        CancellationToken cancellationToken)
    {
        if (result is not
            {
                Model: null,
                FailureReason: BackendFailureReason.None,
                UnsatCore.IsDefault: false
            })
        {
            return Unknown(AbstentionReason.MalformedBackendResult);
        }

        var justifications = ImmutableArray.CreateBuilder<ProofJustification>();
        var seen = ImmutableHashSet.CreateBuilder<int>();
        foreach (var index in result.UnsatCore)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index < 0 || index >= query.Assumptions.Length)
            {
                return Unknown(AbstentionReason.MalformedBackendResult);
            }

            if (!seen.Add(index))
            {
                continue;
            }

            justifications.Add(query.Assumptions[index].Justification);
        }
        return new ProvenOutcome(justifications.ToImmutable());
    }
    private ProofOutcome ReplayCounterexample(
        VerificationQuery query,
        BackendCheckResult result,
        CallableReplayContext? replayContext,
        CancellationToken cancellationToken)
    {
        if (result is not
            {
                Model: { Assignments: not null } model,
                FailureReason: BackendFailureReason.None,
                UnsatCore.IsDefaultOrEmpty: true
            })
        {
            return Unknown(AbstentionReason.MalformedBackendResult);
        }

        if (!ValidateAssignments(query, model.Assignments, cancellationToken))
        {
            return Unknown(AbstentionReason.CounterexampleReplayFailed);
        }

        var interpreter = new IrInterpreter(query.Factory);
        var unavailable = false;
        var observedCaches = ImmutableDictionary.CreateBuilder<IrMemberId, (object Identity, IrTypeId Type)>();
        IrValue? Observe(IrOpaqueTerm term)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            var value = (ConcreteOpaqueValueProvider ?? replayContext?.ReplayOptions?.ConcreteOpaqueValueProvider)?.Invoke(term);
            if (value == null && IrOpaqueCallSite.DefaultComparerValueKind(query.Factory, term) != null)
            { unavailable = true; }
            if (value is { Kind: IrValueKind.Reference } && IrOpaqueCallSite.DefaultComparerValueKind(query.Factory, term) != null)
            { observedCaches[term.Member] = (value.Reference, term.Type); }
            return value;
        }
        foreach (var assumption in query.Assumptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluated = interpreter.Evaluate(assumption.Predicate,
                model.Assignments, null, null, cancellationToken, concreteOpaqueValueProvider: Observe);
            if (!IsBoolean(evaluated, expected: true))
            {
                return Unknown(unavailable ? AbstentionReason.CounterexampleNotReplayable : AbstentionReason.CounterexampleReplayFailed);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var goal = interpreter.Evaluate(query.Goal.Predicate, model.Assignments, null, null, cancellationToken, concreteOpaqueValueProvider: Observe);
        if (goal.Status == IrEvaluationStatus.Exception)
        {
            return Unknown(query.Goal.Diagnostic switch
            {
                ProofDiagnosticKind.InternalConsistency =>
                    AbstentionReason.InternalConsistencyMayBeUndefined,
                ProofDiagnosticKind.Postcondition =>
                    AbstentionReason.PostconditionMayBeUndefined,
                _ => AbstentionReason.CounterexampleReplayFailed
            });
        }

        if (!IsBoolean(goal, expected: false))
        {
            return Unknown(unavailable ? AbstentionReason.CounterexampleNotReplayable : AbstentionReason.CounterexampleReplayFailed);
        }
        var observedCacheSnapshot = observedCaches.ToImmutable();
        foreach (var assignment in model.Assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assignment.Value.Kind != IrValueKind.Reference)
            { continue; }
            foreach (var observed in observedCacheSnapshot.Values)
            {
                if (ReferenceEquals(assignment.Value.Reference, observed.Identity) &&
                    !IrOpaqueCallSite.IsDefaultComparerValueView(query.Factory, observed.Type, assignment.Value.Type))
                { return Unknown(AbstentionReason.CounterexampleReplayFailed); }
            }
        }
        if (replayContext != null && CallableReplayValidator.Validate(
                query.Factory, replayContext, model.Assignments, cancellationToken) is { } failure)
        {
            return Unknown(failure);
        }
        return new RefutedOutcome(new ValidatedModel(model.Assignments));
    }
    private static bool ValidateAssignments(VerificationQuery query,
        ImmutableDictionary<IrVarId, IrValue> assignments,
        CancellationToken cancellationToken)
    {
        if (assignments.Count != query.ModelVariables.Length ||
            query.ModelVariables.Any(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (!assignments.ContainsKey(variable))) ||
            assignments.Keys.Any(key => key.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(key)) : (!query.ModelVariables.Contains(key))))
        {
            return false;
        }

        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsValid(assignment))
            {
                return false;
            }
        }
        return true;

        bool IsValid(KeyValuePair<IrVarId, IrValue> assignment)
        {
            if (assignment.Value == null)
            {
                return false;
            }

            // Query construction validates these IDs, and the exact key-set
            // check above prevents a backend from substituting another ID.
            var type = query.Factory.GetVariableInfo(assignment.Key).Type;
            return type == assignment.Value.Type &&
                (type == query.Factory.BooleanType || query.Factory.GetTypeInfo(type).Kind == IrTypeKind.Integer ||
                    query.Factory.Semantics == IrExecutionSemantics.Total && IsReferenceDomain(type));
        }

        bool IsReferenceDomain(IrTypeId type)
        {
            RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
            var info = query.Factory.GetTypeInfo(type);
            return info.Kind == IrTypeKind.Sequence && info.ElementType is { } element
                ? element == query.Factory.BooleanType || element == query.Factory.StringType ||
                    query.Factory.GetTypeInfo(element).Kind is IrTypeKind.Integer or IrTypeKind.Reference
                : info.Kind is IrTypeKind.Reference or IrTypeKind.String;
        }
    }

    private static bool IsBoolean(IrEvaluationResult result, bool expected)
    {
        result = RepositoryParameterGuard.NotNull(result, nameof(result));
        return result.Status == IrEvaluationStatus.Value &&
        result.Value is { Kind: IrValueKind.Boolean } value &&
        value.Boolean == expected;
    }

    private static UnknownOutcome Unknown(AbstentionReason reason)
    {
        return new(reason);
    }
}
