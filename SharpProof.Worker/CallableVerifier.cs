namespace SharpProof.Worker;
#pragma warning disable IDE0055 // Compact verification kernel preserves the fixed production-size ceiling.

internal sealed class CallableVerifier(ISmtBackend backend, int maximumExpressionDepth) {
    private readonly ProofKernel _kernel = new(backend ?? throw new ArgumentNullException(nameof(backend)));
    private readonly int _maximumExpressionDepth = maximumExpressionDepth > 0 ? maximumExpressionDepth
        : throw new ArgumentOutOfRangeException(nameof(maximumExpressionDepth));
    internal async Task<ImmutableArray<WorkerClaimResult>> VerifyAsync(CompilerCallablePreparation target,
        MethodResourceBudget resourceBudget, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(resourceBudget);
        cancellationToken.ThrowIfCancellationRequested();
        if (!target.IsSuccess) return CallableClaimResultAssembler.Unknowns(target, target.FailureReason);
        var factory = target.Factory;
        var ensures = target.Clauses.Where(static clause => clause.Kind == CompilerContractKind.Ensures).ToImmutableArray();
        if (ensures.Length != target.Entry.ClaimIds.Length ||
            !ensures.Select(static clause => clause.ClaimId!).SequenceEqual(
                target.Entry.ClaimIds, StringComparer.Ordinal))
            return CallableClaimResultAssembler.Unknowns(target, WorkerClaimReason.UnsupportedContract);
        if (ensures.IsDefaultOrEmpty) return [];
        var body = target.Body switch {
            { Kind: CompilerPreparedBodyKind.Trivial } => BodyLoweringResult.Trivial(factory),
            { Kind: CompilerPreparedBodyKind.Program, Program: not null } prepared when HasBoundSpecCalls(prepared) =>
                ExecuteAcyclicBody(target.Variables, factory, prepared.Program,
                    prepared.SpecCalls, prepared.ParameterBindings.ToImmutableDictionary(
                        static item => item.Key, item => (IrTerm)factory.Variable(item.Value)),
                    prepared.ParameterBindings, 64, 4096),
            _ => BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody)
        };
        if (!body.IsSuccess) return CallableClaimResultAssembler.Unknowns(target, body.Reason);

        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        var assumptionLabels = new Dictionary<ProofJustification, string>(ReferenceEqualityComparer.Instance);
        var userAssumptionIds = new Dictionary<ProofJustification, string>(ReferenceEqualityComparer.Instance);
        var assumptionOrdinal = 0;
        foreach (var clause in target.Clauses) {
            if (clause.Kind == CompilerContractKind.Ensures) continue;
            var predicate = ApplyEntrySubstitutions(factory, clause.Condition, target.Variables);
            if (predicate == null || GetDepth(predicate) > _maximumExpressionDepth)
                return CallableClaimResultAssembler.Unknowns(target, WorkerClaimReason.UnsupportedExpression);
            ProofJustification justification = clause.Kind == CompilerContractKind.Assume
                ? new UserAssumedJustification(new SourceLocationId(assumptionOrdinal))
                : new LoweredJustification(factory.CreateOperation("contract:" + assumptionOrdinal));
            assumptions.Add(new Assumption(factory, predicate, justification));
            if (clause.Kind == CompilerContractKind.Assume)
                userAssumptionIds.Add(justification, clause.AssumptionId!);
            assumptionLabels.Add(justification, clause.Kind.ToString().ToLowerInvariant() + ":" +
                assumptionOrdinal.ToString(CultureInfo.InvariantCulture));
            assumptionOrdinal++;
        }
        foreach (var path in body.Paths) {
            foreach (var specAssumption in path.SpecAssumptions) {
                var pathCondition = SpecResultDomainProjection.Rewrite(factory, path.Condition, path.SpecResultProjections);
                var specPredicate = SpecResultDomainProjection.Rewrite(factory, specAssumption.Predicate, path.SpecResultProjections);
                var predicate = Guard(factory, pathCondition, specPredicate);
                if (GetDepth(predicate) > _maximumExpressionDepth)
                    return CallableClaimResultAssembler.Unknowns(target, WorkerClaimReason.UnsupportedExpression);
                ProofJustification justification = new SpecJustification(specAssumption.Spec);
                assumptions.Add(new Assumption(factory, predicate, justification));
                assumptionLabels.Add(justification, "spec:" + specAssumption.WitnessIdentifier);
            }
        }
        if (!TryAddSourceDomainAssumptions(factory, target.Variables, body.Paths, assumptions, assumptionLabels))
            return CallableClaimResultAssembler.Unknowns(target, WorkerClaimReason.UnsupportedExpression);
        AddNormalCompletionAssumption(factory, body.Paths, assumptions, assumptionLabels);
        if (assumptions.Any(assumption =>
                GetDepth(assumption.Predicate) > _maximumExpressionDepth))
            return CallableClaimResultAssembler.Unknowns(target, WorkerClaimReason.UnsupportedExpression);
        var assumptionsUseSupportedDomain = assumptions.All(assumption =>
            IsSupportedProofDomain(factory, assumption.Predicate));

