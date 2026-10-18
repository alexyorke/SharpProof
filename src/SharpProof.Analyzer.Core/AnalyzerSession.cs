namespace SharpProof.Analyzer;

internal interface IAnalyzerSessionFactory
{
    AnalyzerSession Create(
        Compilation compilation,
        CompilerConfiguration configuration,
        CancellationToken cancellationToken);
}

internal sealed class DefaultAnalyzerSessionFactory : IAnalyzerSessionFactory
{
    internal static DefaultAnalyzerSessionFactory Instance { get; } = new();

    private DefaultAnalyzerSessionFactory()
    {
    }

    public AnalyzerSession Create(
        Compilation compilation,
        CompilerConfiguration configuration,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        configuration = RepositoryParameterGuard.NotNull(configuration, nameof(configuration));
        return new(compilation, configuration, cancellationToken);
    }
}

internal sealed class AnalyzerSession
{
    private readonly Action<IMethodSymbol, AnalyzerSemanticOutcome>? _outcomeObserver;
    private ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _validatedAttributes = ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>.Empty;
    private ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _validatedContractIntrinsics = ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>.Empty;
    private ImmutableDictionary<IMethodSymbol, byte>
        _reportedRejectedContractApis =
            ImmutableDictionary.Create<IMethodSymbol, byte>(SymbolEqualityComparer.Default);
    private ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>
        _reportedRejectedControlAttributes = ImmutableDictionary<(SyntaxTree Tree, TextSpan Span), byte>.Empty;
    private ImmutableDictionary<IMethodSymbol, byte>
        _executableAnalyses =
            ImmutableDictionary.Create<IMethodSymbol, byte>(SymbolEqualityComparer.Default);
    private ImmutableDictionary<IMethodSymbol, byte>
        _selectedSemicolonAccessors =
            ImmutableDictionary.Create<IMethodSymbol, byte>(SymbolEqualityComparer.Default);
    private ImmutableDictionary<IMethodSymbol, byte>
        _semanticOutcomes =
            ImmutableDictionary.Create<IMethodSymbol, byte>(SymbolEqualityComparer.Default);

    internal AnalyzerSession(
        Compilation compilation,
        CompilerConfiguration configuration,
        CancellationToken cancellationToken,
        Action<IMethodSymbol, AnalyzerSemanticOutcome>? outcomeObserver = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        Configuration = ArgumentNullGuard.NotNull(configuration, nameof(configuration));
        ClausePlacement = new(compilation, cancellationToken);
        _outcomeObserver = outcomeObserver;
        Semantics = new(compilation, cancellationToken);

    }

    internal Compilation Compilation
    {
        get;
    }
    internal CompilerConfiguration Configuration
    {
        get;
    }
    internal ContractSelectionInventory Attributes =>
        Semantics.Attributes;
    internal ContractClausePlacementDiagnostics ClausePlacement { get; }
    internal CompilerSemanticSession Semantics { get; }
    internal IrFactory IrFactory => Semantics.IrFactory;
    internal ResolvedApiSpecTable ApiSpecs => Semantics.ApiSpecs;
    internal bool HasCreatedApiSpecs => Semantics.HasCreatedApiSpecs;

