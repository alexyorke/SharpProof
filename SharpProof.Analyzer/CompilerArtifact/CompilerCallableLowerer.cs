namespace SharpProof.CompilerArtifact;

internal sealed class CompilerCallableLowerer
{
    private const int MaximumBodyBlocks = 64;
    private readonly IrFactory _factory;
    private readonly ContractBinder _contracts;
    private readonly ResolvedApiSpecTable _apiSpecs;

    internal CompilerCallableLowerer(CSharpCompilation compilation, IrFactory factory)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _contracts = new ContractBinder(compilation, factory);
        _apiSpecs = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
    }

    internal CompilerCallablePreparation Prepare(ManifestCallableTarget target, CancellationToken cancellationToken = default)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!target.IsVerifierSupported || target.Declaration is not BaseMethodDeclarationSyntax || target.SemanticModel == null)
        {
            return Fail(target, WorkerClaimReason.UnsupportedCallable);
        }

        var binding = _contracts.Bind(target.Method);
        if (!binding.IsSuccess)
        {
            return Fail(target, MapBindingFailure(binding.Failure));
        }

        var contracts = binding.Contracts!;
        var preconditions = target.Entry.Assumptions.Where(static evidence => evidence.Kind == WorkerAssumptionKind.Precondition).ToArray();
        var userAssumptions = target.Entry.Assumptions.Where(static evidence => evidence.Kind == WorkerAssumptionKind.UserAssume).ToArray();
        if (contracts.Clauses.Count(static clause => clause.Kind == BoundContractKind.Requires) != preconditions.Length ||
            contracts.Clauses.Count(static clause => clause.Kind == BoundContractKind.Assume) != userAssumptions.Length)
        {
            return Fail(target, WorkerClaimReason.UnsupportedContract);
        }

        if (!HasManifestParity(target, contracts))
        {
            return Fail(target, WorkerClaimReason.UnsupportedContract);
        }

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
        if (target.Claims.IsDefaultOrEmpty)
        {
            return Success(target, clauses, variables, body: null);
        }

        var preparedBody = PrepareBody(target, contracts, cancellationToken, out var failure);
        return failure == WorkerClaimReason.None
            ? Success(target, clauses, variables, preparedBody)
            : Fail(target, failure, clauses, variables);
    }

    private CompilerPreparedBody? PrepareBody(ManifestCallableTarget target, BoundMethodContracts contracts,
        CancellationToken cancellationToken, out WorkerClaimReason failure)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target.Method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None))
        {
            return Unsupported(out failure);
        }

        if (target.Method.MethodKind == MethodKind.Constructor)
        {
            return Unsupported(out failure);
        }

        if (target.Method.ReturnsVoid)
        {
            failure = ContainsOnlyContractStatements(target) ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody;
            return failure == WorkerClaimReason.None ? CompilerPreparedBody.Trivial() : null;
        }
        var bodyStart = FindExecutableBodyStart(target);
        if (!bodyStart.HasValue)
        {
            return Unsupported(out failure);
        }

        Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph? graph;
        try
        {
            graph = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(
                target.VerifierDeclaration,
                target.VerifierSemanticModel);
        }
        catch (ArgumentException)
        {
            return Unsupported(out failure);
        }
        if (graph == null)
        {
            return Unsupported(out failure);
        }

        var elidedClauseSites = _contracts.GetClauseInventory(target.Method).Clauses
            .Where(static clause => !clause.IsValid)
            .Select(static clause => clause.Invocation.Syntax)
            .ToImmutableArray();
        if (!TryFindProgramStart(
                graph, bodyStart.Value, out var entry, out var firstOperation))
        {
            return Unsupported(out failure);
        }

        var selected = new RoslynProgramLowerer(_factory, IsKnownPure).LowerSelected(
            graph, entry!, firstOperation,
            operation => ContainsElidedClause(operation, elidedClauseSites));
        var lowering = selected.Lowering;
        if (!lowering.IsExact ||
            !TryValidateAcyclicBody(lowering.Program, lowering.Program.Entry, cancellationToken) ||
            !TryCreateParameterBindings(
                target, contracts, lowering.Variables, out var parameterBindings))
        {
            return Unsupported(out failure);
        }

        var specCalls = ImmutableDictionary.CreateBuilder<IrInstructionId, CompilerPreparedSpecCall>();
        foreach (var binding in selected.Calls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetCallIdentity(binding.Value.TargetMethod, out var callIdentity) ||
                !TryPrepareSpecCall(
                    binding.Key, binding.Value, callIdentity, out var prepared))
            {
                return Unsupported(out failure);
            }

            specCalls.Add(binding.Key.Id, prepared!);
        }
        if (specCalls.Count != lowering.Program.Blocks
                .SelectMany(static block => block.Instructions)
                .Count(static instruction => instruction is IrCallInstruction))
        {
            return Unsupported(out failure);
        }

        failure = WorkerClaimReason.None;
        return CompilerPreparedBody.ProgramBody(
            lowering.Program, parameterBindings, specCalls.ToImmutable());
    }

    private static CompilerPreparedBody? Unsupported(out WorkerClaimReason failure)
    {
        failure = WorkerClaimReason.UnsupportedBody;
        return null;
    }

    private static bool HasManifestParity(ManifestCallableTarget target, BoundMethodContracts contracts)
    {
        var ensures = contracts.Clauses.Where(static clause => clause.Kind == BoundContractKind.Ensures).ToImmutableArray();
        if (ensures.Length != target.Claims.Length ||
            !target.Entry.ClaimIds.Take(target.Claims.Length).SequenceEqual(
                target.Claims.Select(static claim => claim.Entry.ClaimId), StringComparer.Ordinal))
        {
            return false;
        }

        for (var index = 0; index < ensures.Length; index++)
        {
            var claim = target.Claims[index];
            if (claim.Entry.Ordinal != index ||
                claim.Entry.Kind != WorkerClaimKind.Postcondition ||
                claim.Entry.CallableId != target.Entry.CallableId ||
                claim.Entry.Evidence != ManifestEvidence(ensures[index].Evidence) ||
                (ensures[index].Evidence == BoundContractEvidence.ClosedAttribute) != (claim.SourceAttribute != null) ||
                (ensures[index].Evidence != BoundContractEvidence.ClosedAttribute) != (claim.SourceOperation != null))
            {
                return false;
            }
        }
        return true;
    }

    private static CompilerCanonicalVariable CreateVariable(BoundContractVariable variable, BoundMethodContracts contracts)
    {
        var source = variable.Role switch
        {
            BoundContractVariableRole.Parameter when variable.Symbol is IParameterSymbol parameter => parameter.Type,
            BoundContractVariableRole.Receiver => variable.Symbol as ITypeSymbol,
            BoundContractVariableRole.Result => contracts.Target.ReturnType,
            _ => null
        };
        return new CompilerCanonicalVariable(Role(variable.Role), variable.Ordinal, variable.Variable,
            variable.CurrentStateVariable, IntegerInterval(source?.SpecialType), ModelLabel(variable));
    }

    private static CompilerIntegerInterval? IntegerInterval(SpecialType? type)
    {
        return type.HasValue && CSharpScalarSemantics.TryGetInteger(type.Value, out var semantics) && semantics.BitWidth < 64
            ? new(semantics.Minimum, semantics.Maximum) : null;
    }

    private static string ModelLabel(BoundContractVariable variable)
    {
        return variable.Role switch
        {
            BoundContractVariableRole.Parameter => "parameter:" + variable.Ordinal.ToString(CultureInfo.InvariantCulture),
            BoundContractVariableRole.Receiver => "receiver",
            BoundContractVariableRole.Result => "result",
            BoundContractVariableRole.PreState =>
                "pre:" +
                (variable.CurrentStateVariable?.Value ?? -1)
                    .ToString(CultureInfo.InvariantCulture),
            _ => "variable:" + variable.Variable.Value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private bool TryPrepareSpecCall(IrCallInstruction call, IInvocationOperation invocation, string callIdentity,
        out CompilerPreparedSpecCall? prepared)
    {
        prepared = null;
        if (!call.Target.HasValue ||
            !RoslynProgramLowerer.IsDirectInvocation(invocation) ||
            invocation.TargetMethod.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            !_apiSpecs.TryGet(invocation.TargetMethod, out var resolved) ||
            !TryAdmitSpecCallEffects(invocation, call, resolved.Template, out var consumesMemoryHavoc) ||
            !resolved.Template.Result.HasValue ||
            !TryGetSpecResultType(invocation.Type, resolved.Template.Target.ResultType,
                _factory.GetVariableInfo(call.Target.Value).Type, out var resultType) ||
            _factory.GetVariableInfo(call.Target.Value).Type != resultType ||
            invocation.Arguments.Length != resolved.Template.Parameters.Length ||
            resolved.Template.Target.DocumentationCommentId != callIdentity ||
            resolved.Template.Receiver.HasValue != (call.Receiver != null) ||
            call.Arguments.Length != resolved.Template.Parameters.Length)
        {
            return false;
        }

        prepared = new CompilerPreparedSpecCall(
            call.Id, callIdentity, resolved.Template.Target.WitnessIdentifier, consumesMemoryHavoc);
        return true;
    }

    private static bool TryGetCallIdentity(IMethodSymbol method, out string identity)
    {
        var symbol = ResolvedApiSpecTable.NormalizeSymbol(method);
        identity = symbol?.GetDocumentationCommentId() ?? string.Empty;
        return identity.Length != 0;
    }

    private static bool TryAdmitSpecCallEffects(IInvocationOperation invocation, IrCallInstruction call,
        ApiSpecTemplate template, out bool consumesMemoryHavoc)
    {
        var effects = template.Facets.Effects.Effects;
        consumesMemoryHavoc = effects != SpecEffect.None;
        var cardinality = template.Facets.Cardinality;
        return !consumesMemoryHavoc ||
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

    private bool TryGetSpecResultType(ITypeSymbol? sourceType, SpecValueType? specType,
        IrTypeId loweredResultType, out IrTypeId resultType)
    {
        switch (specType)
        {
            case SpecValueType.Boolean when sourceType?.SpecialType == SpecialType.System_Boolean:
                resultType = _factory.BooleanType;
                return true;
            case SpecValueType.Integer when CSharpScalarSemantics.IsSupportedInteger(
                sourceType?.SpecialType ?? SpecialType.None):
                resultType = _factory.IntegerType;
                return true;
            case SpecValueType.String when sourceType?.SpecialType == SpecialType.System_String:
                resultType = _factory.StringType;
                return true;
            case SpecValueType.Sequence when sourceType is IArrayTypeSymbol
                && _factory.GetTypeInfo(loweredResultType).Kind == IrTypeKind.Sequence:
                resultType = loweredResultType;
                return true;
            default:
                resultType = default;
                return false;
        }
    }

    private int? FindExecutableBodyStart(ManifestCallableTarget target)
    {
        if (target.VerifierDeclaration.ExpressionBody != null)
        {
            return target.VerifierDeclaration.ExpressionBody.Expression.SpanStart;
        }

        if (target.VerifierDeclaration.Body == null)
        {
            return null;
        }

        foreach (var statement in target.VerifierDeclaration.Body.Statements)
        {
            if (statement is EmptyStatementSyntax || IsContractStatement(target, statement))
            {
                continue;
            }

            return statement.SpanStart;
        }
        return null;
    }

    private static bool IsAtOrAfterBodyStart(IOperation? operation, int bodyStart)
    {
        return operation != null && (operation.Syntax.SpanStart >= bodyStart || operation.Syntax.Span.Contains(bodyStart));
    }

    private static bool ContainsElidedClause(
        IOperation? operation, ImmutableArray<SyntaxNode> sites)
    {
        return operation != null && sites.Any(site =>
            site.SyntaxTree == operation.Syntax.SyntaxTree &&
            operation.Syntax.Span.Contains(site.Span));
    }

    private static bool TryFindProgramStart(
        Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph graph,
        int bodyStart,
        out Microsoft.CodeAnalysis.FlowAnalysis.BasicBlock? entry,
        out int firstOperation)
    {
        foreach (var block in graph.Blocks.OrderBy(static block => block.Ordinal))
        {
            for (var index = 0; index < block.Operations.Length; index++)
            {
                if (block.Operations[index] is not IEmptyOperation &&
                    IsAtOrAfterBodyStart(block.Operations[index], bodyStart))
                {
                    entry = block;
                    firstOperation = index;
                    return true;
                }
            }

            if (IsAtOrAfterBodyStart(block.BranchValue, bodyStart))
            {
                entry = block;
                firstOperation = block.Operations.Length;
                return true;
            }
        }
        entry = null;
        firstOperation = -1;
        return false;
    }

    private static bool TryValidateAcyclicBody(IrProgram program, IrBlockId start, CancellationToken cancellationToken)
    {
        var colors = new Dictionary<IrBlockId, int>();
        var reachable = 0;
        var instructions = 0;
        return Visit(start);

        bool Visit(IrBlockId blockId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (colors.TryGetValue(blockId, out var color))
            {
                return color == 2;
            }

            if (++reachable > MaximumBodyBlocks)
            {
                return false;
            }

            colors.Add(blockId, 1);
            var block = program.GetBlock(blockId);
            if (block.Instructions.Length > CompilerPreparedBody.MaximumInstructions - instructions)
            {
                return false;
            }

            instructions += block.Instructions.Length;
            foreach (var successor in GetSuccessors(block.Terminator))
            {
                if (colors.TryGetValue(successor, out color) && color == 1)
                {
                    return false;
                }

                if (!Visit(successor))
                {
                    return false;
                }
            }
            colors[blockId] = 2;
            return true;
        }
    }

    private static ImmutableArray<IrBlockId> GetSuccessors(IrInstruction terminator)
    {
        return terminator switch
        {
            IrBranchInstruction branch => branch.WhenTrue == branch.WhenFalse
                ? [branch.WhenTrue] : [branch.WhenTrue, branch.WhenFalse],
            IrGotoInstruction go => [go.Target],
            IrReturnInstruction => [],
            _ => []
        };
    }

    private static bool TryCreateParameterBindings(ManifestCallableTarget target, BoundMethodContracts contracts,
        ImmutableArray<FrontendVariableBinding> variables,
        out ImmutableDictionary<IrVarId, IrVarId> parameterBindings)
    {
        var canonicalParameters = contracts.Variables.Where(
            static variable => variable.Role == BoundContractVariableRole.Parameter).ToDictionary(static variable => variable.Ordinal);
        var bindings = ImmutableDictionary.CreateBuilder<IrVarId, IrVarId>();
        foreach (var binding in variables)
        {
            if (binding.Symbol is ILocalSymbol)
            {
                continue;
            }

            if (binding.Symbol is not IParameterSymbol parameter ||
                !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, target.Method) ||
                !canonicalParameters.TryGetValue(parameter.Ordinal, out var canonical))
            {
                parameterBindings = ImmutableDictionary<IrVarId, IrVarId>.Empty;
                return false;
            }
            bindings.Add(binding.Variable, canonical.Variable);
        }
        parameterBindings = bindings.ToImmutable();
        return true;
    }

    private bool ContainsOnlyContractStatements(ManifestCallableTarget target)
    {
        return target.VerifierDeclaration.Body != null &&
        target.VerifierDeclaration.Body.Statements.All(statement => IsContractStatement(target, statement));
    }

    private bool IsContractStatement(ManifestCallableTarget target, StatementSyntax statement)
    {
        if (statement is EmptyStatementSyntax)
        {
            return true;
        }

        return statement is ExpressionStatementSyntax expression && _contracts.GetClauseInventory(target.Method).Clauses.Any(clause =>
            clause.Invocation.Syntax.SyntaxTree == expression.SyntaxTree && clause.Invocation.Syntax.Span == expression.Expression.Span);
    }

    private static WorkerClaimEvidence ManifestEvidence(BoundContractEvidence evidence)
    {
        return evidence switch
        {
            BoundContractEvidence.CompilerBoundInvocation => WorkerClaimEvidence.DirectClause,
            BoundContractEvidence.Companion => WorkerClaimEvidence.CompanionClause,
            BoundContractEvidence.ClosedAttribute => WorkerClaimEvidence.ReturnAttribute,
            _ => WorkerClaimEvidence.Unspecified
        };
    }

    private static CompilerContractKind Kind(BoundContractKind value)
    {
        return value switch
        {
            BoundContractKind.Requires => CompilerContractKind.Requires,
            BoundContractKind.Ensures => CompilerContractKind.Ensures,
            BoundContractKind.Assume => CompilerContractKind.Assume,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    private static CompilerContractEvidence CompilerEvidence(BoundContractEvidence value)
    {
        return value switch
        {
            BoundContractEvidence.CompilerBoundInvocation => CompilerContractEvidence.CompilerBoundInvocation,
            BoundContractEvidence.ClosedAttribute => CompilerContractEvidence.ClosedAttribute,
            BoundContractEvidence.Companion => CompilerContractEvidence.Companion,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    private static CompilerVariableRole Role(BoundContractVariableRole value)
    {
        return value switch
        {
            BoundContractVariableRole.Receiver => CompilerVariableRole.Receiver,
            BoundContractVariableRole.Parameter => CompilerVariableRole.Parameter,
            BoundContractVariableRole.Result => CompilerVariableRole.Result,
            BoundContractVariableRole.PreState => CompilerVariableRole.PreState,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    private static WorkerClaimReason MapBindingFailure(ContractBindingFailure failure)
    {
        return failure switch
        {
            ContractBindingFailure.UnsupportedExpression => WorkerClaimReason.UnsupportedExpression,
            ContractBindingFailure.ResultOutsideEnsures or ContractBindingFailure.OldOutsideEnsures or ContractBindingFailure.NestedOld or
            ContractBindingFailure.InvalidIntrinsicSignature or ContractBindingFailure.NonBooleanCondition or
            ContractBindingFailure.InvalidClosedAttribute or
            ContractBindingFailure.InvalidClausePlacement => WorkerClaimReason.UnsupportedContract,
            _ => WorkerClaimReason.UnsupportedCallable
        };
    }

    private bool IsKnownPure(IMethodSymbol method)
    {
        return _apiSpecs.IsSideEffectFree(method);
    }

    private CompilerCallablePreparation Success(ManifestCallableTarget target,
        ImmutableArray<CompilerPreparedClause> clauses,
        ImmutableArray<CompilerCanonicalVariable> variables,
        CompilerPreparedBody? body)
    {
        return new(_factory, target.Entry,
            clauses.IsDefault ? [] : clauses, variables.IsDefault ? [] : variables, WorkerClaimReason.None, body);
    }

    private CompilerCallablePreparation Fail(ManifestCallableTarget target, WorkerClaimReason reason,
        ImmutableArray<CompilerPreparedClause> clauses = default,
        ImmutableArray<CompilerCanonicalVariable> variables = default)
    {
        return new(_factory, target.Entry,
            clauses.IsDefault ? [] : clauses, variables.IsDefault ? [] : variables, reason, null);
    }
}
