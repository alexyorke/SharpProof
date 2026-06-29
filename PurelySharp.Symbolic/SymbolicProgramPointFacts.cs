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
        private const int MaxStructuralNullStateDepth = 4;

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
                        forEachStatementSyntax,
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

                AddElementAssignmentFact(assignment, semanticModel, cancellationToken, facts);
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
            AddCompletedSwitchExitExclusionFacts(switchStatement, semanticModel, cancellationToken, facts);

            if (!switchStatement.Sections.Any(static section => section.Labels.Any(static label => label is DefaultSwitchLabelSyntax)))
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
                if (!SectionDefinitelyExitsFromSwitch(section, switchStatement) ||
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
                if (SectionDefinitelyExitsFromSwitch(section, switchStatement))
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

        private static void AddReferencedSymbols(
            SyntaxNode root,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<ISymbol> symbols)
        {
            foreach (var symbol in GetReferencedLocalAndParameterSymbols(root, semanticModel, cancellationToken))
            {
                if (symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
                {
                    symbols.Add(symbol);
                }
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
            SwitchStatementSyntax switchStatement)
        {
            return section.Statements.Count > 0 &&
                StatementDefinitelyExitsFromSwitch(section.Statements[section.Statements.Count - 1], switchStatement);
        }

        private static bool StatementDefinitelyExitsFromSwitch(
            StatementSyntax statement,
            SwitchStatementSyntax switchStatement)
        {
            statement = UnwrapSingleStatementBlock(statement);
            return statement switch
            {
                ReturnStatementSyntax => true,
                ThrowStatementSyntax => true,
                BreakStatementSyntax breakStatement => !BreakTargetsSwitch(breakStatement, switchStatement),
                ContinueStatementSyntax => true,
                BlockSyntax block when block.Statements.Count > 0 => StatementDefinitelyExitsFromSwitch(block.Statements[block.Statements.Count - 1], switchStatement),
                IfStatementSyntax ifStatement when ifStatement.Else != null =>
                    StatementDefinitelyExitsFromSwitch(ifStatement.Statement, switchStatement) &&
                    StatementDefinitelyExitsFromSwitch(ifStatement.Else.Statement, switchStatement),
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
            public SwitchBranchFacts(SmtFormula condition, IReadOnlyList<SmtFormula> facts, bool conditionSymbolsMutated)
            {
                Condition = condition;
                Facts = facts;
                ConditionSymbolsMutated = conditionSymbolsMutated;
            }

            public SmtFormula Condition { get; }

            public IReadOnlyList<SmtFormula> Facts { get; }

            public bool ConditionSymbolsMutated { get; }
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
            StatementSyntax foreachStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            AddReferenceNullCondition(facts, expressionSyntax, isNull: false, semanticModel, cancellationToken);
            AddFiniteForeachIterationFact(facts, expressionSyntax, iterationSymbol, foreachStatement, semanticModel, cancellationToken);

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
            var allReferenceElementsDefinitelyNonNull = GetSymbolType(iterationSymbol.OriginalDefinition)?.IsReferenceType == true;
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
                GetSymbolType(receiverSymbol) is not IArrayTypeSymbol { Rank: 1 } arrayType ||
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
            return StatementMutatesSymbol(statement, receiverSymbol, semanticModel, cancellationToken) ||
                StatementMayMutateSymbolThroughReference(statement, receiverSymbol, semanticModel, cancellationToken);
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

                if (mutatedExpression != null)
                {
                    var mutatedSymbol = semanticModel.GetSymbolInfo(mutatedExpression, cancellationToken).Symbol;
                    if (mutatedSymbol is ILocalSymbol or IParameterSymbol)
                    {
                        RemoveFactsReferencingSymbol(facts, mutatedSymbol.OriginalDefinition);
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
                if (GetSymbolType(symbol) is IArrayTypeSymbol)
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
            if (!IsPotentiallyMutableThroughReference(GetSymbolType(symbol)))
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
                TryCreateAssignedValueFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var fact))
            {
                facts.Add(fact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                TryCreateStringContentAssignedValueFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var stringContentFact))
            {
                facts.Add(stringContentFact);
            }

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                TryCreateSymbolSmtValue(assignedSymbol, out var targetReferenceFormula) &&
                targetReferenceFormula is { Kind: SmtValueKind.Reference } &&
                GetSymbolType(assignedSymbol)?.SpecialType == SpecialType.System_String &&
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

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                TryCreateBuiltInLengthFact(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, out var lengthFact))
            {
                facts.Add(lengthFact);
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

            if (!ExpressionReferencesSymbol(effectiveValueExpression, assignedSymbol, semanticModel, cancellationToken))
            {
                AddStructuralReferenceNullStateAssignedValueFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
                AddNullableAssignedValueFacts(assignedSymbol, effectiveValueExpression, semanticModel, cancellationToken, facts);
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

            if (!CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                valueFormula is not { Kind: SmtValueKind.Reference })
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
            if (!CSharpConditionToFormula.TryTranslate(
                    conditionalExpression.Condition,
                    semanticModel,
                    cancellationToken,
                    out var conditionFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                conditionFormula == null ||
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
                !CSharpConditionToFormula.TryTranslateValue(
                    conditionalAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                receiverFormula is not { Kind: SmtValueKind.Reference } ||
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
            out SmtFormula? formula)
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
                CSharpConditionToFormula.TryTranslate(
                    conditionalExpression.Condition,
                    semanticModel,
                    cancellationToken,
                    out var conditionFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) &&
                conditionFormula != null &&
                TryTranslateAssignedValueExpression(
                    conditionalExpression.WhenTrue,
                    semanticModel,
                    cancellationToken,
                    assignedSymbol,
                    out var whenTrueFormula) &&
                whenTrueFormula != null &&
                TryTranslateAssignedValueExpression(
                    conditionalExpression.WhenFalse,
                    semanticModel,
                    cancellationToken,
                    assignedSymbol,
                    out var whenFalseFormula) &&
                whenFalseFormula != null &&
                whenTrueFormula.Kind == whenFalseFormula.Kind)
            {
                formula = new SmtConditionalFormula(conditionFormula, whenTrueFormula, whenFalseFormula, whenTrueFormula.Kind);
                return true;
            }

            if (CSharpConditionToFormula.TryTranslateValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
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

        private static bool TryCreateStringContentAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateStringContentFormula(targetSymbol, out var targetFormula) ||
                !CSharpConditionToFormula.TryTranslateStringValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                valueFormula == null)
            {
                return false;
            }

            fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, valueFormula);
            return true;
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

        private static void AddFiniteArrayElementAssignedValueFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (GetSymbolType(assignedSymbol) is not IArrayTypeSymbol { Rank: 1 } arrayType ||
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
                    CanCompareSmtValues(targetFormula, valueFormula))
                {
                    facts.Add(CreateAssignedValueFact(targetFormula, valueFormula));
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
            if (!TryCreateSymbolSmtValue(arraySymbol, out var receiverFormula) ||
                receiverFormula.Kind != SmtValueKind.Reference ||
                !TryGetValueKind(elementType, out var elementKind))
            {
                return false;
            }

            formula = new SmtVariable(
                GetSmtVariableName(arraySymbol) + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]",
                elementKind);
            return true;
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
                !CSharpConditionToFormula.TryTranslateValue(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var targetFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                targetFormula == null ||
                !TryTranslateAssignedValueExpression(
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    assignedSymbol: null,
                    out var valueFormula) ||
                valueFormula == null ||
                !CanCompareSmtValues(targetFormula, valueFormula))
            {
                return;
            }

            facts.Add(CreateAssignedValueFact(targetFormula, valueFormula));
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
                GetSymbolType(receiverSymbol) is not IArrayTypeSymbol { Rank: 1 } arrayType ||
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
                if (ExpressionReferencesSymbol(argumentExpression, assignedSymbol, semanticModel, cancellationToken) ||
                    !TryCreateTupleElementSmtValue(assignedSymbol, elementNames[index], out var targetFormula) ||
                    !CSharpConditionToFormula.TryTranslateValue(
                        argumentExpression,
                        semanticModel,
                        cancellationToken,
                        out var valueFormula,
                        getSymbolVersion: null,
                        inlineDepth: 0) ||
                    valueFormula == null ||
                    !CanCompareSmtValues(targetFormula, valueFormula))
                {
                    continue;
                }

                facts.Add(CreateAssignedValueFact(targetFormula, valueFormula));
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
            var type = tupleSymbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type is not INamedTypeSymbol { IsTupleType: true } tupleType)
            {
                formula = null!;
                return false;
            }

            var element = tupleType.TupleElements
                .FirstOrDefault(field => string.Equals((field.CorrespondingTupleField ?? field).Name, elementName, StringComparison.Ordinal));
            if (element == null)
            {
                formula = null!;
                return false;
            }

            var elementType = element.Type;
            var variableName = GetSmtVariableName(tupleSymbol) + "." + elementName;
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

            if (!CSharpConditionToFormula.TryTranslateValue(
                    rightExpression,
                    semanticModel,
                    cancellationToken,
                    out var rightFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                rightFormula is not { Kind: SmtValueKind.Reference })
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
                !TryGetNullableUnderlyingType(GetSymbolType(assignedSymbol), out var underlyingType))
            {
                return;
            }

            if (TryCreateNullableStateFormulas(
                    rightExpression,
                    semanticModel,
                    cancellationToken,
                    out var rightHasValue,
                    out _) &&
                rightHasValue is SmtBooleanConstant { Value: true })
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
                !TryTranslateAssignedValueExpression(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    targetSymbol,
                    out var valueFormula) ||
                valueFormula == null ||
                !CanCompareSmtValues(targetFormula, valueFormula))
            {
                return false;
            }

            fact = CreateAssignedValueFact(targetFormula, valueFormula);
            return true;
        }

        private static bool TryCreateAssignedValueFact(
            ISymbol targetSymbol,
            SmtFormula valueFormula,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula) ||
                !CanCompareSmtValues(targetFormula, valueFormula))
            {
                return false;
            }

            fact = CreateAssignedValueFact(targetFormula, valueFormula);
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
                !CSharpConditionToFormula.TryTranslateValue(
                    conditionalAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                receiverFormula is not { Kind: SmtValueKind.Reference })
            {
                return;
            }

            var receiverNonNull = new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                receiverFormula,
                new SmtNullConstant());

            if (TryCreateNullableHasValueFormula(assignedSymbol, out var targetHasValue) &&
                TryCreateNullableValueFormula(assignedSymbol, out var targetValue) &&
                TryGetNullableUnderlyingType(GetSymbolType(assignedSymbol), out var underlyingType) &&
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
                    CanCompareSmtValues(targetValue, whenNotNullValue))
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
                    CSharpConditionToFormula.TryTranslateStringValue(
                        conditionalAccess.Expression,
                        semanticModel,
                        cancellationToken,
                        out var stringFormula) &&
                    stringFormula != null)
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
            valueExpression = UnwrapExpression(valueExpression);
            if (valueExpression is not BinaryExpressionSyntax asExpression ||
                !asExpression.IsKind(SyntaxKind.AsExpression) ||
                !TryCreateSymbolSmtValue(assignedSymbol, out var targetFormula) ||
                targetFormula is not { Kind: SmtValueKind.Reference } ||
                !CSharpConditionToFormula.TryTranslateValue(
                    asExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var sourceFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) ||
                sourceFormula is not { Kind: SmtValueKind.Reference })
            {
                return;
            }

            facts.Add(new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, new SmtNullConstant()),
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, sourceFormula, new SmtNullConstant())));
        }

        private static void AddNullableAssignedValueFacts(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!TryCreateNullableHasValueFormula(assignedSymbol, out var targetHasValue) ||
                !TryCreateNullableValueFormula(assignedSymbol, out var targetValue))
            {
                return;
            }

            if (TryCreateNullableStateFormulas(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula,
                    out var valueFormula))
            {
                facts.Add(CreateAssignedValueFact(targetHasValue, hasValueFormula));

                if (valueFormula != null &&
                    CanCompareSmtValues(targetValue, valueFormula))
                {
                    facts.Add(CreateAssignedValueFact(targetValue, valueFormula));
                }
            }
            else if (TryGetNullableUnderlyingType(GetSymbolType(assignedSymbol), out var underlyingType) &&
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
                    facts.Add(CreateAssignedValueFact(targetValue, wrappedValueFormula));
                }
            }
        }

        private static bool TryCreateNullableStateFormulas(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula hasValueFormula,
            out SmtFormula? valueFormula)
        {
            valueExpression = UnwrapExpression(valueExpression);
            if (IsNullOrDefaultNullableValue(valueExpression, semanticModel, cancellationToken))
            {
                hasValueFormula = new SmtBooleanConstant(false);
                valueFormula = null;
                return true;
            }

            if (TryGetNullableWrappedValueExpression(valueExpression, semanticModel, cancellationToken, out var wrappedValueExpression) &&
                CSharpConditionToFormula.TryTranslateValue(
                    wrappedValueExpression,
                    semanticModel,
                    cancellationToken,
                    out valueFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) &&
                valueFormula != null)
            {
                hasValueFormula = new SmtBooleanConstant(true);
                return true;
            }

            if (semanticModel.GetSymbolInfo(valueExpression, cancellationToken).Symbol is { } sourceSymbol &&
                sourceSymbol is ILocalSymbol or IParameterSymbol &&
                TryCreateNullableHasValueFormula(sourceSymbol.OriginalDefinition, out hasValueFormula))
            {
                valueFormula = null;
                return true;
            }

            if (valueExpression is ConditionalExpressionSyntax conditionalExpression &&
                CSharpConditionToFormula.TryTranslate(
                    conditionalExpression.Condition,
                    semanticModel,
                    cancellationToken,
                    out var conditionFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) &&
                conditionFormula != null &&
                TryCreateNullableStateFormulas(
                    conditionalExpression.WhenTrue,
                    semanticModel,
                    cancellationToken,
                    out var trueHasValue,
                    out var trueValue) &&
                TryCreateNullableStateFormulas(
                    conditionalExpression.WhenFalse,
                    semanticModel,
                    cancellationToken,
                    out var falseHasValue,
                    out var falseValue))
            {
                hasValueFormula = new SmtConditionalFormula(conditionFormula, trueHasValue, falseHasValue, SmtValueKind.Bool);
                valueFormula = trueValue != null &&
                    falseValue != null &&
                    trueValue.Kind == falseValue.Kind
                        ? new SmtConditionalFormula(conditionFormula, trueValue, falseValue, trueValue.Kind)
                        : null;
                return true;
            }

            hasValueFormula = null!;
            valueFormula = null;
            return false;
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

        private static bool IsNullOrDefaultNullableValue(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (semanticModel.GetConstantValue(valueExpression, cancellationToken) is { HasValue: true, Value: null } &&
                TryGetNullableUnderlyingType(
                    semanticModel.GetTypeInfo(valueExpression, cancellationToken).ConvertedType ??
                    semanticModel.GetTypeInfo(valueExpression, cancellationToken).Type,
                    out _))
            {
                return true;
            }

            if (!valueExpression.IsKind(SyntaxKind.DefaultLiteralExpression) &&
                valueExpression is not DefaultExpressionSyntax)
            {
                return false;
            }

            return TryGetNullableUnderlyingType(
                semanticModel.GetTypeInfo(valueExpression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(valueExpression, cancellationToken).Type,
                out _);
        }

        private static bool TryGetNullableWrappedValueExpression(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax wrappedValueExpression)
        {
            valueExpression = UnwrapExpression(valueExpression);
            if (valueExpression is CastExpressionSyntax castExpression &&
                TryGetNullableUnderlyingType(
                    semanticModel.GetTypeInfo(castExpression, cancellationToken).Type,
                    out _))
            {
                wrappedValueExpression = castExpression.Expression;
                return true;
            }

            if (valueExpression is ObjectCreationExpressionSyntax objectCreation &&
                TryGetNullableUnderlyingType(
                    semanticModel.GetTypeInfo(objectCreation, cancellationToken).Type,
                    out _) &&
                objectCreation.ArgumentList?.Arguments.Count == 1)
            {
                wrappedValueExpression = objectCreation.ArgumentList.Arguments[0].Expression;
                return true;
            }

            var typeInfo = semanticModel.GetTypeInfo(valueExpression, cancellationToken);
            if (TryGetNullableUnderlyingType(typeInfo.ConvertedType, out var convertedUnderlyingType) &&
                typeInfo.Type != null &&
                SymbolEqualityComparer.Default.Equals(typeInfo.Type, convertedUnderlyingType))
            {
                wrappedValueExpression = valueExpression;
                return true;
            }

            wrappedValueExpression = null!;
            return false;
        }

        private static bool TryCreateNullableHasValueFormula(ISymbol symbol, out SmtFormula formula)
        {
            if (!TryGetNullableUnderlyingType(GetSymbolType(symbol), out _))
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(GetSmtVariableName(symbol) + ".HasValue", SmtValueKind.Bool);
            return true;
        }

        private static bool TryCreateNullableValueFormula(ISymbol symbol, out SmtFormula formula)
        {
            if (!TryGetNullableUnderlyingType(GetSymbolType(symbol), out var underlyingType) ||
                !TryGetValueKind(underlyingType, out var kind))
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(GetSmtVariableName(symbol) + ".Value", kind);
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
                declarationExpression.Designation is not ParenthesizedVariableDesignationSyntax leftDesignation)
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

            if (ExpressionReferencesAnySymbol(assignment.Right, targetSymbols, semanticModel, cancellationToken))
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
                ExpressionReferencesAnySymbol(assignment.Right, targetSymbols, semanticModel, cancellationToken))
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
            IReadOnlyList<ISymbol> targetSymbols,
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
                    AddAssignedValueFacts(
                        targetSymbols[index],
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
                if (TryCreateAssignedValueFact(targetSymbols[index], valueFormulas[index], out var fact))
                {
                    facts.Add(fact);
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

            if (type?.SpecialType == SpecialType.System_String)
            {
                formula = new SmtStringLengthTerm(new SmtVariable(GetSmtVariableName(symbol) + ".String", SmtValueKind.String));
                return true;
            }

            if (type is IArrayTypeSymbol { Rank: 1 })
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

        private static ITypeSymbol? GetSymbolType(ISymbol symbol)
        {
            return symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };
        }

        private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                kind = SmtValueKind.Bool;
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                kind = SmtValueKind.Int;
                return true;
            }

            if (type.IsReferenceType)
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
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

        private static bool IsIntegralOrEnumType(ITypeSymbol typeSymbol)
        {
            return IsIntegralType(typeSymbol) ||
                typeSymbol.TypeKind == TypeKind.Enum;
        }
    }
}
