namespace SharpProof.Symbolic;

internal sealed class SymbolicComplexityLoopModel(
    SymbolicComplexityCostModel _costModel,
    CancellationToken _cancellationToken) {
    internal bool TryGetForLoopBound(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod,
        out LoopBoundInfo bound) {
        bound = default;
        if (!TryGetForLoopVariable(forStatement, semanticModel, out var loopSymbol, out var initializerExpression))
            return false;

        if (!_costModel.TryGetIntegralConstant(initializerExpression, semanticModel, out _)) return false;

        if (forStatement.Condition is not BinaryExpressionSyntax condition ||
            !TryParseLoopCondition(condition, loopSymbol, semanticModel, currentMethod, out var direction,
                out var boundCost, out var boundExpressionText, out var dependentSymbols))
            return false;

        if (!TryParseForLoopStep(forStatement, loopSymbol, semanticModel, out var stepDirection) ||
            stepDirection != direction)
            return false;

        if (dependentSymbols.Any(symbol =>
                IsSymbolMutatedInStatement(symbol, forStatement.Statement, semanticModel)) ||
            IsSymbolMutatedInStatement(loopSymbol, forStatement.Statement, semanticModel))
            return false;

        bound = new LoopBoundInfo(boundCost, boundExpressionText);
        return true;
    }

    internal bool TryGetWhileLikeBound(
        ExpressionSyntax conditionExpression,
        StatementSyntax loopBody,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod,
        out LoopBoundInfo bound) {
        bound = default;
        if (conditionExpression is not BinaryExpressionSyntax condition) return false;

        if (!TryGetLoopConditionVariable(condition, semanticModel, out var loopSymbol)) return false;

        if (!TryParseLoopCondition(condition, loopSymbol, semanticModel, currentMethod, out var direction,
                out var boundCost, out var boundExpressionText, out var dependentSymbols)) return false;

        var updates = GetRecognizedLoopUpdates(loopBody, loopSymbol, semanticModel);
        if (updates.Count != 1 || updates[0] != direction) return false;

        if (dependentSymbols.Any(symbol => IsSymbolMutatedInStatement(symbol, loopBody, semanticModel)) ||
            !IsSymbolMutatedInStatement(loopSymbol, loopBody, semanticModel, true))
            return false;

        bound = new LoopBoundInfo(boundCost, boundExpressionText);
        return true;
    }

    internal bool TryGetForeachBound(
        SyntaxNode collectionSyntaxNode,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod,
        out LoopBoundInfo bound) {
        if (collectionSyntaxNode is not ExpressionSyntax collectionExpression ||
            !_costModel.TryCreate(
                collectionExpression,
                semanticModel,
                currentMethod,
                CostProjection.LengthOrCount,
                false,
                out var cost)) {
            bound = default;
            return false;
        }

        bound = new LoopBoundInfo(cost, collectionExpression.ToString());
        return true;
    }

    private bool TryGetForLoopVariable(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        out ISymbol loopSymbol,
        out ExpressionSyntax initializerExpression) {
        if (forStatement.Declaration is { Variables.Count: 1 } declaration &&
            declaration.Variables[0].Initializer != null &&
            semanticModel.GetDeclaredSymbol(declaration.Variables[0], _cancellationToken) is ISymbol declaredSymbol) {
            loopSymbol = declaredSymbol;
            initializerExpression = declaration.Variables[0].Initializer!.Value;
            return true;
        }

        if (forStatement.Initializers.Count == 1 &&
            forStatement.Initializers[0] is AssignmentExpressionSyntax assignment &&
            semanticModel.GetSymbolInfo(assignment.Left, _cancellationToken).Symbol is { } assignedSymbol &&
            assignedSymbol is ILocalSymbol or IParameterSymbol) {
            loopSymbol = assignedSymbol;
            initializerExpression = assignment.Right;
            return true;
        }

        loopSymbol = null!;
        initializerExpression = null!;
        return false;
    }

    private bool TryGetLoopConditionVariable(
        BinaryExpressionSyntax condition,
        SemanticModel semanticModel,
        out ISymbol symbol) {
        symbol = semanticModel.GetSymbolInfo(
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition.Left),
            _cancellationToken).Symbol!;
        if (symbol is ILocalSymbol or IParameterSymbol) return true;

        symbol = semanticModel.GetSymbolInfo(
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition.Right),
            _cancellationToken).Symbol!;
        return symbol is ILocalSymbol or IParameterSymbol;
    }

    private bool TryParseLoopCondition(
        BinaryExpressionSyntax condition,
        ISymbol loopSymbol,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod,
        out StepDirection direction,
        out SymbolicCostExpression boundCost,
        out string boundDescription,
        out ImmutableArray<ISymbol> dependentSymbols) {
        direction = StepDirection.Up;
        boundCost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnsupportedLoopShape);
        boundDescription = string.Empty;
        dependentSymbols = ImmutableArray<ISymbol>.Empty;

        var left = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition.Left);
        var right = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(condition.Right);
        var leftSymbol = semanticModel.GetSymbolInfo(left, _cancellationToken).Symbol;
        var rightSymbol = semanticModel.GetSymbolInfo(right, _cancellationToken).Symbol;

        ExpressionSyntax? boundExpression = null;
        if (SymbolEquals(leftSymbol, loopSymbol)) {
            direction = condition.IsKind(SyntaxKind.LessThanExpression) ||
                        condition.IsKind(SyntaxKind.LessThanOrEqualExpression)
                ? StepDirection.Up
                : condition.IsKind(SyntaxKind.GreaterThanExpression) ||
                  condition.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
                    ? StepDirection.Down
                    : StepDirection.None;
            boundExpression = right;
        }
        else if (SymbolEquals(rightSymbol, loopSymbol)) {
            direction = condition.IsKind(SyntaxKind.GreaterThanExpression) ||
                        condition.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
                ? StepDirection.Up
                : condition.IsKind(SyntaxKind.LessThanExpression) ||
                  condition.IsKind(SyntaxKind.LessThanOrEqualExpression)
                    ? StepDirection.Down
                    : StepDirection.None;
            boundExpression = left;
        }

        if (direction == StepDirection.None ||
            boundExpression == null ||
            !_costModel.TryCreate(boundExpression, semanticModel, currentMethod, CostProjection.Value, true,
                out boundCost))
            return false;

        boundDescription = boundExpression.ToString();
        dependentSymbols = GetDependentSymbols(boundExpression, semanticModel);
        return true;
    }

    private bool TryParseForLoopStep(
        ForStatementSyntax forStatement,
        ISymbol loopSymbol,
        SemanticModel semanticModel,
        out StepDirection direction) {
        direction = StepDirection.None;
        if (forStatement.Incrementors.Count != 1) return false;

        return TryParseLoopStep(forStatement.Incrementors[0], loopSymbol, semanticModel, out direction);
    }

    private bool TryParseLoopStep(
        ExpressionSyntax expression,
        ISymbol loopSymbol,
        SemanticModel semanticModel,
        out StepDirection direction) {
        direction = StepDirection.None;
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (CSharpSyntaxFacts.TryGetIncrementOrDecrementOperand(expression, out var operand, out var delta) &&
            SymbolEquals(semanticModel.GetSymbolInfo(operand, _cancellationToken).Symbol, loopSymbol)) {
            direction = delta > 0 ? StepDirection.Up : StepDirection.Down;
            return true;
        }

        switch (expression) {
            case AssignmentExpressionSyntax assignment
                when SymbolEquals(semanticModel.GetSymbolInfo(assignment.Left, _cancellationToken).Symbol,
                    loopSymbol):
                if (assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
                    _costModel.TryGetIntegralConstant(assignment.Right, semanticModel, out var addValue) &&
                    addValue > 0) {
                    direction = StepDirection.Up;
                    return true;
                }

                if (assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) &&
                    _costModel.TryGetIntegralConstant(assignment.Right, semanticModel, out var subtractValue) &&
                    subtractValue > 0) {
                    direction = StepDirection.Down;
                    return true;
                }

                if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                    assignment.Right is BinaryExpressionSyntax binaryExpression) {
                    if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                        IsReferenceToSymbol(binaryExpression.Left, loopSymbol, semanticModel) &&
                        _costModel.TryGetIntegralConstant(binaryExpression.Right, semanticModel, out var rightAdd) &&
                        rightAdd > 0) {
                        direction = StepDirection.Up;
                        return true;
                    }

                    if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                        _costModel.TryGetIntegralConstant(binaryExpression.Left, semanticModel, out var leftAdd) &&
                        leftAdd > 0 &&
                        IsReferenceToSymbol(binaryExpression.Right, loopSymbol, semanticModel)) {
                        direction = StepDirection.Up;
                        return true;
                    }

                    if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                        IsReferenceToSymbol(binaryExpression.Left, loopSymbol, semanticModel) &&
                        _costModel.TryGetIntegralConstant(binaryExpression.Right, semanticModel, out var rightSubtract) &&
                        rightSubtract > 0) {
                        direction = StepDirection.Down;
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    private bool IsSymbolMutatedInStatement(
        ISymbol symbol,
        StatementSyntax statement,
        SemanticModel semanticModel,
        bool allowRecognizedLoopUpdates = false) {
        var sawMutation = false;
        foreach (var node in statement.DescendantNodesAndSelf(static candidate =>
                     !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))) {
            if (!SymbolMutationFacts.TryGetMutationTarget(node, out var mutatedExpression) ||
                !AssignmentTargetReferencesSymbol(mutatedExpression, symbol, semanticModel))
                continue;

            if (allowRecognizedLoopUpdates &&
                node is ExpressionSyntax mutationExpression &&
                TryParseLoopStep(mutationExpression, symbol, semanticModel, out _)) {
                sawMutation = true;
                continue;
            }

            return true;
        }

        return allowRecognizedLoopUpdates ? sawMutation : false;
    }

    private bool AssignmentTargetReferencesSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel) {
        var operation = semanticModel.GetOperation(expression, _cancellationToken);
        return operation != null
            ? AssignmentTargetReferencesSymbol(operation, symbol)
            : expression is TupleExpressionSyntax tuple &&
              tuple.Arguments.Any(argument =>
                  AssignmentTargetReferencesSymbol(argument.Expression, symbol, semanticModel));
    }

    private static bool AssignmentTargetReferencesSymbol(IOperation operation, ISymbol symbol) {
        switch (operation) {
            case ILocalReferenceOperation local:
                return SymbolEquals(local.Local, symbol);
            case IParameterReferenceOperation parameter:
                return SymbolEquals(parameter.Parameter, symbol);
            case ITupleOperation tuple:
                return tuple.Elements.Any(element => AssignmentTargetReferencesSymbol(element, symbol));
            case IDeclarationExpressionOperation declaration:
                return AssignmentTargetReferencesSymbol(declaration.Expression, symbol);
            case IConversionOperation conversion:
                return AssignmentTargetReferencesSymbol(conversion.Operand, symbol);
            case IParenthesizedOperation parenthesized:
                return AssignmentTargetReferencesSymbol(parenthesized.Operand, symbol);
            default:
                return false;
        }
    }

    private List<StepDirection> GetRecognizedLoopUpdates(
        StatementSyntax loopBody,
        ISymbol loopSymbol,
        SemanticModel semanticModel) {
        var updates = new List<StepDirection>();
        foreach (var expression in loopBody.DescendantNodesAndSelf(static candidate =>
                         !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
                     .OfType<ExpressionSyntax>())
            if (TryParseLoopStep(expression, loopSymbol, semanticModel, out var direction))
                updates.Add(direction);

        return updates;
    }

    private ImmutableArray<ISymbol> GetDependentSymbols(
        ExpressionSyntax expression,
        SemanticModel semanticModel) {
        var builder = ImmutableArray.CreateBuilder<ISymbol>();
        foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            if (semanticModel.GetSymbolInfo(identifier, _cancellationToken).Symbol is ISymbol symbol &&
                builder.All(existing => !SymbolEquals(existing, symbol)))
                builder.Add(symbol);

        return builder.ToImmutable();
    }

    private bool IsReferenceToSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel) {
        return SymbolEquals(
            semanticModel.GetSymbolInfo(
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression),
                _cancellationToken).Symbol,
            symbol);
    }

    private static bool SymbolEquals(ISymbol? left, ISymbol? right) {
        return left != null &&
               right != null &&
               SymbolEqualityComparer.Default.Equals(left.OriginalDefinition, right.OriginalDefinition);
    }
}
