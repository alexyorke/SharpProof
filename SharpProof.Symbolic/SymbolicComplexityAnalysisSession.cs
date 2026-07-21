namespace SharpProof.Symbolic;
internal sealed class SymbolicComplexityAnalysisSession {
    private readonly HashSet<IMethodSymbol> _active = new(SymbolEqualityComparer.Default);

    private readonly SymbolicComplexityCallModel _callModel;
    private readonly CancellationToken _cancellationToken;
    private readonly SymbolicComplexityCostModel _costModel;
    private readonly SymbolicComplexityLoopModel _loopModel;

    private readonly Dictionary<IMethodSymbol, MethodAnalysisSummary> _summaryCache =
        new(SymbolEqualityComparer.Default);

    internal SymbolicComplexityAnalysisSession(Compilation compilation, CancellationToken cancellationToken) {
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        _cancellationToken = cancellationToken;
        _costModel = new SymbolicComplexityCostModel(cancellationToken);
        _loopModel = new SymbolicComplexityLoopModel(_costModel, cancellationToken);
        _callModel = new SymbolicComplexityCallModel(
            compilation,
            _costModel,
            AnalyzeMethod,
            cancellationToken);
    }

    public SymbolicComplexityResult Analyze(ResolvedMethodLikeTarget target) {
        var summary = AnalyzeMethod(target.MethodSymbol!, target.BodyNode!, target.SemanticModel);
        var cost = summary.Cost;
        return new SymbolicComplexityResult(
            new SymbolicComplexityInfo(
                cost.ToBigOText(target.MethodSymbol!),
                cost.ToPublicKind(),
                cost.IsConservative,
                cost.IsUnknown,
                cost.IsRecursiveUnknown),
            summary.Drivers.Distinct().ToArray(),
            summary.UnknownReasons.Where(static reason => reason != SymbolicComplexityUnknownReason.None)
                .Distinct().ToArray(),
            summary.CalleeSummaries.Distinct().ToArray());
    }

    private MethodAnalysisSummary AnalyzeMethod(
        IMethodSymbol methodSymbol,
        SyntaxNode bodyNode,
        SemanticModel semanticModel) {
        _cancellationToken.ThrowIfCancellationRequested();

        var canonical = methodSymbol.OriginalDefinition;
        if (_summaryCache.TryGetValue(canonical, out var cached)) return cached;

        if (_active.Contains(canonical))
            return SymbolicComplexityAlgebra.CreateSummary(
                SymbolicCostExpression.RecursiveUnknown(),
                Array.Empty<SymbolicComplexityDriverInfo>(),
                new[] { SymbolicComplexityUnknownReason.RecursiveCycle },
                new[] {
                    SymbolicComplexityAlgebra.CreateCalleeInfo(
                        canonical.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        SymbolicCostExpression.RecursiveUnknown(),
                        canonical)
                });

        _active.Add(canonical);
        try {
            var operation = semanticModel.GetOperation(bodyNode, _cancellationToken);
            var bodyCost = operation != null
                ? AnalyzeOperation(operation, semanticModel, canonical)
                : _callModel.AnalyzeTopLevelInvocations(bodyNode, semanticModel, canonical);
            var summary = SymbolicComplexityAlgebra.CreateSummary(
                bodyCost.Cost,
                bodyCost.Drivers,
                bodyCost.UnknownReasons,
                bodyCost.CalleeSummaries);
            _summaryCache[canonical] = summary;
            return summary;
        }
        finally {
            _active.Remove(canonical);
        }
    }

