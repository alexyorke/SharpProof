using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Purity;
using PurelySharp.Analyzer.Engine.Smt;
using SearchLib.Smt;

namespace PurelySharp.Analyzer.Engine
{
    internal static class ExecutionVisibility
    {
        public static IEnumerable<IOperation> VisibleDescendants(IOperation rootOperation)
        {
            foreach (var operation in rootOperation.DescendantsAndSelf())
            {
                if (!IsNestedFunctionDescendant(operation, rootOperation))
                {
                    yield return operation;
                }
            }
        }

        public static bool IsNestedCallableBoundary(SyntaxNode node)
        {
            return node is MethodDeclarationSyntax or
                ConstructorDeclarationSyntax or
                OperatorDeclarationSyntax or
                AccessorDeclarationSyntax or
                LocalFunctionStatementSyntax or
                ParenthesizedLambdaExpressionSyntax or
                SimpleLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax;
        }

        public static bool IsInStaticallyUnreachableBranch(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            return IsInStaticallyUnreachableBranchUsingSmt(syntaxNode, semanticModel, cancellationToken, smtAnalysis: null);
        }

        public static bool IsInStaticallyUnreachableBranchUsingSmt(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis = null)
        {
            foreach (var ancestor in syntaxNode.Ancestors())
            {
                if (ancestor is IfStatementSyntax ifStatement)
                {
                    if (IsConditionAlwaysFalseUsingSmt(ifStatement.Condition, semanticModel, cancellationToken, smtAnalysis) &&
                        ifStatement.Statement.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }

                    if (IsConditionAlwaysTrueUsingSmt(ifStatement.Condition, semanticModel, cancellationToken, smtAnalysis) &&
                        ifStatement.Else?.Statement.Span.Contains(syntaxNode.SpanStart) == true)
                    {
                        return true;
                    }
                }
                else if (ancestor is ConditionalExpressionSyntax conditionalExpression)
                {
                    if (IsConditionAlwaysFalseUsingSmt(conditionalExpression.Condition, semanticModel, cancellationToken, smtAnalysis) &&
                        conditionalExpression.WhenTrue.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }

                    if (IsConditionAlwaysTrueUsingSmt(conditionalExpression.Condition, semanticModel, cancellationToken, smtAnalysis) &&
                        conditionalExpression.WhenFalse.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }
                }
                else if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpression)
                {
                    var receiverValue = semanticModel.GetConstantValue(conditionalAccessExpression.Expression, cancellationToken);
                    if (receiverValue.HasValue &&
                        receiverValue.Value == null &&
                        conditionalAccessExpression.WhenNotNull.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }
                }
                else if (ancestor is BinaryExpressionSyntax binaryExpression)
                {
                    if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                        binaryExpression.Right.Span.Contains(syntaxNode.SpanStart) &&
                        IsConditionAlwaysFalseUsingSmt(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis))
                    {
                        return true;
                    }

                    if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                        binaryExpression.Right.Span.Contains(syntaxNode.SpanStart) &&
                        IsConditionAlwaysTrueUsingSmt(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis))
                    {
                        return true;
                    }

