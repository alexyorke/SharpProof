using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic.Ir;
using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine;

internal static partial class PurityAssignmentStateTransfer
{
    internal static PurityAnalysisState UpdateDelegateMapForOperation(IOperation op, PurityAnalysisContext context,
        PurityAnalysisState currentState)
    {
        var nextState = currentState;
        var operationToTrack = op is IExpressionStatementOperation expressionStatementOperation
            ? expressionStatementOperation.Operation
            : op;

        if (operationToTrack is IAwaitOperation awaitOperation)
            return UpdateDelegateMapForOperation(awaitOperation.Operation, context, currentState);


        if (operationToTrack is ICompoundAssignmentOperation compoundAssignmentOperation)
        {
            var targetOperation = compoundAssignmentOperation.Target;
            var valueOperation = compoundAssignmentOperation.Value;
            var targetSymbol = TryResolveTrackedSymbol(targetOperation, currentState);

            if (targetSymbol is ILocalSymbol compoundLocalSymbol)
                foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(compoundLocalSymbol, context))
                    nextState = nextState.WithSmtSymbolDefinitionVersion(writtenLocalSymbol, operationToTrack.Syntax);
            else if (targetSymbol is IParameterSymbol compoundParameterSymbol)
                nextState = nextState.WithSmtSymbolDefinitionVersion(compoundParameterSymbol, operationToTrack.Syntax);

            nextState = PurityResourceStateFacts.AddCallerVisibleMutationFact(
                nextState,
                targetOperation,
                currentState,
                operationToTrack.Syntax);

            if (targetSymbol != null && targetOperation.Type?.TypeKind == TypeKind.Delegate)
            {
                if (compoundAssignmentOperation.OperatorKind == BinaryOperatorKind.Add)
                {
                    var valueTargets = ResolvePotentialTargets(valueOperation, currentState, context.CancellationToken);
                    if (valueTargets != null &&
                        currentState.DelegateTargetMap.TryGetValue(targetSymbol, out var currentTargets))
                    {
                        var mergedTargets = PotentialTargets.Merge(currentTargets, valueTargets.Value);
                        nextState = nextState.WithDelegateTarget(targetSymbol, mergedTargets);
                    }
                    else
                    {
                        nextState = nextState.WithDelegateTarget(targetSymbol, PotentialTargets.Unresolved);
                    }
                }
                else
                {
                    nextState = nextState.WithDelegateTarget(targetSymbol, PotentialTargets.Unresolved);
                }
            }
        }

        else if (operationToTrack is ICoalesceAssignmentOperation coalesceAssignmentOperation)
        {
            var targetOperation = coalesceAssignmentOperation.Target;
            var valueOperation = coalesceAssignmentOperation.Value;
            var targetSymbol = TryResolveTrackedSymbol(targetOperation, currentState);
            if (targetSymbol is IParameterSymbol coalesceParameterSymbol)
                nextState = nextState.WithSmtSymbolDefinitionVersion(coalesceParameterSymbol, operationToTrack.Syntax);

            if (targetSymbol is ILocalSymbol coalesceLocalSymbol &&
                currentState.IsDefinitelyNullLocalSymbol(coalesceLocalSymbol))
                nextState = ApplyDefiniteAssignmentTargetStateUpdates(
                    nextState,
                    targetOperation,
                    valueOperation,
                    targetSymbol,
                    currentState,
                    context,
                    operationToTrack.Syntax,
                    operationToTrack.Syntax);
        }

        else if (operationToTrack is IDeconstructionAssignmentOperation deconstructionAssignmentOperation)
        {
            nextState = ApplyDeconstructionAssignmentStateUpdates(
                nextState,
                deconstructionAssignmentOperation,
                currentState,
                context);
        }

        else if (operationToTrack is IAssignmentOperation assignmentOperation)
        {
            var targetOperation = assignmentOperation.Target;
            var valueOperation = assignmentOperation.Value;
            var targetSymbol = TryResolveTrackedSymbol(targetOperation, currentState);
            nextState = ApplyDefiniteAssignmentTargetStateUpdates(
                nextState,
                targetOperation,
                valueOperation,
                targetSymbol,
                currentState,
                context,
                operationToTrack.Syntax,
                operationToTrack.Syntax);
        }

        else if (operationToTrack is IVariableDeclaratorOperation variableDeclaratorOperation &&
                 variableDeclaratorOperation.Initializer?.Value is { } variableInitializer)
        {
            nextState = PurityOperationTransferAdapter.ApplyDeclaredBorrow(
                nextState,
                variableDeclaratorOperation.Symbol,
                variableInitializer,
                context.SemanticModel,
                context.CancellationToken);
        }

