using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Ir;
using SearchLib.Smt;

namespace SharpProof.Symbolic
{
    internal static partial class SymbolicProgramPointFacts
    {
        private static void AddTopLevelKnownGuardNormalCompletionFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            expression = UnwrapAwaitedNormalCompletionExpression(expression);
            if (expression is InvocationExpressionSyntax invocation &&
                SymbolicKnownGuardFacts.TryCreateArgumentOutOfRangeGuardConditions(
                    invocation,
                    semanticModel,
                    cancellationToken,
                    out _,
                    out _,
                    out var normalCompletionCondition,
                    out _) &&
                SymbolicIrFormulaEncoder.TryEncode(normalCompletionCondition, out var formula))
            {
                AddUniqueFact(facts, formula);
            }
        }

        private static void AddTopLevelKnownGuardNormalCompletionStateFacts(
            ref SymbolicState state,
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapAwaitedNormalCompletionExpression(expression);
            if (expression is InvocationExpressionSyntax invocation &&
                SymbolicKnownGuardFacts.TryCreateArgumentOutOfRangeGuardConditions(
                    invocation,
                    semanticModel,
                    cancellationToken,
                    out _,
                    out _,
                    out var normalCompletionCondition,
                    out _))
            {
                state = state.AddPathCondition(normalCompletionCondition);
            }
        }
    }
}