    private ComplexityArtifacts AnalyzeOperation(
        IOperation? operation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        _cancellationToken.ThrowIfCancellationRequested();

        if (operation == null) return ComplexityArtifacts.Constant;

        switch (operation) {
            case IBlockOperation block:
                return SymbolicComplexityAlgebra.CombineSequence(block.Operations.Select(child =>
                    AnalyzeOperation(child, semanticModel, currentMethod)));

            case IVariableDeclarationGroupOperation group: {
                    var parts = new List<ComplexityArtifacts>();
                    foreach (var declaration in group.Declarations)
                        foreach (var declarator in declaration.Declarators)
                            if (declarator.Initializer != null)
                                parts.Add(AnalyzeOperation(declarator.Initializer.Value, semanticModel, currentMethod));

                    return SymbolicComplexityAlgebra.CombineSequence(parts);
                }

            case IVariableDeclaratorOperation declarator:
                return declarator.Initializer == null
                    ? ComplexityArtifacts.Constant
                    : AnalyzeOperation(declarator.Initializer.Value, semanticModel, currentMethod);

            case IExpressionStatementOperation expressionStatement:
                return AnalyzeOperation(expressionStatement.Operation, semanticModel, currentMethod);

            case IReturnOperation returnOperation:
                return returnOperation.ReturnedValue != null
                    ? SymbolicComplexityAlgebra.CombineSequence(
                        new[] {
                            AnalyzeOperation(returnOperation.ReturnedValue, semanticModel, currentMethod)
                        }.Concat(returnOperation.ChildOperations
                            .Where(child => !ReferenceEquals(child, returnOperation.ReturnedValue))
                            .Select(child => AnalyzeOperation(child, semanticModel, currentMethod))))
                    : SymbolicComplexityAlgebra.CombineSequence(returnOperation.ChildOperations.Select(child =>
                        AnalyzeOperation(child, semanticModel, currentMethod)));

            case IConditionalOperation conditionalOperation:
                return AnalyzeConditionalOperation(conditionalOperation, semanticModel, currentMethod);

            case IForLoopOperation forLoopOperation:
                return AnalyzeForLoop(forLoopOperation, semanticModel, currentMethod);

            case IForEachLoopOperation forEachLoopOperation:
                return AnalyzeForEachLoop(forEachLoopOperation, semanticModel, currentMethod);

            case IWhileLoopOperation whileLoopOperation:
                return AnalyzeWhileLikeLoop(whileLoopOperation, semanticModel, currentMethod);

            case IInvocationOperation invocationOperation:
                return AnalyzeInvocation(invocationOperation, semanticModel, currentMethod);

            case IObjectCreationOperation objectCreationOperation:
                return AnalyzeObjectCreation(objectCreationOperation, semanticModel, currentMethod);

            case IPropertyReferenceOperation propertyReferenceOperation:
                return AnalyzePropertyReference(propertyReferenceOperation, semanticModel, currentMethod);

            case IArrayCreationOperation arrayCreationOperation:
                return AnalyzeArrayCreation(arrayCreationOperation, semanticModel, currentMethod);

            case IDelegateCreationOperation:
            case IAnonymousFunctionOperation:
            case ILocalFunctionOperation:
            case IMethodReferenceOperation:
                return ComplexityArtifacts.Constant;

            case ISwitchOperation switchOperation:
                return AnalyzeSwitchOperation(switchOperation, semanticModel, currentMethod);

            case ISwitchExpressionOperation switchExpressionOperation:
                return AnalyzeSwitchExpressionOperation(switchExpressionOperation, semanticModel, currentMethod);

            case ITryOperation tryOperation:
                return AnalyzeTryOperation(tryOperation, semanticModel, currentMethod);

            case IAwaitOperation awaitOperation:
                return AnalyzeOperation(awaitOperation.Operation, semanticModel, currentMethod);

            case IDynamicInvocationOperation:
            case IDynamicIndexerAccessOperation:
            case IDynamicObjectCreationOperation:
                return ComplexityArtifacts.Unknown(
                    SymbolicComplexityUnknownReason.UnsupportedOperation,
                    operation.Syntax);

            default:
                return SymbolicComplexityAlgebra.CombineSequence(operation.ChildOperations.Select(child =>
                    AnalyzeOperation(child, semanticModel, currentMethod)));
        }
    }

