namespace SharpProof.CompilerArtifact;

// Independent admission for the same discovered target. A legacy failure is
// preserved and does not suppress exact typed candidate evidence.
internal static class CompilerTotalCallableLowerer
{
    internal static CompilerTotalEntryPreparation? PrepareEntry(CSharpCompilation compilation,
        ManifestCallableTarget target, CompilerSyntaxTreeSnapshot[] capturedTrees, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target.Declaration is not MethodDeclarationSyntax || target.SemanticModel == null ||
            target.Method.Parameters.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        var documents = compilation.SyntaxTrees.Select((tree, ordinal) => (Tree: tree, Path: capturedTrees[ordinal].Path))
            .ToDictionary(item => item.Tree, item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target.Method, tree => documents[tree]);
        context.ModelReceiver();
        var binding = new ContractBinder(compilation, context.Factory).BindTotalRequires(context);
        cancellationToken.ThrowIfCancellationRequested();
        var preconditions = target.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).ToArray();
        if (!binding.IsSuccess || binding.Clauses.Length > CompilerArtifactLimits.MaximumInstructions ||
            binding.Clauses.Length != preconditions.Length)
        { return null; }
        return new(target.Entry.CallableId, context.Factory,
            [.. context.Inputs.Select(parameter => new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))],
            [.. binding.Clauses.Select((clause, ordinal) => new CompilerTotalClause(CompilerContractKind.Requires,
                clause.Value, clause.SafeCondition, clause.SourceOperation, null, preconditions[ordinal].Id))])
        { HasReceiver = context.Receiver != null };
    }

    internal static CompilerTotalCallablePreparation? Prepare(CSharpCompilation compilation,
        ManifestCallableTarget target, CompilerSyntaxTreeSnapshot[] capturedTrees,
        CompilerReferenceSnapshot[]? capturedReferences, CompilerSpecificationPackConfiguration specificationPackAuthority,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TotalBodyLowering.IsBodyDeclaration(target.Declaration) || target.SemanticModel == null ||
            target.Method.Parameters.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        var declaration = target.Declaration!;
        var autoAccessor = TotalBodyLowering.AutoAccessor(declaration);
        var documents = compilation.SyntaxTrees.Select((tree, ordinal) => (Tree: tree, Path: capturedTrees[ordinal].Path))
            .ToDictionary(item => item.Tree, item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target.Method, tree => documents[tree]);
        context.ModelReceiver();
        // An auto-property accessor has no body to carry contract clauses.
        var binding = autoAccessor != null ? new TotalContractBindingResult([], ContractBindingFailure.None, context.Origin)
            : new ContractBinder(compilation, context.Factory).BindTotal(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!binding.IsSuccess || binding.Clauses.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        var ensures = binding.Clauses.Where(clause => clause.Kind == BoundContractKind.Ensures).ToArray();
        // A postcondition on an overridable method is a contract for every
        // override; only effect claims, which describe this body, are verified.
        if (ensures.Length != 0 && (target.Method.IsVirtual || target.Method.IsOverride))
        { return null; }
        var requires = binding.Clauses.Where(clause => clause.Kind == BoundContractKind.Requires).ToArray();
        var preconditions = target.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.Precondition).ToArray();
        var assumptions = target.Entry.Assumptions.Where(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume).ToArray();
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
                documents[syntax.SyntaxTree] != span.Document)
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
        if (!lowering.IsExact || lowering.Program.Blocks.Length > CompilerArtifactLimits.MaximumInstructions ||
            TotalBodyLowering.UnmodeledElementWrites(lowering.Program) && TotalBodyLowering.ReadsElements(context.Factory, TotalBodyLowering.BodyTerms(lowering.Program)
                .Concat(lowering.CallPreconditions.Values.SelectMany(clause => new[] { clause.Value, clause.Safe }))))
        {
            if (lowering.ConstructionLimitExceeded || graph == null || graph.Blocks.Length > CompilerArtifactLimits.MaximumInstructions ||
                graph.Blocks.Sum(block => block.Operations.Length) > CompilerArtifactLimits.MaximumInstructions ||
                binding.Clauses.Any(clause => clause.Kind == BoundContractKind.Assume) ||
                context.Receiver is { } receiver && binding.Clauses.Any(clause => IrTraversal.CollectVariables([clause.Value, clause.SafeCondition])
                    .Any(variable => variable == receiver.Entry || variable == receiver.Current || variable == receiver.PreState)) ||
                context.Parameters.Any(parameter => !Primitive(parameter.Entry)) ||
                context.Result is { } resultVariable && !Primitive(resultVariable) ||
                context.Parameters.Length * 2 + 2 > CompilerArtifactLimits.MaximumInstructions)
            { return null; }
            var builder = new IrProgramBuilder(context.Factory);
            var block = builder.CreateBlock();
            var site = context.Site(target.SemanticModel.GetOperation(declaration, cancellationToken)!);
            foreach (var parameter in context.Parameters)
            {
                builder.Assign(block, site, parameter.Current, context.Factory.Variable(parameter.Entry));
                builder.Assign(block, site, parameter.PreState, context.Factory.Variable(parameter.Entry));
            }
            var mutable = context.Parameters.Select(parameter => parameter.Current)
                .Concat(context.Result is { } resultId ? [resultId] : Array.Empty<IrVarId>()).ToArray();
            if (mutable.Length == 0)
            { mutable = [context.Factory.CreateVariable("abstract-body", context.Factory.BooleanType)]; }
            builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, mutable);
            builder.Return(block, site, context.Result is { } returned ? context.Factory.Variable(returned) : null);
            program = builder.Build();
            isBodyAbstraction = true;

            bool Primitive(IrVarId variable)
            { return context.Factory.GetTypeInfo(context.Factory.GetVariableInfo(variable).Type).Kind is IrTypeKind.Boolean or IrTypeKind.Integer; }
        }
        var instructionCount = 0;
        foreach (var block in program.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block.Instructions.Length > CompilerArtifactLimits.MaximumInstructions - instructionCount)
            { return null; }
            instructionCount += block.Instructions.Length;
        }
        var claimOrdinal = 0;
        var assumptionOrdinal = 0;
        var userAssumptionOrdinal = 0;
        ImmutableArray<TotalInputBinding> inputs = isBodyAbstraction ? [.. context.Parameters] : context.Inputs;
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
            DisjointInputs = isBodyAbstraction ? [] : DisjointInputs(target.Method, cancellationToken),
            EffectsCompleteAtEntry = HasNoEffectEntryInitialization(compilation, target.Method.ContainingType, cancellationToken),
            ValidEffectClaimIds = [.. target.EffectClaims.Where(claim => claim.HasValidConstraint)
                .Select(claim => claim.Evidence.ClaimId).OrderBy(id => id, StringComparer.Ordinal)],
            ExceptionConstraints = ExceptionConstraints(compilation, target, cancellationToken),
            CallPreconditions = isBodyAbstraction ? [] : [.. lowering.CallPreconditions.OrderBy(pair => pair.Key.Id.Value)
                .Select(pair => new CompilerTotalCallPrecondition(pair.Key.Id, pair.Value.CalleeIdentity,
                    pair.Value.ClauseOrdinal, pair.Value.ClauseSite, pair.Value.Value, pair.Value.Safe,
                MetadataOrigin(pair.Value.MetadataClause))
                { Ancestry = [.. pair.Value.Ancestry.Select(hop => new CompilerShadowCallHop(hop.CallerIdentity, hop.CalleeIdentity, hop.Site))] })]
        };
    }

    private static ImmutableArray<CompilerDisjointInputPair> DisjointInputs(IMethodSymbol method, CancellationToken cancellationToken)
    {
        // Only Roslyn's closed sealed class symbols certify impossible aliases.
        var pairs = ImmutableArray.CreateBuilder<CompilerDisjointInputPair>();
        if (method.Parameters.Length > 128)
        { return []; }
        var remainingWork = 4096;
        var eligible = method.Parameters.Select(parameter => CompilerIdentityBridge.IsClosedSealedReferenceType(parameter.Type, cancellationToken)).ToArray();
        bool Related(INamedTypeSymbol left, INamedTypeSymbol right)
        {
            for (var current = left; current != null; current = current.BaseType)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remainingWork < 0)
                { return true; }
                if (SymbolEqualityComparer.Default.Equals(current, right))
                { return true; }
            }
            return false;
        }
        for (var left = 0; left < method.Parameters.Length; left++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!eligible[left])
            { continue; }
            for (var right = left + 1; right < method.Parameters.Length; right++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (eligible[right] &&
                    !Related((INamedTypeSymbol)method.Parameters[left].Type, (INamedTypeSymbol)method.Parameters[right].Type) &&
                    !Related((INamedTypeSymbol)method.Parameters[right].Type, (INamedTypeSymbol)method.Parameters[left].Type))
                {
                    if (pairs.Count == CompilerArtifactLimits.MaximumInstructions)
                    { return []; }
                    pairs.Add(new(left, right));
                }
            }
        }
        return pairs.ToImmutable();
    }

    // Rebuild the bounded source census inside this batch. Callers cannot
    // provide a forged owner or bypass the all-tree contract-binding guard.
    internal static CompilerShadowPreparationBatch PrepareShadowCallers(CSharpCompilation compilation,
        WorkerFeatureSet features, CompilerSyntaxTreeSnapshot[] capturedTrees,
        CompilerReferenceSnapshot[]? capturedReferences, CompilerSpecificationPackConfiguration specificationPackAuthority,
        CancellationToken cancellationToken, bool enableMetadataRequires = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (enableMetadataRequires && capturedReferences == null)
        { return new([], [new(0, 0, 0, "MissingMetadataOwnership")]); }
        if (compilation.References.Any(static reference => reference is CompilationReference))
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
        var published = discovery.Manifest.Callables.Select(static entry => entry.CallableId)
            .ToImmutableHashSet(StringComparer.Ordinal);
        // Use producer-owned paths instead of caller-supplied reporting metadata.
        var authoritativeTrees = CompilerCompilationCapture.CaptureTrees(compilation, cancellationToken);
        var initializationFree = CreateShadowInitializationPredicate(compilation, cancellationToken);
        foreach (var owner in inventory.Owners.OrderBy(static owner => owner.CallableId, StringComparer.Ordinal))
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
        var documents = compilation.SyntaxTrees.Select((tree, ordinal) => (Tree: tree, Path: capturedTrees[ordinal].Path))
            .ToDictionary(item => item.Tree, item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), owner.Method, tree => documents[tree]) { CaptureShadowCallAncestry = true };
        var binding = new ContractBinder(compilation, context.Factory).BindTotal(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!binding.IsSuccess || !binding.Clauses.IsEmpty)
        { reason = "UnsupportedOwnContracts"; return null; }
        ControlFlowGraph? graph;
        try
        { graph = ControlFlowGraph.Create(owner.Declaration, owner.SemanticModel, cancellationToken); }
        catch (ArgumentException)
        { return null; }
        if (graph == null)
        { return null; }
        var lowering = LowerBody(compilation, graph, context, capturedReferences, specificationPackAuthority, cancellationToken, initializationFree, enableMetadataRequires);
        cancellationToken.ThrowIfCancellationRequested();
        if (!lowering.IsExact || lowering.Program.Blocks.Length > CompilerArtifactLimits.MaximumInstructions)
        { return null; }
        var instructionCount = 0;
        foreach (var block in lowering.Program.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block.Instructions.Length > CompilerArtifactLimits.MaximumInstructions - instructionCount)
            { return null; }
            instructionCount += block.Instructions.Length;
        }
        return new(owner.CallableId, lowering.Program,
            [.. context.Parameters.Select(parameter => new CompilerTotalParameter(parameter.Entry, parameter.Current, parameter.PreState))],
            context.Result, [], false)
        {
            CallPreconditions = [.. lowering.CallPreconditions.OrderBy(pair => pair.Key.Id.Value)
                .Select(pair => new CompilerTotalCallPrecondition(pair.Key.Id, pair.Value.CalleeIdentity,
                    pair.Value.ClauseOrdinal, pair.Value.ClauseSite, pair.Value.Value, pair.Value.Safe,
                MetadataOrigin(pair.Value.MetadataClause))
                { Ancestry = [.. pair.Value.Ancestry.Select(hop => new CompilerShadowCallHop(hop.CallerIdentity, hop.CalleeIdentity, hop.Site))] })]
        };
    }

    private static FrontendProgramLoweringResult LowerBody(CSharpCompilation compilation,
        ControlFlowGraph graph, TotalLoweringContext context, CompilerReferenceSnapshot[]? capturedReferences,
        CompilerSpecificationPackConfiguration specificationPackAuthority, CancellationToken cancellationToken,
        Func<INamedTypeSymbol, bool>? initializationFree = null, bool enableMetadataRequires = false, bool opaqueCalls = false)
    {
        var specificationPacks = new CompilerSpecificationPackProvider(context.Factory, specificationPackAuthority);
        return TotalBodyLowering.Lower(compilation, graph, context, cancellationToken,
            new CompilerTotalIlBodyProvider(compilation, capturedReferences).Resolve, specificationPacks.ResolveTotal,
            initializationFree, enableMetadataRequires ? (frame, body) => PrepareMetadataRequires(compilation, frame, body, cancellationToken) : null,
            opaqueCalls);
    }

    private static Func<INamedTypeSymbol, bool> CreateShadowInitializationPredicate(
        CSharpCompilation compilation, CancellationToken cancellationToken)
    {
        var types = new Dictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);
        bool? moduleSafe = null;
        return type =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (moduleSafe == false)
            { return false; }
            if (!types.TryGetValue(type, out var typeSafe))
            {
                if (types.Count >= CompilerArtifactLimits.MaximumInstructions)
                { return false; }
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
        return HasNoTypeEntryInitialization(compilation, type, cancellationToken) &&
            HasNoModuleEntryInitialization(compilation, cancellationToken);
    }

    // A compiler-generated static constructor that only stores scalar
    // constants into readonly statics has no effect a body could observe.
    private static bool HasNoTypeEntryInitialization(CSharpCompilation compilation, INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var remaining = CompilerArtifactLimits.MaximumInstructions;
        for (var current = type; current != null; current = current.ContainingType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--remaining < 0 || current.StaticConstructors.Any(constructor => !constructor.IsImplicitlyDeclared))
            { return false; }
            foreach (var member in current.GetMembers().Where(member => member.IsStatic && member is IFieldSymbol { IsConst: false } or IPropertySymbol))
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
                        attribute.AttributeClass is { Name: "ModuleInitializerAttribute", ContainingNamespace: { } ns } &&
                        CompilerMetadataResolution.HasNamespace(ns, "System", "Runtime", "CompilerServices")))
                { return false; }
            }
        }
        return true;
    }

    private static ImmutableArray<CompilerTotalExceptionConstraint> ExceptionConstraints(CSharpCompilation compilation,
        ManifestCallableTarget target, CancellationToken cancellationToken)
    {
        var constraints = ImmutableArray.CreateBuilder<CompilerTotalExceptionConstraint>();
        var core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        foreach (var claim in target.EffectClaims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = claim.Evidence;
            if (evidence.ContractKind is not (WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions or
                    WorkerEffectContractKind.EffectContract) || !claim.HasValidConstraint)
            { continue; }
            var listsTypes = evidence.ContractKind == WorkerEffectContractKind.AllowedExceptions ||
                evidence.ContractKind == WorkerEffectContractKind.EffectContract &&
                (evidence.Constraint.AllowedEffects & WorkerEffectSet.Throws) != 0;
            var allowed = ImmutableArray.CreateBuilder<IrExceptionKind>();
            foreach (var kind in (IrExceptionKind[])Enum.GetValues(typeof(IrExceptionKind)))
            {
                var runtime = core.GetTypeByMetadataName(CSharpOperationSemantics.ExceptionMetadataName(kind));
                if (runtime == null)
                { return []; }
                if (listsTypes &&
                    CompilerExceptionTypeIdentity.EncodeHierarchy(runtime).Any(identity =>
                        evidence.Constraint.AllowedExceptionTypes.Contains(identity, StringComparer.Ordinal)))
                { allowed.Add(kind); }
            }
            constraints.Add(new(evidence.ClaimId, allowed.ToImmutable()));
        }
        return constraints.ToImmutable();
    }

    private static CompilerMetadataClauseOrigin? MetadataOrigin(TotalMetadataPrecondition? clause)
    {
        return clause == null ? null : MetadataOrigin(clause.Body, clause.Evidence);
    }

    private static CompilerMetadataClauseOrigin MetadataOrigin(TotalIlBody body, TotalIlClosedAttribute evidence)
    {
        return new(body.Method.ContainingAssembly.Identity.ToString(), body.ImageSha256, body.Module, body.ModuleMvid,
            evidence.MethodToken, evidence.ParameterOrdinal, evidence.ParameterSequence, evidence.ParameterToken,
            evidence.AttributeToken, evidence.ConstructorToken, evidence.AttributeIdentity, evidence.ConstructorIdentity,
            evidence.Kind, evidence.Minimum, evidence.Maximum, evidence.ConstructorSignature, evidence.ValueBlob);
    }

    private static bool PrepareMetadataRequires(CSharpCompilation compilation, TotalLoweringContext frame,
        TotalIlBody body, CancellationToken cancellationToken)
    {
        var binding = new ContractBinder(compilation, frame.Factory).BindTotalMetadataRequires(frame, cancellationToken);
        if (!binding.IsSuccess || !CompilerTotalIlBodyProvider.TryPairMetadataRequires(binding.Clauses,
            body.ParameterAttributes, cancellationToken, out var paired))
        { return false; }
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
