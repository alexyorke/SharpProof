namespace SharpProof.CompilerArtifact;
#pragma warning disable IDE0055 // Compact preparation code preserves the fixed production-size ceiling.

internal sealed class CompilerCallableLowerer {
    private const int MaximumBodyBlocks = 64; private readonly IrFactory _factory;
    private readonly ContractBinder _contracts; private readonly ResolvedApiSpecTable _apiSpecs;

    internal CompilerCallableLowerer(CSharpCompilation compilation, IrFactory factory) {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory)); _contracts = new ContractBinder(compilation, factory);
        _apiSpecs = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
    }

    internal CompilerCallablePreparation Prepare(ManifestCallableTarget target, CancellationToken cancellationToken = default) {
        if (target == null) throw new ArgumentNullException(nameof(target));
        cancellationToken.ThrowIfCancellationRequested();
        if (!target.IsVerifierSupported || target.Declaration is not BaseMethodDeclarationSyntax || target.SemanticModel == null)
            return Fail(target, WorkerClaimReason.UnsupportedCallable);
        var binding = _contracts.Bind(target.Method);
        if (!binding.IsSuccess) return Fail(target, MapBindingFailure(binding.Failure));
        var contracts = binding.Contracts!;
        var preconditions = target.Entry.Assumptions.Where(static evidence => evidence.Kind == WorkerAssumptionKind.Precondition).ToArray();
        var userAssumptions = target.Entry.Assumptions.Where(static evidence => evidence.Kind == WorkerAssumptionKind.UserAssume).ToArray();
        if (contracts.Clauses.Count(static clause => clause.Kind == BoundContractKind.Requires) != preconditions.Length ||
            contracts.Clauses.Count(static clause => clause.Kind == BoundContractKind.Assume) != userAssumptions.Length)
            return Fail(target, WorkerClaimReason.UnsupportedContract);
        if (!HasManifestParity(target, contracts)) return Fail(target, WorkerClaimReason.UnsupportedContract);
        var preconditionOrdinal = 0;
        var assumptionOrdinal = 0;
        var claimOrdinal = 0;
        ImmutableArray<CompilerPreparedClause> clauses = [.. contracts.Clauses.Select(clause => new CompilerPreparedClause(
            Kind(clause.Kind), clause.Condition, CompilerEvidence(clause.Evidence),
            clause.Kind == BoundContractKind.Ensures ? target.Claims[claimOrdinal++].Entry.ClaimId : null,
            clause.Kind == BoundContractKind.Requires ? preconditions[preconditionOrdinal++].Id :
                clause.Kind == BoundContractKind.Assume ? userAssumptions[assumptionOrdinal++].Id : null))];
        ImmutableArray<CompilerCanonicalVariable> variables = [.. contracts.Variables.Select(
            variable => CreateVariable(variable, contracts))];
        if (target.Claims.IsDefaultOrEmpty) return Success(target, clauses, variables, body: null);
        var preparedBody = PrepareBody(target, contracts, cancellationToken, out var failure);
        return failure == WorkerClaimReason.None
            ? Success(target, clauses, variables, preparedBody)
            : Fail(target, failure, clauses, variables);
    }

    private CompilerPreparedBody? PrepareBody(ManifestCallableTarget target, BoundMethodContracts contracts,
        CancellationToken cancellationToken, out WorkerClaimReason failure) {
        cancellationToken.ThrowIfCancellationRequested();
        if (target.Method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None))
            return Unsupported(out failure);
        if (target.Method.ReturnsVoid || target.Method.MethodKind == MethodKind.Constructor) {
            failure = ContainsOnlyContractStatements(target) ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody;
            return failure == WorkerClaimReason.None ? CompilerPreparedBody.Trivial() : null;
        }
        var bodyStart = FindExecutableBodyStart(target);
        if (!bodyStart.HasValue) return Unsupported(out failure);
        Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph? graph;
        try { graph = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(target.VerifierDeclaration, target.VerifierSemanticModel); }
        catch (ArgumentException) { return Unsupported(out failure); }
        if (graph == null) return Unsupported(out failure);
        var lowering = new RoslynProgramLowerer(_factory, IsKnownPure).Lower(graph);
        if (!TryCreateProgramGroups(graph, lowering.Program, out var groups)) return Unsupported(out failure);
        var start = groups.FirstOrDefault(group => IsAtOrAfterBodyStart(group.Source, bodyStart.Value));
        if (start == null) return Unsupported(out failure);
        var groupsByOperation = groups.ToDictionary(static group => group.Operation);
        foreach (var abstention in lowering.Abstentions) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!groupsByOperation.TryGetValue(abstention.Operation, out var group) || IsAtOrAfterBodyStart(group.Source, bodyStart.Value))
                return Unsupported(out failure);
        }
        var executableGroups = groups.Where(group => IsAtOrAfterBodyStart(group.Source, bodyStart.Value));
        if (!TryCreateCallBindings(executableGroups, cancellationToken, out var callBindings) ||
            !TryValidateAcyclicBody(lowering.Program, start.Block, cancellationToken) ||
            !TryNormalizeProgram(lowering.Program, start.Block, start.StartInstruction,
                out var program, out var instructionMap) ||
            !TryCreateParameterBindings(target, contracts, lowering.Variables, out var parameterBindings))
            return Unsupported(out failure);
        var specCalls = ImmutableDictionary.CreateBuilder<IrInstructionId, CompilerPreparedSpecCall>();
        foreach (var binding in callBindings) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!instructionMap.TryGetValue(binding.Key.Id, out var mapped)) continue;
            if (mapped is not IrCallInstruction call ||
                !TryGetCallIdentity(binding.Value.TargetMethod, out var callIdentity) ||
                !TryPrepareSpecCall(call, binding.Value, callIdentity, out var prepared))
                return Unsupported(out failure);
            specCalls.Add(call.Id, prepared!);
        }
        if (specCalls.Count != program.Blocks.SelectMany(static block => block.Instructions)
                .Count(static instruction => instruction is IrCallInstruction))
            return Unsupported(out failure);
        failure = WorkerClaimReason.None;
        return CompilerPreparedBody.ProgramBody(program, parameterBindings, specCalls.ToImmutable());
    }

    private static CompilerPreparedBody? Unsupported(out WorkerClaimReason failure) { failure = WorkerClaimReason.UnsupportedBody; return null; }

    private static bool HasManifestParity(ManifestCallableTarget target, BoundMethodContracts contracts) {
        var ensures = contracts.Clauses.Where(static clause => clause.Kind == BoundContractKind.Ensures).ToImmutableArray();
        if (ensures.Length != target.Claims.Length ||
            target.Entry.ClaimIds.Length != target.Claims.Length ||
            !target.Entry.ClaimIds.SequenceEqual(target.Claims.Select(static claim => claim.Entry.ClaimId), StringComparer.Ordinal))
            return false;
        for (var index = 0; index < ensures.Length; index++) {
            var claim = target.Claims[index];
            if (claim.Entry.Ordinal != index ||
                claim.Entry.Kind != WorkerClaimKind.Postcondition ||
                claim.Entry.CallableId != target.Entry.CallableId ||
                claim.Entry.Evidence != ManifestEvidence(ensures[index].Evidence) ||
                (ensures[index].Evidence == BoundContractEvidence.ClosedAttribute) != (claim.SourceAttribute != null) ||
                (ensures[index].Evidence != BoundContractEvidence.ClosedAttribute) != (claim.SourceOperation != null))
                return false;
        }
        return true;
    }

    private static CompilerCanonicalVariable CreateVariable(BoundContractVariable variable, BoundMethodContracts contracts) {
        var source = variable.Role switch {
            BoundContractVariableRole.Parameter when variable.Symbol is IParameterSymbol parameter => parameter.Type,
            BoundContractVariableRole.Receiver => variable.Symbol as ITypeSymbol, BoundContractVariableRole.Result => contracts.Target.ReturnType,
            _ => null
        };
        return new CompilerCanonicalVariable(Role(variable.Role), variable.Ordinal, variable.Variable,
            variable.CurrentStateVariable, IntegerInterval(source?.SpecialType), ModelLabel(variable));
    }

    private static CompilerIntegerInterval? IntegerInterval(SpecialType? type) => type switch {
        SpecialType.System_SByte => new(sbyte.MinValue, sbyte.MaxValue), SpecialType.System_Byte => new(byte.MinValue, byte.MaxValue),
        SpecialType.System_Int16 => new(short.MinValue, short.MaxValue), SpecialType.System_UInt16 => new(ushort.MinValue, ushort.MaxValue),
        SpecialType.System_Char => new(char.MinValue, char.MaxValue), SpecialType.System_Int32 => new(int.MinValue, int.MaxValue),
        SpecialType.System_UInt32 => new(uint.MinValue, uint.MaxValue),
        _ => null
    };

    private static string ModelLabel(BoundContractVariable variable) => variable.Role switch {
        BoundContractVariableRole.Parameter => "parameter:" + variable.Ordinal.ToString(CultureInfo.InvariantCulture),
        BoundContractVariableRole.Receiver => "receiver", BoundContractVariableRole.Result => "result",
        BoundContractVariableRole.PreState => "pre:" + (variable.CurrentStateVariable?.Value ?? -1).ToString(CultureInfo.InvariantCulture),
        _ => "variable:" + variable.Variable.Value.ToString(CultureInfo.InvariantCulture)
    };

    private bool TryPrepareSpecCall(IrCallInstruction call, IInvocationOperation invocation, string callIdentity,
        out CompilerPreparedSpecCall? prepared) {
        prepared = null;
        if (!call.Target.HasValue ||
            invocation.TargetMethod.ReducedFrom != null ||
            invocation.TargetMethod.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            !_apiSpecs.TryGet(invocation.TargetMethod, out var resolved) ||
            !TryAdmitSpecCallEffects(invocation, call, resolved.Template, out var consumesMemoryHavoc) ||
            !resolved.Template.Result.HasValue ||
            !TryGetSpecResultType(invocation.Type, resolved.Template.Target.ResultType,
                _factory.GetVariableInfo(call.Target.Value).Type, out var resultType) ||
            _factory.GetVariableInfo(call.Target.Value).Type != resultType ||
            invocation.Arguments.Length != resolved.Template.Parameters.Length ||
            !HasDirectArgumentOrder(invocation) ||
            resolved.Template.Target.DocumentationCommentId != callIdentity ||
            resolved.Template.Receiver.HasValue != (call.Receiver != null) ||
            call.Arguments.Length != resolved.Template.Parameters.Length)
            return false;
        prepared = new CompilerPreparedSpecCall(
            call.Id, callIdentity, resolved.Template.Target.WitnessIdentifier, consumesMemoryHavoc);
        return true;
    }

    private static bool TryGetCallIdentity(IMethodSymbol method, out string identity) {
        var symbol = ResolvedApiSpecTable.NormalizeSymbol(method);
        identity = symbol?.GetDocumentationCommentId() ?? string.Empty;
        return identity.Length != 0;
    }

    private static bool TryAdmitSpecCallEffects(IInvocationOperation invocation, IrCallInstruction call,
        ApiSpecTemplate template, out bool consumesMemoryHavoc) {
        var effects = template.Facets.Effects.Effects; consumesMemoryHavoc = effects != SpecEffect.None;
        var cardinality = template.Facets.Cardinality; return !consumesMemoryHavoc ||
               effects == SpecEffect.Unknown &&
               invocation.TargetMethod.IsStatic &&
               invocation.TargetMethod.Parameters.IsEmpty && invocation.Instance == null &&
               invocation.Arguments.IsEmpty && call.Receiver == null && call.Arguments.IsEmpty &&
               invocation.Type is IArrayTypeSymbol &&
               !template.Receiver.HasValue &&
               template.Parameters.IsEmpty &&
               template.Postconditions.IsDefaultOrEmpty &&
               template.Facets.Nullness.Result == SpecNullness.NonNull &&
               (cardinality.Result is SpecCardinality.Empty or SpecCardinality.NonEmpty
                || cardinality is { Result: SpecCardinality.Exact, ExactCount: not null });
    }

    private static bool HasDirectArgumentOrder(IInvocationOperation invocation) {
        if (invocation.Arguments.Length != invocation.TargetMethod.Parameters.Length) return false;
        for (var index = 0; index < invocation.Arguments.Length; index++) {
            var argument = invocation.Arguments[index];
            if (argument.ArgumentKind != ArgumentKind.Explicit || argument.Parameter?.Ordinal != index) return false;
        }
        return true;
    }

    private bool TryGetSpecResultType(ITypeSymbol? sourceType, SpecValueType? specType,
        IrTypeId loweredResultType, out IrTypeId resultType) {
        switch (specType) {
            case SpecValueType.Boolean when sourceType?.SpecialType == SpecialType.System_Boolean:
                resultType = _factory.BooleanType; return true;
            case SpecValueType.Integer when sourceType?.SpecialType is
                SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or
                SpecialType.System_UInt16 or SpecialType.System_Char or SpecialType.System_Int32 or
                SpecialType.System_UInt32 or SpecialType.System_Int64:
                resultType = _factory.IntegerType; return true;
            case SpecValueType.String when sourceType?.SpecialType == SpecialType.System_String:
                resultType = _factory.StringType; return true;
            case SpecValueType.Sequence when sourceType is IArrayTypeSymbol
                && _factory.GetTypeInfo(loweredResultType).Kind == IrTypeKind.Sequence:
                resultType = loweredResultType; return true;
            default: resultType = default; return false;
        }
    }

    private int? FindExecutableBodyStart(ManifestCallableTarget target) {
        if (target.VerifierDeclaration.ExpressionBody != null) return target.VerifierDeclaration.ExpressionBody.Expression.SpanStart;
        if (target.VerifierDeclaration.Body == null) return null;
        foreach (var statement in target.VerifierDeclaration.Body.Statements) {
            if (statement is EmptyStatementSyntax || IsContractStatement(target, statement)) continue;
            return statement.SpanStart;
        }
        return null;
    }

    private static bool IsAtOrAfterBodyStart(IOperation? operation, int bodyStart) =>
        operation != null && (operation.Syntax.SpanStart >= bodyStart || operation.Syntax.Span.Contains(bodyStart));

    private static bool TryCreateProgramGroups(Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph graph, IrProgram program,
        out ImmutableArray<ProgramOperationGroup> groups) {
        groups = [];
        if (graph.Blocks.Length != program.Blocks.Length) return false;
        var result = ImmutableArray.CreateBuilder<ProgramOperationGroup>();
        for (var blockIndex = 0; blockIndex < graph.Blocks.Length; blockIndex++) {
            var source = graph.Blocks[blockIndex]; var target = program.Blocks[blockIndex];
            var instructionGroups = CreateInstructionGroups(target);
            var groupIndex = 0; var terminated = false;
            foreach (var operation in source.Operations) {
                if (operation is IEmptyOperation) continue;
                if (groupIndex >= instructionGroups.Length) return false;
                result.Add(instructionGroups[groupIndex++].WithSource(operation));
                if (operation is IReturnOperation) { terminated = true; break; }
            }
            if (!terminated) {
                if (groupIndex >= instructionGroups.Length) return false;
                result.Add(instructionGroups[groupIndex++].WithSource(source.BranchValue));
            }
            if (groupIndex != instructionGroups.Length) return false;
        }
        groups = result.ToImmutable(); return true;
    }

    private static ImmutableArray<ProgramOperationGroup> CreateInstructionGroups(IrBasicBlock block) {
        var groups = ImmutableArray.CreateBuilder<ProgramOperationGroup>();
        var start = 0;
        while (start < block.Instructions.Length) {
            var operation = block.Instructions[start].Operation; var end = start + 1;
            while (end < block.Instructions.Length && block.Instructions[end].Operation == operation) end++;
            groups.Add(new ProgramOperationGroup(block.Id, start, operation, null, block.Instructions.Slice(start, end - start)));
            start = end;
        }
        return groups.ToImmutable();
    }

    private bool TryCreateCallBindings(IEnumerable<ProgramOperationGroup> groups, CancellationToken cancellationToken,
        out ImmutableDictionary<IrCallInstruction, IInvocationOperation> bindings) {
        var result = ImmutableDictionary.CreateBuilder<IrCallInstruction, IInvocationOperation>();
        foreach (var group in groups) {
            cancellationToken.ThrowIfCancellationRequested();
            var calls = group.Instructions.OfType<IrCallInstruction>().ToImmutableArray();
            if (calls.IsDefaultOrEmpty) continue;
            if (group.Source == null) { bindings = ImmutableDictionary<IrCallInstruction, IInvocationOperation>.Empty; return false; }
            var invocations = EnumerateInvocationsInEvaluationOrder(group.Source).ToImmutableArray();
            if (calls.Length != invocations.Length) { bindings = ImmutableDictionary<IrCallInstruction, IInvocationOperation>.Empty; return false; }
            for (var index = 0; index < calls.Length; index++) {
                var call = calls[index]; var invocation = invocations[index];
                var member = _factory.GetMemberInfo(call.Member);
                if (member.Identity != CompilerIdentityBridge.InternSymbol(_factory, invocation.TargetMethod)
                    || member.IsStatic != (invocation.Instance == null) || member.ParameterTypes.Length != invocation.Arguments.Length) {
                    bindings = ImmutableDictionary<IrCallInstruction, IInvocationOperation>.Empty; return false;
                }
                result.Add(call, invocation);
            }
        }
        bindings = result.ToImmutable(); return true;
    }

    private static IEnumerable<IInvocationOperation> EnumerateInvocationsInEvaluationOrder(IOperation operation) {
        foreach (var child in operation.ChildOperations)
            foreach (var invocation in EnumerateInvocationsInEvaluationOrder(child)) yield return invocation;
        if (operation is IInvocationOperation current) yield return current;
    }

    private static bool TryValidateAcyclicBody(IrProgram program, IrBlockId start, CancellationToken cancellationToken) {
        var colors = new Dictionary<IrBlockId, int>(); var reachable = 0;
        return Visit(start);

        bool Visit(IrBlockId blockId) {
            cancellationToken.ThrowIfCancellationRequested();
            if (colors.TryGetValue(blockId, out var color)) return color == 2;
            if (++reachable > MaximumBodyBlocks) return false;
            colors.Add(blockId, 1);
            var block = program.GetBlock(blockId);
            foreach (var successor in GetSuccessors(block.Terminator)) {
                if (colors.TryGetValue(successor, out color) && color == 1) return false;
                if (!Visit(successor)) return false;
            }
            colors[blockId] = 2; return true;
        }
    }

    private static bool TryNormalizeProgram(IrProgram source, IrBlockId start, int startInstruction,
        out IrProgram program, out ImmutableDictionary<IrInstructionId, IrInstruction> instructionMap) {
        program = null!; instructionMap = ImmutableDictionary<IrInstructionId, IrInstruction>.Empty;
        if (startInstruction < 0 || startInstruction >= source.GetBlock(start).Instructions.Length) return false;
        var reachable = new HashSet<IrBlockId>(); var pending = new Stack<IrBlockId>(); pending.Push(start);
        while (pending.Count != 0) {
            var current = pending.Pop(); if (!reachable.Add(current)) continue;
            foreach (var successor in GetSuccessors(source.GetBlock(current).Terminator)) pending.Push(successor); }
        var ordered = source.Blocks.Where(block => reachable.Contains(block.Id))
            .OrderBy(block => block.Id == start ? -1 : block.Id.Value).ToArray();
        var builder = new IrProgramBuilder(source.Factory); var instructions = ImmutableDictionary.CreateBuilder<IrInstructionId, IrInstruction>();
        var blocks = ordered.ToDictionary(static block => block.Id,
            block => builder.CreateBlock(block.Name.HasValue ? source.Factory.GetString(block.Name.Value) : null));
        foreach (var block in ordered) {
            var offset = block.Id == start ? startInstruction : 0;
            foreach (var value in block.Instructions.Skip(offset))
                instructions.Add(value.Id, CopyInstruction(builder, blocks[block.Id], blocks, value)); }
        program = builder.Build(); instructionMap = instructions.ToImmutable(); return program.Entry.Value == 0;
    }

    private static IrInstruction CopyInstruction(IrProgramBuilder builder, IrBlockId block,
        Dictionary<IrBlockId, IrBlockId> blocks, IrInstruction value) => value switch {
        IrAssignInstruction x => builder.Assign(block, x.Operation, x.Target, x.Value), IrLoadInstruction x => builder.Load(block, x.Operation, x.Target, CopyLocation(builder, x.Location)),
        IrStoreInstruction x => builder.Store(block, x.Operation, CopyLocation(builder, x.Location), x.Value), IrCallInstruction x => builder.Call(block, x.Operation, x.Target, x.Member, x.Receiver, [.. x.Arguments]),
        IrAssumeInstruction x => builder.Assume(block, x.Operation, x.Condition), IrAssertInstruction x => builder.Assert(block, x.Operation, x.Condition), IrHavocInstruction x => builder.Havoc(block, x.Operation, x.HavocKind, [.. x.Variables]),
        IrBranchInstruction x => builder.Branch(block, x.Operation, x.Condition, blocks[x.WhenTrue], blocks[x.WhenFalse]),
        IrGotoInstruction x => builder.Goto(block, x.Operation, blocks[x.Target]), IrReturnInstruction x => builder.Return(block, x.Operation, x.Value),
        _ => throw new InvalidOperationException("Unknown lowered instruction.")
    };

    private static IrLocation CopyLocation(IrProgramBuilder builder, IrLocation value) => value switch {
        IrMemberLocation x => builder.MemberLocation(x.Member, x.Receiver, [.. x.Arguments]), IrSequenceLocation x => builder.SequenceLocation(x.Sequence, x.Index),
        _ => throw new InvalidOperationException("Unknown lowered location.")
    };

    private static ImmutableArray<IrBlockId> GetSuccessors(IrInstruction terminator) => terminator switch {
        IrBranchInstruction branch => branch.WhenTrue == branch.WhenFalse
            ? [branch.WhenTrue] : [branch.WhenTrue, branch.WhenFalse],
        IrGotoInstruction go => [go.Target],
        IrReturnInstruction => [],
        _ => []
    };

    private static bool TryCreateParameterBindings(ManifestCallableTarget target, BoundMethodContracts contracts,
        ImmutableArray<FrontendVariableBinding> variables,
        out ImmutableDictionary<IrVarId, IrVarId> parameterBindings) {
        var canonicalParameters = contracts.Variables.Where(
            static variable => variable.Role == BoundContractVariableRole.Parameter).ToDictionary(static variable => variable.Ordinal);
        var bindings = ImmutableDictionary.CreateBuilder<IrVarId, IrVarId>();
        foreach (var binding in variables) {
            if (binding.Symbol is ILocalSymbol) continue;
            if (binding.Symbol is not IParameterSymbol parameter ||
                !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, target.Method) ||
                !canonicalParameters.TryGetValue(parameter.Ordinal, out var canonical)) {
                parameterBindings = ImmutableDictionary<IrVarId, IrVarId>.Empty; return false;
            }
            bindings.Add(binding.Variable, canonical.Variable);
        }
        parameterBindings = bindings.ToImmutable(); return true;
    }

    private bool ContainsOnlyContractStatements(ManifestCallableTarget target) => target.VerifierDeclaration.Body != null &&
        target.VerifierDeclaration.Body.Statements.All(statement => IsContractStatement(target, statement));

    private bool IsContractStatement(ManifestCallableTarget target, StatementSyntax statement) {
        if (statement is EmptyStatementSyntax) return true;
        return statement is ExpressionStatementSyntax expression && _contracts.GetClauseInventory(target.Method).Clauses.Any(clause =>
            clause.Invocation.Syntax.SyntaxTree == expression.SyntaxTree && clause.Invocation.Syntax.Span == expression.Expression.Span);
    }

    private static WorkerClaimEvidence ManifestEvidence(BoundContractEvidence evidence) => evidence switch {
        BoundContractEvidence.CompilerBoundInvocation => WorkerClaimEvidence.DirectClause,
        BoundContractEvidence.Companion => WorkerClaimEvidence.CompanionClause,
        BoundContractEvidence.ClosedAttribute => WorkerClaimEvidence.ReturnAttribute,
        _ => WorkerClaimEvidence.Unspecified
    };
    private static CompilerContractKind Kind(BoundContractKind value) => value switch {
        BoundContractKind.Requires => CompilerContractKind.Requires,
        BoundContractKind.Ensures => CompilerContractKind.Ensures,
        BoundContractKind.Assume => CompilerContractKind.Assume,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    private static CompilerContractEvidence CompilerEvidence(BoundContractEvidence value) => value switch {
        BoundContractEvidence.CompilerBoundInvocation => CompilerContractEvidence.CompilerBoundInvocation,
        BoundContractEvidence.ClosedAttribute => CompilerContractEvidence.ClosedAttribute,
        BoundContractEvidence.Companion => CompilerContractEvidence.Companion,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    private static CompilerVariableRole Role(BoundContractVariableRole value) => value switch {
        BoundContractVariableRole.Receiver => CompilerVariableRole.Receiver,
        BoundContractVariableRole.Parameter => CompilerVariableRole.Parameter,
        BoundContractVariableRole.Result => CompilerVariableRole.Result,
        BoundContractVariableRole.PreState => CompilerVariableRole.PreState,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static WorkerClaimReason MapBindingFailure(ContractBindingFailure failure) => failure switch {
        ContractBindingFailure.UnsupportedExpression => WorkerClaimReason.UnsupportedExpression,
        ContractBindingFailure.ResultOutsideEnsures or ContractBindingFailure.OldOutsideEnsures or ContractBindingFailure.NestedOld or
        ContractBindingFailure.InvalidIntrinsicSignature or ContractBindingFailure.NonBooleanCondition or ContractBindingFailure.InvalidClosedAttribute or
        ContractBindingFailure.InvalidClausePlacement => WorkerClaimReason.UnsupportedContract,
        _ => WorkerClaimReason.UnsupportedCallable
    };

    private bool IsKnownPure(IMethodSymbol method) => _apiSpecs.IsSideEffectFree(method);

    private CompilerCallablePreparation Success(ManifestCallableTarget target,
        ImmutableArray<CompilerPreparedClause> clauses,
        ImmutableArray<CompilerCanonicalVariable> variables,
        CompilerPreparedBody? body) => new(_factory, target.Entry,
            clauses.IsDefault ? [] : clauses, variables.IsDefault ? [] : variables, WorkerClaimReason.None, body);

    private CompilerCallablePreparation Fail(ManifestCallableTarget target, WorkerClaimReason reason,
        ImmutableArray<CompilerPreparedClause> clauses = default,
        ImmutableArray<CompilerCanonicalVariable> variables = default) => new(_factory, target.Entry,
            clauses.IsDefault ? [] : clauses, variables.IsDefault ? [] : variables, reason, null);

    private sealed record ProgramOperationGroup(IrBlockId Block, int StartInstruction, OperationId Operation,
        IOperation? Source, ImmutableArray<IrInstruction> Instructions) {
        internal ProgramOperationGroup WithSource(IOperation? source) =>
            this with { Source = source };
    }
}
