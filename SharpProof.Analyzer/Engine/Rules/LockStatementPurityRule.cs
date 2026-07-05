using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using System.Linq;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal class LockStatementPurityRule : IPurityRule
    {
        public System.Collections.Generic.IEnumerable<OperationKind> ApplicableOperationKinds => System.Collections.Immutable.ImmutableArray.Create(OperationKind.Lock);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var lockOp = (ILockOperation)operation;

            bool isSynchronizationAllowed = context.AllowSynchronizationAttributeSymbol != null && 
                                            context.ContainingMethodSymbol != null && 
                                            PurityAnalysisEngine.HasAttribute(context.ContainingMethodSymbol, context.AllowSynchronizationAttributeSymbol);

            if (!isSynchronizationAllowed)
            {
                return ImpureSynchronization(lockOp);
            }

            var lockedValue = lockOp.LockedValue;
            bool isAllowableTarget = false;

            if (lockedValue is ITypeOfOperation)
            {
                isAllowableTarget = true;
            }
            else if (lockedValue is IFieldReferenceOperation fieldRef)
            {
                if (fieldRef.Field.IsReadOnly && fieldRef.Field.Type.SpecialType == SpecialType.System_Object)
                {
                    isAllowableTarget = true;
                }
            }

            if (!isAllowableTarget)
            {
                return ImpureSynchronization(lockOp); // Not a robust check (needs diagnostic for SP0025, but returning Impure correctly triggers it eventually or handled differently)
            }

            // The lock value expression itself must be pure
            var targetPurity = PurityAnalysisEngine.CheckSingleOperation(lockOp.LockedValue, context, currentState);
            if (!targetPurity.IsPure)
            {
                return targetPurity;
            }

            // The body inside the lock must be pure
            return PurityAnalysisEngine.CheckSingleOperation(lockOp.Body, context, currentState);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult ImpureSynchronization(ILockOperation lockOp)
        {
            return PurityAnalysisEngine.ImpureResult(
                lockOp.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "synchronization",
                    ruleName: nameof(LockStatementPurityRule),
                    operation: lockOp,
                    syntaxNode: lockOp.Syntax));
        }
    }
}
