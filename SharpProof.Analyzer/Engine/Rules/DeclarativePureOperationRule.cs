using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal sealed class DeclarativePureOperationRule : IPurityRule
{
    private readonly ImmutableArray<OperationKind> _applicableOperationKinds;
    private readonly PureOperationRuleDescriptor _descriptor;

    public DeclarativePureOperationRule(PureOperationRuleDescriptor descriptor)
    {
        _descriptor = descriptor;
        _applicableOperationKinds = ImmutableArray.Create(descriptor.OperationKind);
    }

    public IEnumerable<OperationKind> ApplicableOperationKinds => _applicableOperationKinds;

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private string CreateLogMessage(IOperation operation)
    {
        return _descriptor.IncludeSyntaxInLog
            ? $"    [{_descriptor.RuleName}] {_descriptor.OperationDescription} ({operation.Syntax}) - Pure"
            : $"    [{_descriptor.RuleName}] {_descriptor.OperationDescription} - Always Pure.";
    }
}