        else if (operationToTrack is IIncrementOrDecrementOperation incrementOrDecrementOperation)
        {
            var targetSymbol = TryResolveTrackedSymbol(incrementOrDecrementOperation.Target, currentState);
            if (targetSymbol is ILocalSymbol localSymbol)
                foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(localSymbol, context))
                    nextState = nextState
                        .WithoutLocalConcreteType(writtenLocalSymbol)
                        .WithoutDefinitelyNullLocal(writtenLocalSymbol)
                        .WithSmtSymbolDefinitionVersion(writtenLocalSymbol, operationToTrack.Syntax);
            else if (targetSymbol is IParameterSymbol parameterSymbol)
                nextState = nextState.WithSmtSymbolDefinitionVersion(parameterSymbol, operationToTrack.Syntax);

            nextState = PurityResourceStateFacts.AddCallerVisibleMutationFact(
                nextState,
                incrementOrDecrementOperation.Target,
                currentState,
                operationToTrack.Syntax);
        }

        else if (operationToTrack is IInvocationOperation invocationOperation)
        {
            nextState = PurityResourceStateFacts.AddDisposeInvocationFacts(nextState, invocationOperation, currentState);

            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.Parameter?.RefKind is not (RefKind.Ref or RefKind.Out)) continue;

                var writtenSymbol = TryResolveTrackedSymbol(SkipImplicitConversions(argument.Value), currentState);
                if (writtenSymbol is ILocalSymbol localSymbol)
                    foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(localSymbol, context))
                    {
                        nextState = nextState
                            .WithoutLocalConcreteType(writtenLocalSymbol)
                            .WithoutDefinitelyNullLocal(writtenLocalSymbol)
                            .WithSmtSymbolDefinitionVersion(writtenLocalSymbol, operationToTrack.Syntax);

                        if (writtenLocalSymbol.Type?.TypeKind == TypeKind.Delegate)
                            nextState = nextState.WithDelegateTarget(writtenLocalSymbol, PotentialTargets.Unresolved);
                    }
                else if (writtenSymbol is IParameterSymbol parameterSymbol)
                    nextState = nextState.WithSmtSymbolDefinitionVersion(parameterSymbol, operationToTrack.Syntax);
            }
        }

        else if (operationToTrack is IReturnOperation returnOperation)
        {
            nextState = PurityResourceStateFacts.AddReturnedOwnedResourceFacts(nextState, returnOperation, currentState);
        }

        else if (operationToTrack is IUsingOperation usingOperation)
        {
            nextState = PurityResourceStateFacts.AddUsingStatementDisposeFacts(nextState, usingOperation, currentState);
        }

        else if (operationToTrack is IFlowCaptureOperation flowCaptureOperation)
        {
            if (TryResolveTrackedSymbol(flowCaptureOperation.Value, currentState) is ISymbol capturedSymbol)
                nextState = nextState.WithFlowCaptureSymbol(flowCaptureOperation.Id, capturedSymbol);

            var valueTargets =
                ResolvePotentialTargets(flowCaptureOperation.Value, currentState, context.CancellationToken);
            if (valueTargets != null)
                nextState = nextState.WithFlowCaptureTarget(flowCaptureOperation.Id, valueTargets.Value);

            if (PurityConcreteReceiverResolver.TryResolveKnownConcreteType(flowCaptureOperation.Value, currentState, context.SemanticModel.Compilation,
                    out var concreteType))
                nextState = nextState.WithFlowCaptureConcreteType(flowCaptureOperation.Id, concreteType);

            if (PurityKnownBclSemantics.IsOwnedLocalArrayValue(
                    flowCaptureOperation.Value,
                    currentState,
                    context.SemanticModel.Compilation))
                nextState = nextState.WithOwnedArrayFlowCapture(flowCaptureOperation.Id, flowCaptureOperation.Syntax);
            else
                nextState = nextState.WithoutOwnedArrayFlowCapture(flowCaptureOperation.Id);
        }

        else if (operationToTrack is IVariableDeclarationGroupOperation groupOperation)
        {
            foreach (var declaration in groupOperation.Declarations)
                foreach (var declarator in declaration.Declarators)
                    if (declarator.Initializer != null)
                    {
                        var initializerValue = declarator.Initializer.Value;
                        var declaredSymbol = declarator.Symbol;
                        nextState = ApplyWrittenLocalStateUpdates(
                            nextState,
                            new[] { declaredSymbol },
                            initializerValue,
                            nextState,
                            context.SemanticModel,
                            context.SemanticModel.Compilation,
                            context.CancellationToken,
                            advanceDefinitionVersion: false);

                        if (declaredSymbol.Type?.TypeKind == TypeKind.Delegate)
                        {
                            var valueTargets =
                                ResolvePotentialTargets(initializerValue, nextState, context.CancellationToken);
                            if (valueTargets != null)
                                nextState = nextState.WithDelegateTarget(declaredSymbol, valueTargets.Value);
                        }

                        nextState = PurityOperationTransferAdapter.ApplyDeclaredBorrow(
                            nextState,
                            declaredSymbol,
                            initializerValue,
                            context.SemanticModel,
                            context.CancellationToken);
                        if (!PurityResourceStateFacts.IsUsingResourceDeclarator(declarator))
                            nextState = PurityResourceStateFacts.AddOwnedDisposableLocalFacts(
                                nextState,
                                declaredSymbol,
                                initializerValue,
                                context.SemanticModel.Compilation);
                    }
        }


        return nextState;
    }

    private static PurityAnalysisState ApplyDefiniteAssignmentTargetStateUpdates(
        PurityAnalysisState nextState,
        IOperation targetOperation,
        IOperation valueOperation,
        ISymbol? targetSymbol,
        PurityAnalysisState currentState,
        PurityAnalysisContext context,
        SyntaxNode definitionSyntax,
        SyntaxNode mutationSyntax)
    {
        var writtenLocalSymbols = targetSymbol is ILocalSymbol localSymbol
            ? EnumerateWrittenLocalSymbols(localSymbol, context).ToArray()
            : Array.Empty<ILocalSymbol>();
        if (targetSymbol is IParameterSymbol parameterSymbol)
        {
            nextState = nextState.WithSmtSymbolDefinitionVersion(parameterSymbol, definitionSyntax);
            nextState = PurityOperationTransferAdapter.ApplyAssignmentFacts(
                nextState,
                parameterSymbol,
                valueOperation,
                currentState,
                context.SemanticModel,
                context.CancellationToken);
        }

        nextState = ApplyWrittenLocalStateUpdates(
            nextState,
            writtenLocalSymbols,
            valueOperation,
            currentState,
            context.SemanticModel,
            context.SemanticModel.Compilation,
            context.CancellationToken);
        nextState = PurityResourceStateFacts.AddCallerVisibleMutationFact(
            nextState,
            targetOperation,
            currentState,
            mutationSyntax);
        return ApplyAssignedDelegateTargets(
            nextState,
            targetSymbol,
            targetOperation.Type,
            valueOperation,
            writtenLocalSymbols,
            currentState,
            context.CancellationToken);
    }

    private static PurityAnalysisState ApplyDeconstructionAssignmentStateUpdates(
        PurityAnalysisState nextState,
        IDeconstructionAssignmentOperation deconstructionAssignmentOperation,
        PurityAnalysisState currentState,
        PurityAnalysisContext context)
    {
        if (!SymbolicDeconstructionPlan.TryPair(
                deconstructionAssignmentOperation.Target,
                deconstructionAssignmentOperation.Value,
                target => TryResolveDeconstructionTargetSymbol(
                    target,
                    currentState,
                    context.SemanticModel,
                    context.CancellationToken),
                out var assignments))
            return nextState;

        foreach (var assignment in assignments)
        {
            if (assignment.Target.IsDiscard) continue;
            nextState = ApplyDefiniteAssignmentTargetStateUpdates(
                nextState,
                assignment.Target.Operation,
                assignment.Value,
                assignment.Target.Symbol,
                currentState,
                context,
                assignment.Target.Operation.Syntax,
                deconstructionAssignmentOperation.Syntax);
        }

        return nextState;
    }

    private static ISymbol? TryResolveDeconstructionTargetSymbol(
        IOperation targetOperation,
        PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        targetOperation = SkipImplicitConversions(targetOperation) ?? targetOperation;
        if (TryResolveTrackedSymbol(targetOperation, currentState) is { } trackedSymbol) return trackedSymbol;

        if (targetOperation is IDeclarationExpressionOperation declarationExpression)
        {
            if (TryResolveTrackedSymbol(declarationExpression.Expression, currentState) is { } declaredTrackedSymbol)
                return declaredTrackedSymbol;

            if (declarationExpression.Syntax is DeclarationExpressionSyntax
                {
                    Designation: SingleVariableDesignationSyntax designation
                } &&
                semanticModel.GetDeclaredSymbol(designation, cancellationToken) is { } declaredSymbol)
                return declaredSymbol;
        }

        if (targetOperation.Syntax is SingleVariableDesignationSyntax singleVariable &&
            semanticModel.GetDeclaredSymbol(singleVariable, cancellationToken) is { } singleVariableSymbol)
            return singleVariableSymbol;

        return targetOperation.Syntax is IdentifierNameSyntax identifier
            ? semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol
            : null;
    }

}