    private ComplexityArtifacts AnalyzeConditionalOperation(
        IConditionalOperation conditionalOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var conditionCost = AnalyzeOperation(conditionalOperation.Condition, semanticModel, currentMethod);
        if (TryGetConstantBoolean(conditionalOperation.Condition.Syntax, semanticModel, out var constantValue))
            return SymbolicComplexityAlgebra.CombineSequence(
                conditionCost,
                constantValue
                    ? AnalyzeOperation(conditionalOperation.WhenTrue, semanticModel, currentMethod)
                    : AnalyzeOperation(conditionalOperation.WhenFalse, semanticModel, currentMethod));

        return SymbolicComplexityAlgebra.CombineSequence(
            conditionCost,
            SymbolicComplexityAlgebra.CombineBranch(
                AnalyzeOperation(conditionalOperation.WhenTrue, semanticModel, currentMethod),
                AnalyzeOperation(conditionalOperation.WhenFalse, semanticModel, currentMethod)));
    }

    private ComplexityArtifacts AnalyzeForLoop(
        IForLoopOperation forLoopOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var beforeCost =
            SymbolicComplexityAlgebra.CombineSequence(
                forLoopOperation.Before.Select(op => AnalyzeOperation(op, semanticModel, currentMethod)));
        var conditionCost = AnalyzeOperation(forLoopOperation.Condition, semanticModel, currentMethod);
        var bottomCost =
            SymbolicComplexityAlgebra.CombineSequence(
                forLoopOperation.AtLoopBottom.Select(op => AnalyzeOperation(op, semanticModel, currentMethod)));
        var bodyCost = AnalyzeOperation(forLoopOperation.Body, semanticModel, currentMethod);

        if (forLoopOperation.Syntax is not ForStatementSyntax forStatement ||
            !_loopModel.TryGetForLoopBound(forStatement, semanticModel, currentMethod, out var bound))
            return SymbolicComplexityAlgebra.CombineSequence(
                beforeCost,
                ComplexityArtifacts.Unknown(
                    SymbolicComplexityUnknownReason.UnsupportedLoopShape,
                    forLoopOperation.Syntax,
                    conditionCost,
                    bottomCost,
                    bodyCost));

        var perIteration = SymbolicComplexityAlgebra.CombineSequence(conditionCost, bottomCost, bodyCost);
        var multiplied = SymbolicComplexityAlgebra.Multiply(bound.Cost, perIteration);
        multiplied = multiplied.WithDriver(SymbolicComplexityAlgebra.CreateDriver(
            "ForLoop",
            "for-loop bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
            forStatement));
        return SymbolicComplexityAlgebra.CombineSequence(beforeCost, multiplied);
    }

    private ComplexityArtifacts AnalyzeForEachLoop(
        IForEachLoopOperation forEachLoopOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var collectionCost = AnalyzeOperation(forEachLoopOperation.Collection, semanticModel, currentMethod);
        var bodyCost = AnalyzeOperation(forEachLoopOperation.Body, semanticModel, currentMethod);

        if (forEachLoopOperation.Syntax is not CommonForEachStatementSyntax foreachSyntax ||
            !_loopModel.TryGetForeachBound(forEachLoopOperation.Collection.Syntax, semanticModel, currentMethod,
                out var bound))
            return SymbolicComplexityAlgebra.CombineSequence(
                collectionCost,
                ComplexityArtifacts.Unknown(
                    SymbolicComplexityUnknownReason.UnsupportedLoopShape,
                    forEachLoopOperation.Syntax,
                    bodyCost));

        var multiplied = SymbolicComplexityAlgebra.Multiply(bound.Cost, bodyCost);
        multiplied = multiplied.WithDriver(SymbolicComplexityAlgebra.CreateDriver(
            "ForeachLoop",
            "foreach bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
            foreachSyntax));
        return SymbolicComplexityAlgebra.CombineSequence(collectionCost, multiplied);
    }

