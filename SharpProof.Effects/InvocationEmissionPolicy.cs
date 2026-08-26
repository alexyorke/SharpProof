using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class InvocationEmissionPolicy(Compilation compilation)
{
    private readonly INamedTypeSymbol? _conditionalAttribute =
        compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.ConditionalAttribute);
    private readonly ConcurrentDictionary<SyntaxTree, ImmutableHashSet<string>>
        _definedPreprocessorSymbols = [];

    internal bool IsElided(IInvocationOperation invocation)
    {
        var target = invocation.TargetMethod.ReducedFrom ??
            invocation.TargetMethod;
        if (IsUnimplementedPartial(target))
        {
            return true;
        }

        if (_conditionalAttribute == null ||
            invocation.Syntax.SyntaxTree.Options is not CSharpParseOptions)
        {
            return false;
        }
        var conditionalSymbols = target.GetAttributes()
            .Where(attribute => SymbolEqualityComparer.Default.Equals(
                attribute.AttributeClass?.OriginalDefinition,
                _conditionalAttribute.OriginalDefinition))
            .Select(attribute =>
                attribute.ConstructorArguments.Length == 1
                    ? attribute.ConstructorArguments[0].Value as string
                    : null)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .ToImmutableArray();
        if (conditionalSymbols.IsDefaultOrEmpty)
        {
            return false;
        }
        var definedSymbols = _definedPreprocessorSymbols.GetOrAdd(
            invocation.Syntax.SyntaxTree,
            static tree => CSharpPreprocessorSymbols.GetDefined(tree));
        return conditionalSymbols.All(symbol =>
            !definedSymbols.Contains(symbol!));
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
