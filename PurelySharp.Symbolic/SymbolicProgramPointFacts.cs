using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    public static class SymbolicProgramPointFacts
    {
        private const int MaxMergedIfElseFacts = 16;
        private const int MaxMergedSwitchFacts = 32;
        private const int MaxFiniteForeachElementFacts = 8;

        public static List<SmtFormula> CollectPriorAssignmentFacts(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var facts = new List<SmtFormula>();
            foreach (var containingBlock in EnumerateContainingBlocks(site).Reverse())
            {
                if (IsLoopBodyBlock(containingBlock.Block))
                {
                    RemoveFactsInvalidatedByNestedMutations(containingBlock.Block, semanticModel, cancellationToken, facts);
                }

                foreach (var statement in containingBlock.Block.Statements)
                {
                    if (ReferenceEquals(statement, containingBlock.ContainingStatement))
                    {
                        break;
                    }

                    AddPriorStatementFacts(statement, semanticModel, cancellationToken, facts);
                }
            }

            return facts;
        }

        public static ImmutableArray<SmtFormula> CollectAncestorReachabilityConditions(
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
                         usingStatementSyntax.Declaration != null &&
                         usingStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    AddUsingStatementDeclarationFacts(
                        builder,
                        usingStatementSyntax,
                        semanticModel,
                        cancellationToken);
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
                }
                else if (ancestor is ForStatementSyntax forStatementSyntax &&
                         forStatementSyntax.Condition != null &&
                         forStatementSyntax.Statement.Span.Contains(syntaxNode.Span) &&
                         !AnyReferencedSymbolAssignedBeforeUse(
                             forStatementSyntax.Condition,
                             forStatementSyntax.Statement,
                             syntaxNode.SpanStart,
                             semanticModel,
                             cancellationToken))
                {
                    AddReachabilityCondition(builder, forStatementSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    builder.AddRange(CollectForLoopBodyInvariantFacts(forStatementSyntax, semanticModel, cancellationToken));
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
                        semanticModel,
                        cancellationToken);
                }
                else if (ancestor is SwitchStatementSyntax switchStatementSyntax)
                {
                    var matchingSection = switchStatementSyntax.Sections
                        .FirstOrDefault(section => section.Span.Contains(syntaxNode.SpanStart));
                    if (matchingSection != null &&
                        !AnyReferencedSymbolAssignedBeforeUse(
                            switchStatementSyntax.Expression,
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
                        !AnyReferencedSymbolAssignedBeforeUse(
                            switchExpressionSyntax.GoverningExpression,
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

        public static IEnumerable<SmtFormula> CollectForInitializerFacts(
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

        public static ImmutableArray<SmtFormula> CollectForLoopBodyInvariantFacts(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var builder = ImmutableArray.CreateBuilder<SmtFormula>();
            AddForLoopMonotonicLowerBoundFacts(builder, forStatement, semanticModel, cancellationToken);
            AddForLoopMonotonicUpperBoundFacts(builder, forStatement, semanticModel, cancellationToken);
            return builder.ToImmutable();
        }

        public static ImmutableArray<SmtFormula> CollectCompletedLoopExitInvariantFacts(
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
            foreach (var initializer in EnumerateForLoopConstantInitializers(forStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolSmtValue(initializer.Symbol, out var symbolFormula) ||
                    symbolFormula.Kind != SmtValueKind.Int ||
                    StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
                    !ForLoopIncrementorsPreserveLowerBound(forStatement, initializer.Symbol, semanticModel, cancellationToken))
                {
                    continue;
                }

                facts.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    symbolFormula,
                    new SmtIntegerConstant(initializer.InitialValue)));
            }
        }

        private static IEnumerable<(ISymbol Symbol, long InitialValue)> EnumerateForLoopConstantInitializers(
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
                        !TryGetIntegralConstant(declarator.Initializer.Value, semanticModel, cancellationToken, out var initialValue))
                    {
                        continue;
                    }

                    yield return (localSymbol.OriginalDefinition, initialValue);
                }
            }

            foreach (var expression in forStatement.Initializers)
            {
                if (expression is not AssignmentExpressionSyntax assignment ||
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } symbol ||
                    symbol is not ILocalSymbol and not IParameterSymbol ||
                    !TryGetIntegralConstant(assignment.Right, semanticModel, cancellationToken, out var initialValue))
                {
                    continue;
                }

                yield return (symbol.OriginalDefinition, initialValue);
            }
        }

        private static void AddForLoopMonotonicUpperBoundFacts(
            ICollection<SmtFormula> facts,
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var initializer in EnumerateForLoopStrictUpperBoundInitializers(forStatement, semanticModel, cancellationToken))
            {
                if (!TryCreateSymbolSmtValue(initializer.Symbol, out var symbolFormula) ||
                    symbolFormula.Kind != SmtValueKind.Int ||
                    initializer.UpperBound.Kind != SmtValueKind.Int ||
                    StatementMutatesSymbol(forStatement.Statement, initializer.Symbol, semanticModel, cancellationToken) ||
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

        private static IEnumerable<(ISymbol Symbol, SmtFormula UpperBound)> EnumerateForLoopStrictUpperBoundInitializers(
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
                        !TryGetStrictUpperBoundInitializer(declarator.Initializer.Value, localSymbol.OriginalDefinition, semanticModel, cancellationToken, out var upperBound))
                    {
                        continue;
                    }

                    yield return (localSymbol.OriginalDefinition, upperBound);
                }
            }

            foreach (var expression in forStatement.Initializers)
            {
                if (expression is not AssignmentExpressionSyntax assignment ||
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } symbol ||
                    symbol is not ILocalSymbol and not IParameterSymbol ||
                    !TryGetStrictUpperBoundInitializer(assignment.Right, symbol.OriginalDefinition, semanticModel, cancellationToken, out var upperBound))
                {
                    continue;
                }

                yield return (symbol.OriginalDefinition, upperBound);
            }
        }

        private static bool TryGetStrictUpperBoundInitializer(
            ExpressionSyntax expression,
            ISymbol initializedSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula upperBound)
        {
            expression = UnwrapExpression(expression);
            if (expression is not BinaryExpressionSyntax binaryExpression)
            {
                upperBound = null!;
                return false;
            }

            if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var subtractedValue) &&
                subtractedValue > 0 &&
                TryTranslateInitializerBound(binaryExpression.Left, initializedSymbol, semanticModel, cancellationToken, out upperBound))
            {
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.AddExpression))
            {
                if (TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue) &&
                    rightValue < 0 &&
                    TryTranslateInitializerBound(binaryExpression.Left, initializedSymbol, semanticModel, cancellationToken, out upperBound))
                {
                    return true;
                }

                if (TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue) &&
                    leftValue < 0 &&
                    TryTranslateInitializerBound(binaryExpression.Right, initializedSymbol, semanticModel, cancellationToken, out upperBound))
                {
                    return true;
                }
            }

            upperBound = null!;
            return false;
        }

        private static bool TryTranslateInitializerBound(
            ExpressionSyntax expression,
            ISymbol initializedSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula upperBound)
        {
            if (ExpressionReferencesSymbol(expression, initializedSymbol, semanticModel, cancellationToken) ||
                !CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var candidate,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                candidate is not { Kind: SmtValueKind.Int })
            {
                upperBound = null!;
                return false;
            }

            upperBound = candidate;
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
            var conditionSymbols = GetReferencedLocalAndParameterSymbols(condition, semanticModel, cancellationToken);
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
            var referencedSymbols = GetReferencedLocalAndParameterSymbols(condition, semanticModel, cancellationToken);
            if (referencedSymbols.Count == 0)
            {
                return false;
            }

            foreach (var symbol in referencedSymbols)
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
                SmtFormula? previousAssignedValue = null;
                if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                {
                    TryGetCurrentSymbolValue(facts, assignedSymbol.OriginalDefinition, out previousAssignedValue);
                }

                RemoveFactsInvalidatedByNestedMutations(assignment.Left, semanticModel, cancellationToken, facts);
                RemoveFactsInvalidatedByNestedMutations(assignment.Right, semanticModel, cancellationToken, facts);

                if (assignedSymbol is ILocalSymbol or IParameterSymbol)
                {
                    var originalAssignedSymbol = assignedSymbol.OriginalDefinition;
                    RemoveFactsReferencingSymbol(facts, originalAssignedSymbol);
                    if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        AddAssignedValueFacts(originalAssignedSymbol, assignment.Right, semanticModel, cancellationToken, facts);
                    }
                    else if (previousAssignedValue != null &&
                             TryCreateCompoundAssignmentFact(
                                 originalAssignedSymbol,
                                 previousAssignedValue,
                                 assignment,
                                 semanticModel,
                                 cancellationToken,
                                 out var compoundAssignmentFact))
                    {
                        facts.Add(compoundAssignmentFact);
                    }
                }

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
            if (statement is IfStatementSyntax ifStatement)
            {
                AddCompletedIfStatementFacts(ifStatement, factsBeforeStatement, semanticModel, cancellationToken, facts);
            }
            else if (statement is SwitchStatementSyntax switchStatement)
            {
                AddCompletedSwitchStatementFacts(switchStatement, factsBeforeStatement, semanticModel, cancellationToken, facts);
            }
            else
            {
                AddCompletedLoopStatementFacts(statement, semanticModel, cancellationToken, facts);
            }
        }

        private static void AddCompletedIfStatementFacts(
            IfStatementSyntax ifStatement,
            IReadOnlyList<SmtFormula> factsBeforeStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (StatementDefinitelyExits(ifStatement.Statement) &&
                (ifStatement.Else?.Statement == null ||
                 !AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Else.Statement, semanticModel, cancellationToken)))
            {
                AddBranchConditionFacts(
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
                AddBranchConditionFacts(
                    ifStatement.Condition,
                    branchWhenTrue: true,
                    semanticModel,
                    cancellationToken,
                    facts);
            }

            AddCompletedIfElseMergedFacts(ifStatement, semanticModel, cancellationToken, facts);
            AddCompletedIfImplicitElseMergedFacts(ifStatement, factsBeforeStatement, semanticModel, cancellationToken, facts);
        }

        private static void AddCompletedIfImplicitElseMergedFacts(
            IfStatementSyntax ifStatement,
            IReadOnlyList<SmtFormula> factsBeforeStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (ifStatement.Else != null ||
                StatementDefinitelyExits(ifStatement.Statement))
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

            if (AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Statement, semanticModel, cancellationToken) ||
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
                StatementDefinitelyExits(ifStatement.Statement) ||
                StatementDefinitelyExits(elseStatement))
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

            if (AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Statement, semanticModel, cancellationToken) ||
                AnyConditionSymbolMutatedInStatement(ifStatement.Condition, elseStatement, semanticModel, cancellationToken) ||
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
            if (!CSharpConditionToFormula.TryCollectBranchAssumptions(
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
            public MergeableBranchFact(SmtFormula formula)
            {
                Formula = formula;
                FactKey = GetFormulaKey(formula);
                TargetKey = TryGetMergeTargetKey(formula, out var targetKey)
                    ? targetKey
                    : string.Empty;
            }

            public SmtFormula Formula { get; }

            public string FactKey { get; }

            public string TargetKey { get; }

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
            if (!switchStatement.Sections.Any(static section => section.Labels.Any(static label => label is DefaultSwitchLabelSyntax)))
            {
                return;
            }

            var branches = new List<SwitchBranchFacts>();
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

                var sectionFacts = new List<SmtFormula>(factsBeforeStatement);
                sectionFacts.Add(sectionCondition);
                foreach (var statement in section.Statements)
                {
                    if (statement is BreakStatementSyntax breakStatement &&
                        BreakTargetsSwitch(breakStatement, switchStatement))
                    {
                        break;
                    }

                    AddPriorStatementFacts(statement, semanticModel, cancellationToken, sectionFacts);
                }

                branches.Add(new SwitchBranchFacts(sectionCondition, sectionFacts));
            }

            if (branches.Count == 0)
            {
                return;
            }

            AddIdenticalSwitchBranchFacts(branches, facts);
            AddConditionalSwitchBranchFacts(branches, facts.ToArray(), facts);
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
            public SwitchBranchFacts(SmtFormula condition, IReadOnlyList<SmtFormula> facts)
            {
                Condition = condition;
                Facts = facts;
            }

            public SmtFormula Condition { get; }

            public IReadOnlyList<SmtFormula> Facts { get; }
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
                    break;
                case ForStatementSyntax { Condition: { } condition } forStatement
                    when CanAssumeLoopConditionFalseAfterNormalExit(forStatement, forStatement.Statement):
                    AddBranchConditionFacts(
                        condition,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        facts);
                    break;
                case DoStatementSyntax doStatement
                    when CanAssumeLoopConditionFalseAfterNormalExit(doStatement, doStatement.Statement):
                    AddBranchConditionFacts(
                        doStatement.Condition,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        facts);
                    break;
            }
        }

        private static bool CanAssumeLoopConditionFalseAfterNormalExit(
            StatementSyntax loopStatement,
            StatementSyntax loopBody)
        {
            if (loopBody.DescendantNodesAndSelf(
                    descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                .OfType<GotoStatementSyntax>()
                .Any())
            {
                return false;
            }

            return !loopBody.DescendantNodesAndSelf(
                    descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate))
                .OfType<BreakStatementSyntax>()
                .Any(breakStatement => BreakTargetsLoop(breakStatement, loopStatement));
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
            CSharpConditionToFormula.TryCollectBranchAssumptions(
                expressionSyntax,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                facts);
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

        private static void AddForeachBodyEntryFacts(
            ICollection<SmtFormula> facts,
            ExpressionSyntax expressionSyntax,
            ILocalSymbol? iterationSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddReferenceNullCondition(facts, expressionSyntax, isNull: false, semanticModel, cancellationToken);
            AddFiniteForeachIterationFact(facts, expressionSyntax, iterationSymbol, semanticModel, cancellationToken);

            var typeInfo = semanticModel.GetTypeInfo(expressionSyntax, cancellationToken);
            if (!IsSupportedForeachLengthReceiver(expressionSyntax) &&
                !IsSupportedForeachLengthReceiver(typeInfo.Type) &&
                !IsSupportedForeachLengthReceiver(typeInfo.ConvertedType))
            {
                return;
            }

            if (!CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                    expressionSyntax,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula,
                    getSymbolVersion: null) ||
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
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (iterationSymbol == null ||
                !TryGetFiniteElementExpressions(expressionSyntax, out var elementExpressions))
            {
                return;
            }

            SmtFormula? finiteDomainFact = null;
            foreach (var elementExpression in elementExpressions)
            {
                if (ExpressionReferencesSymbol(elementExpression, iterationSymbol.OriginalDefinition, semanticModel, cancellationToken) ||
                    !TryCreateAssignedValueFact(
                        iterationSymbol.OriginalDefinition,
                        elementExpression,
                        semanticModel,
                        cancellationToken,
                        out var elementValueFact))
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
            if (!CSharpConditionToFormula.TryTranslateValue(
                    expressionSyntax,
                    semanticModel,
                    cancellationToken,
                    out var formula,
                    getSymbolVersion: null) ||
                formula is not { Kind: SmtValueKind.Reference })
            {
                return;
            }

            facts.Add(new SmtBinaryFormula(
                isNull ? SmtBinaryOperator.Equal : SmtBinaryOperator.NotEqual,
                formula,
                new SmtNullConstant()));
        }

        private static bool StatementDefinitelyExits(StatementSyntax statement)
        {
            statement = UnwrapSingleStatementBlock(statement);
            return statement switch
            {
                ReturnStatementSyntax => true,
                ThrowStatementSyntax => true,
                BreakStatementSyntax => true,
                ContinueStatementSyntax => true,
                BlockSyntax block when block.Statements.Count > 0 => StatementDefinitelyExits(block.Statements[block.Statements.Count - 1]),
                IfStatementSyntax ifStatement when ifStatement.Else != null =>
                    StatementDefinitelyExits(ifStatement.Statement) &&
                    StatementDefinitelyExits(ifStatement.Else.Statement),
                _ => false
            };
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

                if (mutatedExpression == null)
                {
                    continue;
                }

                var mutatedSymbol = semanticModel.GetSymbolInfo(mutatedExpression, cancellationToken).Symbol;
                if (mutatedSymbol is ILocalSymbol or IParameterSymbol)
                {
                    RemoveFactsReferencingSymbol(facts, mutatedSymbol.OriginalDefinition);
                }
            }
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
                TryCreateAssignedValueFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var fact))
            {
                facts.Add(fact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                TryCreateBuiltInLengthFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var lengthFact))
            {
                facts.Add(lengthFact);
            }

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

        private static void AddReferenceNonNullFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var formula,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                formula is not { Kind: SmtValueKind.Reference })
            {
                return;
            }

            facts.Add(new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                formula,
                new SmtNullConstant()));
        }

        private static bool TryCreateAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula) ||
                !CSharpConditionToFormula.TryTranslateValue(valueExpression, semanticModel, cancellationToken, out var valueFormula, getSymbolVersion: null, inlineDepth: 0) ||
                valueFormula == null ||
                !CanCompareSmtValues(targetFormula, valueFormula))
            {
                return false;
            }

            fact = CreateAssignedValueFact(targetFormula, valueFormula);
            return true;
        }

        private static bool TryHandleTupleDeconstructionDeclaration(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapExpression(assignment.Left) is not DeclarationExpressionSyntax declarationExpression ||
                declarationExpression.Designation is not ParenthesizedVariableDesignationSyntax leftDesignation ||
                UnwrapExpression(assignment.Right) is not TupleExpressionSyntax rightTuple ||
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
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapExpression(assignment.Left) is not TupleExpressionSyntax leftTuple)
            {
                return false;
            }

            var targetSymbols = new List<ISymbol>();
            foreach (var argument in leftTuple.Arguments)
            {
                var targetSymbol = semanticModel.GetSymbolInfo(argument.Expression, cancellationToken).Symbol;
                if (targetSymbol is ILocalSymbol or IParameterSymbol)
                {
                    targetSymbols.Add(targetSymbol.OriginalDefinition);
                }
            }

            foreach (var targetSymbol in targetSymbols)
            {
                RemoveFactsReferencingSymbol(facts, targetSymbol);
            }

            if (targetSymbols.Count != leftTuple.Arguments.Count ||
                UnwrapExpression(assignment.Right) is not TupleExpressionSyntax rightTuple ||
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
            CancellationToken cancellationToken,
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

        private static bool TryCreateBuiltInLengthFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateBuiltInLengthFormula(targetSymbol, out var targetLengthFormula) ||
                !TryCreateBuiltInLengthValueFormula(valueExpression, semanticModel, cancellationToken, out var valueLengthFormula))
            {
                return false;
            }

            fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, targetLengthFormula, valueLengthFormula);
            return true;
        }

        private static bool TryCreateBuiltInLengthFormula(ISymbol symbol, out SmtFormula formula)
        {
            var type = symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type is IArrayTypeSymbol { Rank: 1 } ||
                type?.SpecialType == SpecialType.System_String)
            {
                var receiverFormula = new SmtVariable(GetSmtVariableName(symbol), SmtValueKind.Reference);
                formula = new SmtVariable(receiverFormula + ".Length", SmtValueKind.Int);
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateBuiltInLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            return CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                valueExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion: null,
                inlineDepth: 0);
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

            if (IsIntegralType(type))
            {
                formula = new SmtVariable(variableName, SmtValueKind.Int);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Reference);
                return true;
            }

            formula = null!;
            return false;
        }

        private static SmtFormula CreateAssignedValueFact(SmtFormula targetFormula, SmtFormula valueFormula)
        {
            if (targetFormula.Kind == SmtValueKind.Bool &&
                valueFormula is SmtBooleanConstant booleanConstant)
            {
                return booleanConstant.Value
                    ? targetFormula
                    : new SmtUnaryFormula(SmtUnaryOperator.Not, targetFormula);
            }

            return new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, valueFormula);
        }

        private static bool CanCompareSmtValues(SmtFormula left, SmtFormula right)
        {
            return left.Kind == right.Kind ||
                left is SmtNullConstant && right.Kind == SmtValueKind.Reference ||
                right is SmtNullConstant && left.Kind == SmtValueKind.Reference;
        }

        private static string GetSmtVariableName(ISymbol symbol)
        {
            var firstLocation = symbol.Locations.FirstOrDefault();
            var start = firstLocation?.SourceSpan.Start ?? 0;
            return symbol.Name + "#" + start.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
                    return variable.Name.Contains(variablePrefix, StringComparison.Ordinal);
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
                case SmtConditionalFormula conditional:
                    return ReferencesSmtVariable(conditional.Condition, variablePrefix) ||
                        ReferencesSmtVariable(conditional.WhenTrue, variablePrefix) ||
                        ReferencesSmtVariable(conditional.WhenFalse, variablePrefix);
                default:
                    return false;
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
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
                {
                    expression = parenthesizedExpression.Expression;
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
    }
}
