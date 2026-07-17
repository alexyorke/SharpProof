using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Rules;

/// <summary>
/// Base class for purity rules that apply to a single <see cref="OperationKind"/>
/// and operate on one strongly-typed <see cref="IOperation"/>. It absorbs the
/// per-rule <see cref="ApplicableOperationKinds"/> declaration and the leading
/// cast guard so each rule only implements <see cref="CheckTyped"/>. Rules whose
/// operation does not match are treated as pure, matching the hand-written guard
/// they replace.
/// </summary>
internal abstract class PurityRuleBase<TOperation> : IPurityRule
    where TOperation : class, IOperation
{
    protected abstract OperationKind Kind { get; }

    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(Kind);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
        IOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (operation is not TOperation typed)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return CheckTyped(typed, context, currentState);
    }

    protected abstract PurityAnalysisEngine.PurityAnalysisResult CheckTyped(
        TOperation operation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState);
}
