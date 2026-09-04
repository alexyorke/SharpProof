// This lowerer runs only in the build-time compiler collector.
namespace SharpProof.CompilerArtifact;

internal sealed class CompilerCallableLowerer
{
    private const int MaximumBodyBlocks = 64;
    private readonly IrFactory _factory;
    private readonly ContractBinder _contracts;
    private readonly ResolvedApiSpecTable _apiSpecs;
    private readonly CompilerRelationalSummaryProvider _summaries;

    internal CompilerImplementationIlAbstentionReason LastImplementationIlAbstention =>
        _summaries.LastImplementationIlAbstention;

    internal ImmutableArray<CompilerSummaryEvidenceAuthority> SummaryEvidenceAuthorities =>
        _summaries.SummaryEvidenceAuthorities;

    internal CompilerCallableLowerer(
        CSharpCompilation compilation,
        IrFactory factory,
        IEnumerable<string>? specificationPacks = null)
        : this(
            compilation,
            factory,
            CompilerSpecificationPackProvider.ResolveAuthority(specificationPacks))
    {
    }

    internal CompilerCallableLowerer(
        CSharpCompilation compilation,
        IrFactory factory,
        CompilerSpecificationPackAuthority specificationPackAuthority,
        CompilerSyntaxTreeSnapshot[]? capturedTrees = null)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        _contracts = new ContractBinder(compilation, factory);
        _apiSpecs = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        _summaries = new CompilerRelationalSummaryProvider(
            compilation,
            factory,
            _apiSpecs,
            specificationPackAuthority,
            capturedTrees);
    }

    internal CompilerCallablePreparation Prepare(ManifestCallableTarget target, CancellationToken cancellationToken = default)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));

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
            CompilerLoweringWireMappings.ToCompiler(clause.Kind), clause.Condition,
            CompilerLoweringWireMappings.ToCompiler(clause.Evidence),
            clause.Kind == BoundContractKind.Ensures ? target.Claims[claimOrdinal++].Entry.ClaimId : null,
            clause.Kind == BoundContractKind.Requires ? preconditions[preconditionOrdinal++].Id :
                clause.Kind == BoundContractKind.Assume ? userAssumptions[assumptionOrdinal++].Id : null))];
        ImmutableArray<CompilerCanonicalVariable> variables = [.. contracts.Variables.Select(
            variable => CreateVariable(variable, contracts))];
        var requiresBodyAdmission =
            !target.Claims.IsDefaultOrEmpty ||
            !contracts.Clauses.IsDefaultOrEmpty;
        if (!requiresBodyAdmission)
        {
            return Success(target, clauses, variables, body: null);
        }

        var preparedBody = PrepareBody(target, contracts, cancellationToken, out var failure);
        if (failure != WorkerClaimReason.None)
        {
            return Fail(target, failure, clauses, variables);
        }

        return Success(
            target,
            clauses,
            variables,
            target.Claims.IsDefaultOrEmpty ? null : preparedBody);
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

        var selected = new RoslynProgramLowerer(
            _factory,
            _summaries.IsAdmissiblePureCall).LowerSelected(
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
        var summaryCalls = ImmutableDictionary.CreateBuilder<IrInstructionId, CompilerPreparedSummaryCall>();
        foreach (var binding in selected.Calls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetCallIdentity(
                    binding.Value.TargetMethod,
                    out var callIdentity))
            {
                return Unsupported(out failure);
            }

            if (TryPrepareSpecCall(
                    binding.Key,
                    binding.Value,
                    callIdentity,
                    out var preparedSpec))
            {
                specCalls.Add(binding.Key.Id, preparedSpec!);
                continue;
            }

            if (TryPrepareSummaryCall(
                    binding.Key,
                    binding.Value,
                    callIdentity,
                    cancellationToken,
                    out var preparedSource))
            {
                summaryCalls.Add(binding.Key.Id, preparedSource!);
                continue;
            }

            return Unsupported(out failure);
        }
        if (specCalls.Count + summaryCalls.Count != selected.Calls.Count)
        {
            return Unsupported(out failure);
        }

        failure = WorkerClaimReason.None;
        return CompilerPreparedBody.ProgramBody(
            lowering.Program,
            parameterBindings,
            specCalls.ToImmutable(),
            summaryCalls.ToImmutable());
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
                claim.Entry.Evidence !=
                    CompilerLoweringWireMappings.ToWorkerEvidence(ensures[index].Evidence) ||
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
        var source = CompilerCallableProjections.GetVariableSource(
            variable,
            contracts);
        return new CompilerCanonicalVariable(
            CompilerLoweringWireMappings.ToCompiler(variable.Role), variable.Ordinal, variable.Variable,
            variable.CurrentStateVariable,
            IntegerInterval(source?.SpecialType),
            CompilerCallableProjections.GetModelLabel(variable));
    }

    private static CompilerIntegerInterval? IntegerInterval(SpecialType? type)
    {
        return type.HasValue && CSharpScalarSemantics.TryGetInteger(type.Value, out var semantics) && semantics.BitWidth <= 64
            ? new(semantics.Minimum, semantics.Maximum) : null;
    }

    private bool TryPrepareSpecCall(IrCallInstruction call, IInvocationOperation invocation, string callIdentity,
        out CompilerPreparedSpecCall? prepared)
    {
        prepared = null;
        if (!TryGetAdmissibleByValueCall(call, invocation))
        {
            return false;
        }

        var targetType = _factory.GetVariableInfo(call.Target!.Value).Type;
        if (!_apiSpecs.TryGet(invocation.TargetMethod, out var resolved) ||
            resolved.Template.Facets.Throws.Behavior != SpecThrowBehavior.DoesNotThrow ||
            !TryAdmitSpecCallEffects(invocation, call, resolved.Template, out var consumesMemoryHavoc) ||
            !resolved.Template.Result.HasValue ||
            !TryGetSpecResultType(invocation.Type, resolved.Template.Target.ResultType,
                targetType, out var resultType) ||
            targetType != resultType ||
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

    private bool TryPrepareSummaryCall(
        IrCallInstruction call,
        IInvocationOperation invocation,
        string callIdentity,
        CancellationToken cancellationToken,
        out CompilerPreparedSummaryCall? prepared)
    {
        prepared = null;
        if (!TryGetAdmissibleByValueCall(call, invocation) ||
            !_summaries.TryGet(
                invocation.TargetMethod,
                call.Member,
                cancellationToken,
                out var summary) ||
            summary == null)
        {
            return false;
        }

        if (!string.Equals(
                summary.Signature.Provenance.EvidenceCallIdentity,
                callIdentity,
                StringComparison.Ordinal))
        {
            return false;
        }

        IrSummaryInstantiation instantiated;
        try
        {
            instantiated = IrRelationalSummaryInstantiator.Instantiate(
                summary,
                call.Receiver,
                call.Arguments,
                call.Id.Value);
        }
        catch (ArgumentException)
        {
            return false;
        }

        prepared = new CompilerPreparedSummaryCall(
            call.Id,
            callIdentity,
            ToCompilerOrigin(summary.Signature.Provenance.Origin),
            instantiated.Result,
            [.. instantiated.FreshVariables.Skip(1)],
            instantiated.NormalRelation,
            summary.Signature.Provenance.EvidenceSha256,
            summary.Signature.Provenance.EvidenceIdentity,
            [.. summary.DependencyProvenance.Select(static provenance =>
                new CompilerPreparedSummaryEvidence(
                    ToCompilerOrigin(provenance.Origin),
                    provenance.EvidenceCallIdentity,
                    provenance.EvidenceSha256,
                    provenance.EvidenceIdentity))]);
        return true;
    }

    private static bool TryGetAdmissibleByValueCall(
        IrCallInstruction call,
        IInvocationOperation invocation)
    {
        return call.Target.HasValue &&
            RoslynProgramLowerer.IsDirectInvocation(invocation) &&
            !invocation.TargetMethod.Parameters.Any(
                static parameter => parameter.RefKind != RefKind.None);
    }

    private static CompilerSummaryOrigin ToCompilerOrigin(
        IrSummaryOrigin origin)
    {
        return origin switch
        {
            IrSummaryOrigin.Source => CompilerSummaryOrigin.Source,
            IrSummaryOrigin.ImplementationIl =>
                CompilerSummaryOrigin.ImplementationIl,
            IrSummaryOrigin.SpecificationPack =>
                CompilerSummaryOrigin.SpecificationPack,
            _ => throw new InvalidOperationException(
                "A relational summary has an unsupported origin.")
        };
    }

    private static bool TryGetCallIdentity(IMethodSymbol method, out string identity)
    {
        var symbol = ResolvedApiSpecTable.NormalizeSymbol(method);
        identity = symbol?.GetDocumentationCommentId() ?? string.Empty;
        // Compiler artifacts bound identity fields to 512 characters. Reject
        // an otherwise legal Roslyn documentation ID here so a long symbol
        // becomes a scoped unsupported call instead of failing artifact
        // construction after partially lowering the body.
        return identity is { Length: > 0 and <= 512 } &&
            identity.All(static character => !char.IsControl(character));
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

    private bool TryGetSpecResultType(ITypeSymbol? sourceType, IrTypeKind? specType,
        IrTypeId loweredResultType, out IrTypeId resultType)
    {
        switch (specType)
        {
            case IrTypeKind.Boolean when sourceType?.SpecialType == SpecialType.System_Boolean:
                resultType = _factory.BooleanType;
                return true;
            case IrTypeKind.Integer when CSharpScalarSemantics.IsSupportedInteger(
                sourceType?.SpecialType ?? SpecialType.None):
                resultType = _factory.IntegerType;
                return true;
            case IrTypeKind.String when sourceType?.SpecialType == SpecialType.System_String:
                resultType = _factory.StringType;
                return true;
            case IrTypeKind.Sequence when sourceType is IArrayTypeSymbol
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
        var declaration = target.VerifierDeclaration;
        if (declaration.ExpressionBody is { } expressionBody)
        {
            return expressionBody.Expression.SpanStart;
        }

        if (declaration.Body is not { } body)
        {
            return null;
        }

        foreach (var statement in body.Statements)
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
            if (!block.IsReachable)
            {
                continue;
            }

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
        return IrInstructionFacts.TryGetSuccessors(terminator) ?? [];
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

            if (binding.Symbol is ITypeSymbol instanceType &&
                !target.Method.IsStatic &&
                SymbolEqualityComparer.Default.Equals(
                    instanceType, target.Method.ContainingType))
            {
                var receiver = contracts.Variables.FirstOrDefault(
                    static variable => variable.Role == BoundContractVariableRole.Receiver);
                if (receiver != null)
                {
                    bindings.Add(binding.Variable, receiver.Variable);
                    continue;
                }
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
        var declaration = target.VerifierDeclaration;
        return declaration.Body is { } body
            ? body.Statements.All(statement =>
                IsContractStatement(target, statement))
            : declaration.ExpressionBody is { Expression: { } expression } &&
                IsContractExpression(target, expression);
    }

    private bool IsContractStatement(ManifestCallableTarget target, StatementSyntax statement)
    {
        if (statement is EmptyStatementSyntax)
        {
            return true;
        }

        return statement is ExpressionStatementSyntax expression &&
            IsContractExpression(target, expression.Expression);
    }

    private bool IsContractExpression(
        ManifestCallableTarget target,
        ExpressionSyntax expression)
    {
        return _contracts.GetClauseInventory(target.Method).Clauses.Any(
            clause =>
                clause.Invocation.Syntax.SyntaxTree ==
                    expression.SyntaxTree &&
                clause.Invocation.Syntax.Span == expression.Span);
    }

    private static WorkerClaimReason MapBindingFailure(ContractBindingFailure failure)
    {
        try
        {
            return CompilerLoweringWireMappings.ToWorkerFailure(failure);
        }
        catch (ArgumentOutOfRangeException)
        {
            return WorkerClaimReason.UnsupportedCallable;
        }
    }

    private CompilerCallablePreparation Success(ManifestCallableTarget target,
        ImmutableArray<CompilerPreparedClause> clauses,
        ImmutableArray<CompilerCanonicalVariable> variables,
        CompilerPreparedBody? body)
    {
        return new(_factory, target.Entry,
            clauses.IsDefault ? [] : clauses, variables.IsDefault ? [] : variables,
            CompilerCallableArtifactReasonCatalog.SuccessReason, body);
    }

    private CompilerCallablePreparation Fail(ManifestCallableTarget target, WorkerClaimReason reason,
        ImmutableArray<CompilerPreparedClause> clauses = default,
        ImmutableArray<CompilerCanonicalVariable> variables = default)
    {
        if (!CompilerCallableArtifactReasonCatalog.IsFailureReason(reason))
        {
            throw new InvalidOperationException(
                "The compiler callable failure reason is not producer-owned.");
        }

        return new(_factory, target.Entry,
            clauses.IsDefault ? [] : clauses, variables.IsDefault ? [] : variables, reason, null);
    }
}
