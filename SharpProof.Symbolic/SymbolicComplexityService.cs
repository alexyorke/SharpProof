using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal sealed class SymbolicComplexityService
{
    public SymbolicComplexityResult Query(
        SymbolicQueryContext request,
        CancellationToken cancellationToken)
    {
        return SymbolicMethodLikeQueryDispatcher.Execute(
            request,
            SymbolicSourceCompilationKind.Complexity,
            "Complexity source kind is not supported.",
            "Complexity queries support point, position, line, or node targets only.",
            "Node complexity queries require a node target.",
            static node => SymbolicMethodLikeDeclaration.IsSupported(
                node,
                includeAnonymousFunctions: true,
                includeDestructors: true),
            ResolveMethodLikeDeclaration,
            ExecuteAnalysis,
            cancellationToken);
    }

    private static SymbolicComplexityResult ExecuteAnalysis(
        ResolvedComplexityTarget target,
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var summary = new SymbolicComplexityAnalysisSession(compilation, cancellationToken).Analyze(target);
        return SymbolicComplexityResultProjector.Project(target, summary, cancellationToken);
    }

    private static ResolvedComplexityTarget ResolveMethodLikeDeclaration(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var bodyNode = SymbolicMethodSourceResolver.GetBodyNode(declaration);
        if (bodyNode == null)
            throw new ArgumentException("The requested method-like declaration does not have a body.");

        var symbol = SymbolicMethodLikeDeclaration.GetMethodSymbol(declaration, semanticModel, cancellationToken);
        if (symbol == null)
            throw new ArgumentException("Could not resolve the symbol for the requested method-like body.");

        var syntaxTree = declaration.SyntaxTree;
        var span = SymbolicSourceLocation.GetNodeSourceSpan(syntaxTree, declaration.Span, cancellationToken);
        var methodName = GetMethodName(symbol, declaration);
        return new ResolvedComplexityTarget(
            syntaxTree,
            semanticModel,
            declaration,
            bodyNode,
            symbol,
            syntaxTree.FilePath ?? string.Empty,
            methodName,
            symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            GetDeclarationKind(declaration),
            declaration.Span.Start,
            declaration.Span.End,
            span.StartLine,
            span.StartColumn,
            span.EndLine,
            span.EndColumn);
    }

    private static string GetDeclarationKind(SyntaxNode declaration)
    {
        return declaration switch
        {
            MethodDeclarationSyntax => "method",
            ConstructorDeclarationSyntax => "constructor",
            DestructorDeclarationSyntax => "destructor",
            OperatorDeclarationSyntax => "operator",
            ConversionOperatorDeclarationSyntax => "conversion_operator",
            AccessorDeclarationSyntax accessor => "accessor:" + accessor.Keyword.ValueText,
            PropertyDeclarationSyntax => "property_getter",
            IndexerDeclarationSyntax => "indexer_getter",
            LocalFunctionStatementSyntax => "local_function",
            AnonymousFunctionExpressionSyntax => "anonymous_function",
            _ => declaration.Kind().ToString()
        };
    }

    private static string GetMethodName(IMethodSymbol symbol, SyntaxNode declaration)
    {
        if (!string.IsNullOrWhiteSpace(symbol.Name)) return symbol.Name;

        return declaration switch
        {
            AccessorDeclarationSyntax accessor => accessor.Keyword.ValueText,
            AnonymousFunctionExpressionSyntax => "anonymous_function",
            _ => declaration.Kind().ToString()
        };
    }
}
