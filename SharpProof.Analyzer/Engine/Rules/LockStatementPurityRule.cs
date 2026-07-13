using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class LockStatementPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Lock);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        var lockOp = (ILockOperation)operation;

        var isSynchronizationAllowed = context.ContainingMethodSymbol != null &&
                                       context.AttributePolicy.HasAttribute(
                                           context.ContainingMethodSymbol,
                                           "AllowSynchronizationAttribute");

        if (!isSynchronizationAllowed)
            return PurityAnalysisEngine.ImpureResult(
                lockOp,
                "synchronization",
                nameof(LockStatementPurityRule));

        var lockedValue = lockOp.LockedValue;
        var isAllowableTarget = false;

        if (lockedValue is ITypeOfOperation)
            isAllowableTarget = true;
        else if (lockedValue is IFieldReferenceOperation fieldRef)
            if (fieldRef.Field.IsReadOnly && fieldRef.Field.Type.SpecialType == SpecialType.System_Object)
                isAllowableTarget = true;

        if (!isAllowableTarget)
            return PurityAnalysisEngine.ImpureResult(
                lockOp,
                "synchronization",
                nameof(LockStatementPurityRule));

        // The lock value expression itself must be pure
        var targetPurity = PurityAnalysisEngine.CheckSingleOperation(lockOp.LockedValue, context, currentState);
        if (!targetPurity.IsPure) return targetPurity;

        // The body inside the lock must be pure
        return PurityAnalysisEngine.CheckSingleOperation(lockOp.Body, context, currentState);
    }
}
