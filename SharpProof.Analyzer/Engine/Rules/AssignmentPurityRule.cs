using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class AssignmentPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.SimpleAssignment, OperationKind.CompoundAssignment, OperationKind.CoalesceAssignment, OperationKind.Increment, OperationKind.Decrement);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            IOperation targetOperation;
            IOperation? valueOperation = null;
            IMethodSymbol? compoundOperatorMethod = null;
            SyntaxNode diagnosticNode = operation.Syntax;

            if (operation is IAssignmentOperation assignmentOperation)
            {
                targetOperation = assignmentOperation.Target;
                valueOperation = assignmentOperation.Value;

            }
            else if (operation is ICompoundAssignmentOperation compoundAssignmentOperation)
            {
                targetOperation = compoundAssignmentOperation.Target;
                valueOperation = compoundAssignmentOperation.Value;
                compoundOperatorMethod = compoundAssignmentOperation.OperatorMethod?.OriginalDefinition;

            }
            else if (operation is IIncrementOrDecrementOperation incrementDecrementOperation)
            {
                targetOperation = incrementDecrementOperation.Target;
                compoundOperatorMethod = incrementDecrementOperation.OperatorMethod?.OriginalDefinition;

            }
            else
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            if (targetOperation == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            targetOperation = NormalizeAssignmentTargetOperation(targetOperation, context);

            if (valueOperation != null)
            {
                var valueResult = PurityAnalysisEngine.CheckSingleOperation(valueOperation, context, currentState);
                if (!valueResult.IsPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        valueResult.ImpureSyntaxNode ?? valueOperation.Syntax,
                        valueResult.Evidence);
                }



                ITypeSymbol? targetType = (targetOperation as ILocalReferenceOperation)?.Type ??
                                          (targetOperation as IParameterReferenceOperation)?.Type ??
                                          (targetOperation as IFieldReferenceOperation)?.Type ??
                                          (targetOperation as IPropertyReferenceOperation)?.Type;

                ITypeSymbol? valueType = valueOperation.Type;

                if (targetType != null && valueType != null && !SymbolEqualityComparer.Default.Equals(targetType, valueType))
                {
                    IConversionOperation? conversionOp = null;


                    if (valueOperation is IConversionOperation topLevelConv &&
                        topLevelConv.Conversion.IsImplicit &&
                        SymbolEqualityComparer.Default.Equals(topLevelConv.Type, targetType))
                    {
                        conversionOp = topLevelConv;
                    }
                    else
                    {

                        conversionOp = valueOperation.DescendantsAndSelf()
                                        .OfType<IConversionOperation>()
                                        .FirstOrDefault(conv => conv.Conversion.IsImplicit &&
                                                               SymbolEqualityComparer.Default.Equals(conv.Type, targetType) &&
                                                               conv.Operand != null &&
                                                               SymbolEqualityComparer.Default.Equals(conv.Operand.Type, valueType));
                        if (conversionOp != null)
                        {
                        }
                    }


                    if (conversionOp != null)
                    {
                        var conversionResult = PurityAnalysisEngine.CheckSingleOperation(conversionOp, context, currentState);
                        if (!conversionResult.IsPure)
                        {

                            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                                conversionResult.ImpureSyntaxNode ?? conversionOp.Operand?.Syntax ?? valueOperation.Syntax,
                                conversionResult.Evidence);
                        }
                    }
                }

            }

            if (compoundOperatorMethod != null)
            {
                var operatorResult = CheckCompoundAssignmentOperatorPurity(compoundOperatorMethod, operation, context);
                if (!operatorResult.IsPure)
                {
                    return operatorResult;
                }
            }


            var targetResult = PurityAnalysisEngine.CheckSingleOperation(targetOperation, context, currentState);
            if (!targetResult.IsPure)
            {

                if (TryCreateMutableBorrowConflictEvidence(
                        operation,
                        TryResolveSymbol(targetOperation),
                        currentState,
                        context,
                        out var borrowConflictEvidence))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        operation.Syntax,
                        borrowConflictEvidence);
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax, targetResult.Evidence);
            }


            var setterResult = CheckPropertySetterPurity(targetOperation, context, currentState);
            if (!setterResult.IsPure)
            {
                return setterResult;
            }

            var targetSymbol = TryResolveSymbol(targetOperation);
            if (TryCreateMutableBorrowConflictEvidence(
                    operation,
                    targetSymbol,
                    currentState,
                    context,
                    out var earlyBorrowConflictEvidence))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    earlyBorrowConflictEvidence);
            }

            bool isPureAssignment = IsAssignmentTargetPure(targetOperation, context, targetSymbol, currentState);

            if (!isPureAssignment)
            {
                if (TryCreateMutableBorrowConflictEvidence(
                        operation,
                        targetSymbol,
                        currentState,
                        context,
                        out var borrowConflictEvidence))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        operation.Syntax,
                        borrowConflictEvidence);
                }

                if (PurityAnalysisEngine.TryCreateCallerVisibleMutationEvidence(
                        operation,
                        targetOperation,
                        currentState,
                        nameof(AssignmentPurityRule),
                        out var mutationEvidence))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        operation.Syntax,
                        mutationEvidence);
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "mutable_state_write",
                        ruleName: nameof(AssignmentPurityRule),
                        operation: operation,
                        syntaxNode: operation.Syntax,
                        symbol: targetSymbol));
            }



            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

    }
}
