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

        private static bool IsProgramPointUnreachableUsingSharedFacts(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (IsInReachableConstantSwitchGotoSection(syntaxNode, semanticModel, cancellationToken))
            {
                return false;
            }

            var pathConditions = SymbolicReachabilityService.CollectPathConditionsAt(
                syntaxNode,
                semanticModel,
                cancellationToken);
            return pathConditions.Count > 0 &&
                ArePathConditionsUnsatisfiableAt(pathConditions, syntaxNode, smtAnalysis);
        }

        private static bool ArePathConditionsUnsatisfiableAt(
            IReadOnlyCollection<SmtFormula> pathConditions,
            SyntaxNode site,
            SmtAnalysisService? smtAnalysis)
        {
            return SymbolicReachabilityService.PathConditionsAreUnsatisfiableWithIrFirst(
                pathConditions,
                site,
                smtAnalysis,
                "execution.visibility.path",
                "execution-visibility-path");
        }

    }
}
