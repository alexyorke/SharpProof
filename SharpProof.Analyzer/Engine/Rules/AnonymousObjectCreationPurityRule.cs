namespace SharpProof.Analyzer.Engine.Rules;

internal static class AnonymousObjectCreationPurityRule {
    internal static PurityAnalysisEngine.PurityAnalysisResult CheckTyped(
        IAnonymousObjectCreationOperation anonymousObjectCreationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState) {
        foreach (var initializer in anonymousObjectCreationOperation.Initializers) {
            if (initializer is ISimpleAssignmentOperation assignment) {
                if (assignment.Value == null)
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(assignment.Syntax);

                var valueResult = PurityAnalysisEngine.CheckSingleOperation(assignment.Value, context, currentState);
                if (!valueResult.IsPure) return valueResult;

                continue;
            }

            var initializerResult = PurityAnalysisEngine.CheckSingleOperation(initializer, context, currentState);
            if (!initializerResult.IsPure) return initializerResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
