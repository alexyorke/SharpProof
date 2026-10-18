namespace SharpProof.CompilerArtifact;

// Independent admission for the same discovered target. A legacy failure is
// preserved and does not suppress exact typed candidate evidence.
internal static class CompilerTotalCallableLowerer
{
    internal static CompilerTotalEntryPreparation? PrepareEntry(CSharpCompilation compilation,
        ManifestCallableTarget target, CompilerSyntaxTreeSnapshot[] capturedTrees, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        capturedTrees = RepositoryParameterGuard.NotNull(capturedTrees, nameof(capturedTrees));
        if (target.Declaration is not MethodDeclarationSyntax || target.SemanticModel == null ||
            target.Method.Parameters.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        RequireCapturedTrees(compilation, capturedTrees);
        var documents = compilation.SyntaxTrees.Select((tree, ordinal) => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((Tree: tree, Path: capturedTrees[ordinal].Path)))))
            .ToDictionary(item => item.Tree, item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target.Method, tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (documents[tree]));
        context.ModelReceiver();
        var binding = new ContractBinder(compilation, context.Factory).BindTotalRequires(context,
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var preconditions = target.Entry.Assumptions.Where(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.Precondition)).ToArray();
        if (!binding.IsSuccess || binding.Clauses.Length > CompilerArtifactLimits.MaximumInstructions ||
            binding.Clauses.Length != preconditions.Length)
        { return null; }
        return new(target.Entry.CallableId, context.Factory,
            [.. context.Inputs.Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState)))],
            [.. binding.Clauses.Select((clause, ordinal) => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (new CompilerTotalClause(CompilerContractKind.Requires,
                clause.Value, clause.SafeCondition, clause.SourceOperation, null, preconditions[ordinal].Id)))))])
        { HasReceiver = context.Receiver != null };
    }

    internal static CompilerTotalCallablePreparation? Prepare(CSharpCompilation compilation,
        ManifestCallableTarget target, CompilerSyntaxTreeSnapshot[] capturedTrees,
        CompilerReferenceSnapshot[]? capturedReferences, CompilerSpecificationPackConfiguration specificationPackAuthority,
        CancellationToken cancellationToken)
    {
        specificationPackAuthority = RepositoryParameterGuard.NotNull(specificationPackAuthority, nameof(specificationPackAuthority));
        cancellationToken.ThrowIfCancellationRequested();
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        capturedTrees = RepositoryParameterGuard.NotNull(capturedTrees, nameof(capturedTrees));
        if (!TotalBodyLowering.IsBodyDeclaration(target.Declaration) || target.SemanticModel == null ||
            target.Method.Parameters.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        var declaration = target.Declaration!;
        var autoAccessor = TotalBodyLowering.AutoAccessor(declaration);
        RequireCapturedTrees(compilation, capturedTrees);
        var documents = compilation.SyntaxTrees.Select((tree, ordinal) => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((Tree: tree, Path: capturedTrees[ordinal].Path)))))
            .ToDictionary(item => item.Tree, item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target.Method, tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (documents[tree]));
        context.ModelReceiver();
        // An auto-property accessor has no body to carry contract clauses.
        var binding = autoAccessor != null ? new TotalContractBindingResult([], ContractBindingFailure.None, context.Origin)
            : new ContractBinder(compilation, context.Factory).BindTotal(context,
                cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!binding.IsSuccess || binding.Clauses.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        var ensures = binding.Clauses.Where(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Ensures)).ToArray();
        // A postcondition on an overridable method is a contract for every
        // override; only effect claims, which describe this body, are verified.
        if (ensures.Length != 0 && (target.Method.IsVirtual || target.Method.IsOverride))
        { return null; }
        var requires = binding.Clauses.Where(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Requires)).ToArray();
        var preconditions = target.Entry.Assumptions.Where(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.Precondition)).ToArray();
        var assumptions = target.Entry.Assumptions.Where(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.UserAssume)).ToArray();
        if (ensures.Length != target.Claims.Length || requires.Length != preconditions.Length ||
            binding.Clauses.Count(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Assume)) != assumptions.Length)
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
        { lowering = new RoslynProgramLowerer(context.Factory).LowerAutoAccessor(context, autoAccessor); }
        else
        {
            graph = TotalBodyLowering.CreateGraph(declaration, target.Method, target.SemanticModel, cancellationToken);
            lowering = graph == null ? null : LowerBody(compilation, graph, context, capturedReferences, specificationPackAuthority,
                cancellationToken, opaqueCalls: true);
        }
        if (lowering == null)
        { return null; }
        cancellationToken.ThrowIfCancellationRequested();
        var program = lowering.Program;
        var isBodyAbstraction = false;
        if (!lowering.IsExact || lowering.Program.Blocks.Length > CompilerArtifactLimits.MaximumInstructions)
        {
            if (lowering.ConstructionLimitExceeded || graph == null || graph.Blocks.Length > CompilerArtifactLimits.MaximumInstructions ||
                graph.Blocks.Sum(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations.Length)) > CompilerArtifactLimits.MaximumInstructions ||
                binding.Clauses.Any(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Assume)) ||
                context.Receiver is { } receiver && binding.Clauses.Any(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (IrTraversal.CollectVariables([clause.Value, clause.SafeCondition])
                    .Any(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable == receiver.Entry || variable == receiver.Current || variable == receiver.PreState)))) ||
                context.Parameters.Any(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (!Primitive(parameter.Entry))) ||
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
            var mutable = context.Parameters.Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.Current))
                .Concat(context.Result is { } resultId ? [resultId] : Array.Empty<IrVarId>()).ToArray();
            if (mutable.Length == 0)
            { mutable = [context.Factory.CreateVariable("abstract-body", context.Factory.BooleanType)]; }
            builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, mutable);
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
        var claimOrdinal = 0;
        var assumptionOrdinal = 0;
        var userAssumptionOrdinal = 0;
        ImmutableArray<TotalInputBinding> inputs = isBodyAbstraction ? [.. context.Parameters] : context.Inputs;
        return new(target.Entry.CallableId, program,
            [.. inputs.Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState)))],
            context.Result,
            [.. binding.Clauses.Select(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (new CompilerTotalClause(CompilerLoweringWireMappings.ToCompiler(clause.Kind),
                clause.Value, clause.SafeCondition, clause.SourceOperation,
                clause.Kind == BoundContractKind.Ensures ? target.Claims[claimOrdinal++].Entry.ClaimId : null,
                clause.Kind == BoundContractKind.Requires ? preconditions[assumptionOrdinal++].Id :
                    clause.Kind == BoundContractKind.Assume ? assumptions[userAssumptionOrdinal++].Id : null)))], isBodyAbstraction)
        {
            HasReceiver = inputs.Length != context.Parameters.Length,
            DisjointInputs = isBodyAbstraction ? [] : CompilerDisjointInputAnalysis.Collect(target.Method, cancellationToken),
            EffectsCompleteAtEntry = lowering.SourceInitializationEffectsComplete &&
                HasNoEffectEntryInitialization(compilation, target.Method.ContainingType, cancellationToken),
            ValidEffectClaimIds = [.. target.EffectClaims.Where(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.HasValidConstraint))
                .Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.Evidence.ClaimId)).OrderBy(id => id is null ? throw new System.ArgumentNullException(nameof(id)) : (id), StringComparer.Ordinal)],
            ExceptionConstraints = ExceptionConstraints(compilation, target, cancellationToken),
            CallPreconditions = isBodyAbstraction ? [] : CreateCallPreconditions(lowering)
        };
    }

    // Captured snapshots are indexed by compilation tree ordinal; a mismatch
    // is a caller contract violation, not an admission decision.
    private static void RequireCapturedTrees(CSharpCompilation compilation, CompilerSyntaxTreeSnapshot[] capturedTrees)
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
        WorkerFeatureSet features, CompilerSyntaxTreeSnapshot[] capturedTrees,
        CompilerReferenceSnapshot[]? capturedReferences, CompilerSpecificationPackConfiguration specificationPackAuthority,
        CancellationToken cancellationToken, bool enableMetadataRequires = false)
    {
        specificationPackAuthority = RepositoryParameterGuard.NotNull(specificationPackAuthority, nameof(specificationPackAuthority));
        cancellationToken.ThrowIfCancellationRequested();
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        capturedTrees = RepositoryParameterGuard.NotNull(capturedTrees, nameof(capturedTrees));
        if (enableMetadataRequires && capturedReferences == null)
        { return new([], [new(0, 0, 0, "MissingMetadataOwnership")]); }
        if (compilation.References.Any(static reference => reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference is CompilationReference)))
        { return new([], [new(0, 0, 0, "UnsupportedSourceReference")]); }
        var trees = compilation.SyntaxTrees.ToArray();
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
        var published = discovery.Manifest.Callables.Select(static entry => entry is null ? throw new System.ArgumentNullException(nameof(entry)) : (entry.CallableId))
            .ToImmutableHashSet(StringComparer.Ordinal);
        // Use producer-owned paths instead of caller-supplied reporting metadata.
        var authoritativeTrees = CompilerCompilationCapture.CaptureTrees(compilation, cancellationToken);
        var initializationFree = CreateShadowInitializationPredicate(compilation, cancellationToken);
        foreach (var owner in inventory.Owners.OrderBy(static owner => owner is null ? throw new System.ArgumentNullException(nameof(owner)) : (owner.CallableId), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (published.Contains(owner.CallableId) || !owner.DiscoveryComplete)
            { continue; }
            var treeOrdinal = Array.IndexOf(trees, owner.Declaration.SyntaxTree);
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
        CompilerPotentialCallOwner owner, CompilerSyntaxTreeSnapshot[] capturedTrees,
        CompilerReferenceSnapshot[]? capturedReferences, CompilerSpecificationPackConfiguration specificationPackAuthority,
        Func<INamedTypeSymbol, bool> initializationFree, CancellationToken cancellationToken, out string reason, bool enableMetadataRequires = false)
    {
        capturedTrees = RepositoryParameterGuard.NotNull(capturedTrees, nameof(capturedTrees));
        specificationPackAuthority = RepositoryParameterGuard.NotNull(specificationPackAuthority, nameof(specificationPackAuthority));
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
        var documents = compilation.SyntaxTrees.Select((tree, ordinal) => ((ordinal < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(ordinal)) : (tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((Tree: tree, Path: capturedTrees[ordinal].Path)))))
            .ToDictionary(item => item.Tree, item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), owner.Method, tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (documents[tree])) { CaptureShadowCallAncestry = true };
        var binding = new ContractBinder(compilation, context.Factory).BindTotal(context,
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
            [.. context.Parameters.Select(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState)))],
            context.Result, [], false)
        {
            CallPreconditions = CreateCallPreconditions(lowering)
        };
    }

    private static ImmutableArray<CompilerTotalCallPrecondition> CreateCallPreconditions(
        FrontendProgramLoweringResult lowering)
    {
        lowering = RepositoryParameterGuard.NotNull(lowering, nameof(lowering));
        return [.. lowering.CallPreconditions.OrderBy(pair => pair.Key.Id.Value)
            .Select(pair => new CompilerTotalCallPrecondition(pair.Key.Id, pair.Value.CalleeIdentity,
                pair.Value.ClauseOrdinal, pair.Value.ClauseSite, pair.Value.Value, pair.Value.Safe,
                MetadataOrigin(pair.Value.MetadataClause))
            { Ancestry = [.. pair.Value.Ancestry.Select(hop => hop is null ? throw new System.ArgumentNullException(nameof(hop)) : (new CompilerShadowCallHop(hop.CallerIdentity, hop.CalleeIdentity, hop.Site)))] })];
    }

    private static FrontendProgramLoweringResult LowerBody(CSharpCompilation compilation,
        ControlFlowGraph graph, TotalLoweringContext context, CompilerReferenceSnapshot[]? capturedReferences,
        CompilerSpecificationPackConfiguration specificationPackAuthority, CancellationToken cancellationToken,
        Func<INamedTypeSymbol, bool>? initializationFree = null, bool enableMetadataRequires = false, bool opaqueCalls = false)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        specificationPackAuthority = RepositoryParameterGuard.NotNull(specificationPackAuthority, nameof(specificationPackAuthority));
        var specificationPacks = new CompilerSpecificationPackProvider(context.Factory, specificationPackAuthority);
        return TotalBodyLowering.Lower(compilation, graph, context, cancellationToken,
            new CompilerTotalIlBodyProvider(compilation, capturedReferences).Resolve, specificationPacks.ResolveTotal,
            initializationFree, enableMetadataRequires ? (frame, body) => body is null ? throw new System.ArgumentNullException(nameof(body)) : frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (PrepareMetadataRequires(compilation, frame, body, cancellationToken)) : null,
            opaqueCalls);
    }

    private static Func<INamedTypeSymbol, bool> CreateShadowInitializationPredicate(
        CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var types = new Dictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);
        bool? moduleSafe = null;
        return type =>
        {
            type = RepositoryParameterGuard.NotNull(type, nameof(type));
            cancellationToken.ThrowIfCancellationRequested();
            if (moduleSafe == false)
            { return false; }
            if (!types.TryGetValue(type, out var typeSafe))
            {
                typeSafe = HasNoTypeEntryInitialization(compilation, type, cancellationToken);
                types.Add(type, typeSafe);
            }
            if (!typeSafe)
            { return false; }
            moduleSafe ??= HasNoModuleEntryInitialization(compilation, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return moduleSafe.Value;
        };
    }

    internal static bool HasNoEffectEntryInitialization(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return HasNoTypeEntryInitialization(compilation, type, cancellationToken) &&
            HasNoModuleEntryInitialization(compilation, cancellationToken);
    }

    // A compiler-generated static constructor that only stores scalar
    // constants into readonly statics has no effect a body could observe.
    private static bool HasNoTypeEntryInitialization(CSharpCompilation compilation, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        var remaining = CompilerArtifactLimits.MaximumInstructions;
        for (var current = type; current != null; current = current.ContainingType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--remaining < 0 || current.StaticConstructors.Any(constructor => constructor is null ? throw new System.ArgumentNullException(nameof(constructor)) : (!constructor.IsImplicitlyDeclared)))
            { return false; }
            foreach (var member in current.GetMembers().Where(member => member is null ? throw new System.ArgumentNullException(nameof(member)) : (member.IsStatic && member is IFieldSymbol { IsConst: false } or IPropertySymbol)))
            {
                foreach (var reference in member.DeclaringSyntaxReferences)
                {
                    if (--remaining < 0)
                    { return false; }
                    var syntax = reference.GetSyntax(cancellationToken);
                    var value = syntax switch
                    {
                        VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                        PropertyDeclarationSyntax property => property.Initializer?.Value,
                        _ => null
                    };
                    if (value != null && (member is IFieldSymbol { IsReadOnly: false } or IPropertySymbol { SetMethod: not null } ||
                        !SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, reference.SyntaxTree)
                            .GetConstantValue(value, cancellationToken).HasValue ||
                        !CSharpOperationSemantics.IsScalar(member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type)))
                    { return false; }
                }
            }
        }
        return true;
    }

    private static bool HasNoModuleEntryInitialization(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(compilation.Assembly.GlobalNamespace);
        var remaining = CompilerArtifactLimits.MaximumInstructions;
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var member in pending.Pop().GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remaining < 0)
                { return false; }
                if (member is INamespaceOrTypeSymbol nested)
                { pending.Push(nested); }
                if (member is IMethodSymbol method && method.GetAttributes().Any(attribute =>
                        attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeClass is { Name: "ModuleInitializerAttribute", ContainingNamespace: { } ns } &&
                        CompilerMetadataResolution.HasNamespace(ns, "System", "Runtime", "CompilerServices"))))
                { return false; }
            }
        }
        return true;
    }

    // Exception proof admission is separate from entry purity: a constant
    // static write or ordinary object allocation need not throw. Unproved
    // initializer code cannot supply a complete exception constraint row.
    private sealed class ExceptionEntryInitialization(CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        private readonly Dictionary<INamedTypeSymbol, bool> _checked = (compilation is null ? throw new System.ArgumentNullException(nameof(compilation)) : new(SymbolEqualityComparer.Default));
        private readonly HashSet<INamedTypeSymbol> _active = new(SymbolEqualityComparer.Default);
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
            if (type.DeclaringSyntaxReferences.Length == 0 || !_active.Add(type))
            { return false; }
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
            var pending = new Stack<INamespaceOrTypeSymbol>();
            pending.Push(compilation.Assembly.GlobalNamespace);
            while (pending.Count != 0)
            {
                foreach (var member in pending.Pop().GetMembers())
                {
                    if (!Spend())
                    { return false; }
                    if (member is INamespaceOrTypeSymbol nested)
                    { pending.Push(nested); }
                    if (member is IMethodSymbol method && method.GetAttributes().Any(attribute =>
                            attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeClass is { Name: "ModuleInitializerAttribute", ContainingNamespace: { } ns } &&
                            CompilerMetadataResolution.HasNamespace(ns, "System", "Runtime", "CompilerServices"))) &&
                        (!NoFaultType(method.ContainingType) || !NoFaultBody(method, method.ContainingType)))
                    { return false; }
                }
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
            initializedOwner = RepositoryParameterGuard.NotNull(initializedOwner, nameof(initializedOwner));
            model = RepositoryParameterGuard.NotNull(model, nameof(model));
            statement = RepositoryParameterGuard.NotNull(statement, nameof(statement));
            if (!Spend())
            { return false; }
            return statement switch
            {
                BlockSyntax block => block.Statements.All(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (NoFaultStatement(child, model, initializedOwner))),
                EmptyStatementSyntax or ReturnStatementSyntax { Expression: null } => true,
                ExpressionStatementSyntax expression => NoFaultStore(model.GetOperation(expression.Expression, cancellationToken), initializedOwner),
                _ => false
            };
        }

        private bool NoFaultStore(IOperation? operation, INamedTypeSymbol initializedOwner)
        {
            initializedOwner = RepositoryParameterGuard.NotNull(initializedOwner, nameof(initializedOwner));
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
            operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
            return CSharpOperationSemantics.IsScalar(operation.Type) || operation.Type?.SpecialType == SpecialType.System_String ||
                operation.ConstantValue.Value == null && (operation.Type == null || operation.Type.IsReferenceType);
        }
    }
    private static ImmutableArray<CompilerTotalExceptionConstraint> ExceptionConstraints(CSharpCompilation compilation,
        ManifestCallableTarget target, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
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
        return clause == null ? null : MetadataOrigin(clause.Body, clause.Evidence);
    }

    private static CompilerMetadataClauseOrigin MetadataOrigin(TotalIlBody body, TotalIlClosedAttribute evidence)
    {
        body = RepositoryParameterGuard.NotNull(body, nameof(body));
        evidence = RepositoryParameterGuard.NotNull(evidence, nameof(evidence));
        return new(body.Method.ContainingAssembly.Identity.ToString(), body.ImageSha256, body.Module, body.ModuleMvid,
            evidence.MethodToken, evidence.ParameterOrdinal, evidence.ParameterSequence, evidence.ParameterToken,
            evidence.AttributeToken, evidence.ConstructorToken, evidence.AttributeIdentity, evidence.ConstructorIdentity,
            evidence.Kind, evidence.Minimum, evidence.Maximum, evidence.ConstructorSignature, evidence.ValueBlob);
    }

    private static bool PrepareMetadataRequires(CSharpCompilation compilation, TotalLoweringContext frame,
        TotalIlBody body, CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        frame = RepositoryParameterGuard.NotNull(frame, nameof(frame));
        var binding = new ContractBinder(compilation, frame.Factory).BindTotalMetadataRequires(frame, cancellationToken);
        _ = CompilerTotalIlBodyProvider.TryPairMetadataRequires(binding.Clauses,
            body.ParameterAttributes, cancellationToken, out var paired);
        var clauses = ImmutableArray.CreateBuilder<TotalMetadataPrecondition>();
        for (var ordinal = 0; ordinal < binding.Clauses.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CompilerTotalCallableArtifactCodec.TryGetMetadataEvidenceDigest(MetadataOrigin(body, paired[ordinal]), out var digest))
            { return false; }
            var bound = binding.Clauses[ordinal];
            clauses.Add(new(bound.Value, bound.SafeCondition, frame.Factory.CreateOperation("metadata Requires"),
                body, paired[ordinal], digest));
        }
        frame.MetadataCallPreconditions = clauses.ToImmutable();
        return true;
    }
}
