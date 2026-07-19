namespace SharpProof.Symbolic;

internal static class SymbolicProgramPointMetadata
{
    public static string? GetContainingMethodName(SyntaxNode node)
    {
        foreach (var ancestor in node.AncestorsAndSelf())
            switch (ancestor)
            {
                case MethodDeclarationSyntax method:
                    return method.Identifier.ValueText;
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction.Identifier.ValueText;
                case ConstructorDeclarationSyntax constructor:
                    return constructor.Identifier.ValueText;
                case DestructorDeclarationSyntax destructor:
                    return "~" + destructor.Identifier.ValueText;
                case OperatorDeclarationSyntax operatorDeclaration:
                    return "operator " + operatorDeclaration.OperatorToken.ValueText;
                case ConversionOperatorDeclarationSyntax conversionOperator:
                    return "operator " + conversionOperator.Type;
            }

        return null;
    }

    public static string GetProgramPointKind(SyntaxNode node)
    {
        return node switch
        {
            StatementSyntax => SymbolicProgramPointKinds.Statement,
            ExpressionSyntax => SymbolicProgramPointKinds.Expression,
            _ => SymbolicProgramPointKinds.Other
        };
    }
}