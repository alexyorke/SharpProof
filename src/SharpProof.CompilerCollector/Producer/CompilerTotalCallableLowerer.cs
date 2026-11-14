namespace SharpProof.CompilerCollector;

// Independent admission for the same discovered target. A legacy failure is
// preserved and does not suppress exact typed candidate evidence.
internal static class CompilerTotalCallableLowerer
{
    internal static CompilerTotalEntryPreparation? PrepareEntry(CSharpCompilation compilation,
        ManifestCallableTarget target, ImmutableArray<CompilerSyntaxTreeSnapshot> capturedTrees, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        RepositoryParameterGuard.ThrowIfMissing(capturedTrees.IsDefault, nameof(capturedTrees));
        if (target.Declaration is not MethodDeclarationSyntax || target.SemanticModel == null ||
            target.Method.Parameters.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        RequireCapturedTrees(compilation, capturedTrees);
        var documents = ImmutableDictionaryCapture.Create(compilation.SyntaxTrees.Select((tree, ordinal) => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((Tree: tree, Path: capturedTrees[ordinal].Path)))))
            , static item => item.Tree, static item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(), target.Method, tree => documents[tree]);
        context.ModelReceiver();
        if (target.Method.IsAsync)
        {
            // An owned scalar entry for resource projection only. This does not
            // admit an async functional body or bypass any source contract.
            var runtimeAssumptions = CompilerTaskRuntimePremiseCollector.VerifiedManifestAssumptionIds(target.Method,
                target.Entry.CallableId, target.Entry.Assumptions, cancellationToken);
            if (ResourceForkJoinPlan.TryCreate(target.Method, compilation, cancellationToken) is not { ConfiguredAwait: true } ||
                !target.Claims.IsEmpty || target.EffectClaims.IsEmpty ||
                target.EffectClaims.Any(static claim => !claim.HasValidConstraint || claim.Entry.EffectContractKind != WorkerEffectContractKind.ResourceUpperBound) ||
                runtimeAssumptions is null ||
                target.Entry.Assumptions.Any(assumption => assumption.Kind != WorkerAssumptionKind.TrustedBoundary &&
                    !(assumption.Kind == WorkerAssumptionKind.UserAssume && runtimeAssumptions.Contains(assumption.Id))))
            { return null; }
            return new(target.Entry.CallableId, context.Factory,
                [.. context.Inputs.Select(static parameter => new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))], []);
        }
        var binding = new TotalContractBinder(compilation, context.Factory).BindTotalRequires(context,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var preconditions = target.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).ToImmutableArray();
        if (!binding.IsSuccess || binding.Clauses.Length > CompilerArtifactLimits.MaximumInstructions ||
            binding.Clauses.Length != preconditions.Length)
        { return null; }
        return new(target.Entry.CallableId, context.Factory,
            [.. context.Inputs.Select(parameter => new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))],
            [.. binding.Clauses.Select((clause, ordinal) => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (new CompilerTotalClause(CompilerContractKind.Requires,
                clause.Value, clause.SafeCondition, clause.SourceOperation, null, preconditions[ordinal].Id)))))])
        { HasReceiver = context.Receiver != null };
    }

    internal static CompilerTotalCallablePreparation? Prepare(CSharpCompilation compilation,
        ManifestCallableTarget target, ImmutableArray<CompilerSyntaxTreeSnapshot> capturedTrees,
        ImmutableArray<CompilerReferenceSnapshot>? capturedReferences, CompilerSpecificationPackConfiguration specificationPackAuthority,
        CancellationToken cancellationToken, Action<WorkerDiagnosticContext>? reportDiagnostic = null,
        CompilerContractExports? contractExports = null)
    {
        specificationPackAuthority = RepositoryParameterGuard.NotNull(specificationPackAuthority, nameof(specificationPackAuthority));
        cancellationToken.ThrowIfCancellationRequested();
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        RepositoryParameterGuard.ThrowIfMissing(capturedTrees.IsDefault, nameof(capturedTrees));
        RepositoryParameterGuard.ThrowIfMissing(capturedReferences is { IsDefault: true }, nameof(capturedReferences));
        if (!TotalSourceCallSession.IsBodyDeclaration(target.Declaration) || target.SemanticModel == null ||
            target.Method.Parameters.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        var declaration = target.Declaration!;
        var autoAccessor = TotalBodyLowering.AutoAccessor(declaration);
        RequireCapturedTrees(compilation, capturedTrees);
        var documents = ImmutableDictionaryCapture.Create(compilation.SyntaxTrees.Select((tree, ordinal) => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((Tree: tree, Path: capturedTrees[ordinal].Path)))))
            , static item => item.Tree, static item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(), target.Method, tree => documents[tree])
        {
            CaptureCallFrames = true,
            CaptureResourceLoopEntries = target.EffectClaims.Any(claim => claim.HasValidConstraint && claim.Entry.EffectContractKind == WorkerEffectContractKind.ResourceUpperBound)
        };
        context.ModelReceiver();
        // An auto-property accessor has no body to carry contract clauses.
        var binding = autoAccessor != null ? new TotalContractBindingResult([], ContractBindingFailure.None, context.Origin)
            : new TotalContractBinder(compilation, context.Factory).BindTotal(context,
                cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!binding.IsSuccess || binding.Clauses.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        var ensures = binding.Clauses.Where(clause => clause.Kind == BoundContractKind.Ensures).ToImmutableArray();
        // A postcondition on an overridable method is a contract for every
        // override; only effect claims, which describe this body, are verified.
        if (ensures.Length != 0 && (target.Method.IsVirtual || target.Method.IsOverride))
        { return null; }
        var requires = binding.Clauses.Where(clause => clause.Kind == BoundContractKind.Requires).ToImmutableArray();
        var preconditions = target.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).ToImmutableArray();
        var runtimeAssumptionIds = CompilerTaskRuntimePremiseCollector.VerifiedManifestAssumptionIds(target.Method,
            target.Entry.CallableId, target.Entry.Assumptions, cancellationToken) ?? ImmutableHashSet<string>.Empty;
        var assumptions = target.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume && !runtimeAssumptionIds.Contains(assumption.Id)).ToImmutableArray();
        if (ensures.Length != target.Claims.Length || requires.Length != preconditions.Length ||
            binding.Clauses.Count(clause => clause.Kind == BoundContractKind.Assume) != assumptions.Length)
        { return null; }
        for (var ordinal = 0; ordinal < ensures.Length; ordinal++)
        {
            var claim = target.Claims[ordinal];
            var span = context.Factory.GetOperationInfo(ensures[ordinal].SourceOperation).SourceSpan;
            var syntax = claim.SourceOperation?.Syntax ?? claim.SourceAttribute?.ApplicationSyntaxReference?.GetSyntax(cancellationToken);
            if (claim.Entry.Kind != WorkerClaimKind.Postcondition ||
                claim.Entry.Evidence != CompilerLoweringWireMappings.ToWorkerEvidence(ensures[ordinal].Evidence) ||
                claim.Entry.Ordinal != ordinal || syntax == null || span == null ||
                syntax.SpanStart != span.Start || syntax.Span.Length != span.Length ||
                !documents.TryGetValue(syntax.SyntaxTree, out var clauseDocument) || clauseDocument != span.Document)
            { return null; }
        }
        ControlFlowGraph? graph = null;
        FrontendProgramLoweringResult? lowering;
        if (autoAccessor != null)
        { lowering = RoslynProgramLowering.LowerAutoAccessor(context, autoAccessor); }
        else
        {
            graph = TotalSourceCallSession.CreateGraph(declaration, target.Method, target.SemanticModel, cancellationToken);
            lowering = graph == null ? null : LowerBody(compilation, graph, context, capturedReferences, specificationPackAuthority,
                cancellationToken, opaqueCalls: true, metadataCallees: true, contractExports: contractExports);
        }
        if (lowering == null)
        { return null; }
        cancellationToken.ThrowIfCancellationRequested();
        if (reportDiagnostic != null && !lowering.Abstentions.IsDefaultOrEmpty)
        {
            var abstention = lowering.Abstentions[0];
            var source = context.Factory.GetOperationInfo(abstention.Operation).SourceSpan;
            var tree = source == null ? null : documents.FirstOrDefault(pair => pair.Value == source.Document).Key;
            if (source != null && tree != null)
            {
                var location = Location.Create(tree, new Microsoft.CodeAnalysis.Text.TextSpan(source.Start, source.Length));
                reportDiagnostic(new WorkerDiagnosticContext { Code = abstention.Reason.ToString(), Location = CompilerSourceLocationProjection.Create(location) });
            }
        }
        var program = lowering.Program;
        var isBodyAbstraction = false;
        if (!lowering.IsExact || lowering.Program.Blocks.Length > CompilerArtifactLimits.MaximumInstructions)
        {
            if (lowering.ConstructionLimitExceeded || graph == null || graph.Blocks.Length > CompilerArtifactLimits.MaximumInstructions ||
                graph.Blocks.Sum(block => block.Operations.Length) > CompilerArtifactLimits.MaximumInstructions ||
                binding.Clauses.Any(clause => clause.Kind == BoundContractKind.Assume) ||
                context.Receiver is { } receiver && binding.Clauses.Any(clause => IrTraversal.CollectVariables([clause.Value, clause.SafeCondition])
                    .Any(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable == receiver.Entry || variable == receiver.Current || variable == receiver.PreState))) ||
                context.Parameters.Any(parameter => !Primitive(parameter.Entry)) ||
                context.Result is { } resultVariable && !Primitive(resultVariable) ||
                context.Parameters.Length * 2 + 2 > CompilerArtifactLimits.MaximumInstructions)
            { return null; }
            var declarationOperation = target.SemanticModel.GetOperation(declaration, cancellationToken)!;
            var builder = new IrProgramBuilder(context.Factory);
            var block = builder.CreateBlock();
            var site = context.Site(declarationOperation);
            foreach (var parameter in context.Parameters)
            {
                builder.Assign(block, site, parameter.Current, context.Factory.Variable(parameter.Entry));
                builder.Assign(block, site, parameter.PreState, context.Factory.Variable(parameter.Entry));
            }
            var havocVariables = ImmutableArray.CreateBuilder<IrVarId>();
            havocVariables.AddRange(context.Parameters.Select(parameter => parameter.Current));
            if (context.Result is { } resultId)
            { havocVariables.Add(resultId); }
            if (havocVariables.Count == 0)
            { havocVariables.Add(context.Factory.CreateVariable("abstract-body", context.Factory.BooleanType)); }
            builder.HavocSnapshot(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, havocVariables.ToImmutable());
            builder.Return(block, site, context.Result is { } returned ? context.Factory.Variable(returned) : null);
            program = builder.Build();
            isBodyAbstraction = true;

            bool Primitive(IrVarId variable)
            { RepositoryParameterGuard.ThrowIfInvalid(variable.IsDefault, nameof(variable)); return context.Factory.GetTypeInfo(context.Factory.GetVariableInfo(variable).Type).Kind is IrTypeKind.Boolean or IrTypeKind.Integer; }
        }
        // Compiler-owned producers satisfy the independently tested artifact bound.
        foreach (var _ in program.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        // Initialization a source callee reaches is admitted only when it
        // stores constants; the declaring type's may also construct fresh
        // values, which only allocation-insensitive claims accept.
        var entryInitialization = lowering.SourceInitializationEffectsComplete
            ? EntryInitialization.ClassifyEntry(compilation, target.Method.ContainingType, cancellationToken)
            : InitializationEffect.Effectful;
        // A modeled struct value is built as an IR allocation, though the CLR
        // does not allocate it on the heap. Only allocation-insensitive claims
        // accept such a body, as they accept fresh-value initialization.
        if (entryInitialization == InitializationEffect.None && ConstructsValueStructs(context.Factory, program))
        { entryInitialization = InitializationEffect.FreshValues; }
        var claimOrdinal = 0;
        var assumptionOrdinal = 0;
        var userAssumptionOrdinal = 0;
        ImmutableArray<TotalInputBinding> inputs = isBodyAbstraction
            ? [.. context.Parameters.Select(parameter => parameter.Input)] : context.Inputs;
        return new(target.Entry.CallableId, program,
            [.. inputs.Select(parameter => new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))],
            context.Result,
            [.. binding.Clauses.Select(clause => new CompilerTotalClause(CompilerLoweringWireMappings.ToCompiler(clause.Kind),
                clause.Value, clause.SafeCondition, clause.SourceOperation,
                clause.Kind == BoundContractKind.Ensures ? target.Claims[claimOrdinal++].Entry.ClaimId : null,
                clause.Kind == BoundContractKind.Requires ? preconditions[assumptionOrdinal++].Id :
                    clause.Kind == BoundContractKind.Assume ? assumptions[userAssumptionOrdinal++].Id : null))], isBodyAbstraction)
        {
            HasReceiver = inputs.Length != context.Parameters.Length,
            DisjointInputs = isBodyAbstraction ? [] : CompilerDisjointInputAnalysis.Collect(target.Method, cancellationToken),
            EffectsCompleteAtEntry = entryInitialization == InitializationEffect.None,
            EntryConstructsFreshValues = entryInitialization == InitializationEffect.FreshValues,
            ValidEffectClaimIds = [.. target.EffectClaims.Where(claim => claim.HasValidConstraint)
                .Select(claim => claim.Evidence.ClaimId).OrderBy(id => id, StringComparer.Ordinal)],
            ExceptionConstraints = ExceptionConstraints(compilation, target, cancellationToken),
            CallPreconditions = isBodyAbstraction ? [] : CreateCallPreconditions(lowering)
        };
    }

    // Captured snapshots are indexed by compilation tree ordinal; a mismatch
    // is a caller contract violation, not an admission decision.
    private static void RequireCapturedTrees(CSharpCompilation compilation, ImmutableArray<CompilerSyntaxTreeSnapshot> capturedTrees)
    {
        if (capturedTrees.Length != compilation.SyntaxTrees.Length)
        {
            throw new ArgumentException(
                "Captured source snapshots must match the compilation syntax trees.",
                nameof(capturedTrees));
        }
    }

    // Rebuild the bounded source census inside this batch. Callers cannot
    // provide a forged owner or bypass the all-tree contract-binding guard.
    internal static CompilerShadowPreparationBatch PrepareShadowCallers(CSharpCompilation compilation,
        WorkerFeatureSet features, ImmutableArray<CompilerSyntaxTreeSnapshot> capturedTrees,
        ImmutableArray<CompilerReferenceSnapshot>? capturedReferences, CompilerSpecificationPackConfiguration specificationPackAuthority,
        CancellationToken cancellationToken, bool enableMetadataRequires = false)
    {
        specificationPackAuthority = RepositoryParameterGuard.NotNull(specificationPackAuthority, nameof(specificationPackAuthority));
        cancellationToken.ThrowIfCancellationRequested();
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        RepositoryParameterGuard.ThrowIfMissing(capturedTrees.IsDefault, nameof(capturedTrees));
        RepositoryParameterGuard.ThrowIfMissing(capturedReferences is { IsDefault: true }, nameof(capturedReferences));
        if (enableMetadataRequires && capturedReferences is null)
        { return new([], [new(0, 0, 0, "MissingMetadataOwnership")]); }
        if (compilation.References.Any(static reference => reference is CompilationReference))
        { return new([], [new(0, 0, 0, "UnsupportedSourceReference")]); }
        var trees = compilation.SyntaxTrees;
        if (trees.Length > CompilerArtifactLimits.MaximumInstructions)
        { return new([], [new(0, 0, 0, "InventoryBudget")]); }
        if (trees.Length != capturedTrees.Length)
        { return new([], [new(0, 0, 0, "SourceSnapshotMismatch")]); }
        // Hashing materializes UTF-8; syntax-node limits do not bound comments.
        const int maximumTreeCharacters = 4_194_304;
        var remainingCharacters = 16_777_216;
        for (var ordinal = 0; ordinal < trees.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = trees[ordinal].GetText(cancellationToken);
            if (text.Length > maximumTreeCharacters || text.Length > remainingCharacters)
            { return new([], [new(ordinal, 0, 0, "SourceSnapshotBudget")]); }
            remainingCharacters -= text.Length;
            if (capturedTrees[ordinal].Sha256 != CompilerCompilationCapture.ComputeTextSha256(text))
            { return new([], [new(ordinal, 0, 0, "SourceSnapshotMismatch")]); }
        }
        var builder = new ClaimManifestBuilder(compilation, features, cancellationToken);
        // Inventory guards every source tree before any selected-body binding.
        var inventory = builder.BuildPotentialCallShadow(allowReferenceOwners: enableMetadataRequires);
        if (inventory.Owners.IsEmpty)
        { return new([], inventory.Gaps); }
        var discovery = builder.Build();
        var gaps = inventory.Gaps.ToBuilder();
        var prepared = ImmutableArray.CreateBuilder<CompilerShadowPreparation>();
        var published = discovery.Manifest.Callables.Select(static entry => entry.CallableId)
            .ToImmutableHashSet(StringComparer.Ordinal);
        // Use producer-owned paths instead of caller-supplied reporting metadata.
        var authoritativeTrees = CompilerCompilationCapture.CaptureTrees(compilation, cancellationToken).ToImmutableArray();
        var initializationFree = CreateShadowInitializationPredicate(compilation, cancellationToken);
        foreach (var owner in inventory.Owners.OrderBy(static owner => owner.CallableId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (published.Contains(owner.CallableId) || !owner.DiscoveryComplete)
            { continue; }
            var treeOrdinal = trees.IndexOf(owner.Declaration.SyntaxTree);
            var body = PrepareShadow(compilation, owner, authoritativeTrees, capturedReferences,
                specificationPackAuthority, initializationFree, cancellationToken, out var reason, enableMetadataRequires);
            if (body == null)
            { gaps.Add(new(treeOrdinal, owner.Declaration.SpanStart, owner.Declaration.Span.Length, reason)); }
            else if (!body.CallPreconditions.IsEmpty)
            { prepared.Add(new(body)); }
            else if (!owner.Calls.IsEmpty)
            { gaps.Add(new(treeOrdinal, owner.Declaration.SpanStart, owner.Declaration.Span.Length, "NoNativeCallPreconditions")); }
        }
        return new(prepared.ToImmutable(), gaps.ToImmutable());
    }

    private static CompilerTotalCallablePreparation? PrepareShadow(CSharpCompilation compilation,
        CompilerPotentialCallOwner owner, ImmutableArray<CompilerSyntaxTreeSnapshot> capturedTrees,
        ImmutableArray<CompilerReferenceSnapshot>? capturedReferences, CompilerSpecificationPackConfiguration specificationPackAuthority,
        Func<INamedTypeSymbol, bool> initializationFree, CancellationToken cancellationToken, out string reason, bool enableMetadataRequires = false)
    {
        RepositoryParameterGuard.ThrowIfInvalid(capturedTrees.IsDefault, nameof(capturedTrees));
        cancellationToken.ThrowIfCancellationRequested();
        reason = "UnsupportedBody";
        if (owner.Method.Parameters.Length > CompilerArtifactLimits.MaximumInstructions ||
            !ReferenceEquals(owner.SemanticModel.Compilation, compilation) ||
            !ReferenceEquals(owner.SemanticModel.SyntaxTree, owner.Declaration.SyntaxTree) ||
            !SymbolEqualityComparer.Default.Equals(
                owner.SemanticModel.GetDeclaredSymbol(owner.Declaration, cancellationToken), owner.Method))
        { return null; }
        if (!initializationFree(owner.Method.ContainingType))
        { reason = "UnsupportedEntryInitialization"; return null; }
        var documents = ImmutableDictionaryCapture.Create(compilation.SyntaxTrees.Select((tree, ordinal) => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((Tree: tree, Path: capturedTrees[ordinal].Path)))))
            , static item => item.Tree, static item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(), owner.Method, tree => documents[tree]) { CaptureShadowCallAncestry = true };
        var binding = new TotalContractBinder(compilation, context.Factory).BindTotal(context,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!binding.IsSuccess || !binding.Clauses.IsEmpty)
        { reason = "UnsupportedOwnContracts"; return null; }
        // The source census retains the compiler-owned method body after
        // checking its declaration and null parent. Reuse that rooted body
        // instead of asking the semantic model to bind the declaration again.
        var graph = ControlFlowGraph.Create(owner.BodyOperation, cancellationToken);
        var lowering = LowerBody(compilation, graph, context, capturedReferences, specificationPackAuthority, cancellationToken, initializationFree, enableMetadataRequires);
        cancellationToken.ThrowIfCancellationRequested();
        if (!lowering.IsExact || lowering.Program.Blocks.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        // CompilerFrontendInstructionBoundContractTests enforces the independent limits.
        // Exact shadow bodies satisfy the frontend bound; retain per-block cancellation.
        foreach (var _ in lowering.Program.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        return new(owner.CallableId, lowering.Program,
            [.. context.Parameters.Select(parameter => new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))],
            context.Result, [], false)
        {
            CallPreconditions = CreateCallPreconditions(lowering)
        };
    }

    private static ImmutableArray<CompilerTotalCallPrecondition> CreateCallPreconditions(
        FrontendProgramLoweringResult lowering)
    {
        return [.. lowering.CallPreconditions.OrderBy(pair => pair.Key.Id.Value)
            .Select(pair => new CompilerTotalCallPrecondition(pair.Key.Id, pair.Value.CalleeIdentity,
                pair.Value.ClauseOrdinal, pair.Value.ClauseSite, pair.Value.Value, pair.Value.Safe,
                MetadataOrigin(pair.Value.MetadataClause))
            {
                Ancestry = [.. pair.Value.Ancestry.Select(hop => new CompilerShadowCallHop(hop.CallerIdentity, hop.CalleeIdentity, hop.Site))],
                CallerDeclared = pair.Value.CallerDeclared,
                ExportedClause = pair.Value.ExportedClause is { } exported
                    ? exported.Origin as CompilerExportedClauseOrigin ?? throw new InvalidOperationException("An exported row has no export origin.")
                    : null
            })];
    }

    private static FrontendProgramLoweringResult LowerBody(CSharpCompilation compilation,
        ControlFlowGraph graph, TotalLoweringContext context, ImmutableArray<CompilerReferenceSnapshot>? capturedReferences,
        CompilerSpecificationPackConfiguration specificationPackAuthority, CancellationToken cancellationToken,
        Func<INamedTypeSymbol, bool>? initializationFree = null, bool enableMetadataRequires = false, bool opaqueCalls = false,
        bool metadataCallees = false, CompilerContractExports? contractExports = null)
    {
        var specificationPacks = new CompilerSpecificationPackProvider(context.Factory, specificationPackAuthority);
        var images = new CompilerTotalIlBodyProvider(compilation, capturedReferences);
        return TotalBodyLowering.Lower(compilation, graph, context, cancellationToken,
            images.Resolve, specificationPacks.ResolveTotal,
            initializationFree, enableMetadataRequires ? (frame, body) => body is null ? throw new System.ArgumentNullException(nameof(body)) : frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (PrepareMetadataRequires(compilation, frame, body, cancellationToken)) : null,
            opaqueCalls, prepareMetadataCallee: metadataCallees
                ? frame => PrepareMetadataCallee(compilation, images, RepositoryParameterGuard.NotNull(frame, nameof(frame)),
                    contractExports ?? CompilerContractExports.Empty, cancellationToken) : null);
    }

    internal static bool ConstructsValueStructs(IrFactory factory, IrProgram program)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        program = RepositoryParameterGuard.NotNull(program, nameof(program));
        return program.Blocks.SelectMany(static block => block.Instructions).OfType<IrAllocationInstruction>()
            .Any(allocation => factory.IsValueStructType(allocation.AllocatedType));
    }

    private static Func<INamedTypeSymbol, bool> CreateShadowInitializationPredicate(
        CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        var types = ImmutableDictionary.CreateBuilder<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);
        bool? moduleSafe = null;
        return type =>
        {
            type = RepositoryParameterGuard.NotNull(type, nameof(type));
            cancellationToken.ThrowIfCancellationRequested();
            if (moduleSafe == false)
            { return false; }
            if (!types.TryGetValue(type, out var typeSafe))
            {
                typeSafe = EntryInitialization.HasNoEffectTypeInitialization(compilation, type, cancellationToken);
                types.AddUnique(type, typeSafe);
            }
            if (!typeSafe)
            { return false; }
            moduleSafe ??= EntryInitialization.ModuleInitializers(compilation, cancellationToken).IsEmpty;
            cancellationToken.ThrowIfCancellationRequested();
            return moduleSafe.Value;
        };
    }

    internal static bool HasNoEffectEntryInitialization(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return EntryInitialization.FindEffectGap(compilation, type, cancellationToken) == EntryInitializationGap.None;
    }

    // Exception proof admission is separate from entry purity: a constant
    // static write or ordinary object allocation need not throw. Unproved
    // initializer code cannot supply a complete exception constraint row.
    private sealed class ExceptionEntryInitialization(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        private readonly ImmutableDictionary<INamedTypeSymbol, bool>.Builder _checked = (compilation is null ? throw new System.ArgumentNullException(nameof(compilation)) : ImmutableDictionary.CreateBuilder<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default));
        private readonly ImmutableHashSet<INamedTypeSymbol>.Builder _active = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        private int _remaining = CompilerArtifactLimits.MaximumInstructions;

        internal bool NoFault(INamedTypeSymbol type)
        { type = RepositoryParameterGuard.NotNull(type, nameof(type)); return NoFaultType(type) && NoFaultModule(); }

        private bool Spend()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return --_remaining >= 0;
        }

        private bool NoFaultType(INamedTypeSymbol type)
        {
            if (!Spend())
            { return false; }
            if (_checked.TryGetValue(type, out var known))
            { return known; }
            if (type.DeclaringSyntaxReferences.Length == 0 || _active.Contains(type))
            { return false; }
            _active.Add(type);
            var safe = false;
            try
            {
                foreach (var member in type.GetMembers())
                {
                    if (!Spend())
                    { return false; }
                    if (member is IMethodSymbol { MethodKind: MethodKind.StaticConstructor, IsImplicitlyDeclared: false } constructor &&
                        !NoFaultBody(constructor, type))
                    { return false; }
                    if (!member.IsStatic)
                    { continue; }
                    var initializedType = member switch
                    {
                        IFieldSymbol { IsConst: false } field => field.Type,
                        IPropertySymbol property => property.Type,
                        IEventSymbol eventSymbol => eventSymbol.Type,
                        _ => null
                    };
                    if (initializedType == null)
                    { continue; }
                    foreach (var reference in member.DeclaringSyntaxReferences)
                    {
                        if (!Spend())
                        { return false; }
                        var value = reference.GetSyntax(cancellationToken) switch
                        {
                            VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                            PropertyDeclarationSyntax property => property.Initializer?.Value,
                            _ => null
                        };
                        if (value == null)
                        { continue; }
                        var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, reference.SyntaxTree);
                        // The literal can be constant while the implicit conversion
                        // into its declared field/property/event invokes user code.
                        var conversion = model.ClassifyConversion(value, initializedType);
                        if (!conversion.Exists || !conversion.IsImplicit || conversion.IsUserDefined ||
                            !NoFaultValue(model.GetOperation(value, cancellationToken)))
                        { return false; }
                    }
                }
                safe = true;
                return true;
            }
            finally
            {
                _active.Remove(type);
                _checked[type] = safe;
            }
        }

        private bool NoFaultModule()
        {
            foreach (var method in EntryInitialization.ModuleInitializers(compilation, cancellationToken))
            {
                if (!Spend() || !NoFaultType(method.ContainingType) || !NoFaultBody(method, method.ContainingType))
                { return false; }
            }
            return true;
        }

        private bool NoFaultBody(IMethodSymbol method, INamedTypeSymbol initializedOwner)
        {
            if (method.DeclaringSyntaxReferences.Length == 0)
            { return false; }
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (!Spend())
                { return false; }
                var declaration = reference.GetSyntax(cancellationToken);
                var (body, expression) = declaration switch
                {
                    ConstructorDeclarationSyntax constructor => (constructor.Body, constructor.ExpressionBody?.Expression),
                    MethodDeclarationSyntax ordinary => (ordinary.Body, ordinary.ExpressionBody?.Expression),
                    _ => ((BlockSyntax?)null, (ExpressionSyntax?)null)
                };
                var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, reference.SyntaxTree);
                if (body != null ? !NoFaultStatement(body, model, initializedOwner) :
                    expression == null || !NoFaultStore(model.GetOperation(expression, cancellationToken), initializedOwner))
                { return false; }
            }
            return true;
        }

        private bool NoFaultStatement(StatementSyntax statement, SemanticModel model, INamedTypeSymbol initializedOwner)
        {
            if (!Spend())
            { return false; }
            return statement switch
            {
                BlockSyntax block => block.Statements.All(child => NoFaultStatement(child, model, initializedOwner)),
                EmptyStatementSyntax or ReturnStatementSyntax { Expression: null } => true,
                ExpressionStatementSyntax expression => NoFaultStore(model.GetOperation(expression.Expression, cancellationToken), initializedOwner),
                _ => false
            };
        }

        private bool NoFaultStore(IOperation? operation, INamedTypeSymbol initializedOwner)
        {
            return Spend() && operation is ISimpleAssignmentOperation
            {
                IsRef: false, Target: IFieldReferenceOperation { Field.IsStatic: true, Instance: null } target
            } assignment && NoFaultValue(assignment.Value) &&
                // A write into the type currently being initialized does not
                // start that initializer again; a different type is a dependency.
                (SymbolEqualityComparer.Default.Equals(target.Field.ContainingType, initializedOwner) ||
                    NoFaultType(target.Field.ContainingType));
        }

        private bool NoFaultValue(IOperation? operation)
        {
            if (!Spend() || operation == null)
            { return false; }
            if (operation is IConversionOperation conversion)
            {
                if (conversion.OperatorMethod != null)
                { return false; }
                if (conversion.ConstantValue.HasValue)
                { return NoFaultConstant(conversion); }
                return (conversion.Conversion.IsIdentity || conversion.IsImplicit && conversion.Conversion.IsReference ||
                    CSharpOperationSemantics.IsScalarBoxing(conversion)) && NoFaultValue(conversion.Operand);
            }
            if (operation.ConstantValue.HasValue)
            { return NoFaultConstant(operation); }
            return operation is IObjectCreationOperation creation && CSharpOperationSemantics.IsCoreObjectCreation(creation) ||
                operation is IParenthesizedOperation parentheses && NoFaultValue(parentheses.Operand);
        }

        private static bool NoFaultConstant(IOperation operation)
        {
            return CSharpOperationSemantics.IsScalar(operation.Type) || operation.Type?.SpecialType == SpecialType.System_String ||
                operation.ConstantValue.Value == null && (operation.Type == null || operation.Type.IsReferenceType);
        }
    }
    private static ImmutableArray<CompilerTotalExceptionConstraint> ExceptionConstraints(CSharpCompilation compilation,
        ManifestCallableTarget target, CancellationToken cancellationToken)
    {
        bool? initializationSafe = null;
        var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        return CompilerExceptionConstraintEvidence.Project(core, EligibleClaims());

        IEnumerable<CompilerEffectClaimArtifact> EligibleClaims()
        {
            foreach (var claim in target.EffectClaims)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var evidence = claim.Evidence;
                if (evidence.ContractKind is not (WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions or
                        WorkerEffectContractKind.EffectContract) || !claim.HasValidConstraint)
                { continue; }
                if (evidence.ContractKind is WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions)
                {
                    initializationSafe ??= new ExceptionEntryInitialization(compilation, cancellationToken).NoFault(target.Method.ContainingType);
                    if (initializationSafe != true)
                    { continue; }
                }
                yield return evidence;
            }
        }
    }

    private static CompilerMetadataClauseOrigin? MetadataOrigin(TotalMetadataPrecondition? clause)
    {
        return clause == null ? null : MetadataOrigin(clause.Image, clause.Evidence);
    }

    private static CompilerMetadataClauseOrigin MetadataOrigin(TotalMetadataImage body, TotalIlClosedAttribute evidence)
    {
        return new(body.Method.ContainingAssembly.Identity.ToString(), body.ImageSha256, body.Module, body.ModuleMvid,
            evidence.MethodToken, evidence.ParameterOrdinal, evidence.ParameterSequence, evidence.ParameterToken,
            evidence.AttributeToken, evidence.ConstructorToken, evidence.AttributeIdentity, evidence.ConstructorIdentity,
            evidence.Kind, evidence.Minimum, evidence.Maximum, evidence.ConstructorSignature, evidence.ValueBlob);
    }

    private static bool PrepareMetadataRequires(CSharpCompilation compilation, TotalLoweringContext frame,
        TotalIlBody body, CancellationToken cancellationToken)
    {
        var binding = new TotalContractBinder(compilation, frame.Factory).BindTotalMetadataRequires(frame, cancellationToken);
        if (!CompilerTotalIlBodyProvider.TryPairMetadataRequires(binding.Clauses,
            body.ParameterAttributes, cancellationToken, out var paired))
        { return false; }
        var clauses = ImmutableArray.CreateBuilder<TotalMetadataPrecondition>();
        for (var ordinal = 0; ordinal < binding.Clauses.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CompilerTotalCallableArtifactCodec.TryGetMetadataEvidenceDigest(MetadataOrigin(body.Image, paired[ordinal]), out var digest))
            { return false; }
            var bound = binding.Clauses[ordinal];
            clauses.Add(new(bound.Value, bound.SafeCondition, frame.Factory.CreateOperation("metadata Requires"),
                body.Image, paired[ordinal], digest));
        }
        frame.MetadataCallPreconditions = clauses.ToImmutable();
        return true;
    }

    // The checked clauses of a callee in another assembly: the Requires of a
    // companion this compilation declares for it, its closed parameter
    // attributes paired with the rows of its authenticated reference image,
    // and the Requires its project exported for the exact image bound here.
    // Either all of them are recorded at a call or none is, so a call is never
    // counted as checked while one of its clauses is not.
    private static bool PrepareMetadataCallee(CSharpCompilation compilation, CompilerTotalIlBodyProvider images,
        TotalLoweringContext frame, CompilerContractExports contractExports, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!contractExports.TryPrepare(frame, cancellationToken, out var exported))
        { return false; }
        var binder = new TotalContractBinder(compilation, frame.Factory);
        var companion = binder.BindTotalCompanionRequires(frame, cancellationToken);
        if (!companion.IsSuccess)
        { return false; }
        var attributes = binder.BindTotalMetadataRequires(frame, cancellationToken);
        if (!attributes.IsSuccess)
        { return false; }
        var clauses = ImmutableArray.CreateBuilder<TotalMetadataPrecondition>(attributes.Clauses.Length);
        if (!attributes.Clauses.IsEmpty)
        {
            if (images.ResolveParameterAttributes(frame.Target, cancellationToken) is not { } image ||
                !CompilerTotalIlBodyProvider.TryPairMetadataRequires(attributes.Clauses, image.ParameterAttributes, cancellationToken, out var paired))
            { return false; }
            for (var ordinal = 0; ordinal < attributes.Clauses.Length; ordinal++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CompilerTotalCallableArtifactCodec.TryGetMetadataEvidenceDigest(MetadataOrigin(image, paired[ordinal]), out var digest))
                { return false; }
                var bound = attributes.Clauses[ordinal];
                clauses.Add(new(bound.Value, bound.SafeCondition, frame.Factory.CreateOperation("metadata Requires"),
                    image, paired[ordinal], digest));
            }
        }
        frame.SourceCallPreconditions = [.. companion.Clauses.Where(static clause => clause.Kind == BoundContractKind.Requires)
            .Select(static clause => new TotalSourcePrecondition(clause.Value, clause.SafeCondition, clause.SourceOperation))];
        frame.MetadataCallPreconditions = clauses.MoveToImmutable();
        frame.ExportedCallPreconditions = exported;
        return true;
    }
}
