using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class InvocationEmissionPolicy(Compilation compilation)
{
    private readonly INamedTypeSymbol? _conditionalAttribute =
        compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.ConditionalAttribute);
    // One policy is shared by every concurrent analyzer callback on a
    // compilation session, so the caches are guarded.  Values are pure
    // functions of their keys: compute outside the gate and let the last
    // writer store the identical result.
    private readonly object _cacheGate = new();
    private readonly Dictionary<SyntaxTree, ImmutableHashSet<string>>
        _definedPreprocessorSymbols = [];
    private readonly Dictionary<IMethodSymbol, bool>
        _unimplementedPartials = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, ImmutableArray<string>>
        _conditionalSymbols = new(SymbolEqualityComparer.Default);

    internal bool IsElided(IOperation operation)
    {
        // CFG lowering separates receivers and branching arguments from their
        // invocation. Recover the enclosing source call so they are omitted
        // together, without crossing into an enclosing callable's body.
        SyntaxNode? callSyntax = null;
        for (var syntax = operation.Syntax; syntax != null; syntax = syntax.Parent)
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
                compilation, callSyntax.SyntaxTree).GetOperation(callSyntax)
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
            return false;
        }
        var target = invocation.TargetMethod.ReducedFrom ??
            invocation.TargetMethod;
        var isUnimplementedPartial = GetOrAdd(
            _unimplementedPartials,
            target,
            IsUnimplementedPartial);
        if (isUnimplementedPartial)
        {
            return true;
        }

        if (_conditionalAttribute == null ||
            invocation.Syntax.SyntaxTree.Options is not CSharpParseOptions)
        {
            return false;
        }
        var conditionalSymbols = GetOrAdd(
            _conditionalSymbols,
            target,
            method =>
            {
                // Overrides inherit the conditional symbols of the original
                // virtual declaration, even though GetAttributes is local.
                while (method.OverriddenMethod is { } overridden)
                {
                    method = overridden;
                }
                return method.GetAttributes()
                    .Where(attribute => SymbolEqualityComparer.Default.Equals(
                        attribute.AttributeClass?.OriginalDefinition,
                        _conditionalAttribute.OriginalDefinition))
                    .Select(attribute =>
                        attribute.ConstructorArguments.Length == 1
                            ? attribute.ConstructorArguments[0].Value as string
                            : null)
                    .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
                    .Select(static symbol => symbol!)
                    .ToImmutableArray();
            });
        if (conditionalSymbols.IsDefaultOrEmpty)
        {
            return false;
        }
        var definedSymbols = GetOrAdd(
            _definedPreprocessorSymbols,
            invocation.Syntax.SyntaxTree,
            static tree => CSharpPreprocessorSymbols.GetDefined(tree));
        return conditionalSymbols.All(symbol =>
            !definedSymbols.Contains(symbol));
    }

    private TValue GetOrAdd<TKey, TValue>(
        Dictionary<TKey, TValue> cache,
        TKey key,
        Func<TKey, TValue> create)
        where TKey : notnull
    {
        lock (_cacheGate)
        {
            if (cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var value = create(key);
        lock (_cacheGate)
        {
            cache[key] = value;
        }

        return value;
    }

    internal static bool IsUnimplementedPartial(IMethodSymbol method)
    {
        return method.PartialDefinitionPart == null &&
            method.PartialImplementationPart == null &&
            method.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is MethodDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                declaration.Body == null &&
                declaration.ExpressionBody == null);
    }
}
