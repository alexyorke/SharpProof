using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class ConditionalTruthOperatorFacts
{
    internal static IMethodSymbol? Resolve(IBinaryOperation binary)
    {
        if (binary.OperatorMethod is not { } binaryOperator ||
            binary.OperatorKind is not (
                BinaryOperatorKind.ConditionalAnd or
                BinaryOperatorKind.ConditionalOr) ||
            binaryOperator.Parameters.Length == 0)
        {
            return null;
        }

        var name = binary.OperatorKind == BinaryOperatorKind.ConditionalAnd
            ? "op_False"
            : "op_True";
        var operandType = binaryOperator.Parameters[0].Type;
        var candidates = binaryOperator.ContainingType
            .GetMembers(name)
            .OfType<IMethodSymbol>()
            .Where(method =>
                method.MethodKind == MethodKind.UserDefinedOperator &&
                method.IsStatic &&
                method.ReturnType.SpecialType == SpecialType.System_Boolean &&
                method.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(
                    method.Parameters[0].Type,
                    operandType))
            .Take(2)
            .ToImmutableArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    internal static bool SkipsRightOperand(
        BinaryOperatorKind operatorKind,
        bool leftValue)
    {
        return operatorKind switch
        {
            BinaryOperatorKind.ConditionalAnd => !leftValue,
            BinaryOperatorKind.ConditionalOr => leftValue,
            _ => false
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1508:Avoid dead conditional code",
        Justification = "The analyzer does not track the nullable expression " +
            "assignment across the two declaration forms.")]
    internal static bool ReturnsConstant(
        Compilation compilation,
        IMethodSymbol method,
        out bool value)
    {
        value = false;
        if (method.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        var expression = GetReturnExpression(
            method.DeclaringSyntaxReferences[0].GetSyntax());
        if (expression == null)
        {
            return false;
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, expression.SyntaxTree);
        var constant = model.GetConstantValue(expression);
        if (constant is { HasValue: true, Value: bool result })
        {
            value = result;
            return true;
        }

        return false;
    }

    private static ExpressionSyntax? GetReturnExpression(SyntaxNode declaration)
    {
        if (declaration is not BaseMethodDeclarationSyntax method ||
            method is not (MethodDeclarationSyntax or OperatorDeclarationSyntax))
        {
            return null;
        }

        if (method.ExpressionBody?.Expression is { } expression)
        {
            return expression;
        }

        if (method.Body?.Statements.Count == 1 &&
            method.Body.Statements[0] is ReturnStatementSyntax
            { Expression: { } returned })
        {
            return returned;
        }

        if (method is OperatorDeclarationSyntax { Body: { } operatorBlock } &&
            TryGetReturnAfterHarmlessDiscards(
                operatorBlock,
                out var returnedAfterDiscards))
        {
            return returnedAfterDiscards;
        }

        return null;
    }

    private static bool TryGetReturnAfterHarmlessDiscards(
        BlockSyntax body,
        out ExpressionSyntax? expression)
    {
        expression = null;
        if (body.Statements.Count == 0 ||
            body.Statements[body.Statements.Count - 1] is not
                ReturnStatementSyntax
            { Expression: { } returned } ||
            body.Statements.Take(body.Statements.Count - 1).Any(
                static statement => statement is not ExpressionStatementSyntax
                {
                    Expression: AssignmentExpressionSyntax
                    {
                        Left: IdentifierNameSyntax
                        {
                            Identifier.ValueText: "_"
                        }
                    }
                }))
        {
            return false;
        }

        expression = returned;
        return true;
    }
}
