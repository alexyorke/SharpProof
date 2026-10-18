using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Frontend;

internal static class CSharpPreprocessorSymbols
{
    internal static ImmutableHashSet<string> GetDefined(
        SyntaxTree tree,
        CancellationToken cancellationToken)
    {
        tree = ArgumentNullGuard.NotNull(tree, nameof(tree));

        if (tree.Options is not CSharpParseOptions options)
        {
            return ImmutableHashSet<string>.Empty;
        }

        var defined = options.PreprocessorSymbolNames
            .ToImmutableHashSet(StringComparer.Ordinal)
            .ToBuilder();
        // C# permits #define and #undef only before the first token, so the
        // first token's leading trivia is the complete directive inventory.
        // Avoid walking every trivia node in an otherwise unannotated build.
        foreach (var trivia in tree.GetRoot(cancellationToken).GetLeadingTrivia())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (trivia.GetStructure())
            {
                case DefineDirectiveTriviaSyntax { IsActive: true } define:
                    defined.Add(define.Name.ValueText);
                    break;
                case UndefDirectiveTriviaSyntax { IsActive: true } undef:
                    defined.Remove(undef.Name.ValueText);
                    break;
            }
        }

        return defined.ToImmutable();
    }

    internal static bool IsDefined(
        SyntaxTree tree,
        string symbol,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException(
                "A preprocessor symbol is required.",
                nameof(symbol));
        }

        tree = ArgumentNullGuard.NotNull(tree, nameof(tree));
        if (tree.Options is not CSharpParseOptions options)
        {
            return false;
        }

        var defined = options.PreprocessorSymbolNames.Contains(symbol);
        foreach (var trivia in tree.GetRoot(cancellationToken).GetLeadingTrivia())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (trivia.GetStructure())
            {
                case DefineDirectiveTriviaSyntax { IsActive: true } define
                    when string.Equals(
                        define.Name.ValueText,
                        symbol,
                        StringComparison.Ordinal):
                    defined = true;
                    break;
                case UndefDirectiveTriviaSyntax { IsActive: true } undef
                    when string.Equals(
                        undef.Name.ValueText,
                        symbol,
                        StringComparison.Ordinal):
                    defined = false;
                    break;
            }
        }

        return defined;
    }
}

internal sealed class CSharpInvocationEmissionPolicy(Compilation compilation)
{
    // Analyzer callbacks share these pure caches; atomic publication retains
    // concurrent entries without exposing mutable collection state.
    private readonly Compilation _compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
    private ImmutableDictionary<SyntaxTree, ImmutableHashSet<string>>
        _definedPreprocessorSymbols = ImmutableDictionary<SyntaxTree, ImmutableHashSet<string>>.Empty;
    private ImmutableDictionary<IMethodSymbol, bool>
        _unimplementedPartials = ImmutableDictionary<IMethodSymbol, bool>.Empty.WithComparers(SymbolEqualityComparer.Default);
    private ImmutableDictionary<IMethodSymbol, ImmutableArray<string>>
        _conditionalSymbols = ImmutableDictionary<IMethodSymbol, ImmutableArray<string>>.Empty.WithComparers(SymbolEqualityComparer.Default);