    // One identity for a callable however it is referenced.
    internal static IMethodSymbol NormalizeMethod(IMethodSymbol method)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        return CompilerSemanticSession.NormalizeMethod(method);
    }

    internal ContractClauseInventory GetContractClauses(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return Semantics.GetContractClauses(method);
    }

    internal EffectiveContractSourceResolution ResolveContractSource(
        IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return Semantics.ResolveContractSource(method);
    }

    internal bool IsContractCompanion(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return Semantics.IsContractCompanion(method);
    }

    internal ContractBindingResult BindRequires(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return Semantics.BindRequires(method);
    }

    // Whether a call to the method may have to establish a precondition: a
    // Requires clause, a closed parameter contract, or one that cannot bind.
    internal bool HasPotentialCallPreconditions(
        IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return Semantics.HasPotentialCallPreconditions(method);
    }

    internal bool HasRejectedMetadataPrecondition(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return Semantics.HasRejectedMetadataPrecondition(method);
    }

    internal bool TryBeginExecutableAnalysis(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return ImmutableInterlocked.TryAdd(ref _executableAnalyses,
            NormalizeMethod(method),
            (byte)0);
    }

    internal ImmutableArray<ContractIntrinsicViolation> GetContractIntrinsicViolations(
        ContractClauseInventory inventory)
    {
        inventory = RepositoryParameterGuard.NotNull(inventory, nameof(inventory));
        return Semantics.GetContractIntrinsicViolations(inventory);
    }

    internal ImmutableArray<ContractIntrinsicViolation> GetContractIntrinsicViolations(
        IMethodSymbol callable,
        IOperation? body)
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        return Semantics.GetContractIntrinsicViolations(callable, body);
    }

    internal ImmutableArray<ContractIntrinsicViolation> GetMemberInitializerContractIntrinsicViolations(
        IOperation initializer)
    {
        initializer = RepositoryParameterGuard.NotNull(initializer, nameof(initializer));
        return Semantics.GetMemberInitializerContractIntrinsicViolations(initializer);
    }

    internal EffectContractResolution ResolveEffectContract(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return Semantics.ResolveEffectContract(method);
    }

    internal void RecordSemanticOutcome(
        IMethodSymbol method,
        AnalyzerSemanticOutcome outcome)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        method = NormalizeMethod(method);
        ImmutableInterlocked.TryAdd(ref _semanticOutcomes, method, (byte)0);
        _outcomeObserver?.Invoke(method, outcome);
    }

    internal void RegisterSelectedSemicolonAccessor(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        ImmutableInterlocked.TryAdd(ref _selectedSemicolonAccessors,
            NormalizeMethod(method),
            (byte)0);
    }

    internal ImmutableArray<IMethodSymbol> GetUnrecordedSelectedSemicolonAccessors()
    {
        return [.. _selectedSemicolonAccessors.Keys
            .Where(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (!_semanticOutcomes.ContainsKey(method)))
            .Select(static method =>
            {
                method = RepositoryParameterGuard.NotNull(method, nameof(method));
                var reference = method.DeclaringSyntaxReferences.FirstOrDefault();
                return (
                    Method: method,
                    FilePath: reference?.SyntaxTree.FilePath,
                    SpanStart: reference?.Span.Start ?? int.MaxValue);
            })
            .OrderBy(static item => item.FilePath, StringComparer.Ordinal)
            .ThenBy(static item => item.SpanStart)
            .Select(static item => item.Method)];
    }

    internal bool TryMarkAttributeValidated(AttributeData attribute)
    {
        attribute = ArgumentNullGuard.NotNull(attribute, nameof(attribute));
        var reference = attribute.ApplicationSyntaxReference;
        return reference == null ||
               TryMarkAttributeValidated(
                   reference.SyntaxTree,
                   reference.Span);
    }

    internal bool TryMarkAttributeValidated(
        SyntaxTree tree,
        TextSpan span)
    {
        tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
        return ImmutableInterlocked.TryAdd(ref _validatedAttributes, (tree, span), (byte)0);
    }

    internal bool TryMarkContractIntrinsicValidated(
        ContractIntrinsicViolation violation)
    {
        return ImmutableInterlocked.TryAdd(ref _validatedContractIntrinsics,
            (violation.Operation.Syntax.SyntaxTree,
             violation.Operation.Syntax.Span),
            (byte)0);
    }

    internal bool TryMarkRejectedContractApiReported(
        IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return ImmutableInterlocked.TryAdd(ref _reportedRejectedContractApis,
            ContractClauseInventoryBuilder.NormalizeCallable(method),
            (byte)0);
    }

    internal bool TryMarkRejectedControlAttributeReported(
        AttributeData attribute)
    {
        attribute = ArgumentNullGuard.NotNull(attribute, nameof(attribute));
        var reference = attribute.ApplicationSyntaxReference;
        return reference == null ||
            ImmutableInterlocked.TryAdd(ref _reportedRejectedControlAttributes,
                (reference.SyntaxTree, reference.Span),
                (byte)0);
    }

}
