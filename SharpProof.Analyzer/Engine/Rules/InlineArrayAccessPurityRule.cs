using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class InlineArrayAccessPurityRule : PurityRuleBase<IInlineArrayAccessOperation>
{
    protected override OperationKind Kind => OperationKind.InlineArrayAccess;

    protected override PurityAnalysisEngine.PurityAnalysisResult CheckTyped(
        IInlineArrayAccessOperation inlineArrayAccessOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
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