    private ComplexityArtifacts AnalyzeWhileLikeLoop(
        IWhileLoopOperation loopOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var conditionCost = AnalyzeOperation(loopOperation.Condition, semanticModel, currentMethod);
        var bodyCost = AnalyzeOperation(loopOperation.Body, semanticModel, currentMethod);
        var (condition, body, driverKind, description) = loopOperation.Syntax switch {
            WhileStatementSyntax statement =>
                (statement.Condition, statement.Statement, "WhileLoop", "while-loop"),
            DoStatementSyntax statement =>
                (statement.Condition, statement.Statement, "DoLoop", "do-loop"),
            _ => (null, null, string.Empty, string.Empty)
        };

        if (condition == null ||
            body == null ||
            !_loopModel.TryGetWhileLikeBound(
                condition,
                body,
                semanticModel,
                currentMethod,
                out var bound))
            return ComplexityArtifacts.Unknown(
                SymbolicComplexityUnknownReason.UnsupportedWhileLoop,
                loopOperation.Syntax,
                conditionCost,
                bodyCost);

        var multiplied = SymbolicComplexityAlgebra.Multiply(bound.Cost, SymbolicComplexityAlgebra.CombineSequence(conditionCost, bodyCost));
        multiplied = multiplied.WithDriver(SymbolicComplexityAlgebra.CreateDriver(
            driverKind,
            description + " bound " + bound.Cost.ToBigOText(currentMethod) + " from " + bound.Description,
            loopOperation.Syntax));
        return multiplied;
    }

    private ComplexityArtifacts AnalyzeInvocation(
        IInvocationOperation invocationOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var receiverAndArguments = new List<ComplexityArtifacts>();
        if (invocationOperation.Instance != null)
            receiverAndArguments.Add(AnalyzeOperation(invocationOperation.Instance, semanticModel, currentMethod));

        foreach (var argument in invocationOperation.Arguments)
            receiverAndArguments.Add(AnalyzeOperation(argument.Value, semanticModel, currentMethod));

        var callCost = _callModel.AnalyzeMethodCall(
            invocationOperation.TargetMethod,
            invocationOperation,
            invocationOperation.Syntax,
            semanticModel,
            currentMethod,
            SymbolicComplexityCallModel.GetArgumentSyntaxes(invocationOperation.TargetMethod, invocationOperation.Arguments),
            invocationOperation.Instance?.Syntax);
        receiverAndArguments.Add(callCost);
        return SymbolicComplexityAlgebra.CombineSequence(receiverAndArguments);
    }

    private ComplexityArtifacts AnalyzeObjectCreation(
        IObjectCreationOperation objectCreationOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var parts = new List<ComplexityArtifacts>();
        foreach (var argument in objectCreationOperation.Arguments)
            parts.Add(AnalyzeOperation(argument.Value, semanticModel, currentMethod));

        if (objectCreationOperation.Initializer != null)
            parts.Add(AnalyzeOperation(objectCreationOperation.Initializer, semanticModel, currentMethod));

        if (objectCreationOperation.Constructor != null)
            parts.Add(_callModel.AnalyzeMethodCall(
                objectCreationOperation.Constructor,
                objectCreationOperation,
                objectCreationOperation.Syntax,
                semanticModel,
                currentMethod,
                SymbolicComplexityCallModel.GetArgumentSyntaxes(objectCreationOperation.Constructor, objectCreationOperation.Arguments),
                null));

        return SymbolicComplexityAlgebra.CombineSequence(parts);
    }

