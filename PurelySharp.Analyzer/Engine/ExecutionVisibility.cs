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
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Analyzer.Engine
{
    internal static class ExecutionVisibility
    {
        private static readonly ConditionalWeakTable<SemanticModel, ConditionTruthCache> s_conditionTruthCache = new();

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
                    if (conditionalAccessExpression.WhenNotNull.Span.Contains(syntaxNode.SpanStart) &&
                        IsReferenceKnownNullAt(
                            conditionalAccessExpression.Expression,
                            conditionalAccessExpression,
                            semanticModel,
                            cancellationToken,
                            smtAnalysis))
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
                        if (IsReferenceKnownNonNullAt(
                                binaryExpression.Left,
                                binaryExpression,
                                semanticModel,
                                cancellationToken,
                                smtAnalysis))
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
                        SymbolicReachabilityService.IsForInitialEntryConditionAlwaysFalse(
                            forStatement,
                            semanticModel,
                            cancellationToken,
                            smtAnalysis))
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

        public static bool IsEvaluationPathUnsatisfiableUsingSmt(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            IReadOnlyCollection<SmtFormula> basePathConditions,
            Func<ISymbol, int>? getSymbolVersion,
            SmtAnalysisService smtAnalysis)
        {
            if (basePathConditions.Count == 0)
            {
                return false;
            }

            var pathConditions = basePathConditions.ToList();
            var originalCount = pathConditions.Count;
            foreach (var ancestor in syntaxNode.Ancestors())
            {
                AddEvaluationPathFacts(
                    syntaxNode,
                    ancestor,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);

                if (pathConditions.Count > originalCount &&
                    ArePathConditionsUnsatisfiableAt(pathConditions, syntaxNode, smtAnalysis))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddEvaluationPathFacts(
            SyntaxNode syntaxNode,
            SyntaxNode ancestor,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (ancestor is IfStatementSyntax ifStatement)
            {
                if (ifStatement.Statement.Span.Contains(syntaxNode.SpanStart))
                {
                    AddBranchConditionFact(
                        ifStatement.Condition,
                        branchWhenTrue: true,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }
                else if (ifStatement.Else?.Statement.Span.Contains(syntaxNode.SpanStart) == true)
                {
                    AddBranchConditionFact(
                        ifStatement.Condition,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }

                return;
            }

            if (ancestor is ConditionalExpressionSyntax conditionalExpression)
            {
                if (conditionalExpression.WhenTrue.Span.Contains(syntaxNode.SpanStart))
                {
                    AddBranchConditionFact(
                        conditionalExpression.Condition,
                        branchWhenTrue: true,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }
                else if (conditionalExpression.WhenFalse.Span.Contains(syntaxNode.SpanStart))
                {
                    AddBranchConditionFact(
                        conditionalExpression.Condition,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }

                return;
            }

            if (ancestor is BinaryExpressionSyntax binaryExpression)
            {
                if (!binaryExpression.Right.Span.Contains(syntaxNode.SpanStart))
                {
                    return;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    AddBranchConditionFact(
                        binaryExpression.Left,
                        branchWhenTrue: true,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }
                else if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    AddBranchConditionFact(
                        binaryExpression.Left,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }
                else if (binaryExpression.IsKind(SyntaxKind.CoalesceExpression))
                {
                    AddReferenceNullStateFact(
                        binaryExpression.Left,
                        equalToNull: true,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }

                return;
            }

            if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpression &&
                conditionalAccessExpression.WhenNotNull.Span.Contains(syntaxNode.SpanStart))
            {
                AddReferenceNullStateFact(
                    conditionalAccessExpression.Expression,
                    equalToNull: false,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);

                return;
            }

            if (ancestor is SwitchStatementSyntax switchStatement)
            {
                var section = switchStatement.Sections.FirstOrDefault(candidate =>
                    candidate.Statements.Any(statement => statement.Span.Contains(syntaxNode.SpanStart)));
                if (section != null &&
                    !IsReachableConstantSwitchGotoTarget(section, switchStatement, semanticModel) &&
                    SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                        switchStatement.Expression,
                        section,
                        semanticModel,
                        cancellationToken,
                        out var sectionCondition))
                {
                    AddArrayLengthCountAliasFact(
                        switchStatement.Expression,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                    pathConditions.Add(sectionCondition);
                }

                return;
            }

            if (ancestor is SwitchExpressionSyntax switchExpression)
            {
                var arm = switchExpression.Arms.FirstOrDefault(candidate =>
                    candidate.Expression.Span.Contains(syntaxNode.SpanStart));
                if (arm != null &&
                    SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                        switchExpression.GoverningExpression,
                        arm,
                        semanticModel,
                        cancellationToken,
                        out var armCondition))
                {
                    AddArrayLengthCountAliasFact(
                        switchExpression.GoverningExpression,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                    pathConditions.Add(armCondition);
                }
            }
        }

        private static void AddBranchConditionFact(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            SymbolicReachabilityService.TryAddBranchConditionFacts(
                expression,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                pathConditions,
                getSymbolVersion,
                collectDomainFactsBeforeBranchAssumptions: true,
                addTranslatedFormulaFallback: true);
        }

        private static void AddReferenceNullStateFact(
            ExpressionSyntax expression,
            bool equalToNull,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (SymbolicReachabilityService.TryCreateReferenceNullComparison(
                    expression,
                    semanticModel,
                    cancellationToken,
                    equalToNull,
                    out var formula,
                    getSymbolVersion))
            {
                pathConditions.Add(formula);
            }
        }

        private static void AddArrayLengthCountAliasFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (SymbolicReachabilityService.TryCreateArrayLengthCountAliasFact(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var aliasFact,
                    getSymbolVersion))
            {
                pathConditions.Add(aliasFact);
            }
        }

        private static bool IsProgramPointUnreachableUsingSharedFacts(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (IsInReachableConstantSwitchGotoSection(syntaxNode, semanticModel))
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

            return IsFormulaAlwaysFalseAt(
                sectionCondition,
                switchStatement,
                semanticModel,
                cancellationToken,
                smtAnalysis);
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

        private static bool IsReferenceKnownNullAt(
            ExpressionSyntax expression,
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (TryGetConstantReferenceNullState(expression, semanticModel, cancellationToken, out var isNull))
            {
                return isNull;
            }

            if (!SymbolicReachabilityService.TryCreateReferenceNullComparison(
                    expression,
                    semanticModel,
                    cancellationToken,
                    equalToNull: true,
                    out var nullFormula))
            {
                return false;
            }

            return IsFormulaAlwaysTrueAt(
                nullFormula,
                site,
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

        private static bool IsReferenceKnownNonNullAt(
            ExpressionSyntax expression,
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (TryGetConstantReferenceNullState(expression, semanticModel, cancellationToken, out var isNull))
            {
                return !isNull;
            }

            if (!SymbolicReachabilityService.TryCreateReferenceNullComparison(
                    expression,
                    semanticModel,
                    cancellationToken,
                    equalToNull: false,
                    out var nonNullFormula))
            {
                return false;
            }

            return IsFormulaAlwaysTrueAt(
                nonNullFormula,
                site,
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

        private static bool IsFormulaAlwaysFalseAt(
            SmtFormula formula,
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            var pathConditions = SymbolicReachabilityService.CollectPathConditionsAt(site, semanticModel, cancellationToken);
            return SymbolicReachabilityService.IsFormulaAlwaysFalseWithIrFirst(
                formula,
                pathConditions,
                site,
                smtAnalysis,
                "execution.visibility.query",
                "execution-visibility-query");
        }

        private static bool IsFormulaAlwaysTrueAt(
            SmtFormula formula,
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            var pathConditions = SymbolicReachabilityService.CollectPathConditionsAt(site, semanticModel, cancellationToken);
            return SymbolicReachabilityService.IsFormulaAlwaysTrueWithIrFirst(
                formula,
                pathConditions,
                site,
                smtAnalysis,
                "execution.visibility.query",
                "execution-visibility-query");
        }

        private static bool TryGetConstantReferenceNullState(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool isNull)
        {
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue)
            {
                isNull = constantValue.Value == null;
                return true;
            }

            isNull = false;
            return false;
        }

        private static bool IsConditionAlwaysFalseAt(
            ExpressionSyntax expression,
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return EvaluateKnownConditionTruthAtSite(
                expression,
                site,
                semanticModel,
                cancellationToken,
                smtAnalysis) == false;
        }

        private static bool IsConditionAlwaysTrueAt(
            ExpressionSyntax expression,
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return EvaluateKnownConditionTruthAtSite(
                expression,
                site,
                semanticModel,
                cancellationToken,
                smtAnalysis) == true;
        }

        private static bool? EvaluateKnownConditionTruthAtSite(
            ExpressionSyntax expression,
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            var key = new ConditionTruthCacheKey(
                expression.SpanStart,
                expression.Span.Length,
                site.SpanStart,
                site.Span.Length,
                smtAnalysis);
            var cache = s_conditionTruthCache.GetOrCreateValue(semanticModel);
            if (cache.Values.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var truth = SymbolicReachabilityService.EvaluateKnownConditionTruth(
                expression,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                SymbolicReachabilityService.CollectPathConditionsAt(site, semanticModel, cancellationToken));
            cache.Values.TryAdd(key, truth);
            return truth;
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
            return SymbolicReachabilityService.EvaluateKnownConditionTruth(
                expression,
                semanticModel,
                cancellationToken,
                smtAnalysis) == true;
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
            return SymbolicReachabilityService.EvaluateKnownConditionTruth(
                expression,
                semanticModel,
                cancellationToken,
                smtAnalysis) == false;
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

        private sealed class ConditionTruthCache
        {
            public ConcurrentDictionary<ConditionTruthCacheKey, bool?> Values { get; } = new();
        }

        private readonly struct ConditionTruthCacheKey : IEquatable<ConditionTruthCacheKey>
        {
            public ConditionTruthCacheKey(
                int expressionStart,
                int expressionLength,
                int siteStart,
                int siteLength,
                SmtAnalysisService? smtAnalysis)
            {
                ExpressionStart = expressionStart;
                ExpressionLength = expressionLength;
                SiteStart = siteStart;
                SiteLength = siteLength;
                SmtAnalysis = smtAnalysis;
            }

            public int ExpressionStart { get; }
            public int ExpressionLength { get; }
            public int SiteStart { get; }
            public int SiteLength { get; }
            public SmtAnalysisService? SmtAnalysis { get; }

            public bool Equals(ConditionTruthCacheKey other)
            {
                return ExpressionStart == other.ExpressionStart &&
                    ExpressionLength == other.ExpressionLength &&
                    SiteStart == other.SiteStart &&
                    SiteLength == other.SiteLength &&
                    ReferenceEquals(SmtAnalysis, other.SmtAnalysis);
            }

            public override bool Equals(object? obj)
            {
                return obj is ConditionTruthCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = ExpressionStart;
                    hash = (hash * 397) ^ ExpressionLength;
                    hash = (hash * 397) ^ SiteStart;
                    hash = (hash * 397) ^ SiteLength;
                    hash = (hash * 397) ^ RuntimeHelpers.GetHashCode(SmtAnalysis);
                    return hash;
                }
            }
        }

    }
}
