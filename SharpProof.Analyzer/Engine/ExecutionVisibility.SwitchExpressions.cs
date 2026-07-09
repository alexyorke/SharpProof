using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;

namespace SharpProof.Analyzer.Engine
{
    internal static partial class ExecutionVisibility
    {

        private static bool IsInUnreachableSwitchExpressionArm(
            SyntaxNode syntaxNode,
            SwitchExpressionSyntax switchExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            var arm = switchExpression.Arms.FirstOrDefault(candidate => candidate.Expression.Span.Contains(syntaxNode.SpanStart));
            if (arm == null ||
                !SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                    switchExpression.GoverningExpression,
                    arm,
                    semanticModel,
                    cancellationToken,
                    out var armCondition))
            {
                return false;
            }

            return IsFormulaAlwaysFalseAt(
                armCondition,
                switchExpression,
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

    }
}
