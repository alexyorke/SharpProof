namespace SharpProof.Symbolic;

[Flags]
internal enum ExecutionRootPolicy
{
    None = 0,
    Callable = 1 << 0,
    ExpressionBodiedPropertyOrIndexer = 1 << 1,
    Initializer = 1 << 2,
    GlobalStatement = 1 << 3,
    SyntaxTreeRootFallback = 1 << 4
}

internal enum ExpressionCastUnwrapPolicy
{
    None,
    NullableOnly,
    All
}

internal static class CSharpSyntaxFacts
{
    public static IEnumerable<SyntaxNode> DescendantNodesInExecution(
        SyntaxNode root,
        bool includeSelf = true,
        bool includeNestedCallables = false)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));

        bool DescendIntoChildren(SyntaxNode candidate)
        {
            return includeNestedCallables ||
                   ReferenceEquals(candidate, root) ||
                   !IsNestedLocalCallableBoundary(candidate);
        }

        return includeSelf
            ? root.DescendantNodesAndSelf(descendIntoTrivia: false, descendIntoChildren: DescendIntoChildren)
            : root.DescendantNodes(descendIntoTrivia: false, descendIntoChildren: DescendIntoChildren);
    }

    public static bool IsNestedLocalCallableBoundary(SyntaxNode node)
    {
        return node is AnonymousFunctionExpressionSyntax ||
               node is LocalFunctionStatementSyntax;
    }

    public static StatementSyntax? GetContainingLoopBody(SyntaxNode node)
    {
        return node.Ancestors().Select(static ancestor => ancestor switch
            {
                WhileStatementSyntax whileStatement => whileStatement.Statement,
                DoStatementSyntax doStatement => doStatement.Statement,
                ForStatementSyntax forStatement => forStatement.Statement,
                ForEachStatementSyntax forEachStatement => forEachStatement.Statement,
                ForEachVariableStatementSyntax forEachVariable => forEachVariable.Statement,
                _ => null
            })
            .FirstOrDefault(body => body?.Span.Contains(node.SpanStart) == true);
    }

    public static bool IsCallableBoundary(SyntaxNode node)
    {
        return node is MethodDeclarationSyntax or
            ConstructorDeclarationSyntax or
            DestructorDeclarationSyntax or
            OperatorDeclarationSyntax or
            ConversionOperatorDeclarationSyntax or
            AccessorDeclarationSyntax or
            LocalFunctionStatementSyntax or
            AnonymousFunctionExpressionSyntax;
    }

    public static ITypeSymbol? GetExpressionType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return typeInfo.ConvertedType ?? typeInfo.Type;
    }

    public static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }

    internal static bool IsMemberOrQualifiedNameRightSide(IdentifierNameSyntax identifier)
    {
        return identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
               ReferenceEquals(memberAccess.Name, identifier) ||
               identifier.Parent is QualifiedNameSyntax qualifiedName &&
               ReferenceEquals(qualifiedName.Right, identifier);
    }

    public static bool TryGetExpressionBody(SyntaxNode node, out ExpressionSyntax expression)
    {
        expression = node switch
        {
            MethodDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } body } => body,
            ConstructorDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            OperatorDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            ConversionOperatorDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            AccessorDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            PropertyDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            IndexerDeclarationSyntax { ExpressionBody.Expression: { } body } => body,
            _ => null!
        };
        return expression != null;
    }

    public static BlockSyntax? GetBlockBody(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => method.Body,
            LocalFunctionStatementSyntax local => local.Body,
            ConstructorDeclarationSyntax constructor => constructor.Body,
            DestructorDeclarationSyntax destructor => destructor.Body,
            OperatorDeclarationSyntax op => op.Body,
            ConversionOperatorDeclarationSyntax conversion => conversion.Body,
            AccessorDeclarationSyntax accessor => accessor.Body,
            _ => null
        };
    }

    public static bool IsThrowOnlyStatement(StatementSyntax statement)
    {
        return statement is ThrowStatementSyntax ||
               statement is BlockSyntax { Statements.Count: 1 } block &&
               block.Statements[0] is ThrowStatementSyntax;
    }

    internal static bool TryGetCompoundAssignmentBinaryKind(SyntaxKind assignmentKind, out SyntaxKind binaryKind)
    {
        binaryKind = assignmentKind switch
        {
            SyntaxKind.AddAssignmentExpression => SyntaxKind.AddExpression,
            SyntaxKind.SubtractAssignmentExpression => SyntaxKind.SubtractExpression,
            SyntaxKind.MultiplyAssignmentExpression => SyntaxKind.MultiplyExpression,
            SyntaxKind.DivideAssignmentExpression => SyntaxKind.DivideExpression,
            SyntaxKind.ModuloAssignmentExpression => SyntaxKind.ModuloExpression,
            SyntaxKind.AndAssignmentExpression => SyntaxKind.BitwiseAndExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression => SyntaxKind.ExclusiveOrExpression,
            SyntaxKind.OrAssignmentExpression => SyntaxKind.BitwiseOrExpression,
            SyntaxKind.LeftShiftAssignmentExpression => SyntaxKind.LeftShiftExpression,
            SyntaxKind.RightShiftAssignmentExpression => SyntaxKind.RightShiftExpression,
            SyntaxKind.UnsignedRightShiftAssignmentExpression => SyntaxKind.UnsignedRightShiftExpression,
            SyntaxKind.CoalesceAssignmentExpression => SyntaxKind.CoalesceExpression,
            _ => SyntaxKind.None
        };
        return binaryKind != SyntaxKind.None;
    }

    internal static bool TryGetIncrementOrDecrementOperand(
        ExpressionSyntax expression,
        out ExpressionSyntax operand,
        out int delta)
    {
        expression = UnwrapParenthesesAndNullableSuppression(expression);
        operand = expression switch
        {
            PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                                                    prefix.IsKind(SyntaxKind.PreDecrementExpression) => prefix.Operand,
            PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                                                      postfix.IsKind(SyntaxKind.PostDecrementExpression) => postfix.Operand,
            _ => null!
        };
        delta = expression.IsKind(SyntaxKind.PreIncrementExpression) ||
                expression.IsKind(SyntaxKind.PostIncrementExpression) ? 1 : -1;
        return operand != null;
    }

    public static bool IsNullLiteral(ExpressionSyntax expression) =>
        UnwrapParentheses(expression).IsKind(SyntaxKind.NullLiteralExpression);

    public static bool TryGetNullPatternPolarity(PatternSyntax pattern, out bool matchesNonNull)
    {
        if (pattern is ConstantPatternSyntax { Expression: var expression } && IsNullLiteral(expression))
        {
            matchesNonNull = false;
            return true;
        }

        if (pattern is UnaryPatternSyntax unaryPattern &&
            unaryPattern.IsKind(SyntaxKind.NotPattern) &&
            TryGetNullPatternPolarity(unaryPattern.Pattern, out var nestedMatchesNonNull))
        {
            matchesNonNull = !nestedMatchesNonNull;
            return true;
        }

        matchesNonNull = false;
        return false;
    }

    public static SyntaxNode GetContainingExecutionRoot(SyntaxNode node)
    {
        return GetContainingExecutionRoot(
            node,
            ExecutionRootPolicy.Callable | ExecutionRootPolicy.SyntaxTreeRootFallback)!;
    }

    public static SyntaxNode? GetContainingExecutionRoot(
        SyntaxNode node,
        ExecutionRootPolicy policy)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        foreach (var candidate in node.AncestorsAndSelf())
        {
            if ((policy & ExecutionRootPolicy.Callable) != 0 && IsCallableBoundary(candidate))
                return candidate;
            if ((policy & ExecutionRootPolicy.ExpressionBodiedPropertyOrIndexer) != 0 &&
                candidate is PropertyDeclarationSyntax { ExpressionBody: not null } or
                    IndexerDeclarationSyntax { ExpressionBody: not null })
                return candidate;
            if ((policy & ExecutionRootPolicy.Initializer) != 0 && candidate is EqualsValueClauseSyntax)
                return candidate;
            if ((policy & ExecutionRootPolicy.GlobalStatement) != 0 && candidate is GlobalStatementSyntax)
                return candidate;
        }

        return (policy & ExecutionRootPolicy.SyntaxTreeRootFallback) != 0
            ? node.SyntaxTree.GetRoot()
            : null;
    }

    public static IEnumerable<(BlockSyntax Block, StatementSyntax ContainingStatement)> EnumerateContainingBlocks(
        SyntaxNode node,
        bool stopAtExecutionRoot = false)
    {
        var executionRoot = stopAtExecutionRoot ? GetContainingExecutionRoot(node) : null;
        for (var current = node; current != null; current = current.Parent)
        {
            if (current is StatementSyntax statement && statement.Parent is BlockSyntax block)
                yield return (block, statement);

            if (ReferenceEquals(current, executionRoot)) yield break;
        }
    }

    internal static ExpressionSyntax UnwrapParenthesesAndNullableSuppression(ExpressionSyntax expression) =>
        UnwrapExpression(expression, ExpressionCastUnwrapPolicy.None);

    internal static ExpressionSyntax UnwrapExpression(
        ExpressionSyntax expression,
        ExpressionCastUnwrapPolicy castPolicy)
    {
        while (true)
        {
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
                continue;
            }

            if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                expression = postfixUnary.Operand;
                continue;
            }

            if (expression is CastExpressionSyntax castExpression &&
                (castPolicy == ExpressionCastUnwrapPolicy.All ||
                 castPolicy == ExpressionCastUnwrapPolicy.NullableOnly &&
                 castExpression.Type is NullableTypeSyntax))
            {
                expression = castExpression.Expression;
                continue;
            }

            return expression;
        }
    }

    public static IEnumerable<ExpressionSyntax> GetExplicitArraySizeExpressions(
        ArrayCreationExpressionSyntax arrayCreation)
    {
        foreach (var rankSpecifier in arrayCreation.Type.RankSpecifiers)
            foreach (var sizeExpression in rankSpecifier.Sizes)
                if (!sizeExpression.IsKind(SyntaxKind.OmittedArraySizeExpression))
                    yield return sizeExpression;
    }

    public static ExpressionSyntax UnwrapConditionExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            if (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
            {
                expression = parenthesizedExpression.Expression;
                continue;
            }

            if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                expression = postfixUnary.Operand;
                continue;
            }

            if (expression is CheckedExpressionSyntax checkedExpression &&
                checkedExpression.IsKind(SyntaxKind.CheckedExpression))
            {
                expression = checkedExpression.Expression;
                continue;
            }

            return expression;
        }
    }
}
