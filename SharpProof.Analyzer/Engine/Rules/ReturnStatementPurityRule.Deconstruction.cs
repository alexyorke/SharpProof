using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class ReturnStatementPurityRule : IPurityRule
{
    private static bool TryGetDeconstructionElementInitializer(
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax initializerSyntax,
        out SyntaxNode declarationSyntax)
    {
        if (TryGetPriorDeconstructionAssignmentElementInitializer(
                localSymbol,
                observationSyntax,
                semanticModel,
                cancellationToken,
                out initializerSyntax,
                out declarationSyntax))
            return true;

        var designation = localSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<SingleVariableDesignationSyntax>()
            .FirstOrDefault();
        if (designation == null ||
            !TryGetDeconstructionDesignationPath(designation, out var path) ||
            designation.FirstAncestorOrSelf<AssignmentExpressionSyntax>() is not { } assignment ||
            !TryGetTupleElementExpression(assignment.Right, path, out initializerSyntax) ||
            semanticModel.GetDeclaredSymbol(designation, cancellationToken) is not ILocalSymbol declaredSymbol ||
            !SymbolEqualityComparer.Default.Equals(declaredSymbol, localSymbol))
        {
            initializerSyntax = null!;
            declarationSyntax = null!;
            return false;
        }

        declarationSyntax = assignment;
        return true;
    }

    private static bool TryGetPriorDeconstructionAssignmentElementInitializer(
        ILocalSymbol localSymbol,
        SyntaxNode observationSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax initializerSyntax,
        out SyntaxNode declarationSyntax)
    {
        initializerSyntax = null!;
        declarationSyntax = null!;
        var containingBlock = observationSyntax.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null) return false;

        var localDeclarationStart = localSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken).SpanStart)
            .DefaultIfEmpty(int.MinValue)
            .Min();

        foreach (var assignment in containingBlock.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assignment.SpanStart <= localDeclarationStart ||
                assignment.SpanStart >= observationSyntax.SpanStart ||
                !TryGetDeconstructionAssignmentTargetPath(
                    assignment.Left,
                    localSymbol,
                    semanticModel,
                    cancellationToken,
                    out var path) ||
                !TryGetTupleElementExpression(assignment.Right, path, out var candidateInitializer))
                continue;

            initializerSyntax = candidateInitializer;
            declarationSyntax = assignment;
        }

        return initializerSyntax != null;
    }

    private static bool TryGetDeconstructionAssignmentTargetPath(
        ExpressionSyntax target,
        ILocalSymbol localSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ImmutableArray<int> path)
    {
        var builder = ImmutableArray.CreateBuilder<int>();
        if (TryGetDeconstructionAssignmentTargetPathCore(
                CSharpSyntaxFacts.UnwrapParentheses(target),
                localSymbol,
                semanticModel,
                cancellationToken,
                builder))
        {
            path = builder.ToImmutable();
            return path.Length > 0;
        }

        path = default;
        return false;
    }

    private static bool TryGetDeconstructionAssignmentTargetPathCore(
        ExpressionSyntax target,
        ILocalSymbol localSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ImmutableArray<int>.Builder path)
    {
        target = CSharpSyntaxFacts.UnwrapParentheses(target);
        if (target is DeclarationExpressionSyntax declarationExpression)
            return TryGetDeconstructionDesignationPathForLocal(
                declarationExpression.Designation,
                localSymbol,
                semanticModel,
                cancellationToken,
                path);

        if (target is IdentifierNameSyntax identifierName &&
            semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is ILocalSymbol targetLocal &&
            SymbolEqualityComparer.Default.Equals(targetLocal, localSymbol))
            return true;

        if (target is TupleExpressionSyntax tuple)
            for (var i = 0; i < tuple.Arguments.Count; i++)
            {
                var count = path.Count;
                path.Add(i);
                if (TryGetDeconstructionAssignmentTargetPathCore(
                        tuple.Arguments[i].Expression,
                        localSymbol,
                        semanticModel,
                        cancellationToken,
                        path))
                    return true;

                path.RemoveRange(count, path.Count - count);
            }

        return false;
    }

    private static bool TryGetDeconstructionDesignationPathForLocal(
        VariableDesignationSyntax designation,
        ILocalSymbol localSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ImmutableArray<int>.Builder path)
    {
        if (designation is SingleVariableDesignationSyntax singleVariable &&
            semanticModel.GetDeclaredSymbol(singleVariable, cancellationToken) is ILocalSymbol declaredLocal &&
            SymbolEqualityComparer.Default.Equals(declaredLocal, localSymbol))
            return true;

        if (designation is ParenthesizedVariableDesignationSyntax parenthesized)
            for (var i = 0; i < parenthesized.Variables.Count; i++)
            {
                var count = path.Count;
                path.Add(i);
                if (TryGetDeconstructionDesignationPathForLocal(
                        parenthesized.Variables[i],
                        localSymbol,
                        semanticModel,
                        cancellationToken,
                        path))
                    return true;

                path.RemoveRange(count, path.Count - count);
            }

        return false;
    }

    private static bool TryGetDeconstructionDesignationPath(
        SingleVariableDesignationSyntax designation,
        out ImmutableArray<int> path)
    {
        var builder = ImmutableArray.CreateBuilder<int>();
        VariableDesignationSyntax current = designation;
        while (current.Parent is ParenthesizedVariableDesignationSyntax parenthesized)
        {
            var index = IndexOfDesignation(parenthesized, current);
            if (index < 0)
            {
                path = default;
                return false;
            }

            builder.Insert(0, index);
            current = parenthesized;
        }

        path = builder.ToImmutable();
        return path.Length > 0;
    }

    private static int IndexOfDesignation(
        ParenthesizedVariableDesignationSyntax parenthesized,
        VariableDesignationSyntax designation)
    {
        for (var i = 0; i < parenthesized.Variables.Count; i++)
            if (ReferenceEquals(parenthesized.Variables[i], designation))
                return i;

        return -1;
    }

    private static bool TryGetTupleElementExpression(
        ExpressionSyntax tupleExpression,
        ImmutableArray<int> path,
        out ExpressionSyntax elementExpression)
    {
        elementExpression = tupleExpression;
        foreach (var index in path)
        {
            elementExpression = CSharpSyntaxFacts.UnwrapParentheses(elementExpression);
            if (elementExpression is not TupleExpressionSyntax tuple ||
                index < 0 ||
                index >= tuple.Arguments.Count)
            {
                elementExpression = null!;
                return false;
            }

            elementExpression = tuple.Arguments[index].Expression;
        }

        return true;
    }

}
