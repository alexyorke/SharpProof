namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class DeconstructionAssignmentPurityRule {
    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState) {
        if (operation.Syntax is AssignmentExpressionSyntax assignmentSyntax) {
            var deconstructionInfo = context.SemanticModel.GetDeconstructionInfo(assignmentSyntax);
            var deconstructResult = CheckDeconstructionInfo(deconstructionInfo, operation, context);
            if (!deconstructResult.IsPure) return deconstructResult;
        }

        if (operation is not IDeconstructionAssignmentOperation deconstructionAssignment)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var valueResult = PurityAnalysisEngine.CheckSingleOperation(
            deconstructionAssignment.Value,
            context,
            currentState);
        if (!valueResult.IsPure) return valueResult;

        foreach (var target in EnumerateTargets(deconstructionAssignment.Target)) {
            if (IsPureDeconstructionTargetPlaceholder(target)) continue;

            var targetResult = AssignmentPurityRule.CheckWriteTargetPurity(
                operation,
                target,
                context,
                currentState);
            if (!targetResult.IsPure) return targetResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static IEnumerable<IOperation> EnumerateTargets(IOperation target) {
        target = PurityAnalysisEngine.SkipImplicitConversions(target) ?? target;
        if (target is ITupleOperation tuple) {
            foreach (var element in tuple.Elements)
                foreach (var nested in EnumerateTargets(element))
                    yield return nested;

            yield break;
        }

        yield return target;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDeconstructionInfo(
        DeconstructionInfo deconstructionInfo,
        IOperation operation,
        PurityAnalysisContext context) {
        if (deconstructionInfo.Method is IMethodSymbol deconstructMethod) {
            var calleeResult = PurityCalleeResolver.GetCanonicalCalleePurityAtUse(
                deconstructMethod,
                operation.Syntax,
                context);
            if (!calleeResult.IsPure)
                return calleeResult;
        }

        foreach (var nestedInfo in deconstructionInfo.Nested) {
            var nestedResult = CheckDeconstructionInfo(nestedInfo, operation, context);
            if (!nestedResult.IsPure) return nestedResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static bool IsPureDeconstructionTargetPlaceholder(IOperation operation) {
        operation = PurityAnalysisEngine.SkipImplicitConversions(operation) ?? operation;

        if (operation is IDeclarationExpressionOperation ||
            operation is IDiscardOperation ||
            operation is ILocalReferenceOperation)
            return true;

        if (operation is ITupleOperation tupleOperation) {
            foreach (var element in tupleOperation.Elements)
                if (!IsPureDeconstructionTargetPlaceholder(element))
                    return false;

            return true;
        }

        return false;
    }
}
