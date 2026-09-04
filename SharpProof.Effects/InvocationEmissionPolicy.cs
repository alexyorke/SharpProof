using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class InvocationEmissionPolicy(Compilation compilation)
{
    private readonly INamedTypeSymbol? _conditionalAttribute =
        compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.ConditionalAttribute);
    private readonly Dictionary<SyntaxTree, ImmutableHashSet<string>>
        _definedPreprocessorSymbols = [];
    private readonly Dictionary<IMethodSymbol, bool>
        _unimplementedPartials = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, ImmutableArray<string>>
        _conditionalSymbols = new(SymbolEqualityComparer.Default);

    internal bool IsElided(IInvocationOperation invocation)
    {
        var target = invocation.TargetMethod.ReducedFrom ??
            invocation.TargetMethod;
        if (!_unimplementedPartials.TryGetValue(target, out var isUnimplementedPartial))
        {
            isUnimplementedPartial = IsUnimplementedPartial(target);
            _unimplementedPartials.Add(target, isUnimplementedPartial);
        }
        if (isUnimplementedPartial)
        {
            return true;
        }

        if (_conditionalAttribute == null ||
            invocation.Syntax.SyntaxTree.Options is not CSharpParseOptions)
        {
            return false;
        }
        if (!_conditionalSymbols.TryGetValue(target, out var conditionalSymbols))
        {
            conditionalSymbols = target.GetAttributes()
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
            _conditionalSymbols.Add(target, conditionalSymbols);
        }
        if (conditionalSymbols.IsDefaultOrEmpty)
        {
            return false;
        }
        if (!_definedPreprocessorSymbols.TryGetValue(
                invocation.Syntax.SyntaxTree,
                out var definedSymbols))
        {
            definedSymbols = CSharpPreprocessorSymbols.GetDefined(
                invocation.Syntax.SyntaxTree);
            _definedPreprocessorSymbols.Add(
                invocation.Syntax.SyntaxTree,
                definedSymbols);
        }
        return conditionalSymbols.All(symbol =>
            !definedSymbols.Contains(symbol));
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