                    if (binaryExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                        binaryExpression.Right.Span.Contains(syntaxNode.SpanStart))
                    {
                        var leftValue = semanticModel.GetConstantValue(binaryExpression.Left, cancellationToken);
                        if (leftValue.HasValue && leftValue.Value != null)
                        {
                            return true;
                        }
                    }
                }
                else if (ancestor is WhileStatementSyntax whileStatement)
                {
                    if (whileStatement.Statement.Span.Contains(syntaxNode.SpanStart) &&
                        IsConditionAlwaysFalseUsingSmt(whileStatement.Condition, semanticModel, cancellationToken, smtAnalysis))
                    {
                        return true;
                    }
                }
                else if (ancestor is ForStatementSyntax forStatement)
                {
                    if (forStatement.Condition != null &&
                        forStatement.Statement.Span.Contains(syntaxNode.SpanStart) &&
                        IsForInitialEntryConditionAlwaysFalseUsingSmt(forStatement, semanticModel, cancellationToken, smtAnalysis))
                    {
                        return true;
                    }
                }
                else if (ancestor is SwitchStatementSyntax switchStatement &&
                         IsInUnreachableSwitchStatementSection(syntaxNode, switchStatement, semanticModel, cancellationToken, smtAnalysis))
                {
                    return true;
                }
                else if (ancestor is SwitchExpressionSyntax switchExpression &&
                         IsInUnreachableSwitchExpressionArm(syntaxNode, switchExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInUnreachableSwitchStatementSection(
            SyntaxNode syntaxNode,
            SwitchStatementSyntax switchStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (smtAnalysis == null)
            {
                return false;
            }

            var section = switchStatement.Sections.FirstOrDefault(candidate => candidate.Span.Contains(syntaxNode.SpanStart));
            if (section == null ||
                !SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                    switchStatement.Expression,
                    section,
                    semanticModel,
                    cancellationToken,
                    out var sectionCondition))
            {
                return false;
            }

            return IsFormulaAlwaysFalseUsingSmt(sectionCondition, smtAnalysis);
        }

        private static bool IsInUnreachableSwitchExpressionArm(
            SyntaxNode syntaxNode,
            SwitchExpressionSyntax switchExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (smtAnalysis == null)
            {
                return false;
            }

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

            return IsFormulaAlwaysFalseUsingSmt(armCondition, smtAnalysis);
        }

        private static bool IsFormulaAlwaysFalseUsingSmt(SmtFormula formula, SmtAnalysisService smtAnalysis)
        {
            var query = new PurityProofQuery(
                Array.Empty<SmtFormula>(),
                new PurityHazard(PurityHazardKind.BranchReachability, formula));

            var proofResult = smtAnalysis.Classify(query);
            return proofResult.Outcome == PurityProofOutcome.ProvablyPure;
        }

        private static bool IsForInitialEntryConditionAlwaysFalseUsingSmt(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (forStatement.Condition == null)
            {
                return false;
            }

            if (!CSharpConditionToFormula.TryTranslate(forStatement.Condition, semanticModel, cancellationToken, out var formula) ||
                formula == null)
            {
                return IsConditionAlwaysFalseUsingSmt(forStatement.Condition, semanticModel, cancellationToken, smtAnalysis);
            }

            var pathConditions = new List<SmtFormula>();
            foreach (var initializerFact in CollectForInitializerFacts(forStatement, semanticModel, cancellationToken))
            {
                pathConditions.Add(initializerFact);
            }

            CSharpConditionToFormula.TryCollectDomainFacts(forStatement.Condition, semanticModel, cancellationToken, pathConditions);
            return IsBranchConditionUnreachable(formula, pathConditions, smtAnalysis);
        }

        private static IEnumerable<SmtFormula> CollectForInitializerFacts(
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
                        AddForInitializerFact(localSymbol, declarator.Initializer.Value, semanticModel, cancellationToken, facts);
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
                    AddForInitializerFact(assignedSymbol.OriginalDefinition, assignment.Right, semanticModel, cancellationToken, facts);
                }
            }

