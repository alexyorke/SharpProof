using SharpProof.Analyzer.Engine.Rules;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static PurityAnalysisResult CheckCfgImplicitSemantics(
        SyntaxNode bodyNode,
        PurityAnalysisContext context,
        PurityAnalysisState returnState)
    {
        var root = context.SemanticModel.GetOperation(bodyNode, context.CancellationToken);
        if (root == null) return PurityAnalysisResult.Pure;

        var probeState = returnState.WithPathState(SymbolicRuntimeTypeFacts.RetainExactRuntimeTypes(returnState.PathState));
        foreach (var operation in ExecutionVisibility.VisibleDescendants(root))
        {
            if (operation is ITryOperation tryOperation)
            {
                foreach (var catchClause in tryOperation.Catches)
                {
                    var result = AnalyzeOperationSubtreePurity(catchClause, context);
                    if (!result.IsPure) return result;
                }
                if (tryOperation.Finally is { } finallyClause)
                {
                    var result = AnalyzeOperationSubtreePurity(finallyClause, context);
                    if (!result.IsPure) return result;
                }
            }
            else if (operation.Kind is OperationKind.Using or OperationKind.UsingDeclaration)
            {
                var result = CheckSingleOperation(operation, context, probeState);
                if (!result.IsPure) return result;
            }
            else if (operation is IForEachLoopOperation forEach &&
                     !IsSyntaxProvenUnreachable(
                         operation.Syntax,
                         context.SemanticModel,
                         context.SmtAnalysis,
                         context.CancellationToken))
            {
                var result = forEach.IsAsynchronous
                    ? LoopPurityRule.CheckForEachAsyncEnumeratorPurity(forEach.Collection, context)
                    : LoopPurityRule.CheckForEachEnumeratorPurity(forEach.Collection, context);
                if (!result.IsPure) return result;
            }
            else if (operation is ICompoundAssignmentOperation { OperatorMethod: { } operatorMethod } &&
                     !IsSyntaxProvenUnreachable(
                         operation.Syntax,
                         context.SemanticModel,
                         context.SmtAnalysis,
                         context.CancellationToken) &&
                     !PurityCalleeResolver.GetCalleePurity(operatorMethod, context).IsPure)
                return PurityAnalysisResult.Impure(operation.Syntax);
        }

        return PurityAnalysisResult.Pure;
    }

    internal static PurityAnalysisResult CheckSingleOperation(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisState currentState)
    {
        if ((!currentState.PathState.Facts.IsDefaultOrEmpty ||
             !currentState.PathState.PathConditions.IsDefaultOrEmpty) &&
            IsPathStateUnsatisfiable(currentState.PathState, context.SmtAnalysis))
            return PurityAnalysisResult.Pure;

        if ((!currentState.PathState.Facts.IsDefaultOrEmpty ||
             !currentState.PathState.PathConditions.IsDefaultOrEmpty) &&
            ExecutionVisibility.IsEvaluationPathUnsatisfiableUsingSymbolicState(
                operation.Syntax,
                context.SemanticModel,
                context.CancellationToken,
                currentState.PathState,
                currentState.GetSmtSymbolVersion,
                context.SmtAnalysis))
            return PurityAnalysisResult.Pure;

        if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                operation.Syntax,
                context.SemanticModel,
                context.CancellationToken,
                context.SmtAnalysis))
            return PurityAnalysisResult.Pure;

        if (operation is IFlowCaptureReferenceOperation flowRef)
        {
            if (currentState.FlowCaptures.TryGetValue(flowRef.Id, out var capturedPurity)) return capturedPurity;

            return PurityAnalysisResult.Pure;
        }

        if (operation is IFlowCaptureOperation flowCap)
            return CheckSingleOperation(flowCap.Value, context, currentState);


        var isChecked = TryGetOperatorMethodForDirectPurityCheck(operation, out var operatorMethod);

        if (isChecked && operation is IBinaryOperation binaryOp)
        {
            var leftResult = CheckSingleOperation(binaryOp.LeftOperand, context, currentState);
            if (!leftResult.IsPure) return leftResult;

            var rightResult = CheckSingleOperation(binaryOp.RightOperand, context, currentState);
            if (!rightResult.IsPure) return rightResult;
        }
        else if (isChecked && operation is IUnaryOperation unaryOp)
        {
            var operandResult = CheckSingleOperation(unaryOp.Operand, context, currentState);
            if (!operandResult.IsPure) return operandResult;
        }

        if (isChecked)
        {
            if (operatorMethod != null)
            {
                if (context.PurityCache.TryGetValue(operatorMethod.OriginalDefinition, out var cachedResult))
                {
                    if (!cachedResult.IsPure) return PurityAnalysisResult.Impure(operation.Syntax);
                    return PurityAnalysisResult.Pure;
                }


                var hasTrustedGeneratedPurity = TryGetTrustedGeneratedPurityCoverage(
                    operatorMethod,
                    context.SemanticModel.Compilation,
                    out var generatedPurity);

                if (hasTrustedGeneratedPurity)
                {
                    if (generatedPurity.IsPure) return PurityAnalysisResult.Pure;

                    if (!generatedPurity.IsPure)
                        return PurityAnalysisResult.Impure(
                            operation.Syntax,
                            PurityEvidence.Create(
                                generatedPurity.PrimaryCategory,
                                syntaxNode: operation.Syntax,
                                symbol: operatorMethod.OriginalDefinition,
                                catalogSource: "generated_purity_summary"));
                }

                if (!hasTrustedGeneratedPurity &&
                    ImpurityCatalog.IsKnownPureBCLMember(operatorMethod, context.SemanticModel.Compilation))
                    return PurityAnalysisResult.Pure;

                if (ImpurityCatalog.IsKnownImpure(operatorMethod)) return PurityAnalysisResult.Impure(operation.Syntax);


                var operatorPurity = PurityCalleeResolver.GetCalleePurity(operatorMethod, context);

                if (!operatorPurity.IsPure) return PurityAnalysisResult.Impure(operation.Syntax);
            }

            return PurityAnalysisResult.Pure;
        }


        if (operation.Kind == OperationKind.InterpolatedStringText ||
            operation.Kind == OperationKind.Interpolation)
            return PurityAnalysisResult.Pure;

        if (operation.Kind == OperationKind.Discard) return PurityAnalysisResult.Pure;

        _firstRuleByOperationKind.TryGetValue(operation.Kind, out var applicableRule);

        if (applicableRule != null)
        {
            var ruleResult = applicableRule.CheckPurity(operation, context, currentState);

            if (!ruleResult.IsPure)
            {
                if (ruleResult.ImpureSyntaxNode == null)
                    return operation.Syntax != null
                        ? PurityAnalysisResult.Impure(operation.Syntax)
                        : PurityAnalysisResult.ImpureUnknownLocation;
                return ruleResult;
            }

            return PurityAnalysisResult.Pure;
        }

        return ImpureResult(operation.Syntax, CreateUnsupportedOperationEvidence(operation));
    }

    private static bool TryGetOperatorMethodForDirectPurityCheck(
        IOperation operation,
        out IMethodSymbol? operatorMethod)
    {
        switch (operation)
        {
            case IBinaryOperation { IsChecked: true } binary:
                operatorMethod = binary.OperatorMethod;
                return true;
            case IUnaryOperation { IsChecked: true } unary:
                operatorMethod = unary.OperatorMethod;
                return true;
            default:
                operatorMethod = null;
                return false;
        }
    }
    private sealed record CfgFinallyContinuation(System.Collections.Immutable.ImmutableArray<ControlFlowRegion> Regions,
        int RegionIndex, BasicBlock? Destination, CfgFinallyContinuation? Parent);
}
