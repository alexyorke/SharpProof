using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Analyzer.Engine;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Analyzer
{
    internal static partial class ExceptionFlowAnalyzer
    {
        private const string DoesNotReturnIfAttributeName = "System.Diagnostics.CodeAnalysis.DoesNotReturnIfAttribute";
        private const string NotNullAttributeName = "System.Diagnostics.CodeAnalysis.NotNullAttribute";

        private static bool IsKnownByDominatingIf(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            PathFactKind factKind,
            SmtAnalysisService smtAnalysis)
        {
            var invalidatedSymbols = CollectLocalAndParameterSymbols(expression, semanticModel, cancellationToken);
            if (invalidatedSymbols.Count == 0)
            {
                return false;
            }

            if (!TryCreateFactFormula(expression, factKind, semanticModel, cancellationToken, out var factFormula) ||
                factFormula == null)
            {
                return false;
            }

            var pathConditions = CollectPathConditionsForUse(
                useNode,
                invalidatedSymbols,
                semanticModel,
                cancellationToken);
            return pathConditions.Count > 0 &&
                SymbolicReachabilityService.PathConditionsAllowAndImply(pathConditions, factFormula, smtAnalysis);
        }

        private static bool IsKnownByPriorAssignment(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            PathFactKind factKind)
        {
            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            if (symbol == null)
            {
                return false;
            }

            var containingStatement = useNode
                .AncestorsAndSelf()
                .OfType<StatementSyntax>()
                .FirstOrDefault(statement => statement.Parent is BlockSyntax);
            if (containingStatement?.Parent is not BlockSyntax block)
            {
                return false;
            }

            var matchedAssignment = false;
            foreach (var statement in block.Statements)
            {
                if (ReferenceEquals(statement, containingStatement))
                {
                    break;
                }

                foreach (var candidate in statement.DescendantNodesAndSelf(
                             descendIntoChildren: node => !ExecutionVisibility.IsNestedCallableBoundary(node)))
                {
                    if (candidate is AssignmentExpressionSyntax assignment &&
                        ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                    {
                        if (!ExpressionMatchesFact(assignment.Right, factKind, semanticModel, cancellationToken))
                        {
                            return false;
                        }

                        matchedAssignment = true;
                    }
                    else if (candidate is VariableDeclaratorSyntax declarator &&
                             declarator.Initializer != null &&
                             semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                             SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                    {
                        if (!ExpressionMatchesFact(declarator.Initializer.Value, factKind, semanticModel, cancellationToken))
                        {
                            return false;
                        }

                        matchedAssignment = true;
                    }
                    else if (MutatesSymbol(candidate, symbol, semanticModel, cancellationToken))
                    {
                        return false;
                    }
                }
            }

            return matchedAssignment;
        }

        private static void AddPrecedingGuardConditions(
            ISymbol symbol,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            var containingStatement = useNode
                .AncestorsAndSelf()
                .OfType<StatementSyntax>()
                .FirstOrDefault(statement => statement.Parent is BlockSyntax);
            if (containingStatement?.Parent is not BlockSyntax block)
            {
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (ReferenceEquals(statement, containingStatement))
                {
                    break;
                }

                if (statement is IfStatementSyntax ifStatement &&
                    !IsSymbolAssignedBetween(block, ifStatement.Span.End, useNode.SpanStart, symbol, semanticModel, cancellationToken) &&
                    !AnyReferencedSymbolAssignedBetween(ifStatement.Condition, block, ifStatement.Span.End, useNode.SpanStart, semanticModel, cancellationToken))
                {
                    AddCompletedIfStatementFacts(ifStatement, symbol, semanticModel, cancellationToken, pathConditions);
                }
            }
        }

        private static List<SmtFormula> CollectPathConditionsForUse(
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return CollectPathConditionsForUse(
                useNode,
                CollectLocalAndParameterSymbols(useNode, semanticModel, cancellationToken),
                semanticModel,
                cancellationToken);
        }

        private static List<SmtFormula> CollectPathConditionsForUse(
            SyntaxNode useNode,
            IReadOnlyCollection<ISymbol> invalidatedSymbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var pathConditions = new List<SmtFormula>();
            AddPriorAssignmentPathConditions(useNode, semanticModel, cancellationToken, pathConditions);
            AddSharedAncestorPathConditions(useNode, semanticModel, cancellationToken, pathConditions);

            foreach (var ifStatement in useNode.Ancestors().OfType<IfStatementSyntax>())
            {
                if (ifStatement.Statement.Span.Contains(useNode.SpanStart) &&
                    !AnySymbolAssignedBeforeUse(ifStatement.Statement, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken) &&
                    !AnyReferencedSymbolAssignedBeforeUse(ifStatement.Condition, ifStatement.Statement, useNode.SpanStart, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, pathConditions);
                }

                if (ifStatement.Else?.Statement is { } elseStatement &&
                    elseStatement.Span.Contains(useNode.SpanStart) &&
                    !AnySymbolAssignedBeforeUse(elseStatement, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken) &&
                    !AnyReferencedSymbolAssignedBeforeUse(ifStatement.Condition, elseStatement, useNode.SpanStart, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, pathConditions);
                }
            }

            AddSwitchPathConditions(useNode, invalidatedSymbols, semanticModel, cancellationToken, pathConditions);
            AddLoopPathConditions(useNode, invalidatedSymbols, semanticModel, cancellationToken, pathConditions);
            AddCatchFilterPathConditions(useNode, invalidatedSymbols, semanticModel, cancellationToken, pathConditions);
            AddExpressionBranchPathConditions(useNode, invalidatedSymbols, semanticModel, cancellationToken, pathConditions);
            AddPrecedingGuardConditions(invalidatedSymbols, useNode, semanticModel, cancellationToken, pathConditions);
            return pathConditions;
        }

        private static void AddSharedAncestorPathConditions(
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            foreach (var condition in SymbolicReachabilityService.CollectAncestorReachabilityConditions(useNode, semanticModel, cancellationToken))
            {
                pathConditions.Add(condition);
            }
        }

        private static void AddExpressionBranchPathConditions(
            SyntaxNode useNode,
            IReadOnlyCollection<ISymbol> invalidatedSymbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            foreach (var conditionalExpression in useNode.Ancestors().OfType<ConditionalExpressionSyntax>())
            {
                if (AnySymbolMutatedInSyntax(conditionalExpression.Condition, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (conditionalExpression.WhenTrue.Span.Contains(useNode.SpanStart) &&
                    !AnySymbolAssignedBeforeUse(conditionalExpression.WhenTrue, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(conditionalExpression.Condition, branchWhenTrue: true, semanticModel, cancellationToken, pathConditions);
                }
                else if (conditionalExpression.WhenFalse.Span.Contains(useNode.SpanStart) &&
                         !AnySymbolAssignedBeforeUse(conditionalExpression.WhenFalse, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(conditionalExpression.Condition, branchWhenTrue: false, semanticModel, cancellationToken, pathConditions);
                }
            }

            foreach (var binaryExpression in useNode.Ancestors().OfType<BinaryExpressionSyntax>())
            {
                if (!binaryExpression.Right.Span.Contains(useNode.SpanStart) ||
                    AnySymbolMutatedInSyntax(binaryExpression.Left, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnySymbolAssignedBeforeUse(binaryExpression.Right, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    TryAddPathCondition(binaryExpression.Left, branchWhenTrue: true, semanticModel, cancellationToken, pathConditions);
                }
                else if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    TryAddPathCondition(binaryExpression.Left, branchWhenTrue: false, semanticModel, cancellationToken, pathConditions);
                }
                else if (binaryExpression.IsKind(SyntaxKind.CoalesceExpression))
                {
                    TryAddCoalesceRightPathCondition(binaryExpression.Left, semanticModel, cancellationToken, pathConditions);
                }
            }

            foreach (var conditionalAccess in useNode.Ancestors().OfType<ConditionalAccessExpressionSyntax>())
            {
                if (!conditionalAccess.WhenNotNull.Span.Contains(useNode.SpanStart) ||
                    AnySymbolMutatedInSyntax(conditionalAccess.Expression, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnySymbolAssignedBeforeUse(conditionalAccess.WhenNotNull, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    continue;
                }

                TryAddReferenceNullCondition(conditionalAccess.Expression, isNull: false, semanticModel, cancellationToken, pathConditions);
            }
        }

        private static void AddLoopPathConditions(
            SyntaxNode useNode,
            IReadOnlyCollection<ISymbol> invalidatedSymbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            foreach (var whileStatement in useNode.Ancestors().OfType<WhileStatementSyntax>())
            {
                if (!whileStatement.Statement.Span.Contains(useNode.SpanStart) ||
                    AnySymbolAssignedBeforeUse(whileStatement.Statement, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnyReferencedSymbolAssignedBeforeUse(whileStatement.Condition, whileStatement.Statement, useNode.SpanStart, semanticModel, cancellationToken))
                {
                    continue;
                }

                TryAddPathCondition(whileStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, pathConditions);
                foreach (var loopFact in SymbolicReachabilityService.CollectLoopBodyInvariantFacts(whileStatement, semanticModel, cancellationToken))
                {
                    pathConditions.Add(loopFact);
                }
            }

            foreach (var doStatement in useNode.Ancestors().OfType<DoStatementSyntax>())
            {
                if (!doStatement.Statement.Span.Contains(useNode.SpanStart) ||
                    AnySymbolAssignedBeforeUse(doStatement.Statement, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    continue;
                }

                foreach (var loopFact in SymbolicReachabilityService.CollectLoopBodyInvariantFacts(doStatement, semanticModel, cancellationToken))
                {
                    pathConditions.Add(loopFact);
                }
            }

            foreach (var forStatement in useNode.Ancestors().OfType<ForStatementSyntax>())
            {
                if (forStatement.Condition == null ||
                    !forStatement.Statement.Span.Contains(useNode.SpanStart) ||
                    AnySymbolAssignedBeforeUse(forStatement.Statement, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnyReferencedSymbolAssignedBeforeUse(forStatement.Condition, forStatement.Statement, useNode.SpanStart, semanticModel, cancellationToken))
                {
                    continue;
                }

                TryAddPathCondition(forStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, pathConditions);
                foreach (var loopFact in SymbolicReachabilityService.CollectLoopBodyInvariantFacts(forStatement, semanticModel, cancellationToken))
                {
                    pathConditions.Add(loopFact);
                }
            }
        }

        private static void AddSwitchPathConditions(
            SyntaxNode useNode,
            IReadOnlyCollection<ISymbol> invalidatedSymbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            foreach (var switchStatement in useNode.Ancestors().OfType<SwitchStatementSyntax>())
            {
                var matchingSection = switchStatement.Sections
                    .FirstOrDefault(section => section.Span.Contains(useNode.SpanStart));
                if (matchingSection == null ||
                    AnySymbolMutatedInSyntax(switchStatement.Expression, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnySymbolAssignedBeforeUse(matchingSection, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnyReferencedSymbolAssignedBeforeUse(switchStatement.Expression, matchingSection, useNode.SpanStart, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                    switchStatement.Expression,
                    matchingSection,
                    semanticModel,
                    cancellationToken,
                    out var sectionCondition))
                {
                    pathConditions.Add(sectionCondition);
                }
            }

            foreach (var switchExpression in useNode.Ancestors().OfType<SwitchExpressionSyntax>())
            {
                var matchingArm = switchExpression.Arms
                    .FirstOrDefault(arm => arm.Expression.Span.Contains(useNode.SpanStart));
                if (matchingArm == null ||
                    AnySymbolMutatedInSyntax(switchExpression.GoverningExpression, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnySymbolAssignedBeforeUse(matchingArm, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnyReferencedSymbolAssignedBeforeUse(switchExpression.GoverningExpression, matchingArm, useNode.SpanStart, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                    switchExpression.GoverningExpression,
                    matchingArm,
                    semanticModel,
                    cancellationToken,
                    out var armCondition))
                {
                    pathConditions.Add(armCondition);
                }
            }
        }

        private static void AddCatchFilterPathConditions(
            SyntaxNode useNode,
            IReadOnlyCollection<ISymbol> invalidatedSymbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            foreach (var catchClause in useNode.Ancestors().OfType<CatchClauseSyntax>())
            {
                if (catchClause.Filter?.FilterExpression is not { } filterExpression ||
                    !catchClause.Block.Span.Contains(useNode.SpanStart) ||
                    AnySymbolMutatedInSyntax(filterExpression, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnySymbolAssignedBeforeUse(catchClause.Block, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnyReferencedSymbolAssignedBeforeUse(filterExpression, catchClause.Block, useNode.SpanStart, semanticModel, cancellationToken))
                {
                    continue;
                }

                TryAddPathCondition(filterExpression, branchWhenTrue: true, semanticModel, cancellationToken, pathConditions);
            }
        }

        private static void AddPrecedingGuardConditions(
            IReadOnlyCollection<ISymbol> invalidatedSymbols,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            var containingStatement = useNode
                .AncestorsAndSelf()
                .OfType<StatementSyntax>()
                .FirstOrDefault(statement => statement.Parent is BlockSyntax);
            if (containingStatement?.Parent is not BlockSyntax block)
            {
                return;
            }

            foreach (var statement in block.Statements)
            {
                if (ReferenceEquals(statement, containingStatement))
                {
                    break;
                }

                if (statement is IfStatementSyntax ifStatement &&
                    !AnySymbolAssignedBetween(block, ifStatement.Span.End, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken) &&
                    !AnyReferencedSymbolAssignedBetween(ifStatement.Condition, block, ifStatement.Span.End, useNode.SpanStart, semanticModel, cancellationToken))
                {
                    AddCompletedIfStatementFacts(ifStatement, invalidatedSymbols, semanticModel, cancellationToken, pathConditions);
                }
            }
        }

        private static IReadOnlyCollection<ISymbol> CollectLocalAndParameterSymbols(
            SyntaxNode root,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                if (node is not ExpressionSyntax expression)
                {
                    continue;
                }

                var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
                if (symbol != null)
                {
                    symbols.Add(symbol);
                }
            }

            return symbols;
        }

        private static HashSet<ISymbol> CollectRelevantSymbols(
            SyntaxNode primaryRoot,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return new HashSet<ISymbol>(
                CollectLocalAndParameterSymbols(primaryRoot, semanticModel, cancellationToken),
                SymbolEqualityComparer.Default);
        }

        private static HashSet<ISymbol> CollectRelevantSymbols(
            SyntaxNode primaryRoot,
            SyntaxNode? additionalRoot,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var symbols = CollectRelevantSymbols(primaryRoot, semanticModel, cancellationToken);
            if (additionalRoot != null && !ReferenceEquals(additionalRoot, primaryRoot))
            {
                AddRelevantSymbols(symbols, additionalRoot, semanticModel, cancellationToken);
            }

            return symbols;
        }

        private static void AddRelevantSymbols(
            ICollection<ISymbol> symbols,
            SyntaxNode root,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var symbol in CollectLocalAndParameterSymbols(root, semanticModel, cancellationToken))
            {
                symbols.Add(symbol);
            }
        }

        private static void AddPriorAssignmentPathConditions(
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            foreach (var fact in SymbolicReachabilityService.CollectPriorAssignmentFacts(useNode, semanticModel, cancellationToken))
            {
                pathConditions.Add(fact);
            }

            AddPriorCoalesceAssignmentThrowFacts(useNode, semanticModel, cancellationToken, pathConditions);
            AddPriorSelfThrowGuardedAssignmentFacts(useNode, semanticModel, cancellationToken, pathConditions);
        }

        private static void AddPriorCoalesceAssignmentThrowFacts(
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            {
                foreach (var statement in block.Statements)
                {
                    if (ReferenceEquals(statement, containingStatement))
                    {
                        break;
                    }

                    if (statement is not ExpressionStatementSyntax
                        {
                            Expression: AssignmentExpressionSyntax assignment
                        } ||
                        !assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) ||
                        UnwrapFactExpression(assignment.Right) is not ThrowExpressionSyntax ||
                        GetLocalOrParameterSymbol(assignment.Left, semanticModel, cancellationToken) is not { } assignedSymbol ||
                        IsSymbolAssignedBetween(block, assignment.Span.End, useNode.SpanStart, assignedSymbol, semanticModel, cancellationToken))
                    {
                        continue;
                    }

                    AddSymbolNonNullFact(assignedSymbol, pathConditions);
                }
            }
        }

        private static void AddPriorSelfThrowGuardedAssignmentFacts(
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            {
                foreach (var statement in block.Statements)
                {
                    if (ReferenceEquals(statement, containingStatement))
                    {
                        break;
                    }

                    if (statement is not ExpressionStatementSyntax
                        {
                            Expression: AssignmentExpressionSyntax assignment
                        } ||
                        !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                        GetLocalOrParameterSymbol(assignment.Left, semanticModel, cancellationToken) is not { } assignedSymbol ||
                        !TryGetThrowGuardedValue(
                            assignment.Right,
                            out var effectiveValueExpression,
                            out var guardExpression,
                            out var guardBranchWhenTrue,
                            out var requiresNonNullValue) ||
                        !ExpressionMatchesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) ||
                        IsSymbolAssignedBetween(block, assignment.Span.End, useNode.SpanStart, assignedSymbol, semanticModel, cancellationToken))
                    {
                        continue;
                    }

                    if (guardExpression != null)
                    {
                        if (AnyReferencedSymbolAssignedBetween(guardExpression, block, assignment.Span.End, useNode.SpanStart, semanticModel, cancellationToken))
                        {
                            continue;
                        }

                        TryAddPathCondition(guardExpression, guardBranchWhenTrue, semanticModel, cancellationToken, pathConditions);
                    }
                    else if (requiresNonNullValue)
                    {
                        AddSymbolNonNullFact(assignedSymbol, pathConditions);
                    }
                }
            }
        }

        internal static bool IsExceptionPathReachable(
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var pathConditions = CollectPathConditionsForUse(useNode, semanticModel, cancellationToken);

            return SymbolicReachabilityService.IsSatisfiable(pathConditions, smtAnalysis);
        }

        internal static bool IsMethodCallCandidatePathReachable(
            MethodCallCandidate candidate,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var relevantSymbols = CollectRelevantSymbols(candidate.CallSite, semanticModel, cancellationToken);
            if (candidate.UsingDisposeGuard?.ResourceExpression is { } resourceExpression)
            {
                AddRelevantSymbols(relevantSymbols, resourceExpression, semanticModel, cancellationToken);
            }

            var pathConditions = CollectPathConditionsForUse(
                candidate.CallSite,
                relevantSymbols,
                semanticModel,
                cancellationToken);

            if (candidate.UsingDisposeGuard?.ResourceExpression is { } disposeReceiver)
            {
                TryAddReferenceNullCondition(
                    disposeReceiver,
                    isNull: false,
                    semanticModel,
                    cancellationToken,
                    pathConditions);
            }

            return SymbolicReachabilityService.IsSatisfiable(pathConditions, smtAnalysis);
        }

        internal static List<SmtFormula> CollectExceptionSitePathConditions(
            SyntaxNode exceptionSite,
            SyntaxNode? relevantRoot,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var relevantSymbols = CollectRelevantSymbols(
                exceptionSite,
                relevantRoot,
                semanticModel,
                cancellationToken);

            return CollectPathConditionsForUse(
                exceptionSite,
                relevantSymbols,
                semanticModel,
                cancellationToken);
        }

        internal static bool IsShadowedByPathSensitiveThrowingFinally(
            SyntaxNode site,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var tryStatement in site.Ancestors().OfType<TryStatementSyntax>())
            {
                if (!tryStatement.Span.Contains(site.SpanStart) ||
                    tryStatement.Finally?.Block is not { } finallyBlock ||
                    finallyBlock.Span.Contains(site.SpanStart))
                {
                    continue;
                }

                if (!tryStatement.Block.Span.Contains(site.SpanStart) &&
                    !tryStatement.Catches.Any(catchClause => catchClause.Block.Span.Contains(site.SpanStart)))
                {
                    continue;
                }

                if (FinallyBlockIsProvenToExit(site, finallyBlock, semanticModel, cancellationToken, smtAnalysis))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool FinallyBlockIsProvenToExit(
            SyntaxNode site,
            BlockSyntax finallyBlock,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var pathConditions = CollectExceptionSitePathConditions(
                site,
                finallyBlock,
                semanticModel,
                cancellationToken);
            if (!SymbolicReachabilityService.IsSatisfiable(pathConditions, smtAnalysis))
            {
                return false;
            }

            foreach (var statement in finallyBlock.Statements)
            {
                if (StatementExitIsProven(statement, pathConditions, semanticModel, cancellationToken, smtAnalysis))
                {
                    return true;
                }

                AddPriorStatementFacts(statement, semanticModel, cancellationToken, pathConditions);
                if (!SymbolicReachabilityService.IsSatisfiable(pathConditions, smtAnalysis))
                {
                    return false;
                }
            }

            return false;
        }

        private static bool StatementExitIsProven(
            StatementSyntax statement,
            IReadOnlyCollection<SmtFormula> pathConditions,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (StatementDefinitelyExits(statement))
            {
                return true;
            }

            switch (statement)
            {
                case BlockSyntax block:
                    return BlockExitIsProven(block, pathConditions, semanticModel, cancellationToken, smtAnalysis);
                case IfStatementSyntax ifStatement:
                    return IfStatementExitIsProven(ifStatement, pathConditions, semanticModel, cancellationToken, smtAnalysis);
                default:
                    return false;
            }
        }

        private static bool BlockExitIsProven(
            BlockSyntax block,
            IReadOnlyCollection<SmtFormula> pathConditions,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var blockConditions = pathConditions.ToList();
            foreach (var statement in block.Statements)
            {
                if (StatementExitIsProven(statement, blockConditions, semanticModel, cancellationToken, smtAnalysis))
                {
                    return true;
                }

                AddPriorStatementFacts(statement, semanticModel, cancellationToken, blockConditions);
                if (!SymbolicReachabilityService.IsSatisfiable(blockConditions, smtAnalysis))
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IfStatementExitIsProven(
            IfStatementSyntax ifStatement,
            IReadOnlyCollection<SmtFormula> pathConditions,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var trueConditions = SymbolicReachabilityService.TryCollectBranchConditions(
                pathConditions,
                ifStatement.Condition,
                branchWhenTrue: true,
                semanticModel,
                cancellationToken);
            if (trueConditions == null)
            {
                return false;
            }

            var trueReachable = SymbolicReachabilityService.IsSatisfiable(trueConditions, smtAnalysis);
            var trueExits = !trueReachable ||
                StatementExitIsProven(ifStatement.Statement, trueConditions, semanticModel, cancellationToken, smtAnalysis);

            if (ifStatement.Else?.Statement is not { } elseStatement)
            {
                return trueReachable && trueExits &&
                    SymbolicReachabilityService.PathConditionsImplyBranch(
                        pathConditions,
                        ifStatement.Condition,
                        branchWhenTrue: true,
                        semanticModel,
                        cancellationToken,
                        smtAnalysis);
            }

            var falseConditions = SymbolicReachabilityService.TryCollectBranchConditions(
                pathConditions,
                ifStatement.Condition,
                branchWhenTrue: false,
                semanticModel,
                cancellationToken);
            if (falseConditions == null)
            {
                return false;
            }

            var falseReachable = SymbolicReachabilityService.IsSatisfiable(falseConditions, smtAnalysis);
            var falseExits = !falseReachable ||
                StatementExitIsProven(elseStatement, falseConditions, semanticModel, cancellationToken, smtAnalysis);

            return trueExits && falseExits && (trueReachable || falseReachable);
        }

        private static IEnumerable<(BlockSyntax Block, StatementSyntax ContainingStatement)> EnumerateContainingBlocks(SyntaxNode useNode)
        {
            for (SyntaxNode? current = useNode; current != null; current = current.Parent)
            {
                if (current is StatementSyntax statement &&
                    statement.Parent is BlockSyntax block)
                {
                    yield return (block, statement);
                }
            }
        }

        private static bool AnyConditionSymbolMutatedInStatement(
            ExpressionSyntax condition,
            StatementSyntax statement,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var conditionSymbols = GetReferencedLocalAndParameterSymbols(condition, semanticModel, cancellationToken);
            if (conditionSymbols.Count == 0)
            {
                return false;
            }

            foreach (var node in statement.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                foreach (var symbol in conditionSymbols)
                {
                    if (MutatesSymbol(node, symbol, semanticModel, cancellationToken))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IReadOnlyList<ISymbol> GetReferencedLocalAndParameterSymbols(
            SyntaxNode root,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var symbols = new List<ISymbol>();
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                if (node is not ExpressionSyntax expression)
                {
                    continue;
                }

                var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
                if (symbol != null &&
                    symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
                {
                    symbols.Add(symbol);
                }
            }

            return symbols;
        }

        private static void AddPriorStatementFacts(
            StatementSyntax statement,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (statement is LocalDeclarationStatementSyntax localDeclaration)
            {
                foreach (var declarator in localDeclaration.Declaration.Variables)
                {
                    if (declarator.Initializer == null)
                    {
                        continue;
                    }

                    RemoveFactsInvalidatedByNestedMutations(declarator.Initializer.Value, semanticModel, cancellationToken, facts);
                    if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol)
                    {
                        continue;
                    }

                    AddAssignedValueFacts(localSymbol, declarator.Initializer.Value, semanticModel, cancellationToken, facts);
                    AddNotNullParameterNormalCompletionFacts(
                        declarator.Initializer.Value,
                        localDeclaration,
                        semanticModel,
                        cancellationToken,
                        facts);
                    AddDoesNotReturnIfNormalCompletionFacts(
                        declarator.Initializer.Value,
                        localDeclaration,
                        semanticModel,
                        cancellationToken,
                        facts);
                    AddArrayCreationNormalCompletionFacts(
                        declarator.Initializer.Value,
                        localDeclaration,
                        semanticModel,
                        cancellationToken,
                        facts);
                }

                return;
            }

            if (statement is ExpressionStatementSyntax expressionStatement &&
                expressionStatement.Expression is AssignmentExpressionSyntax assignment)
            {
                if (TryHandleTupleDeconstructionDeclaration(assignment, semanticModel, cancellationToken, facts))
                {
                    return;
                }

                if (TryHandleTupleAssignment(assignment, semanticModel, cancellationToken, facts))
                {
                    return;
                }

                SmtFormula? previousAssignedValue = null;
                if (TryGetMutatedLocalOrParameterSymbol(assignment, semanticModel, cancellationToken, out var assignedSymbol))
                {
                    TryGetCurrentSymbolValue(facts, assignedSymbol, out previousAssignedValue);
                }

                RemoveFactsInvalidatedByNestedMutations(assignment.Left, semanticModel, cancellationToken, facts);
                RemoveFactsInvalidatedByNestedMutations(assignment.Right, semanticModel, cancellationToken, facts);

                if (TryGetMutatedLocalOrParameterSymbol(assignment, semanticModel, cancellationToken, out assignedSymbol))
                {
                    RemoveFactsReferencingSymbol(facts, assignedSymbol);
                    if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        AddAssignedValueFacts(assignedSymbol, assignment.Right, semanticModel, cancellationToken, facts);
                    }
                    else if (previousAssignedValue != null &&
                             TryCreateCompoundAssignmentFact(
                                 assignedSymbol,
                                 previousAssignedValue,
                                 assignment,
                                 semanticModel,
                                 cancellationToken,
                                 out var compoundAssignmentFact))
                    {
                        facts.Add(compoundAssignmentFact);
                    }
                }

                AddArrayCreationNormalCompletionFacts(
                    assignment.Right,
                    expressionStatement,
                    semanticModel,
                    cancellationToken,
                    facts);
                AddNotNullParameterNormalCompletionFacts(
                    assignment.Right,
                    expressionStatement,
                    semanticModel,
                    cancellationToken,
                    facts);
                AddDoesNotReturnIfNormalCompletionFacts(
                    assignment.Right,
                    expressionStatement,
                    semanticModel,
                    cancellationToken,
                    facts);
                return;
            }

            if (statement is ExpressionStatementSyntax unaryExpressionStatement &&
                TryGetIncrementedOrDecrementedSymbol(
                    unaryExpressionStatement.Expression,
                    semanticModel,
                    cancellationToken,
                    out var incrementedSymbol,
                    out var delta) &&
                TryGetCurrentSymbolValue(facts, incrementedSymbol, out var previousIncrementedValue))
            {
                RemoveFactsReferencingSymbol(facts, incrementedSymbol);
                if (TryCreateIncrementOrDecrementFact(incrementedSymbol, previousIncrementedValue, delta, out var mutationFact))
                {
                    facts.Add(mutationFact);
                }

                return;
            }

            foreach (var node in statement.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                if (TryGetMutatedLocalOrParameterSymbol(node, semanticModel, cancellationToken, out var mutatedSymbol))
                {
                    RemoveFactsReferencingSymbol(facts, mutatedSymbol);
                }
            }

            if (statement is IfStatementSyntax ifStatement)
            {
                AddCompletedIfStatementFacts(ifStatement, semanticModel, cancellationToken, facts);
            }
            else if (statement is ExpressionStatementSyntax completedExpressionStatement)
            {
                AddNotNullParameterNormalCompletionFacts(
                    completedExpressionStatement.Expression,
                    completedExpressionStatement,
                    semanticModel,
                    cancellationToken,
                    facts);
                AddDoesNotReturnIfNormalCompletionFacts(
                    completedExpressionStatement.Expression,
                    completedExpressionStatement,
                    semanticModel,
                    cancellationToken,
                    facts);
                AddArrayCreationNormalCompletionFacts(
                    completedExpressionStatement.Expression,
                    completedExpressionStatement,
                    semanticModel,
                    cancellationToken,
                    facts);
            }
            else
            {
                foreach (var loopFact in SymbolicReachabilityService.CollectCompletedLoopExitInvariantFacts(statement, semanticModel, cancellationToken))
                {
                    facts.Add(loopFact);
                }
            }
        }

        private static void AddNotNullParameterNormalCompletionFacts(
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            expression = UnwrapAwaitedFactExpression(expression);
            if (expression is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation)
            {
                return;
            }

            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.ArgumentKind != ArgumentKind.Explicit ||
                    argument.Parameter is not { RefKind: RefKind.None, IsParams: false } parameter ||
                    !ParameterHasNotNullAttribute(parameter) ||
                    argument.Syntax is not ArgumentSyntax argumentSyntax ||
                    !argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.None) ||
                    GetLocalOrParameterSymbol(argumentSyntax.Expression, semanticModel, cancellationToken) is not { } argumentSymbol ||
                    AnyConditionSymbolMutatedInStatement(argumentSyntax.Expression, statement, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddSymbolNonNullFact(argumentSymbol, facts);
            }
        }

        private static bool ParameterHasNotNullAttribute(IParameterSymbol parameter)
        {
            return SymbolHasNotNullAttribute(parameter) ||
                (!SymbolEqualityComparer.Default.Equals(parameter, parameter.OriginalDefinition) &&
                 SymbolHasNotNullAttribute(parameter.OriginalDefinition));
        }

        private static bool SymbolHasNotNullAttribute(IParameterSymbol parameter)
        {
            return parameter.GetAttributes().Any(attribute =>
                string.Equals(
                    GetFullMetadataName(attribute.AttributeClass),
                    NotNullAttributeName,
                    System.StringComparison.Ordinal));
        }

        private static void AddDoesNotReturnIfNormalCompletionFacts(
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            expression = UnwrapAwaitedFactExpression(expression);
            if (expression is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation)
            {
                return;
            }

            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.ArgumentKind != ArgumentKind.Explicit ||
                    argument.Parameter is not { RefKind: RefKind.None, IsParams: false } parameter ||
                    !TryGetDoesNotReturnIfValue(parameter, out var doesNotReturnWhen) ||
                    argument.Syntax is not ArgumentSyntax argumentSyntax ||
                    !argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.None) ||
                    AnyConditionSymbolMutatedInStatement(argumentSyntax.Expression, statement, semanticModel, cancellationToken))
                {
                    continue;
                }

                TryAddPathCondition(
                    argumentSyntax.Expression,
                    branchWhenTrue: !doesNotReturnWhen,
                    semanticModel,
                    cancellationToken,
                    facts);
            }
        }

        private static bool TryGetDoesNotReturnIfValue(IParameterSymbol parameter, out bool value)
        {
            return TryGetDoesNotReturnIfValueFromSymbol(parameter, out value) ||
                (!SymbolEqualityComparer.Default.Equals(parameter, parameter.OriginalDefinition) &&
                 TryGetDoesNotReturnIfValueFromSymbol(parameter.OriginalDefinition, out value));
        }

        private static bool TryGetDoesNotReturnIfValueFromSymbol(IParameterSymbol parameter, out bool value)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                if (!string.Equals(
                        GetFullMetadataName(attribute.AttributeClass),
                        DoesNotReturnIfAttributeName,
                        System.StringComparison.Ordinal) ||
                    attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not bool attributeValue)
                {
                    continue;
                }

                value = attributeValue;
                return true;
            }

            value = false;
            return false;
        }

        private static string? GetFullMetadataName(INamedTypeSymbol? type)
        {
            if (type == null)
            {
                return null;
            }

            var namespaceName = type.ContainingNamespace?.IsGlobalNamespace == false
                ? type.ContainingNamespace.ToDisplayString()
                : string.Empty;
            return string.IsNullOrEmpty(namespaceName)
                ? type.MetadataName
                : namespaceName + "." + type.MetadataName;
        }

        private static ExpressionSyntax UnwrapAwaitedFactExpression(ExpressionSyntax expression)
        {
            expression = UnwrapFactExpression(expression);
            return expression is AwaitExpressionSyntax awaitExpression
                ? UnwrapFactExpression(awaitExpression.Expression)
                : expression;
        }

        private static void AddArrayCreationNormalCompletionFacts(
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            expression = UnwrapFactExpression(expression);
            if (expression is not ArrayCreationExpressionSyntax arrayCreation)
            {
                return;
            }

            foreach (var sizeExpression in GetExplicitArraySizeExpressions(arrayCreation))
            {
                if (AnyConditionSymbolMutatedInStatement(sizeExpression, statement, semanticModel, cancellationToken) ||
                    !CSharpConditionToFormula.TryTranslateValue(
                        sizeExpression,
                        semanticModel,
                        cancellationToken,
                        out var sizeFormula,
                        getSymbolVersion: null) ||
                    sizeFormula is not { Kind: SmtValueKind.Int })
                {
                    continue;
                }

                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    sizeFormula,
                    new SmtIntegerConstant(0)));
            }
        }

        private static void AddSymbolNonNullFact(
            ISymbol symbol,
            ICollection<SmtFormula> facts)
        {
            if (!TryCreateSymbolSmtValue(symbol, out var formula) ||
                formula is not { Kind: SmtValueKind.Reference })
            {
                return;
            }

            facts.Add(new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                formula,
                new SmtNullConstant()));
        }

        private static void AddCompletedIfStatementFacts(
            IfStatementSyntax ifStatement,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (StatementDefinitelyExits(ifStatement.Statement) &&
                (ifStatement.Else?.Statement == null ||
                 !AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Else.Statement, semanticModel, cancellationToken)))
            {
                CSharpConditionToFormula.TryCollectBranchAssumptions(
                    ifStatement.Condition,
                    branchWhenTrue: false,
                    semanticModel,
                    cancellationToken,
                    facts);
            }

            if (ifStatement.Else?.Statement is { } elseStatement &&
                StatementDefinitelyExits(elseStatement) &&
                !AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Statement, semanticModel, cancellationToken))
            {
                CSharpConditionToFormula.TryCollectBranchAssumptions(
                    ifStatement.Condition,
                    branchWhenTrue: true,
                    semanticModel,
                    cancellationToken,
                    facts);
            }
        }

        private static void AddCompletedIfStatementFacts(
            IfStatementSyntax ifStatement,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            AddCompletedIfStatementFacts(ifStatement, new[] { symbol }, semanticModel, cancellationToken, facts);
        }

        private static void AddCompletedIfStatementFacts(
            IfStatementSyntax ifStatement,
            IReadOnlyCollection<ISymbol> invalidatedSymbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (StatementDefinitelyExits(ifStatement.Statement) &&
                (ifStatement.Else?.Statement == null ||
                 !AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Else.Statement, semanticModel, cancellationToken)))
            {
                TryAddPathCondition(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, facts);
            }

            if (ifStatement.Else?.Statement is { } elseStatement &&
                StatementDefinitelyExits(elseStatement) &&
                !AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Statement, semanticModel, cancellationToken))
            {
                TryAddPathCondition(ifStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, facts);
            }
        }

        private static void AddAssignedValueFacts(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            RemoveFactsReferencingSymbol(facts, targetSymbol);
            var hasThrowGuard = TryGetThrowGuardedValue(
                valueExpression,
                out var throwGuardedValue,
                out var guardExpression,
                out var guardBranchWhenTrue,
                out var requiresNonNullValue);
            var effectiveValueExpression = hasThrowGuard
                ? throwGuardedValue
                : valueExpression;
            var effectiveValueIsTarget =
                hasThrowGuard &&
                ExpressionMatchesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken);

            if (TryCreateSymbolSmtValue(targetSymbol, out var targetFormula) &&
                !ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken) &&
                CSharpConditionToFormula.TryTranslateValue(
                    effectiveValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion: null) &&
                valueFormula != null &&
                CanCompareSmtValues(targetFormula, valueFormula))
            {
                facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetFormula, valueFormula));
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken))
            {
                AddNullableAssignedValueFacts(targetSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            if (TryCreateBuiltInLengthFormula(targetSymbol, out var targetLengthFormula) &&
                !ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken) &&
                TryCreateBuiltInLengthValueFormula(effectiveValueExpression, semanticModel, cancellationToken, out var valueLengthFormula))
            {
                facts.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, targetLengthFormula, valueLengthFormula));
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken) &&
                TryCreateReferenceBackedLengthFact(targetSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var referenceLengthFact))
            {
                facts.Add(referenceLengthFact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken) &&
                TryCreateCollectionExpressionLengthLowerBoundFact(targetSymbol, effectiveValueExpression, out var lowerBoundLengthFact))
            {
                facts.Add(lowerBoundLengthFact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken))
            {
                AddArrayDimensionLengthAssignedValueFacts(targetSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken))
            {
                AddReferenceBackedArrayDimensionLengthFacts(targetSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            if (TryCreateStringContentFormula(targetSymbol, out var targetStringFormula) &&
                !ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken) &&
                CSharpConditionToFormula.TryTranslateStringValue(
                    effectiveValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueStringFormula,
                    getSymbolVersion: null) &&
                valueStringFormula != null)
            {
                facts.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, targetStringFormula, valueStringFormula));
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken) &&
                TryCreateReferenceBackedStringContentFact(targetSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var referenceStringFact))
            {
                facts.Add(referenceStringFact);
            }

            if (TryCreateSymbolSmtValue(targetSymbol, out var targetReferenceFormula) &&
                targetReferenceFormula is { Kind: SmtValueKind.Reference } &&
                GetTrackedSymbolType(targetSymbol)?.SpecialType == SpecialType.System_String &&
                !ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken) &&
                CSharpConditionToFormula.TryCreateStringNonNullFormula(
                    effectiveValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueNonNullFormula) &&
                valueNonNullFormula != null)
            {
                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtBinaryFormula(
                        SmtBinaryOperator.NotEqual,
                        targetReferenceFormula,
                        new SmtNullConstant()),
                    valueNonNullFormula));
            }

            if (hasThrowGuard &&
                guardExpression != null &&
                (!ExpressionReferencesSymbol(guardExpression, targetSymbol, semanticModel, cancellationToken) ||
                 effectiveValueIsTarget))
            {
                CSharpConditionToFormula.TryCollectBranchAssumptions(
                    guardExpression,
                    guardBranchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    facts);
            }
            else if (hasThrowGuard &&
                     requiresNonNullValue &&
                     !ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken))
            {
                AddReferenceNonNullFact(effectiveValueExpression, semanticModel, cancellationToken, facts);
            }
            else if (hasThrowGuard &&
                     requiresNonNullValue &&
                     effectiveValueIsTarget)
            {
                AddSymbolNonNullFact(targetSymbol, facts);
            }
        }

        private static void AddNullableAssignedValueFacts(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!TryCreateNullableHasValueFormula(targetSymbol, out var targetHasValue) ||
                !TryCreateNullableValueFormula(targetSymbol, out var targetValue))
            {
                return;
            }

            if (CSharpConditionToFormula.TryTranslateNullableValueParts(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var parts,
                    getSymbolVersion: null,
                    inlineDepth: 0))
            {
                facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetHasValue, parts.HasValue));

                if (parts.Value != null &&
                    CanCompareSmtValues(targetValue, parts.Value))
                {
                    facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetValue, parts.Value));
                }
            }
            else if (TryGetNullableUnderlyingType(GetTrackedSymbolType(targetSymbol), out var underlyingType) &&
                     TryTranslateNullableWrappedValueForUnderlyingType(
                         valueExpression,
                         underlyingType,
                         semanticModel,
                         cancellationToken,
                         out var wrappedValueFormula))
            {
                facts.Add(targetHasValue);

                if (CanCompareSmtValues(targetValue, wrappedValueFormula))
                {
                    facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetValue, wrappedValueFormula));
                }
            }
        }

        private static bool TryTranslateNullableWrappedValueForUnderlyingType(
            ExpressionSyntax valueExpression,
            ITypeSymbol underlyingType,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula valueFormula)
        {
            valueExpression = UnwrapFactExpression(valueExpression);
            var typeInfo = semanticModel.GetTypeInfo(valueExpression, cancellationToken);
            if (!SymbolEqualityComparer.Default.Equals(typeInfo.ConvertedType, underlyingType) &&
                !SymbolEqualityComparer.Default.Equals(typeInfo.Type, underlyingType))
            {
                valueFormula = null!;
                return false;
            }

            if (CSharpConditionToFormula.TryTranslateValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var translatedValue,
                    getSymbolVersion: null,
                    inlineDepth: 0) &&
                translatedValue != null)
            {
                valueFormula = translatedValue;
                return true;
            }

            valueFormula = null!;
            return false;
        }

        private static bool TryCreateNullableHasValueFormula(ISymbol symbol, out SmtFormula formula)
        {
            if (!TryGetNullableUnderlyingType(GetTrackedSymbolType(symbol), out _))
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(GetSmtVariableName(symbol) + ".HasValue", SmtValueKind.Bool);
            return true;
        }

        private static bool TryCreateNullableValueFormula(ISymbol symbol, out SmtFormula formula)
        {
            if (!TryGetNullableUnderlyingType(GetTrackedSymbolType(symbol), out var underlyingType) ||
                !TryGetValueKind(underlyingType, out var kind))
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(GetSmtVariableName(symbol) + ".Value", kind);
            return true;
        }

        private static bool TryGetNullableUnderlyingType(ITypeSymbol? type, out ITypeSymbol underlyingType)
        {
            if (type is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                namedType.TypeArguments.Length == 1)
            {
                underlyingType = namedType.TypeArguments[0];
                return true;
            }

            underlyingType = null!;
            return false;
        }

        private static bool TryCreateReferenceBackedLengthFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula fact)
        {
            fact = null!;
            return TryCreateSymbolSmtValue(targetSymbol, out var targetReference) &&
                SymbolicFactFactory.TryCreateReferenceBackedLengthFact(
                    targetReference,
                    valueExpression,
                    UnwrapFactExpression(valueExpression),
                    semanticModel,
                    cancellationToken,
                    (expression, model, token) =>
                        TryCreateBuiltInLengthValueFormula(expression, model, token, out var formula)
                            ? formula
                            : null,
                    out fact);
        }

        private static void AddReferenceBackedArrayDimensionLengthFacts(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            var valueType = semanticModel.GetTypeInfo(UnwrapFactExpression(valueExpression), cancellationToken).Type;
            if (valueType is not IArrayTypeSymbol { Rank: > 1 } arrayType ||
                !TryCreateSymbolSmtValue(targetSymbol, out var targetReference) ||
                targetReference is not { Kind: SmtValueKind.Reference })
            {
                return;
            }

            for (var dimension = 0; dimension < arrayType.Rank; dimension++)
            {
                if (!SymbolicFactFactory.TryCreateReferenceArrayDimensionLengthFormula(targetReference, dimension, out var targetDimensionLength) ||
                    !TryCreateArrayDimensionLengthValueFormula(valueExpression, dimension, semanticModel, cancellationToken, out var valueDimensionLength))
                {
                    continue;
                }

                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    targetDimensionLength,
                    valueDimensionLength));
            }
        }

        private static bool TryCreateReferenceBackedStringContentFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula fact)
        {
            fact = null!;
            return TryCreateSymbolSmtValue(targetSymbol, out var targetReference) &&
                SymbolicFactFactory.TryCreateReferenceBackedStringContentFact(
                    targetReference,
                    valueExpression,
                    UnwrapFactExpression(valueExpression),
                    semanticModel,
                    cancellationToken,
                    (expression, model, token) =>
                        CSharpConditionToFormula.TryTranslateStringValue(
                            expression,
                            model,
                            token,
                            out var valueString,
                            getSymbolVersion: null) &&
                        valueString != null
                            ? valueString
                            : null,
                    out fact);
        }

        private static bool TryCreateCollectionExpressionLengthLowerBoundFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            out SmtFormula fact)
        {
            fact = null!;
            return TryCreateBuiltInLengthFormula(targetSymbol, out var targetLengthFormula) &&
                SymbolicFactFactory.TryCreateCollectionExpressionLengthLowerBoundFact(
                    targetLengthFormula,
                    UnwrapFactExpression(valueExpression),
                    out fact);
        }

        private static void AddArrayDimensionLengthAssignedValueFacts(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (GetTrackedSymbolType(targetSymbol) is not IArrayTypeSymbol { Rank: > 1 } targetArrayType)
            {
                return;
            }

            for (var dimension = 0; dimension < targetArrayType.Rank; dimension++)
            {
                if (!TryCreateArrayDimensionLengthFormula(targetSymbol, dimension, out var targetDimensionLength) ||
                    !TryCreateArrayDimensionLengthValueFormula(valueExpression, dimension, semanticModel, cancellationToken, out var valueDimensionLength))
                {
                    continue;
                }

                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    targetDimensionLength,
                    valueDimensionLength));
            }
        }

        private static bool TryCreateArrayDimensionLengthValueFormula(
            ExpressionSyntax valueExpression,
            int dimension,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            valueExpression = UnwrapFactExpression(valueExpression);
            if (valueExpression is ArrayCreationExpressionSyntax arrayCreation &&
                TryGetExplicitArraySizeExpression(arrayCreation, dimension, out var sizeExpression) &&
                CSharpConditionToFormula.TryTranslateValue(
                    sizeExpression,
                    semanticModel,
                    cancellationToken,
                    out var sizeFormula,
                    getSymbolVersion: null) &&
                sizeFormula is { Kind: SmtValueKind.Int })
            {
                formula = sizeFormula;
                return true;
            }

            var sourceSymbol = GetLocalOrParameterSymbol(valueExpression, semanticModel, cancellationToken);
            if (sourceSymbol != null &&
                TryCreateArrayDimensionLengthFormula(sourceSymbol, dimension, out formula))
            {
                return true;
            }

            formula = null!;
            return false;
        }

        private static IEnumerable<ExpressionSyntax> GetExplicitArraySizeExpressions(ArrayCreationExpressionSyntax arrayCreation)
        {
            foreach (var rankSpecifier in arrayCreation.Type.RankSpecifiers)
            {
                foreach (var sizeExpression in rankSpecifier.Sizes)
                {
                    if (!sizeExpression.IsKind(SyntaxKind.OmittedArraySizeExpression))
                    {
                        yield return sizeExpression;
                    }
                }
            }
        }

        private static bool TryGetExplicitArraySizeExpression(
            ArrayCreationExpressionSyntax arrayCreation,
            int dimension,
            out ExpressionSyntax sizeExpression)
        {
            if (dimension >= 0 &&
                arrayCreation.Type.RankSpecifiers.Count == 1 &&
                arrayCreation.Type.RankSpecifiers[0].Sizes.Count > dimension &&
                !arrayCreation.Type.RankSpecifiers[0].Sizes[dimension].IsKind(SyntaxKind.OmittedArraySizeExpression))
            {
                sizeExpression = arrayCreation.Type.RankSpecifiers[0].Sizes[dimension];
                return true;
            }

            sizeExpression = null!;
            return false;
        }

        private static bool TryGetThrowGuardedValue(
            ExpressionSyntax valueExpression,
            out ExpressionSyntax effectiveValueExpression,
            out ExpressionSyntax? guardExpression,
            out bool guardBranchWhenTrue,
            out bool requiresNonNullValue)
        {
            valueExpression = UnwrapFactExpression(valueExpression);
            if (valueExpression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                UnwrapFactExpression(coalesceExpression.Right) is ThrowExpressionSyntax)
            {
                effectiveValueExpression = coalesceExpression.Left;
                guardExpression = null;
                guardBranchWhenTrue = true;
                requiresNonNullValue = true;
                return true;
            }

            if (valueExpression is ConditionalExpressionSyntax conditionalExpression)
            {
                if (UnwrapFactExpression(conditionalExpression.WhenFalse) is ThrowExpressionSyntax)
                {
                    effectiveValueExpression = conditionalExpression.WhenTrue;
                    guardExpression = conditionalExpression.Condition;
                    guardBranchWhenTrue = true;
                    requiresNonNullValue = false;
                    return true;
                }

                if (UnwrapFactExpression(conditionalExpression.WhenTrue) is ThrowExpressionSyntax)
                {
                    effectiveValueExpression = conditionalExpression.WhenFalse;
                    guardExpression = conditionalExpression.Condition;
                    guardBranchWhenTrue = false;
                    requiresNonNullValue = false;
                    return true;
                }
            }

            effectiveValueExpression = null!;
            guardExpression = null;
            guardBranchWhenTrue = true;
            requiresNonNullValue = false;
            return false;
        }

        private static void AddReferenceNonNullFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var formula,
                    getSymbolVersion: null) ||
                formula is not { Kind: SmtValueKind.Reference })
            {
                return;
            }

            facts.Add(new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                formula,
                new SmtNullConstant()));
        }

        private static void RemoveFactsInvalidatedByNestedMutations(
            SyntaxNode root,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                if (TryGetMutatedLocalOrParameterSymbol(node, semanticModel, cancellationToken, out var mutatedSymbol))
                {
                    RemoveFactsReferencingSymbol(facts, mutatedSymbol);
                }
            }
        }

        private static ITypeSymbol? GetTrackedSymbolType(ISymbol symbol)
        {
            return symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };
        }

        private static bool TryCreateSymbolSmtValue(ISymbol symbol, out SmtFormula formula)
        {
            var type = symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type == null)
            {
                formula = null!;
                return false;
            }

            var variableName = GetSmtVariableName(symbol);
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Bool);
                return true;
            }

            if (IsSearchLibIntegralType(type))
            {
                formula = new SmtVariable(variableName, SmtValueKind.Int);
                return true;
            }

            if (IsReferenceType(type))
            {
                formula = new SmtVariable(variableName, SmtValueKind.Reference);
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                kind = SmtValueKind.Bool;
                return true;
            }

            if (IsSearchLibIntegralType(type))
            {
                kind = SmtValueKind.Int;
                return true;
            }

            if (IsReferenceType(type))
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryCreateStringContentFormula(ISymbol symbol, out SmtFormula formula)
        {
            var type = symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type?.SpecialType == SpecialType.System_String)
            {
                formula = new SmtVariable(GetSmtVariableName(symbol) + ".String", SmtValueKind.String);
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateBuiltInLengthFormula(ISymbol symbol, out SmtFormula formula)
        {
            var type = symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type?.SpecialType == SpecialType.System_String)
            {
                formula = new SmtStringLengthTerm(new SmtVariable(GetSmtVariableName(symbol) + ".String", SmtValueKind.String));
                return true;
            }

            if (type is IArrayTypeSymbol { Rank: 1 } ||
                IsBuiltInSpanOrMemoryType(type))
            {
                var receiverFormula = new SmtVariable(GetSmtVariableName(symbol), SmtValueKind.Reference);
                return SymbolicFactFactory.TryCreateReferenceBuiltInLengthFormula(receiverFormula, out formula);
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateArrayDimensionLengthFormula(
            ISymbol symbol,
            int dimension,
            out SmtFormula formula)
        {
            if (dimension < 0 ||
                GetTrackedSymbolType(symbol) is not IArrayTypeSymbol arrayType ||
                dimension >= arrayType.Rank ||
                !TryCreateSymbolSmtValue(symbol, out var receiverFormula) ||
                receiverFormula is not { Kind: SmtValueKind.Reference })
            {
                formula = null!;
                return false;
            }

            return SymbolicFactFactory.TryCreateReferenceArrayDimensionLengthFormula(receiverFormula, dimension, out formula);
        }

        private static bool TryCreateBuiltInLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            return CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                valueExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion: null);
        }

        private static bool TryHandleTupleDeconstructionDeclaration(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapFactExpression(assignment.Left) is not DeclarationExpressionSyntax declarationExpression ||
                declarationExpression.Designation is not ParenthesizedVariableDesignationSyntax leftDesignation ||
                UnwrapFactExpression(assignment.Right) is not TupleExpressionSyntax rightTuple ||
                rightTuple.Arguments.Count != leftDesignation.Variables.Count)
            {
                return false;
            }

            var targetSymbols = new List<ISymbol>();
            foreach (var variableDesignation in leftDesignation.Variables)
            {
                if (variableDesignation is not SingleVariableDesignationSyntax singleVariableDesignation ||
                    singleVariableDesignation.Identifier.ValueText == "_" ||
                    semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is not ILocalSymbol localSymbol)
                {
                    return true;
                }

                targetSymbols.Add(localSymbol.OriginalDefinition);
            }

            for (var index = 0; index < targetSymbols.Count; index++)
            {
                AddAssignedValueFacts(
                    targetSymbols[index],
                    rightTuple.Arguments[index].Expression,
                    semanticModel,
                    cancellationToken,
                    facts);
            }

            return true;
        }

        private static bool TryHandleTupleAssignment(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapFactExpression(assignment.Left) is not TupleExpressionSyntax leftTuple)
            {
                return false;
            }

            var targetSymbols = new List<ISymbol>();
            foreach (var argument in leftTuple.Arguments)
            {
                if (GetLocalOrParameterSymbol(argument.Expression, semanticModel, cancellationToken) is { } targetSymbol)
                {
                    targetSymbols.Add(targetSymbol);
                }
            }

            foreach (var targetSymbol in targetSymbols)
            {
                RemoveFactsReferencingSymbol(facts, targetSymbol);
            }

            if (targetSymbols.Count != leftTuple.Arguments.Count ||
                UnwrapFactExpression(assignment.Right) is not TupleExpressionSyntax rightTuple ||
                rightTuple.Arguments.Count != leftTuple.Arguments.Count ||
                rightTuple.Arguments.Any(argument => ExpressionReferencesAnySymbol(argument.Expression, targetSymbols, semanticModel, cancellationToken)))
            {
                return true;
            }

            for (var index = 0; index < leftTuple.Arguments.Count; index++)
            {
                AddAssignedValueFacts(
                    targetSymbols[index],
                    rightTuple.Arguments[index].Expression,
                    semanticModel,
                    cancellationToken,
                    facts);
            }

            return true;
        }

        private static bool TryCreateCompoundAssignmentFact(
            ISymbol targetSymbol,
            SmtFormula previousValue,
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula) ||
                targetFormula.Kind != SmtValueKind.Int ||
                previousValue.Kind != SmtValueKind.Int ||
                ReferencesSmtVariable(previousValue, GetSmtVariableName(targetSymbol)) ||
                ExpressionReferencesSymbol(assignment.Right, targetSymbol, semanticModel, cancellationToken) ||
                !CSharpConditionToFormula.TryTranslateValue(
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightValue,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                rightValue is not { Kind: SmtValueKind.Int } ||
                !TryCreateCompoundAssignmentValue(assignment.Kind(), previousValue, rightValue, out var updatedValue))
            {
                return false;
            }

            fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, updatedValue);
            return true;
        }

        private static bool TryCreateIncrementOrDecrementFact(
            ISymbol targetSymbol,
            SmtFormula previousValue,
            int delta,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula) ||
                targetFormula.Kind != SmtValueKind.Int ||
                previousValue.Kind != SmtValueKind.Int ||
                ReferencesSmtVariable(previousValue, GetSmtVariableName(targetSymbol)))
            {
                return false;
            }

            var updatedValue = delta > 0
                ? new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, previousValue, new SmtIntegerConstant(delta))
                : new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, previousValue, new SmtIntegerConstant(-delta));
            fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, updatedValue);
            return true;
        }

        private static bool TryCreateCompoundAssignmentValue(
            SyntaxKind assignmentKind,
            SmtFormula previousValue,
            SmtFormula rightValue,
            out SmtFormula updatedValue)
        {
            switch (assignmentKind)
            {
                case SyntaxKind.AddAssignmentExpression:
                    updatedValue = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, previousValue, rightValue);
                    return true;
                case SyntaxKind.SubtractAssignmentExpression:
                    updatedValue = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, previousValue, rightValue);
                    return true;
                case SyntaxKind.MultiplyAssignmentExpression
                    when previousValue is SmtIntegerConstant || rightValue is SmtIntegerConstant:
                    updatedValue = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, previousValue, rightValue);
                    return true;
                default:
                    updatedValue = null!;
                    return false;
            }
        }

        private static bool TryGetCurrentSymbolValue(
            List<SmtFormula> facts,
            ISymbol symbol,
            out SmtFormula value)
        {
            value = null!;
            if (!TryCreateSymbolSmtValue(symbol, out var targetFormula))
            {
                return false;
            }

            for (var index = facts.Count - 1; index >= 0; index--)
            {
                if (facts[index] is not SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.Equal,
                        Left: var left,
                        Right: var right
                    })
                {
                    continue;
                }

                if (Equals(left, targetFormula) && right.Kind == targetFormula.Kind)
                {
                    value = right;
                    return true;
                }

                if (Equals(right, targetFormula) && left.Kind == targetFormula.Kind)
                {
                    value = left;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetIncrementedOrDecrementedSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ISymbol symbol,
            out int delta)
        {
            expression = UnwrapFactExpression(expression);
            ExpressionSyntax? operand = expression switch
            {
                PrefixUnaryExpressionSyntax prefixUnary
                    when prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) || prefixUnary.IsKind(SyntaxKind.PreDecrementExpression) =>
                    prefixUnary.Operand,
                PostfixUnaryExpressionSyntax postfixUnary
                    when postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) || postfixUnary.IsKind(SyntaxKind.PostDecrementExpression) =>
                    postfixUnary.Operand,
                _ => null
            };

            var expressionSymbol = operand == null
                ? null
                : semanticModel.GetSymbolInfo(operand, cancellationToken).Symbol;
            if (expressionSymbol is not ILocalSymbol && expressionSymbol is not IParameterSymbol)
            {
                symbol = null!;
                delta = 0;
                return false;
            }

            symbol = expressionSymbol.OriginalDefinition;
            delta = expression.IsKind(SyntaxKind.PreIncrementExpression) ||
                expression.IsKind(SyntaxKind.PostIncrementExpression)
                    ? 1
                    : -1;
            return true;
        }

        private static bool CanCompareSmtValues(SmtFormula left, SmtFormula right)
        {
            return left.Kind == right.Kind ||
                left is SmtNullConstant && right.Kind == SmtValueKind.Reference ||
                right is SmtNullConstant && left.Kind == SmtValueKind.Reference;
        }

        private static void RemoveFactsReferencingSymbol(List<SmtFormula> facts, ISymbol symbol)
        {
            var variablePrefix = GetSmtVariableName(symbol);
            for (var index = facts.Count - 1; index >= 0; index--)
            {
                if (ReferencesSmtVariable(facts[index], variablePrefix))
                {
                    facts.RemoveAt(index);
                }
            }
        }

        private static bool ReferencesSmtVariable(SmtFormula formula, string variablePrefix)
        {
            switch (formula)
            {
                case SmtVariable variable:
                    return variable.Name.Contains(variablePrefix, System.StringComparison.Ordinal);
                case SmtUnaryFormula unary:
                    return ReferencesSmtVariable(unary.Operand, variablePrefix);
                case SmtBinaryFormula binary:
                    return ReferencesSmtVariable(binary.Left, variablePrefix) ||
                        ReferencesSmtVariable(binary.Right, variablePrefix);
                case SmtIntegerUnaryTerm integerUnary:
                    return ReferencesSmtVariable(integerUnary.Operand, variablePrefix);
                case SmtIntegerBinaryTerm integerBinary:
                    return ReferencesSmtVariable(integerBinary.Left, variablePrefix) ||
                        ReferencesSmtVariable(integerBinary.Right, variablePrefix);
                case SmtStringLengthTerm stringLength:
                    return ReferencesSmtVariable(stringLength.Value, variablePrefix);
                case SmtStringConcatTerm stringConcat:
                    return ReferencesSmtVariable(stringConcat.Left, variablePrefix) ||
                        ReferencesSmtVariable(stringConcat.Right, variablePrefix);
                case SmtStringContainsFormula stringContains:
                    return ReferencesSmtVariable(stringContains.Value, variablePrefix) ||
                        ReferencesSmtVariable(stringContains.Search, variablePrefix);
                case SmtStringStartsWithFormula stringStartsWith:
                    return ReferencesSmtVariable(stringStartsWith.Value, variablePrefix) ||
                        ReferencesSmtVariable(stringStartsWith.Prefix, variablePrefix);
                case SmtStringEndsWithFormula stringEndsWith:
                    return ReferencesSmtVariable(stringEndsWith.Value, variablePrefix) ||
                        ReferencesSmtVariable(stringEndsWith.Suffix, variablePrefix);
                case SmtRegexMatchFormula regexMatch:
                    return ReferencesSmtVariable(regexMatch.Value, variablePrefix);
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    return ReferencesSmtVariable(runtimeTypeTest.Value, variablePrefix);
                case SmtConditionalFormula conditional:
                    return ReferencesSmtVariable(conditional.Condition, variablePrefix) ||
                        ReferencesSmtVariable(conditional.WhenTrue, variablePrefix) ||
                        ReferencesSmtVariable(conditional.WhenFalse, variablePrefix);
                default:
                    return false;
            }
        }

        private static ISymbol? GetLocalOrParameterSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            expression = UnwrapFactExpression(expression);
            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            return symbol is ILocalSymbol or IParameterSymbol ? symbol.OriginalDefinition : null;
        }

        private static ExpressionSyntax UnwrapFactExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                return expression;
            }
        }

        private static bool ExpressionMatchesSymbol(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var expressionSymbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            return expressionSymbol != null && SymbolEqualityComparer.Default.Equals(expressionSymbol, symbol);
        }

        private static bool ExpressionReferencesSymbol(
            SyntaxNode root,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                if (node is ExpressionSyntax expression &&
                    ExpressionMatchesSymbol(expression, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExpressionReferencesAnySymbol(
            SyntaxNode root,
            IReadOnlyCollection<ISymbol> symbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var symbol in symbols)
            {
                if (ExpressionReferencesSymbol(root, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExpressionMatchesFact(
            ExpressionSyntax expression,
            PathFactKind factKind,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            expression = UnwrapFactExpression(expression);
            if (factKind == PathFactKind.Null)
            {
                return expression.IsKind(SyntaxKind.NullLiteralExpression) ||
                    IsDefaultReferenceExpression(expression, semanticModel, cancellationToken);
            }

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            return constantValue.HasValue && IsIntegralOrDecimalZero(constantValue.Value) ||
                IsDefaultIntegralExpression(expression, semanticModel, cancellationToken);
        }

        private static bool IsDefaultReferenceExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return IsDefaultExpressionSyntax(expression) &&
                IsReferenceType(GetExpressionType(expression, semanticModel, cancellationToken));
        }

        private static bool IsDefaultIntegralExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var type = GetExpressionType(expression, semanticModel, cancellationToken);
            return IsDefaultExpressionSyntax(expression) &&
                type != null &&
                IsSearchLibIntegralType(type);
        }

        private static bool IsDefaultExpressionSyntax(ExpressionSyntax expression)
        {
            return expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
                expression is DefaultExpressionSyntax;
        }

        private static ITypeSymbol? GetExpressionType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return typeInfo.ConvertedType ?? typeInfo.Type;
        }

        private static bool TryCreateFactFormula(
            ISymbol symbol,
            PathFactKind factKind,
            out SmtFormula? factFormula)
        {
            factFormula = null;
            var variableName = GetSmtVariableName(symbol);
            switch (symbol)
            {
                case ILocalSymbol localSymbol:
                    return TryCreateFactFormula(localSymbol.Type, variableName, factKind, out factFormula);
                case IParameterSymbol parameterSymbol:
                    return TryCreateFactFormula(parameterSymbol.Type, variableName, factKind, out factFormula);
                default:
                    return false;
            }
        }

        private static bool TryCreateFactFormula(
            ExpressionSyntax expression,
            PathFactKind factKind,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula? factFormula)
        {
            factFormula = null;
            if (!CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion: null) ||
                valueFormula == null)
            {
                var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
                return symbol != null && TryCreateFactFormula(symbol, factKind, out factFormula);
            }

            if (factKind == PathFactKind.Null)
            {
                if (valueFormula.Kind != SmtValueKind.Reference)
                {
                    return false;
                }

                factFormula = new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    valueFormula,
                    new SmtNullConstant());
                return true;
            }

            if (valueFormula.Kind != SmtValueKind.Int)
            {
                return false;
            }

            factFormula = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                valueFormula,
                new SmtIntegerConstant(0));
            return true;
        }

        private static bool TryCreateFactFormula(
            ITypeSymbol typeSymbol,
            string variableName,
            PathFactKind factKind,
            out SmtFormula? factFormula)
        {
            factFormula = null;
            if (factKind == PathFactKind.Null)
            {
                if (!IsReferenceType(typeSymbol))
                {
                    return false;
                }

                factFormula = new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtVariable(variableName, SmtValueKind.Reference),
                    new SmtNullConstant());
                return true;
            }

            if (!IsSearchLibIntegralType(typeSymbol))
            {
                return false;
            }

            factFormula = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtVariable(variableName, SmtValueKind.Int),
                new SmtIntegerConstant(0));
            return true;
        }

        private static bool IsSearchLibIntegralType(ITypeSymbol typeSymbol)
        {
            if (typeSymbol.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64)
            {
                return true;
            }

            return typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType } &&
                IsSearchLibIntegralType(underlyingType);
        }

        private static string GetSmtVariableName(ISymbol symbol)
        {
            var firstLocation = symbol.Locations.FirstOrDefault();
            var start = firstLocation?.SourceSpan.Start ?? 0;
            return symbol.Name + "#" + start.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void TryAddPathCondition(
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            CSharpConditionToFormula.TryCollectBranchAssumptions(
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                pathConditions);
        }

        private static void TryAddReferenceNullCondition(
            ExpressionSyntax expression,
            bool isNull,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            if (TryGetSyntacticReferenceNullState(expression, semanticModel, cancellationToken, out var isDefinitelyNull))
            {
                if (isNull != isDefinitelyNull)
                {
                    pathConditions.Add(new SmtBooleanConstant(false));
                }

                return;
            }

            if (!SymbolicReachabilityService.TryCreateReferenceNullComparison(
                    expression,
                    semanticModel,
                    cancellationToken,
                    equalToNull: isNull,
                    out var formula))
            {
                return;
            }

            pathConditions.Add(formula);
        }

        private static bool TryGetSyntacticReferenceNullState(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out bool isNull)
        {
            expression = UnwrapFactExpression(expression);
            if (expression is CastExpressionSyntax castExpression)
            {
                if (!IsReferenceType(GetExpressionType(castExpression, semanticModel, cancellationToken)))
                {
                    isNull = false;
                    return false;
                }

                return TryGetSyntacticReferenceNullState(castExpression.Expression, semanticModel, cancellationToken, out isNull);
            }

            if (expression.IsKind(SyntaxKind.NullLiteralExpression) ||
                IsDefaultReferenceExpression(expression, semanticModel, cancellationToken))
            {
                isNull = true;
                return true;
            }

            var expressionType = GetExpressionType(expression, semanticModel, cancellationToken);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue)
            {
                if (constantValue.Value == null && IsReferenceType(expressionType))
                {
                    isNull = true;
                    return true;
                }

                if (constantValue.Value is string)
                {
                    isNull = false;
                    return true;
                }
            }

            if (IsSyntacticallyNonNullReferenceExpression(expression, expressionType))
            {
                isNull = false;
                return true;
            }

            isNull = false;
            return false;
        }

        private static bool IsSyntacticallyNonNullReferenceExpression(
            ExpressionSyntax expression,
            ITypeSymbol? expressionType)
        {
            if (!IsReferenceType(expressionType))
            {
                return false;
            }

            return expression switch
            {
                ThisExpressionSyntax => true,
                BaseExpressionSyntax => true,
                ObjectCreationExpressionSyntax => true,
                ImplicitObjectCreationExpressionSyntax => true,
                AnonymousObjectCreationExpressionSyntax => true,
                ArrayCreationExpressionSyntax => true,
                ImplicitArrayCreationExpressionSyntax => true,
                InterpolatedStringExpressionSyntax => true,
                TypeOfExpressionSyntax => true,
                CollectionExpressionSyntax when expressionType is IArrayTypeSymbol => true,
                _ => false
            };
        }

        private static void TryAddCoalesceRightPathCondition(
            ExpressionSyntax leftExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            var originalCount = pathConditions.Count;
            TryAddReferenceNullCondition(leftExpression, isNull: true, semanticModel, cancellationToken, pathConditions);
            if (pathConditions.Count != originalCount)
            {
                return;
            }

            leftExpression = UnwrapFactExpression(leftExpression);
            if (leftExpression is ConditionalAccessExpressionSyntax conditionalAccess &&
                ConditionalAccessFallbackRequiresNullReceiver(conditionalAccess, semanticModel, cancellationToken))
            {
                TryAddReferenceNullCondition(conditionalAccess.Expression, isNull: true, semanticModel, cancellationToken, pathConditions);
            }
        }

        private static bool ConditionalAccessFallbackRequiresNullReceiver(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var whenNotNullType = GetConditionalAccessWhenNotNullType(
                conditionalAccess.WhenNotNull,
                semanticModel,
                cancellationToken);
            return IsKnownNonNullableValueType(whenNotNullType);
        }

        private static ITypeSymbol? GetConditionalAccessWhenNotNullType(
            ExpressionSyntax whenNotNullExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(whenNotNullExpression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type != null)
            {
                return type;
            }

            var symbol = semanticModel.GetSymbolInfo(whenNotNullExpression, cancellationToken).Symbol;
            return symbol switch
            {
                IFieldSymbol fieldSymbol => fieldSymbol.Type,
                IPropertySymbol propertySymbol => propertySymbol.Type,
                IEventSymbol eventSymbol => eventSymbol.Type,
                IMethodSymbol methodSymbol => methodSymbol.ReturnType,
                _ => null
            };
        }

        private static bool IsKnownNonNullableValueType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol?.IsValueType == true &&
                typeSymbol.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
        }

        private static bool IsSymbolAssignedBeforeUse(
            SyntaxNode branchRoot,
            int useSpanStart,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return IsSymbolAssignedBetween(branchRoot, branchRoot.SpanStart - 1, useSpanStart, symbol, semanticModel, cancellationToken);
        }

        private static bool AnySymbolAssignedBeforeUse(
            SyntaxNode branchRoot,
            int useSpanStart,
            IReadOnlyCollection<ISymbol> symbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return AnySymbolAssignedBetween(branchRoot, branchRoot.SpanStart - 1, useSpanStart, symbols, semanticModel, cancellationToken);
        }

        private static bool AnyReferencedSymbolAssignedBeforeUse(
            SyntaxNode condition,
            SyntaxNode branchRoot,
            int useSpanStart,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var referencedSymbols = GetReferencedLocalAndParameterSymbols(condition, semanticModel, cancellationToken);
            return referencedSymbols.Count != 0 &&
                AnySymbolAssignedBeforeUse(branchRoot, useSpanStart, referencedSymbols, semanticModel, cancellationToken);
        }

        private static bool AnyReferencedSymbolAssignedBetween(
            SyntaxNode condition,
            SyntaxNode root,
            int afterSpanStart,
            int beforeSpanStart,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var referencedSymbols = GetReferencedLocalAndParameterSymbols(condition, semanticModel, cancellationToken);
            return referencedSymbols.Count != 0 &&
                AnySymbolAssignedBetween(root, afterSpanStart, beforeSpanStart, referencedSymbols, semanticModel, cancellationToken);
        }

        private static bool IsSymbolAssignedBetween(
            SyntaxNode root,
            int afterSpanStart,
            int beforeSpanStart,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var node in root.DescendantNodes(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                if (node.SpanStart <= afterSpanStart || node.SpanStart >= beforeSpanStart)
                {
                    continue;
                }

                if (TryGetMutatedLocalOrParameterSymbol(node, semanticModel, cancellationToken, out var mutatedSymbol) &&
                    SymbolEqualityComparer.Default.Equals(mutatedSymbol, symbol) ||
                    MutatesSymbol(node, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnySymbolAssignedBetween(
            SyntaxNode root,
            int afterSpanStart,
            int beforeSpanStart,
            IReadOnlyCollection<ISymbol> symbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (symbols.Count == 0)
            {
                return false;
            }

            foreach (var symbol in symbols)
            {
                if (IsSymbolAssignedBetween(root, afterSpanStart, beforeSpanStart, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnySymbolMutatedInSyntax(
            SyntaxNode root,
            IReadOnlyCollection<ISymbol> symbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (symbols.Count == 0)
            {
                return false;
            }

            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                if (!TryGetMutatedLocalOrParameterSymbol(node, semanticModel, cancellationToken, out var mutatedSymbol))
                {
                    continue;
                }

                foreach (var symbol in symbols)
                {
                    if (SymbolEqualityComparer.Default.Equals(mutatedSymbol, symbol))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool MutatesSymbol(
            SyntaxNode node,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return node switch
            {
                AssignmentExpressionSyntax assignment =>
                    ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken) ||
                    TupleAssignmentMutatesSymbol(assignment, symbol, semanticModel, cancellationToken),
                PrefixUnaryExpressionSyntax prefixUnary
                    when prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) || prefixUnary.IsKind(SyntaxKind.PreDecrementExpression) =>
                    ExpressionMatchesSymbol(prefixUnary.Operand, symbol, semanticModel, cancellationToken),
                PostfixUnaryExpressionSyntax postfixUnary
                    when postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) || postfixUnary.IsKind(SyntaxKind.PostDecrementExpression) =>
                    ExpressionMatchesSymbol(postfixUnary.Operand, symbol, semanticModel, cancellationToken),
                ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) =>
                    ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken),
                _ => false
            };
        }

        private static bool TupleAssignmentMutatesSymbol(
            AssignmentExpressionSyntax assignment,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (UnwrapFactExpression(assignment.Left) is not TupleExpressionSyntax leftTuple)
            {
                return false;
            }

            return leftTuple.Arguments.Any(argument =>
                ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken));
        }

        private static bool TryGetMutatedLocalOrParameterSymbol(
            SyntaxNode node,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ISymbol symbol)
        {
            symbol = null!;
            ExpressionSyntax? mutatedExpression = node switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left,
                PrefixUnaryExpressionSyntax prefixUnary
                    when prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) || prefixUnary.IsKind(SyntaxKind.PreDecrementExpression) =>
                    prefixUnary.Operand,
                PostfixUnaryExpressionSyntax postfixUnary
                    when postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) || postfixUnary.IsKind(SyntaxKind.PostDecrementExpression) =>
                    postfixUnary.Operand,
                ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) => argument.Expression,
                _ => null
            };

            if (mutatedExpression == null)
            {
                return false;
            }

            var candidate = GetLocalOrParameterSymbol(mutatedExpression, semanticModel, cancellationToken);
            if (candidate == null)
            {
                return false;
            }

            symbol = candidate;
            return true;
        }

        private static bool StatementDefinitelyExits(StatementSyntax statement)
        {
            switch (statement)
            {
                case ReturnStatementSyntax:
                case ThrowStatementSyntax:
                case ContinueStatementSyntax:
                case BreakStatementSyntax:
                    return true;
                case YieldStatementSyntax yieldStatement:
                    return yieldStatement.IsKind(SyntaxKind.YieldBreakStatement);
                case BlockSyntax block:
                    return block.Statements.LastOrDefault() is { } lastStatement &&
                        StatementDefinitelyExits(lastStatement);
                case IfStatementSyntax ifStatement:
                    return StatementDefinitelyExits(ifStatement.Statement) &&
                        ifStatement.Else?.Statement is { } elseStatement &&
                        StatementDefinitelyExits(elseStatement);
                default:
                    return false;
            }
        }
    }
}