            return facts;
        }

        private static void AddForInitializerFact(
            ISymbol assignedSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            RemoveFactsReferencingSymbol(facts, assignedSymbol);
            if (!ExpressionReferencesSymbol(valueExpression, assignedSymbol, semanticModel, cancellationToken) &&
                TryCreateAssignedValueFact(assignedSymbol, valueExpression, semanticModel, cancellationToken, out var fact))
            {
                facts.Add(fact);
            }
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
                         descendIntoChildren: candidate => !IsNestedCallableBoundary(candidate)))
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

        public static bool IsConditionAlwaysTrue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            return IsConditionAlwaysTrueUsingSmt(expression, semanticModel, cancellationToken, smtAnalysis: null);
        }

        public static bool IsConditionAlwaysTrueUsingSmt(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis = null)
        {
            return EvaluateKnownBoolean(expression, semanticModel, cancellationToken, smtAnalysis) == KnownBooleanValue.True;
        }

        public static bool IsConditionAlwaysFalse(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            return IsConditionAlwaysFalseUsingSmt(expression, semanticModel, cancellationToken, smtAnalysis: null);
        }

        public static bool IsConditionAlwaysFalseUsingSmt(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis = null)
        {
            return EvaluateKnownBoolean(expression, semanticModel, cancellationToken, smtAnalysis) == KnownBooleanValue.False;
        }

        private static bool IsNestedFunctionDescendant(IOperation operation, IOperation rootOperation)
        {
            if (ReferenceEquals(operation, rootOperation))
            {
                return false;
            }

            for (var parent = operation.Parent; parent != null && !ReferenceEquals(parent, rootOperation); parent = parent.Parent)
            {
                if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                {
                    return true;
                }
            }

            return false;
        }

        private static KnownBooleanValue EvaluateKnownBoolean(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue && constantValue.Value is bool booleanValue)
            {
                return booleanValue ? KnownBooleanValue.True : KnownBooleanValue.False;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                return Negate(EvaluateKnownBoolean(prefixUnary.Operand, semanticModel, cancellationToken, smtAnalysis));
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    var left = EvaluateKnownBoolean(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis);
                    var right = EvaluateKnownBoolean(binaryExpression.Right, semanticModel, cancellationToken, smtAnalysis);
                    if (left == KnownBooleanValue.False || right == KnownBooleanValue.False)
                    {
                        return KnownBooleanValue.False;
                    }

                    if (left == KnownBooleanValue.True && right == KnownBooleanValue.True)
                    {
                        return KnownBooleanValue.True;
                    }

                    return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis);
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    var left = EvaluateKnownBoolean(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis);
                    var right = EvaluateKnownBoolean(binaryExpression.Right, semanticModel, cancellationToken, smtAnalysis);
                    if (left == KnownBooleanValue.True || right == KnownBooleanValue.True)
                    {
                        return KnownBooleanValue.True;
                    }

                    if (left == KnownBooleanValue.False && right == KnownBooleanValue.False)
                    {
                        return KnownBooleanValue.False;
                    }

                    return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis);
                }

                return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis);
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression)
            {
                return EvaluateWithSmtFallback(isPatternExpression, semanticModel, cancellationToken, smtAnalysis);
            }

            return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis);
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

        private static KnownBooleanValue Negate(KnownBooleanValue value)
        {
            return value switch
            {
                KnownBooleanValue.True => KnownBooleanValue.False,
                KnownBooleanValue.False => KnownBooleanValue.True,
                _ => KnownBooleanValue.Unknown
            };
        }

        private static KnownBooleanValue EvaluateWithSmtFallback(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (!CSharpConditionToFormula.TryTranslate(expression, semanticModel, cancellationToken, out var formula) ||
                formula == null)
            {
                return EvaluateBranchAssumptionFeasibility(expression, semanticModel, cancellationToken, smtAnalysis);
            }

            var domainFacts = new List<SmtFormula>();
            CSharpConditionToFormula.TryCollectDomainFacts(expression, semanticModel, cancellationToken, domainFacts);

            if (IsBranchConditionUnreachable(formula, domainFacts, smtAnalysis))
            {
                return KnownBooleanValue.False;
            }

            if (IsBranchConditionUnreachable(new SmtUnaryFormula(SmtUnaryOperator.Not, formula), domainFacts, smtAnalysis))
            {
                return KnownBooleanValue.True;
            }

            return KnownBooleanValue.Unknown;
        }

        private static KnownBooleanValue EvaluateBranchAssumptionFeasibility(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            var trueBranchFacts = new List<SmtFormula>();
            if (CSharpConditionToFormula.TryCollectBranchAssumptions(
                    expression,
                    branchWhenTrue: true,
                    semanticModel,
                    cancellationToken,
                    trueBranchFacts) &&
                IsBranchConditionUnreachable(new SmtBooleanConstant(true), trueBranchFacts, smtAnalysis))
            {
                return KnownBooleanValue.False;
            }

            var falseBranchFacts = new List<SmtFormula>();
            if (CSharpConditionToFormula.TryCollectBranchAssumptions(
                    expression,
                    branchWhenTrue: false,
                    semanticModel,
                    cancellationToken,
                    falseBranchFacts) &&
                IsBranchConditionUnreachable(new SmtBooleanConstant(true), falseBranchFacts, smtAnalysis))
            {
                return KnownBooleanValue.True;
            }

            return KnownBooleanValue.Unknown;
        }

        private static bool IsBranchConditionUnreachable(
            SmtFormula formula,
            IReadOnlyCollection<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            var query = new PurityProofQuery(
                pathConditions.ToArray(),
                new PurityHazard(PurityHazardKind.BranchReachability, formula));

            var proofResult = (smtAnalysis ?? new SmtAnalysisService(SmtAnalysisOptions.Default)).Classify(query);
            return proofResult.Outcome == PurityProofOutcome.ProvablyPure;
        }

        private enum KnownBooleanValue
        {
            Unknown,
            False,
            True
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