    private ComplexityArtifacts AnalyzePropertyReference(
        IPropertyReferenceOperation propertyReferenceOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var parts = new List<ComplexityArtifacts>();
        if (propertyReferenceOperation.Instance != null)
            parts.Add(AnalyzeOperation(propertyReferenceOperation.Instance, semanticModel, currentMethod));

        foreach (var argument in propertyReferenceOperation.Arguments)
            parts.Add(AnalyzeOperation(argument.Value, semanticModel, currentMethod));

        var getter = propertyReferenceOperation.Property.GetMethod;
        if (getter != null)
            parts.Add(_callModel.AnalyzeMethodCall(
                getter,
                propertyReferenceOperation,
                propertyReferenceOperation.Syntax,
                semanticModel,
                currentMethod,
                SymbolicComplexityCallModel.GetArgumentSyntaxes(getter, propertyReferenceOperation.Arguments),
                propertyReferenceOperation.Instance?.Syntax));

        return SymbolicComplexityAlgebra.CombineSequence(parts);
    }

    private ComplexityArtifacts AnalyzeArrayCreation(
        IArrayCreationOperation arrayCreationOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var dimensionCosts = arrayCreationOperation.DimensionSizes
            .Select(size => AnalyzeOperation(size, semanticModel, currentMethod))
            .ToArray();
        var initializerCost = AnalyzeOperation(arrayCreationOperation.Initializer, semanticModel, currentMethod);
        return SymbolicComplexityAlgebra.CombineSequence(dimensionCosts.Concat(new[] { initializerCost }));
    }

    private ComplexityArtifacts AnalyzeSwitchOperation(
        ISwitchOperation switchOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var conditionCost = AnalyzeOperation(switchOperation.Value, semanticModel, currentMethod);
        var branchCosts = switchOperation.Cases
            .Select(@case =>
                SymbolicComplexityAlgebra.CombineSequence(@case.Body.Select(statement =>
                    AnalyzeOperation(statement, semanticModel, currentMethod))))
            .ToArray();
        if (branchCosts.Length == 0) return conditionCost;

        return SymbolicComplexityAlgebra.CombineSequence(conditionCost, SymbolicComplexityAlgebra.CombineBranch(branchCosts));
    }

    private ComplexityArtifacts AnalyzeSwitchExpressionOperation(
        ISwitchExpressionOperation switchExpressionOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var valueCost = AnalyzeOperation(switchExpressionOperation.Value, semanticModel, currentMethod);
        var armCosts = switchExpressionOperation.Arms
            .Select(arm => SymbolicComplexityAlgebra.CombineSequence(
                AnalyzeOperation(arm.Pattern, semanticModel, currentMethod),
                AnalyzeOperation(arm.Guard, semanticModel, currentMethod),
                AnalyzeOperation(arm.Value, semanticModel, currentMethod)))
            .ToArray();
        if (armCosts.Length == 0) return valueCost;

        return SymbolicComplexityAlgebra.CombineSequence(valueCost, SymbolicComplexityAlgebra.CombineBranch(armCosts));
    }

    private ComplexityArtifacts AnalyzeTryOperation(
        ITryOperation tryOperation,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod) {
        var paths = new List<ComplexityArtifacts> {
            AnalyzeOperation(tryOperation.Body, semanticModel, currentMethod)
        };
        foreach (var @catch in tryOperation.Catches)
            paths.Add(AnalyzeOperation(@catch.Handler, semanticModel, currentMethod));

        var finallyCost = AnalyzeOperation(tryOperation.Finally, semanticModel, currentMethod);
        return SymbolicComplexityAlgebra.CombineSequence(SymbolicComplexityAlgebra.CombineBranch(paths), finallyCost);
    }

    private bool TryGetConstantBoolean(
        SyntaxNode syntaxNode,
        SemanticModel semanticModel,
        out bool value) {
        if (syntaxNode is ExpressionSyntax expression &&
            semanticModel.GetConstantValue(expression, _cancellationToken) is
            { HasValue: true, Value: bool boolValue }) {
            value = boolValue;
            return true;
        }

        value = false;
        return false;
    }

}
