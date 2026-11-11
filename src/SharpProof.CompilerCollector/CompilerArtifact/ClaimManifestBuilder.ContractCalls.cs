// This census runs only in the build-time compiler collector.
namespace SharpProof.CompilerCollector;

// Finds the source calls whose callee declares a precondition the compiler can
// see: the callers that may need a claim, and the sites a pass does not cover.
// A call spells its callee's name or is one of a few forms that cannot, so only
// those nodes are bound and a body that names no such callee stays unbound.
internal sealed partial class ClaimManifestBuilder
{
    private sealed record PreconditionCallNames(ImmutableHashSet<string> Names, bool Constructors, bool Indexers)
    {
        public ImmutableHashSet<string> Names { get; init; } = Names ?? throw new ArgumentNullException(nameof(Names));
    }

    private ClaimManifestBuildResult WithContractCalls(ClaimManifestBuildResult result)
    {
        result = RepositoryParameterGuard.NotNull(result, nameof(result));
        if (!ContractsEnabled || CollectPreconditionCallNames(result.Targets) is not { } calls)
        { return result; }
        var preconditions = ImmutableDictionary.CreateBuilder<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
        var seen = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
        var callers = ImmutableArray.CreateBuilder<ManifestCallableTarget>();
        var sites = ImmutableHashSet.CreateBuilder<WorkerSourceLocation>();
        var bound = ImmutableArray.CreateBuilder<SyntaxNode>();
        foreach (var tree in _compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remainingNodes = int.MaxValue;
            // Semantic-model creation binds every nested local function; a tree
            // too deep to walk here would exhaust Roslyn's binder stack there.
            if (!TryCollectPotentialSyntax(tree.GetRoot(cancellationToken), ref remainingNodes, out var nodes, maximumNodes: int.MaxValue))
            { continue; }
            SemanticModel? model = null;
            foreach (var node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MayCallPrecondition(node, calls))
                { continue; }
                model ??= SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(_compilation, tree);
                bound.Add(node);
                if (model.GetOperation(node, cancellationToken) is not { } operation || !CallsPrecondition(operation, preconditions))
                { continue; }
                sites.Add(CallSite(operation));
                if (CallRoot(model, node) is { } root && !result.Targets.ContainsKey(root) && seen.Add(root) && IsCallRoot(root))
                { callers.Add(CreateContractCaller(root)); }
            }
        }
        return result with
        {
            ContractCallers = callers.ToImmutable(),
            PreconditionCallSites = [.. sites],
            BoundCallSyntax = bound.ToImmutable()
        };
    }

    // Null when nothing the compiler can see declares a precondition.
    private PreconditionCallNames? CollectPreconditionCallNames(ImmutableDictionary<IMethodSymbol, ManifestCallableTarget> selected)
    {
        selected = RepositoryParameterGuard.NotNull(selected, nameof(selected));
        var names = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var constructors = false;
        var indexers = false;
        var callees = selected.Values
            .Where(static target => target.Entry.Assumptions.Any(static assumption => assumption.Kind == WorkerAssumptionKind.Precondition))
            .Select(static target => target.Method);
        foreach (var callee in callees.Concat(MetadataPreconditionCallees()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (callee.MethodKind == MethodKind.Constructor)
            {
                constructors = true;
                names.Add(callee.ContainingType.Name);
            }
            else if (callee.AssociatedSymbol is IPropertySymbol { IsIndexer: true })
            { indexers = true; }
            else
            { names.Add(callee.AssociatedSymbol?.Name ?? callee.Name); }
        }
        if (names.Count == 0 && !indexers)
        { return null; }
        // An alias renames a constructor's type at the call.
        foreach (var tree in _compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var alias in tree.GetRoot(cancellationToken).DescendantNodes(static node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax).OfType<UsingDirectiveSyntax>())
            {
                if (alias.Alias is { } declared && alias.Name is { } target && names.Contains(RightmostName(target)))
                { names.Add(declared.Name.Identifier.ValueText); }
            }
        }
        return new(names.ToImmutable(), constructors, indexers);
    }

    // Parameter attributes are all a referenced assembly keeps: the compiler
    // removes its Contract.Requires calls from the binary.
    private IEnumerable<IMethodSymbol> MetadataPreconditionCallees()
    {
        foreach (var assembly in _compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (!assembly.Modules.Any(static module => module.ReferencedAssemblySymbols.Any(static reference => reference.Identity.Name == "SharpProof.Attributes")))
            { continue; }
            var pending = ImmutableStack<INamespaceOrTypeSymbol>.Empty.Push(assembly.GlobalNamespace);
            while (!pending.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var owner = pending.Peek();
                pending = pending.Pop();
                foreach (var member in owner.GetMembers())
                {
                    if (member is INamespaceOrTypeSymbol nested)
                    { pending = pending.Push(nested); }
                    else if (member is IMethodSymbol method && method.Parameters.Any(parameter => parameter.GetAttributes().Any(_attributes.IsClosedContract)))
                    { yield return method; }
                }
            }
        }
    }

    private static bool MayCallPrecondition(SyntaxNode node, PreconditionCallNames calls)
    {
        node = RepositoryParameterGuard.NotNull(node, nameof(node));
        calls = RepositoryParameterGuard.NotNull(calls, nameof(calls));
        return node switch
        {
            InvocationExpressionSyntax invocation => invocation.Expression switch
            {
                SimpleNameSyntax name => calls.Names.Contains(name.Identifier.ValueText),
                MemberAccessExpressionSyntax access => calls.Names.Contains(access.Name.Identifier.ValueText),
                MemberBindingExpressionSyntax binding => calls.Names.Contains(binding.Name.Identifier.ValueText),
                _ => false
            },
            ObjectCreationExpressionSyntax { Type: NameSyntax type } => calls.Names.Contains(RightmostName(type)),
            ImplicitObjectCreationExpressionSyntax or ConstructorInitializerSyntax => calls.Constructors,
            ElementAccessExpressionSyntax => calls.Indexers,
            MemberAccessExpressionSyntax property => calls.Names.Contains(property.Name.Identifier.ValueText) && property.Parent is not InvocationExpressionSyntax,
            IdentifierNameSyntax property => calls.Names.Contains(property.Identifier.ValueText) &&
                property.Parent is not (InvocationExpressionSyntax or MemberAccessExpressionSyntax),
            _ => false
        };
    }

    private static string RightmostName(NameSyntax name)
    {
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        return name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => string.Empty
        };
    }

    private bool CallsPrecondition(IOperation operation, ImmutableDictionary<IMethodSymbol, bool>.Builder known)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        known = RepositoryParameterGuard.NotNull(known, nameof(known));
        return operation switch
        {
            IInvocationOperation invocation => Has(invocation.TargetMethod),
            IObjectCreationOperation { Constructor: { } constructor } => Has(constructor),
            IPropertyReferenceOperation property => property.Property.GetMethod is { } getter && Has(getter) ||
                property.Property.SetMethod is { } setter && Has(setter),
            _ => false
        };

        bool Has(IMethodSymbol method)
        {
            method = RepositoryParameterGuard.NotNull(method, nameof(method));
            if (!known.TryGetValue(method, out var result))
            {
                result = _effectSession.HasPotentialCallPreconditions(method);
                known.Add(method, result);
            }
            return result;
        }
    }

    // Matches the site a call-precondition claim records, which for a setter
    // is the assignment.
    private WorkerSourceLocation CallSite(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var site = operation.Parent is ISimpleAssignmentOperation assignment && ReferenceEquals(assignment.Target, operation)
            ? assignment.Syntax : operation.Syntax;
        return ToSourceLocation(site.GetLocation());
    }

    private IMethodSymbol? CallRoot(SemanticModel model, SyntaxNode node)
    {
        model = RepositoryParameterGuard.NotNull(model, nameof(model));
        node = RepositoryParameterGuard.NotNull(node, nameof(node));
        var symbol = model.GetEnclosingSymbol(node.SpanStart, cancellationToken);
        while (symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
        { symbol = symbol.ContainingSymbol; }
        return symbol is IMethodSymbol method ? ContractClauseInventoryBuilder.NormalizeCallable(method) : null;
    }

    // Lambdas and local functions are not roots: giving them manifest identity
    // would renumber their siblings.
    private bool IsCallRoot(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return method.MethodKind is MethodKind.Ordinary or MethodKind.Constructor or MethodKind.ExplicitInterfaceImplementation &&
            CreateSeed(method).Declaration is MethodDeclarationSyntax or ConstructorDeclarationSyntax &&
            CreateSeed(method).Declaration is BaseMethodDeclarationSyntax { } declaration &&
            (declaration.Body != null || declaration.ExpressionBody != null) &&
            CallableSubset.IsSupported(method, declaration) &&
            !ContractForSymbolMatcher.IsCompanionType(_contractSources.Companions, method.ContainingType);
    }

    private ManifestCallableTarget CreateContractCaller(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var seed = CreateSeed(method);
        var entry = new WorkerCallableManifestEntry
        {
            CallableId = SemanticClaimIdentity.CreateCallableId(seed.Method),
            SelectedFeatures = [WorkerSelectedFeature.Contracts],
            SelectionReasons = [WorkerSelectionReason.DiscoveredCallPrecondition],
            Location = ToSourceLocation(CallableLocation(seed.Method, seed.Declaration))
        };
        return new ManifestCallableTarget(seed.Method, seed.Declaration, seed.Model, entry, [], [], true);
    }
}
