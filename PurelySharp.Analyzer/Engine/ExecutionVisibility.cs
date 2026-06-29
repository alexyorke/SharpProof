using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Purity;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;
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
                    if (IsConditionAlwaysFalseAt(ifStatement.Condition, ifStatement, semanticModel, cancellationToken, smtAnalysis) &&
                        ifStatement.Statement.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }

                    if (IsConditionAlwaysTrueAt(ifStatement.Condition, ifStatement, semanticModel, cancellationToken, smtAnalysis) &&
                        ifStatement.Else?.Statement.Span.Contains(syntaxNode.SpanStart) == true)
                    {
                        return true;
                    }
                }
                else if (ancestor is ConditionalExpressionSyntax conditionalExpression)
                {
                    if (IsConditionAlwaysFalseAt(conditionalExpression.Condition, conditionalExpression, semanticModel, cancellationToken, smtAnalysis) &&
                        conditionalExpression.WhenTrue.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }

                    if (IsConditionAlwaysTrueAt(conditionalExpression.Condition, conditionalExpression, semanticModel, cancellationToken, smtAnalysis) &&
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
                        IsConditionAlwaysFalseAt(binaryExpression.Left, binaryExpression, semanticModel, cancellationToken, smtAnalysis))
                    {
                        return true;
                    }

                    if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                        binaryExpression.Right.Span.Contains(syntaxNode.SpanStart) &&
                        IsConditionAlwaysTrueAt(binaryExpression.Left, binaryExpression, semanticModel, cancellationToken, smtAnalysis))
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
                        IsConditionAlwaysFalseAt(whileStatement.Condition, whileStatement, semanticModel, cancellationToken, smtAnalysis))
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

            if (IsProgramPointUnreachableUsingSharedFacts(syntaxNode, semanticModel, cancellationToken, smtAnalysis))
            {
                return true;
            }

            return false;
        }

        private static bool IsProgramPointUnreachableUsingSharedFacts(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (smtAnalysis == null)
            {
                return false;
            }

            if (IsInReachableConstantSwitchGotoSection(syntaxNode, semanticModel))
            {
                return false;
            }

            var analysis = new SymbolicInvariantService().AnalyzeAt(syntaxNode, semanticModel, smtAnalysis, cancellationToken);
            return analysis.PathConditions.Count > 0 &&
                analysis.Reachability == SymbolicReachability.Unreachable;
        }

        private static bool IsInReachableConstantSwitchGotoSection(SyntaxNode syntaxNode, SemanticModel semanticModel)
        {
            foreach (var switchStatement in syntaxNode.Ancestors().OfType<SwitchStatementSyntax>())
            {
                var section = switchStatement.Sections.FirstOrDefault(candidate => candidate.Span.Contains(syntaxNode.SpanStart));
                if (section != null && IsReachableConstantSwitchGotoTarget(section, switchStatement, semanticModel))
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

            if (IsReachableConstantSwitchGotoTarget(section, switchStatement, semanticModel))
            {
                return false;
            }

            return IsFormulaAlwaysFalseUsingSmt(sectionCondition, smtAnalysis);
        }

        private static bool IsReachableConstantSwitchGotoTarget(
            SwitchSectionSyntax section,
            SwitchStatementSyntax switchStatement,
            SemanticModel semanticModel)
        {
            var governingValue = semanticModel.GetConstantValue(switchStatement.Expression);
            if (!governingValue.HasValue)
            {
                return false;
            }

            var initialSection = ResolveInitialConstantSwitchSection(switchStatement, semanticModel, governingValue.Value);
            if (initialSection == null)
            {
                return false;
            }

            var reachableSections = new List<SwitchSectionSyntax> { initialSection };
            for (var index = 0; index < reachableSections.Count; index++)
            {
                foreach (var gotoStatement in reachableSections[index]
                             .DescendantNodes()
                             .OfType<GotoStatementSyntax>())
                {
                    if (!ReferenceEquals(
                            gotoStatement.Ancestors().OfType<SwitchStatementSyntax>().FirstOrDefault(),
                            switchStatement))
                    {
                        continue;
                    }

                    var targetSection = ResolveConstantSwitchGotoTarget(gotoStatement, switchStatement, semanticModel);
                    if (targetSection == null ||
                        reachableSections.Any(reachableSection => ReferenceEquals(reachableSection, targetSection)))
                    {
                        continue;
                    }

                    reachableSections.Add(targetSection);
                }
            }

            return reachableSections.Any(reachableSection => ReferenceEquals(reachableSection, section));
        }

        private static SwitchSectionSyntax? ResolveInitialConstantSwitchSection(
            SwitchStatementSyntax switchStatement,
            SemanticModel semanticModel,
            object? governingValue)
        {
            SwitchSectionSyntax? defaultSection = null;

            foreach (var section in switchStatement.Sections)
            {
                foreach (var label in section.Labels)
                {
                    if (label is DefaultSwitchLabelSyntax)
                    {
                        defaultSection ??= section;
                        continue;
                    }

                    if (label is CaseSwitchLabelSyntax caseLabel)
                    {
                        var labelValue = semanticModel.GetConstantValue(caseLabel.Value);
                        if (labelValue.HasValue && ConstantValuesEqual(labelValue.Value, governingValue))
                        {
                            return section;
                        }

                        continue;
                    }

                    if (label is CasePatternSwitchLabelSyntax patternLabel &&
                        PatternMatchesConstant(patternLabel.Pattern, governingValue, semanticModel) &&
                        WhenClauseCanMatch(patternLabel.WhenClause, semanticModel))
                    {
                        return section;
                    }
                }
            }

            return defaultSection;
        }

        private static SwitchSectionSyntax? ResolveConstantSwitchGotoTarget(
            GotoStatementSyntax gotoStatement,
            SwitchStatementSyntax switchStatement,
            SemanticModel semanticModel)
        {
            if (gotoStatement.IsKind(SyntaxKind.GotoDefaultStatement))
            {
                return switchStatement.Sections.FirstOrDefault(section =>
                    section.Labels.Any(label => label is DefaultSwitchLabelSyntax));
            }

            if (!gotoStatement.IsKind(SyntaxKind.GotoCaseStatement) ||
                gotoStatement.Expression == null)
            {
                return null;
            }

            var gotoValue = semanticModel.GetConstantValue(gotoStatement.Expression);
            if (!gotoValue.HasValue)
            {
                return null;
            }

            foreach (var section in switchStatement.Sections)
            {
                if (section.Labels.OfType<CaseSwitchLabelSyntax>().Any(label =>
                    semanticModel.GetConstantValue(label.Value) is { HasValue: true } labelValue &&
                    ConstantValuesEqual(labelValue.Value, gotoValue.Value)))
                {
                    return section;
                }
            }

            return null;
        }

        private static bool PatternMatchesConstant(
            PatternSyntax pattern,
            object? governingValue,
            SemanticModel semanticModel)
        {
            switch (pattern)
            {
                case DiscardPatternSyntax:
                    return true;
                case ParenthesizedPatternSyntax parenthesizedPattern:
                    return PatternMatchesConstant(parenthesizedPattern.Pattern, governingValue, semanticModel);
                case ConstantPatternSyntax constantPattern:
                    var patternValue = semanticModel.GetConstantValue(constantPattern.Expression);
                    return patternValue.HasValue && ConstantValuesEqual(patternValue.Value, governingValue);
                default:
                    return false;
            }
        }

        private static bool WhenClauseCanMatch(WhenClauseSyntax? whenClause, SemanticModel semanticModel)
        {
            if (whenClause == null)
            {
                return true;
            }

            var constantValue = semanticModel.GetConstantValue(whenClause.Condition);
            return constantValue.HasValue &&
                constantValue.Value is bool booleanValue &&
                booleanValue;
        }

        private static bool ConstantValuesEqual(object? left, object? right)
        {
            return Equals(left, right);
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

            var pathConditions = SymbolicProgramPointFacts.CollectPriorAssignmentFacts(forStatement, semanticModel, cancellationToken);
            foreach (var initializerFact in SymbolicProgramPointFacts.CollectForInitializerFacts(forStatement, semanticModel, cancellationToken))
            {
                pathConditions.Add(initializerFact);
            }

            CSharpConditionToFormula.TryCollectDomainFacts(forStatement.Condition, semanticModel, cancellationToken, pathConditions);
            return IsBranchConditionUnreachable(formula, pathConditions, smtAnalysis);
        }

        private static bool IsConditionAlwaysFalseAt(
            ExpressionSyntax expression,
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return EvaluateKnownBoolean(
                expression,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                SymbolicProgramPointFacts.CollectPriorAssignmentFacts(site, semanticModel, cancellationToken)) == KnownBooleanValue.False;
        }

        private static bool IsConditionAlwaysTrueAt(
            ExpressionSyntax expression,
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return EvaluateKnownBoolean(
                expression,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                SymbolicProgramPointFacts.CollectPriorAssignmentFacts(site, semanticModel, cancellationToken)) == KnownBooleanValue.True;
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
            SmtAnalysisService? smtAnalysis,
            IReadOnlyCollection<SmtFormula>? pathConditions = null)
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
                return Negate(EvaluateKnownBoolean(prefixUnary.Operand, semanticModel, cancellationToken, smtAnalysis, pathConditions));
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    var left = EvaluateKnownBoolean(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis, pathConditions);
                    var right = EvaluateKnownBoolean(binaryExpression.Right, semanticModel, cancellationToken, smtAnalysis, pathConditions);
                    if (left == KnownBooleanValue.False || right == KnownBooleanValue.False)
                    {
                        return KnownBooleanValue.False;
                    }

                    if (left == KnownBooleanValue.True && right == KnownBooleanValue.True)
                    {
                        return KnownBooleanValue.True;
                    }

                    return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis, pathConditions);
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    var left = EvaluateKnownBoolean(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis, pathConditions);
                    var right = EvaluateKnownBoolean(binaryExpression.Right, semanticModel, cancellationToken, smtAnalysis, pathConditions);
                    if (left == KnownBooleanValue.True || right == KnownBooleanValue.True)
                    {
                        return KnownBooleanValue.True;
                    }

                    if (left == KnownBooleanValue.False && right == KnownBooleanValue.False)
                    {
                        return KnownBooleanValue.False;
                    }

                    return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis, pathConditions);
                }

                return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis, pathConditions);
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression)
            {
                return EvaluateWithSmtFallback(isPatternExpression, semanticModel, cancellationToken, smtAnalysis, pathConditions);
            }

            return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis, pathConditions);
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
            SmtAnalysisService? smtAnalysis,
            IReadOnlyCollection<SmtFormula>? pathConditions = null)
        {
            if (!CSharpConditionToFormula.TryTranslate(expression, semanticModel, cancellationToken, out var formula) ||
                formula == null)
            {
                return EvaluateBranchAssumptionFeasibility(expression, semanticModel, cancellationToken, smtAnalysis, pathConditions);
            }

            var domainFacts = pathConditions?.ToList() ?? new List<SmtFormula>();
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
            SmtAnalysisService? smtAnalysis,
            IReadOnlyCollection<SmtFormula>? pathConditions = null)
        {
            var trueBranchFacts = pathConditions?.ToList() ?? new List<SmtFormula>();
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

            var falseBranchFacts = pathConditions?.ToList() ?? new List<SmtFormula>();
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

            using var fallbackSmtAnalysis = smtAnalysis == null ? new SmtAnalysisService(SmtAnalysisOptions.Default) : null;
            var proofResult = (smtAnalysis ?? fallbackSmtAnalysis!).Classify(query);
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