        var records = ImmutableArray.CreateBuilder<WorkerClaimResult>(ensures.Length);
        for (var index = 0; index < ensures.Length; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var pathObligations = ImmutableArray.CreateBuilder<IrTerm>(body.Paths.Length);
            var missingReturnValue = false;
            foreach (var path in body.Paths) {
                var pathCondition = ApplyBodySubstitutions(factory, ensures[index].Condition,
                    target.Variables, path.ReturnTerm, path.CurrentStates);
                if (pathCondition == null) { missingReturnValue = true; break; }
                pathCondition = SpecResultDomainProjection.Rewrite(factory, pathCondition, path.SpecResultProjections);
                var executionCondition = SpecResultDomainProjection.Rewrite(factory, path.Condition, path.SpecResultProjections);
                pathObligations.Add(Guard(factory, executionCondition, pathCondition));
            }
            if (missingReturnValue) {
                records.Add(CallableClaimResultAssembler.Unknown(target, index, WorkerClaimReason.MissingReturnValue));
                continue;
            }
            var condition = Conjoin(factory, pathObligations);
            if (GetDepth(condition) > _maximumExpressionDepth) {
                records.Add(CallableClaimResultAssembler.Unknown(target, index, WorkerClaimReason.DeepPostcondition));
                continue;
            }
            if (!assumptionsUseSupportedDomain || !IsSupportedProofDomain(factory, condition)) {
                records.Add(CallableClaimResultAssembler.Unknown(target, index, WorkerClaimReason.UnsupportedExpression));
                continue;
            }
            if (!resourceBudget.TryStartQuery()) {
                CallableClaimResultAssembler.AppendResourceLimit(records, target, index, ensures.Length);
                break;
            }
            var query = new VerificationQuery(factory, assumptions,
                new Goal(factory, condition, ProofDiagnosticKind.Postcondition, new SourceLocationId(index)));
            var outcome = await _kernel.VerifyAsync(query, cancellationToken).ConfigureAwait(false);
            if (resourceBudget.IsExceeded) {
                CallableClaimResultAssembler.AppendResourceLimit(records, target, index, ensures.Length);
                break;
            }
            records.Add(CallableClaimResultAssembler.FromOutcome(target, index, outcome, target.Variables,
                assumptionLabels, userAssumptionIds, body.UsesSpecModeledCallResult));
        }
        return records.ToImmutable();
    }

    private static bool TryAddSourceDomainAssumptions(IrFactory factory,
        ImmutableArray<CompilerCanonicalVariable> variables, ImmutableArray<BodyPath> paths,
        ImmutableArray<Assumption>.Builder assumptions,
        Dictionary<ProofJustification, string> assumptionLabels) {
        var seenPredicates = assumptions.Select(static assumption => assumption.Predicate.Id).ToHashSet();
        foreach (var variable in variables
                     .Where(static variable => variable.Role is CompilerVariableRole.Receiver
                         or CompilerVariableRole.Parameter or CompilerVariableRole.Result)
                     .OrderBy(static variable => GetDomainRoleOrder(variable.Role))
                     .ThenBy(static variable => variable.Ordinal)) {
            if (variable.SourceIntegerInterval is not { } sourceInterval) continue;
            var interval = IntervalDomain.Instance.Range(sourceInterval.Minimum, sourceInterval.Maximum);
            if (interval.IsBottom) return false;
            if (variable.Role == CompilerVariableRole.Result) {
                foreach (var path in paths) {
                    if (path.ReturnTerm == null ||
                        path.ReturnTerm.Type != factory.IntegerType ||
                        !SpecResultDomainProjection.TryCreateIntervalPredicate(factory, path.ReturnTerm, interval, out var predicate) ||
                        predicate == null)
                        return false;
                    AddDomainAssumption(Guard(factory,
                        SpecResultDomainProjection.Rewrite(factory, path.Condition, path.SpecResultProjections), predicate), variable);
                }
            }
            else {
                if (!SpecResultDomainProjection.TryCreateIntervalPredicate(factory,
                        factory.Variable(variable.Variable), interval, out var predicate))
                    return false;
                if (predicate == null) return false;
                AddDomainAssumption(predicate, variable);
            }
        }
        return true;

        void AddDomainAssumption(IrTerm predicate, CompilerCanonicalVariable variable) {
            if (predicate is IrBooleanTerm { Value: true } || !seenPredicates.Add(predicate.Id)) return;
            var label = CreateDomainLabel(variable);
            ProofJustification justification = new LoweredJustification(factory.CreateOperation("source-" + label));
            assumptions.Add(new Assumption(factory, predicate, justification));
            assumptionLabels.Add(justification, label);
        }
    }

    private static void AddNormalCompletionAssumption(IrFactory factory, ImmutableArray<BodyPath> paths,
        ImmutableArray<Assumption>.Builder assumptions,
        Dictionary<ProofJustification, string> assumptionLabels) {
        var completions = ImmutableArray.CreateBuilder<IrTerm>(paths.Length);
        foreach (var path in paths) {
            var completion = path.ReturnTerm == null ? path.Condition : factory.Binary(
                IrBinaryOperator.AndAlso, path.Condition,
                factory.Binary(IrBinaryOperator.Equal, path.ReturnTerm, path.ReturnTerm));
            completions.Add(SpecResultDomainProjection.Rewrite(factory, completion, path.SpecResultProjections));
        }
        var predicate = Disjoin(factory, completions);
        if (predicate is IrBooleanTerm { Value: true } ||
            assumptions.Any(assumption => assumption.Predicate.Id == predicate.Id))
            return;
        ProofJustification justification = new LoweredJustification(factory.CreateOperation("body:normal-completion"));
        assumptions.Add(new Assumption(factory, predicate, justification));
        assumptionLabels.Add(justification, "body:normal-completion");
    }

    private static int GetDomainRoleOrder(CompilerVariableRole role) => role switch {
        CompilerVariableRole.Receiver => 0, CompilerVariableRole.Parameter => 1,
        CompilerVariableRole.Result => 2, _ => 3
    };

    private static string CreateDomainLabel(CompilerCanonicalVariable variable) => variable.Role switch {
        CompilerVariableRole.Receiver => "domain:receiver",
        CompilerVariableRole.Parameter => "domain:parameter:" + variable.Ordinal.ToString(CultureInfo.InvariantCulture),
        CompilerVariableRole.Result => "domain:result",
        _ => throw new ArgumentOutOfRangeException(nameof(variable))
    };

    private static bool IsSupportedProofDomain(IrFactory factory, IrTerm root) {
        var pending = new Stack<IrTerm>(); var visited = new HashSet<IrId>();
        pending.Push(root);
        while (pending.Count != 0) {
            var term = pending.Pop();
            if (!visited.Add(term.Id)) continue;
            if (term is IrVariableTerm variable) {
                var kind = factory.GetTypeInfo(variable.Type).Kind;
                if (kind is not (IrTypeKind.Boolean or IrTypeKind.Integer)) return false;
            }
            if (term is IrBinaryTerm { Operator: IrBinaryOperator.StringConcat }) return false;
            if (term is IrLengthTerm length && length.Value.Type == factory.StringType) return false;
            foreach (var child in IrTraversal.GetChildren(term)) pending.Push(child);
        }
        return true;
    }

    private BodyLoweringResult ExecuteAcyclicBody(ImmutableArray<CompilerCanonicalVariable> variables,
        IrFactory factory, IrProgram program,
        ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall> specCalls,
        ImmutableDictionary<IrVarId, IrTerm> initialEnvironment,
        ImmutableDictionary<IrVarId, IrVarId> parameterBindings,
        int maximumBodyPaths, int maximumExecutionStates) {
        var pending = new Stack<SymbolicExecutionState>();
        pending.Push(new SymbolicExecutionState(program.Entry, initialEnvironment, factory.Boolean(true),
            ImmutableDictionary<IrVarId, SpecResultProjection>.Empty, []));
        var paths = ImmutableArray.CreateBuilder<BodyPath>();
        var executionStates = 0;
        while (pending.Count != 0) {
            if (++executionStates > maximumExecutionStates)
                return BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody);
            var state = pending.Pop(); var block = program.GetBlock(state.Block);
            var environment = state.Environment;
            var specResultProjections = state.SpecResultProjections;
            var specAssumptions = state.SpecAssumptions;
            OperationId? expectedMemoryHavoc = null; var transferred = false;
            for (var index = 0; index < block.Instructions.Length; index++) {
                var instruction = block.Instructions[index];
                if (expectedMemoryHavoc is { } expectedOperation) {
                    expectedMemoryHavoc = null;
                    if (instruction is IrHavocInstruction havoc &&
                        havoc.Operation == expectedOperation && havoc.HavocKind == IrHavocKind.Memory &&
                        havoc.Variables.IsEmpty)
                        continue;
                    return BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody);
                }
                switch (instruction) {
                    case IrAssignInstruction assign:
                        if (!TrySubstitute(factory, assign.Value, environment, out var assigned) ||
                            GetDepth(assigned) > _maximumExpressionDepth)
                            return BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody);
                        environment = environment.SetItem(assign.Target, assigned);
                        break;
                    case IrCallInstruction call:
                        if (!specCalls.TryGetValue(call.Id, out var specCall) ||
                            !TryApplySpecCall(factory, call, specCall, environment, out var resultTerm,
                                out var addedAssumptions, out var resultProjection, out var consumesMemoryHavoc))
                            return BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody);
                        environment = environment.SetItem(call.Target!.Value, resultTerm);
                        if (resultProjection.HasFacts)
                            specResultProjections = specResultProjections.SetItem(call.Target.Value, resultProjection);
                        specAssumptions = specAssumptions.AddRange(addedAssumptions);
                        expectedMemoryHavoc = consumesMemoryHavoc ? call.Operation : null;
                        break;
                    case IrBranchInstruction branch:
                        if (!TrySubstitute(factory, branch.Condition, environment, out var condition) ||
                            condition.Type != factory.BooleanType ||
                            GetDepth(condition) > _maximumExpressionDepth)
                            return BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody);
                        if (condition is IrBooleanTerm literal) {
                            pending.Push(new SymbolicExecutionState(literal.Value ? branch.WhenTrue : branch.WhenFalse,
                                environment, state.PathCondition, specResultProjections, specAssumptions));
                        }
                        else {
                            var whenTrue = factory.Binary(IrBinaryOperator.AndAlso, state.PathCondition, condition);
                            var whenFalse = factory.Binary(IrBinaryOperator.AndAlso, state.PathCondition,
                                factory.Unary(IrUnaryOperator.Not, condition));
                            if (GetDepth(whenTrue) > _maximumExpressionDepth || GetDepth(whenFalse) > _maximumExpressionDepth)
                                return BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody);
                            pending.Push(new SymbolicExecutionState(branch.WhenFalse, environment, whenFalse,
                                specResultProjections, specAssumptions));
                            pending.Push(new SymbolicExecutionState(branch.WhenTrue, environment, whenTrue,
                                specResultProjections, specAssumptions));
                        }
                        transferred = true;
                        break;
                    case IrGotoInstruction go:
                        pending.Push(new SymbolicExecutionState(go.Target, environment, state.PathCondition,
                            specResultProjections, specAssumptions));
                        transferred = true;
                        break;
                    case IrReturnInstruction returned:
                        if (returned.Value == null || !TrySubstitute(factory, returned.Value, environment, out var returnTerm) ||
                            GetDepth(returnTerm) > _maximumExpressionDepth)
                            return BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody);
                        paths.Add(new BodyPath(state.PathCondition, returnTerm,
                            CreateCurrentStates(variables, factory, environment, parameterBindings),
                            specResultProjections, specAssumptions));
                        if (paths.Count > maximumBodyPaths)
                            return BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody);
                        transferred = true;
                        break;
                    case IrLoadInstruction:
                    case IrStoreInstruction:
                    case IrHavocInstruction:
                    case IrAssumeInstruction:
                    case IrAssertInstruction:
                    default:
                        return BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody);
                }
                if (transferred) break;
            }
            if (!transferred) return BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody);
        }
        return paths.Count == 0
            ? BodyLoweringResult.Fail(WorkerClaimReason.UnsupportedBody)
            : BodyLoweringResult.Success(paths.ToImmutable());
    }

    private bool TryApplySpecCall(IrFactory factory, IrCallInstruction call, CompilerPreparedSpecCall specCall,
        IReadOnlyDictionary<IrVarId, IrTerm> environment,
        [NotNullWhen(true)] out IrTerm? resultTerm,
        out ImmutableArray<BodySpecAssumption> assumptions, out SpecResultProjection projection, out bool consumesMemoryHavoc) {
        resultTerm = null; assumptions = []; projection = default; consumesMemoryHavoc = specCall.ConsumesMemoryHavoc;
        if (!call.Target.HasValue ||
            !ApiSpecTable.Default.TryGetByWitnessIdentifier(specCall.WitnessIdentifier, out var template) ||
            !template.Result.HasValue ||
            consumesMemoryHavoc != (template.Facets.Effects.Effects != SpecEffect.None) ||
            !IsSpecResultType(factory, template.Target.ResultType,
                factory.GetVariableInfo(call.Target.Value).Type) ||
            call.Arguments.Length != template.Parameters.Length ||
            template.Receiver.HasValue != (call.Receiver != null))
            return false;

        var substitutions = new Dictionary<SpecVarId, IrTerm>();
        if (template.Receiver.HasValue) {
            if (!TrySubstitute(factory, call.Receiver!, environment, out var receiver))
                return false;
            substitutions.Add(template.Receiver.Value, receiver);
        }

        for (var index = 0; index < call.Arguments.Length; index++) {
            if (!TrySubstitute(factory, call.Arguments[index], environment, out var argument)) return false;
            substitutions.Add(template.Parameters[index], argument);
        }

        resultTerm = factory.Variable(call.Target.Value);
        substitutions.Add(template.Result.Value, resultTerm);
        if (!SpecResultDomainProjection.TryCreate(factory, template, call.Target.Value, out projection, out var facetPredicates)) {
            resultTerm = null; projection = default; return false;
        }
        var instantiated = ApiSpecInstantiator.InstantiatePostconditions(template, factory, substitutions);
        if (instantiated.Status != SpecInstantiationStatus.Succeeded) {
            resultTerm = null; projection = default; return false;
        }
        var projectionMap = projection.HasFacts
            ? ImmutableDictionary<IrVarId, SpecResultProjection>.Empty.Add(call.Target.Value, projection)
            : ImmutableDictionary<IrVarId, SpecResultProjection>.Empty;
        var predicates = instantiated.Postconditions.Select(predicate =>
                SpecResultDomainProjection.Rewrite(factory, predicate, projectionMap))
            .Concat(facetPredicates).ToImmutableArray();
        if (predicates.IsDefaultOrEmpty || predicates.Any(predicate => GetDepth(predicate) > _maximumExpressionDepth)) {
            resultTerm = null; projection = default; return false;
        }
        assumptions = [.. predicates.Select(predicate =>
            new BodySpecAssumption(template.Id, template.Target.WitnessIdentifier, predicate))];
        return true;
    }

    private static bool HasBoundSpecCalls(CompilerPreparedBody body) => body.SpecCalls.All(item =>
        ApiSpecTable.Default.TryGetByWitnessIdentifier(item.Value.WitnessIdentifier, out var template) &&
        template.Target.DocumentationCommentId == item.Value.CallIdentity);

    private static bool IsSpecResultType(IrFactory factory, SpecValueType? specType, IrTypeId resultType) =>
        specType switch {
            SpecValueType.Boolean => resultType == factory.BooleanType,
            SpecValueType.Integer => resultType == factory.IntegerType,
            SpecValueType.String => resultType == factory.StringType,
            SpecValueType.Sequence => factory.GetTypeInfo(resultType).Kind == IrTypeKind.Sequence,
            _ => false
        };

    private static ImmutableDictionary<IrVarId, IrTerm> CreateCurrentStates(
        ImmutableArray<CompilerCanonicalVariable> variables, IrFactory factory,
        ImmutableDictionary<IrVarId, IrTerm> environment, IReadOnlyDictionary<IrVarId, IrVarId> parameterBindings) {
        var result = ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
        foreach (var variable in variables) {
            if (variable.Role == CompilerVariableRole.Parameter)
                result[variable.Variable] = factory.Variable(variable.Variable);
        }
        foreach (var binding in parameterBindings) {
            if (environment.TryGetValue(binding.Key, out var value))
                result[binding.Value] = value;
        }
        return result.ToImmutable();
    }

    private static bool TrySubstitute(IrFactory factory, IrTerm term,
        IReadOnlyDictionary<IrVarId, IrTerm> environment,
        [NotNullWhen(true)] out IrTerm? substituted) {
        foreach (var variable in IrTraversal.CollectVariables(term)) {
            if (environment.ContainsKey(variable)) continue;
            substituted = null; return false;
        }
        try {
            substituted = IrSubstitution.Substitute(factory, term, environment); return true;
        }
        catch (ArgumentException) {
            substituted = null; return false;
        }
    }

    private static IrTerm? ApplyEntrySubstitutions(IrFactory factory, IrTerm term,
        ImmutableArray<CompilerCanonicalVariable> variables) => ApplyBodySubstitutions(
            factory, term, variables, null, ImmutableDictionary<IrVarId, IrTerm>.Empty, allowMissingResult: true);

    private static IrTerm? ApplyBodySubstitutions(IrFactory factory, IrTerm term,
        ImmutableArray<CompilerCanonicalVariable> variables, IrTerm? returnTerm,
        IReadOnlyDictionary<IrVarId, IrTerm> currentStates) =>
        ApplyBodySubstitutions(factory, term, variables, returnTerm, currentStates, allowMissingResult: false);

    private static IrTerm? ApplyBodySubstitutions(IrFactory factory, IrTerm term,
        ImmutableArray<CompilerCanonicalVariable> variables, IrTerm? returnTerm,
        IReadOnlyDictionary<IrVarId, IrTerm> currentStates, bool allowMissingResult) {
        var replacements = new Dictionary<IrVarId, IrTerm>();
        foreach (var variable in variables) {
            if (variable.Role == CompilerVariableRole.PreState &&
                variable.CurrentStateVariable.HasValue)
                replacements[variable.Variable] =
                    factory.Variable(variable.CurrentStateVariable.Value);
            else if (variable.Role == CompilerVariableRole.Result) {
                if (returnTerm == null) {
                    if (!allowMissingResult &&
                        IrTraversal.CollectVariables(term).Contains(variable.Variable))
                        return null;
                }
                else {
                    replacements[variable.Variable] = returnTerm;
                }
            }
        }
        foreach (var currentState in currentStates)
            replacements[currentState.Key] = currentState.Value;
        try { return IrSubstitution.Substitute(factory, term, replacements); }
        catch (ArgumentException) { return null; }
    }

    private static IrTerm Guard(
        IrFactory factory,
        IrTerm condition,
        IrTerm consequence) =>
        factory.Binary(
            IrBinaryOperator.OrElse,
            factory.Unary(IrUnaryOperator.Not, condition),
            consequence);

    private static IrTerm Conjoin(
        IrFactory factory,
        IReadOnlyList<IrTerm> terms) =>
        Combine(
            factory,
            terms,
            IrBinaryOperator.AndAlso,
            identity: true);

    private static IrTerm Disjoin(
        IrFactory factory,
        IReadOnlyList<IrTerm> terms) =>
        Combine(
            factory,
            terms,
            IrBinaryOperator.OrElse,
            identity: false);

    private static IrTerm Combine(
        IrFactory factory,
        IReadOnlyList<IrTerm> terms,
        IrBinaryOperator @operator,
        bool identity) {
        if (terms.Count == 0) return factory.Boolean(identity);
        return Visit(0, terms.Count);

        IrTerm Visit(int start, int count) {
            if (count == 1) return terms[start];
            var leftCount = count / 2;
            return factory.Binary(
                @operator,
                Visit(start, leftCount),
                Visit(start + leftCount, count - leftCount));
        }
    }

    private static int GetDepth(IrTerm root) {
        var memo = new Dictionary<IrId, int>();
        return Visit(root);

        int Visit(IrTerm term) {
            if (memo.TryGetValue(term.Id, out var existing)) return existing;
            var children = IrTraversal.GetChildren(term);
            var depth = children.Length == 0
                ? 1
                : 1 + children.Max(Visit);
            memo.Add(term.Id, depth);
            return depth;
        }
    }

    private readonly record struct BodyLoweringResult(
        ImmutableArray<BodyPath> Paths, WorkerClaimReason Reason) {
        internal bool UsesSpecModeledCallResult =>
            Paths.Any(path => !path.SpecAssumptions.IsDefaultOrEmpty);
        internal bool IsSuccess => Reason == WorkerClaimReason.None;
        internal static BodyLoweringResult Trivial(IrFactory factory) => Success([new BodyPath(
            factory.Boolean(true), null, ImmutableDictionary<IrVarId, IrTerm>.Empty,
            ImmutableDictionary<IrVarId, SpecResultProjection>.Empty, [])]);
        internal static BodyLoweringResult Success(ImmutableArray<BodyPath> paths) =>
            new(paths, WorkerClaimReason.None);
        internal static BodyLoweringResult Fail(WorkerClaimReason reason) => new([], reason);
    }

    private sealed record SymbolicExecutionState(
        IrBlockId Block,
        ImmutableDictionary<IrVarId, IrTerm> Environment,
        IrTerm PathCondition,
        ImmutableDictionary<IrVarId, SpecResultProjection>
            SpecResultProjections,
        ImmutableArray<BodySpecAssumption> SpecAssumptions);

    private readonly record struct BodyPath(
        IrTerm Condition,
        IrTerm? ReturnTerm,
        ImmutableDictionary<IrVarId, IrTerm> CurrentStates,
        ImmutableDictionary<IrVarId, SpecResultProjection>
            SpecResultProjections,
        ImmutableArray<BodySpecAssumption> SpecAssumptions);

    private readonly record struct BodySpecAssumption(
        SpecId Spec,
        string WitnessIdentifier,
        IrTerm Predicate);
}
