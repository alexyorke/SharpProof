using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class InlineArrayAccessPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(
        OperationKind.InlineArrayAccess);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is not IInlineArrayAccessOperation inlineArrayAccessOperation)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (inlineArrayAccessOperation.Instance == null ||
            inlineArrayAccessOperation.Argument == null)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(inlineArrayAccessOperation.Syntax);

        var instanceResult = PurityAnalysisEngine.CheckSingleOperation(
            inlineArrayAccessOperation.Instance,
            context,
            currentState);
        if (!instanceResult.IsPure) return instanceResult;

        var argumentResult = PurityAnalysisEngine.CheckSingleOperation(
            inlineArrayAccessOperation.Argument,
            context,
            currentState);
        if (!argumentResult.IsPure) return argumentResult;

        if (RuleAnalysisHelper.IsWriteOnlyAssignmentTarget(inlineArrayAccessOperation))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }
}