    internal bool IsElided(IOperation operation, bool requireOwningInvocation = false)
    {
        ArgumentNullGuard.NotNull(operation, nameof(operation));
        var sourceSyntax = (operation is IExpressionStatementOperation statement ? statement.Operation : operation).Syntax;
        if (OwningInvocation(operation) is not { } invocation)
        { return false; }
        // A collection initializer can introduce a nested statement call in
        // an omitted argument. Only the recovered owning invocation ends
        // that argument subgraph during compiler-elision continuation scans.
        if (requireOwningInvocation && (sourceSyntax.SyntaxTree != invocation.Syntax.SyntaxTree ||
            sourceSyntax.Span != invocation.Syntax.Span))
        { return false; }
        var target = invocation.TargetMethod.ReducedFrom ??
            invocation.TargetMethod;
        var isUnimplementedPartial = GetOrAdd(
            ref _unimplementedPartials,
            target,
            IsUnimplementedPartial);
        if (isUnimplementedPartial)
        {
            return true;
        }

        if (!target.IsConditional ||
            invocation.Syntax.SyntaxTree.Options is not CSharpParseOptions)
        {
            return false;
        }
        var conditionalSymbols = GetOrAdd(
            ref _conditionalSymbols,
            target,
            GetConditionalSymbols);
        if (conditionalSymbols.IsDefaultOrEmpty)
        {
            return false;
        }
        var definedSymbols = GetOrAdd(
            ref _definedPreprocessorSymbols,
            invocation.Syntax.SyntaxTree,
            static tree =>
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                tree = RepositoryParameterGuard.NotNull(tree, nameof(tree));
                return CSharpPreprocessorSymbols.GetDefined(tree, cancellation.Token);
            });
        return conditionalSymbols.All(symbol =>
            symbol is null ? throw new System.ArgumentNullException(nameof(symbol)) : (!definedSymbols.Contains(symbol)));
    }

    internal IInvocationOperation? OwningInvocation(IOperation operation)
    {
        ArgumentNullGuard.NotNull(operation, nameof(operation));
        if (operation is IExpressionStatementOperation statement)
        { operation = statement.Operation; }
        // CFG lowering separates receivers and branching arguments from their
        // invocation. Recover the enclosing source call so they are omitted
        // together, without crossing into an enclosing callable's body.
        var sourceSyntax = operation.Syntax;
        SyntaxNode? callSyntax = null;
        for (var syntax = sourceSyntax; syntax != null; syntax = syntax.Parent)
        {
            if (syntax is ConditionalAccessExpressionSyntax or InvocationExpressionSyntax)
            {
                callSyntax = syntax;
            }
            // A lowered delegate creation can use the lambda's own syntax;
            // only operations inside its body establish a callable boundary.
            if (syntax is StatementSyntax or MemberDeclarationSyntax ||
                syntax is AnonymousFunctionExpressionSyntax &&
                !(ReferenceEquals(syntax, operation.Syntax) &&
                    operation is IDelegateCreationOperation or IFlowCaptureOperation or
                        IFlowAnonymousFunctionOperation))
            {
                break;
            }
        }
        if (callSyntax != null &&
            (!ReferenceEquals(callSyntax, operation.Syntax) ||
                operation is not (IInvocationOperation or IConditionalAccessOperation)) &&
            SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
                _compilation, callSyntax.SyntaxTree).GetOperation(callSyntax)
                is { } sourceCall)
        {
            operation = sourceCall;
        }
        while (operation is IConditionalAccessOperation conditional)
        {
            operation = conditional.WhenNotNull;
        }
        if (operation is not IInvocationOperation invocation)
        {
            return null;
        }
        return invocation;
    }

    private ImmutableArray<string> GetConditionalSymbols(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var symbols = ImmutableArray.CreateBuilder<string>();
        for (IMethodSymbol? current = method; current != null; current = current.OverriddenMethod)
        {
            var declaration = ConditionalDeclaration(current);
            if (declaration.DeclaringSyntaxReferences.Length != 0)
            {
                foreach (var attribute in declaration.GetAttributes())
                {
                    if (attribute.AttributeClass is not
                        { Name: "ConditionalAttribute", ContainingSymbol: INamespaceSymbol owner } ||
                        owner.Name != "Diagnostics" || owner.ContainingNamespace is not
                        { Name: "System", ContainingNamespace.IsGlobalNamespace: true } ||
                        attribute.AttributeConstructor == null ||
                        attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax ||
                        syntax.ArgumentList?.Arguments.Count(argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (argument.NameEquals == null)) != 1 ||
                        attribute.ConstructorArguments.IsDefaultOrEmpty)
                    { continue; }
                    // Source early decoding counts supplied arguments, even when
                    // the bound constructor has object or optional parameters.
                    var argument = attribute.ConstructorArguments[0];
                    if (argument.Kind == TypedConstantKind.Primitive &&
                        argument.Type?.SpecialType == SpecialType.System_String && argument.Value is string symbol)
                    { symbols.Add(symbol); }
                }
            }
            else
            {
                AddMetadataConditionalSymbols(declaration, symbols);
            }
        }
        return symbols.ToImmutable();
    }

    private IMethodSymbol ConditionalDeclaration(IMethodSymbol method)
    {
        var declaration = method.OriginalDefinition;
        if (declaration.ContainingType.NativeIntegerUnderlyingType is not { } underlying)
        { return declaration; }
        return ConditionalMethodIdentityPolicy.ResolveNativeDeclaration(
            _compilation, declaration, underlying);
    }

    private static void AddMetadataConditionalSymbols(IMethodSymbol method, ImmutableArray<string>.Builder symbols)
    {
        // Borrow the original declaring module; no compilation-owned metadata
        // is disposed. Unavailable authority must propagate, not guess emission.
        symbols = RepositoryParameterGuard.NotNull(symbols, nameof(symbols));
        var metadata = method.ContainingModule.GetMetadata() ??
            throw new InvalidOperationException("Conditional method metadata is unavailable.");
        var reader = metadata.GetMetadataReader();
        var handle = ConditionalMethodIdentityPolicy.GetMethodDefinitionHandle(reader, method.MetadataToken);
        ConditionalMetadataSymbolReader.AppendSymbols(reader, handle, symbols);
    }

    private static TValue GetOrAdd<TKey, TValue>(
        ref ImmutableDictionary<TKey, TValue> cache,
        TKey key,
        Func<TKey, TValue> create)
        where TKey : notnull
    {
        RepositoryParameterGuard.NotNull(cache, nameof(cache));
        ArgumentNullGuard.NotNull(key, nameof(key));
        create = RepositoryParameterGuard.NotNull(create, nameof(create));
        return ImmutableInterlocked.GetOrAdd(ref cache, key, create);
    }

    internal static bool IsUnimplementedPartial(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return method.PartialDefinitionPart == null &&
            method.PartialImplementationPart == null &&
            method.DeclaringSyntaxReferences.Any(reference =>
                reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.GetSyntax() is MethodDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                declaration.Body == null &&
                declaration.ExpressionBody == null));
    }
}
