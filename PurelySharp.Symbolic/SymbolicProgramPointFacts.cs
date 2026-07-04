using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    internal static class SymbolicProgramPointFacts
    {
        private const int MaxMergedIfElseFacts = 16;
        private const int MaxMergedSwitchFacts = 32;
        private const int MaxMergedTryFacts = 16;
        private const int MaxTryCompletionBranches = 8;
        private const int MaxFiniteForeachElementFacts = 8;
        private const int MaxScopedBlockCompletionStatements = 32;
        private const int MaxStructuralNullStateDepth = 4;
        private const string DoesNotReturnAttributeName = "System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute";
        private const string DoesNotReturnIfAttributeName = "System.Diagnostics.CodeAnalysis.DoesNotReturnIfAttribute";
        private const string ImplicitThisVariableName = "this";
        private const string MemberNotNullAttributeName = "System.Diagnostics.CodeAnalysis.MemberNotNullAttribute";
        private const string MemberNotNullWhenAttributeName = "System.Diagnostics.CodeAnalysis.MemberNotNullWhenAttribute";
        private const string NotNullAttributeName = "System.Diagnostics.CodeAnalysis.NotNullAttribute";
        internal static List<SmtFormula> CollectPriorAssignmentFacts(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool includeCurrentStatementCompletionFacts = false)
        {
            var facts = new List<SmtFormula>();
            foreach (var containingBlock in EnumerateContainingBlocks(site).Reverse())
            {
                if (IsLoopBodyBlock(containingBlock.Block))
                {
                    RemoveFactsInvalidatedByNestedMutations(containingBlock.Block, semanticModel, cancellationToken, facts);
                }

                RemoveFactsInvalidatedByForLoopEntry(containingBlock.Block, semanticModel, cancellationToken, facts);
                var temporaryEntryFacts = AddTemporaryContainingBlockEntryFacts(
                    containingBlock.Block,
                    semanticModel,
                    cancellationToken,
                    facts);
                foreach (var statement in containingBlock.Block.Statements)
                {
                    if (ReferenceEquals(statement, containingBlock.ContainingStatement))
                    {
                        if (includeCurrentStatementCompletionFacts &&
                            ReferenceEquals(site, statement) &&
                            SupportsCurrentStatementCompletionFacts(statement))
                        {
                            AddPriorStatementFacts(statement, semanticModel, cancellationToken, facts);
                        }

                        break;
                    }

                    AddPriorStatementFacts(statement, semanticModel, cancellationToken, facts);
                }

                RemoveTemporaryFacts(facts, temporaryEntryFacts);
            }

            if (site is BlockSyntax siteBlock)
            {
                AddContainingBlockEntryFacts(siteBlock, semanticModel, cancellationToken, facts);
            }

            return facts;
        }

        internal static SymbolicState CollectPriorAssignmentState(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool includeCurrentStatementCompletionFacts = false)
        {
            var state = new SymbolicState();
            AddFormulaPathConditions(
                ref state,
                CollectPriorAssignmentFacts(site, semanticModel, cancellationToken, includeCurrentStatementCompletionFacts),
                site,
                "ir.path.prior-statement");
            return state;
        }

        internal static SymbolicState CollectForInitialEntryState(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var state = CollectAncestorReachabilityState(forStatement, semanticModel, cancellationToken);
            state = MergeStates(
                state,
                CollectPriorAssignmentState(forStatement, semanticModel, cancellationToken));
            state = MergeStates(
                state,
                CollectForInitializerState(forStatement, semanticModel, cancellationToken));
            return state;
        }

        internal static SymbolicState MergeStates(SymbolicState left, SymbolicState right)
        {
            var symbolVersions = left.SymbolVersions.SetItems(right.SymbolVersions);
            return new SymbolicState(
                left.Facts.Concat(right.Facts),
                left.PathConditions.Concat(right.PathConditions),
                symbolVersions,
                left.IsContradictory || right.IsContradictory).Normalize();
        }

        private static bool SupportsCurrentStatementCompletionFacts(StatementSyntax statement)
        {
            return statement is LocalDeclarationStatementSyntax or
                ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax };
        }

        private static void RemoveFactsInvalidatedByForLoopEntry(
            BlockSyntax block,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (block.Parent is not ForStatementSyntax forStatement ||
                !ReferenceEquals(forStatement.Statement, block))
            {
                return;
            }

            foreach (var symbol in GetForLoopInitializerAssignedSymbols(forStatement, semanticModel, cancellationToken))
            {
                RemoveFactsReferencingSymbol(facts, symbol);
            }
        }

        private static IEnumerable<ISymbol> GetForLoopInitializerAssignedSymbols(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (forStatement.Declaration != null)
            {
                foreach (var declarator in forStatement.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol)
                    {
                        yield return localSymbol.OriginalDefinition;
                    }
                }
            }

            foreach (var initializer in forStatement.Initializers)
            {
                if (initializer is AssignmentExpressionSyntax assignment &&
                    assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is { } assignedSymbol &&
                    assignedSymbol is ILocalSymbol or IParameterSymbol)
                {
                    yield return assignedSymbol.OriginalDefinition;
                }
            }
        }

        internal static ImmutableArray<SmtFormula> CollectAncestorReachabilityConditions(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var builder = ImmutableArray.CreateBuilder<SmtFormula>();

            foreach (var ancestor in syntaxNode.Ancestors())
            {
                if (ancestor is IfStatementSyntax ifStatementSyntax)
                {
                    if (ifStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                        !AnyReferencedSymbolAssignedBeforeUse(
                            ifStatementSyntax.Condition,
                            ifStatementSyntax.Statement,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken))
                    {
                        AddReachabilityCondition(builder, ifStatementSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    }
                    else if (ifStatementSyntax.Else?.Statement is { } elseStatement &&
                             elseStatement.Span.Contains(syntaxNode.Span) &&
                             !AnyReferencedSymbolAssignedBeforeUse(
                                 ifStatementSyntax.Condition,
                                 elseStatement,
                                 syntaxNode.SpanStart,
                                 semanticModel,
                                 cancellationToken))
                    {
                        AddReachabilityCondition(builder, ifStatementSyntax.Condition, mustBeTrue: false, semanticModel, cancellationToken);
                    }
                }
                else if (ancestor is ConditionalExpressionSyntax conditionalExpressionSyntax)
                {
                    if (conditionalExpressionSyntax.WhenTrue.Span.Contains(syntaxNode.Span) &&
                        !AnyReferencedSymbolAssignedBeforeUse(
                            conditionalExpressionSyntax.Condition,
                            conditionalExpressionSyntax.WhenTrue,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken))
                    {
                        AddReachabilityCondition(builder, conditionalExpressionSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    }
                    else if (conditionalExpressionSyntax.WhenFalse.Span.Contains(syntaxNode.Span) &&
                             !AnyReferencedSymbolAssignedBeforeUse(
                                 conditionalExpressionSyntax.Condition,
                                 conditionalExpressionSyntax.WhenFalse,
                                 syntaxNode.SpanStart,
                                 semanticModel,
                                 cancellationToken))
                    {
                        AddReachabilityCondition(builder, conditionalExpressionSyntax.Condition, mustBeTrue: false, semanticModel, cancellationToken);
                    }
                }
                else if (ancestor is BinaryExpressionSyntax binaryExpressionSyntax &&
                         binaryExpressionSyntax.Right.Span.Contains(syntaxNode.Span))
                {
                    if (AnyReferencedSymbolAssignedBeforeUse(
                            binaryExpressionSyntax.Left,
                            binaryExpressionSyntax.Right,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken))
                    {
                        continue;
                    }

                    if (binaryExpressionSyntax.IsKind(SyntaxKind.LogicalAndExpression))
                    {
                        AddReachabilityCondition(builder, binaryExpressionSyntax.Left, mustBeTrue: true, semanticModel, cancellationToken);
                    }
                    else if (binaryExpressionSyntax.IsKind(SyntaxKind.LogicalOrExpression))
                    {
                        AddReachabilityCondition(builder, binaryExpressionSyntax.Left, mustBeTrue: false, semanticModel, cancellationToken);
                    }
                    else if (binaryExpressionSyntax.IsKind(SyntaxKind.CoalesceExpression))
                    {
                        AddReferenceNullCondition(
                            builder,
                            binaryExpressionSyntax.Left,
                            isNull: true,
                            semanticModel,
                            cancellationToken);
                    }
                }
                else if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpressionSyntax &&
                         conditionalAccessExpressionSyntax.WhenNotNull.Span.Contains(syntaxNode.SpanStart) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             conditionalAccessExpressionSyntax.Expression,
                             conditionalAccessExpressionSyntax.WhenNotNull,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    AddReferenceNullCondition(
                        builder,
                        conditionalAccessExpressionSyntax.Expression,
                        isNull: false,
                        semanticModel,
                        cancellationToken);
                }
                else if (ancestor is LockStatementSyntax lockStatementSyntax &&
                         lockStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                         IsLocalOrParameterReference(lockStatementSyntax.Expression, semanticModel, cancellationToken) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             lockStatementSyntax.Expression,
                             lockStatementSyntax.Statement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    AddReferenceNullCondition(
                        builder,
                        lockStatementSyntax.Expression,
                        isNull: false,
                        semanticModel,
                        cancellationToken);
                }
                else if (ancestor is CatchClauseSyntax catchClauseSyntax &&
                         catchClauseSyntax.Block.Span.Contains(syntaxNode.Span))
                {
                    AddCatchBodyEntryFacts(
                        builder,
                        catchClauseSyntax,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken);
                }
                else if (ancestor is UsingStatementSyntax usingStatementSyntax &&
                         usingStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    if (usingStatementSyntax.Declaration != null)
                    {
                        AddUsingStatementDeclarationFacts(
                            builder,
                            usingStatementSyntax,
                            semanticModel,
                            cancellationToken);
                    }
                    else if (usingStatementSyntax.Expression != null &&
                             !AnyReferencedSymbolAssignedBeforeUse(
                                 usingStatementSyntax.Expression,
                                 usingStatementSyntax.Statement,
                                 syntaxNode.SpanStart,
                                 semanticModel,
                                 cancellationToken))
                    {
                        AddUsingStatementExpressionFacts(
                            builder,
                            usingStatementSyntax.Expression,
                            semanticModel,
                            cancellationToken);
                    }
                }
                else if (ancestor is WhileStatementSyntax whileStatementSyntax &&
                         whileStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             whileStatementSyntax.Condition,
                             whileStatementSyntax.Statement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    AddReachabilityCondition(builder, whileStatementSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    builder.AddRange(CollectLoopBodyInvariantFacts(whileStatementSyntax, semanticModel, cancellationToken));
                }
                else if (ancestor is DoStatementSyntax doStatementSyntax &&
                         doStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    builder.AddRange(CollectLoopBodyInvariantFacts(doStatementSyntax, semanticModel, cancellationToken));
                }
                else if (ancestor is ForStatementSyntax forStatementSyntax &&
                         forStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    if (forStatementSyntax.Condition != null &&
                        !AnyReferencedSymbolAssignedBeforeUse(
                            forStatementSyntax.Condition,
                            forStatementSyntax.Statement,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken))
                    {
                        AddReachabilityCondition(builder, forStatementSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    }

                    builder.AddRange(CollectLoopBodyInvariantFacts(forStatementSyntax, semanticModel, cancellationToken));
                }
                else if (ancestor is ForEachStatementSyntax forEachStatementSyntax &&
                         forEachStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             forEachStatementSyntax.Expression,
                             forEachStatementSyntax.Statement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    AddForeachBodyEntryFacts(
                        builder,
                        forEachStatementSyntax.Expression,
                        semanticModel.GetDeclaredSymbol(forEachStatementSyntax, cancellationToken) as ILocalSymbol,
                        forEachStatementSyntax,
                        forEachStatementSyntax.Statement,
                        semanticModel,
                        cancellationToken);
                }
                else if (ancestor is ForEachVariableStatementSyntax forEachVariableStatementSyntax &&
                         forEachVariableStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             forEachVariableStatementSyntax.Expression,
                             forEachVariableStatementSyntax.Statement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    AddForeachBodyEntryFacts(
                        builder,
                        forEachVariableStatementSyntax.Expression,
                        iterationSymbol: null,
                        forEachVariableStatementSyntax,
                        forEachVariableStatementSyntax.Statement,
                        semanticModel,
                        cancellationToken);
                }
                else if (ancestor is SwitchStatementSyntax switchStatementSyntax)
                {
                    var matchingSection = switchStatementSyntax.Sections
                        .FirstOrDefault(section => section.Span.Contains(syntaxNode.SpanStart));
                    if (matchingSection != null &&
                        !AnySwitchStatementConditionSymbolAssignedBeforeUse(
                            switchStatementSyntax,
                            matchingSection,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken) &&
                        SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                            switchStatementSyntax.Expression,
                            matchingSection,
                            semanticModel,
                            cancellationToken,
                            out var sectionCondition))
                    {
                        builder.Add(sectionCondition);
                    }
                }
                else if (ancestor is SwitchExpressionSyntax switchExpressionSyntax)
                {
                    var matchingArm = switchExpressionSyntax.Arms
                        .FirstOrDefault(arm => arm.Expression.Span.Contains(syntaxNode.SpanStart));
                    if (matchingArm != null &&
                        !AnySwitchExpressionConditionSymbolAssignedBeforeUse(
                            switchExpressionSyntax,
                            matchingArm,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken) &&
                        SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                            switchExpressionSyntax.GoverningExpression,
                            matchingArm,
                            semanticModel,
                            cancellationToken,
                            out var armCondition))
                    {
                        builder.Add(armCondition);
                    }
                }
            }

            return builder.ToImmutable();
        }

        public static SymbolicState CollectAncestorReachabilityState(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var state = new SymbolicState();

            foreach (var ancestor in syntaxNode.Ancestors())
            {
                if (ancestor is IfStatementSyntax ifStatementSyntax)
                {
                    if (ifStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                        !AnyReferencedSymbolAssignedBeforeUse(
                            ifStatementSyntax.Condition,
                            ifStatementSyntax.Statement,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken))
                    {
                        AddReachabilityCondition(ref state, ifStatementSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    }
                    else if (ifStatementSyntax.Else?.Statement is { } elseStatement &&
                             elseStatement.Span.Contains(syntaxNode.Span) &&
                             !AnyReferencedSymbolAssignedBeforeUse(
                                 ifStatementSyntax.Condition,
                                 elseStatement,
                                 syntaxNode.SpanStart,
                                 semanticModel,
                                 cancellationToken))
                    {
                        AddReachabilityCondition(ref state, ifStatementSyntax.Condition, mustBeTrue: false, semanticModel, cancellationToken);
                    }
                }
                else if (ancestor is ConditionalExpressionSyntax conditionalExpressionSyntax)
                {
                    if (conditionalExpressionSyntax.WhenTrue.Span.Contains(syntaxNode.Span) &&
                        !AnyReferencedSymbolAssignedBeforeUse(
                            conditionalExpressionSyntax.Condition,
                            conditionalExpressionSyntax.WhenTrue,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken))
                    {
                        AddReachabilityCondition(ref state, conditionalExpressionSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    }
                    else if (conditionalExpressionSyntax.WhenFalse.Span.Contains(syntaxNode.Span) &&
                             !AnyReferencedSymbolAssignedBeforeUse(
                                 conditionalExpressionSyntax.Condition,
                                 conditionalExpressionSyntax.WhenFalse,
                                 syntaxNode.SpanStart,
                                 semanticModel,
                                 cancellationToken))
                    {
                        AddReachabilityCondition(ref state, conditionalExpressionSyntax.Condition, mustBeTrue: false, semanticModel, cancellationToken);
                    }
                }
                else if (ancestor is BinaryExpressionSyntax binaryExpressionSyntax &&
                         binaryExpressionSyntax.Right.Span.Contains(syntaxNode.Span))
                {
                    if (AnyReferencedSymbolAssignedBeforeUse(
                            binaryExpressionSyntax.Left,
                            binaryExpressionSyntax.Right,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken))
                    {
                        continue;
                    }

                    if (binaryExpressionSyntax.IsKind(SyntaxKind.LogicalAndExpression))
                    {
                        AddReachabilityCondition(ref state, binaryExpressionSyntax.Left, mustBeTrue: true, semanticModel, cancellationToken);
                    }
                    else if (binaryExpressionSyntax.IsKind(SyntaxKind.LogicalOrExpression))
                    {
                        AddReachabilityCondition(ref state, binaryExpressionSyntax.Left, mustBeTrue: false, semanticModel, cancellationToken);
                    }
                    else if (binaryExpressionSyntax.IsKind(SyntaxKind.CoalesceExpression))
                    {
                        AddReferenceNullCondition(ref state, binaryExpressionSyntax.Left, isNull: true, semanticModel, cancellationToken);
                    }
                }
                else if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpressionSyntax &&
                         conditionalAccessExpressionSyntax.WhenNotNull.Span.Contains(syntaxNode.SpanStart) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             conditionalAccessExpressionSyntax.Expression,
                             conditionalAccessExpressionSyntax.WhenNotNull,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    AddReferenceNullCondition(ref state, conditionalAccessExpressionSyntax.Expression, isNull: false, semanticModel, cancellationToken);
                }
                else if (ancestor is LockStatementSyntax lockStatementSyntax &&
                         lockStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                         IsLocalOrParameterReference(lockStatementSyntax.Expression, semanticModel, cancellationToken) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             lockStatementSyntax.Expression,
                             lockStatementSyntax.Statement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    AddReferenceNullCondition(
                        ref state,
                        lockStatementSyntax.Expression,
                        isNull: false,
                        semanticModel,
                        cancellationToken,
                        "ir.path.lock-entry.not-null");
                }
                else if (ancestor is CatchClauseSyntax catchClauseSyntax &&
                         catchClauseSyntax.Block.Span.Contains(syntaxNode.Span))
                {
                    var facts = ImmutableArray.CreateBuilder<SmtFormula>();
                    AddCatchBodyEntryStateFacts(
                        ref state,
                        catchClauseSyntax,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken);
                    AddCatchBodyEntryFacts(
                        facts,
                        catchClauseSyntax,
                        syntaxNode.SpanStart,
                        semanticModel,
                        cancellationToken);
                    AddFormulaPathConditions(ref state, facts, catchClauseSyntax, "ir.path.catch-entry");
                }
                else if (ancestor is UsingStatementSyntax usingStatementSyntax &&
                         usingStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    var facts = ImmutableArray.CreateBuilder<SmtFormula>();
                    if (usingStatementSyntax.Declaration != null)
                    {
                        AddUsingStatementDeclarationStateFacts(
                            ref state,
                            usingStatementSyntax,
                            semanticModel,
                            cancellationToken);
                        AddUsingStatementDeclarationFacts(
                            facts,
                            usingStatementSyntax,
                            semanticModel,
                            cancellationToken);
                    }
                    else if (usingStatementSyntax.Expression != null &&
                             !AnyReferencedSymbolAssignedBeforeUse(
                                 usingStatementSyntax.Expression,
                                 usingStatementSyntax.Statement,
                                 syntaxNode.SpanStart,
                                 semanticModel,
                                 cancellationToken))
                    {
                        AddUsingStatementExpressionStateFacts(
                            ref state,
                            usingStatementSyntax.Expression,
                            usingStatementSyntax.Statement,
                            semanticModel,
                            cancellationToken);
                        AddUsingStatementExpressionFacts(
                            facts,
                            usingStatementSyntax.Expression,
                            semanticModel,
                            cancellationToken);
                    }

                    AddFormulaPathConditions(ref state, facts, usingStatementSyntax, "ir.path.using-entry");
                }
                else if (ancestor is WhileStatementSyntax whileStatementSyntax &&
                         whileStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             whileStatementSyntax.Condition,
                             whileStatementSyntax.Statement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    AddReachabilityCondition(ref state, whileStatementSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    AddPreLoopBodyInvariantStateFacts(
                        ref state,
                        whileStatementSyntax,
                        whileStatementSyntax.Statement,
                        "ir.path.while-loop-invariant",
                        semanticModel,
                        cancellationToken);
                    AddFormulaPathConditions(
                        ref state,
                        CollectLoopBodyInvariantFacts(whileStatementSyntax, semanticModel, cancellationToken),
                        whileStatementSyntax,
                        "ir.path.while-loop-invariant");
                }
                else if (ancestor is DoStatementSyntax doStatementSyntax &&
                         doStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    AddPreLoopBodyInvariantStateFacts(
                        ref state,
                        doStatementSyntax,
                        doStatementSyntax.Statement,
                        "ir.path.do-loop-invariant",
                        semanticModel,
                        cancellationToken);
                    AddFormulaPathConditions(
                        ref state,
                        CollectLoopBodyInvariantFacts(doStatementSyntax, semanticModel, cancellationToken),
                        doStatementSyntax,
                        "ir.path.do-loop-invariant");
                }
                else if (ancestor is ForStatementSyntax forStatementSyntax &&
                         forStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    if (forStatementSyntax.Condition != null &&
                        !AnyReferencedSymbolAssignedBeforeUse(
                            forStatementSyntax.Condition,
                            forStatementSyntax.Statement,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken))
                    {
                        AddReachabilityCondition(ref state, forStatementSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    }

                    AddForLoopBodyInvariantStateFacts(
                        ref state,
                        forStatementSyntax,
                        semanticModel,
                        cancellationToken);
                    AddFormulaPathConditions(
                        ref state,
                        CollectLoopBodyInvariantFacts(forStatementSyntax, semanticModel, cancellationToken),
                        forStatementSyntax,
                        "ir.path.for-loop-invariant");
                }
                else if (ancestor is ForEachStatementSyntax forEachStatementSyntax &&
                         forEachStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             forEachStatementSyntax.Expression,
                             forEachStatementSyntax.Statement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    var facts = ImmutableArray.CreateBuilder<SmtFormula>();
                    AddForeachBodyEntryStateFacts(
                        ref state,
                        forEachStatementSyntax.Expression,
                        forEachStatementSyntax,
                        forEachStatementSyntax.Statement,
                        semanticModel,
                        cancellationToken);
                    AddForeachBodyEntryFacts(
                        facts,
                        forEachStatementSyntax.Expression,
                        semanticModel.GetDeclaredSymbol(forEachStatementSyntax, cancellationToken) as ILocalSymbol,
                        forEachStatementSyntax,
                        forEachStatementSyntax.Statement,
                        semanticModel,
                        cancellationToken);
                    AddFormulaPathConditions(ref state, facts, forEachStatementSyntax, "ir.path.foreach-entry");
                }
                else if (ancestor is ForEachVariableStatementSyntax forEachVariableStatementSyntax &&
                         forEachVariableStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             forEachVariableStatementSyntax.Expression,
                             forEachVariableStatementSyntax.Statement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    var facts = ImmutableArray.CreateBuilder<SmtFormula>();
                    AddForeachBodyEntryStateFacts(
                        ref state,
                        forEachVariableStatementSyntax.Expression,
                        forEachVariableStatementSyntax,
                        forEachVariableStatementSyntax.Statement,
                        semanticModel,
                        cancellationToken);
                    AddForeachBodyEntryFacts(
                        facts,
                        forEachVariableStatementSyntax.Expression,
                        iterationSymbol: null,
                        forEachVariableStatementSyntax,
                        forEachVariableStatementSyntax.Statement,
                        semanticModel,
                        cancellationToken);
                    AddFormulaPathConditions(ref state, facts, forEachVariableStatementSyntax, "ir.path.foreach-entry");
                }
                else if (ancestor is SwitchStatementSyntax switchStatementSyntax)
                {
                    var matchingSection = switchStatementSyntax.Sections
                        .FirstOrDefault(section => section.Span.Contains(syntaxNode.SpanStart));
                    if (matchingSection != null &&
                        !AnySwitchStatementConditionSymbolAssignedBeforeUse(
                            switchStatementSyntax,
                            matchingSection,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken) &&
                        SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                            switchStatementSyntax.Expression,
                            matchingSection,
                            semanticModel,
                            cancellationToken,
                            out var sectionCondition))
                    {
                        AddFormulaPathCondition(ref state, sectionCondition, matchingSection, "ir.path.switch-section");
                    }
                }
                else if (ancestor is SwitchExpressionSyntax switchExpressionSyntax)
                {
                    var matchingArm = switchExpressionSyntax.Arms
                        .FirstOrDefault(arm => arm.Expression.Span.Contains(syntaxNode.SpanStart));
                    if (matchingArm != null &&
                        !AnySwitchExpressionConditionSymbolAssignedBeforeUse(
                            switchExpressionSyntax,
                            matchingArm,
                            syntaxNode.SpanStart,
                            semanticModel,
                            cancellationToken) &&
                        SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                            switchExpressionSyntax.GoverningExpression,
                            matchingArm,
                            semanticModel,
                            cancellationToken,
                            out var armCondition))
                    {
                        AddFormulaPathCondition(ref state, armCondition, matchingArm, "ir.path.switch-expression-arm");
                    }
                }
            }

            return state;
        }

        private static void AddReachabilityCondition(
            ref SymbolicState state,
            ExpressionSyntax condition,
            bool mustBeTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (SymbolicReachabilityService.TryCollectBranchState(
                    state,
                    condition,
                    mustBeTrue,
                    semanticModel,
                    cancellationToken,
                    out var branchState))
            {
                state = branchState;
            }
        }

        private static void AddReferenceNullCondition(
            ref SymbolicState state,
            ExpressionSyntax expression,
            bool isNull,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            string? provenance = null)
        {
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (!SymbolicIrLowerer.TryLowerTerm(expression, context, out var subject) ||
                subject.Kind != SmtValueKind.Reference)
            {
                return;
            }

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                    subject,
                    new SymbolicNullTerm()),
                expression,
                provenance ?? (isNull ? "ir.path.reference-null" : "ir.path.reference-not-null"));
            state = state.AddPathCondition(new SymbolicFactCondition(fact));
        }

        private static void AddSymbolReferenceNullCondition(
            ref SymbolicState state,
            ISymbol symbol,
            SyntaxNode source,
            bool isNull,
            string provenance)
        {
            if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not { } type ||
                !TryGetValueKind(type, out var kind) ||
                kind != SmtValueKind.Reference)
            {
                return;
            }

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                    new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(symbol), SmtValueKind.Reference),
                    new SymbolicNullTerm()),
                source,
                provenance);
            state = state.AddPathCondition(new SymbolicFactCondition(fact));
        }

        private static void AddCatchBodyEntryStateFacts(
            ref SymbolicState state,
            CatchClauseSyntax catchClause,
            int useSpanStart,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (catchClause.Declaration != null &&
                semanticModel.GetDeclaredSymbol(catchClause.Declaration, cancellationToken) is ILocalSymbol localSymbol &&
                !IsSymbolAssignedBetween(
                    catchClause.Block,
                    catchClause.Block.SpanStart - 1,
                    useSpanStart,
                    localSymbol.OriginalDefinition,
                    semanticModel,
                    cancellationToken))
            {
                AddSymbolReferenceNullCondition(
                    ref state,
                    localSymbol.OriginalDefinition,
                    catchClause.Declaration,
                    isNull: false,
                    "ir.path.catch-entry.exception-not-null");
            }

            if (catchClause.Filter?.FilterExpression is { } filterExpression &&
                !AnyReferencedSymbolAssignedBeforeUse(
                    filterExpression,
                    catchClause.Block,
                    useSpanStart,
                    semanticModel,
                    cancellationToken))
            {
                AddReachabilityCondition(ref state, filterExpression, mustBeTrue: true, semanticModel, cancellationToken);
            }
        }

        private static void AddUsingStatementExpressionStateFacts(
            ref SymbolicState state,
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddThrowGuardedExpressionStateFacts(
                ref state,
                expression,
                statement,
                semanticModel,
                cancellationToken,
                "ir.path.using-entry.throw-guarded-not-null");
        }

        private static void AddUsingStatementDeclarationStateFacts(
            ref SymbolicState state,
            UsingStatementSyntax usingStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (usingStatement.Declaration == null)
            {
                return;
            }

            foreach (var declarator in usingStatement.Declaration.Variables)
            {
                if (declarator.Initializer == null ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol)
                {
                    continue;
                }

                AddUsingDeclarationInitializerStateFacts(
                    ref state,
                    localSymbol,
                    declarator.Initializer.Value,
                    usingStatement.Statement,
                    semanticModel,
                    cancellationToken);
            }
        }

        private static void AddUsingDeclarationInitializerStateFacts(
            ref SymbolicState state,
            ILocalSymbol localSymbol,
            ExpressionSyntax initializer,
            StatementSyntax usingBody,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var effectiveInitializer = initializer;
            if (TryGetThrowGuardedValue(
                    initializer,
                    out var guardedValue,
                    out _,
                    out _,
                    out _))
            {
                effectiveInitializer = guardedValue;
                AddThrowGuardedExpressionStateFacts(
                    ref state,
                    initializer,
                    usingBody,
                    semanticModel,
                    cancellationToken,
                    "ir.path.using-entry.throw-guarded-not-null");
            }

            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (!TryCreateLocalSymbolTerm(localSymbol, out var target) ||
                !SymbolicIrLowerer.TryLowerTerm(effectiveInitializer, context, out var value) ||
                !CanCompareIrTerms(target, value))
            {
                return;
            }

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    target,
                    value),
                initializer,
                "ir.path.using-entry.declaration-alias");
            state = state.AddPathCondition(new SymbolicFactCondition(fact));
        }

        private static bool TryCreateLocalSymbolTerm(
            ILocalSymbol localSymbol,
            out SymbolicTerm term)
        {
            if (!TryGetValueKind(localSymbol.Type, out var kind))
            {
                term = null!;
                return false;
            }

            term = new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(localSymbol), kind);
            return true;
        }

        private static bool CanCompareIrTerms(SymbolicTerm left, SymbolicTerm right)
        {
            return left.Kind == right.Kind ||
                left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference ||
                right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference;
        }

        private static void AddForLoopBodyInvariantStateFacts(
            ref SymbolicState state,
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddForLoopMonotonicLowerBoundStateFacts(ref state, forStatement, semanticModel, cancellationToken);
            AddForLoopMonotonicUpperBoundStateFacts(ref state, forStatement, semanticModel, cancellationToken);
        }

        private static void AddPreLoopBodyInvariantStateFacts(
            ref SymbolicState state,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            string provenancePrefix,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddPreLoopMonotonicLowerBoundStateFacts(
                ref state,
                loopStatement,
                loopBody,
                provenancePrefix,
                semanticModel,
                cancellationToken);
            AddPreLoopMonotonicUpperBoundStateFacts(
                ref state,
                loopStatement,
                loopBody,
                provenancePrefix,
                semanticModel,
                cancellationToken);
        }

        private static void AddPreLoopMonotonicLowerBoundStateFacts(
            ref SymbolicState state,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            string provenancePrefix,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumeratePreLoopInitializerBoundTerms(loopStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                    symbolTerm.Kind != SmtValueKind.Int ||
                    initializer.Bound.Kind != SmtValueKind.Int ||
                    LoopHeaderInvalidatesSymbolValue(loopStatement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(loopBody, symbol, semanticModel, cancellationToken) ||
                        LoopHeaderInvalidatesSymbolValue(loopStatement, symbol, semanticModel, cancellationToken)) ||
                    !LoopBodyMutationsPreserveLowerBound(loopBody, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.GreaterThanOrEqual,
                    symbolTerm,
                    initializer.Bound,
                    loopStatement,
                    provenancePrefix + ".lower-bound");
            }
        }

        private static void AddPreLoopMonotonicUpperBoundStateFacts(
            ref SymbolicState state,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            string provenancePrefix,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddPreLoopMonotonicInitialUpperBoundStateFacts(
                ref state,
                loopStatement,
                loopBody,
                provenancePrefix,
                semanticModel,
                cancellationToken);

            foreach (var initializer in EnumeratePreLoopStrictUpperBoundInitializerTerms(loopStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                    symbolTerm.Kind != SmtValueKind.Int ||
                    initializer.UpperBound.Kind != SmtValueKind.Int ||
                    LoopHeaderInvalidatesSymbolValue(loopStatement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(loopBody, symbol, semanticModel, cancellationToken) ||
                        LoopHeaderInvalidatesSymbolValue(loopStatement, symbol, semanticModel, cancellationToken)) ||
                    !LoopBodyMutationsPreserveUpperBound(loopBody, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.LessThan,
                    symbolTerm,
                    initializer.UpperBound,
                    loopStatement,
                    provenancePrefix + ".strict-upper-bound");
            }
        }

        private static void AddPreLoopMonotonicInitialUpperBoundStateFacts(
            ref SymbolicState state,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            string provenancePrefix,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumeratePreLoopInitializerBoundTerms(loopStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                    symbolTerm.Kind != SmtValueKind.Int ||
                    initializer.Bound.Kind != SmtValueKind.Int ||
                    LoopHeaderInvalidatesSymbolValue(loopStatement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(loopBody, symbol, semanticModel, cancellationToken) ||
                        LoopHeaderInvalidatesSymbolValue(loopStatement, symbol, semanticModel, cancellationToken)) ||
                    !LoopBodyMutationsPreserveUpperBound(loopBody, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.LessThanOrEqual,
                    symbolTerm,
                    initializer.Bound,
                    loopStatement,
                    provenancePrefix + ".initial-upper-bound");
            }
        }

        private static void AddForLoopMonotonicLowerBoundStateFacts(
            ref SymbolicState state,
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumerateForLoopInitializerBoundTerms(forStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                    symbolTerm.Kind != SmtValueKind.Int ||
                    initializer.Bound.Kind != SmtValueKind.Int ||
                    StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(forStatement.Statement, symbol, semanticModel, cancellationToken) ||
                        ForLoopIncrementorsInvalidateSymbolValue(forStatement, symbol, semanticModel, cancellationToken)) ||
                    !ForLoopIncrementorsPreserveLowerBound(forStatement, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.GreaterThanOrEqual,
                    symbolTerm,
                    initializer.Bound,
                    forStatement,
                    "ir.path.for-loop-invariant.lower-bound");
            }
        }

        private static void AddForLoopMonotonicUpperBoundStateFacts(
            ref SymbolicState state,
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddForLoopMonotonicInitialUpperBoundStateFacts(ref state, forStatement, semanticModel, cancellationToken);

            foreach (var initializer in EnumerateForLoopStrictUpperBoundInitializerTerms(forStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                    symbolTerm.Kind != SmtValueKind.Int ||
                    initializer.UpperBound.Kind != SmtValueKind.Int ||
                    StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(forStatement.Statement, symbol, semanticModel, cancellationToken) ||
                        ForLoopIncrementorsInvalidateSymbolValue(forStatement, symbol, semanticModel, cancellationToken)) ||
                    !ForLoopIncrementorsPreserveUpperBound(forStatement, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.LessThan,
                    symbolTerm,
                    initializer.UpperBound,
                    forStatement,
                    "ir.path.for-loop-invariant.strict-upper-bound");
            }
        }

        private static void AddForLoopMonotonicInitialUpperBoundStateFacts(
            ref SymbolicState state,
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumerateForLoopInitializerBoundTerms(forStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolTerm(initializer.Symbol, out var symbolTerm) ||
                    symbolTerm.Kind != SmtValueKind.Int ||
                    initializer.Bound.Kind != SmtValueKind.Int ||
                    StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(forStatement.Statement, symbol, semanticModel, cancellationToken) ||
                        ForLoopIncrementorsInvalidateSymbolValue(forStatement, symbol, semanticModel, cancellationToken)) ||
                    !ForLoopIncrementorsPreserveUpperBound(forStatement, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddRelationPathFact(
                    ref state,
                    SymbolicRelationOperator.LessThanOrEqual,
                    symbolTerm,
                    initializer.Bound,
                    forStatement,
                    "ir.path.for-loop-invariant.initial-upper-bound");
            }
        }

        private static IEnumerable<(ISymbol Symbol, SymbolicTerm Bound, IReadOnlyList<ISymbol> BoundSymbols)> EnumerateForLoopInitializerBoundTerms(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (forStatement.Declaration != null)
            {
                foreach (var declarator in forStatement.Declaration.Variables)
                {
                    if (declarator.Initializer == null ||
                        semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol ||
                        !TryLowerInitializerBoundTerm(
                            declarator.Initializer.Value,
                            localSymbol.OriginalDefinition,
                            semanticModel,
                            cancellationToken,
                            out var lowerBound,
                            out var boundSymbols))
                    {
                        continue;
                    }

                    yield return (localSymbol.OriginalDefinition, lowerBound, boundSymbols);
                }
            }

            foreach (var expression in forStatement.Initializers)
            {
                if (expression is not AssignmentExpressionSyntax assignment ||
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } symbol ||
                    symbol is not ILocalSymbol and not IParameterSymbol ||
                    !TryLowerInitializerBoundTerm(
                        assignment.Right,
                        symbol.OriginalDefinition,
                        semanticModel,
                        cancellationToken,
                        out var lowerBound,
                        out var boundSymbols))
                {
                    continue;
                }

                yield return (symbol.OriginalDefinition, lowerBound, boundSymbols);
            }
        }

        private static IEnumerable<(ISymbol Symbol, SymbolicTerm UpperBound, IReadOnlyList<ISymbol> BoundSymbols)> EnumerateForLoopStrictUpperBoundInitializerTerms(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (forStatement.Declaration != null)
            {
                foreach (var declarator in forStatement.Declaration.Variables)
                {
                    if (declarator.Initializer == null ||
                        semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol ||
                        !TryGetStrictUpperBoundInitializerTerm(
                            declarator.Initializer.Value,
                            localSymbol.OriginalDefinition,
                            semanticModel,
                            cancellationToken,
                            out var upperBound,
                            out var boundSymbols))
                    {
                        continue;
                    }

                    yield return (localSymbol.OriginalDefinition, upperBound, boundSymbols);
                }
            }

            foreach (var expression in forStatement.Initializers)
            {
                if (expression is not AssignmentExpressionSyntax assignment ||
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } symbol ||
                    symbol is not ILocalSymbol and not IParameterSymbol ||
                    !TryGetStrictUpperBoundInitializerTerm(
                        assignment.Right,
                        symbol.OriginalDefinition,
                        semanticModel,
                        cancellationToken,
                        out var upperBound,
                        out var boundSymbols))
                {
                    continue;
                }

                yield return (symbol.OriginalDefinition, upperBound, boundSymbols);
            }
        }

        private static IEnumerable<(ISymbol Symbol, SymbolicTerm Bound, IReadOnlyList<ISymbol> BoundSymbols)> EnumeratePreLoopInitializerBoundTerms(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumeratePreLoopInitializerExpressions(loopStatement, semanticModel, cancellationToken))
            {
                if (TryLowerInitializerBoundTerm(
                        initializer.Value,
                        initializer.Symbol,
                        semanticModel,
                        cancellationToken,
                        out var bound,
                        out var boundSymbols) &&
                    !AnyPriorStatementsInvalidateInitializer(
                        initializer,
                        loopStatement,
                        boundSymbols,
                        semanticModel,
                        cancellationToken))
                {
                    yield return (initializer.Symbol, bound, boundSymbols);
                }
            }
        }

        private static IEnumerable<(ISymbol Symbol, SymbolicTerm UpperBound, IReadOnlyList<ISymbol> BoundSymbols)> EnumeratePreLoopStrictUpperBoundInitializerTerms(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumeratePreLoopInitializerExpressions(loopStatement, semanticModel, cancellationToken))
            {
                if (TryGetStrictUpperBoundInitializerTerm(
                        initializer.Value,
                        initializer.Symbol,
                        semanticModel,
                        cancellationToken,
                        out var upperBound,
                        out var boundSymbols) &&
                    !AnyPriorStatementsInvalidateInitializer(
                        initializer,
                        loopStatement,
                        boundSymbols,
                        semanticModel,
                        cancellationToken))
                {
                    yield return (initializer.Symbol, upperBound, boundSymbols);
                }
            }
        }

        private static bool TryGetStrictUpperBoundInitializerTerm(
            ExpressionSyntax expression,
            ISymbol initializedSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SymbolicTerm upperBound,
            out IReadOnlyList<ISymbol> upperBoundSymbols)
        {
            expression = UnwrapExpression(expression);
            if (expression is not BinaryExpressionSyntax binaryExpression)
            {
                upperBound = null!;
                upperBoundSymbols = Array.Empty<ISymbol>();
                return false;
            }

            if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var subtractedValue) &&
                subtractedValue > 0 &&
                TryLowerInitializerBoundTerm(
                    binaryExpression.Left,
                    initializedSymbol,
                    semanticModel,
                    cancellationToken,
                    out upperBound,
                    out upperBoundSymbols))
            {
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.AddExpression))
            {
                if (TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue) &&
                    rightValue < 0 &&
                    TryLowerInitializerBoundTerm(
                        binaryExpression.Left,
                        initializedSymbol,
                        semanticModel,
                        cancellationToken,
                        out upperBound,
                        out upperBoundSymbols))
                {
                    return true;
                }

                if (TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue) &&
                    leftValue < 0 &&
                    TryLowerInitializerBoundTerm(
                        binaryExpression.Right,
                        initializedSymbol,
                        semanticModel,
                        cancellationToken,
                        out upperBound,
                        out upperBoundSymbols))
                {
                    return true;
                }
            }

            upperBound = null!;
            upperBoundSymbols = Array.Empty<ISymbol>();
            return false;
        }

        private static bool TryLowerInitializerBoundTerm(
            ExpressionSyntax expression,
            ISymbol initializedSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SymbolicTerm bound,
            out IReadOnlyList<ISymbol> boundSymbols)
        {
            var referencedSymbols = GetReferencedLocalAndParameterSymbols(expression, semanticModel, cancellationToken);
            if (referencedSymbols.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, initializedSymbol)) ||
                !SymbolicIrLowerer.TryLowerTerm(
                    expression,
                    new SymbolicLoweringContext(semanticModel, cancellationToken),
                    out var candidate) ||
                candidate.Kind != SmtValueKind.Int)
            {
                bound = null!;
                boundSymbols = Array.Empty<ISymbol>();
                return false;
            }

            bound = candidate;
            boundSymbols = referencedSymbols;
            return true;
        }

        private static bool TryCreateSymbolTerm(ISymbol symbol, out SymbolicTerm term)
        {
            if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not { } type ||
                !TryGetValueKind(type, out var kind))
            {
                term = null!;
                return false;
            }

            term = new SymbolicVariableTerm(SymbolicFactFactory.GetSmtVariableName(symbol), kind);
            return true;
        }

        private static void AddRelationPathFact(
            ref SymbolicState state,
            SymbolicRelationOperator op,
            SymbolicTerm left,
            SymbolicTerm right,
            SyntaxNode source,
            string provenance)
        {
            if (!CanCompareIrTerms(left, right))
            {
                return;
            }

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(op, left, right),
                source,
                provenance);
            state = state.AddPathCondition(new SymbolicFactCondition(fact));
        }

        private static void AddForeachBodyEntryStateFacts(
            ref SymbolicState state,
            ExpressionSyntax expressionSyntax,
            StatementSyntax foreachStatement,
            StatementSyntax foreachBody,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddThrowGuardedExpressionStateFacts(
                ref state,
                expressionSyntax,
                foreachBody,
                semanticModel,
                cancellationToken);
            AddReferenceNullCondition(
                ref state,
                expressionSyntax,
                isNull: false,
                semanticModel,
                cancellationToken,
                "ir.path.foreach-entry.not-null");
            AddForeachLengthPositiveStateFact(
                ref state,
                expressionSyntax,
                foreachStatement,
                semanticModel,
                cancellationToken);
        }

        private static void AddThrowGuardedExpressionStateFacts(
            ref SymbolicState state,
            ExpressionSyntax expression,
            StatementSyntax guardedStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            string nonNullProvenance = "ir.path.foreach-entry.throw-guarded-not-null")
        {
            if (!TryGetThrowGuardedValue(
                    expression,
                    out var effectiveValueExpression,
                    out var guardExpression,
                    out var guardBranchWhenTrue,
                    out var requiresNonNullValue))
            {
                return;
            }

            if (guardExpression != null)
            {
                if (!AnyConditionSymbolInvalidatedInStatement(guardExpression, guardedStatement, semanticModel, cancellationToken))
                {
                    AddReachabilityCondition(ref state, guardExpression, guardBranchWhenTrue, semanticModel, cancellationToken);
                }
            }
            else if (requiresNonNullValue &&
                     !AnyConditionSymbolInvalidatedInStatement(effectiveValueExpression, guardedStatement, semanticModel, cancellationToken))
            {
                AddReferenceNullCondition(
                    ref state,
                    effectiveValueExpression,
                    isNull: false,
                    semanticModel,
                    cancellationToken,
                    nonNullProvenance);
            }
        }

        private static void AddForeachLengthPositiveStateFact(
            ref SymbolicState state,
            ExpressionSyntax expressionSyntax,
            StatementSyntax foreachStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (AnyConditionSymbolInvalidatedInStatement(expressionSyntax, foreachStatement, semanticModel, cancellationToken))
            {
                return;
            }

            var typeInfo = semanticModel.GetTypeInfo(expressionSyntax, cancellationToken);
            if (!TryCreateForeachLengthTerm(expressionSyntax, typeInfo.Type, semanticModel, cancellationToken, out var length) &&
                !TryCreateForeachLengthTerm(expressionSyntax, typeInfo.ConvertedType, semanticModel, cancellationToken, out length))
            {
                return;
            }

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.GreaterThan,
                    length,
                    new SymbolicIntegerConstantTerm(0)),
                expressionSyntax,
                "ir.path.foreach-entry.length-positive");
            state = state.AddPathCondition(new SymbolicFactCondition(fact));
        }

        private static bool TryCreateForeachLengthTerm(
            ExpressionSyntax expressionSyntax,
            ITypeSymbol? type,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SymbolicTerm length)
        {
            length = null!;
            if (!IsSupportedForeachLengthReceiver(type))
            {
                return false;
            }

            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (!SymbolicIrLowerer.TryLowerTerm(expressionSyntax, context, out var receiver))
            {
                return false;
            }

            if (type?.SpecialType == SpecialType.System_String)
            {
                length = receiver.Kind == SmtValueKind.String
                    ? new SymbolicLengthTerm(receiver)
                    : receiver.Kind == SmtValueKind.Reference
                        ? new SymbolicLengthTerm(new SymbolicStringContentTerm(receiver))
                        : null!;
                return length != null;
            }

            if (type is IArrayTypeSymbol { Rank: 1 } &&
                receiver.Kind == SmtValueKind.Reference)
            {
                length = new SymbolicLengthTerm(receiver);
                return true;
            }

            return false;
        }

        private static void AddFormulaPathConditions(
            ref SymbolicState state,
            IEnumerable<SmtFormula> formulas,
            SyntaxNode source,
            string provenance)
        {
            foreach (var formula in formulas)
            {
                AddFormulaPathCondition(ref state, formula, source, provenance);
            }
        }

        private static void AddFormulaPathCondition(
            ref SymbolicState state,
            SmtFormula formula,
            SyntaxNode source,
            string provenance)
        {
            if (SymbolicSmtFormulaLowerer.TryLowerCondition(
                    formula,
                    source,
                    provenance,
                    evidenceKey: provenance,
                    out var condition))
            {
                state = state.AddPathCondition(condition);
            }
        }

        internal static IEnumerable<SmtFormula> CollectForInitializerFacts(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var facts = new List<SmtFormula>();
            if (forStatement.Declaration != null)
            {
                foreach (var declarator in forStatement.Declaration.Variables)
                {
                    if (declarator.Initializer != null &&
                        semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol)
                    {
                        AddAssignedValueFacts(localSymbol, declarator.Initializer.Value, semanticModel, cancellationToken, facts);
                    }
                }
            }

            foreach (var initializer in forStatement.Initializers)
            {
                if (initializer is not AssignmentExpressionSyntax assignment ||
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    continue;
                }

                var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
                if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                {
                    AddAssignedValueFacts(assignedSymbol.OriginalDefinition, assignment.Right, semanticModel, cancellationToken, facts);
                }
            }

            return facts;
        }

        internal static SymbolicState CollectForInitializerState(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var state = new SymbolicState();
            AddFormulaPathConditions(
                ref state,
                CollectForInitializerFacts(forStatement, semanticModel, cancellationToken),
                forStatement,
                "ir.path.for-initializer");
            return state;
        }

        internal static ImmutableArray<SmtFormula> CollectForLoopBodyInvariantFacts(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return CollectLoopBodyInvariantFacts(forStatement, semanticModel, cancellationToken);
        }

        internal static ImmutableArray<SmtFormula> CollectLoopBodyInvariantFacts(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var builder = ImmutableArray.CreateBuilder<SmtFormula>();
            switch (loopStatement)
            {
                case ForStatementSyntax forStatement:
                    AddForLoopMonotonicLowerBoundFacts(builder, forStatement, semanticModel, cancellationToken);
                    AddForLoopMonotonicUpperBoundFacts(builder, forStatement, semanticModel, cancellationToken);
                    break;
                case WhileStatementSyntax whileStatement:
                    AddPreLoopMonotonicLowerBoundFacts(builder, whileStatement, whileStatement.Statement, semanticModel, cancellationToken);
                    AddPreLoopMonotonicUpperBoundFacts(builder, whileStatement, whileStatement.Statement, semanticModel, cancellationToken);
                    break;
                case DoStatementSyntax doStatement:
                    AddPreLoopMonotonicLowerBoundFacts(builder, doStatement, doStatement.Statement, semanticModel, cancellationToken);
                    AddPreLoopMonotonicUpperBoundFacts(builder, doStatement, doStatement.Statement, semanticModel, cancellationToken);
                    break;
            }

            return builder.ToImmutable();
        }

        internal static ImmutableArray<SmtFormula> CollectCompletedLoopExitInvariantFacts(
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var builder = ImmutableArray.CreateBuilder<SmtFormula>();
            AddCompletedLoopStatementFacts(statement, semanticModel, cancellationToken, builder);
            return builder.ToImmutable();
        }

        private static void AddForLoopMonotonicLowerBoundFacts(
            ICollection<SmtFormula> facts,
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumerateForLoopInitializerBounds(forStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolSmtValue(initializer.Symbol, out var symbolFormula) ||
                    symbolFormula.Kind != SmtValueKind.Int ||
                    initializer.Bound.Kind != SmtValueKind.Int ||
                    StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(forStatement.Statement, symbol, semanticModel, cancellationToken) ||
                        ForLoopIncrementorsInvalidateSymbolValue(forStatement, symbol, semanticModel, cancellationToken)) ||
                    !ForLoopIncrementorsPreserveLowerBound(forStatement, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    symbolFormula,
                    initializer.Bound));
            }
        }

        private static IEnumerable<(ISymbol Symbol, SmtFormula Bound, IReadOnlyList<ISymbol> BoundSymbols)> EnumerateForLoopInitializerBounds(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (forStatement.Declaration != null)
            {
                foreach (var declarator in forStatement.Declaration.Variables)
                {
                    if (declarator.Initializer == null ||
                        semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol ||
                        !TryTranslateInitializerBound(
                            declarator.Initializer.Value,
                            localSymbol.OriginalDefinition,
                            semanticModel,
                            cancellationToken,
                            out var lowerBound,
                            out var boundSymbols))
                    {
                        continue;
                    }

                    yield return (localSymbol.OriginalDefinition, lowerBound, boundSymbols);
                }
            }

            foreach (var expression in forStatement.Initializers)
            {
                if (expression is not AssignmentExpressionSyntax assignment ||
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } symbol ||
                    symbol is not ILocalSymbol and not IParameterSymbol ||
                    !TryTranslateInitializerBound(
                        assignment.Right,
                        symbol.OriginalDefinition,
                        semanticModel,
                        cancellationToken,
                        out var lowerBound,
                        out var boundSymbols))
                {
                    continue;
                }

                yield return (symbol.OriginalDefinition, lowerBound, boundSymbols);
            }
        }

        private static void AddForLoopMonotonicUpperBoundFacts(
            ICollection<SmtFormula> facts,
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddForLoopMonotonicInitialUpperBoundFacts(facts, forStatement, semanticModel, cancellationToken);

            foreach (var initializer in EnumerateForLoopStrictUpperBoundInitializers(forStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolSmtValue(initializer.Symbol, out var symbolFormula) ||
                    symbolFormula.Kind != SmtValueKind.Int ||
                    initializer.UpperBound.Kind != SmtValueKind.Int ||
                    StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(forStatement.Statement, symbol, semanticModel, cancellationToken) ||
                        ForLoopIncrementorsInvalidateSymbolValue(forStatement, symbol, semanticModel, cancellationToken)) ||
                    !ForLoopIncrementorsPreserveUpperBound(forStatement, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.LessThan,
                    symbolFormula,
                    initializer.UpperBound));
            }
        }

        private static void AddForLoopMonotonicInitialUpperBoundFacts(
            ICollection<SmtFormula> facts,
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumerateForLoopInitializerBounds(forStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolSmtValue(initializer.Symbol, out var symbolFormula) ||
                    symbolFormula.Kind != SmtValueKind.Int ||
                    initializer.Bound.Kind != SmtValueKind.Int ||
                    StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(forStatement.Statement, symbol, semanticModel, cancellationToken) ||
                        ForLoopIncrementorsInvalidateSymbolValue(forStatement, symbol, semanticModel, cancellationToken)) ||
                    !ForLoopIncrementorsPreserveUpperBound(forStatement, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.LessThanOrEqual,
                    symbolFormula,
                    initializer.Bound));
            }
        }

        private static bool ForLoopIncrementorsInvalidateSymbolValue(
            ForStatementSyntax forStatement,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var incrementor in forStatement.Incrementors)
            {
                if (NodeMutatesSymbol(incrementor, symbol, semanticModel, cancellationToken) ||
                    NodeMayMutateSymbolThroughReference(incrementor, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddPreLoopMonotonicLowerBoundFacts(
            ICollection<SmtFormula> facts,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumeratePreLoopInitializerBounds(loopStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolSmtValue(initializer.Symbol, out var symbolFormula) ||
                    symbolFormula.Kind != SmtValueKind.Int ||
                    initializer.Bound.Kind != SmtValueKind.Int ||
                    LoopHeaderInvalidatesSymbolValue(loopStatement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(loopBody, symbol, semanticModel, cancellationToken) ||
                        LoopHeaderInvalidatesSymbolValue(loopStatement, symbol, semanticModel, cancellationToken)) ||
                    !LoopBodyMutationsPreserveLowerBound(loopBody, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    symbolFormula,
                    initializer.Bound));
            }
        }

        private static void AddPreLoopMonotonicUpperBoundFacts(
            ICollection<SmtFormula> facts,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddPreLoopMonotonicInitialUpperBoundFacts(facts, loopStatement, loopBody, semanticModel, cancellationToken);

            foreach (var initializer in EnumeratePreLoopStrictUpperBoundInitializers(loopStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolSmtValue(initializer.Symbol, out var symbolFormula) ||
                    symbolFormula.Kind != SmtValueKind.Int ||
                    initializer.UpperBound.Kind != SmtValueKind.Int ||
                    LoopHeaderInvalidatesSymbolValue(loopStatement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(loopBody, symbol, semanticModel, cancellationToken) ||
                        LoopHeaderInvalidatesSymbolValue(loopStatement, symbol, semanticModel, cancellationToken)) ||
                    !LoopBodyMutationsPreserveUpperBound(loopBody, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.LessThan,
                    symbolFormula,
                    initializer.UpperBound));
            }
        }

        private static void AddPreLoopMonotonicInitialUpperBoundFacts(
            ICollection<SmtFormula> facts,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumeratePreLoopInitializerBounds(loopStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolSmtValue(initializer.Symbol, out var symbolFormula) ||
                    symbolFormula.Kind != SmtValueKind.Int ||
                    initializer.Bound.Kind != SmtValueKind.Int ||
                    LoopHeaderInvalidatesSymbolValue(loopStatement, initializer.Symbol, semanticModel, cancellationToken) ||
                    initializer.BoundSymbols.Any(symbol =>
                        StatementInvalidatesSymbolValue(loopBody, symbol, semanticModel, cancellationToken) ||
                        LoopHeaderInvalidatesSymbolValue(loopStatement, symbol, semanticModel, cancellationToken)) ||
                    !LoopBodyMutationsPreserveUpperBound(loopBody, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.LessThanOrEqual,
                    symbolFormula,
                    initializer.Bound));
            }
        }

        private static IEnumerable<(ISymbol Symbol, SmtFormula Bound, IReadOnlyList<ISymbol> BoundSymbols)> EnumeratePreLoopInitializerBounds(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumeratePreLoopInitializerExpressions(loopStatement, semanticModel, cancellationToken))
            {
                if (TryTranslateInitializerBound(
                        initializer.Value,
                        initializer.Symbol,
                        semanticModel,
                        cancellationToken,
                        out var bound,
                        out var boundSymbols) &&
                    !AnyPriorStatementsInvalidateInitializer(
                        initializer,
                        loopStatement,
                        boundSymbols,
                        semanticModel,
                        cancellationToken))
                {
                    yield return (initializer.Symbol, bound, boundSymbols);
                }
            }
        }

        private static IEnumerable<(ISymbol Symbol, SmtFormula UpperBound, IReadOnlyList<ISymbol> BoundSymbols)> EnumeratePreLoopStrictUpperBoundInitializers(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumeratePreLoopInitializerExpressions(loopStatement, semanticModel, cancellationToken))
            {
                if (TryGetStrictUpperBoundInitializer(
                        initializer.Value,
                        initializer.Symbol,
                        semanticModel,
                        cancellationToken,
                        out var upperBound,
                        out var boundSymbols) &&
                    !AnyPriorStatementsInvalidateInitializer(
                        initializer,
                        loopStatement,
                        boundSymbols,
                        semanticModel,
                        cancellationToken))
                {
                    yield return (initializer.Symbol, upperBound, boundSymbols);
                }
            }
        }

        private static IEnumerable<(ISymbol Symbol, ExpressionSyntax Value, int StatementIndex)> EnumeratePreLoopInitializerExpressions(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (loopStatement.Parent is not BlockSyntax containingBlock)
            {
                yield break;
            }

            var loopIndex = containingBlock.Statements.IndexOf(loopStatement);
            for (var statementIndex = 0; statementIndex < loopIndex; statementIndex++)
            {
                var statement = containingBlock.Statements[statementIndex];
                if (statement is LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 } localDeclaration)
                {
                    var declarator = localDeclaration.Declaration.Variables[0];
                    if (declarator.Initializer != null &&
                        semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol)
                    {
                        yield return (localSymbol.OriginalDefinition, declarator.Initializer.Value, statementIndex);
                    }

                    continue;
                }

                if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
                    assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is { } symbol &&
                    symbol is ILocalSymbol or IParameterSymbol)
                {
                    yield return (symbol.OriginalDefinition, assignment.Right, statementIndex);
                }
            }
        }

        private static bool AnyPriorStatementsInvalidateInitializer(
            (ISymbol Symbol, ExpressionSyntax Value, int StatementIndex) initializer,
            StatementSyntax loopStatement,
            IReadOnlyList<ISymbol> boundSymbols,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (loopStatement.Parent is not BlockSyntax containingBlock)
            {
                return true;
            }

            var loopIndex = containingBlock.Statements.IndexOf(loopStatement);
            for (var statementIndex = initializer.StatementIndex + 1; statementIndex < loopIndex; statementIndex++)
            {
                var statement = containingBlock.Statements[statementIndex];
                if (StatementInvalidatesSymbolValue(statement, initializer.Symbol, semanticModel, cancellationToken) ||
                    boundSymbols.Any(symbol => StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LoopHeaderInvalidatesSymbolValue(
            StatementSyntax loopStatement,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var headerExpression = loopStatement switch
            {
                WhileStatementSyntax whileStatement => whileStatement.Condition,
                DoStatementSyntax doStatement => doStatement.Condition,
                ForStatementSyntax forStatement => forStatement.Condition,
                _ => null
            };

            return headerExpression != null &&
                SyntaxNodeInvalidatesSymbolValue(headerExpression, symbol, semanticModel, cancellationToken);
        }

        private static bool SyntaxNodeInvalidatesSymbolValue(
            SyntaxNode root,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken) ||
                    NodeMayMutateSymbolThroughReference(node, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<(ISymbol Symbol, SmtFormula UpperBound, IReadOnlyList<ISymbol> BoundSymbols)> EnumerateForLoopStrictUpperBoundInitializers(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (forStatement.Declaration != null)
            {
                foreach (var declarator in forStatement.Declaration.Variables)
                {
                    if (declarator.Initializer == null ||
                        semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol ||
                        !TryGetStrictUpperBoundInitializer(
                            declarator.Initializer.Value,
                            localSymbol.OriginalDefinition,
                            semanticModel,
                            cancellationToken,
                            out var upperBound,
                            out var boundSymbols))
                    {
                        continue;
                    }

                    yield return (localSymbol.OriginalDefinition, upperBound, boundSymbols);
                }
            }

            foreach (var expression in forStatement.Initializers)
            {
                if (expression is not AssignmentExpressionSyntax assignment ||
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } symbol ||
                    symbol is not ILocalSymbol and not IParameterSymbol ||
                    !TryGetStrictUpperBoundInitializer(
                        assignment.Right,
                        symbol.OriginalDefinition,
                        semanticModel,
                        cancellationToken,
                        out var upperBound,
                        out var boundSymbols))
                {
                    continue;
                }

                yield return (symbol.OriginalDefinition, upperBound, boundSymbols);
            }
        }

        private static bool TryGetStrictUpperBoundInitializer(
            ExpressionSyntax expression,
            ISymbol initializedSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula upperBound,
            out IReadOnlyList<ISymbol> upperBoundSymbols)
        {
            expression = UnwrapExpression(expression);
            if (expression is not BinaryExpressionSyntax binaryExpression)
            {
                upperBound = null!;
                upperBoundSymbols = Array.Empty<ISymbol>();
                return false;
            }

            if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var subtractedValue) &&
                subtractedValue > 0 &&
                TryTranslateInitializerBound(
                    binaryExpression.Left,
                    initializedSymbol,
                    semanticModel,
                    cancellationToken,
                    out upperBound,
                    out upperBoundSymbols))
            {
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.AddExpression))
            {
                if (TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue) &&
                    rightValue < 0 &&
                    TryTranslateInitializerBound(
                        binaryExpression.Left,
                        initializedSymbol,
                        semanticModel,
                        cancellationToken,
                        out upperBound,
                        out upperBoundSymbols))
                {
                    return true;
                }

                if (TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue) &&
                    leftValue < 0 &&
                    TryTranslateInitializerBound(
                        binaryExpression.Right,
                        initializedSymbol,
                        semanticModel,
                        cancellationToken,
                        out upperBound,
                        out upperBoundSymbols))
                {
                    return true;
                }
            }

            upperBound = null!;
            upperBoundSymbols = Array.Empty<ISymbol>();
            return false;
        }

        private static bool TryTranslateInitializerBound(
            ExpressionSyntax expression,
            ISymbol initializedSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula upperBound,
            out IReadOnlyList<ISymbol> upperBoundSymbols)
        {
            var referencedSymbols = GetReferencedLocalAndParameterSymbols(expression, semanticModel, cancellationToken);
            if (referencedSymbols.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, initializedSymbol)) ||
                !TryCreateIntegerValueFormula(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var candidate))
            {
                upperBound = null!;
                upperBoundSymbols = Array.Empty<ISymbol>();
                return false;
            }

            upperBound = candidate;
            upperBoundSymbols = referencedSymbols;
            return true;
        }

        private static bool ForLoopIncrementorsPreserveLowerBound(
            ForStatementSyntax forStatement,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var incrementor in forStatement.Incrementors)
            {
                if (!ExpressionReferencesSymbol(incrementor, symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (!IncrementorPreservesLowerBound(incrementor, symbol, semanticModel, cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LoopBodyMutationsPreserveLowerBound(
            StatementSyntax loopBody,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var node in loopBody.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (NodeMayMutateSymbolThroughReference(node, symbol, semanticModel, cancellationToken))
                {
                    return false;
                }

                if (!NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (node is not ExpressionSyntax expression ||
                    !IncrementorPreservesLowerBound(expression, symbol, semanticModel, cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IncrementorPreservesLowerBound(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (TryGetIncrementedOrDecrementedSymbol(expression, semanticModel, cancellationToken, out var unarySymbol, out var delta) &&
                SymbolEqualityComparer.Default.Equals(unarySymbol, symbol))
            {
                return delta >= 0;
            }

            if (expression is not AssignmentExpressionSyntax assignment ||
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } assignedSymbol ||
                !SymbolEqualityComparer.Default.Equals(assignedSymbol.OriginalDefinition, symbol))
            {
                return false;
            }

            if (assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
                TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var addedValue))
            {
                return addedValue >= 0;
            }

            if (assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) &&
                TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var subtractedValue))
            {
                return subtractedValue <= 0;
            }

            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                return TryIsSelfPlusNonNegativeConstant(assignment.Right, symbol, semanticModel, cancellationToken);
            }

            return false;
        }

        private static bool TryIsSelfPlusNonNegativeConstant(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (expression is not BinaryExpressionSyntax binaryExpression)
            {
                return false;
            }

            if (binaryExpression.IsKind(SyntaxKind.AddExpression))
            {
                return IsSymbolReference(binaryExpression.Left, symbol, semanticModel, cancellationToken) &&
                        TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue) &&
                        rightValue >= 0 ||
                    IsSymbolReference(binaryExpression.Right, symbol, semanticModel, cancellationToken) &&
                        TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue) &&
                        leftValue >= 0;
            }

            return binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                IsSymbolReference(binaryExpression.Left, symbol, semanticModel, cancellationToken) &&
                TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var subtractValue) &&
                subtractValue <= 0;
        }

        private static bool ForLoopIncrementorsPreserveUpperBound(
            ForStatementSyntax forStatement,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var incrementor in forStatement.Incrementors)
            {
                if (!ExpressionReferencesSymbol(incrementor, symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (!IncrementorPreservesUpperBound(incrementor, symbol, semanticModel, cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LoopBodyMutationsPreserveUpperBound(
            StatementSyntax loopBody,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var node in loopBody.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (NodeMayMutateSymbolThroughReference(node, symbol, semanticModel, cancellationToken))
                {
                    return false;
                }

                if (!NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (node is not ExpressionSyntax expression ||
                    !IncrementorPreservesUpperBound(expression, symbol, semanticModel, cancellationToken))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IncrementorPreservesUpperBound(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (TryGetIncrementedOrDecrementedSymbol(expression, semanticModel, cancellationToken, out var unarySymbol, out var delta) &&
                SymbolEqualityComparer.Default.Equals(unarySymbol, symbol))
            {
                return delta <= 0;
            }

            if (expression is not AssignmentExpressionSyntax assignment ||
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } assignedSymbol ||
                !SymbolEqualityComparer.Default.Equals(assignedSymbol.OriginalDefinition, symbol))
            {
                return false;
            }

            if (assignment.IsKind(SyntaxKind.AddAssignmentExpression) &&
                TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var addedValue))
            {
                return addedValue <= 0;
            }

            if (assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) &&
                TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var subtractedValue))
            {
                return subtractedValue >= 0;
            }

            if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                return TryIsSelfPlusNonPositiveConstant(assignment.Right, symbol, semanticModel, cancellationToken);
            }

            return false;
        }

        private static bool TryIsSelfPlusNonPositiveConstant(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (expression is not BinaryExpressionSyntax binaryExpression)
            {
                return false;
            }

            if (binaryExpression.IsKind(SyntaxKind.AddExpression))
            {
                return IsSymbolReference(binaryExpression.Left, symbol, semanticModel, cancellationToken) &&
                        TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue) &&
                        rightValue <= 0 ||
                    IsSymbolReference(binaryExpression.Right, symbol, semanticModel, cancellationToken) &&
                        TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue) &&
                        leftValue <= 0;
            }

            return binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                IsSymbolReference(binaryExpression.Left, symbol, semanticModel, cancellationToken) &&
                TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var subtractValue) &&
                subtractValue >= 0;
        }

        private static bool IsSymbolReference(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var expressionSymbol = semanticModel.GetSymbolInfo(UnwrapExpression(expression), cancellationToken).Symbol;
            return expressionSymbol != null &&
                SymbolEqualityComparer.Default.Equals(expressionSymbol.OriginalDefinition, symbol);
        }

        private static bool IsLoopBodyBlock(BlockSyntax block)
        {
            return block.Parent switch
            {
                WhileStatementSyntax whileStatement => ReferenceEquals(whileStatement.Statement, block),
                ForStatementSyntax forStatement => ReferenceEquals(forStatement.Statement, block),
                ForEachStatementSyntax forEachStatement => ReferenceEquals(forEachStatement.Statement, block),
                DoStatementSyntax doStatement => ReferenceEquals(doStatement.Statement, block),
                _ => false
            };
        }

        private static bool AnyConditionSymbolMutatedInStatement(
            ExpressionSyntax condition,
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var conditionSymbols = GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
            if (conditionSymbols.Count == 0)
            {
                return false;
            }

            foreach (var node in statement.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (node is AssignmentExpressionSyntax tupleAssignment &&
                    UnwrapExpression(tupleAssignment.Left) is TupleExpressionSyntax leftTuple &&
                    leftTuple.Arguments.Any(argument =>
                        ExpressionReferencesAnySymbol(argument.Expression, conditionSymbols, semanticModel, cancellationToken)))
                {
                    return true;
                }

                var mutatedExpression = node switch
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
                    continue;
                }

                var mutatedSymbol = semanticModel.GetSymbolInfo(mutatedExpression, cancellationToken).Symbol?.OriginalDefinition;
                if (mutatedSymbol != null &&
                    conditionSymbols.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, mutatedSymbol)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyConditionSymbolInvalidatedInStatement(
            ExpressionSyntax condition,
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var conditionSymbols = GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
            return conditionSymbols.Count != 0 &&
                conditionSymbols.Any(symbol => StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken));
        }

        private static bool StatementMutatesSymbol(
            StatementSyntax statement,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var node in statement.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                var mutatedExpression = node switch
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

                if (mutatedExpression != null &&
                    ExpressionReferencesSymbol(mutatedExpression, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExpressionMutatesAnySymbol(
            ExpressionSyntax expression,
            IReadOnlyCollection<ISymbol> symbols,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (symbols.Count == 0)
            {
                return false;
            }

            foreach (var node in expression.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (symbols.Any(symbol => NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLocalOrParameterReference(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbol = semanticModel.GetSymbolInfo(UnwrapExpression(expression), cancellationToken).Symbol?.OriginalDefinition;
            return symbol is ILocalSymbol or IParameterSymbol;
        }

        private static bool AnyReferencedSymbolAssignedBeforeUse(
            SyntaxNode condition,
            SyntaxNode branchRoot,
            int useSpanStart,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var dependencySymbols = GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
            return AnySymbolAssignedBeforeUse(
                dependencySymbols,
                branchRoot,
                useSpanStart,
                semanticModel,
                cancellationToken);
        }

        private static bool AnySwitchStatementConditionSymbolAssignedBeforeUse(
            SwitchStatementSyntax switchStatement,
            SwitchSectionSyntax section,
            int useSpanStart,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return AnySymbolAssignedBeforeUse(
                GetSwitchConditionSymbols(switchStatement, semanticModel, cancellationToken),
                section,
                useSpanStart,
                semanticModel,
                cancellationToken);
        }

        private static bool AnySwitchExpressionConditionSymbolAssignedBeforeUse(
            SwitchExpressionSyntax switchExpression,
            SwitchExpressionArmSyntax arm,
            int useSpanStart,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return AnySymbolAssignedBeforeUse(
                GetSwitchExpressionConditionSymbols(switchExpression, semanticModel, cancellationToken),
                arm,
                useSpanStart,
                semanticModel,
                cancellationToken);
        }

        private static bool AnySymbolAssignedBeforeUse(
            IReadOnlyList<ISymbol> symbols,
            SyntaxNode branchRoot,
            int useSpanStart,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (symbols.Count == 0)
            {
                return false;
            }

            foreach (var symbol in symbols)
            {
                if (IsSymbolAssignedBetween(
                        branchRoot,
                        branchRoot.SpanStart - 1,
                        useSpanStart,
                        symbol,
                        semanticModel,
                        cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSymbolAssignedBetween(
            SyntaxNode root,
            int afterSpanStart,
            int beforeSpanStart,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var node in root.DescendantNodes(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (node.SpanStart <= afterSpanStart || node.SpanStart >= beforeSpanStart)
                {
                    continue;
                }

                if (NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NodeMutatesSymbol(
            SyntaxNode node,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var mutatedExpression = node switch
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

            return mutatedExpression != null &&
                ExpressionReferencesSymbol(mutatedExpression, symbol, semanticModel, cancellationToken);
        }

        private static IReadOnlyList<ISymbol> GetReferencedLocalAndParameterSymbols(
            SyntaxNode root,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbols = new List<ISymbol>();
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (node is not ExpressionSyntax expression)
                {
                    continue;
                }

                var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition;
                if (symbol is ILocalSymbol or IParameterSymbol &&
                    symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
                {
                    symbols.Add(symbol);
                }
            }

            return symbols;
        }

        private static IReadOnlyList<ISymbol> GetConditionDependencySymbols(
            SyntaxNode root,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbols = new List<ISymbol>();
            AddReferencedSymbols(root, semanticModel, cancellationToken, symbols);
            AddDeclaredPatternSymbols(root, semanticModel, cancellationToken, symbols);
            AddMemberNotNullWhenTargetSymbols(root, semanticModel, cancellationToken, symbols);
            return symbols;
        }

        private static IEnumerable<(BlockSyntax Block, StatementSyntax ContainingStatement)> EnumerateContainingBlocks(SyntaxNode site)
        {
            for (SyntaxNode? current = site; current != null; current = current.Parent)
            {
                if (current is StatementSyntax statement &&
                    statement.Parent is BlockSyntax block)
                {
                    yield return (block, statement);
                }
            }
        }

        private static IReadOnlyList<SmtFormula> AddTemporaryContainingBlockEntryFacts(
            BlockSyntax block,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            RemoveFactsInvalidatedByContainingBlockEntry(block, semanticModel, cancellationToken, facts);
            var entryFacts = CollectContainingBlockEntryFacts(block, semanticModel, cancellationToken);
            if (entryFacts.Length == 0)
            {
                return Array.Empty<SmtFormula>();
            }

            return AddUniqueFacts(facts, entryFacts);
        }

        private static void AddContainingBlockEntryFacts(
            BlockSyntax block,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            RemoveFactsInvalidatedByContainingBlockEntry(block, semanticModel, cancellationToken, facts);
            AddUniqueFacts(facts, CollectContainingBlockEntryFacts(block, semanticModel, cancellationToken));
        }

        private static IReadOnlyList<SmtFormula> AddUniqueFacts(
            List<SmtFormula> facts,
            IReadOnlyList<SmtFormula> entryFacts)
        {
            var existingKeys = new HashSet<string>(facts.Select(GetFormulaKey), StringComparer.Ordinal);
            var addedFacts = new List<SmtFormula>(entryFacts.Count);
            foreach (var entryFact in entryFacts)
            {
                if (!existingKeys.Add(GetFormulaKey(entryFact)))
                {
                    continue;
                }

                facts.Add(entryFact);
                addedFacts.Add(entryFact);
            }

            return addedFacts;
        }

        private static void RemoveFactsInvalidatedByContainingBlockEntry(
            BlockSyntax block,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            foreach (var symbol in GetContainingBlockEntryAssignedSymbols(block, semanticModel, cancellationToken))
            {
                RemoveFactsReferencingSymbol(facts, symbol);
            }
        }

        private static IEnumerable<ISymbol> GetContainingBlockEntryAssignedSymbols(
            BlockSyntax block,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            ExpressionSyntax? condition = null;
            switch (block.Parent)
            {
                case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, block):
                    condition = ifStatement.Condition;
                    break;
                case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement, Statement: var statement }
                    when ReferenceEquals(statement, block):
                    condition = ifStatement.Condition;
                    break;
                case WhileStatementSyntax whileStatement when ReferenceEquals(whileStatement.Statement, block):
                    condition = whileStatement.Condition;
                    break;
                case ForStatementSyntax forStatement when ReferenceEquals(forStatement.Statement, block):
                    condition = forStatement.Condition;
                    break;
            }

            if (condition == null)
            {
                yield break;
            }

            foreach (var assignment in condition.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                         .OfType<AssignmentExpressionSyntax>())
            {
                if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    continue;
                }

                var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
                if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                {
                    yield return assignedSymbol.OriginalDefinition;
                }
            }
        }

        private static ImmutableArray<SmtFormula> CollectContainingBlockEntryFacts(
            BlockSyntax block,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var builder = ImmutableArray.CreateBuilder<SmtFormula>();
            switch (block.Parent)
            {
                case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, block):
                    AddBranchConditionFacts(ifStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, builder);
                    break;
                case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement, Statement: var statement }
                    when ReferenceEquals(statement, block):
                    AddBranchConditionFacts(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, builder);
                    break;
                case WhileStatementSyntax whileStatement when ReferenceEquals(whileStatement.Statement, block):
                    AddBranchConditionFacts(whileStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, builder);
                    builder.AddRange(CollectLoopBodyInvariantFacts(whileStatement, semanticModel, cancellationToken));
                    break;
                case DoStatementSyntax doStatement when ReferenceEquals(doStatement.Statement, block):
                    builder.AddRange(CollectLoopBodyInvariantFacts(doStatement, semanticModel, cancellationToken));
                    break;
                case ForStatementSyntax forStatement when ReferenceEquals(forStatement.Statement, block):
                    if (forStatement.Condition != null)
                    {
                        AddBranchConditionFacts(forStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, builder);
                    }

                    builder.AddRange(CollectLoopBodyInvariantFacts(forStatement, semanticModel, cancellationToken));
                    break;
                case ForEachStatementSyntax forEachStatement when ReferenceEquals(forEachStatement.Statement, block):
                    AddForeachBodyEntryFacts(
                        builder,
                        forEachStatement.Expression,
                        semanticModel.GetDeclaredSymbol(forEachStatement, cancellationToken) as ILocalSymbol,
                        forEachStatement,
                        forEachStatement.Statement,
                        semanticModel,
                        cancellationToken);
                    break;
                case ForEachVariableStatementSyntax forEachVariableStatement when ReferenceEquals(forEachVariableStatement.Statement, block):
                    AddForeachBodyEntryFacts(
                        builder,
                        forEachVariableStatement.Expression,
                        iterationSymbol: null,
                        forEachVariableStatement,
                        forEachVariableStatement.Statement,
                        semanticModel,
                        cancellationToken);
                    break;
            }

            return builder.ToImmutable();
        }

        private static void RemoveTemporaryFacts(
            List<SmtFormula> facts,
            IReadOnlyList<SmtFormula> temporaryFacts)
        {
            if (temporaryFacts.Count == 0)
            {
                return;
            }

            for (var factIndex = facts.Count - 1; factIndex >= 0; factIndex--)
            {
                if (temporaryFacts.Any(temporaryFact => ReferenceEquals(temporaryFact, facts[factIndex])))
                {
                    facts.RemoveAt(factIndex);
                }
            }
        }

        private static void AddPriorStatementFacts(
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
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
                    if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol)
                    {
                        AddAssignedValueFacts(localSymbol, declarator.Initializer.Value, semanticModel, cancellationToken, facts);
                    }

                    AddNormalCompletionFacts(
                        declarator.Initializer.Value,
                        localDeclaration,
                        false,
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

                var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
                if (assignedSymbol != null)
                {
                    assignedSymbol = NormalizeMutatedSymbol(assignedSymbol);
                }

                SmtFormula? previousAssignedValue = null;
                if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                {
                    TryGetCurrentSymbolValue(facts, assignedSymbol.OriginalDefinition, out previousAssignedValue);
                }

                RemoveFactsInvalidatedByNestedMutations(assignment.Left, semanticModel, cancellationToken, facts);
                RemoveFactsInvalidatedByNestedMutations(assignment.Right, semanticModel, cancellationToken, facts);
                if (assignedSymbol is IFieldSymbol or IPropertySymbol &&
                    IsCurrentInstanceMemberReference(assignment.Left, semanticModel, cancellationToken))
                {
                    RemoveFactsReferencingImplicitThisMember(facts, assignedSymbol.Name);
                }

                if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                {
                    var originalAssignedSymbol = assignedSymbol.OriginalDefinition;
                    var coalesceAssignmentIsKnownNoOp = assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                        (IsKnownNonNullReferenceSymbol(facts, originalAssignedSymbol) ||
                         IsKnownNullableHasValueSymbol(facts, originalAssignedSymbol));
                    var coalesceAssignmentIsKnownNullableNoValue = assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) &&
                        IsKnownNullableNoValueSymbol(facts, originalAssignedSymbol);
                    if (!coalesceAssignmentIsKnownNoOp)
                    {
                        RemoveFactsReferencingSymbol(facts, originalAssignedSymbol);
                    }

                    if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        AddAssignedValueFacts(originalAssignedSymbol, assignment.Right, semanticModel, cancellationToken, facts);
                    }
                    else if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
                    {
                        if (!coalesceAssignmentIsKnownNoOp)
                        {
                            if (coalesceAssignmentIsKnownNullableNoValue)
                            {
                                AddAssignedValueFacts(originalAssignedSymbol, assignment.Right, semanticModel, cancellationToken, facts);
                            }
                            else
                            {
                                AddCoalesceAssignmentFacts(originalAssignedSymbol, assignment.Right, previousAssignedValue, semanticModel, cancellationToken, facts);
                            }
                        }
                    }
                    else if (previousAssignedValue != null &&
                             SymbolicReachabilityService.TryCreateCompoundAssignmentFact(
                                 originalAssignedSymbol,
                                 previousAssignedValue,
                                 assignment,
                                 semanticModel,
                                 cancellationToken,
                                 ExpressionReferencesSymbol(assignment.Right, originalAssignedSymbol, semanticModel, cancellationToken),
                                 out var compoundAssignmentFact))
                    {
                        facts.Add(compoundAssignmentFact);
                    }
                }

                AddElementAssignmentFact(assignment, semanticModel, cancellationToken, facts);
                AddNormalCompletionFacts(
                    assignment.Right,
                    expressionStatement,
                    assignedSymbol is not ILocalSymbol and not IParameterSymbol,
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
                    out var mutatedSymbol,
                    out var delta) &&
                TryGetCurrentSymbolValue(facts, mutatedSymbol, out var previousMutatedValue))
            {
                RemoveFactsReferencingSymbol(facts, mutatedSymbol);
                if (TryCreateIncrementOrDecrementFact(mutatedSymbol, previousMutatedValue, delta, out var mutationFact))
                {
                    facts.Add(mutationFact);
                }

                return;
            }

            var factsBeforeStatement = facts.ToArray();
            RemoveFactsInvalidatedByNestedMutations(statement, semanticModel, cancellationToken, facts);
            if (statement is BlockSyntax block)
            {
                AddCompletedScopedBlockFacts(block, semanticModel, cancellationToken, facts);
            }
            else if (statement is IfStatementSyntax ifStatement)
            {
                AddCompletedIfStatementFacts(ifStatement, factsBeforeStatement, semanticModel, cancellationToken, facts);
            }
            else if (statement is SwitchStatementSyntax switchStatement)
            {
                AddCompletedSwitchStatementFacts(switchStatement, factsBeforeStatement, semanticModel, cancellationToken, facts);
            }
            else if (statement is TryStatementSyntax tryStatement)
            {
                AddCompletedTryStatementFacts(tryStatement, semanticModel, cancellationToken, facts);
            }
            else if (statement is UsingStatementSyntax usingStatement)
            {
                AddCompletedUsingStatementFacts(usingStatement, semanticModel, cancellationToken, facts);
            }
            else if (statement is ExpressionStatementSyntax completedExpressionStatement)
            {
                AddNormalCompletionFacts(
                    completedExpressionStatement.Expression,
                    completedExpressionStatement,
                    true,
                    semanticModel,
                    cancellationToken,
                    facts);
            }
            else
            {
                AddCompletedLoopStatementFacts(statement, semanticModel, cancellationToken, facts);
            }
        }

        private static void AddCompletedScopedBlockFacts(
            BlockSyntax block,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (StatementDefinitelyExits(block, semanticModel, cancellationToken))
            {
                return;
            }

            var blockFacts = new List<SmtFormula>(facts);
            var processedStatementCount = 0;
            foreach (var statement in block.Statements)
            {
                if (processedStatementCount >= MaxScopedBlockCompletionStatements)
                {
                    return;
                }

                processedStatementCount++;
                AddPriorStatementFacts(statement, semanticModel, cancellationToken, blockFacts);
                if (StatementDefinitelyExits(statement, semanticModel, cancellationToken))
                {
                    break;
                }
            }

            AddVisibleSingleBranchFacts(blockFacts, block, semanticModel, cancellationToken, facts);
        }

        private static void AddCompletedTryStatementFacts(
            TryStatementSyntax tryStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            AddCompletedTryCatchFacts(tryStatement, semanticModel, cancellationToken, facts);
            AddCompletedTryFinallyFacts(tryStatement, semanticModel, cancellationToken, facts);
        }

        private static void AddCompletedTryCatchFacts(
            TryStatementSyntax tryStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (tryStatement.Finally != null)
            {
                return;
            }

            var completedBranches = CollectCompletedTryBranches(tryStatement, facts, semanticModel, cancellationToken);
            if (completedBranches.Count == 0)
            {
                return;
            }

            if (completedBranches.Count == 1)
            {
                var branch = completedBranches[0];
                AddVisibleSingleBranchFacts(branch.Facts, branch.Statement, semanticModel, cancellationToken, facts);
                return;
            }

            AddIdenticalCompletedBranchFacts(completedBranches, semanticModel, cancellationToken, facts);
        }

        private static List<CompletedBranchFacts> CollectCompletedTryBranches(
            TryStatementSyntax tryStatement,
            IEnumerable<SmtFormula> currentFacts,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var completedBranches = new List<CompletedBranchFacts>();
            AddCompletedTryBlockBranch(
                completedBranches,
                tryStatement.Block,
                currentFacts,
                semanticModel,
                cancellationToken);

            foreach (var catchClause in tryStatement.Catches)
            {
                if (completedBranches.Count >= MaxTryCompletionBranches)
                {
                    return completedBranches;
                }

                AddCompletedCatchBranch(
                    completedBranches,
                    catchClause,
                    currentFacts,
                    semanticModel,
                    cancellationToken);
            }

            return completedBranches;
        }

        private static void AddCompletedTryBlockBranch(
            ICollection<CompletedBranchFacts> completedBranches,
            BlockSyntax block,
            IEnumerable<SmtFormula> currentFacts,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (StatementDefinitelyExits(block, semanticModel, cancellationToken))
            {
                return;
            }

            var branchFacts = CollectCompletedBlockFacts(currentFacts, block, semanticModel, cancellationToken);
            completedBranches.Add(new CompletedBranchFacts(block, branchFacts));
        }

        private static void AddCompletedCatchBranch(
            ICollection<CompletedBranchFacts> completedBranches,
            CatchClauseSyntax catchClause,
            IEnumerable<SmtFormula> currentFacts,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (StatementDefinitelyExits(catchClause.Block, semanticModel, cancellationToken))
            {
                return;
            }

            var branchFacts = new List<SmtFormula>(currentFacts);
            AddCatchBodyEntryFacts(
                branchFacts,
                catchClause,
                catchClause.Block.Span.End,
                semanticModel,
                cancellationToken);
            AddCompletedBlockFacts(branchFacts, catchClause.Block, semanticModel, cancellationToken);
            completedBranches.Add(new CompletedBranchFacts(catchClause.Block, branchFacts));
        }

        private static List<SmtFormula> CollectCompletedBlockFacts(
            IEnumerable<SmtFormula> currentFacts,
            BlockSyntax block,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var branchFacts = new List<SmtFormula>(currentFacts);
            AddCompletedBlockFacts(branchFacts, block, semanticModel, cancellationToken);
            return branchFacts;
        }

        private static void AddCompletedBlockFacts(
            List<SmtFormula> facts,
            BlockSyntax block,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var processedStatementCount = 0;
            foreach (var statement in block.Statements)
            {
                if (processedStatementCount >= MaxScopedBlockCompletionStatements)
                {
                    return;
                }

                processedStatementCount++;
                AddPriorStatementFacts(statement, semanticModel, cancellationToken, facts);
                if (StatementDefinitelyExits(statement, semanticModel, cancellationToken))
                {
                    return;
                }
            }
        }

        private static void AddCompletedTryFinallyFacts(
            TryStatementSyntax tryStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (tryStatement.Finally?.Block is not { } finallyBlock ||
                StatementDefinitelyExits(finallyBlock, semanticModel, cancellationToken))
            {
                return;
            }

            var finallyFacts = new List<SmtFormula>(facts);
            var processedStatementCount = 0;
            foreach (var statement in finallyBlock.Statements)
            {
                if (processedStatementCount >= MaxScopedBlockCompletionStatements)
                {
                    return;
                }

                processedStatementCount++;
                AddPriorStatementFacts(statement, semanticModel, cancellationToken, finallyFacts);
                if (StatementDefinitelyExits(statement, semanticModel, cancellationToken))
                {
                    break;
                }
            }

            AddVisibleSingleBranchFacts(finallyFacts, finallyBlock, semanticModel, cancellationToken, facts);
        }

        private static void AddCompletedUsingStatementFacts(
            UsingStatementSyntax usingStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (StatementDefinitelyExits(usingStatement.Statement, semanticModel, cancellationToken))
            {
                return;
            }

            if (usingStatement.Expression != null)
            {
                AddNormalCompletionFacts(
                    usingStatement.Expression,
                    usingStatement,
                    true,
                    semanticModel,
                    cancellationToken,
                    facts);
            }

            if (usingStatement.Declaration == null)
            {
                return;
            }

            foreach (var declarator in usingStatement.Declaration.Variables)
            {
                if (declarator.Initializer == null)
                {
                    continue;
                }

                AddNormalCompletionFacts(
                    declarator.Initializer.Value,
                    usingStatement,
                    true,
                    semanticModel,
                    cancellationToken,
                    facts);
            }
        }

        private static void AddIdenticalCompletedBranchFacts(
            IReadOnlyList<CompletedBranchFacts> completedBranches,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            var visibleBranchFacts = completedBranches
                .Select(branch => FilterVisibleBranchFacts(branch.Facts, branch.Statement, semanticModel, cancellationToken))
                .ToArray();
            if (visibleBranchFacts.Length == 0)
            {
                return;
            }

            var commonKeys = new HashSet<string>(visibleBranchFacts[0].Select(GetFormulaKey), StringComparer.Ordinal);
            for (var branchIndex = 1; branchIndex < visibleBranchFacts.Length; branchIndex++)
            {
                commonKeys.IntersectWith(visibleBranchFacts[branchIndex].Select(GetFormulaKey));
                if (commonKeys.Count == 0)
                {
                    return;
                }
            }

            var existingKeys = new HashSet<string>(facts.Select(GetFormulaKey), StringComparer.Ordinal);
            var addedCount = 0;
            foreach (var fact in visibleBranchFacts[0])
            {
                var key = GetFormulaKey(fact);
                if (!commonKeys.Contains(key) || !existingKeys.Add(key))
                {
                    continue;
                }

                facts.Add(fact);
                addedCount++;
                if (addedCount >= MaxMergedTryFacts)
                {
                    return;
                }
            }
        }

        private static void AddNormalCompletionFacts(
            ExpressionSyntax expression,
            StatementSyntax statement,
            bool includeThrowGuardFacts,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (includeThrowGuardFacts)
            {
                AddTopLevelThrowGuardNormalCompletionFacts(expression, statement, semanticModel, cancellationToken, facts);
            }

            AddTopLevelNotNullParameterNormalCompletionFacts(expression, statement, semanticModel, cancellationToken, facts);
            AddTopLevelDoesNotReturnIfNormalCompletionFacts(expression, statement, semanticModel, cancellationToken, facts);
            AddTopLevelMemberNotNullNormalCompletionFacts(expression, semanticModel, cancellationToken, facts);
            AddTopLevelArrayCreationNormalCompletionFacts(expression, statement, semanticModel, cancellationToken, facts);
            AddTopLevelDereferenceNormalCompletionFacts(expression, statement, semanticModel, cancellationToken, facts);
        }

        private static void AddTopLevelNotNullParameterNormalCompletionFacts(
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            expression = UnwrapAwaitedNormalCompletionExpression(expression);
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
                    !argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.None))
                {
                    continue;
                }

                AddStableReferenceNonNullFact(argumentSyntax.Expression, statement, semanticModel, cancellationToken, facts);
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
                    StringComparison.Ordinal));
        }

        private static void AddTopLevelMemberNotNullNormalCompletionFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            expression = UnwrapAwaitedNormalCompletionExpression(expression);
            if (expression is not InvocationExpressionSyntax invocation ||
                semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod.IsStatic ||
                !IsCurrentInstanceInvocation(invocation))
            {
                return;
            }

            var memberTargets = GetMemberNotNullTargets(invocationOperation.TargetMethod);
            foreach (var memberTarget in memberTargets)
            {
                if (!TryResolveMemberNotNullTarget(invocationOperation.TargetMethod.ContainingType, memberTarget, out var member) ||
                    !TryCreateImplicitThisMemberReferenceFormula(member, out var memberFormula))
                {
                    continue;
                }

                AddUniqueFact(
                    facts,
                    new SmtBinaryFormula(
                        SmtBinaryOperator.NotEqual,
                        memberFormula,
                        new SmtNullConstant()));
            }
        }

        private static bool IsCurrentInstanceInvocation(InvocationExpressionSyntax invocation)
        {
            var invokedExpression = UnwrapExpression(invocation.Expression);
            return invokedExpression is IdentifierNameSyntax ||
                invokedExpression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
        }

        private static IEnumerable<string> GetMemberNotNullTargets(IMethodSymbol method)
        {
            var targets = new List<string>();
            AddMemberNotNullTargets(method, targets);
            if (!SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition))
            {
                AddMemberNotNullTargets(method.OriginalDefinition, targets);
            }

            return targets.Distinct(StringComparer.Ordinal);
        }

        private static void AddMemberNotNullTargets(IMethodSymbol method, ICollection<string> targets)
        {
            foreach (var attribute in method.GetAttributes())
            {
                if (!string.Equals(
                        GetFullMetadataName(attribute.AttributeClass),
                        MemberNotNullAttributeName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var argument in attribute.ConstructorArguments)
                {
                    AddMemberNotNullTarget(argument, targets);
                }
            }
        }

        private static void AddMemberNotNullTarget(TypedConstant argument, ICollection<string> targets)
        {
            if (argument.Kind == TypedConstantKind.Array)
            {
                foreach (var item in argument.Values)
                {
                    AddMemberNotNullTarget(item, targets);
                }

                return;
            }

            if (argument.Value is string target &&
                !string.IsNullOrWhiteSpace(target))
            {
                targets.Add(target);
            }
        }

        private static bool TryResolveMemberNotNullTarget(
            INamedTypeSymbol containingType,
            string target,
            out ISymbol member)
        {
            var memberName = NormalizeMemberNotNullTarget(target);
            if (memberName == null)
            {
                member = null!;
                return false;
            }

            var candidates = containingType.GetMembers(memberName)
                .Where(candidate =>
                    candidate is IFieldSymbol or IPropertySymbol &&
                    !candidate.IsStatic &&
                    TryGetMemberNotNullTargetType(candidate, out var type) &&
                    IsReferenceLikeType(type))
                .ToArray();
            if (candidates.Length != 1)
            {
                member = null!;
                return false;
            }

            member = candidates[0].OriginalDefinition;
            return true;
        }

        private static string? NormalizeMemberNotNullTarget(string target)
        {
            target = target.Trim();
            if (target.StartsWith("this.", StringComparison.Ordinal))
            {
                target = target.Substring("this.".Length);
            }

            return target.Length != 0 && !target.Contains(".", StringComparison.Ordinal)
                ? target
                : null;
        }

        private static bool TryGetMemberNotNullTargetType(ISymbol member, out ITypeSymbol type)
        {
            switch (member)
            {
                case IFieldSymbol fieldSymbol:
                    type = fieldSymbol.Type;
                    return true;
                case IPropertySymbol propertySymbol:
                    type = propertySymbol.Type;
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }

        private static bool TryCreateImplicitThisMemberReferenceFormula(ISymbol member, out SmtFormula formula)
        {
            if (!TryGetMemberNotNullTargetType(member, out var type) ||
                !TryGetValueKind(type, out var kind) ||
                kind != SmtValueKind.Reference)
            {
                formula = null!;
                return false;
            }

            return SymbolicIrFormulaEncoder.TryEncodeTerm(
                new SymbolicMemberTerm(
                    new SymbolicVariableTerm(ImplicitThisVariableName, SmtValueKind.Reference),
                    member.Name,
                    kind),
                out formula);
        }

        private static void AddTopLevelDoesNotReturnIfNormalCompletionFacts(
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            expression = UnwrapAwaitedNormalCompletionExpression(expression);
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
                    AnyConditionSymbolInvalidatedInStatement(argumentSyntax.Expression, statement, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddBranchConditionFacts(
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
                        StringComparison.Ordinal) ||
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

        private static bool MethodHasDoesNotReturnAttribute(IMethodSymbol method)
        {
            return SymbolHasDoesNotReturnAttribute(method) ||
                (!SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition) &&
                 SymbolHasDoesNotReturnAttribute(method.OriginalDefinition));
        }

        private static bool SymbolHasDoesNotReturnAttribute(IMethodSymbol method)
        {
            return method.GetAttributes().Any(attribute =>
                string.Equals(
                    GetFullMetadataName(attribute.AttributeClass),
                    DoesNotReturnAttributeName,
                    StringComparison.Ordinal));
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

        private static void AddTopLevelArrayCreationNormalCompletionFacts(
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            expression = UnwrapAwaitedNormalCompletionExpression(expression);
            if (expression is not ArrayCreationExpressionSyntax arrayCreation)
            {
                return;
            }

            foreach (var sizeExpression in GetExplicitArraySizeExpressions(arrayCreation))
            {
                if (AnyConditionSymbolInvalidatedInStatement(sizeExpression, statement, semanticModel, cancellationToken) ||
                    !TryCreateIntegerValueFormula(
                        sizeExpression,
                        semanticModel,
                        cancellationToken,
                        out var sizeFormula))
                {
                    continue;
                }

                AddUniqueFact(
                    facts,
                    new SmtBinaryFormula(
                        SmtBinaryOperator.GreaterThanOrEqual,
                        sizeFormula,
                        new SmtIntegerConstant(0)));
            }
        }

        private static void AddTopLevelThrowGuardNormalCompletionFacts(
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            expression = UnwrapAwaitedNormalCompletionExpression(expression);
            AddThrowGuardedExpressionFacts(expression, statement, semanticModel, cancellationToken, facts);
        }

        private static void AddTopLevelDereferenceNormalCompletionFacts(
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            expression = UnwrapExpression(expression);
            if (expression is AwaitExpressionSyntax awaitExpression)
            {
                var awaitableExpression = UnwrapExpression(awaitExpression.Expression);
                AddStableReferenceNonNullFact(awaitableExpression, statement, semanticModel, cancellationToken, facts);
                expression = awaitableExpression;
            }

            if (expression is ElementAccessExpressionSyntax elementAccess &&
                !AnyConditionSymbolInvalidatedInStatement(elementAccess, statement, semanticModel, cancellationToken) &&
                SymbolicReachabilityService.TryCreateBuiltInElementAccessInRangeCondition(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFact))
            {
                AddUniqueFact(facts, inRangeFact);
            }

            if (!TryGetTopLevelDereferenceReceiver(expression, semanticModel, cancellationToken, out var receiver) ||
                !AddStableReferenceNonNullFact(receiver, statement, semanticModel, cancellationToken, facts))
            {
                return;
            }
        }

        private static bool AddStableReferenceNonNullFact(
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (!IsLocalOrParameterReference(expression, semanticModel, cancellationToken) ||
                AnyConditionSymbolMutatedInStatement(expression, statement, semanticModel, cancellationToken))
            {
                return false;
            }

            if (SymbolicReachabilityService.TryCreateReferenceNullComparison(
                    expression,
                    semanticModel,
                    cancellationToken,
                    equalToNull: false,
                    out var notNullFact))
            {
                AddUniqueFact(facts, notNullFact);
                return true;
            }

            return false;
        }

        private static bool TryGetTopLevelDereferenceReceiver(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax receiver)
        {
            expression = UnwrapExpression(expression);
            switch (expression)
            {
                case InvocationExpressionSyntax invocation
                    when UnwrapExpression(invocation.Expression) is MemberAccessExpressionSyntax memberAccess &&
                         !IsReducedExtensionMethodInvocation(invocation, semanticModel, cancellationToken):
                    receiver = memberAccess.Expression;
                    return true;
                case MemberAccessExpressionSyntax memberAccess:
                    receiver = memberAccess.Expression;
                    return true;
                case ElementAccessExpressionSyntax elementAccess:
                    receiver = elementAccess.Expression;
                    return true;
                default:
                    receiver = null!;
                    return false;
            }
        }

        private static ExpressionSyntax UnwrapAwaitedNormalCompletionExpression(ExpressionSyntax expression)
        {
            expression = UnwrapExpression(expression);
            return expression is AwaitExpressionSyntax awaitExpression
                ? UnwrapExpression(awaitExpression.Expression)
                : expression;
        }

        private static bool IsReducedExtensionMethodInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation &&
                invocationOperation.TargetMethod.ReducedFrom != null;
        }

        private static void AddUniqueFact(
            ICollection<SmtFormula> facts,
            SmtFormula fact)
        {
            var key = GetFormulaKey(fact);
            if (facts.Any(existing => string.Equals(GetFormulaKey(existing), key, StringComparison.Ordinal)))
            {
                return;
            }

            facts.Add(fact);
        }

        private static void AddCompletedIfStatementFacts(
            IfStatementSyntax ifStatement,
            IReadOnlyList<SmtFormula> factsBeforeStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (StatementDefinitelyExits(ifStatement.Statement, semanticModel, cancellationToken) &&
                (ifStatement.Else?.Statement == null ||
                 !AnyConditionSymbolInvalidatedInStatement(ifStatement.Condition, ifStatement.Else.Statement, semanticModel, cancellationToken)))
            {
                AddBranchConditionFacts(
                    ifStatement.Condition,
                    branchWhenTrue: false,
                    semanticModel,
                    cancellationToken,
                    facts);
            }

            if (ifStatement.Else?.Statement is { } elseStatement &&
                StatementDefinitelyExits(elseStatement, semanticModel, cancellationToken) &&
                !AnyConditionSymbolInvalidatedInStatement(ifStatement.Condition, ifStatement.Statement, semanticModel, cancellationToken))
            {
                AddBranchConditionFacts(
                    ifStatement.Condition,
                    branchWhenTrue: true,
                    semanticModel,
                    cancellationToken,
                    facts);
            }

            AddSingleSurvivingBranchFacts(ifStatement, factsBeforeStatement, semanticModel, cancellationToken, facts);
            AddCompletedIfElseMergedFacts(ifStatement, semanticModel, cancellationToken, facts);
            AddCompletedIfImplicitElseMergedFacts(ifStatement, factsBeforeStatement, semanticModel, cancellationToken, facts);
        }

        private static void AddSingleSurvivingBranchFacts(
            IfStatementSyntax ifStatement,
            IReadOnlyList<SmtFormula> factsBeforeStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            var trueBranchExits = StatementDefinitelyExits(ifStatement.Statement, semanticModel, cancellationToken);
            var falseBranch = ifStatement.Else?.Statement;
            var falseBranchExits = falseBranch != null && StatementDefinitelyExits(falseBranch, semanticModel, cancellationToken);

            if (trueBranchExits &&
                falseBranch is { } survivingFalseBranch &&
                !falseBranchExits &&
                !AnyConditionSymbolInvalidatedInStatement(ifStatement.Condition, survivingFalseBranch, semanticModel, cancellationToken))
            {
                var branchFacts = CollectCompletedBranchFacts(
                    factsBeforeStatement,
                    ifStatement.Condition,
                    branchWhenTrue: false,
                    survivingFalseBranch,
                    semanticModel,
                    cancellationToken);
                AddVisibleSingleBranchFacts(branchFacts, survivingFalseBranch, semanticModel, cancellationToken, facts);
            }

            if (falseBranchExits &&
                !trueBranchExits &&
                !AnyConditionSymbolInvalidatedInStatement(ifStatement.Condition, ifStatement.Statement, semanticModel, cancellationToken))
            {
                var branchFacts = CollectCompletedBranchFacts(
                    factsBeforeStatement,
                    ifStatement.Condition,
                    branchWhenTrue: true,
                    ifStatement.Statement,
                    semanticModel,
                    cancellationToken);
                AddVisibleSingleBranchFacts(branchFacts, ifStatement.Statement, semanticModel, cancellationToken, facts);
            }
        }

        private static void AddVisibleSingleBranchFacts(
            IReadOnlyCollection<SmtFormula> branchFacts,
            StatementSyntax survivingBranch,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            var existingKeys = new HashSet<string>(facts.Select(GetFormulaKey), StringComparer.Ordinal);
            foreach (var branchFact in FilterVisibleBranchFacts(branchFacts, survivingBranch, semanticModel, cancellationToken))
            {
                var key = GetFormulaKey(branchFact);
                if (existingKeys.Add(key))
                {
                    facts.Add(branchFact);
                }
            }
        }

        private static List<SmtFormula> FilterVisibleBranchFacts(
            IReadOnlyCollection<SmtFormula> branchFacts,
            StatementSyntax survivingBranch,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var visibleFacts = new List<SmtFormula>(branchFacts.Count);
            var hiddenSymbols = GetLocalsDeclaredInside(survivingBranch, semanticModel, cancellationToken);
            foreach (var branchFact in branchFacts)
            {
                if (hiddenSymbols.Any(symbol => SmtFormulaReferenceScanner.ContainsVariablePrefix(
                    branchFact,
                    SymbolicFactFactory.GetSmtVariableName(symbol))))
                {
                    continue;
                }

                visibleFacts.Add(branchFact);
            }

            return visibleFacts;
        }

        private static IReadOnlyList<ISymbol> GetLocalsDeclaredInside(
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbols = new List<ISymbol>();
            foreach (var node in statement.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                ISymbol? symbol = node switch
                {
                    VariableDeclaratorSyntax declarator => semanticModel.GetDeclaredSymbol(declarator, cancellationToken),
                    SingleVariableDesignationSyntax designation => semanticModel.GetDeclaredSymbol(designation, cancellationToken),
                    ForEachStatementSyntax forEachStatement => semanticModel.GetDeclaredSymbol(forEachStatement, cancellationToken),
                    CatchDeclarationSyntax catchDeclaration => semanticModel.GetDeclaredSymbol(catchDeclaration, cancellationToken),
                    _ => null
                };

                if (symbol is ILocalSymbol &&
                    symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol.OriginalDefinition)))
                {
                    symbols.Add(symbol.OriginalDefinition);
                }
            }

            return symbols;
        }

        private static void AddCompletedIfImplicitElseMergedFacts(
            IfStatementSyntax ifStatement,
            IReadOnlyList<SmtFormula> factsBeforeStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (ifStatement.Else != null ||
                StatementDefinitelyExits(ifStatement.Statement, semanticModel, cancellationToken))
            {
                return;
            }

            var trueBranchFacts = CollectCompletedBranchFacts(
                factsBeforeStatement,
                ifStatement.Condition,
                branchWhenTrue: true,
                ifStatement.Statement,
                semanticModel,
                cancellationToken);
            var falseBranchFacts = new List<SmtFormula>(factsBeforeStatement);
            AddBranchConditionFacts(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, falseBranchFacts);

            AddIdenticalBranchFacts(trueBranchFacts, falseBranchFacts, facts);

            if (AnyConditionSymbolInvalidatedInStatement(ifStatement.Condition, ifStatement.Statement, semanticModel, cancellationToken) ||
                !TryCreateBranchConditionFormula(ifStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, out var trueCondition) ||
                !TryCreateBranchConditionFormula(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, out var falseCondition))
            {
                return;
            }

            AddConditionalMergedBranchFacts(
                trueBranchFacts,
                falseBranchFacts,
                facts.ToArray(),
                trueCondition,
                falseCondition,
                facts);
        }

        private static void AddCompletedIfElseMergedFacts(
            IfStatementSyntax ifStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (ifStatement.Else?.Statement is not { } elseStatement ||
                StatementDefinitelyExits(ifStatement.Statement, semanticModel, cancellationToken) ||
                StatementDefinitelyExits(elseStatement, semanticModel, cancellationToken))
            {
                return;
            }

            var currentFacts = facts.ToArray();
            var trueBranchFacts = CollectCompletedBranchFacts(
                currentFacts,
                ifStatement.Condition,
                branchWhenTrue: true,
                ifStatement.Statement,
                semanticModel,
                cancellationToken);
            var falseBranchFacts = CollectCompletedBranchFacts(
                currentFacts,
                ifStatement.Condition,
                branchWhenTrue: false,
                elseStatement,
                semanticModel,
                cancellationToken);

            AddIdenticalBranchFacts(trueBranchFacts, falseBranchFacts, facts);

            if (AnyConditionSymbolInvalidatedInStatement(ifStatement.Condition, ifStatement.Statement, semanticModel, cancellationToken) ||
                AnyConditionSymbolInvalidatedInStatement(ifStatement.Condition, elseStatement, semanticModel, cancellationToken) ||
                !TryCreateBranchConditionFormula(ifStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, out var trueCondition) ||
                !TryCreateBranchConditionFormula(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, out var falseCondition))
            {
                return;
            }

            AddConditionalMergedBranchFacts(
                trueBranchFacts,
                falseBranchFacts,
                currentFacts,
                trueCondition,
                falseCondition,
                facts);
        }

        private static void AddIdenticalBranchFacts(
            IReadOnlyCollection<SmtFormula> trueBranchFacts,
            IReadOnlyCollection<SmtFormula> falseBranchFacts,
            ICollection<SmtFormula> facts)
        {
            var existingKeys = new HashSet<string>(facts.Select(GetFormulaKey), StringComparer.Ordinal);
            var falseBranchKeys = new HashSet<string>(falseBranchFacts.Select(GetFormulaKey), StringComparer.Ordinal);

            foreach (var fact in trueBranchFacts)
            {
                var key = GetFormulaKey(fact);
                if (existingKeys.Contains(key) || !falseBranchKeys.Contains(key))
                {
                    continue;
                }

                facts.Add(fact);
                existingKeys.Add(key);
            }
        }

        private static void AddConditionalMergedBranchFacts(
            IReadOnlyCollection<SmtFormula> trueBranchFacts,
            IReadOnlyCollection<SmtFormula> falseBranchFacts,
            IEnumerable<SmtFormula> commonFacts,
            SmtFormula trueCondition,
            SmtFormula falseCondition,
            ICollection<SmtFormula> facts)
        {
            var existingKeys = new HashSet<string>(facts.Select(GetFormulaKey), StringComparer.Ordinal);
            var currentKeys = new HashSet<string>(commonFacts.Select(GetFormulaKey), StringComparer.Ordinal);

            var falseFactsByTarget = falseBranchFacts
                .Where(fact => !currentKeys.Contains(GetFormulaKey(fact)))
                .Select(fact => new MergeableBranchFact(fact))
                .Where(static fact => fact.TargetKey.Length > 0)
                .GroupBy(static fact => fact.TargetKey, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
            var mergedFactCount = 0;
            foreach (var trueFact in trueBranchFacts.Where(fact => !currentKeys.Contains(GetFormulaKey(fact))))
            {
                var trueMergeableFact = new MergeableBranchFact(trueFact);
                if (trueMergeableFact.TargetKey.Length == 0 ||
                    !falseFactsByTarget.TryGetValue(trueMergeableFact.TargetKey, out var falseMergeableFacts))
                {
                    continue;
                }

                foreach (var falseMergeableFact in falseMergeableFacts)
                {
                    if (string.Equals(trueMergeableFact.FactKey, falseMergeableFact.FactKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var mergedFact = CreateConditionalBranchFact(
                        trueCondition,
                        trueMergeableFact.Formula,
                        falseCondition,
                        falseMergeableFact.Formula);
                    var mergedKey = GetFormulaKey(mergedFact);
                    if (!existingKeys.Add(mergedKey))
                    {
                        continue;
                    }

                    facts.Add(mergedFact);
                    mergedFactCount++;
                    if (mergedFactCount >= MaxMergedIfElseFacts)
                    {
                        return;
                    }
                }
            }
        }

        private static List<SmtFormula> CollectCompletedBranchFacts(
            IEnumerable<SmtFormula> currentFacts,
            ExpressionSyntax condition,
            bool branchWhenTrue,
            StatementSyntax branchStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var branchFacts = new List<SmtFormula>(currentFacts);
            AddBranchConditionFacts(condition, branchWhenTrue, semanticModel, cancellationToken, branchFacts);
            foreach (var statement in EnumerateBranchStatements(branchStatement))
            {
                AddPriorStatementFacts(statement, semanticModel, cancellationToken, branchFacts);
            }

            return branchFacts;
        }

        private static IEnumerable<StatementSyntax> EnumerateBranchStatements(StatementSyntax branchStatement)
        {
            if (branchStatement is BlockSyntax block)
            {
                foreach (var statement in block.Statements)
                {
                    yield return statement;
                }

                yield break;
            }

            yield return branchStatement;
        }

        private static string GetFormulaKey(SmtFormula formula)
        {
            return formula.ToString() ?? string.Empty;
        }

        private static bool TryCreateBranchConditionFormula(
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            var formulas = new List<SmtFormula>();
            if (!TryCollectBranchAssumptionFacts(
                    condition,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    formulas))
            {
                formula = null!;
                return false;
            }

            formula = CreateConjunction(formulas);
            return true;
        }

        private static SmtFormula CreateConditionalBranchFact(
            SmtFormula trueCondition,
            SmtFormula trueFact,
            SmtFormula falseCondition,
            SmtFormula falseFact)
        {
            return new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtBinaryFormula(SmtBinaryOperator.And, trueCondition, trueFact),
                new SmtBinaryFormula(SmtBinaryOperator.And, falseCondition, falseFact));
        }

        private static SmtFormula CreateConjunction(IReadOnlyList<SmtFormula> formulas)
        {
            var formula = formulas[0];
            for (var index = 1; index < formulas.Count; index++)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, formula, formulas[index]);
            }

            return formula;
        }

        private sealed class MergeableBranchFact
        {
            internal MergeableBranchFact(SmtFormula formula)
            {
                Formula = formula;
                FactKey = GetFormulaKey(formula);
                TargetKey = TryGetMergeTargetKey(formula, out var targetKey)
                    ? targetKey
                    : string.Empty;
            }

            internal SmtFormula Formula { get; }

            internal string FactKey { get; }

            internal string TargetKey { get; }

            private static bool TryGetMergeTargetKey(SmtFormula formula, out string targetKey)
            {
                switch (formula)
                {
                    case SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.Equal,
                        Left: SmtVariable target,
                        Right: { } right
                    } when target.Kind == right.Kind:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.NotEqual,
                        Left: SmtVariable { Kind: SmtValueKind.Reference } target,
                        Right: SmtNullConstant
                    }:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.NotEqual,
                        Left: SmtNullConstant,
                        Right: SmtVariable { Kind: SmtValueKind.Reference } target
                    }:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtUnaryFormula
                    {
                        Operator: SmtUnaryOperator.Not,
                        Operand: SmtBinaryFormula
                        {
                            Operator: SmtBinaryOperator.Equal,
                            Left: SmtVariable { Kind: SmtValueKind.Reference } target,
                            Right: SmtNullConstant
                        }
                    }:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtUnaryFormula
                    {
                        Operator: SmtUnaryOperator.Not,
                        Operand: SmtBinaryFormula
                        {
                            Operator: SmtBinaryOperator.Equal,
                            Left: SmtNullConstant,
                            Right: SmtVariable { Kind: SmtValueKind.Reference } target
                        }
                    }:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtVariable { Kind: SmtValueKind.Bool } target:
                        targetKey = GetFormulaKey(target);
                        return true;
                    case SmtUnaryFormula
                    {
                        Operator: SmtUnaryOperator.Not,
                        Operand: SmtVariable { Kind: SmtValueKind.Bool } target
                    }:
                        targetKey = GetFormulaKey(target);
                        return true;
                    default:
                        targetKey = string.Empty;
                        return false;
                }
            }
        }

        private static void AddCompletedSwitchStatementFacts(
            SwitchStatementSyntax switchStatement,
            IReadOnlyList<SmtFormula> factsBeforeStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            AddCompletedSwitchExitExclusionFacts(switchStatement, semanticModel, cancellationToken, facts);

            if (!SwitchStatementHasDefaultOrExhaustiveBooleanLabels(switchStatement, semanticModel, cancellationToken))
            {
                return;
            }

            var branches = new List<SwitchBranchFacts>();
            var conditionSymbols = GetSwitchConditionSymbols(switchStatement, semanticModel, cancellationToken);
            foreach (var section in switchStatement.Sections)
            {
                if (!SectionBreaksFromSwitch(section, switchStatement))
                {
                    continue;
                }

                if (!SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                        switchStatement.Expression,
                        section,
                        semanticModel,
                        cancellationToken,
                        out var sectionCondition))
                {
                    return;
                }

                var sectionMutatesConditionSymbols = SectionMutatesAnySymbolBeforeSwitchBreak(
                    section,
                    switchStatement,
                    conditionSymbols,
                    semanticModel,
                    cancellationToken);
                var sectionFacts = new List<SmtFormula>(factsBeforeStatement);
                if (!sectionMutatesConditionSymbols)
                {
                    sectionFacts.Add(sectionCondition);
                }

                foreach (var statement in section.Statements)
                {
                    if (statement is BreakStatementSyntax breakStatement &&
                        BreakTargetsSwitch(breakStatement, switchStatement))
                    {
                        break;
                    }

                    AddPriorStatementFacts(statement, semanticModel, cancellationToken, sectionFacts);
                }

                branches.Add(new SwitchBranchFacts(sectionCondition, sectionFacts, sectionMutatesConditionSymbols));
            }

            if (branches.Count == 0)
            {
                return;
            }

            AddIdenticalSwitchBranchFacts(branches, facts);
            if (branches.All(static branch => !branch.ConditionSymbolsMutated))
            {
                AddConditionalSwitchBranchFacts(branches, facts.ToArray(), facts);
            }
        }

        private static bool SwitchStatementHasDefaultOrExhaustiveBooleanLabels(
            SwitchStatementSyntax switchStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (switchStatement.Sections.Any(static section => section.Labels.Any(static label => label is DefaultSwitchLabelSyntax)))
            {
                return true;
            }

            var typeInfo = semanticModel.GetTypeInfo(switchStatement.Expression, cancellationToken);
            var switchType = typeInfo.ConvertedType ?? typeInfo.Type;
            if (switchType?.SpecialType != SpecialType.System_Boolean)
            {
                return false;
            }

            var hasTrue = false;
            var hasFalse = false;
            foreach (var section in switchStatement.Sections)
            {
                foreach (var label in section.Labels)
                {
                    if (label is not CaseSwitchLabelSyntax caseLabel ||
                        semanticModel.GetConstantValue(caseLabel.Value, cancellationToken) is not { HasValue: true, Value: bool value })
                    {
                        continue;
                    }

                    if (value)
                    {
                        hasTrue = true;
                    }
                    else
                    {
                        hasFalse = true;
                    }
                }
            }

            return hasTrue && hasFalse;
        }

        private static void AddCompletedSwitchExitExclusionFacts(
            SwitchStatementSyntax switchStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (SwitchContinuingSectionsMutateConditionSymbols(switchStatement, semanticModel, cancellationToken))
            {
                return;
            }

            foreach (var section in switchStatement.Sections)
            {
                if (!SectionDefinitelyExitsFromSwitch(section, switchStatement, semanticModel, cancellationToken) ||
                    !SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                        switchStatement.Expression,
                        section,
                        semanticModel,
                        cancellationToken,
                        out var sectionCondition,
                        getSymbolVersion: null,
                        includePatternBindings: false))
                {
                    continue;
                }

                facts.Add(new SmtUnaryFormula(SmtUnaryOperator.Not, sectionCondition));
            }
        }

        private static bool SwitchContinuingSectionsMutateConditionSymbols(
            SwitchStatementSyntax switchStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var conditionSymbols = GetSwitchConditionSymbols(switchStatement, semanticModel, cancellationToken);
            if (conditionSymbols.Count == 0)
            {
                return false;
            }

            foreach (var section in switchStatement.Sections)
            {
                if (SectionDefinitelyExitsFromSwitch(section, switchStatement, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (SectionMutatesAnySymbolBeforeSwitchBreak(
                    section,
                    switchStatement,
                    conditionSymbols,
                    semanticModel,
                    cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SectionMutatesAnySymbolBeforeSwitchBreak(
            SwitchSectionSyntax section,
            SwitchStatementSyntax switchStatement,
            IReadOnlyCollection<ISymbol> symbols,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (symbols.Count == 0)
            {
                return false;
            }

            foreach (var statement in section.Statements)
            {
                if (statement is BreakStatementSyntax breakStatement &&
                    BreakTargetsSwitch(breakStatement, switchStatement))
                {
                    break;
                }

                if (symbols.Any(symbol => StatementMutatesSymbol(statement, symbol, semanticModel, cancellationToken)))
                {
                    return true;
                }
            }

            return false;
        }

        private static IReadOnlyList<ISymbol> GetSwitchConditionSymbols(
            SwitchStatementSyntax switchStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbols = new List<ISymbol>();
            AddReferencedSymbols(switchStatement.Expression, semanticModel, cancellationToken, symbols);
            foreach (var section in switchStatement.Sections)
            {
                foreach (var label in section.Labels)
                {
                    switch (label)
                    {
                        case CaseSwitchLabelSyntax caseLabel:
                            AddReferencedSymbols(caseLabel.Value, semanticModel, cancellationToken, symbols);
                            break;
                        case CasePatternSwitchLabelSyntax patternLabel:
                            AddReferencedSymbols(patternLabel.Pattern, semanticModel, cancellationToken, symbols);
                            AddDeclaredPatternSymbols(patternLabel.Pattern, semanticModel, cancellationToken, symbols);
                            if (patternLabel.WhenClause != null)
                            {
                                AddReferencedSymbols(patternLabel.WhenClause.Condition, semanticModel, cancellationToken, symbols);
                            }

                            break;
                    }
                }
            }

            return symbols;
        }

        private static IReadOnlyList<ISymbol> GetSwitchExpressionConditionSymbols(
            SwitchExpressionSyntax switchExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbols = new List<ISymbol>();
            AddReferencedSymbols(switchExpression.GoverningExpression, semanticModel, cancellationToken, symbols);

            foreach (var arm in switchExpression.Arms)
            {
                AddReferencedSymbols(arm.Pattern, semanticModel, cancellationToken, symbols);
                AddDeclaredPatternSymbols(arm.Pattern, semanticModel, cancellationToken, symbols);
                if (arm.WhenClause != null)
                {
                    AddReferencedSymbols(arm.WhenClause.Condition, semanticModel, cancellationToken, symbols);
                }
            }

            return symbols;
        }

        private static void AddReferencedSymbols(
            SyntaxNode root,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<ISymbol> symbols)
        {
            foreach (var symbol in GetReferencedLocalAndParameterSymbols(root, semanticModel, cancellationToken))
            {
                AddSymbolIfAbsent(symbols, symbol);
            }
        }

        private static void AddDeclaredPatternSymbols(
            SyntaxNode root,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<ISymbol> symbols)
        {
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (node is SingleVariableDesignationSyntax singleVariableDesignation &&
                    singleVariableDesignation.Identifier.ValueText != "_" &&
                    semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is ILocalSymbol localSymbol)
                {
                    AddSymbolIfAbsent(symbols, localSymbol.OriginalDefinition);
                }
            }
        }

        private static void AddMemberNotNullWhenTargetSymbols(
            SyntaxNode root,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<ISymbol> symbols)
        {
            foreach (var invocation in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                         .OfType<InvocationExpressionSyntax>())
            {
                if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
                    invocationOperation.TargetMethod.IsStatic ||
                    !IsCurrentInstanceInvocation(invocation))
                {
                    continue;
                }

                foreach (var target in GetMemberNotNullWhenTargets(invocationOperation.TargetMethod))
                {
                    if (TryResolveMemberNotNullTarget(invocationOperation.TargetMethod.ContainingType, target, out var member))
                    {
                        AddSymbolIfAbsent(symbols, member);
                    }
                }
            }
        }

        private static IEnumerable<string> GetMemberNotNullWhenTargets(IMethodSymbol method)
        {
            var targets = new List<string>();
            AddMemberNotNullWhenTargets(method, targets);
            if (!SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition))
            {
                AddMemberNotNullWhenTargets(method.OriginalDefinition, targets);
            }

            return targets.Distinct(StringComparer.Ordinal);
        }

        private static void AddMemberNotNullWhenTargets(IMethodSymbol method, ICollection<string> targets)
        {
            foreach (var attribute in method.GetAttributes())
            {
                if (!string.Equals(
                        GetFullMetadataName(attribute.AttributeClass),
                        MemberNotNullWhenAttributeName,
                        StringComparison.Ordinal) ||
                    attribute.ConstructorArguments.Length < 2)
                {
                    continue;
                }

                for (var index = 1; index < attribute.ConstructorArguments.Length; index++)
                {
                    AddMemberNotNullTarget(attribute.ConstructorArguments[index], targets);
                }
            }
        }

        private static void AddSymbolIfAbsent(ICollection<ISymbol> symbols, ISymbol symbol)
        {
            if (symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
            {
                symbols.Add(symbol);
            }
        }

        private static void AddIdenticalSwitchBranchFacts(
            IReadOnlyList<SwitchBranchFacts> branches,
            ICollection<SmtFormula> facts)
        {
            var existingKeys = new HashSet<string>(facts.Select(GetFormulaKey), StringComparer.Ordinal);
            var commonKeys = new HashSet<string>(branches[0].Facts.Select(GetFormulaKey), StringComparer.Ordinal);
            for (var index = 1; index < branches.Count; index++)
            {
                commonKeys.IntersectWith(branches[index].Facts.Select(GetFormulaKey));
            }

            foreach (var fact in branches[0].Facts)
            {
                var key = GetFormulaKey(fact);
                if (!commonKeys.Contains(key) || !existingKeys.Add(key))
                {
                    continue;
                }

                facts.Add(fact);
            }
        }

        private static void AddConditionalSwitchBranchFacts(
            IReadOnlyList<SwitchBranchFacts> branches,
            IEnumerable<SmtFormula> commonFacts,
            ICollection<SmtFormula> facts)
        {
            var existingKeys = new HashSet<string>(facts.Select(GetFormulaKey), StringComparer.Ordinal);
            var commonKeys = new HashSet<string>(commonFacts.Select(GetFormulaKey), StringComparer.Ordinal);
            var branchFactsByTarget = branches
                .Select(branch => branch.Facts
                    .Where(fact => !commonKeys.Contains(GetFormulaKey(fact)))
                    .Select(fact => new MergeableBranchFact(fact))
                    .Where(static fact => fact.TargetKey.Length > 0)
                    .GroupBy(static fact => fact.TargetKey, StringComparer.Ordinal)
                    .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal))
                .ToArray();

            if (branchFactsByTarget.Length == 0)
            {
                return;
            }

            var candidateTargets = new HashSet<string>(branchFactsByTarget[0].Keys, StringComparer.Ordinal);
            for (var index = 1; index < branchFactsByTarget.Length; index++)
            {
                candidateTargets.IntersectWith(branchFactsByTarget[index].Keys);
            }

            var mergedFactCount = 0;
            foreach (var target in candidateTargets)
            {
                var factChoices = branchFactsByTarget
                    .Select(branchFacts => branchFacts[target][0])
                    .ToArray();
                if (factChoices.Select(static fact => fact.FactKey).Distinct(StringComparer.Ordinal).Count() == 1)
                {
                    continue;
                }

                var mergedFact = CreateSwitchConditionalFact(branches, factChoices);
                var mergedKey = GetFormulaKey(mergedFact);
                if (!existingKeys.Add(mergedKey))
                {
                    continue;
                }

                facts.Add(mergedFact);
                mergedFactCount++;
                if (mergedFactCount >= MaxMergedSwitchFacts)
                {
                    return;
                }
            }
        }

        private static SmtFormula CreateSwitchConditionalFact(
            IReadOnlyList<SwitchBranchFacts> branches,
            IReadOnlyList<MergeableBranchFact> branchFacts)
        {
            var branchTerms = new SmtFormula[branches.Count];
            for (var index = 0; index < branches.Count; index++)
            {
                branchTerms[index] = new SmtBinaryFormula(
                    SmtBinaryOperator.And,
                    branches[index].Condition,
                    branchFacts[index].Formula);
            }

            return CreateDisjunction(branchTerms);
        }

        private static SmtFormula CreateDisjunction(IReadOnlyList<SmtFormula> formulas)
        {
            var formula = formulas[0];
            for (var index = 1; index < formulas.Count; index++)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Or, formula, formulas[index]);
            }

            return formula;
        }

        private static bool SectionBreaksFromSwitch(
            SwitchSectionSyntax section,
            SwitchStatementSyntax switchStatement)
        {
            return section.Statements.Count > 0 &&
                section.Statements[section.Statements.Count - 1] is BreakStatementSyntax breakStatement &&
                BreakTargetsSwitch(breakStatement, switchStatement);
        }

        private static bool SectionDefinitelyExitsFromSwitch(
            SwitchSectionSyntax section,
            SwitchStatementSyntax switchStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return section.Statements.Count > 0 &&
                StatementDefinitelyExitsFromSwitch(section.Statements[section.Statements.Count - 1], switchStatement, semanticModel, cancellationToken);
        }

        private static bool StatementDefinitelyExitsFromSwitch(
            StatementSyntax statement,
            SwitchStatementSyntax switchStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            statement = UnwrapSingleStatementBlock(statement);
            return statement switch
            {
                ReturnStatementSyntax => true,
                ThrowStatementSyntax => true,
                BreakStatementSyntax breakStatement => !BreakTargetsSwitch(breakStatement, switchStatement),
                ContinueStatementSyntax => true,
                ExpressionStatementSyntax expressionStatement => ExpressionStatementDefinitelyExits(expressionStatement, semanticModel, cancellationToken),
                BlockSyntax block when block.Statements.Count > 0 => StatementDefinitelyExitsFromSwitch(block.Statements[block.Statements.Count - 1], switchStatement, semanticModel, cancellationToken),
                IfStatementSyntax ifStatement when ifStatement.Else != null =>
                    StatementDefinitelyExitsFromSwitch(ifStatement.Statement, switchStatement, semanticModel, cancellationToken) &&
                    StatementDefinitelyExitsFromSwitch(ifStatement.Else.Statement, switchStatement, semanticModel, cancellationToken),
                _ => false
            };
        }

        private static bool BreakTargetsSwitch(
            BreakStatementSyntax breakStatement,
            SwitchStatementSyntax switchStatement)
        {
            for (var ancestor = breakStatement.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, switchStatement))
                {
                    return true;
                }

                if (ancestor is SwitchStatementSyntax ||
                    IsLoopStatement(ancestor))
                {
                    return false;
                }
            }

            return false;
        }

        private sealed class SwitchBranchFacts
        {
            internal SwitchBranchFacts(SmtFormula condition, IReadOnlyList<SmtFormula> facts, bool conditionSymbolsMutated)
            {
                Condition = condition;
                Facts = facts;
                ConditionSymbolsMutated = conditionSymbolsMutated;
            }

            internal SmtFormula Condition { get; }

            internal IReadOnlyList<SmtFormula> Facts { get; }

            internal bool ConditionSymbolsMutated { get; }
        }

        private sealed class CompletedBranchFacts
        {
            internal CompletedBranchFacts(StatementSyntax statement, IReadOnlyList<SmtFormula> facts)
            {
                Statement = statement;
                Facts = facts;
            }

            internal StatementSyntax Statement { get; }

            internal IReadOnlyList<SmtFormula> Facts { get; }
        }

        private static void AddCompletedLoopStatementFacts(
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            switch (statement)
            {
                case WhileStatementSyntax whileStatement
                    when CanAssumeLoopConditionFalseAfterNormalExit(whileStatement, whileStatement.Statement):
                    AddBranchConditionFacts(
                        whileStatement.Condition,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        facts);
                    AddLoopBodyInvariantFacts(facts, whileStatement, semanticModel, cancellationToken);
                    break;
                case WhileStatementSyntax whileStatement
                    when TryCreateGuardedBreakLoopExitConditionFact(
                        whileStatement,
                        whileStatement.Statement,
                        whileStatement.Condition,
                        semanticModel,
                        cancellationToken,
                        out var exitCondition):
                    facts.Add(exitCondition);
                    AddLoopBodyInvariantFacts(facts, whileStatement, semanticModel, cancellationToken);
                    break;
                case ForStatementSyntax { Condition: { } condition } forStatement
                    when CanAssumeLoopConditionFalseAfterNormalExit(forStatement, forStatement.Statement):
                    AddBranchConditionFacts(
                        condition,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        facts);
                    AddLoopBodyInvariantFacts(facts, forStatement, semanticModel, cancellationToken);
                    break;
                case ForStatementSyntax { Condition: { } condition } forStatement
                    when TryCreateGuardedBreakLoopExitConditionFact(
                        forStatement,
                        forStatement.Statement,
                        condition,
                        semanticModel,
                        cancellationToken,
                        out var exitCondition):
                    facts.Add(exitCondition);
                    AddLoopBodyInvariantFacts(facts, forStatement, semanticModel, cancellationToken);
                    break;
                case ForStatementSyntax { Condition: null } forStatement
                    when TryCreateGuardedBreakLoopExitConditionFact(
                        forStatement,
                        forStatement.Statement,
                        condition: null,
                        semanticModel,
                        cancellationToken,
                        out var exitCondition):
                    facts.Add(exitCondition);
                    AddLoopBodyInvariantFacts(facts, forStatement, semanticModel, cancellationToken);
                    break;
                case DoStatementSyntax doStatement
                    when CanAssumeLoopConditionFalseAfterNormalExit(doStatement, doStatement.Statement):
                    AddBranchConditionFacts(
                        doStatement.Condition,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        facts);
                    AddLoopBodyInvariantFacts(facts, doStatement, semanticModel, cancellationToken);
                    break;
                case DoStatementSyntax doStatement
                    when TryCreateGuardedBreakLoopExitConditionFact(
                        doStatement,
                        doStatement.Statement,
                        doStatement.Condition,
                        semanticModel,
                        cancellationToken,
                        out var exitCondition):
                    facts.Add(exitCondition);
                    AddLoopBodyInvariantFacts(facts, doStatement, semanticModel, cancellationToken);
                    break;
                case ForEachStatementSyntax forEachStatement:
                    AddCompletedForeachStatementFacts(
                        forEachStatement.Expression,
                        forEachStatement.Statement,
                        semanticModel,
                        cancellationToken,
                        facts);
                    break;
                case ForEachVariableStatementSyntax forEachVariableStatement:
                    AddCompletedForeachStatementFacts(
                        forEachVariableStatement.Expression,
                        forEachVariableStatement.Statement,
                        semanticModel,
                        cancellationToken,
                        facts);
                    break;
                case LockStatementSyntax lockStatement:
                    AddCompletedLockStatementFacts(
                        lockStatement,
                        semanticModel,
                        cancellationToken,
                        facts);
                    break;
            }
        }

        private static void AddLoopBodyInvariantFacts(
            ICollection<SmtFormula> facts,
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var loopFact in CollectLoopBodyInvariantFacts(loopStatement, semanticModel, cancellationToken))
            {
                facts.Add(loopFact);
            }
        }

        private static bool CanAssumeLoopConditionFalseAfterNormalExit(
            StatementSyntax loopStatement,
            StatementSyntax loopBody)
        {
            foreach (var node in loopBody.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                switch (node)
                {
                    case GotoStatementSyntax:
                        return false;
                    case BreakStatementSyntax breakStatement when BreakTargetsLoop(breakStatement, loopStatement):
                        return false;
                }
            }

            return true;
        }

        private static bool TryCreateGuardedBreakLoopExitConditionFact(
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            ExpressionSyntax? condition,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula exitCondition)
        {
            exitCondition = null!;
            if (LoopBodyContainsGoto(loopBody) ||
                !TryCreateTopLevelGuardedBreakCondition(
                    loopStatement,
                    loopBody,
                    semanticModel,
                    cancellationToken,
                    out var breakCondition))
            {
                return false;
            }

            if (condition == null)
            {
                exitCondition = breakCondition;
                return true;
            }

            if (!TryCreateBranchConditionFormula(
                    condition,
                    branchWhenTrue: false,
                    semanticModel,
                    cancellationToken,
                    out var normalExitCondition))
            {
                return false;
            }

            exitCondition = new SmtBinaryFormula(SmtBinaryOperator.Or, normalExitCondition, breakCondition);
            return true;
        }

        private static bool TryCreateTopLevelGuardedBreakCondition(
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula breakCondition)
        {
            breakCondition = null!;
            var loopBreaks = loopBody
                .DescendantNodesAndSelf(descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                .OfType<BreakStatementSyntax>()
                .Where(breakStatement => BreakTargetsLoop(breakStatement, loopStatement))
                .ToArray();
            if (loopBreaks.Length == 0)
            {
                return false;
            }

            if (loopBreaks.Length == 1)
            {
                var breakStatement = loopBreaks[0];
                return TryCreateDirectGuardedBreakCondition(
                    breakStatement,
                    loopStatement,
                    loopBody,
                    semanticModel,
                    cancellationToken,
                    out breakCondition) ||
                    TryCreateNestedGuardedBreakCondition(
                        breakStatement,
                        loopStatement,
                        loopBody,
                        semanticModel,
                        cancellationToken,
                        out breakCondition) ||
                    TryCreateGuardedContinueBeforeBreakCondition(
                        loopStatement,
                        loopBody,
                        breakStatement,
                        semanticModel,
                        cancellationToken,
                        out breakCondition);
            }

            var breakConditions = new List<SmtFormula>(loopBreaks.Length);
            foreach (var breakStatement in loopBreaks)
            {
                if (!TryCreateDirectGuardedBreakCondition(
                        breakStatement,
                        loopStatement,
                        loopBody,
                        semanticModel,
                        cancellationToken,
                        out var directBreakCondition))
                {
                    breakCondition = null!;
                    return false;
                }

                breakConditions.Add(directBreakCondition);
            }

            breakCondition = CreateDisjunction(breakConditions);
            return true;
        }

        private static bool TryCreateDirectGuardedBreakCondition(
            BreakStatementSyntax breakStatement,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula breakCondition)
        {
            breakCondition = null!;
            var ifStatement = breakStatement.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault();
            if (ifStatement == null ||
                !IsTopLevelLoopBodyStatement(ifStatement, loopBody) ||
                !TryGetDirectBreakBranch(ifStatement, breakStatement, out var branchWhenTrue) ||
                AnyConditionSymbolInvalidatedBeforeStatement(ifStatement.Condition, loopBody, ifStatement.SpanStart, semanticModel, cancellationToken) ||
                !TryCreateBranchConditionFormula(
                    ifStatement.Condition,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    out breakCondition))
            {
                breakCondition = null!;
                return false;
            }

            if (TryCreateGuardedContinueFallThroughBeforeStatement(
                loopStatement,
                loopBody,
                ifStatement,
                semanticModel,
                cancellationToken,
                out var fallThroughCondition))
            {
                breakCondition = new SmtBinaryFormula(SmtBinaryOperator.And, fallThroughCondition, breakCondition);
            }

            return true;
        }

        private static bool TryCreateNestedGuardedBreakCondition(
            BreakStatementSyntax breakStatement,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula breakCondition)
        {
            breakCondition = null!;

            var guards = new List<(IfStatementSyntax IfStatement, bool BranchWhenTrue)>();
            StatementSyntax currentStatement = breakStatement;
            while (TryGetOnlyParentIfBranch(currentStatement, out var ifStatement, out var branchWhenTrue))
            {
                guards.Add((ifStatement, branchWhenTrue));
                currentStatement = ifStatement;
            }

            if (guards.Count <= 1 ||
                !IsTopLevelLoopBodyStatement(currentStatement, loopBody))
            {
                return false;
            }

            SmtFormula? combinedCondition = null;
            for (var index = guards.Count - 1; index >= 0; index--)
            {
                var guard = guards[index];
                if (AnyConditionSymbolInvalidatedBeforeStatement(
                        guard.IfStatement.Condition,
                        loopBody,
                        guard.IfStatement.SpanStart,
                        semanticModel,
                        cancellationToken) ||
                    !TryCreateBranchConditionFormula(
                        guard.IfStatement.Condition,
                        guard.BranchWhenTrue,
                        semanticModel,
                        cancellationToken,
                        out var guardCondition))
                {
                    breakCondition = null!;
                    return false;
                }

                combinedCondition = combinedCondition == null
                    ? guardCondition
                    : new SmtBinaryFormula(SmtBinaryOperator.And, combinedCondition, guardCondition);
            }

            if (combinedCondition == null)
            {
                return false;
            }

            if (TryCreateGuardedContinueFallThroughBeforeStatement(
                loopStatement,
                loopBody,
                currentStatement,
                semanticModel,
                cancellationToken,
                out var fallThroughCondition))
            {
                combinedCondition = new SmtBinaryFormula(SmtBinaryOperator.And, fallThroughCondition, combinedCondition);
            }

            breakCondition = combinedCondition;
            return true;
        }

        private static bool TryCreateGuardedContinueBeforeBreakCondition(
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            BreakStatementSyntax breakStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula breakCondition)
        {
            breakCondition = null!;
            if (loopBody is not BlockSyntax block)
            {
                return false;
            }

            var breakIndex = -1;
            for (var index = 0; index < block.Statements.Count; index++)
            {
                if (StatementDirectlyContainsOnlyBreak(block.Statements[index], breakStatement))
                {
                    breakIndex = index;
                    break;
                }
            }

            if (breakIndex <= 0)
            {
                return false;
            }

            return TryCreateGuardedContinueFallThroughBeforeStatement(
                loopStatement,
                loopBody,
                block.Statements[breakIndex],
                semanticModel,
                cancellationToken,
                out breakCondition);
        }

        private static bool TryCreateGuardedContinueFallThroughBeforeStatement(
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            StatementSyntax targetStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula breakCondition)
        {
            breakCondition = null!;
            if (loopBody is not BlockSyntax block)
            {
                return false;
            }

            var targetIndex = -1;
            for (var index = 0; index < block.Statements.Count; index++)
            {
                if (ReferenceEquals(block.Statements[index], targetStatement))
                {
                    targetIndex = index;
                    break;
                }
            }

            if (targetIndex <= 0)
            {
                return false;
            }

            SmtFormula? combinedCondition = null;
            for (var index = targetIndex - 1; index >= 0; index--)
            {
                if (block.Statements[index] is not IfStatementSyntax ifStatement ||
                    !TryCreateGuardedContinueFallThroughCondition(
                        ifStatement,
                        loopStatement,
                        loopBody,
                        targetStatement,
                        semanticModel,
                        cancellationToken,
                        out var guardFallThroughCondition))
                {
                    break;
                }

                combinedCondition = combinedCondition == null
                    ? guardFallThroughCondition
                    : new SmtBinaryFormula(SmtBinaryOperator.And, guardFallThroughCondition, combinedCondition);
            }

            if (combinedCondition == null)
            {
                return false;
            }

            breakCondition = combinedCondition;
            return true;
        }

        private static bool TryCreateGuardedContinueFallThroughCondition(
            IfStatementSyntax ifStatement,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            StatementSyntax targetStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fallThroughCondition)
        {
            fallThroughCondition = null!;
            if (TryGetDirectContinueBranch(ifStatement, loopStatement, out var continueBranchWhenTrue))
            {
                if (AnyConditionSymbolInvalidatedBeforeStatement(
                        ifStatement.Condition,
                        loopBody,
                        targetStatement.SpanStart,
                        semanticModel,
                        cancellationToken) ||
                    !TryCreateBranchConditionFormula(
                        ifStatement.Condition,
                        !continueBranchWhenTrue,
                        semanticModel,
                        cancellationToken,
                        out fallThroughCondition))
                {
                    fallThroughCondition = null!;
                    return false;
                }

                return true;
            }

            if (!TryCreateNestedGuardedContinueCondition(
                    ifStatement,
                    loopStatement,
                    loopBody,
                    targetStatement,
                    semanticModel,
                    cancellationToken,
                    out var continueCondition))
            {
                return false;
            }

            fallThroughCondition = new SmtUnaryFormula(SmtUnaryOperator.Not, continueCondition);
            return true;
        }

        private static bool TryCreateNestedGuardedContinueCondition(
            IfStatementSyntax ifStatement,
            StatementSyntax loopStatement,
            StatementSyntax loopBody,
            StatementSyntax targetStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula continueCondition)
        {
            continueCondition = null!;
            var continueStatements = ifStatement
                .DescendantNodes(descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                .OfType<ContinueStatementSyntax>()
                .Where(continueStatement => ContinueTargetsLoop(continueStatement, loopStatement))
                .ToArray();
            if (continueStatements.Length != 1)
            {
                return false;
            }

            var guards = new List<(IfStatementSyntax IfStatement, bool BranchWhenTrue)>();
            StatementSyntax currentStatement = continueStatements[0];
            while (TryGetOnlyParentIfBranch(currentStatement, out var parentIf, out var branchWhenTrue))
            {
                guards.Add((parentIf, branchWhenTrue));
                currentStatement = parentIf;
            }

            if (guards.Count <= 1 ||
                !ReferenceEquals(currentStatement, ifStatement))
            {
                return false;
            }

            SmtFormula? combinedCondition = null;
            for (var index = guards.Count - 1; index >= 0; index--)
            {
                var guard = guards[index];
                if (AnyConditionSymbolInvalidatedBeforeStatement(
                        guard.IfStatement.Condition,
                        loopBody,
                        targetStatement.SpanStart,
                        semanticModel,
                        cancellationToken) ||
                    !TryCreateBranchConditionFormula(
                        guard.IfStatement.Condition,
                        guard.BranchWhenTrue,
                        semanticModel,
                        cancellationToken,
                        out var guardCondition))
                {
                    continueCondition = null!;
                    return false;
                }

                combinedCondition = combinedCondition == null
                    ? guardCondition
                    : new SmtBinaryFormula(SmtBinaryOperator.And, combinedCondition, guardCondition);
            }

            if (combinedCondition == null)
            {
                return false;
            }

            continueCondition = combinedCondition;
            return true;
        }

        private static bool IsTopLevelLoopBodyStatement(
            StatementSyntax statement,
            StatementSyntax loopBody)
        {
            return ReferenceEquals(statement, loopBody) ||
                loopBody is BlockSyntax block &&
                ReferenceEquals(statement.Parent, block);
        }

        private static bool TryGetOnlyParentIfBranch(
            StatementSyntax statement,
            out IfStatementSyntax ifStatement,
            out bool branchWhenTrue)
        {
            ifStatement = null!;
            branchWhenTrue = false;

            StatementSyntax branchStatement = statement;
            if (branchStatement.Parent is BlockSyntax block)
            {
                if (block.Statements.Count != 1 ||
                    !ReferenceEquals(block.Statements[0], branchStatement))
                {
                    return false;
                }

                branchStatement = block;
            }

            if (branchStatement.Parent is not IfStatementSyntax parentIf)
            {
                return false;
            }

            if (ReferenceEquals(parentIf.Statement, branchStatement))
            {
                ifStatement = parentIf;
                branchWhenTrue = true;
                return true;
            }

            if (parentIf.Else?.Statement is { } elseStatement &&
                ReferenceEquals(elseStatement, branchStatement))
            {
                ifStatement = parentIf;
                branchWhenTrue = false;
                return true;
            }

            return false;
        }

        private static bool TryGetDirectBreakBranch(
            IfStatementSyntax ifStatement,
            BreakStatementSyntax breakStatement,
            out bool branchWhenTrue)
        {
            if (StatementDirectlyContainsOnlyBreak(ifStatement.Statement, breakStatement))
            {
                branchWhenTrue = true;
                return true;
            }

            if (ifStatement.Else?.Statement is { } elseStatement &&
                StatementDirectlyContainsOnlyBreak(elseStatement, breakStatement))
            {
                branchWhenTrue = false;
                return true;
            }

            branchWhenTrue = false;
            return false;
        }

        private static bool TryGetDirectContinueBranch(
            IfStatementSyntax ifStatement,
            StatementSyntax loopStatement,
            out bool branchWhenTrue)
        {
            if (StatementDirectlyContainsOnlyContinue(ifStatement.Statement, loopStatement))
            {
                branchWhenTrue = true;
                return true;
            }

            if (ifStatement.Else?.Statement is { } elseStatement &&
                StatementDirectlyContainsOnlyContinue(elseStatement, loopStatement))
            {
                branchWhenTrue = false;
                return true;
            }

            branchWhenTrue = false;
            return false;
        }

        private static bool StatementDirectlyContainsOnlyBreak(
            StatementSyntax statement,
            BreakStatementSyntax breakStatement)
        {
            statement = UnwrapSingleStatementBlock(statement);
            return ReferenceEquals(statement, breakStatement);
        }

        private static bool StatementDirectlyContainsOnlyContinue(
            StatementSyntax statement,
            StatementSyntax loopStatement)
        {
            statement = UnwrapSingleStatementBlock(statement);
            return statement is ContinueStatementSyntax continueStatement &&
                ContinueTargetsLoop(continueStatement, loopStatement);
        }

        private static bool AnyConditionSymbolInvalidatedBeforeStatement(
            ExpressionSyntax condition,
            StatementSyntax root,
            int beforeSpanStart,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var conditionSymbols = GetConditionDependencySymbols(condition, semanticModel, cancellationToken);
            if (conditionSymbols.Count == 0)
            {
                return false;
            }

            foreach (var statement in EnumerateStatementsBefore(root, beforeSpanStart))
            {
                if (conditionSymbols.Any(symbol => StatementInvalidatesSymbolValue(statement, symbol, semanticModel, cancellationToken)))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<StatementSyntax> EnumerateStatementsBefore(
            StatementSyntax root,
            int beforeSpanStart)
        {
            if (root is BlockSyntax block)
            {
                foreach (var statement in block.Statements)
                {
                    if (statement.SpanStart >= beforeSpanStart)
                    {
                        yield break;
                    }

                    yield return statement;
                }

                yield break;
            }

            if (root.SpanStart < beforeSpanStart)
            {
                yield return root;
            }
        }

        private static bool LoopBodyContainsGoto(StatementSyntax loopBody)
        {
            return loopBody
                .DescendantNodesAndSelf(descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                .Any(static node => node is GotoStatementSyntax);
        }

        private static bool BreakTargetsLoop(
            BreakStatementSyntax breakStatement,
            StatementSyntax loopStatement)
        {
            for (var ancestor = breakStatement.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, loopStatement))
                {
                    return true;
                }

                if (ancestor is SwitchStatementSyntax ||
                    IsLoopStatement(ancestor))
                {
                    return false;
                }
            }

            return false;
        }

        private static bool ContinueTargetsLoop(
            ContinueStatementSyntax continueStatement,
            StatementSyntax loopStatement)
        {
            for (var ancestor = continueStatement.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, loopStatement))
                {
                    return true;
                }

                if (IsLoopStatement(ancestor))
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsLoopStatement(SyntaxNode node)
        {
            return node is WhileStatementSyntax or
                ForStatementSyntax or
                ForEachStatementSyntax or
                DoStatementSyntax;
        }

        private static void AddReachabilityCondition(
            ImmutableArray<SmtFormula>.Builder builder,
            ExpressionSyntax expressionSyntax,
            bool mustBeTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddBranchConditionFacts(expressionSyntax, mustBeTrue, semanticModel, cancellationToken, builder);
        }

        private static void AddBranchConditionFacts(
            ExpressionSyntax expressionSyntax,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            TryCollectBranchAssumptionFacts(
                expressionSyntax,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                facts);
            AddNegatedPatternBranchBindingFacts(expressionSyntax, branchWhenTrue, semanticModel, cancellationToken, facts);
        }

        private static void AddNegatedPatternBranchBindingFacts(
            ExpressionSyntax expressionSyntax,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (!TryGetPatternMatchedByNegatedBranch(
                    expressionSyntax,
                    branchWhenTrue,
                    out var isPatternExpression,
                    out var matchedPattern) ||
                !TryCreateValueFormula(
                    isPatternExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    out var matchedValue))
            {
                return;
            }

            var matchedType = semanticModel.GetTypeInfo(isPatternExpression.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(isPatternExpression.Expression, cancellationToken).Type;
            SymbolicReachabilityService.TryCollectPatternBindingFacts(
                matchedValue,
                matchedType,
                matchedPattern,
                semanticModel,
                cancellationToken,
                facts);
        }

        private static bool TryGetPatternMatchedByNegatedBranch(
            ExpressionSyntax expressionSyntax,
            bool branchWhenTrue,
            out IsPatternExpressionSyntax isPatternExpression,
            out PatternSyntax matchedPattern)
        {
            expressionSyntax = UnwrapExpression(expressionSyntax);
            if (expressionSyntax is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                return TryGetPatternMatchedByNegatedBranch(
                    prefixUnary.Operand,
                    !branchWhenTrue,
                    out isPatternExpression,
                    out matchedPattern);
            }

            if (expressionSyntax is IsPatternExpressionSyntax candidate &&
                !branchWhenTrue &&
                TryGetNegatedPattern(candidate.Pattern, out matchedPattern))
            {
                isPatternExpression = candidate;
                return true;
            }

            isPatternExpression = null!;
            matchedPattern = null!;
            return false;
        }

        private static bool TryGetNegatedPattern(PatternSyntax pattern, out PatternSyntax negatedPattern)
        {
            pattern = UnwrapPattern(pattern);
            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword))
            {
                negatedPattern = UnwrapPattern(unaryPattern.Pattern);
                return true;
            }

            negatedPattern = null!;
            return false;
        }

        private static PatternSyntax UnwrapPattern(PatternSyntax pattern)
        {
            while (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                pattern = parenthesizedPattern.Pattern;
            }

            return pattern;
        }

        private static void AddCatchBodyEntryFacts(
            ICollection<SmtFormula> facts,
            CatchClauseSyntax catchClause,
            int useSpanStart,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (catchClause.Declaration != null &&
                semanticModel.GetDeclaredSymbol(catchClause.Declaration, cancellationToken) is ILocalSymbol localSymbol &&
                !IsSymbolAssignedBetween(
                    catchClause.Block,
                    catchClause.Block.SpanStart - 1,
                    useSpanStart,
                    localSymbol.OriginalDefinition,
                    semanticModel,
                    cancellationToken))
            {
                AddSymbolNonNullCondition(facts, localSymbol.OriginalDefinition);
            }

            if (catchClause.Filter?.FilterExpression is { } filterExpression &&
                !AnyReferencedSymbolAssignedBeforeUse(
                    filterExpression,
                    catchClause.Block,
                    useSpanStart,
                    semanticModel,
                    cancellationToken))
            {
                AddBranchConditionFacts(filterExpression, branchWhenTrue: true, semanticModel, cancellationToken, facts);
            }
        }

        private static void AddSymbolNonNullCondition(
            ICollection<SmtFormula> facts,
            ISymbol symbol)
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

        private static void AddUsingStatementDeclarationFacts(
            ICollection<SmtFormula> facts,
            UsingStatementSyntax usingStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (usingStatement.Declaration == null)
            {
                return;
            }

            var declarationFacts = new List<SmtFormula>();
            foreach (var declarator in usingStatement.Declaration.Variables)
            {
                if (declarator.Initializer == null ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol)
                {
                    continue;
                }

                AddAssignedValueFacts(localSymbol, declarator.Initializer.Value, semanticModel, cancellationToken, declarationFacts);
            }

            foreach (var fact in declarationFacts)
            {
                facts.Add(fact);
            }
        }

        private static void AddUsingStatementExpressionFacts(
            ICollection<SmtFormula> facts,
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (!TryGetThrowGuardedValue(
                    expression,
                    out var effectiveValueExpression,
                    out var guardExpression,
                    out var guardBranchWhenTrue,
                    out var requiresNonNullValue))
            {
                return;
            }

            if (guardExpression != null)
            {
                AddBranchConditionFacts(
                    guardExpression,
                    guardBranchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    facts);
            }
            else if (requiresNonNullValue)
            {
                AddReferenceNonNullFact(effectiveValueExpression, semanticModel, cancellationToken, facts);
            }
        }

        private static void AddCompletedForeachStatementFacts(
            ExpressionSyntax expressionSyntax,
            StatementSyntax foreachBody,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (AnyConditionSymbolInvalidatedInStatement(expressionSyntax, foreachBody, semanticModel, cancellationToken))
            {
                return;
            }

            AddReferenceNullCondition(facts, expressionSyntax, isNull: false, semanticModel, cancellationToken);
        }

        private static void AddCompletedLockStatementFacts(
            LockStatementSyntax lockStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (!IsLocalOrParameterReference(lockStatement.Expression, semanticModel, cancellationToken) ||
                AnyConditionSymbolInvalidatedInStatement(
                    lockStatement.Expression,
                    lockStatement.Statement,
                    semanticModel,
                    cancellationToken))
            {
                return;
            }

            AddReferenceNullCondition(facts, lockStatement.Expression, isNull: false, semanticModel, cancellationToken);
        }

        private static void AddForeachBodyEntryFacts(
            ICollection<SmtFormula> facts,
            ExpressionSyntax expressionSyntax,
            ILocalSymbol? iterationSymbol,
            StatementSyntax foreachStatement,
            StatementSyntax foreachBody,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddThrowGuardedExpressionFacts(expressionSyntax, foreachBody, semanticModel, cancellationToken, facts);
            AddReferenceNullCondition(facts, expressionSyntax, isNull: false, semanticModel, cancellationToken);
            AddFiniteForeachIterationFact(facts, expressionSyntax, iterationSymbol, foreachStatement, semanticModel, cancellationToken);

            var typeInfo = semanticModel.GetTypeInfo(expressionSyntax, cancellationToken);
            if (!IsSupportedForeachLengthReceiver(expressionSyntax) &&
                !IsSupportedForeachLengthReceiver(typeInfo.Type) &&
                !IsSupportedForeachLengthReceiver(typeInfo.ConvertedType))
            {
                return;
            }

            if (!TryCreateBuiltInLengthValueFormula(
                    expressionSyntax,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula) ||
                lengthFormula is not { Kind: SmtValueKind.Int })
            {
                return;
            }

            facts.Add(new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThan,
                lengthFormula,
                new SmtIntegerConstant(0)));
        }

        private static void AddFiniteForeachIterationFact(
            ICollection<SmtFormula> facts,
            ExpressionSyntax expressionSyntax,
            ILocalSymbol? iterationSymbol,
            StatementSyntax foreachStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (iterationSymbol == null)
            {
                return;
            }

            if (TryGetFiniteElementExpressions(expressionSyntax, out var elementExpressions))
            {
                AddFiniteForeachIterationExpressionFacts(
                    facts,
                    iterationSymbol,
                    elementExpressions,
                    semanticModel,
                    cancellationToken);
                return;
            }

            if (TryGetPriorAssignedFiniteArrayElementValueFormulas(
                    expressionSyntax,
                    foreachStatement,
                    semanticModel,
                    cancellationToken,
                    out var elementValueFormulas))
            {
                AddFiniteForeachIterationValueFormulaFacts(facts, iterationSymbol, elementValueFormulas);
                return;
            }

            if (TryGetPriorAssignedFiniteElementExpressions(
                    expressionSyntax,
                    foreachStatement,
                    semanticModel,
                    cancellationToken,
                    out elementExpressions))
            {
                AddFiniteForeachIterationExpressionFacts(
                    facts,
                    iterationSymbol,
                    elementExpressions,
                    semanticModel,
                    cancellationToken);
            }
        }

        private static void AddFiniteForeachIterationExpressionFacts(
            ICollection<SmtFormula> facts,
            ILocalSymbol iterationSymbol,
            ImmutableArray<ExpressionSyntax> elementExpressions,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            SmtFormula? finiteDomainFact = null;
            var allReferenceElementsDefinitelyNonNull = SymbolicFactFactory.GetTrackedSymbolType(iterationSymbol.OriginalDefinition)?.IsReferenceType == true;
            foreach (var elementExpression in elementExpressions)
            {
                if (ExpressionReferencesSymbol(elementExpression, iterationSymbol.OriginalDefinition, semanticModel, cancellationToken))
                {
                    return;
                }

                if (TryCreateAssignedValueFact(
                        iterationSymbol.OriginalDefinition,
                        elementExpression,
                        semanticModel,
                        cancellationToken,
                        out var elementValueFact))
                {
                    finiteDomainFact = finiteDomainFact == null
                        ? elementValueFact
                        : new SmtBinaryFormula(SmtBinaryOperator.Or, finiteDomainFact, elementValueFact);
                }
                else if (!allReferenceElementsDefinitelyNonNull)
                {
                    return;
                }

                allReferenceElementsDefinitelyNonNull =
                    allReferenceElementsDefinitelyNonNull &&
                    IsDefinitelyNonNullReferenceValue(elementExpression, semanticModel, cancellationToken);
            }

            if (finiteDomainFact != null)
            {
                facts.Add(finiteDomainFact);
            }

            if (allReferenceElementsDefinitelyNonNull &&
                TryCreateSymbolSmtValue(iterationSymbol.OriginalDefinition, out var iterationFormula) &&
                iterationFormula is { Kind: SmtValueKind.Reference })
            {
                facts.Add(CreateReferenceNonNullFormula(iterationFormula));
            }
        }

        private static void AddFiniteForeachIterationValueFormulaFacts(
            ICollection<SmtFormula> facts,
            ILocalSymbol iterationSymbol,
            ImmutableArray<SmtFormula> elementValueFormulas)
        {
            SmtFormula? finiteDomainFact = null;
            foreach (var elementValueFormula in elementValueFormulas)
            {
                if (!TryCreateAssignedValueFact(iterationSymbol.OriginalDefinition, elementValueFormula, out var elementValueFact))
                {
                    return;
                }

                finiteDomainFact = finiteDomainFact == null
                    ? elementValueFact
                    : new SmtBinaryFormula(SmtBinaryOperator.Or, finiteDomainFact, elementValueFact);
            }

            if (finiteDomainFact != null)
            {
                facts.Add(finiteDomainFact);
            }
        }

        private static bool TryGetFiniteElementExpressions(
            ExpressionSyntax expressionSyntax,
            StatementSyntax foreachStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ImmutableArray<ExpressionSyntax> elementExpressions)
        {
            return TryGetFiniteElementExpressions(expressionSyntax, out elementExpressions) ||
                TryGetPriorAssignedFiniteElementExpressions(
                    expressionSyntax,
                    foreachStatement,
                    semanticModel,
                    cancellationToken,
                    out elementExpressions);
        }

        private static bool TryGetFiniteElementExpressions(
            ExpressionSyntax expressionSyntax,
            out ImmutableArray<ExpressionSyntax> elementExpressions)
        {
            expressionSyntax = UnwrapExpression(expressionSyntax);
            SeparatedSyntaxList<ExpressionSyntax>? initializerExpressions = null;
            switch (expressionSyntax)
            {
                case ArrayCreationExpressionSyntax { Initializer: { } initializer }:
                    initializerExpressions = initializer.Expressions;
                    break;
                case ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer }:
                    initializerExpressions = initializer.Expressions;
                    break;
                case CollectionExpressionSyntax collectionExpression:
                    return TryGetFiniteCollectionExpressionElements(collectionExpression, out elementExpressions);
            }

            if (initializerExpressions is not { } expressions ||
                expressions.Count == 0 ||
                expressions.Count > MaxFiniteForeachElementFacts)
            {
                elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
                return false;
            }

            elementExpressions = expressions.ToImmutableArray();
            return true;
        }

        private static bool TryGetPriorAssignedFiniteElementExpressions(
            ExpressionSyntax expressionSyntax,
            StatementSyntax foreachStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ImmutableArray<ExpressionSyntax> elementExpressions)
        {
            elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
            if (foreachStatement.Parent is not BlockSyntax containingBlock ||
                semanticModel.GetSymbolInfo(UnwrapExpression(expressionSyntax), cancellationToken).Symbol?.OriginalDefinition is not { } receiverSymbol ||
                receiverSymbol is not ILocalSymbol and not IParameterSymbol)
            {
                return false;
            }

            for (var index = containingBlock.Statements.Count - 1; index >= 0; index--)
            {
                var statement = containingBlock.Statements[index];
                if (statement.SpanStart >= foreachStatement.SpanStart)
                {
                    continue;
                }

                if (TryGetFiniteElementsFromAssignmentStatement(statement, receiverSymbol, semanticModel, cancellationToken, out elementExpressions))
                {
                    if (AnyStatementInvalidatesPriorAssignedFiniteElements(
                            containingBlock,
                            index + 1,
                            foreachStatement.SpanStart,
                            receiverSymbol,
                            semanticModel,
                            cancellationToken) ||
                        AnyReferencedElementSymbolInvalidatedAfterAssignment(
                            elementExpressions,
                            containingBlock,
                            index + 1,
                            foreachStatement.SpanStart,
                            receiverSymbol,
                            semanticModel,
                            cancellationToken))
                    {
                        elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
                        return false;
                    }

                    return true;
                }

                if (StatementInvalidatesPriorAssignedFiniteElements(statement, receiverSymbol, semanticModel, cancellationToken))
                {
                    elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
                    return false;
                }
            }

            return false;
        }

        private static bool TryGetPriorAssignedFiniteArrayElementValueFormulas(
            ExpressionSyntax expressionSyntax,
            StatementSyntax foreachStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ImmutableArray<SmtFormula> valueFormulas)
        {
            valueFormulas = ImmutableArray<SmtFormula>.Empty;
            if (semanticModel.GetSymbolInfo(UnwrapExpression(expressionSyntax), cancellationToken).Symbol?.OriginalDefinition is not { } receiverSymbol ||
                receiverSymbol is not ILocalSymbol and not IParameterSymbol ||
                SymbolicFactFactory.GetTrackedSymbolType(receiverSymbol) is not IArrayTypeSymbol { Rank: 1 } arrayType ||
                !TryGetPriorAssignedFiniteElementCount(
                    expressionSyntax,
                    foreachStatement,
                    semanticModel,
                    cancellationToken,
                    out var elementCount))
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<SmtFormula>(elementCount);
            for (var index = 0; index < elementCount; index++)
            {
                if (!TryCreateArrayElementSmtValue(receiverSymbol, arrayType.ElementType, index, out var elementFormula))
                {
                    valueFormulas = ImmutableArray<SmtFormula>.Empty;
                    return false;
                }

                builder.Add(elementFormula);
            }

            valueFormulas = builder.ToImmutable();
            return true;
        }

        private static bool TryGetPriorAssignedFiniteElementCount(
            ExpressionSyntax expressionSyntax,
            StatementSyntax containingStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out int elementCount)
        {
            elementCount = 0;
            if (containingStatement.Parent is not BlockSyntax containingBlock ||
                semanticModel.GetSymbolInfo(UnwrapExpression(expressionSyntax), cancellationToken).Symbol?.OriginalDefinition is not { } receiverSymbol ||
                receiverSymbol is not ILocalSymbol and not IParameterSymbol)
            {
                return false;
            }

            for (var index = containingBlock.Statements.Count - 1; index >= 0; index--)
            {
                var statement = containingBlock.Statements[index];
                if (statement.SpanStart >= containingStatement.SpanStart)
                {
                    continue;
                }

                if (TryGetFiniteElementsFromAssignmentStatement(statement, receiverSymbol, semanticModel, cancellationToken, out var elementExpressions))
                {
                    if (AnyStatementInvalidatesPriorAssignedFiniteElements(
                            containingBlock,
                            index + 1,
                            containingStatement.SpanStart,
                            receiverSymbol,
                            semanticModel,
                            cancellationToken))
                    {
                        elementCount = 0;
                        return false;
                    }

                    elementCount = elementExpressions.Length;
                    return true;
                }

                if (StatementInvalidatesPriorAssignedFiniteElements(statement, receiverSymbol, semanticModel, cancellationToken))
                {
                    elementCount = 0;
                    return false;
                }
            }

            return false;
        }

        private static bool AnyStatementInvalidatesPriorAssignedFiniteElements(
            BlockSyntax containingBlock,
            int firstStatementIndex,
            int beforeSpanStart,
            ISymbol receiverSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            for (var index = firstStatementIndex; index < containingBlock.Statements.Count; index++)
            {
                var statement = containingBlock.Statements[index];
                if (statement.SpanStart >= beforeSpanStart)
                {
                    break;
                }

                if (StatementInvalidatesPriorAssignedFiniteElements(statement, receiverSymbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool StatementInvalidatesPriorAssignedFiniteElements(
            StatementSyntax statement,
            ISymbol receiverSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return StatementInvalidatesSymbolValue(statement, receiverSymbol, semanticModel, cancellationToken);
        }

        private static bool StatementInvalidatesSymbolValue(
            StatementSyntax statement,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return StatementMutatesSymbol(statement, symbol, semanticModel, cancellationToken) ||
                StatementMayMutateSymbolThroughReference(statement, symbol, semanticModel, cancellationToken);
        }

        private static bool AnyReferencedElementSymbolInvalidatedAfterAssignment(
            ImmutableArray<ExpressionSyntax> elementExpressions,
            BlockSyntax containingBlock,
            int firstStatementIndex,
            int beforeSpanStart,
            ISymbol receiverSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var referencedSymbols = ImmutableArray.CreateBuilder<ISymbol>();
            foreach (var elementExpression in elementExpressions)
            {
                foreach (var referencedSymbol in GetReferencedLocalAndParameterSymbols(elementExpression, semanticModel, cancellationToken))
                {
                    if (SymbolEqualityComparer.Default.Equals(referencedSymbol, receiverSymbol))
                    {
                        return true;
                    }

                    if (referencedSymbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, referencedSymbol)))
                    {
                        referencedSymbols.Add(referencedSymbol);
                    }
                }
            }

            foreach (var referencedSymbol in referencedSymbols)
            {
                if (AnyStatementInvalidatesPriorAssignedFiniteElements(
                        containingBlock,
                        firstStatementIndex,
                        beforeSpanStart,
                        referencedSymbol,
                        semanticModel,
                        cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetFiniteElementsFromAssignmentStatement(
            StatementSyntax statement,
            ISymbol receiverSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ImmutableArray<ExpressionSyntax> elementExpressions)
        {
            elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
            if (statement is LocalDeclarationStatementSyntax localDeclaration)
            {
                foreach (var declarator in localDeclaration.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken)?.OriginalDefinition is { } declaredSymbol &&
                        SymbolEqualityComparer.Default.Equals(declaredSymbol, receiverSymbol))
                    {
                        return declarator.Initializer != null &&
                            TryGetFiniteElementExpressions(declarator.Initializer.Value, out elementExpressions);
                    }
                }

                return false;
            }

            if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol?.OriginalDefinition is { } assignedSymbol &&
                SymbolEqualityComparer.Default.Equals(assignedSymbol, receiverSymbol))
            {
                return TryGetFiniteElementExpressions(assignment.Right, out elementExpressions);
            }

            return false;
        }

        private static bool TryGetFiniteCollectionExpressionElements(
            CollectionExpressionSyntax collectionExpression,
            out ImmutableArray<ExpressionSyntax> elementExpressions)
        {
            if (collectionExpression.Elements.Count == 0 ||
                collectionExpression.Elements.Count > MaxFiniteForeachElementFacts)
            {
                elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<ExpressionSyntax>(collectionExpression.Elements.Count);
            foreach (var element in collectionExpression.Elements)
            {
                switch (element)
                {
                    case ExpressionElementSyntax expressionElement:
                        builder.Add(expressionElement.Expression);
                        break;
                    default:
                        elementExpressions = ImmutableArray<ExpressionSyntax>.Empty;
                        return false;
                }
            }

            elementExpressions = builder.ToImmutable();
            return true;
        }

        private static bool IsSupportedForeachLengthReceiver(ExpressionSyntax expressionSyntax)
        {
            expressionSyntax = UnwrapExpression(expressionSyntax);
            return expressionSyntax is ArrayCreationExpressionSyntax or
                ImplicitArrayCreationExpressionSyntax or
                CollectionExpressionSyntax;
        }

        private static bool IsSupportedForeachLengthReceiver(ITypeSymbol? type)
        {
            return type is IArrayTypeSymbol { Rank: 1 } ||
                type?.SpecialType == SpecialType.System_String;
        }

        private static void AddReferenceNullCondition(
            ICollection<SmtFormula> facts,
            ExpressionSyntax expressionSyntax,
            bool isNull,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (!SymbolicReachabilityService.TryCreateReferenceNullComparison(
                    expressionSyntax,
                    semanticModel,
                    cancellationToken,
                    isNull,
                    out var formula))
            {
                return;
            }

            facts.Add(formula);
        }

        private static bool StatementDefinitelyExits(
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            statement = UnwrapSingleStatementBlock(statement);
            return statement switch
            {
                ReturnStatementSyntax => true,
                ThrowStatementSyntax => true,
                BreakStatementSyntax => true,
                ContinueStatementSyntax => true,
                ExpressionStatementSyntax expressionStatement => ExpressionStatementDefinitelyExits(expressionStatement, semanticModel, cancellationToken),
                BlockSyntax block when block.Statements.Count > 0 => StatementDefinitelyExits(block.Statements[block.Statements.Count - 1], semanticModel, cancellationToken),
                IfStatementSyntax ifStatement when ifStatement.Else != null =>
                    StatementDefinitelyExits(ifStatement.Statement, semanticModel, cancellationToken) &&
                    StatementDefinitelyExits(ifStatement.Else.Statement, semanticModel, cancellationToken),
                _ => false
            };
        }

        private static bool ExpressionStatementDefinitelyExits(
            ExpressionStatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var expression = UnwrapExpression(statement.Expression);
            return expression is InvocationExpressionSyntax invocation &&
                semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation &&
                MethodHasDoesNotReturnAttribute(invocationOperation.TargetMethod);
        }

        private static StatementSyntax UnwrapSingleStatementBlock(StatementSyntax statement)
        {
            while (statement is BlockSyntax { Statements.Count: 1 } block)
            {
                statement = block.Statements[0];
            }

            return statement;
        }

        private static void RemoveFactsInvalidatedByNestedMutations(
            SyntaxNode root,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                var mutatedExpression = node switch
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

                if (mutatedExpression != null)
                {
                    var mutatedSymbol = GetMutatedSymbol(mutatedExpression, semanticModel, cancellationToken);
                    if (mutatedSymbol is ILocalSymbol or IParameterSymbol)
                    {
                        RemoveFactsReferencingSymbol(facts, mutatedSymbol.OriginalDefinition);
                    }

                    if (mutatedSymbol is IFieldSymbol or IPropertySymbol &&
                        IsCurrentInstanceMemberReference(mutatedExpression, semanticModel, cancellationToken))
                    {
                        RemoveFactsReferencingImplicitThisMember(facts, mutatedSymbol.Name);
                    }

                    foreach (var receiverSymbol in GetMutatedReceiverSymbols(mutatedExpression, semanticModel, cancellationToken))
                    {
                        RemoveFactsReferencingSymbol(facts, receiverSymbol);
                    }
                }

                foreach (var receiverSymbol in GetPotentiallyMutatedArraySymbols(node, semanticModel, cancellationToken))
                {
                    RemoveFactsReferencingSymbol(facts, receiverSymbol);
                }
            }
        }

        private static ISymbol? GetMutatedSymbol(
            ExpressionSyntax mutatedExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var symbol = semanticModel.GetSymbolInfo(mutatedExpression, cancellationToken).Symbol;
            if (symbol != null)
            {
                return NormalizeMutatedSymbol(symbol);
            }

            return semanticModel.GetOperation(mutatedExpression, cancellationToken) switch
            {
                IFieldReferenceOperation fieldReference => fieldReference.Field,
                IPropertyReferenceOperation propertyReference => propertyReference.Property,
                _ => null,
            };
        }

        private static ISymbol NormalizeMutatedSymbol(ISymbol symbol)
        {
            return symbol is IMethodSymbol { AssociatedSymbol: IPropertySymbol property }
                ? property
                : symbol;
        }

        private static bool IsCurrentInstanceMemberReference(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (expression is IdentifierNameSyntax &&
                GetMutatedSymbol(expression, semanticModel, cancellationToken) is { IsStatic: false } and (IFieldSymbol or IPropertySymbol))
            {
                return true;
            }

            return expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
        }

        private static IEnumerable<ISymbol> GetMutatedReceiverSymbols(
            ExpressionSyntax mutatedExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            ExpressionSyntax? receiverExpression = UnwrapExpression(mutatedExpression) switch
            {
                ElementAccessExpressionSyntax elementAccess => elementAccess.Expression,
                MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
                _ => null
            };

            if (receiverExpression == null)
            {
                yield break;
            }

            var receiverSymbol = semanticModel.GetSymbolInfo(UnwrapExpression(receiverExpression), cancellationToken).Symbol?.OriginalDefinition;
            if (receiverSymbol is ILocalSymbol or IParameterSymbol)
            {
                yield return receiverSymbol;
            }
        }

        private static IEnumerable<ISymbol> GetPotentiallyMutatedArraySymbols(
            SyntaxNode node,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                    {
                        foreach (var symbol in GetReferencedArraySymbols(memberAccess.Expression, semanticModel, cancellationToken))
                        {
                            yield return symbol;
                        }
                    }

                    foreach (var argument in invocation.ArgumentList.Arguments)
                    {
                        foreach (var symbol in GetReferencedArraySymbols(argument.Expression, semanticModel, cancellationToken))
                        {
                            yield return symbol;
                        }
                    }

                    break;
                case ObjectCreationExpressionSyntax { ArgumentList: { } argumentList }:
                    foreach (var argument in argumentList.Arguments)
                    {
                        foreach (var symbol in GetReferencedArraySymbols(argument.Expression, semanticModel, cancellationToken))
                        {
                            yield return symbol;
                        }
                    }

                    break;
            }
        }

        private static IEnumerable<ISymbol> GetReferencedArraySymbols(
            SyntaxNode root,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var symbol in GetReferencedLocalAndParameterSymbols(root, semanticModel, cancellationToken))
            {
                if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is IArrayTypeSymbol)
                {
                    yield return symbol;
                }
            }
        }

        private static bool StatementMayMutateSymbolThroughReference(
            StatementSyntax statement,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (!IsPotentiallyMutableThroughReference(SymbolicFactFactory.GetTrackedSymbolType(symbol)))
            {
                return false;
            }

            foreach (var node in statement.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (NodeMayMutateSymbolThroughReference(node, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NodeMayMutateSymbolThroughReference(
            SyntaxNode node,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                        ExpressionReferencesSymbol(memberAccess.Expression, symbol, semanticModel, cancellationToken))
                    {
                        return true;
                    }

                    return invocation.ArgumentList.Arguments.Any(argument =>
                        ExpressionReferencesSymbol(argument.Expression, symbol, semanticModel, cancellationToken));
                case ObjectCreationExpressionSyntax { ArgumentList: { } argumentList }:
                    return argumentList.Arguments.Any(argument =>
                        ExpressionReferencesSymbol(argument.Expression, symbol, semanticModel, cancellationToken));
                default:
                    return false;
            }
        }

        private static bool IsPotentiallyMutableThroughReference(ITypeSymbol? type)
        {
            return type is IArrayTypeSymbol ||
                type?.IsReferenceType == true &&
                type.SpecialType != SpecialType.System_String;
        }

        private static void AddAssignedValueFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            RemoveFactsReferencingSymbol(facts, assignedSymbol);
            var hasThrowGuard = TryGetThrowGuardedValue(
                valueExpression,
                out var throwGuardedValue,
                out var guardExpression,
                out var guardBranchWhenTrue,
                out var requiresNonNullValue);
            var effectiveValueExpression = hasThrowGuard
                ? throwGuardedValue
                : valueExpression;

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                TryCreateAssignedValueFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts, out var fact))
            {
                facts.Add(fact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                TryCreateAssignedValueNonZeroFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts, out var nonZeroFact))
            {
                facts.Add(nonZeroFact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                AddSwitchExpressionAssignedValueFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                AddMathAbsRemainderAssignedRangeFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                AddAssignedSourceSymbolSnapshotFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                SymbolicReachabilityService.TryCreateStringContentAssignedValueFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var stringContentFact))
            {
                facts.Add(stringContentFact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                SymbolicReachabilityService.TryCreateStringNonNullAssignedValueFact(
                    assignedSymbol,
                    effectiveValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var stringNonNullFact))
            {
                facts.Add(stringNonNullFact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                AddNotNullIfNotNullAssignedNullStateFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                SymbolicReachabilityService.TryCreateBuiltInLengthAssignedValueFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var lengthFact))
            {
                facts.Add(lengthFact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                SymbolicReachabilityService.TryCreateReferenceBackedLengthFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var referenceLengthFact))
            {
                AddUniqueFact(facts, referenceLengthFact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                SymbolicReachabilityService.TryCreateCollectionExpressionLengthLowerBoundFact(assignedSymbol, effectiveValueExpression, out var lowerBoundLengthFact))
            {
                AddUniqueFact(facts, lowerBoundLengthFact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                SymbolicReachabilityService.AddArrayDimensionLengthAssignedValueFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                SymbolicReachabilityService.AddReferenceBackedArrayDimensionLengthFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                AddFiniteArrayElementAssignedValueFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                IsDefinitelyNonNullReferenceValue(effectiveValueExpression, semanticModel, cancellationToken) &&
                TryCreateSymbolSmtValue(assignedSymbol, out var targetFormula) &&
                targetFormula is { Kind: SmtValueKind.Reference })
            {
                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    targetFormula,
                    new SmtNullConstant()));
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                SymbolicReachabilityService.TryCreateReferenceBackedStringContentFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var referenceStringFact))
            {
                AddUniqueFact(facts, referenceStringFact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                AddStructuralReferenceNullStateAssignedValueFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
                SymbolicReachabilityService.AddNullableAssignedValueFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
                AddAsExpressionAssignedValueFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
                AddConditionalAccessAssignedValueFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
            }

            AddTupleElementAssignedValueFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);

            if (hasThrowGuard &&
                guardExpression != null &&
                !ExpressionReferencesSymbol(guardExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                AddBranchConditionFacts(
                    guardExpression,
                    guardBranchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    facts);
            }
            else if (hasThrowGuard &&
                     requiresNonNullValue &&
                     !ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                AddReferenceNonNullFact(effectiveValueExpression, semanticModel, cancellationToken, facts);
            }
        }

        private static void AddNotNullIfNotNullAssignedNullStateFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (!SymbolicReachabilityService.TryCreateNotNullIfNotNullAssignedValueFact(
                    assignedSymbol,
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var fact))
            {
                return;
            }

            facts.Add(fact);
        }

        private static void AddSwitchExpressionAssignedValueFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (UnwrapExpression(valueExpression) is not SwitchExpressionSyntax switchExpression ||
                switchExpression.Arms.Count == 0)
            {
                return;
            }

            var conditionSymbols = GetSwitchExpressionConditionSymbols(switchExpression, semanticModel, cancellationToken);
            if (ExpressionMutatesAnySymbol(switchExpression, conditionSymbols, semanticModel, cancellationToken))
            {
                return;
            }

            var existingKeys = new HashSet<string>(facts.Select(GetFormulaKey), StringComparer.Ordinal);
            var addedCount = 0;
            foreach (var arm in switchExpression.Arms)
            {
                if (!SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                        switchExpression.GoverningExpression,
                        arm,
                        semanticModel,
                        cancellationToken,
                        out var armCondition))
                {
                    continue;
                }

                SmtFormula? armFact = null;
                if (UnwrapExpression(arm.Expression) is ThrowExpressionSyntax)
                {
                    armFact = new SmtUnaryFormula(SmtUnaryOperator.Not, armCondition);
                }
                else if (TryCreateAssignedValueFact(
                             assignedSymbol,
                             arm.Expression,
                             semanticModel,
                             cancellationToken,
                             new[] { armCondition },
                             out var assignedValueFact))
                {
                    armFact = new SmtBinaryFormula(
                        SmtBinaryOperator.Or,
                        new SmtUnaryFormula(SmtUnaryOperator.Not, armCondition),
                        assignedValueFact);
                }

                if (armFact == null ||
                    !existingKeys.Add(GetFormulaKey(armFact)))
                {
                    continue;
                }

                facts.Add(armFact);
                addedCount++;
                if (addedCount >= MaxMergedSwitchFacts)
                {
                    return;
                }
            }
        }

        private static void AddAssignedSourceSymbolSnapshotFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                    UnwrapExpression(valueExpression),
                    semanticModel,
                    cancellationToken,
                    out var sourceSymbol) ||
                SymbolEqualityComparer.Default.Equals(sourceSymbol, assignedSymbol))
            {
                return;
            }

            if (TryCreateSymbolSmtValue(sourceSymbol, out var sourceFormula) &&
                TryCreateSymbolSmtValue(assignedSymbol, out var targetFormula))
            {
                AddSubstitutedCurrentFacts(facts, sourceFormula, targetFormula);
            }

            if (TryCreateBuiltInLengthFormula(sourceSymbol, out var sourceLength) &&
                TryCreateBuiltInLengthFormula(assignedSymbol, out var targetLength))
            {
                AddSubstitutedCurrentFacts(facts, sourceLength, targetLength);
            }

            AddArrayDimensionLengthSourceSymbolSnapshotFacts(assignedSymbol, sourceSymbol, facts);

            if (TryCreateStringContentFormula(sourceSymbol, out var sourceString) &&
                TryCreateStringContentFormula(assignedSymbol, out var targetString))
            {
                AddSubstitutedCurrentFacts(facts, sourceString, targetString);
            }

            if (TryCreateNullableHasValueFormula(sourceSymbol, out var sourceHasValue) &&
                TryCreateNullableHasValueFormula(assignedSymbol, out var targetHasValue))
            {
                AddSubstitutedCurrentFacts(facts, sourceHasValue, targetHasValue);
            }

            if (TryCreateNullableValueFormula(sourceSymbol, out var sourceNullableValue) &&
                TryCreateNullableValueFormula(assignedSymbol, out var targetNullableValue))
            {
                AddSubstitutedCurrentFacts(facts, sourceNullableValue, targetNullableValue);
            }

            AddTupleElementSourceSymbolSnapshotFacts(assignedSymbol, sourceSymbol, facts);
        }

        private static void AddMathAbsRemainderAssignedRangeFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            valueExpression = UnwrapExpression(valueExpression);
            if (valueExpression is not InvocationExpressionSyntax invocationExpression ||
                !TryCreateSymbolSmtValue(assignedSymbol, out var targetFormula) ||
                targetFormula.Kind != SmtValueKind.Int ||
                !CSharpMathPatternRecognizer.TryGetMathAbsRemainderOperands(
                    invocationExpression,
                    semanticModel,
                    cancellationToken,
                    out _,
                    out var divisorExpression) ||
                !TryCreateIntegerValueFormula(
                    divisorExpression,
                    semanticModel,
                    cancellationToken,
                    out var divisorFormula) ||
                (!FactsProvePositiveInteger(divisorFormula, facts) &&
                    !IsBuiltInNonNegativeLengthValue(divisorExpression, divisorFormula, semanticModel, cancellationToken)))
            {
                return;
            }

            AddUniqueFact(
                facts,
                new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    targetFormula,
                    new SmtIntegerConstant(0)));
            AddUniqueFact(
                facts,
                new SmtBinaryFormula(
                    SmtBinaryOperator.LessThan,
                    targetFormula,
                    divisorFormula));
        }

        private static bool IsBuiltInNonNegativeLengthValue(
            ExpressionSyntax expression,
            SmtFormula expectedFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (expression is MemberAccessExpressionSyntax memberAccess &&
                string.Equals(memberAccess.Name.Identifier.ValueText, "Length", StringComparison.Ordinal) &&
                TryCreateBuiltInLengthValueFormula(
                    memberAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverLengthFormula) &&
                Equals(receiverLengthFormula, expectedFormula))
            {
                return true;
            }

            return TryCreateBuiltInLengthValueFormula(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula) &&
                Equals(lengthFormula, expectedFormula);
        }

        private static bool FactsProvePositiveInteger(SmtFormula expression, IEnumerable<SmtFormula> facts)
        {
            foreach (var fact in facts)
            {
                if (FactProvesPositiveInteger(fact, expression))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool FactProvesPositiveInteger(SmtFormula fact, SmtFormula expression)
        {
            return fact switch
            {
                SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula =>
                    FactProvesPositiveInteger(andFormula.Left, expression) ||
                    FactProvesPositiveInteger(andFormula.Right, expression),
                SmtBinaryFormula comparison =>
                    ComparisonProvesPositiveInteger(comparison, expression),
                _ => false,
            };
        }

        private static bool ComparisonProvesPositiveInteger(SmtBinaryFormula comparison, SmtFormula expression)
        {
            if (Equals(comparison.Left, expression) &&
                comparison.Right is SmtIntegerConstant rightConstant)
            {
                return comparison.Operator switch
                {
                    SmtBinaryOperator.Equal => rightConstant.Value > 0,
                    SmtBinaryOperator.GreaterThan => rightConstant.Value >= 0,
                    SmtBinaryOperator.GreaterThanOrEqual => rightConstant.Value > 0,
                    _ => false,
                };
            }

            if (Equals(comparison.Right, expression) &&
                comparison.Left is SmtIntegerConstant leftConstant)
            {
                return comparison.Operator switch
                {
                    SmtBinaryOperator.Equal => leftConstant.Value > 0,
                    SmtBinaryOperator.LessThan => leftConstant.Value >= 0,
                    SmtBinaryOperator.LessThanOrEqual => leftConstant.Value > 0,
                    _ => false,
                };
            }

            return false;
        }

        private static void AddArrayDimensionLengthSourceSymbolSnapshotFacts(
            ISymbol assignedSymbol,
            ISymbol sourceSymbol,
            List<SmtFormula> facts)
        {
            if (SymbolicFactFactory.GetTrackedSymbolType(assignedSymbol) is not IArrayTypeSymbol assignedArrayType ||
                SymbolicFactFactory.GetTrackedSymbolType(sourceSymbol) is not IArrayTypeSymbol sourceArrayType ||
                assignedArrayType.Rank != sourceArrayType.Rank ||
                assignedArrayType.Rank <= 1)
            {
                return;
            }

            for (var dimension = 0; dimension < assignedArrayType.Rank; dimension++)
            {
                if (TryCreateArrayDimensionLengthFormula(sourceSymbol, dimension, out var sourceLength) &&
                    TryCreateArrayDimensionLengthFormula(assignedSymbol, dimension, out var targetLength))
                {
                    AddSubstitutedCurrentFacts(facts, sourceLength, targetLength);
                }
            }
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

        private static void AddTupleElementSourceSymbolSnapshotFacts(
            ISymbol assignedSymbol,
            ISymbol sourceSymbol,
            List<SmtFormula> facts)
        {
            if (!TryGetTupleElementStorageNames(assignedSymbol, expectedCount: 0, out var targetElementNames) ||
                !TryGetTupleElementStorageNames(sourceSymbol, targetElementNames.Length, out var sourceElementNames))
            {
                return;
            }

            for (var index = 0; index < targetElementNames.Length; index++)
            {
                if (!TryCreateTupleElementSmtValue(sourceSymbol, sourceElementNames[index], out var sourceElement) ||
                    !TryCreateTupleElementSmtValue(assignedSymbol, targetElementNames[index], out var targetElement))
                {
                    continue;
                }

                AddSubstitutedCurrentFacts(facts, sourceElement, targetElement);

                AddTupleElementDerivedSourceSymbolSnapshotFacts(
                    assignedSymbol,
                    targetElementNames[index],
                    sourceSymbol,
                    sourceElementNames[index],
                    facts);
            }
        }

        private static void AddTupleElementDerivedSourceSymbolSnapshotFacts(
            ISymbol targetTupleSymbol,
            string targetElementName,
            ISymbol sourceTupleSymbol,
            string sourceElementName,
            List<SmtFormula> facts)
        {
            if (TryCreateTupleElementBuiltInLengthFormula(sourceTupleSymbol, sourceElementName, out var sourceLength) &&
                TryCreateTupleElementBuiltInLengthFormula(targetTupleSymbol, targetElementName, out var targetLength))
            {
                AddSubstitutedCurrentFacts(facts, sourceLength, targetLength);
            }

            if (TryCreateTupleElementStringContentFormula(sourceTupleSymbol, sourceElementName, out var sourceString) &&
                TryCreateTupleElementStringContentFormula(targetTupleSymbol, targetElementName, out var targetString))
            {
                AddSubstitutedCurrentFacts(facts, sourceString, targetString);
            }

            var sourceElementType = TryGetTupleElementType(sourceTupleSymbol, sourceElementName, out var sourceType)
                ? sourceType
                : null;
            var targetElementType = TryGetTupleElementType(targetTupleSymbol, targetElementName, out var targetType)
                ? targetType
                : null;
            if (sourceElementType is not IArrayTypeSymbol sourceArrayType ||
                targetElementType is not IArrayTypeSymbol targetArrayType ||
                sourceArrayType.Rank != targetArrayType.Rank ||
                sourceArrayType.Rank <= 1)
            {
                return;
            }

            for (var dimension = 0; dimension < sourceArrayType.Rank; dimension++)
            {
                if (TryCreateTupleElementArrayDimensionLengthFormula(sourceTupleSymbol, sourceElementName, dimension, out var sourceDimensionLength) &&
                    TryCreateTupleElementArrayDimensionLengthFormula(targetTupleSymbol, targetElementName, dimension, out var targetDimensionLength))
                {
                    AddSubstitutedCurrentFacts(facts, sourceDimensionLength, targetDimensionLength);
                }
            }
        }

        private static void AddSubstitutedCurrentFacts(
            List<SmtFormula> facts,
            SmtFormula sourceFormula,
            SmtFormula targetFormula)
        {
            if (sourceFormula.Kind != targetFormula.Kind ||
                Equals(sourceFormula, targetFormula))
            {
                return;
            }

            var existingKeys = new HashSet<string>(facts.Select(GetFormulaKey), StringComparer.Ordinal);
            foreach (var fact in facts.ToArray())
            {
                if (!TrySubstituteFormula(fact, sourceFormula, targetFormula, out var substituted) ||
                    Equals(substituted, fact))
                {
                    continue;
                }

                var key = GetFormulaKey(substituted);
                if (existingKeys.Add(key))
                {
                    facts.Add(substituted);
                }
            }
        }

        private static bool TrySubstituteFormula(
            SmtFormula formula,
            SmtFormula sourceFormula,
            SmtFormula targetFormula,
            out SmtFormula substituted)
        {
            if (Equals(formula, sourceFormula))
            {
                substituted = targetFormula;
                return true;
            }

            switch (formula)
            {
                case SmtUnaryFormula unary:
                    if (TrySubstituteFormula(unary.Operand, sourceFormula, targetFormula, out var operand))
                    {
                        substituted = new SmtUnaryFormula(unary.Operator, operand);
                        return true;
                    }

                    break;
                case SmtBinaryFormula binary:
                    var leftChanged = TrySubstituteFormula(binary.Left, sourceFormula, targetFormula, out var left);
                    var rightChanged = TrySubstituteFormula(binary.Right, sourceFormula, targetFormula, out var right);
                    if (leftChanged || rightChanged)
                    {
                        substituted = new SmtBinaryFormula(
                            binary.Operator,
                            leftChanged ? left : binary.Left,
                            rightChanged ? right : binary.Right);
                        return true;
                    }

                    break;
                case SmtIntegerUnaryTerm integerUnary:
                    if (TrySubstituteFormula(integerUnary.Operand, sourceFormula, targetFormula, out var integerOperand))
                    {
                        substituted = new SmtIntegerUnaryTerm(integerUnary.Operator, integerOperand);
                        return true;
                    }

                    break;
                case SmtIntegerBinaryTerm integerBinary:
                    var integerLeftChanged = TrySubstituteFormula(integerBinary.Left, sourceFormula, targetFormula, out var integerLeft);
                    var integerRightChanged = TrySubstituteFormula(integerBinary.Right, sourceFormula, targetFormula, out var integerRight);
                    if (integerLeftChanged || integerRightChanged)
                    {
                        substituted = new SmtIntegerBinaryTerm(
                            integerBinary.Operator,
                            integerLeftChanged ? integerLeft : integerBinary.Left,
                            integerRightChanged ? integerRight : integerBinary.Right);
                        return true;
                    }

                    break;
                case SmtStringLengthTerm stringLength:
                    if (TrySubstituteFormula(stringLength.Value, sourceFormula, targetFormula, out var stringLengthValue))
                    {
                        substituted = new SmtStringLengthTerm(stringLengthValue);
                        return true;
                    }

                    break;
                case SmtStringConcatTerm stringConcat:
                    var stringConcatLeftChanged = TrySubstituteFormula(stringConcat.Left, sourceFormula, targetFormula, out var stringConcatLeft);
                    var stringConcatRightChanged = TrySubstituteFormula(stringConcat.Right, sourceFormula, targetFormula, out var stringConcatRight);
                    if (stringConcatLeftChanged || stringConcatRightChanged)
                    {
                        substituted = new SmtStringConcatTerm(
                            stringConcatLeftChanged ? stringConcatLeft : stringConcat.Left,
                            stringConcatRightChanged ? stringConcatRight : stringConcat.Right);
                        return true;
                    }

                    break;
                case SmtStringContainsFormula stringContains:
                    var stringContainsValueChanged = TrySubstituteFormula(stringContains.Value, sourceFormula, targetFormula, out var stringContainsValue);
                    var stringContainsSearchChanged = TrySubstituteFormula(stringContains.Search, sourceFormula, targetFormula, out var stringContainsSearch);
                    if (stringContainsValueChanged || stringContainsSearchChanged)
                    {
                        substituted = new SmtStringContainsFormula(
                            stringContainsValueChanged ? stringContainsValue : stringContains.Value,
                            stringContainsSearchChanged ? stringContainsSearch : stringContains.Search);
                        return true;
                    }

                    break;
                case SmtStringStartsWithFormula stringStartsWith:
                    var stringStartsWithValueChanged = TrySubstituteFormula(stringStartsWith.Value, sourceFormula, targetFormula, out var stringStartsWithValue);
                    var stringStartsWithPrefixChanged = TrySubstituteFormula(stringStartsWith.Prefix, sourceFormula, targetFormula, out var stringStartsWithPrefix);
                    if (stringStartsWithValueChanged || stringStartsWithPrefixChanged)
                    {
                        substituted = new SmtStringStartsWithFormula(
                            stringStartsWithValueChanged ? stringStartsWithValue : stringStartsWith.Value,
                            stringStartsWithPrefixChanged ? stringStartsWithPrefix : stringStartsWith.Prefix);
                        return true;
                    }

                    break;
                case SmtStringEndsWithFormula stringEndsWith:
                    var stringEndsWithValueChanged = TrySubstituteFormula(stringEndsWith.Value, sourceFormula, targetFormula, out var stringEndsWithValue);
                    var stringEndsWithSuffixChanged = TrySubstituteFormula(stringEndsWith.Suffix, sourceFormula, targetFormula, out var stringEndsWithSuffix);
                    if (stringEndsWithValueChanged || stringEndsWithSuffixChanged)
                    {
                        substituted = new SmtStringEndsWithFormula(
                            stringEndsWithValueChanged ? stringEndsWithValue : stringEndsWith.Value,
                            stringEndsWithSuffixChanged ? stringEndsWithSuffix : stringEndsWith.Suffix);
                        return true;
                    }

                    break;
                case SmtRegexMatchFormula regexMatch:
                    if (TrySubstituteFormula(regexMatch.Value, sourceFormula, targetFormula, out var regexValue))
                    {
                        substituted = new SmtRegexMatchFormula(regexValue, regexMatch.Pattern, regexMatch.Options);
                        return true;
                    }

                    break;
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    if (TrySubstituteFormula(runtimeTypeTest.Value, sourceFormula, targetFormula, out var runtimeTypeValue))
                    {
                        substituted = new SmtRuntimeTypeTestFormula(runtimeTypeValue, runtimeTypeTest.TypeKey);
                        return true;
                    }

                    break;
                case SmtConditionalFormula conditional:
                    var conditionChanged = TrySubstituteFormula(conditional.Condition, sourceFormula, targetFormula, out var condition);
                    var whenTrueChanged = TrySubstituteFormula(conditional.WhenTrue, sourceFormula, targetFormula, out var whenTrue);
                    var whenFalseChanged = TrySubstituteFormula(conditional.WhenFalse, sourceFormula, targetFormula, out var whenFalse);
                    if (conditionChanged || whenTrueChanged || whenFalseChanged)
                    {
                        substituted = new SmtConditionalFormula(
                            conditionChanged ? condition : conditional.Condition,
                            whenTrueChanged ? whenTrue : conditional.WhenTrue,
                            whenFalseChanged ? whenFalse : conditional.WhenFalse,
                            conditional.ResultKind);
                        return true;
                    }

                    break;
            }

            substituted = formula;
            return false;
        }

        private static void AddStructuralReferenceNullStateAssignedValueFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!IsStructuralReferenceNullStateExpression(valueExpression) ||
                !TryCreateSymbolSmtValue(assignedSymbol, out var targetFormula) ||
                targetFormula is not { Kind: SmtValueKind.Reference } ||
                !TryCreateReferenceNullStateFormula(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    depth: 0,
                    out var valueNullState))
            {
                return;
            }

            facts.Add(new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                CreateReferenceNullFormula(targetFormula),
                valueNullState));
        }

        private static bool IsStructuralReferenceNullStateExpression(ExpressionSyntax expression)
        {
            expression = UnwrapExpression(expression);
            return expression is ConditionalExpressionSyntax ||
                expression is ConditionalAccessExpressionSyntax ||
                expression is BinaryExpressionSyntax binaryExpression &&
                binaryExpression.IsKind(SyntaxKind.CoalesceExpression);
        }

        private static bool TryCreateReferenceNullStateFormula(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int depth,
            out SmtFormula formula)
        {
            formula = null!;
            if (depth > MaxStructuralNullStateDepth)
            {
                return false;
            }

            expression = UnwrapExpression(expression);
            if (semanticModel.GetConstantValue(expression, cancellationToken) is { HasValue: true, Value: null })
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type?.IsReferenceType != true)
            {
                return false;
            }

            if (IsDefinitelyNonNullReferenceValue(expression, semanticModel, cancellationToken))
            {
                formula = new SmtBooleanConstant(false);
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression)
            {
                return TryCreateConditionalReferenceNullStateFormula(
                    conditionalExpression,
                    semanticModel,
                    cancellationToken,
                    depth,
                    out formula);
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression))
            {
                return TryCreateCoalesceReferenceNullStateFormula(
                    coalesceExpression,
                    semanticModel,
                    cancellationToken,
                    depth,
                    out formula);
            }

            if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
            {
                return TryCreateConditionalAccessReferenceNullStateFormula(
                    conditionalAccess,
                    semanticModel,
                    cancellationToken,
                    depth,
                    out formula);
            }

            if (!TryCreateReferenceValueFormula(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula))
            {
                return false;
            }

            formula = CreateReferenceNullFormula(valueFormula);
            return true;
        }

        private static bool TryCreateConditionalReferenceNullStateFormula(
            ConditionalExpressionSyntax conditionalExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int depth,
            out SmtFormula formula)
        {
            formula = null!;
            if (!TryCreateConditionFormula(
                    conditionalExpression.Condition,
                    semanticModel,
                    cancellationToken,
                    out var conditionFormula) ||
                !TryCreateReferenceNullStateFormula(
                    conditionalExpression.WhenTrue,
                    semanticModel,
                    cancellationToken,
                    depth + 1,
                    out var whenTrueNullState) ||
                !TryCreateReferenceNullStateFormula(
                    conditionalExpression.WhenFalse,
                    semanticModel,
                    cancellationToken,
                    depth + 1,
                    out var whenFalseNullState))
            {
                return false;
            }

            formula = new SmtConditionalFormula(conditionFormula, whenTrueNullState, whenFalseNullState, SmtValueKind.Bool);
            return true;
        }

        private static bool TryCreateCoalesceReferenceNullStateFormula(
            BinaryExpressionSyntax coalesceExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int depth,
            out SmtFormula formula)
        {
            formula = null!;
            if (!TryCreateReferenceNullStateFormula(
                    coalesceExpression.Left,
                    semanticModel,
                    cancellationToken,
                    depth + 1,
                    out var leftNullState) ||
                !TryCreateReferenceNullStateFormula(
                    coalesceExpression.Right,
                    semanticModel,
                    cancellationToken,
                    depth + 1,
                    out var rightNullState))
            {
                return false;
            }

            formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftNullState, rightNullState);
            return true;
        }

        private static bool TryCreateConditionalAccessReferenceNullStateFormula(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int depth,
            out SmtFormula formula)
        {
            formula = null!;
            if (depth >= MaxStructuralNullStateDepth ||
                !TryCreateReferenceValueFormula(
                    conditionalAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula) ||
                !TryCreateConditionalAccessWhenNotNullValueFormula(
                    conditionalAccess,
                    receiverFormula,
                    semanticModel,
                    cancellationToken,
                    out var whenNotNullValue) ||
                whenNotNullValue is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formula = new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                CreateReferenceNullFormula(receiverFormula),
                CreateReferenceNullFormula(whenNotNullValue));
            return true;
        }

        private static SmtFormula CreateReferenceNullFormula(SmtFormula formula)
        {
            return new SmtBinaryFormula(SmtBinaryOperator.Equal, formula, new SmtNullConstant());
        }

        private static SmtFormula CreateReferenceNonNullFormula(SmtFormula formula)
        {
            return new SmtBinaryFormula(SmtBinaryOperator.NotEqual, formula, new SmtNullConstant());
        }

        private static bool TryTranslateAssignedValueExpression(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ISymbol? assignedSymbol,
            out SmtFormula? formula,
            IEnumerable<SmtFormula>? pathFacts = null)
        {
            valueExpression = UnwrapExpression(valueExpression);
            if (TryTranslateFiniteElementAccessValue(
                valueExpression,
                semanticModel,
                cancellationToken,
                assignedSymbol,
                out formula))
            {
                return true;
            }

            if (valueExpression is ConditionalExpressionSyntax conditionalExpression &&
                TryCreateConditionFormula(
                    conditionalExpression.Condition,
                    semanticModel,
                    cancellationToken,
                    out var conditionFormula) &&
                TryTranslateAssignedValueExpression(
                    conditionalExpression.WhenTrue,
                    semanticModel,
                    cancellationToken,
                    assignedSymbol,
                    out var whenTrueFormula,
                    pathFacts) &&
                whenTrueFormula != null &&
                TryTranslateAssignedValueExpression(
                    conditionalExpression.WhenFalse,
                    semanticModel,
                    cancellationToken,
                    assignedSymbol,
                    out var whenFalseFormula,
                    pathFacts) &&
                whenFalseFormula != null &&
                whenTrueFormula.Kind == whenFalseFormula.Kind)
            {
                formula = new SmtConditionalFormula(conditionFormula, whenTrueFormula, whenFalseFormula, whenTrueFormula.Kind);
                return true;
            }

            if (SymbolicReachabilityService.TryTranslateValueWithPathFacts(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    pathFacts,
                    out formula,
                    getSymbolVersion: null,
                    inlineDepth: 0) &&
                formula != null)
            {
                return true;
            }

            formula = null;
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

            if (type?.SpecialType == SpecialType.System_String &&
                TryCreateSymbolTerm(symbol, out var reference) &&
                reference.Kind == SmtValueKind.Reference &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(new SymbolicStringContentTerm(reference), out formula))
            {
                return true;
            }

            return SymbolicFactFactory.TryCreateStringContentFormula(SymbolicFactFactory.GetSmtVariableName(symbol), type, out formula);
        }

        private static bool TryCreateStringContentFormulaForReference(
            SmtFormula receiverFormula,
            ITypeSymbol? type,
            out SmtFormula formula)
        {
            formula = null!;
            if (type?.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            if (SymbolicSmtFormulaLowerer.TryLowerTerm(receiverFormula, out var receiver) &&
                receiver.Kind == SmtValueKind.Reference &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(new SymbolicStringContentTerm(receiver), out formula))
            {
                return true;
            }

            return SymbolicFactFactory.TryCreateReferenceStringContentFormula(receiverFormula, out formula);
        }

        private static void AddFiniteArrayElementAssignedValueFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (SymbolicFactFactory.GetTrackedSymbolType(assignedSymbol) is not IArrayTypeSymbol { Rank: 1 } arrayType ||
                !TryGetFiniteElementExpressions(valueExpression, out var elementExpressions))
            {
                return;
            }

            for (var index = 0; index < elementExpressions.Length; index++)
            {
                if (!TryCreateArrayElementSmtValue(assignedSymbol, arrayType.ElementType, index, out var targetFormula))
                {
                    return;
                }

                var elementExpression = elementExpressions[index];
                if (!ExpressionReferencesSymbol(elementExpression, assignedSymbol, semanticModel, cancellationToken) &&
                    TryTranslateAssignedValueExpression(
                        elementExpression,
                        semanticModel,
                        cancellationToken,
                        assignedSymbol,
                        out var valueFormula) &&
                    valueFormula != null &&
                    SymbolicFactFactory.CanCompareSmtValues(targetFormula, valueFormula))
                {
                    facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetFormula, valueFormula));
                }

                if (targetFormula.Kind == SmtValueKind.Reference &&
                    IsDefinitelyNonNullReferenceValue(elementExpression, semanticModel, cancellationToken))
                {
                    facts.Add(CreateReferenceNonNullFormula(targetFormula));
                }
            }
        }

        private static bool TryCreateArrayElementSmtValue(
            ISymbol arraySymbol,
            ITypeSymbol elementType,
            int index,
            out SmtFormula formula)
        {
            formula = null!;
            if (!TryCreateSymbolTerm(arraySymbol, out var receiver) ||
                receiver.Kind != SmtValueKind.Reference ||
                !TryGetValueKind(elementType, out var elementKind))
            {
                return false;
            }

            var element = new SymbolicElementTerm(
                receiver,
                new SymbolicIntegerConstantTerm(index),
                elementKind);
            return SymbolicIrFormulaEncoder.TryEncodeTerm(element, out formula);
        }

        private static void AddElementAssignmentFact(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapExpression(assignment.Left) is not ElementAccessExpressionSyntax elementAccess)
            {
                return;
            }

            var receiverSymbols = GetReferencedLocalAndParameterSymbols(elementAccess.Expression, semanticModel, cancellationToken);
            if (ExpressionReferencesAnySymbol(assignment.Right, receiverSymbols, semanticModel, cancellationToken) ||
                !TryCreateComparableValueFormula(
                    elementAccess,
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    out var targetFormula,
                    out var valueFormula))
            {
                return;
            }

            facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetFormula, valueFormula));
        }

        private static bool TryTranslateFiniteElementAccessValue(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ISymbol? assignedSymbol,
            out SmtFormula? formula)
        {
            formula = null;
            valueExpression = UnwrapExpression(valueExpression);
            if (valueExpression is not ElementAccessExpressionSyntax elementAccess ||
                elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            if (TryGetFiniteElementExpressions(elementAccess.Expression, out var elementExpressions))
            {
                if (!TryGetFiniteElementIndex(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    elementExpressions.Length,
                    semanticModel,
                    cancellationToken,
                    out var index))
                {
                    return false;
                }

                var elementExpression = elementExpressions[index];
                if (assignedSymbol != null &&
                    ExpressionReferencesSymbol(elementExpression, assignedSymbol, semanticModel, cancellationToken))
                {
                    return false;
                }

                return TryTranslateAssignedValueExpression(
                    elementExpression,
                    semanticModel,
                    cancellationToken,
                    assignedSymbol,
                    out formula);
            }

            var containingStatement = valueExpression.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
            if (containingStatement == null ||
                !TryGetPriorAssignedFiniteElementCount(
                    elementAccess.Expression,
                    containingStatement,
                    semanticModel,
                    cancellationToken,
                    out var elementCount) ||
                !TryGetFiniteElementIndex(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    elementCount,
                    semanticModel,
                    cancellationToken,
                    out var priorIndex) ||
                semanticModel.GetSymbolInfo(UnwrapExpression(elementAccess.Expression), cancellationToken).Symbol?.OriginalDefinition is not { } receiverSymbol ||
                receiverSymbol is not ILocalSymbol and not IParameterSymbol ||
                SymbolicFactFactory.GetTrackedSymbolType(receiverSymbol) is not IArrayTypeSymbol { Rank: 1 } arrayType ||
                !TryCreateArrayElementSmtValue(receiverSymbol, arrayType.ElementType, priorIndex, out formula))
            {
                formula = null;
                return false;
            }

            return true;
        }

        private static bool TryGetFiniteElementIndex(
            ExpressionSyntax indexExpression,
            int elementCount,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out int index)
        {
            indexExpression = UnwrapExpression(indexExpression);
            if (indexExpression is PrefixUnaryExpressionSyntax fromEndIndex &&
                fromEndIndex.OperatorToken.IsKind(SyntaxKind.CaretToken))
            {
                if (!TryGetIntegralConstant(fromEndIndex.Operand, semanticModel, cancellationToken, out var offset) ||
                    offset <= 0 ||
                    offset > elementCount)
                {
                    index = 0;
                    return false;
                }

                index = elementCount - (int)offset;
                return true;
            }

            if (!TryGetIntegralConstant(indexExpression, semanticModel, cancellationToken, out var ordinaryIndex) ||
                ordinaryIndex < 0 ||
                ordinaryIndex >= elementCount)
            {
                index = 0;
                return false;
            }

            index = (int)ordinaryIndex;
            return true;
        }

        private static void AddTupleElementAssignedValueFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            valueExpression = UnwrapExpression(valueExpression);
            if (valueExpression is not TupleExpressionSyntax tupleExpression ||
                !TryGetTupleElementStorageNames(assignedSymbol, tupleExpression.Arguments.Count, out var elementNames))
            {
                return;
            }

            for (var index = 0; index < tupleExpression.Arguments.Count; index++)
            {
                var argumentExpression = tupleExpression.Arguments[index].Expression;
                if (ExpressionReferencesSymbol(argumentExpression, assignedSymbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddTupleElementDerivedAssignedValueFacts(
                    assignedSymbol,
                    elementNames[index],
                    argumentExpression,
                    semanticModel,
                    cancellationToken,
                    facts);

                if (!TryCreateTupleElementSmtValue(assignedSymbol, elementNames[index], out var targetFormula) ||
                    !TryCreateComparableValueFormula(
                        argumentExpression,
                        targetFormula,
                        semanticModel,
                        cancellationToken,
                        out var valueFormula))
                {
                    continue;
                }

                facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetFormula, valueFormula));
            }
        }

        private static void AddTupleElementDerivedAssignedValueFacts(
            ISymbol tupleSymbol,
            string elementName,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (TryCreateTupleElementStringContentFormula(tupleSymbol, elementName, out var targetString) &&
                TryCreateStringValueFormula(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueString))
            {
                facts.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, targetString, valueString));
            }

            if (TryCreateTupleElementBuiltInLengthFormula(tupleSymbol, elementName, out var targetLength) &&
                TryCreateBuiltInLengthValueFormula(valueExpression, semanticModel, cancellationToken, out var valueLength))
            {
                AddUniqueFact(
                    facts,
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, targetLength, valueLength));
            }

            if (!TryGetTupleElementType(tupleSymbol, elementName, out var elementType) ||
                elementType is not IArrayTypeSymbol { Rank: > 1 } arrayType)
            {
                return;
            }

            for (var dimension = 0; dimension < arrayType.Rank; dimension++)
            {
                if (TryCreateTupleElementArrayDimensionLengthFormula(tupleSymbol, elementName, dimension, out var targetDimensionLength) &&
                    SymbolicReachabilityService.TryTranslateArrayDimensionLengthValue(
                        valueExpression,
                        dimension,
                        semanticModel,
                        cancellationToken,
                        out var valueDimensionLength,
                        getSymbolVersion: null))
                {
                    AddUniqueFact(
                        facts,
                        new SmtBinaryFormula(
                            SmtBinaryOperator.Equal,
                            targetDimensionLength,
                            valueDimensionLength));
                }
            }
        }

        private static bool TryGetTupleElementStorageNames(
            ISymbol assignedSymbol,
            int expectedCount,
            out string[] elementNames)
        {
            elementNames = Array.Empty<string>();
            var type = assignedSymbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type is not INamedTypeSymbol { IsTupleType: true } tupleType ||
                expectedCount > 0 &&
                tupleType.TupleElements.Length != expectedCount)
            {
                return false;
            }

            elementNames = new string[tupleType.TupleElements.Length];
            for (var index = 0; index < tupleType.TupleElements.Length; index++)
            {
                var field = tupleType.TupleElements[index].CorrespondingTupleField ?? tupleType.TupleElements[index];
                if (string.IsNullOrWhiteSpace(field.Name))
                {
                    return false;
                }

                elementNames[index] = field.Name;
            }

            return true;
        }

        private static bool TryCreateTupleElementSmtValue(
            ISymbol tupleSymbol,
            string elementName,
            out SmtFormula formula)
        {
            if (!TryGetTupleElementType(tupleSymbol, elementName, out var elementType))
            {
                formula = null!;
                return false;
            }

            var variableName = SymbolicFactFactory.GetSmtVariableName(tupleSymbol) + "." + elementName;
            if (elementType.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Bool);
                return true;
            }

            if (IsIntegralType(elementType))
            {
                formula = new SmtVariable(variableName, SmtValueKind.Int);
                return true;
            }

            if (elementType.IsReferenceType)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Reference);
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryGetTupleElementType(
            ISymbol tupleSymbol,
            string elementName,
            out ITypeSymbol elementType)
        {
            var type = SymbolicFactFactory.GetTrackedSymbolType(tupleSymbol);
            if (type is not INamedTypeSymbol { IsTupleType: true } tupleType)
            {
                elementType = null!;
                return false;
            }

            var element = tupleType.TupleElements
                .FirstOrDefault(field => string.Equals((field.CorrespondingTupleField ?? field).Name, elementName, StringComparison.Ordinal));
            if (element == null)
            {
                elementType = null!;
                return false;
            }

            elementType = element.Type;
            return true;
        }

        private static bool TryCreateTupleElementStringContentFormula(
            ISymbol tupleSymbol,
            string elementName,
            out SmtFormula formula)
        {
            if (!TryGetTupleElementType(tupleSymbol, elementName, out var elementType) ||
                elementType.SpecialType != SpecialType.System_String)
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(SymbolicFactFactory.GetSmtVariableName(tupleSymbol) + "." + elementName + ".String", SmtValueKind.String);
            return true;
        }

        private static bool TryCreateTupleElementBuiltInLengthFormula(
            ISymbol tupleSymbol,
            string elementName,
            out SmtFormula formula)
        {
            if (!TryGetTupleElementType(tupleSymbol, elementName, out var elementType))
            {
                formula = null!;
                return false;
            }

            var elementFormula = new SmtVariable(SymbolicFactFactory.GetSmtVariableName(tupleSymbol) + "." + elementName, SmtValueKind.Reference);
            return SymbolicFactFactory.TryCreateBuiltInLengthFormula(
                SymbolicFactFactory.GetReferenceFormulaName(elementFormula),
                elementType,
                out formula);
        }

        private static bool TryCreateTupleElementArrayDimensionLengthFormula(
            ISymbol tupleSymbol,
            string elementName,
            int dimension,
            out SmtFormula formula)
        {
            if (dimension < 0 ||
                !TryGetTupleElementType(tupleSymbol, elementName, out var elementType) ||
                elementType is not IArrayTypeSymbol arrayType ||
                dimension >= arrayType.Rank)
            {
                formula = null!;
                return false;
            }

            var elementFormula = new SmtVariable(SymbolicFactFactory.GetSmtVariableName(tupleSymbol) + "." + elementName, SmtValueKind.Reference);
            return SymbolicFactFactory.TryCreateArrayDimensionLengthFormulaForReference(
                elementFormula,
                arrayType,
                dimension,
                out formula);
        }

        private static void AddCoalesceAssignmentFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax rightExpression,
            SmtFormula? previousAssignedValue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!TryCreateSymbolSmtValue(assignedSymbol, out var targetFormula) ||
                targetFormula is not { Kind: SmtValueKind.Reference })
            {
                AddNullableCoalesceAssignmentFacts(assignedSymbol, rightExpression, semanticModel, cancellationToken, facts);
                return;
            }

            if (previousAssignedValue is SmtNullConstant)
            {
                AddAssignedValueFacts(assignedSymbol, rightExpression, semanticModel, cancellationToken, facts);
                return;
            }

            if (IsDefinitelyNonNullReferenceValue(rightExpression, semanticModel, cancellationToken))
            {
                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    targetFormula,
                    new SmtNullConstant()));
                return;
            }

            AddCoalesceAssignmentRightNullImplication(targetFormula, rightExpression, semanticModel, cancellationToken, facts);

            if (!TryCreateReferenceValueFormula(
                    rightExpression,
                    semanticModel,
                    cancellationToken,
                    out var rightFormula))
            {
                return;
            }

            var targetNonNull = new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                targetFormula,
                new SmtNullConstant());
            var targetEqualsRight = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                targetFormula,
                rightFormula);
            facts.Add(new SmtBinaryFormula(SmtBinaryOperator.Or, targetNonNull, targetEqualsRight));
        }

        private static void AddCoalesceAssignmentRightNullImplication(
            SmtFormula targetFormula,
            ExpressionSyntax rightExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!IsStructuralReferenceNullStateExpression(rightExpression) ||
                !TryCreateReferenceNullStateFormula(
                    rightExpression,
                    semanticModel,
                    cancellationToken,
                    depth: 0,
                    out var rightNullState))
            {
                return;
            }

            facts.Add(new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                CreateReferenceNonNullFormula(targetFormula),
                rightNullState));
        }

        private static void AddNullableCoalesceAssignmentFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax rightExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!TryCreateNullableHasValueFormula(assignedSymbol, out var targetHasValue) ||
                !TryGetNullableUnderlyingType(SymbolicFactFactory.GetTrackedSymbolType(assignedSymbol), out var underlyingType))
            {
                return;
            }

            if (SymbolicReachabilityService.TryTranslateNullableValueParts(
                    rightExpression,
                    semanticModel,
                    cancellationToken,
                    out var parts,
                    getSymbolVersion: null) &&
                parts.HasValue is SmtBooleanConstant { Value: true })
            {
                facts.Add(targetHasValue);
            }
            else if (TryTranslateNullableWrappedValueForUnderlyingType(
                         rightExpression,
                         underlyingType,
                         semanticModel,
                         cancellationToken,
                         out _))
            {
                facts.Add(targetHasValue);
            }
        }

        private static bool IsDefinitelyNonNullReferenceValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            var type = semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            if (type?.IsReferenceType != true)
            {
                return false;
            }

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue is { HasValue: true, Value: not null })
            {
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression)
            {
                return IsDefinitelyNonNullReferenceValue(conditionalExpression.WhenTrue, semanticModel, cancellationToken) &&
                    IsDefinitelyNonNullReferenceValue(conditionalExpression.WhenFalse, semanticModel, cancellationToken);
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression))
            {
                return IsDefinitelyNonNullReferenceValue(coalesceExpression.Left, semanticModel, cancellationToken) ||
                    IsDefinitelyNonNullReferenceValue(coalesceExpression.Right, semanticModel, cancellationToken);
            }

            return expression is ObjectCreationExpressionSyntax or
                AnonymousObjectCreationExpressionSyntax or
                ArrayCreationExpressionSyntax or
                ImplicitArrayCreationExpressionSyntax or
                CollectionExpressionSyntax or
                InterpolatedStringExpressionSyntax or
                TypeOfExpressionSyntax;
        }

        private static bool TryGetThrowGuardedValue(
            ExpressionSyntax valueExpression,
            out ExpressionSyntax effectiveValueExpression,
            out ExpressionSyntax? guardExpression,
            out bool guardBranchWhenTrue,
            out bool requiresNonNullValue)
        {
            valueExpression = UnwrapExpression(valueExpression);
            if (valueExpression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                UnwrapExpression(coalesceExpression.Right) is ThrowExpressionSyntax)
            {
                effectiveValueExpression = coalesceExpression.Left;
                guardExpression = null;
                guardBranchWhenTrue = true;
                requiresNonNullValue = true;
                return true;
            }

            if (valueExpression is ConditionalExpressionSyntax conditionalExpression)
            {
                if (UnwrapExpression(conditionalExpression.WhenFalse) is ThrowExpressionSyntax)
                {
                    effectiveValueExpression = conditionalExpression.WhenTrue;
                    guardExpression = conditionalExpression.Condition;
                    guardBranchWhenTrue = true;
                    requiresNonNullValue = false;
                    return true;
                }

                if (UnwrapExpression(conditionalExpression.WhenTrue) is ThrowExpressionSyntax)
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

        private static void AddThrowGuardedExpressionFacts(
            ExpressionSyntax expression,
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (!TryGetThrowGuardedValue(
                    expression,
                    out var effectiveValueExpression,
                    out var guardExpression,
                    out var guardBranchWhenTrue,
                    out var requiresNonNullValue))
            {
                return;
            }

            if (guardExpression != null)
            {
                if (!AnyConditionSymbolInvalidatedInStatement(guardExpression, statement, semanticModel, cancellationToken))
                {
                    AddBranchConditionFacts(
                        guardExpression,
                        guardBranchWhenTrue,
                        semanticModel,
                        cancellationToken,
                        facts);
                }

                return;
            }

            if (requiresNonNullValue &&
                !AnyConditionSymbolInvalidatedInStatement(effectiveValueExpression, statement, semanticModel, cancellationToken))
            {
                AddReferenceNonNullFact(effectiveValueExpression, semanticModel, cancellationToken, facts);
            }
        }

        private static void AddReferenceNonNullFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            AddReferenceNullCondition(facts, expression, isNull: false, semanticModel, cancellationToken);
        }

        private static bool TryCreateAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact)
        {
            return TryCreateAssignedValueFact(
                targetSymbol,
                valueExpression,
                semanticModel,
                cancellationToken,
                pathFacts: null,
                out fact);
        }

        private static bool TryCreateAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            IEnumerable<SmtFormula>? pathFacts,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula) ||
                !TryTranslateAssignedValueExpression(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    targetSymbol,
                    out var valueFormula,
                    pathFacts) ||
                valueFormula == null ||
                !SymbolicFactFactory.CanCompareSmtValues(targetFormula, valueFormula))
            {
                return false;
            }

            fact = SymbolicFactFactory.CreateAssignedValueFact(targetFormula, valueFormula);
            return true;
        }

        private static bool TryCreateAssignedValueNonZeroFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            IEnumerable<SmtFormula>? pathFacts,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula) ||
                targetFormula.Kind != SmtValueKind.Int ||
                !TryTranslateAssignedValueExpression(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    targetSymbol,
                    out var valueFormula,
                    pathFacts) ||
                valueFormula == null ||
                !ValueFormulaIsDefinitelyNonZero(valueFormula))
            {
                return false;
            }

            fact = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, targetFormula, new SmtIntegerConstant(0));
            return true;
        }

        private static bool ValueFormulaIsDefinitelyNonZero(SmtFormula formula)
        {
            return formula switch
            {
                SmtIntegerConstant integerConstant => integerConstant.Value != 0,
                SmtConditionalFormula { Kind: SmtValueKind.Int } conditional =>
                    ValueFormulaIsDefinitelyNonZero(conditional.WhenTrue) &&
                    ValueFormulaIsDefinitelyNonZero(conditional.WhenFalse),
                _ => false,
            };
        }

        private static bool TryCreateAssignedValueFact(
            ISymbol targetSymbol,
            SmtFormula valueFormula,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula) ||
                !SymbolicFactFactory.CanCompareSmtValues(targetFormula, valueFormula))
            {
                return false;
            }

            fact = SymbolicFactFactory.CreateAssignedValueFact(targetFormula, valueFormula);
            return true;
        }

        private static void AddConditionalAccessAssignedValueFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            valueExpression = UnwrapExpression(valueExpression);
            if (valueExpression is not ConditionalAccessExpressionSyntax conditionalAccess ||
                !TryCreateReferenceValueFormula(
                    conditionalAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula))
            {
                return;
            }

            var receiverNonNull = new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                receiverFormula,
                new SmtNullConstant());

            if (TryCreateNullableHasValueFormula(assignedSymbol, out var targetHasValue) &&
                TryCreateNullableValueFormula(assignedSymbol, out var targetValue) &&
                TryGetNullableUnderlyingType(SymbolicFactFactory.GetTrackedSymbolType(assignedSymbol), out var underlyingType) &&
                ConditionalAccessWhenNotNullHasType(
                    conditionalAccess,
                    underlyingType,
                    semanticModel,
                    cancellationToken))
            {
                facts.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, targetHasValue, receiverNonNull));
                if (TryCreateConditionalAccessWhenNotNullValueFormula(
                        conditionalAccess,
                        receiverFormula,
                        semanticModel,
                        cancellationToken,
                        out var whenNotNullValue) &&
                    SymbolicFactFactory.CanCompareSmtValues(targetValue, whenNotNullValue))
                {
                    facts.Add(new SmtBinaryFormula(
                        SmtBinaryOperator.Or,
                        new SmtUnaryFormula(SmtUnaryOperator.Not, targetHasValue),
                        new SmtBinaryFormula(SmtBinaryOperator.Equal, targetValue, whenNotNullValue)));
                }

                return;
            }

            if (TryCreateSymbolSmtValue(assignedSymbol, out var targetFormula) &&
                targetFormula is { Kind: SmtValueKind.Reference })
            {
                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.Or,
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, new SmtNullConstant()),
                    receiverNonNull));
            }
        }

        private static bool ConditionalAccessWhenNotNullHasType(
            ConditionalAccessExpressionSyntax conditionalAccess,
            ITypeSymbol expectedType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(conditionalAccess.WhenNotNull, cancellationToken);
            var actualType = typeInfo.ConvertedType ?? typeInfo.Type;
            return actualType != null &&
                SymbolEqualityComparer.Default.Equals(actualType, expectedType);
        }

        private static bool TryCreateConditionalAccessWhenNotNullValueFormula(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SmtFormula receiverFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            if (conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding &&
                semanticModel.GetSymbolInfo(memberBinding.Name, cancellationToken).Symbol is { } memberSymbol)
            {
                if (memberSymbol.Name == "Length" &&
                    IsStringExpression(conditionalAccess.Expression, semanticModel, cancellationToken) &&
                    TryCreateStringValueFormula(
                        conditionalAccess.Expression,
                        semanticModel,
                        cancellationToken,
                        out var stringFormula))
                {
                    formula = new SmtStringLengthTerm(stringFormula);
                    return true;
                }

                return TryCreateMemberSmtValue(receiverFormula, memberSymbol, out formula);
            }

            formula = null!;
            return false;
        }

        private static bool IsStringExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(UnwrapExpression(expression), cancellationToken);
            return (typeInfo.ConvertedType ?? typeInfo.Type)?.SpecialType == SpecialType.System_String;
        }

        private static bool TryCreateMemberSmtValue(SmtFormula receiverFormula, ISymbol memberSymbol, out SmtFormula formula)
        {
            var type = memberSymbol switch
            {
                IPropertySymbol propertySymbol => propertySymbol.Type,
                IFieldSymbol fieldSymbol => fieldSymbol.Type,
                _ => null
            };

            if (type == null ||
                !TryGetValueKind(type, out var kind))
            {
                formula = null!;
                return false;
            }

            if (SymbolicSmtFormulaLowerer.TryLowerTerm(receiverFormula, out var receiver) &&
                receiver.Kind == SmtValueKind.Reference &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(
                    new SymbolicMemberTerm(receiver, memberSymbol.Name, kind),
                    out formula))
            {
                return true;
            }

            formula = new SmtVariable(receiverFormula + "." + memberSymbol.Name, kind);
            return true;
        }

        private static void AddAsExpressionAssignedValueFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!SymbolicReachabilityService.TryCreateAsExpressionAssignedValueFacts(
                    assignedSymbol,
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var asFacts))
            {
                return;
            }

            foreach (var fact in asFacts)
            {
                AddUniqueFact(facts, fact);
            }
        }

        private static bool TryTranslateNullableWrappedValueForUnderlyingType(
            ExpressionSyntax valueExpression,
            ITypeSymbol underlyingType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula valueFormula)
        {
            valueExpression = UnwrapExpression(valueExpression);
            var typeInfo = semanticModel.GetTypeInfo(valueExpression, cancellationToken);
            if (!SymbolEqualityComparer.Default.Equals(typeInfo.ConvertedType, underlyingType) &&
                !SymbolEqualityComparer.Default.Equals(typeInfo.Type, underlyingType))
            {
                valueFormula = null!;
                return false;
            }

            if (TryCreateValueFormula(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var translatedValue))
            {
                valueFormula = translatedValue;
                return true;
            }

            valueFormula = null!;
            return false;
        }

        private static bool TryCreateNullableHasValueFormula(ISymbol symbol, out SmtFormula formula)
        {
            if (!TryGetNullableUnderlyingType(SymbolicFactFactory.GetTrackedSymbolType(symbol), out _))
            {
                formula = null!;
                return false;
            }

            return SymbolicIrFormulaEncoder.TryEncodeTerm(
                new SymbolicNullableHasValueTerm(SymbolicFactFactory.GetSmtVariableName(symbol)),
                out formula);
        }

        private static bool TryCreateNullableValueFormula(ISymbol symbol, out SmtFormula formula)
        {
            if (!TryGetNullableUnderlyingType(SymbolicFactFactory.GetTrackedSymbolType(symbol), out var underlyingType) ||
                !TryGetValueKind(underlyingType, out var kind))
            {
                formula = null!;
                return false;
            }

            return SymbolicIrFormulaEncoder.TryEncodeTerm(
                new SymbolicNullableValueTerm(SymbolicFactFactory.GetSmtVariableName(symbol), kind),
                out formula);
        }

        private static bool TryHandleTupleDeconstructionDeclaration(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapExpression(assignment.Left) is not DeclarationExpressionSyntax declarationExpression ||
                declarationExpression.Designation is not ParenthesizedVariableDesignationSyntax leftDesignation)
            {
                return false;
            }

            var targetSymbols = new List<ISymbol?>();
            foreach (var variableDesignation in leftDesignation.Variables)
            {
                if (variableDesignation is not SingleVariableDesignationSyntax singleVariableDesignation)
                {
                    return true;
                }

                if (singleVariableDesignation.Identifier.ValueText == "_")
                {
                    targetSymbols.Add(null);
                    continue;
                }

                if (semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is not ILocalSymbol localSymbol)
                {
                    return true;
                }

                targetSymbols.Add(localSymbol.OriginalDefinition);
            }

            var nonDiscardTargets = targetSymbols.Where(static symbol => symbol != null).Cast<ISymbol>().ToArray();
            if (ExpressionReferencesAnySymbol(assignment.Right, nonDiscardTargets, semanticModel, cancellationToken))
            {
                return true;
            }

            AddTupleElementTargetFacts(
                targetSymbols,
                assignment.Right,
                semanticModel,
                cancellationToken,
                facts);
            return true;
        }

        private static bool TryHandleTupleAssignment(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapExpression(assignment.Left) is not TupleExpressionSyntax leftTuple)
            {
                return false;
            }

            var targetSymbols = new List<ISymbol?>();
            foreach (var argument in leftTuple.Arguments)
            {
                if (argument.Expression is IdentifierNameSyntax identifier &&
                    identifier.Identifier.ValueText == "_")
                {
                    targetSymbols.Add(null);
                    continue;
                }

                var targetSymbol = semanticModel.GetSymbolInfo(argument.Expression, cancellationToken).Symbol;
                if (targetSymbol is ILocalSymbol or IParameterSymbol)
                {
                    targetSymbols.Add(targetSymbol.OriginalDefinition);
                    continue;
                }

                return true;
            }

            foreach (var targetSymbol in targetSymbols)
            {
                if (targetSymbol != null)
                {
                    RemoveFactsReferencingSymbol(facts, targetSymbol);
                }
            }

            var nonDiscardTargets = targetSymbols.Where(static symbol => symbol != null).Cast<ISymbol>().ToArray();
            if (targetSymbols.All(static symbol => symbol == null) ||
                ExpressionReferencesAnySymbol(assignment.Right, nonDiscardTargets, semanticModel, cancellationToken))
            {
                return true;
            }

            AddTupleElementTargetFacts(
                targetSymbols,
                assignment.Right,
                semanticModel,
                cancellationToken,
                facts);
            return true;
        }

        private static void AddTupleElementTargetFacts(
            IReadOnlyList<ISymbol?> targetSymbols,
            ExpressionSyntax rightExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            rightExpression = UnwrapExpression(rightExpression);
            if (rightExpression is TupleExpressionSyntax rightTuple)
            {
                if (rightTuple.Arguments.Count != targetSymbols.Count)
                {
                    return;
                }

                for (var index = 0; index < targetSymbols.Count; index++)
                {
                    if (targetSymbols[index] == null)
                    {
                        continue;
                    }

                    AddAssignedValueFacts(
                        targetSymbols[index]!,
                        rightTuple.Arguments[index].Expression,
                        semanticModel,
                        cancellationToken,
                        facts);
                }

                return;
            }

            if (!TryGetTupleElementValueFormulas(
                    rightExpression,
                    targetSymbols.Count,
                    semanticModel,
                    cancellationToken,
                    out var valueFormulas))
            {
                return;
            }

            for (var index = 0; index < targetSymbols.Count; index++)
            {
                if (targetSymbols[index] == null)
                {
                    continue;
                }

                if (TryGetCurrentFormulaValue(facts, valueFormulas[index], out var currentValue) &&
                    TryCreateAssignedValueFact(targetSymbols[index]!, currentValue, out var currentValueFact))
                {
                    facts.Add(currentValueFact);
                }

                if (TryCreateAssignedValueFact(targetSymbols[index]!, valueFormulas[index], out var fact))
                {
                    facts.Add(fact);
                }

                AddTupleElementDerivedTargetFacts(targetSymbols[index]!, valueFormulas[index], facts);
            }
        }

        private static void AddTupleElementDerivedTargetFacts(
            ISymbol targetSymbol,
            SmtFormula sourceFormula,
            List<SmtFormula> facts)
        {
            if (TryCreateBuiltInLengthFormulaForReference(sourceFormula, SymbolicFactFactory.GetTrackedSymbolType(targetSymbol), out var sourceLength) &&
                TryCreateBuiltInLengthFormula(targetSymbol, out var targetLength))
            {
                AddSubstitutedCurrentFacts(facts, sourceLength, targetLength);
            }

            if (TryCreateStringContentFormulaForReference(sourceFormula, SymbolicFactFactory.GetTrackedSymbolType(targetSymbol), out var sourceString) &&
                TryCreateStringContentFormula(targetSymbol, out var targetString))
            {
                AddSubstitutedCurrentFacts(facts, sourceString, targetString);
            }

            if (SymbolicFactFactory.GetTrackedSymbolType(targetSymbol) is not IArrayTypeSymbol { Rank: > 1 } arrayType)
            {
                return;
            }

            for (var dimension = 0; dimension < arrayType.Rank; dimension++)
            {
                if (TryCreateArrayDimensionLengthFormulaForReference(sourceFormula, arrayType, dimension, out var sourceDimensionLength) &&
                    TryCreateArrayDimensionLengthFormula(targetSymbol, dimension, out var targetDimensionLength))
                {
                    AddSubstitutedCurrentFacts(facts, sourceDimensionLength, targetDimensionLength);
                }
            }
        }

        private static bool TryGetTupleElementValueFormulas(
            ExpressionSyntax expression,
            int expectedCount,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ImmutableArray<SmtFormula> valueFormulas)
        {
            valueFormulas = ImmutableArray<SmtFormula>.Empty;
            var receiverSymbol = semanticModel.GetSymbolInfo(UnwrapExpression(expression), cancellationToken).Symbol?.OriginalDefinition;
            if (receiverSymbol is not ILocalSymbol and not IParameterSymbol ||
                !TryGetTupleElementStorageNames(receiverSymbol, expectedCount, out var elementNames))
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<SmtFormula>(expectedCount);
            foreach (var elementName in elementNames)
            {
                if (!TryCreateTupleElementSmtValue(receiverSymbol, elementName, out var elementFormula))
                {
                    valueFormulas = ImmutableArray<SmtFormula>.Empty;
                    return false;
                }

                builder.Add(elementFormula);
            }

            valueFormulas = builder.ToImmutable();
            return true;
        }

        private static bool TryCreateIncrementOrDecrementFact(
            ISymbol targetSymbol,
            SmtFormula previousValue,
            int delta,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula))
            {
                return false;
            }

            return SymbolicMutationFactFactory.TryCreateIncrementOrDecrementFact(
                targetFormula,
                previousValue,
                SmtFormulaReferenceScanner.ContainsVariablePrefix(
                    previousValue,
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol)),
                delta,
                out fact);
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

            return TryGetCurrentFormulaValue(facts, targetFormula, out value);
        }

        private static bool TryGetCurrentFormulaValue(
            List<SmtFormula> facts,
            SmtFormula targetFormula,
            out SmtFormula value)
        {
            value = null!;
            for (var index = facts.Count - 1; index >= 0; index--)
            {
                if (targetFormula.Kind == SmtValueKind.Bool)
                {
                    if (Equals(facts[index], targetFormula))
                    {
                        value = new SmtBooleanConstant(true);
                        return true;
                    }

                    if (facts[index] is SmtUnaryFormula
                        {
                            Operator: SmtUnaryOperator.Not,
                            Operand: var operand
                        } &&
                        Equals(operand, targetFormula))
                    {
                        value = new SmtBooleanConstant(false);
                        return true;
                    }
                }

                if (facts[index] is SmtUnaryFormula
                    {
                        Operator: SmtUnaryOperator.Not,
                        Operand: SmtBinaryFormula
                        {
                            Operator: SmtBinaryOperator.NotEqual,
                            Left: var notEqualLeft,
                            Right: var notEqualRight
                        }
                    })
                {
                    if (Equals(notEqualLeft, targetFormula) && notEqualRight.Kind == targetFormula.Kind)
                    {
                        value = notEqualRight;
                        return true;
                    }

                    if (Equals(notEqualRight, targetFormula) && notEqualLeft.Kind == targetFormula.Kind)
                    {
                        value = notEqualLeft;
                        return true;
                    }
                }

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

        private static bool IsKnownNonNullReferenceSymbol(List<SmtFormula> facts, ISymbol symbol)
        {
            if (!TryCreateSymbolSmtValue(symbol, out var targetFormula) ||
                targetFormula is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            for (var index = facts.Count - 1; index >= 0; index--)
            {
                if (facts[index] is SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.NotEqual,
                        Left: var notEqualLeft,
                        Right: var notEqualRight
                    } &&
                    IsFormulaPair(targetFormula, new SmtNullConstant(), notEqualLeft, notEqualRight))
                {
                    return true;
                }

                if (facts[index] is SmtUnaryFormula
                    {
                        Operator: SmtUnaryOperator.Not,
                        Operand: SmtBinaryFormula
                        {
                            Operator: SmtBinaryOperator.Equal,
                            Left: var equalLeft,
                            Right: var equalRight
                        }
                    } &&
                    IsFormulaPair(targetFormula, new SmtNullConstant(), equalLeft, equalRight))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsKnownNullableHasValueSymbol(List<SmtFormula> facts, ISymbol symbol)
        {
            return TryGetKnownNullableHasValueState(facts, symbol, out var hasValue) && hasValue;
        }

        private static bool IsKnownNullableNoValueSymbol(List<SmtFormula> facts, ISymbol symbol)
        {
            return TryGetKnownNullableHasValueState(facts, symbol, out var hasValue) && !hasValue;
        }

        private static bool TryGetKnownNullableHasValueState(List<SmtFormula> facts, ISymbol symbol, out bool hasValue)
        {
            hasValue = false;
            if (!TryCreateNullableHasValueFormula(symbol, out var targetHasValue))
            {
                return false;
            }

            for (var index = facts.Count - 1; index >= 0; index--)
            {
                if (Equals(facts[index], targetHasValue))
                {
                    hasValue = true;
                    return true;
                }

                if (facts[index] is SmtUnaryFormula
                    {
                        Operator: SmtUnaryOperator.Not,
                        Operand: var operand
                    } &&
                    Equals(operand, targetHasValue))
                {
                    hasValue = false;
                    return true;
                }

                if (facts[index] is SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.Equal,
                        Left: var equalLeft,
                        Right: var equalRight
                    })
                {
                    if (Equals(equalLeft, targetHasValue) && equalRight is SmtBooleanConstant rightConstant)
                    {
                        hasValue = rightConstant.Value;
                        return true;
                    }

                    if (Equals(equalRight, targetHasValue) && equalLeft is SmtBooleanConstant leftConstant)
                    {
                        hasValue = leftConstant.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsFormulaPair(SmtFormula expectedLeft, SmtFormula expectedRight, SmtFormula actualLeft, SmtFormula actualRight)
        {
            return Equals(actualLeft, expectedLeft) && Equals(actualRight, expectedRight) ||
                Equals(actualLeft, expectedRight) && Equals(actualRight, expectedLeft);
        }

        private static bool TryGetIncrementedOrDecrementedSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ISymbol symbol,
            out int delta)
        {
            expression = UnwrapExpression(expression);
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

        private static bool TryCreateArrayDimensionLengthFormula(
            ISymbol symbol,
            int dimension,
            out SmtFormula formula)
        {
            if (dimension < 0 ||
                SymbolicFactFactory.GetTrackedSymbolType(symbol) is not IArrayTypeSymbol arrayType)
            {
                formula = null!;
                return false;
            }

            return SymbolicFactFactory.TryCreateArrayDimensionLengthFormula(
                SymbolicFactFactory.GetSmtVariableName(symbol),
                arrayType,
                dimension,
                out formula);
        }

        private static bool TryCreateBuiltInLengthFormula(ISymbol symbol, out SmtFormula formula)
        {
            var type = symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (TryCreateSymbolTerm(symbol, out var reference) &&
                reference.Kind == SmtValueKind.Reference &&
                TryCreateBuiltInLengthTerm(reference, type, out var lengthTerm) &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(lengthTerm, out formula))
            {
                return true;
            }

            return SymbolicFactFactory.TryCreateBuiltInLengthFormula(SymbolicFactFactory.GetSmtVariableName(symbol), type, out formula);
        }

        private static bool TryCreateBuiltInLengthFormulaForReference(
            SmtFormula receiverFormula,
            ITypeSymbol? type,
            out SmtFormula formula)
        {
            if (SymbolicSmtFormulaLowerer.TryLowerTerm(receiverFormula, out var receiver) &&
                receiver.Kind == SmtValueKind.Reference &&
                TryCreateBuiltInLengthTerm(receiver, type, out var lengthTerm) &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(lengthTerm, out formula))
            {
                return true;
            }

            return SymbolicFactFactory.TryCreateBuiltInLengthFormulaForReference(receiverFormula, type, out formula);
        }

        private static bool TryCreateBuiltInLengthTerm(
            SymbolicTerm receiver,
            ITypeSymbol? type,
            out SymbolicTerm term)
        {
            if (type?.SpecialType == SpecialType.System_String)
            {
                term = new SymbolicLengthTerm(new SymbolicStringContentTerm(receiver));
                return true;
            }

            if (type is IArrayTypeSymbol { Rank: 1 } ||
                SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(type))
            {
                term = new SymbolicLengthTerm(receiver);
                return true;
            }

            term = null!;
            return false;
        }

        private static bool TryCreateArrayDimensionLengthFormulaForReference(
            SmtFormula receiverFormula,
            IArrayTypeSymbol arrayType,
            int dimension,
            out SmtFormula formula)
        {
            return SymbolicFactFactory.TryCreateArrayDimensionLengthFormulaForReference(
                receiverFormula,
                arrayType,
                dimension,
                out formula);
        }

        private static bool TryCreateBuiltInLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            return SymbolicReachabilityService.TryTranslateBuiltInLengthValue(
                valueExpression,
                semanticModel,
                cancellationToken,
                out formula);
        }

        private static bool TryCreateStringValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            return SymbolicReachabilityService.TryTranslateStringValue(
                valueExpression,
                semanticModel,
                cancellationToken,
                out formula);
        }

        private static bool TryCreateComparableValueFormula(
            ExpressionSyntax targetExpression,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula targetFormula,
            out SmtFormula valueFormula)
        {
            targetFormula = null!;
            valueFormula = null!;
            if (!TryCreateValueFormula(targetExpression, semanticModel, cancellationToken, out targetFormula) ||
                !TryTranslateAssignedValueExpression(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    assignedSymbol: null,
                    out var translatedValue) ||
                translatedValue == null ||
                !SymbolicFactFactory.CanCompareSmtValues(targetFormula, translatedValue))
            {
                return false;
            }

            valueFormula = translatedValue;
            return true;
        }

        private static bool TryCreateComparableValueFormula(
            ExpressionSyntax valueExpression,
            SmtFormula targetFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula valueFormula)
        {
            valueFormula = null!;
            if (!TryCreateValueFormula(valueExpression, semanticModel, cancellationToken, out var translatedValue) ||
                !SymbolicFactFactory.CanCompareSmtValues(targetFormula, translatedValue))
            {
                return false;
            }

            valueFormula = translatedValue;
            return true;
        }

        private static bool TryCreateValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            return SymbolicReachabilityService.TryTranslateValue(
                valueExpression,
                semanticModel,
                cancellationToken,
                out formula);
        }

        private static bool TryCreateConditionFormula(
            ExpressionSyntax condition,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            if (SymbolicReachabilityService.TryTranslateConditionFormula(
                    condition,
                    semanticModel,
                    cancellationToken,
                    out var translatedFormula) &&
                translatedFormula != null)
            {
                formula = translatedFormula;
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCollectBranchAssumptionFacts(
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            var branchFacts = new List<SmtFormula>();
            if (!SymbolicReachabilityService.TryAddBranchConditionFacts(
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                branchFacts))
            {
                return false;
            }

            var added = false;
            foreach (var branchFact in branchFacts)
            {
                if (facts.Contains(branchFact))
                {
                    continue;
                }

                facts.Add(branchFact);
                added = true;
            }

            return added;
        }

        private static bool TryCreateIntegerValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            if (TryCreateValueFormula(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var translatedFormula) &&
                translatedFormula is { Kind: SmtValueKind.Int })
            {
                formula = translatedFormula;
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateReferenceValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            if (TryCreateValueFormula(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var translatedFormula) &&
                translatedFormula is { Kind: SmtValueKind.Reference })
            {
                formula = translatedFormula;
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateSymbolSmtValue(ISymbol symbol, out SmtFormula formula)
        {
            return SymbolicFactFactory.TryCreateSymbolVariableFormula(
                SymbolicFactFactory.GetSmtVariableName(symbol),
                SymbolicFactFactory.GetTrackedSymbolType(symbol),
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                IsReferenceLikeType,
                out formula);
        }

        private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
        {
            return SymbolicFactFactory.TryGetValueKind(
                type,
                IsIntegralOrEnumType,
                IsReferenceLikeType,
                out kind);
        }

        private static bool IsReferenceLikeType(ITypeSymbol type)
        {
            return type.TypeKind == TypeKind.Dynamic ||
                type.IsReferenceType;
        }

        private static bool TryGetNullableUnderlyingType(ITypeSymbol? type, out ITypeSymbol underlyingType)
        {
            return SymbolicTypeFacts.TryGetNullableUnderlyingType(type, out underlyingType);
        }

        private static void RemoveFactsReferencingSymbol(List<SmtFormula> facts, ISymbol symbol)
        {
            SmtFormulaReferenceScanner.RemoveFactsReferencingSymbol(facts, symbol);
        }

        private static void RemoveFactsReferencingImplicitThisMember(List<SmtFormula> facts, string memberName)
        {
            var variableName = ImplicitThisVariableName + "." + memberName;
            for (var index = facts.Count - 1; index >= 0; index--)
            {
                if (SmtFormulaReferenceScanner.ContainsVariableOrMember(facts[index], variableName))
                {
                    facts.RemoveAt(index);
                }
            }
        }

        private static bool ExpressionReferencesSymbol(
            SyntaxNode root,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (node is not ExpressionSyntax expression)
                {
                    continue;
                }

                var expressionSymbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
                if (expressionSymbol != null &&
                    SymbolEqualityComparer.Default.Equals(expressionSymbol.OriginalDefinition, symbol))
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
            CancellationToken cancellationToken)
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

        private static bool TryGetIntegralConstant(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out long value)
        {
            var constantValue = semanticModel.GetConstantValue(UnwrapExpression(expression), cancellationToken);
            if (!constantValue.HasValue || constantValue.Value == null)
            {
                value = 0;
                return false;
            }

            switch (constantValue.Value)
            {
                case sbyte sbyteValue:
                    value = sbyteValue;
                    return true;
                case byte byteValue:
                    value = byteValue;
                    return true;
                case short shortValue:
                    value = shortValue;
                    return true;
                case ushort ushortValue:
                    value = ushortValue;
                    return true;
                case int intValue:
                    value = intValue;
                    return true;
                case uint uintValue:
                    value = uintValue;
                    return true;
                case long longValue:
                    value = longValue;
                    return true;
                case ulong ulongValue when ulongValue <= long.MaxValue:
                    value = (long)ulongValue;
                    return true;
                case char charValue:
                    value = charValue;
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        }

        private static bool IsIntegralType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64;
        }

        private static bool IsIntegralOrEnumType(ITypeSymbol typeSymbol)
        {
            return SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType(typeSymbol);
        }
    }
}
