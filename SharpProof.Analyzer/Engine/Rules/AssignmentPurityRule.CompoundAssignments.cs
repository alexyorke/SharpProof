using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class AssignmentPurityRule : IPurityRule
{
    private static PurityAnalysisEngine.PurityAnalysisResult CheckCompoundAssignmentOperatorPurity(
        IMethodSymbol operatorMethod,
        IOperation operation,
        PurityAnalysisContext context)
    {
        var hasTrustedGeneratedPurity = PurityAnalysisEngine.TryGetTrustedGeneratedPurityCoverage(
            operatorMethod,
            context.SemanticModel.Compilation,
            out var generatedPurity);

        if ((!hasTrustedGeneratedPurity &&
             PurityAnalysisEngine.IsKnownPureBCLMember(operatorMethod, context.SemanticModel.Compilation)) ||
            PurityAnalysisEngine.HasPureExternalAttribute(operatorMethod))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (hasTrustedGeneratedPurity)
        {
            if (generatedPurity.IsPure) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

            if (!generatedPurity.IsPure)
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        generatedPurity.PrimaryCategory,
                        nameof(AssignmentPurityRule),
                        operation,
                        operation.Syntax,
                        operatorMethod.OriginalDefinition,
                        "generated_purity_summary"));
        }

        if (!ShouldAnalyzeCompoundAssignmentOperator(operatorMethod))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var operatorPurity = PurityAnalysisEngine.GetCalleePurity(operatorMethod, context);
        return operatorPurity.IsPure
            ? PurityAnalysisEngine.PurityAnalysisResult.Pure
            : operatorPurity.WithCallee(operatorMethod, operation.Syntax);
    }

    private static bool ShouldAnalyzeCompoundAssignmentOperator(IMethodSymbol operatorMethod)
    {
        return PurityAnalysisEngine.ShouldAnalyzeCompoundAssignmentOperator(operatorMethod);
    }

    private static bool TryCreateMutableBorrowConflictEvidence(
        IOperation operation,
        ISymbol? targetSymbol,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityEvidence evidence)
    {
        return PurityAnalysisEngine.TryCreateMutableBorrowConflictEvidence(
            operation,
            targetSymbol,
            currentState,
            context.SemanticModel,
            context.CancellationToken,
            nameof(AssignmentPurityRule),
            out evidence);
    }
}