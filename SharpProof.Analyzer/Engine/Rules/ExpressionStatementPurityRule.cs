using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class ExpressionStatementPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.ExpressionStatement);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IExpressionStatementOperation expressionStatement))
            {

                PurityAnalysisEngine.LogDebug($"WARNING: ExpressionStatementPurityRule called with unexpected operation type: {operation.Kind}. Assuming Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);
            }


            PurityAnalysisEngine.LogDebug($"    [ExprStmtRule] Checking underlying expression of kind {expressionStatement.Operation?.Kind} for statement: {operation.Syntax}");
            if (expressionStatement.Operation == null)
            {
                PurityAnalysisEngine.LogDebug($"    [ExprStmtRule] ExpressionStatement has null underlying operation. Assuming Pure (e.g., empty statement?).");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            var innerResult = PurityAnalysisEngine.CheckSingleOperation(expressionStatement.Operation, context, currentState);
            PurityAnalysisEngine.LogDebug($"    [ExprStmtRule] Inner operation ({expressionStatement.Operation.Kind}) result: IsPure={innerResult.IsPure}");

            return innerResult;
        }
    }
}