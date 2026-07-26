namespace SharpProof.Worker;

internal sealed class CallableVerifier(
    CSharpCompilation compilation,
    ISmtBackend backend,
    int maximumExpressionDepth) {
    private readonly CSharpCompilation _compilation =
        compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly ProofKernel _kernel =
        new(backend ?? throw new ArgumentNullException(nameof(backend)));
    private readonly IrFactory _factory = new();
    private ContractBinder? _contractBinder;
    private readonly ResolvedApiSpecTable _apiSpecs =
        new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
    private readonly int _maximumExpressionDepth = maximumExpressionDepth > 0
        ? maximumExpressionDepth
        : throw new ArgumentOutOfRangeException(nameof(maximumExpressionDepth));
    internal async Task<ImmutableArray<WorkerClaimResult>> VerifyAsync(
        ManifestCallableTarget target,
        MethodResourceBudget resourceBudget,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(resourceBudget);
        cancellationToken.ThrowIfCancellationRequested();
        var factory = _factory;
        var binding = ContractBinder.Bind(target.Method);
        if (!binding.IsSuccess)
            return CreateUnknowns(target, MapBindingFailure(binding.Failure));
        var contracts = binding.Contracts!;
        var ensures = contracts.Clauses
            .Where(static clause => clause.Kind == BoundContractKind.Ensures)
            .ToImmutableArray();
        if (ensures.Length != target.Claims.Length)
            return CreateUnknowns(
                target,
                WorkerClaimReason.UnsupportedContract);
        if (ensures.IsDefaultOrEmpty) return [];
        var userAssumptionEvidence = target.Assumptions.Where(static evidence =>
            evidence.Kind == WorkerAssumptionKind.UserAssume).ToImmutableArray();
        if (contracts.Clauses.Count(static clause =>
                clause.Kind == BoundContractKind.Assume) !=
            userAssumptionEvidence.Length)
            return CreateUnknowns(target, WorkerClaimReason.UnsupportedContract);
        var body = LowerBody(target, contracts, factory);
        if (!body.IsSuccess)
            return [.. ensures.Select((_, index) =>
                CreateUnknown(target, index, body.Reason))];

        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        var assumptionLabels = new Dictionary<ProofJustification, string>(
            ReferenceEqualityComparer.Instance);
        var userAssumptionIds = new Dictionary<ProofJustification, string>(
            ReferenceEqualityComparer.Instance);
        var assumptionOrdinal = 0;
        var userAssumptionOrdinal = 0;
        foreach (var clause in contracts.Clauses) {
            if (clause.Kind == BoundContractKind.Ensures) continue;
            var predicate = ApplyEntrySubstitutions(
                factory,
                clause.Condition,
                contracts);
            if (predicate == null ||
                GetDepth(predicate) > _maximumExpressionDepth)
                return [.. ensures.Select((_, index) =>
                    CreateUnknown(
                        target,
                        index,
                        WorkerClaimReason.UnsupportedExpression))];
            ProofJustification justification = clause.Kind ==
                BoundContractKind.Assume
                ? new UserAssumedJustification(
                    new SourceLocationId(assumptionOrdinal))
                : new LoweredJustification(clause.SourceOperation);
            assumptions.Add(new Assumption(
                factory,
                predicate,
                justification));
            if (clause.Kind == BoundContractKind.Assume)
                userAssumptionIds.Add(
                    justification,
                    userAssumptionEvidence[userAssumptionOrdinal++].Id);
            assumptionLabels.Add(
                justification,
                clause.Kind.ToString().ToLowerInvariant() + ":" +
                assumptionOrdinal.ToString(CultureInfo.InvariantCulture));
            assumptionOrdinal++;
        }
        foreach (var path in body.Paths) {
            foreach (var specAssumption in path.SpecAssumptions) {
                var pathCondition = SpecResultDomainProjection.Rewrite(
                    factory, path.Condition, path.SpecResultProjections);
                var specPredicate = SpecResultDomainProjection.Rewrite(
                    factory, specAssumption.Predicate, path.SpecResultProjections);
                var predicate = Guard(factory, pathCondition, specPredicate);
                if (GetDepth(predicate) > _maximumExpressionDepth)
                    return [.. ensures.Select((_, index) =>
                        CreateUnknown(
                            target,
                            index,
                            WorkerClaimReason.UnsupportedExpression))];
                ProofJustification justification =
                    new SpecJustification(specAssumption.Spec);
                assumptions.Add(new Assumption(
                    factory,
                    predicate,
                    justification));
                assumptionLabels.Add(
                    justification,
                    "spec:" + specAssumption.WitnessIdentifier);
            }
        }
        if (!TryAddSourceDomainAssumptions(
                factory,
                contracts,
                body.Paths,
                assumptions,
                assumptionLabels))
            return [.. ensures.Select((_, index) =>
                CreateUnknown(
                    target,
                    index,
                    WorkerClaimReason.UnsupportedExpression))];
        AddNormalCompletionAssumption(
            factory,
            body.Paths,
            assumptions,
            assumptionLabels);
        if (assumptions.Any(assumption =>
                GetDepth(assumption.Predicate) > _maximumExpressionDepth))
            return [.. ensures.Select((_, index) =>
                CreateUnknown(
                    target,
                    index,
                    WorkerClaimReason.UnsupportedExpression))];
        var assumptionsUseSupportedDomain = assumptions.All(assumption =>
            IsSupportedProofDomain(factory, assumption.Predicate));

        var records = ImmutableArray.CreateBuilder<WorkerClaimResult>(
            ensures.Length);
        for (var index = 0; index < ensures.Length; index++) {
            cancellationToken.ThrowIfCancellationRequested();
            var pathObligations = ImmutableArray.CreateBuilder<IrTerm>(
                body.Paths.Length);
            var missingReturnValue = false;
            foreach (var path in body.Paths) {
                var pathCondition = ApplyBodySubstitutions(
                    factory,
                    ensures[index].Condition,
                    contracts,
                    path.ReturnTerm,
                    path.CurrentStates);
                if (pathCondition == null) {
                    missingReturnValue = true;
                    break;
                }
                pathCondition = SpecResultDomainProjection.Rewrite(
                    factory, pathCondition, path.SpecResultProjections);
                var executionCondition = SpecResultDomainProjection.Rewrite(
                    factory, path.Condition, path.SpecResultProjections);
                pathObligations.Add(Guard(factory, executionCondition, pathCondition));
            }
            if (missingReturnValue) {
                records.Add(CreateUnknown(
                    target,
                    index,
                    WorkerClaimReason.MissingReturnValue));
                continue;
            }
            var condition = Conjoin(factory, pathObligations);
            if (GetDepth(condition) > _maximumExpressionDepth) {
                records.Add(CreateUnknown(
                    target,
                    index,
                    WorkerClaimReason.DeepPostcondition));
                continue;
            }
            if (!assumptionsUseSupportedDomain ||
                !IsSupportedProofDomain(factory, condition)) {
                records.Add(CreateUnknown(
                    target,
                    index,
                    WorkerClaimReason.UnsupportedExpression));
                continue;
            }
            if (!resourceBudget.TryStartQuery()) {
                AddResourceLimitRecords(
                    records,
                    target,
                    index,
                    ensures.Length);
                break;
            }
            var query = new VerificationQuery(
                factory,
                assumptions,
                new Goal(
                    factory,
                    condition,
                    ProofDiagnosticKind.Postcondition,
                    new SourceLocationId(index)));
            var outcome = await _kernel.VerifyAsync(
                query,
                cancellationToken).ConfigureAwait(false);
            if (resourceBudget.IsExceeded) {
                AddResourceLimitRecords(
                    records,
                    target,
                    index,
                    ensures.Length);
                break;
            }
            records.Add(CreateRecord(
                target,
                index,
                outcome,
                contracts,
                assumptionLabels,
                userAssumptionIds,
                body.UsesSpecModeledCallResult));
        }
        return records.ToImmutable();
    }

    private static bool TryAddSourceDomainAssumptions(
        IrFactory factory,
        BoundMethodContracts contracts,
        ImmutableArray<BodyPath> paths,
        ImmutableArray<Assumption>.Builder assumptions,
        Dictionary<ProofJustification, string> assumptionLabels) {
        var seenPredicates = assumptions
            .Select(static assumption => assumption.Predicate.Id)
            .ToHashSet();
        foreach (var variable in contracts.Variables
                     .Where(static variable => variable.Role is
                         BoundContractVariableRole.Receiver or
                         BoundContractVariableRole.Parameter or
                         BoundContractVariableRole.Result)
                     .OrderBy(static variable =>
                         GetDomainRoleOrder(variable.Role))
                     .ThenBy(static variable => variable.Ordinal)) {
            var sourceType = GetSourceType(variable, contracts);
            if (!TryGetNarrowIntegerInterval(
                    sourceType?.SpecialType ?? SpecialType.None,
                    out var interval))
                continue;
            if (variable.Role == BoundContractVariableRole.Result) {
                foreach (var path in paths) {
                    if (path.ReturnTerm == null ||
                        path.ReturnTerm.Type != factory.IntegerType ||
                        !SpecResultDomainProjection.TryCreateIntervalPredicate(
                            factory, path.ReturnTerm, interval, out var predicate) ||
                        predicate == null)
                        return false;
                    AddDomainAssumption(
                        Guard(
                            factory,
                            SpecResultDomainProjection.Rewrite(
                                factory, path.Condition, path.SpecResultProjections),
                            predicate),
                        variable);
                }
            }
            else {
                if (!SpecResultDomainProjection.TryCreateIntervalPredicate(
                        factory, factory.Variable(variable.Variable),
                        interval, out var predicate))
                    return false;
                if (predicate == null) return false;
                AddDomainAssumption(predicate, variable);
            }
        }
        return true;

        void AddDomainAssumption(
            IrTerm predicate,
            BoundContractVariable variable) {
            if (predicate is IrBooleanTerm { Value: true } ||
                !seenPredicates.Add(predicate.Id))
                return;
            var label = CreateDomainLabel(variable);
            ProofJustification justification = new LoweredJustification(
                factory.CreateOperation("source-" + label));
            assumptions.Add(new Assumption(
                factory,
                predicate,
                justification));
            assumptionLabels.Add(justification, label);
        }
    }

    private static void AddNormalCompletionAssumption(
        IrFactory factory,
        ImmutableArray<BodyPath> paths,
        ImmutableArray<Assumption>.Builder assumptions,
        Dictionary<ProofJustification, string> assumptionLabels) {
        var completions = ImmutableArray.CreateBuilder<IrTerm>(paths.Length);
        foreach (var path in paths) {
            var completion = path.ReturnTerm == null
                ? path.Condition
                : factory.Binary(
                    IrBinaryOperator.AndAlso,
                    path.Condition,
                    factory.Binary(
                        IrBinaryOperator.Equal,
                        path.ReturnTerm,
                        path.ReturnTerm));
            completions.Add(SpecResultDomainProjection.Rewrite(
                factory, completion, path.SpecResultProjections));
        }
        var predicate = Disjoin(factory, completions);
        if (predicate is IrBooleanTerm { Value: true } ||
            assumptions.Any(assumption =>
                assumption.Predicate.Id == predicate.Id))
            return;
        ProofJustification justification = new LoweredJustification(
            factory.CreateOperation("body:normal-completion"));
        assumptions.Add(new Assumption(
            factory,
            predicate,
            justification));
        assumptionLabels.Add(
            justification,
            "body:normal-completion");
    }

    private static int GetDomainRoleOrder(BoundContractVariableRole role) =>
        role switch {
            BoundContractVariableRole.Receiver => 0,
            BoundContractVariableRole.Parameter => 1,
            BoundContractVariableRole.Result => 2,
            _ => 3
        };

    private static ITypeSymbol? GetSourceType(
        BoundContractVariable variable,
        BoundMethodContracts contracts) =>
        variable.Role switch {
            BoundContractVariableRole.Parameter
                when variable.Symbol is IParameterSymbol parameter =>
                parameter.Type,
            BoundContractVariableRole.Receiver =>
                variable.Symbol as ITypeSymbol,
            BoundContractVariableRole.Result => contracts.Target.ReturnType,
            _ => null
        };

    private static string CreateDomainLabel(
        BoundContractVariable variable) =>
        variable.Role switch {
            BoundContractVariableRole.Receiver => "domain:receiver",
            BoundContractVariableRole.Parameter =>
                "domain:parameter:" +
                variable.Ordinal.ToString(CultureInfo.InvariantCulture),
            BoundContractVariableRole.Result => "domain:result",
            _ => throw new ArgumentOutOfRangeException(nameof(variable))
        };

    private static bool TryGetNarrowIntegerInterval(
        SpecialType type,
        out IntervalValue interval) {
        (long Minimum, long Maximum)? range = type switch {
            SpecialType.System_SByte => (sbyte.MinValue, sbyte.MaxValue),
            SpecialType.System_Byte => (byte.MinValue, byte.MaxValue),
            SpecialType.System_Int16 => (short.MinValue, short.MaxValue),
            SpecialType.System_UInt16 => (ushort.MinValue, ushort.MaxValue),
            SpecialType.System_Char => (char.MinValue, char.MaxValue),
            SpecialType.System_Int32 => (int.MinValue, int.MaxValue),
            SpecialType.System_UInt32 => (uint.MinValue, uint.MaxValue),
            _ => null
        };
        interval = range.HasValue
            ? IntervalDomain.Instance.Range(range.Value.Minimum, range.Value.Maximum)
            : IntervalValue.Bottom;
        return range.HasValue && !interval.IsBottom;
    }

    private static bool IsSupportedProofDomain(
        IrFactory factory,
        IrTerm root) {
        var pending = new Stack<IrTerm>();
        var visited = new HashSet<IrId>();
        pending.Push(root);
        while (pending.Count != 0) {
            var term = pending.Pop();
            if (!visited.Add(term.Id)) continue;
            if (term is IrVariableTerm variable) {
                var kind = factory.GetTypeInfo(variable.Type).Kind;
                if (kind is not (IrTypeKind.Boolean or IrTypeKind.Integer)) return false;
            }
            if (term is IrBinaryTerm { Operator: IrBinaryOperator.StringConcat }) return false;
            if (term is IrLengthTerm length &&
                length.Value.Type == factory.StringType)
                return false;
            foreach (var child in GetChildren(term)) pending.Push(child);
        }
        return true;
    }

    private static void AddResourceLimitRecords(
        ImmutableArray<WorkerClaimResult>.Builder records,
        ManifestCallableTarget target,
        int start,
        int count) {
        for (var index = start; index < count; index++)
            records.Add(CreateUnknown(
                target,
                index,
                WorkerClaimReason.ResourceLimit));
    }

    private BodyLoweringResult LowerBody(
        ManifestCallableTarget target,
        BoundMethodContracts contracts,
        IrFactory factory) {
        const int maximumBodyBlocks = 64;
        const int maximumBodyPaths = 64;
        const int maximumExecutionStates = 4096;

        if (target.Method.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None))
            return BodyLoweringResult.Fail(
                WorkerClaimReason.UnsupportedBody);
        if (target.Method.ReturnsVoid ||
            target.Method.MethodKind == MethodKind.Constructor) {
            return ContainsOnlyContractStatements(target)
                ? BodyLoweringResult.Single(factory, null)
                : BodyLoweringResult.Fail(
                    WorkerClaimReason.UnsupportedBody);
        }

        var bodyStart = FindExecutableBodyStart(target);
        if (!bodyStart.HasValue)
            return BodyLoweringResult.Fail(
                WorkerClaimReason.UnsupportedBody);
        Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph? graph;
        try {
            graph = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(
                target.VerifierDeclaration,
                target.VerifierSemanticModel);
        }
        catch (ArgumentException) {
            return BodyLoweringResult.Fail(
                WorkerClaimReason.UnsupportedBody);
        }
        if (graph == null)
            return BodyLoweringResult.Fail(
                WorkerClaimReason.UnsupportedBody);

        var lowering = new RoslynProgramLowerer(
            factory,
            IsKnownPure).Lower(graph);
        if (!TryCreateProgramGroups(
                graph,
                lowering.Program,
                out var groups))
            return BodyLoweringResult.Fail(
                WorkerClaimReason.UnsupportedBody);

        var start = groups.FirstOrDefault(group =>
            IsAtOrAfterBodyStart(group.Source, bodyStart.Value));
        if (start == null)
            return BodyLoweringResult.Fail(
                WorkerClaimReason.UnsupportedBody);

        var groupsByOperation = groups
            .ToDictionary(static group => group.Operation);
        foreach (var abstention in lowering.Abstentions) {
            if (!groupsByOperation.TryGetValue(
                    abstention.Operation,
                    out var group) ||
                IsAtOrAfterBodyStart(group.Source, bodyStart.Value))
                return BodyLoweringResult.Fail(
                    WorkerClaimReason.UnsupportedBody);
        }

        if (!TryCreateCallBindings(
                factory,
                groups.Where(group =>
                    IsAtOrAfterBodyStart(group.Source, bodyStart.Value)),
                out var callBindings))
            return BodyLoweringResult.Fail(
                WorkerClaimReason.UnsupportedBody);

        if (!TryValidateAcyclicBody(
                lowering.Program,
                start.Block,
                maximumBodyBlocks))
            return BodyLoweringResult.Fail(
                WorkerClaimReason.UnsupportedBody);
        if (!TryCreateInitialEnvironment(
                target,
                contracts,
                factory,
                lowering.Variables,
                out var initialEnvironment,
                out var parameterBindings))
            return BodyLoweringResult.Fail(
                WorkerClaimReason.UnsupportedBody);

        return ExecuteAcyclicBody(
            contracts,
            factory,
            lowering.Program,
            start,
            callBindings,
            initialEnvironment,
            parameterBindings,
            maximumBodyPaths,
            maximumExecutionStates);
    }

    private BodyLoweringResult ExecuteAcyclicBody(
        BoundMethodContracts contracts,
        IrFactory factory,
        IrProgram program,
        ProgramOperationGroup start,
        ImmutableDictionary<IrInstructionId, IInvocationOperation> callBindings,
        ImmutableDictionary<IrVarId, IrTerm> initialEnvironment,
        ImmutableDictionary<IrVarId, IrVarId> parameterBindings,
        int maximumBodyPaths,
        int maximumExecutionStates) {
        var pending = new Stack<SymbolicExecutionState>();
        pending.Push(new SymbolicExecutionState(
            start.Block,
            start.StartInstruction,
            initialEnvironment,
            factory.Boolean(true),
            ImmutableDictionary<IrVarId, SpecResultProjection>.Empty,
            []));
        var paths = ImmutableArray.CreateBuilder<BodyPath>();
        var executionStates = 0;
        while (pending.Count != 0) {
            if (++executionStates > maximumExecutionStates)
                return BodyLoweringResult.Fail(
                    WorkerClaimReason.UnsupportedBody);
            var state = pending.Pop();
            var block = program.GetBlock(state.Block);
            var environment = state.Environment;
            var specResultProjections = state.SpecResultProjections;
            var specAssumptions = state.SpecAssumptions;
            OperationId? expectedMemoryHavoc = null;
            var transferred = false;
            for (var index = state.StartInstruction;
                 index < block.Instructions.Length;
                 index++) {
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
                        if (!TrySubstitute(
                                factory,
                                assign.Value,
                                environment,
                                out var assigned) ||
                            GetDepth(assigned) > _maximumExpressionDepth)
                            return BodyLoweringResult.Fail(
                                WorkerClaimReason.UnsupportedBody);
                        environment = environment.SetItem(
                            assign.Target,
                            assigned);
                        break;
                    case IrCallInstruction call:
                        if (!callBindings.TryGetValue(
                                call.Id,
                                out var invocation) ||
                            !TryApplySpecCall(
                                factory,
                                call,
                                invocation,
                                environment,
                                out var resultTerm,
                                out var addedAssumptions,
                                out var resultProjection,
                                out var consumesMemoryHavoc))
                            return BodyLoweringResult.Fail(
                                WorkerClaimReason.UnsupportedBody);
                        environment = environment.SetItem(
                            call.Target!.Value,
                            resultTerm);
                        if (resultProjection.HasFacts)
                            specResultProjections =
                                specResultProjections.SetItem(
                                    resultProjection.ResultVariable,
                                    resultProjection);
                        specAssumptions =
                            specAssumptions.AddRange(addedAssumptions);
                        expectedMemoryHavoc = consumesMemoryHavoc ? call.Operation : null;
                        break;
                    case IrBranchInstruction branch:
                        if (!TrySubstitute(
                                factory,
                                branch.Condition,
                                environment,
                                out var condition) ||
                            condition.Type != factory.BooleanType ||
                            GetDepth(condition) > _maximumExpressionDepth)
                            return BodyLoweringResult.Fail(
                                WorkerClaimReason.UnsupportedBody);
                        if (condition is IrBooleanTerm literal) {
                            pending.Push(new SymbolicExecutionState(
                                literal.Value
                                    ? branch.WhenTrue
                                    : branch.WhenFalse,
                                0,
                                environment,
                                state.PathCondition,
                                specResultProjections,
                                specAssumptions));
                        }
                        else {
                            var whenTrue = factory.Binary(
                                IrBinaryOperator.AndAlso,
                                state.PathCondition,
                                condition);
                            var whenFalse = factory.Binary(
                                IrBinaryOperator.AndAlso,
                                state.PathCondition,
                                factory.Unary(
                                    IrUnaryOperator.Not,
                                    condition));
                            if (GetDepth(whenTrue) >
                                    _maximumExpressionDepth ||
                                GetDepth(whenFalse) >
                                    _maximumExpressionDepth)
                                return BodyLoweringResult.Fail(
                                    WorkerClaimReason.UnsupportedBody);
                            pending.Push(new SymbolicExecutionState(
                                branch.WhenFalse,
                                0,
                                environment,
                                whenFalse,
                                specResultProjections,
                                specAssumptions));
                            pending.Push(new SymbolicExecutionState(
                                branch.WhenTrue,
                                0,
                                environment,
                                whenTrue,
                                specResultProjections,
                                specAssumptions));
                        }
                        transferred = true;
                        break;
                    case IrGotoInstruction go:
                        pending.Push(new SymbolicExecutionState(
                            go.Target,
                            0,
                            environment,
                            state.PathCondition,
                            specResultProjections,
                            specAssumptions));
                        transferred = true;
                        break;
                    case IrReturnInstruction returned:
                        if (returned.Value == null ||
                            !TrySubstitute(
                                factory,
                                returned.Value,
                                environment,
                                out var returnTerm) ||
                            GetDepth(returnTerm) >
                                _maximumExpressionDepth)
                            return BodyLoweringResult.Fail(
                                WorkerClaimReason.UnsupportedBody);
                        paths.Add(new BodyPath(
                            state.PathCondition,
                            returnTerm,
                            CreateCurrentStates(
                                contracts,
                                factory,
                                environment,
                                parameterBindings),
                            specResultProjections,
                            specAssumptions));
                        if (paths.Count > maximumBodyPaths)
                            return BodyLoweringResult.Fail(
                                WorkerClaimReason.UnsupportedBody);
                        transferred = true;
                        break;
                    case IrLoadInstruction:
                    case IrStoreInstruction:
                    case IrHavocInstruction:
                    case IrAssumeInstruction:
                    case IrAssertInstruction:
                    default:
                        return BodyLoweringResult.Fail(
                            WorkerClaimReason.UnsupportedBody);
                }
                if (transferred) break;
            }
            if (!transferred)
                return BodyLoweringResult.Fail(
                    WorkerClaimReason.UnsupportedBody);
        }
        return paths.Count == 0
            ? BodyLoweringResult.Fail(
                WorkerClaimReason.UnsupportedBody)
            : BodyLoweringResult.Success(paths.ToImmutable());
    }

    private bool TryApplySpecCall(
        IrFactory factory,
        IrCallInstruction call,
        IInvocationOperation invocation,
        IReadOnlyDictionary<IrVarId, IrTerm> environment,
        [NotNullWhen(true)] out IrTerm? resultTerm,
        out ImmutableArray<BodySpecAssumption> assumptions,
        out SpecResultProjection projection,
        out bool consumesMemoryHavoc) {
        resultTerm = null;
        assumptions = [];
        projection = default;
        consumesMemoryHavoc = false;
        if (!call.Target.HasValue ||
            invocation.TargetMethod.ReducedFrom != null ||
            invocation.TargetMethod.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None) ||
            !_apiSpecs.TryGet(invocation.TargetMethod, out var resolved) ||
            !TryAdmitSpecCallEffects(
                invocation, call, resolved.Template, out consumesMemoryHavoc) ||
            !resolved.Template.Result.HasValue ||
            !TryGetSpecResultType(
                factory,
                invocation.Type,
                resolved.Template.Target.ResultType,
                factory.GetVariableInfo(call.Target.Value).Type,
                out var resultType) ||
            factory.GetVariableInfo(call.Target.Value).Type != resultType ||
            invocation.Arguments.Length !=
                resolved.Template.Parameters.Length ||
            !HasDirectArgumentOrder(invocation))
            return false;

        var substitutions = new Dictionary<SpecVarId, IrTerm>();
        if (resolved.Template.Receiver.HasValue) {
            if (call.Receiver == null ||
                !TrySubstitute(
                    factory,
                    call.Receiver,
                    environment,
                    out var receiver))
                return false;
            substitutions.Add(
                resolved.Template.Receiver.Value,
                receiver);
        }
        else if (call.Receiver != null) {
            return false;
        }

        if (call.Arguments.Length !=
            resolved.Template.Parameters.Length)
            return false;
        for (var index = 0; index < call.Arguments.Length; index++) {
            if (!TrySubstitute(
                    factory,
                    call.Arguments[index],
                    environment,
                    out var argument))
                return false;
            substitutions.Add(
                resolved.Template.Parameters[index],
                argument);
        }

        resultTerm = factory.Variable(call.Target.Value);
        substitutions.Add(
            resolved.Template.Result.Value,
            resultTerm);
        if (!SpecResultDomainProjection.TryCreate(
                factory, resolved.Template, call.Target.Value, out projection,
                out var facetPredicates)) {
            resultTerm = null;
            projection = default;
            return false;
        }
        var instantiated = ApiSpecInstantiator.InstantiatePostconditions(
            resolved.Template, factory, substitutions);
        if (instantiated.Status != SpecInstantiationStatus.Succeeded) {
            resultTerm = null;
            projection = default;
            return false;
        }
        var projectionMap = projection.HasFacts
            ? ImmutableDictionary<IrVarId, SpecResultProjection>.Empty.Add(
                projection.ResultVariable, projection)
            : ImmutableDictionary<IrVarId, SpecResultProjection>.Empty;
        var predicates = instantiated.Postconditions
            .Select(predicate => SpecResultDomainProjection.Rewrite(
                factory, predicate, projectionMap))
            .Concat(facetPredicates)
            .ToImmutableArray();
        if (predicates.IsDefaultOrEmpty ||
            predicates.Any(predicate =>
                GetDepth(predicate) > _maximumExpressionDepth)) {
            resultTerm = null;
            projection = default;
            return false;
        }
        assumptions = [.. predicates.Select(predicate =>
            new BodySpecAssumption(
                resolved.Template.Id,
                resolved.Template.Target.WitnessIdentifier,
                predicate))];
        return true;
    }
    private static bool TryAdmitSpecCallEffects(
        IInvocationOperation invocation, IrCallInstruction call, ApiSpecTemplate template,
        out bool consumesMemoryHavoc) {
        var effects = template.Facets.Effects.Effects;
        consumesMemoryHavoc = effects != SpecEffect.None;
        var cardinality = template.Facets.Cardinality;
        return !consumesMemoryHavoc ||
               effects == SpecEffect.Unknown && invocation.TargetMethod.IsStatic &&
               invocation.TargetMethod.Parameters.IsEmpty && invocation.Instance == null &&
               invocation.Arguments.IsEmpty && call.Receiver == null && call.Arguments.IsEmpty &&
               invocation.Type is IArrayTypeSymbol && !template.Receiver.HasValue &&
               template.Parameters.IsEmpty && template.Postconditions.IsDefaultOrEmpty &&
               template.Facets.Nullness.Result == SpecNullness.NonNull &&
               (cardinality.Result is SpecCardinality.Empty or SpecCardinality.NonEmpty ||
                cardinality.Result == SpecCardinality.Exact && cardinality.ExactCount.HasValue);
    }
    private static bool HasDirectArgumentOrder(
        IInvocationOperation invocation) {
        if (invocation.Arguments.Length !=
            invocation.TargetMethod.Parameters.Length)
            return false;
        for (var index = 0; index < invocation.Arguments.Length; index++) {
            var argument = invocation.Arguments[index];
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                argument.Parameter?.Ordinal != index)
                return false;
        }
        return true;
    }

    private static bool TryGetSpecResultType(
        IrFactory factory,
        ITypeSymbol? sourceType,
        SpecValueType? specType,
        IrTypeId loweredResultType,
        out IrTypeId resultType) {
        switch (specType) {
            case SpecValueType.Boolean
                when sourceType?.SpecialType ==
                    SpecialType.System_Boolean:
                resultType = factory.BooleanType;
                return true;
            case SpecValueType.Integer
                when sourceType?.SpecialType is
                    SpecialType.System_SByte or
                    SpecialType.System_Byte or
                    SpecialType.System_Int16 or
                    SpecialType.System_UInt16 or
                    SpecialType.System_Char or
                    SpecialType.System_Int32 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_Int64:
                resultType = factory.IntegerType;
                return true;
            case SpecValueType.String
                when sourceType?.SpecialType ==
                    SpecialType.System_String:
                resultType = factory.StringType;
                return true;
            case SpecValueType.Sequence
                when sourceType is IArrayTypeSymbol &&
                     factory.GetTypeInfo(loweredResultType).Kind == IrTypeKind.Sequence:
                resultType = loweredResultType;
                return true;
            default:
                resultType = default;
                return false;
        }
    }

    private int? FindExecutableBodyStart(ManifestCallableTarget target) {
        if (target.VerifierDeclaration.ExpressionBody != null)
            return target.VerifierDeclaration.ExpressionBody.Expression.SpanStart;
        if (target.VerifierDeclaration.Body == null) return null;
        foreach (var statement in target.VerifierDeclaration.Body.Statements) {
            if (statement is EmptyStatementSyntax ||
                IsContractStatement(target, statement))
                continue;
            return statement.SpanStart;
        }
        return null;
    }

    private static bool IsAtOrAfterBodyStart(
        IOperation? operation,
        int bodyStart) =>
        operation != null &&
        (operation.Syntax.SpanStart >= bodyStart ||
         operation.Syntax.Span.Contains(bodyStart));

    private static bool TryCreateProgramGroups(
        Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph graph,
        IrProgram program,
        out ImmutableArray<ProgramOperationGroup> groups) {
        groups = [];
        if (graph.Blocks.Length != program.Blocks.Length) return false;
        var result = ImmutableArray.CreateBuilder<ProgramOperationGroup>();
        for (var blockIndex = 0;
             blockIndex < graph.Blocks.Length;
             blockIndex++) {
            var source = graph.Blocks[blockIndex];
            var target = program.Blocks[blockIndex];
            var instructionGroups =
                CreateInstructionGroups(target);
            var groupIndex = 0;
            var terminated = false;
            foreach (var operation in source.Operations) {
                if (operation is IEmptyOperation) continue;
                if (groupIndex >= instructionGroups.Length) return false;
                result.Add(instructionGroups[groupIndex++].WithSource(
                    operation));
                if (operation is IReturnOperation) {
                    terminated = true;
                    break;
                }
            }
            if (!terminated) {
                if (groupIndex >= instructionGroups.Length) return false;
                result.Add(instructionGroups[groupIndex++].WithSource(
                    source.BranchValue));
            }
            if (groupIndex != instructionGroups.Length) return false;
        }
        groups = result.ToImmutable();
        return true;
    }

    private static ImmutableArray<ProgramOperationGroup>
        CreateInstructionGroups(IrBasicBlock block) {
        var groups = ImmutableArray.CreateBuilder<ProgramOperationGroup>();
        var start = 0;
        while (start < block.Instructions.Length) {
            var operation = block.Instructions[start].Operation;
            var end = start + 1;
            while (end < block.Instructions.Length &&
                   block.Instructions[end].Operation == operation)
                end++;
            groups.Add(new ProgramOperationGroup(
                block.Id,
                start,
                end,
                operation,
                null,
                block.Instructions.Slice(start, end - start)));
            start = end;
        }
        return groups.ToImmutable();
    }

    private static bool TryCreateCallBindings(
        IrFactory factory,
        IEnumerable<ProgramOperationGroup> groups,
        out ImmutableDictionary<IrInstructionId, IInvocationOperation>
            bindings) {
        var result =
            ImmutableDictionary.CreateBuilder<
                IrInstructionId,
                IInvocationOperation>();
        foreach (var group in groups) {
            var calls = group.Instructions
                .OfType<IrCallInstruction>()
                .ToImmutableArray();
            if (calls.IsDefaultOrEmpty) continue;
            if (group.Source == null) {
                bindings =
                    ImmutableDictionary<
                        IrInstructionId,
                        IInvocationOperation>.Empty;
                return false;
            }
            var invocations = EnumerateInvocationsInEvaluationOrder(
                    group.Source)
                .ToImmutableArray();
            if (calls.Length != invocations.Length) {
                bindings =
                    ImmutableDictionary<
                        IrInstructionId,
                        IInvocationOperation>.Empty;
                return false;
            }
            for (var index = 0; index < calls.Length; index++) {
                var call = calls[index];
                var invocation = invocations[index];
                var member = factory.GetMemberInfo(call.Member);
                if (member.Identity !=
                        CompilerIdentityBridge.InternSymbol(
                            factory,
                            invocation.TargetMethod) ||
                    member.IsStatic !=
                        (invocation.Instance == null) ||
                    member.ParameterTypes.Length !=
                        invocation.Arguments.Length) {
                    bindings =
                        ImmutableDictionary<
                            IrInstructionId,
                            IInvocationOperation>.Empty;
                    return false;
                }
                result.Add(call.Id, invocation);
            }
        }
        bindings = result.ToImmutable();
        return true;
    }

    private static IEnumerable<IInvocationOperation>
        EnumerateInvocationsInEvaluationOrder(IOperation operation) {
        foreach (var child in operation.ChildOperations)
            foreach (var invocation in
                     EnumerateInvocationsInEvaluationOrder(child))
                yield return invocation;
        if (operation is IInvocationOperation current)
            yield return current;
    }

    private static bool TryValidateAcyclicBody(
        IrProgram program,
        IrBlockId start,
        int maximumBlocks) {
        var colors = new Dictionary<IrBlockId, int>();
        var reachable = 0;
        return Visit(start);

        bool Visit(IrBlockId blockId) {
            if (colors.TryGetValue(blockId, out var color)) return color == 2;
            if (++reachable > maximumBlocks) return false;
            colors.Add(blockId, 1);
            var block = program.GetBlock(blockId);
            foreach (var successor in GetSuccessors(block.Terminator)) {
                if (colors.TryGetValue(successor, out color) && color == 1) return false;
                if (!Visit(successor)) return false;
            }
            colors[blockId] = 2;
            return true;
        }
    }

    private static ImmutableArray<IrBlockId> GetSuccessors(
        IrInstruction terminator) =>
        terminator switch {
            IrBranchInstruction branch =>
                branch.WhenTrue == branch.WhenFalse
                    ? [branch.WhenTrue]
                    : [branch.WhenTrue, branch.WhenFalse],
            IrGotoInstruction go => [go.Target],
            IrReturnInstruction => [],
            _ => []
        };

    private static bool TryCreateInitialEnvironment(
        ManifestCallableTarget target,
        BoundMethodContracts contracts,
        IrFactory factory,
        ImmutableArray<FrontendVariableBinding> variables,
        out ImmutableDictionary<IrVarId, IrTerm> environment,
        out ImmutableDictionary<IrVarId, IrVarId> parameterBindings) {
        var canonicalParameters = contracts.Variables
            .Where(static variable =>
                variable.Role == BoundContractVariableRole.Parameter)
            .ToDictionary(static variable => variable.Ordinal);
        var values =
            ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
        var bindings =
            ImmutableDictionary.CreateBuilder<IrVarId, IrVarId>();
        foreach (var binding in variables) {
            if (binding.Symbol is ILocalSymbol) continue;
            if (binding.Symbol is not IParameterSymbol parameter ||
                !SymbolEqualityComparer.Default.Equals(
                    parameter.ContainingSymbol,
                    target.Method) ||
                !canonicalParameters.TryGetValue(
                    parameter.Ordinal,
                    out var canonical)) {
                environment =
                    ImmutableDictionary<IrVarId, IrTerm>.Empty;
                parameterBindings =
                    ImmutableDictionary<IrVarId, IrVarId>.Empty;
                return false;
            }
            values.Add(
                binding.Variable,
                factory.Variable(canonical.Variable));
            bindings.Add(binding.Variable, canonical.Variable);
        }
        environment = values.ToImmutable();
        parameterBindings = bindings.ToImmutable();
        return true;
    }

    private static ImmutableDictionary<IrVarId, IrTerm>
        CreateCurrentStates(
            BoundMethodContracts contracts,
            IrFactory factory,
            ImmutableDictionary<IrVarId, IrTerm> environment,
            IReadOnlyDictionary<IrVarId, IrVarId> parameterBindings) {
        var result =
            ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
        foreach (var variable in contracts.Variables) {
            if (variable.Role == BoundContractVariableRole.Parameter)
                result[variable.Variable] =
                    factory.Variable(variable.Variable);
        }
        foreach (var binding in parameterBindings) {
            if (environment.TryGetValue(binding.Key, out var value))
                result[binding.Value] = value;
        }
        return result.ToImmutable();
    }

    private static bool TrySubstitute(
        IrFactory factory,
        IrTerm term,
        IReadOnlyDictionary<IrVarId, IrTerm> environment,
        [NotNullWhen(true)] out IrTerm? substituted) {
        foreach (var variable in CollectVariables(term)) {
            if (environment.ContainsKey(variable)) continue;
            substituted = null;
            return false;
        }
        try {
            substituted = IrSubstitution.Substitute(
                factory,
                term,
                environment);
            return true;
        }
        catch (ArgumentException) {
            substituted = null;
            return false;
        }
    }

    private bool ContainsOnlyContractStatements(ManifestCallableTarget target) =>
        target.VerifierDeclaration.Body != null &&
        target.VerifierDeclaration.Body.Statements.All(statement =>
            IsContractStatement(target, statement));

    private bool IsContractStatement(
        ManifestCallableTarget target,
        StatementSyntax statement) {
        if (statement is EmptyStatementSyntax) return true;
        return statement is ExpressionStatementSyntax expression &&
               ContractBinder.GetClauseInventory(target.Method)
                   .Clauses.Any(clause =>
                       clause.Invocation.Syntax.SyntaxTree ==
                           expression.SyntaxTree &&
                       clause.Invocation.Syntax.Span == expression.Expression.Span);
    }

    private static IrTerm? ApplyEntrySubstitutions(
        IrFactory factory,
        IrTerm term,
        BoundMethodContracts contracts) =>
        ApplyBodySubstitutions(
            factory,
            term,
            contracts,
            null,
            ImmutableDictionary<IrVarId, IrTerm>.Empty,
            allowMissingResult: true);

    private static IrTerm? ApplyBodySubstitutions(
        IrFactory factory,
        IrTerm term,
        BoundMethodContracts contracts,
        IrTerm? returnTerm,
        IReadOnlyDictionary<IrVarId, IrTerm> currentStates) =>
        ApplyBodySubstitutions(
            factory,
            term,
            contracts,
            returnTerm,
            currentStates,
            allowMissingResult: false);

    private static IrTerm? ApplyBodySubstitutions(
        IrFactory factory,
        IrTerm term,
        BoundMethodContracts contracts,
        IrTerm? returnTerm,
        IReadOnlyDictionary<IrVarId, IrTerm> currentStates,
        bool allowMissingResult) {
        var replacements = new Dictionary<IrVarId, IrTerm>();
        foreach (var variable in contracts.Variables) {
            if (variable.Role == BoundContractVariableRole.PreState &&
                variable.CurrentStateVariable.HasValue)
                replacements[variable.Variable] =
                    factory.Variable(variable.CurrentStateVariable.Value);
            else if (variable.Role == BoundContractVariableRole.Result) {
                if (returnTerm == null) {
                    if (!allowMissingResult &&
                        CollectVariables(term).Contains(variable.Variable))
                        return null;
                }
                else {
                    replacements[variable.Variable] = returnTerm;
                }
            }
        }
        foreach (var currentState in currentStates)
            replacements[currentState.Key] = currentState.Value;
        try {
            return IrSubstitution.Substitute(
                factory,
                term,
                replacements);
        }
        catch (ArgumentException) {
            return null;
        }
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

    private static WorkerClaimResult CreateRecord(
        ManifestCallableTarget target,
        int contractOrdinal,
        ProofOutcome outcome,
        BoundMethodContracts contracts,
        Dictionary<ProofJustification, string> assumptionLabels,
        Dictionary<ProofJustification, string> userAssumptionIds,
        bool usesSpecModeledCallResult) {
        var record = CreateBaseRecord(target, contractOrdinal);
        var usedUserAssumptions = new HashSet<string>(StringComparer.Ordinal);
        switch (outcome) {
            case ProvenOutcome proven:
                record.Outcome = WorkerClaimOutcome.Proven;
                record.Reason = WorkerClaimReason.None;
                record.ProofCore = [.. proven.Core
                    .Select(justification =>
                        assumptionLabels.TryGetValue(justification, out var label)
                            ? label
                            : "hygienic")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static label => label, StringComparer.Ordinal)];
                foreach (var justification in proven.Core)
                    if (userAssumptionIds.TryGetValue(justification, out var id))
                        usedUserAssumptions.Add(id);
                break;
            case RefutedOutcome when usesSpecModeledCallResult:
                record.Outcome = WorkerClaimOutcome.Unknown;
                record.Reason =
                    WorkerClaimReason.CounterexampleReplayFailed;
                break;
            case RefutedOutcome refuted:
                record.Outcome = WorkerClaimOutcome.Refuted;
                record.Reason = WorkerClaimReason.None;
                record.Model = CreateModel(refuted, contracts);
                break;
            case UnknownOutcome unknown:
                record.Outcome = WorkerClaimOutcome.Unknown;
                record.Reason = MapAbstention(unknown.Reason);
                break;
            default:
                record.Outcome = WorkerClaimOutcome.Unknown;
                record.Reason =
                    WorkerClaimReason.MalformedBackendResult;
                break;
        }
        record.Assumptions = [.. target.Assumptions.Select(evidence =>
            new WorkerAssumptionEvidence {
                Id = evidence.Id,
                Kind = evidence.Kind,
                Used = evidence.Kind == WorkerAssumptionKind.UserAssume &&
                       usedUserAssumptions.Contains(evidence.Id)
            })];
        return record;
    }

    private static WorkerModelValue[] CreateModel(
        RefutedOutcome outcome,
        BoundMethodContracts contracts) {
        var names = contracts.Variables.ToDictionary(
            static variable => variable.Variable,
            static variable => variable.Role switch {
                BoundContractVariableRole.Parameter =>
                    "parameter:" + variable.Ordinal.ToString(
                        CultureInfo.InvariantCulture),
                BoundContractVariableRole.Receiver => "receiver",
                BoundContractVariableRole.Result => "result",
                BoundContractVariableRole.PreState =>
                    "pre:" + (variable.CurrentStateVariable?.Value ?? -1)
                        .ToString(CultureInfo.InvariantCulture),
                _ => "variable:" + variable.Variable.Value.ToString(
                    CultureInfo.InvariantCulture)
            });
        return [.. outcome.Model.Assignments
            .Select(assignment => new WorkerModelValue {
                Variable = names.TryGetValue(assignment.Key, out var name)
                    ? name
                    : "variable:" + assignment.Key.Value.ToString(
                        CultureInfo.InvariantCulture),
                Kind = assignment.Value.Kind.ToString(),
                Value = FormatValue(assignment.Value)
            })
            .OrderBy(static value => value.Variable, StringComparer.Ordinal)];
    }

    private static string FormatValue(IrValue value) => value.Kind switch {
        IrValueKind.Boolean => value.Boolean ? "true" : "false",
        IrValueKind.Integer => value.Integer.ToString(CultureInfo.InvariantCulture),
        IrValueKind.String => value.String,
        IrValueKind.Null => "null",
        _ => "<opaque>"
    };

    private static WorkerClaimResult CreateUnknown(
        ManifestCallableTarget target,
        int contractOrdinal,
        WorkerClaimReason reason) {
        var record = CreateBaseRecord(target, contractOrdinal);
        record.Outcome = WorkerClaimOutcome.Unknown;
        record.Reason = reason;
        return record;
    }

    private static ImmutableArray<WorkerClaimResult> CreateUnknowns(
        ManifestCallableTarget target,
        WorkerClaimReason reason) =>
        [.. target.Claims.Select((_, index) =>
            CreateUnknown(target, index, reason))];

    private static WorkerClaimResult CreateBaseRecord(
        ManifestCallableTarget target,
        int contractOrdinal) =>
        new() {
            ClaimId = target.Claims[contractOrdinal].Entry.ClaimId,
            Outcome = WorkerClaimOutcome.Unknown,
            Reason = WorkerClaimReason.InfrastructureFailure,
            Assumptions = [.. target.Assumptions]
        };

    private static WorkerClaimReason MapBindingFailure(
        ContractBindingFailure failure) => failure switch {
            ContractBindingFailure.UnsupportedExpression =>
                WorkerClaimReason.UnsupportedExpression,
            ContractBindingFailure.ResultOutsideEnsures or
            ContractBindingFailure.OldOutsideEnsures or
            ContractBindingFailure.NestedOld or
            ContractBindingFailure.InvalidIntrinsicSignature or
            ContractBindingFailure.NonBooleanCondition or
            ContractBindingFailure.InvalidClosedAttribute or
            ContractBindingFailure.InvalidClausePlacement =>
                WorkerClaimReason.UnsupportedContract,
            _ => WorkerClaimReason.UnsupportedCallable
        };

    private ContractBinder ContractBinder =>
        LazyInitializer.EnsureInitialized(
            ref _contractBinder,
            () => new ContractBinder(_compilation, _factory));

    private static WorkerClaimReason MapAbstention(
        AbstentionReason reason) => reason switch {
            AbstentionReason.UnsupportedOperation =>
                WorkerClaimReason.UnsupportedExpression,
            AbstentionReason.UnsupportedEncoding =>
                WorkerClaimReason.UnsupportedExpression,
            AbstentionReason.ResourceLimit =>
                WorkerClaimReason.ResourceLimit,
            AbstentionReason.Timeout =>
                WorkerClaimReason.MethodTimeout,
            AbstentionReason.BackendUnavailable =>
                WorkerClaimReason.BackendUnavailable,
            AbstentionReason.InfrastructureFailure =>
                WorkerClaimReason.InfrastructureFailure,
            AbstentionReason.MalformedBackendResult =>
                WorkerClaimReason.MalformedBackendResult,
            AbstentionReason.CounterexampleReplayFailed =>
                WorkerClaimReason.CounterexampleReplayFailed,
            _ => WorkerClaimReason.UnsupportedExpression
        };

    private bool IsKnownPure(IMethodSymbol method) =>
        _apiSpecs.IsSideEffectFree(method);

    private static int GetDepth(IrTerm root) {
        var memo = new Dictionary<IrId, int>();
        return Visit(root);

        int Visit(IrTerm term) {
            if (memo.TryGetValue(term.Id, out var existing)) return existing;
            var children = GetChildren(term);
            var depth = children.Length == 0
                ? 1
                : 1 + children.Max(Visit);
            memo.Add(term.Id, depth);
            return depth;
        }
    }

    private static ImmutableHashSet<IrVarId> CollectVariables(IrTerm root) {
        var result = ImmutableHashSet.CreateBuilder<IrVarId>();
        var pending = new Stack<IrTerm>();
        var visited = new HashSet<IrId>();
        pending.Push(root);
        while (pending.Count != 0) {
            var term = pending.Pop();
            if (!visited.Add(term.Id)) continue;
            if (term is IrVariableTerm variable)
                result.Add(variable.Variable);
            foreach (var child in GetChildren(term)) pending.Push(child);
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<IrTerm> GetChildren(IrTerm term) =>
        term switch {
            IrOpaqueTerm opaque =>
                [.. opaque.Receiver == null
                    ? opaque.Arguments
                    : opaque.Arguments.Insert(0, opaque.Receiver)],
            IrUnaryTerm unary => [unary.Operand],
            IrBinaryTerm binary => [binary.Left, binary.Right],
            IrConditionalTerm conditional =>
                [conditional.Condition, conditional.WhenTrue, conditional.WhenFalse],
            IrCastTerm cast => [cast.Operand],
            IrLengthTerm length => [length.Value],
            IrSequenceAccessTerm access => [access.Sequence, access.Index],
            _ => []
        };

    private readonly struct BodyLoweringResult {
        private BodyLoweringResult(
            ImmutableArray<BodyPath> paths,
            WorkerClaimReason reason,
            bool isSuccess) {
            Paths = paths;
            Reason = reason;
            IsSuccess = isSuccess;
        }

        internal ImmutableArray<BodyPath> Paths { get; }
        internal bool UsesSpecModeledCallResult =>
            Paths.Any(path => !path.SpecAssumptions.IsDefaultOrEmpty);
        internal WorkerClaimReason Reason { get; }
        internal bool IsSuccess { get; }
        internal static BodyLoweringResult Single(
            IrFactory factory,
            IrTerm? term) =>
            Success([
                new BodyPath(
                    factory.Boolean(true),
                    term,
                    ImmutableDictionary<IrVarId, IrTerm>.Empty,
                    ImmutableDictionary<IrVarId, SpecResultProjection>.Empty,
                    [])
            ]);
        internal static BodyLoweringResult Success(
            ImmutableArray<BodyPath> paths) =>
            new(paths, WorkerClaimReason.None, true);
        internal static BodyLoweringResult Fail(
            WorkerClaimReason reason) =>
            new([], reason, false);
    }

    private sealed class ProgramOperationGroup(
        IrBlockId block,
        int startInstruction,
        int endInstruction,
        OperationId operation,
        IOperation? source,
        ImmutableArray<IrInstruction> instructions) {
        internal IrBlockId Block { get; } = block;
        internal int StartInstruction { get; } = startInstruction;
        internal int EndInstruction { get; } = endInstruction;
        internal OperationId Operation { get; } = operation;
        internal IOperation? Source { get; } = source;
        internal ImmutableArray<IrInstruction> Instructions { get; } =
            instructions;

        internal ProgramOperationGroup WithSource(IOperation? source) =>
            new(
                Block,
                StartInstruction,
                EndInstruction,
                Operation,
                source,
                Instructions);
    }

    private sealed record SymbolicExecutionState(
        IrBlockId Block,
        int StartInstruction,
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
