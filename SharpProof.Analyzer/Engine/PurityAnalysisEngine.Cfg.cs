using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static PurityAnalysisResult AnalyzePurityUsingCFGInternal(
        SyntaxNode bodyNode,
        PurityAnalysisContext context,
        out PurityAnalysisState mergedNormalExitState)
    {
        var cancellationToken = context.CancellationToken;
        var semanticModel = context.SemanticModel;
        var smtAnalysis = context.SmtAnalysis;
        var containingMethodSymbol = context.ContainingMethodSymbol;
        cancellationToken.ThrowIfCancellationRequested();
        mergedNormalExitState = PurityAnalysisState.Pure;
        // Roslyn 4.x: Create(BlockSyntax|ArrowClause, model) throws ("operation has a non-null parent").
        // Create(BaseMethodDeclarationSyntax|LocalFunctionStatement|ConstructorDeclaration|... , model) is the supported root.
        ControlFlowGraph? cfg = null;
        try
        {
            cfg = ControlFlowGraph.Create(bodyNode, semanticModel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PurityAnalysisResult.Impure(bodyNode);
        }

        if (cfg == null || cfg.Blocks.IsEmpty) return PurityAnalysisResult.Pure;


        var fixedPoint = new CfgFixedPointWorklist(cfg.Blocks.Length * 200);
        fixedPoint.Seed(
            new CfgTraversalPoint(cfg.Blocks.First(), null),
            CreateInitialRequiresState(
                containingMethodSymbol,
                bodyNode,
                semanticModel,
                context.AttributePolicy,
                cancellationToken));

        while (fixedPoint.TryDequeue(out var currentPoint, out var stateBefore))
        {
            var currentBlock = currentPoint.Block;

            var stateAfter = ApplyTransferFunction(
                currentBlock,
                stateBefore,
                context);

            fixedPoint.RecordExit(currentPoint, stateAfter);


            if (TryGetConstantBranchDecision(currentBlock.BranchValue, semanticModel, smtAnalysis, cancellationToken,
                    out var takeConditionalSuccessor))
            {
                var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock);
                var takenBranch = takeConditionalSuccessor
                    ? trueUsesConditionalSuccessor
                        ? currentBlock.ConditionalSuccessor
                        : currentBlock.FallThroughSuccessor
                    : trueUsesConditionalSuccessor
                        ? currentBlock.FallThroughSuccessor
                        : currentBlock.ConditionalSuccessor;
                if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel,
                        takeConditionalSuccessor, smtAnalysis, cancellationToken, out var takenState))
                    PropagateControlFlowBranch(
                        takenBranch,
                        currentPoint.Continuation,
                        currentBlock.BranchValue,
                        takenState,
                        cfg,
                        fixedPoint);
            }
            else
            {
                var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock);

                if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel,
                        trueUsesConditionalSuccessor, smtAnalysis, cancellationToken, out var conditionalState))
                    PropagateControlFlowBranch(
                        currentBlock.ConditionalSuccessor,
                        currentPoint.Continuation,
                        currentBlock.BranchValue,
                        conditionalState,
                        cfg,
                        fixedPoint);

                if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel,
                        !trueUsesConditionalSuccessor, smtAnalysis, cancellationToken, out var fallThroughState))
                    PropagateControlFlowBranch(
                        currentBlock.FallThroughSuccessor,
                        currentPoint.Continuation,
                        currentBlock.BranchValue,
                        fallThroughState,
                        cfg,
                        fixedPoint);
            }
        }

        if (fixedPoint.HasPendingWork) return PurityAnalysisResult.Impure(bodyNode);

        var normalExitStates = fixedPoint.ExitStates
            .Where(pair => pair.Key.Block.Kind == BasicBlockKind.Exit)
            .Select(static pair => pair.Value)
            .ToArray();
        if (normalExitStates.Length != 0)
            mergedNormalExitState = PurityAnalysisState.Merge(normalExitStates);

        var finalResult = PurityAnalysisResult.Pure;

        foreach (var exitState in fixedPoint.ExitStates.Values)
            if (exitState.HasPotentialImpurity)
            {
                finalResult = exitState.FirstImpureSyntaxNode != null
                    ? PurityAnalysisResult.Impure(exitState.FirstImpureSyntaxNode, exitState.FirstImpurityEvidence)
                    : PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(exitState.FirstImpurityEvidence);
                return finalResult;
            }

        return finalResult;
    }

    private static PurityAnalysisState ApplyTransferFunction(
        BasicBlock block,
        PurityAnalysisState stateBefore,
        PurityAnalysisContext context)
    {
        var cancellationToken = context.CancellationToken;
        var semanticModel = context.SemanticModel;
        var smtAnalysis = context.SmtAnalysis;
        cancellationToken.ThrowIfCancellationRequested();

        if (stateBefore.HasPotentialImpurity) return stateBefore;

        if ((!stateBefore.PathState.Facts.IsDefaultOrEmpty ||
             !stateBefore.PathState.PathConditions.IsDefaultOrEmpty) &&
            IsPathStateUnsatisfiable(stateBefore.PathState, smtAnalysis))
            return stateBefore;

        var currentStateInBlock = stateBefore;
        PurityAnalysisResult? deferredRecursiveImpurity = null;
        SyntaxNode? deferredRecursiveSyntax = null;
        foreach (var op in block.Operations)
        {
            if (op == null) continue;



            if (op is IFlowCaptureOperation flowCap)
            {
                var valResult = CheckSingleOperation(flowCap.Value, context, currentStateInBlock);
                currentStateInBlock = currentStateInBlock.WithFlowCaptureResult(flowCap.Id, valResult);
                if (!valResult.IsPure)
                {
                    if (IsImpurityProvenUnreachable(valResult, semanticModel, smtAnalysis, cancellationToken)) continue;

                    currentStateInBlock = currentStateInBlock.WithImpurity(valResult, flowCap.Syntax);
                    break;
                }

                currentStateInBlock = PurityAssignmentStateTransfer.UpdateDelegateMapForOperation(flowCap, context, currentStateInBlock);
                continue;
            }

            var opResult = CheckSingleOperation(op, context, currentStateInBlock);

            if (!opResult.IsPure)
            {
                if (IsImpurityProvenUnreachable(opResult, semanticModel, smtAnalysis, cancellationToken)) continue;


                if (IsRecursivePlaceholderImpurity(opResult))
                {
                    deferredRecursiveImpurity ??= opResult.WithEvidence(
                        opResult.Evidence.WithSymbol(context.ContainingMethodSymbol.ToDisplayString(_signatureFormat)));
                    deferredRecursiveSyntax ??= op.Syntax;
                    continue;
                }

                currentStateInBlock = currentStateInBlock.WithImpurity(opResult, op.Syntax);
                break;
            }


            currentStateInBlock = PurityAssignmentStateTransfer.UpdateDelegateMapForOperation(op, context, currentStateInBlock);
        }

        if (!currentStateInBlock.HasPotentialImpurity && deferredRecursiveImpurity.HasValue)
        {
            var fallbackSyntax = deferredRecursiveSyntax ??
                                 block.Operations.FirstOrDefault()?.Syntax ??
                                 context.ContainingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                                     ?.GetSyntax(cancellationToken);

            currentStateInBlock = currentStateInBlock.WithImpurity(
                deferredRecursiveImpurity.Value,
                fallbackSyntax!);
        }

        if (!currentStateInBlock.HasPotentialImpurity &&
            block.BranchValue != null &&
            TryCreateThrowBranchImpurity(block.BranchValue, context, currentStateInBlock,
                out var throwBranchResult))
        {
            currentStateInBlock = currentStateInBlock.WithImpurity(throwBranchResult,
                throwBranchResult.ImpureSyntaxNode ?? block.BranchValue.Syntax);
        }
        else if (!currentStateInBlock.HasPotentialImpurity &&
                 block.BranchValue != null &&
                 ShouldAnalyzeStateSensitiveBranchValue(block.BranchValue.Syntax))
        {
            var operationToCheck = TryGetCfgReturnOperation(
                block.BranchValue,
                semanticModel,
                cancellationToken) ?? block.BranchValue;
            var branchValueResult = CheckSingleOperation(operationToCheck, context, currentStateInBlock);
            if (!branchValueResult.IsPure)
            {
                if (!IsImpurityProvenUnreachable(branchValueResult, semanticModel, smtAnalysis, cancellationToken))
                    currentStateInBlock = currentStateInBlock.WithImpurity(branchValueResult, block.BranchValue.Syntax);
            }
            else
            {
                currentStateInBlock =
                    PurityAssignmentStateTransfer.UpdateDelegateMapForOperation(block.BranchValue, context, currentStateInBlock);
            }
        }

        return currentStateInBlock;
    }

    private static IReturnOperation? TryGetCfgReturnOperation(
        IOperation branchValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var returnStatement = branchValue.Syntax.FirstAncestorOrSelf<ReturnStatementSyntax>();
        if (returnStatement != null)
            return semanticModel.GetOperation(returnStatement, cancellationToken) as IReturnOperation;

        var arrowExpression = branchValue.Syntax.FirstAncestorOrSelf<ArrowExpressionClauseSyntax>();
        if (arrowExpression?.Parent == null) return null;

        var declarationOperation = semanticModel.GetOperation(arrowExpression.Parent, cancellationToken);
        return declarationOperation == null
            ? null
            : ExecutionVisibility.VisibleDescendants(declarationOperation)
                .OfType<IReturnOperation>()
                .FirstOrDefault(returnOperation =>
                    returnOperation.ReturnedValue?.Syntax.Span.Contains(branchValue.Syntax.Span) == true);
    }

    private static bool TryCreateThrowBranchImpurity(
        IOperation branchValue,
        PurityAnalysisContext context,
        PurityAnalysisState currentState,
        out PurityAnalysisResult result)
    {
        result = PurityAnalysisResult.Pure;

        var throwSyntax = branchValue.Syntax.FirstAncestorOrSelf<ThrowStatementSyntax>() ??
                          (SyntaxNode?)branchValue.Syntax.FirstAncestorOrSelf<ThrowExpressionSyntax>();
        if (throwSyntax == null) return false;

        var exceptionResult = CheckSingleOperation(branchValue, context, currentState);
        if (!exceptionResult.IsPure)
        {
            result = exceptionResult;
            return true;
        }

        result = PurityAnalysisResult.Impure(
            throwSyntax,
            PurityEvidence.Create(
                "throw",
                "ThrowOperationPurityRule",
                syntaxNode: throwSyntax,
                operationKindOverride: OperationKind.Throw.ToString()));
        return true;
    }

    internal static bool IsRecursivePlaceholderImpurity(PurityAnalysisResult result)
    {
        return !result.IsPure &&
               result.Evidence.RuleName == "RecursivePurityAnalysis" &&
               result.Evidence.CatalogSource == "recursive_call";
    }


    private static PurityAnalysisResult AnalyzeOperationSubtreePurity(
        IOperation rootOperation,
        PurityAnalysisContext context)
    {
        var cancellationToken = context.CancellationToken;
        var semanticModel = context.SemanticModel;
        cancellationToken.ThrowIfCancellationRequested();

        var currentState = CreateInitialRequiresState(
            context.ContainingMethodSymbol,
            rootOperation.Syntax,
            semanticModel,
            context.AttributePolicy,
            cancellationToken);
        var visitedOperations = new HashSet<IOperation>();
        foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
        {
            var operationToAnalyze = operation is IExpressionStatementOperation expressionStatementOperation
                ? expressionStatementOperation.Operation
                : operation;
            if (!visitedOperations.Add(operationToAnalyze)) continue;

            if (operation is IFlowCaptureOperation flowCaptureOperation)
            {
                var valueResult = CheckSingleOperation(flowCaptureOperation.Value, context, currentState);
                currentState = currentState.WithFlowCaptureResult(flowCaptureOperation.Id, valueResult);
                if (!valueResult.IsPure) return valueResult;

                currentState = PurityAssignmentStateTransfer.UpdateDelegateMapForOperation(flowCaptureOperation, context, currentState);
                continue;
            }

            var operationResult = CheckSingleOperation(operationToAnalyze, context, currentState);
            if (!operationResult.IsPure) return operationResult;

            currentState = PurityAssignmentStateTransfer.UpdateDelegateMapForOperation(operationToAnalyze, context, currentState);
        }

        return currentState.HasPotentialImpurity
            ? ImpureResult(currentState.FirstImpureSyntaxNode, currentState.FirstImpurityEvidence)
            : PurityAnalysisResult.Pure;
    }

    private static SyntaxNode? TryGetDirectThrowOnlySyntax(SyntaxNode? bodySyntaxNode)
    {
        switch (bodySyntaxNode)
        {
            case BlockSyntax blockSyntax
                when blockSyntax.Statements.Count == 1:
                return TryGetDirectThrowOnlySyntax(blockSyntax.Statements[0]);
            case ThrowStatementSyntax throwStatementSyntax:
                return throwStatementSyntax;
            case ArrowExpressionClauseSyntax arrowExpressionClauseSyntax
                when arrowExpressionClauseSyntax.Expression is ThrowExpressionSyntax throwExpressionSyntax:
                return throwExpressionSyntax;
            case ThrowExpressionSyntax directThrowExpressionSyntax:
                return directThrowExpressionSyntax;
            case MethodDeclarationSyntax methodDeclarationSyntax
                when methodDeclarationSyntax.ExpressionBody != null:
                return TryGetDirectThrowOnlySyntax(methodDeclarationSyntax.ExpressionBody);
            case MethodDeclarationSyntax methodDeclarationSyntax
                when methodDeclarationSyntax.Body != null:
                return TryGetDirectThrowOnlySyntax(methodDeclarationSyntax.Body);
            case LocalFunctionStatementSyntax localFunctionStatementSyntax
                when localFunctionStatementSyntax.ExpressionBody != null:
                return TryGetDirectThrowOnlySyntax(localFunctionStatementSyntax.ExpressionBody);
            case LocalFunctionStatementSyntax localFunctionStatementSyntax
                when localFunctionStatementSyntax.Body != null:
                return TryGetDirectThrowOnlySyntax(localFunctionStatementSyntax.Body);
            case SimpleLambdaExpressionSyntax simpleLambdaExpressionSyntax:
                return TryGetDirectThrowOnlySyntax(simpleLambdaExpressionSyntax.Body);
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpressionSyntax:
                return TryGetDirectThrowOnlySyntax(parenthesizedLambdaExpressionSyntax.Body);
            case AnonymousMethodExpressionSyntax anonymousMethodExpressionSyntax
                when anonymousMethodExpressionSyntax.Block != null:
                return TryGetDirectThrowOnlySyntax(anonymousMethodExpressionSyntax.Block);
            default:
                return null;
        }
    }
}
