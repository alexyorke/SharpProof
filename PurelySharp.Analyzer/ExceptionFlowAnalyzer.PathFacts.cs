using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Analyzer.Engine;
using PurelySharp.Analyzer.Engine.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Analyzer
{
    internal static partial class ExceptionFlowAnalyzer
    {
        private static bool IsKnownByDominatingIf(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            PathFactKind factKind,
            SmtAnalysisService smtAnalysis)
        {
            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            if (symbol == null)
            {
                return false;
            }

            if (!TryCreateFactFormula(symbol, factKind, out var factFormula) || factFormula == null)
            {
                return false;
            }

            var pathConditions = new List<SmtFormula>();
            foreach (var ifStatement in useNode.Ancestors().OfType<IfStatementSyntax>())
            {
                if (ifStatement.Statement.Span.Contains(useNode.SpanStart) &&
                    !IsSymbolAssignedBeforeUse(ifStatement.Statement, useNode.SpanStart, symbol, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, pathConditions);
                }

                if (ifStatement.Else?.Statement is { } elseStatement &&
                    elseStatement.Span.Contains(useNode.SpanStart) &&
                    !IsSymbolAssignedBeforeUse(elseStatement, useNode.SpanStart, symbol, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, pathConditions);
                }
            }

            AddSwitchPathConditions(useNode, new[] { symbol }, semanticModel, cancellationToken, pathConditions);
            AddPrecedingGuardConditions(symbol, useNode, semanticModel, cancellationToken, pathConditions);
            return pathConditions.Count > 0 && PathConditionsImplyFact(pathConditions, factFormula, smtAnalysis);
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
                    ifStatement.Else == null &&
                    StatementDefinitelyExits(ifStatement.Statement) &&
                    !IsSymbolAssignedBetween(block, ifStatement.Span.End, useNode.SpanStart, symbol, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, pathConditions);
                }
            }
        }

        private static List<SmtFormula> CollectPathConditionsForUse(
            SyntaxNode useNode,
            IReadOnlyCollection<ISymbol> invalidatedSymbols,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var pathConditions = new List<SmtFormula>();
            AddPriorAssignmentPathConditions(useNode, semanticModel, cancellationToken, pathConditions);

            foreach (var ifStatement in useNode.Ancestors().OfType<IfStatementSyntax>())
            {
                if (ifStatement.Statement.Span.Contains(useNode.SpanStart) &&
                    !AnySymbolAssignedBeforeUse(ifStatement.Statement, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, pathConditions);
                }

                if (ifStatement.Else?.Statement is { } elseStatement &&
                    elseStatement.Span.Contains(useNode.SpanStart) &&
                    !AnySymbolAssignedBeforeUse(elseStatement, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, pathConditions);
                }
            }

            AddSwitchPathConditions(useNode, invalidatedSymbols, semanticModel, cancellationToken, pathConditions);
            AddPrecedingGuardConditions(invalidatedSymbols, useNode, semanticModel, cancellationToken, pathConditions);
            return pathConditions;
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
                    matchingSection.Labels.Any(static label => label is DefaultSwitchLabelSyntax) ||
                    AnySymbolMutatedInSyntax(switchStatement.Expression, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnySymbolAssignedBeforeUse(matchingSection, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    continue;
                }

                TryAddSwitchStatementSectionCondition(
                    switchStatement.Expression,
                    matchingSection,
                    semanticModel,
                    cancellationToken,
                    pathConditions);
            }

            foreach (var switchExpression in useNode.Ancestors().OfType<SwitchExpressionSyntax>())
            {
                var matchingArm = switchExpression.Arms
                    .FirstOrDefault(arm => arm.Expression.Span.Contains(useNode.SpanStart));
                if (matchingArm == null ||
                    AnySymbolMutatedInSyntax(switchExpression.GoverningExpression, invalidatedSymbols, semanticModel, cancellationToken) ||
                    AnySymbolAssignedBeforeUse(matchingArm, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    continue;
                }

                TryAddSwitchExpressionArmCondition(
                    switchExpression.GoverningExpression,
                    matchingArm,
                    semanticModel,
                    cancellationToken,
                    pathConditions);
            }
        }

        private static void TryAddSwitchStatementSectionCondition(
            ExpressionSyntax governingExpression,
            SwitchSectionSyntax section,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            var labelConditions = new List<SmtFormula>();
            foreach (var label in section.Labels)
            {
                if (TryCreateSwitchLabelCondition(
                    governingExpression,
                    label,
                    semanticModel,
                    cancellationToken,
                    out var labelCondition))
                {
                    labelConditions.Add(labelCondition);
                }
            }

            if (TryCreateDisjunction(labelConditions, out var sectionCondition))
            {
                pathConditions.Add(sectionCondition);
            }
        }

        private static void TryAddSwitchExpressionArmCondition(
            ExpressionSyntax governingExpression,
            SwitchExpressionArmSyntax arm,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            if (TryTranslateSwitchGoverningValue(governingExpression, semanticModel, cancellationToken, out var governingValue) &&
                TryCreatePatternAndGuardCondition(
                    governingValue,
                    arm.Pattern,
                    arm.WhenClause,
                    semanticModel,
                    cancellationToken,
                    out var armCondition))
            {
                pathConditions.Add(armCondition);
            }
        }

        private static bool TryCreateSwitchLabelCondition(
            ExpressionSyntax governingExpression,
            SwitchLabelSyntax label,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            formula = null!;
            if (!TryTranslateSwitchGoverningValue(governingExpression, semanticModel, cancellationToken, out var governingValue))
            {
                return false;
            }

            if (label is CaseSwitchLabelSyntax caseLabel &&
                CSharpConditionToFormula.TryTranslateValue(
                    caseLabel.Value,
                    semanticModel,
                    cancellationToken,
                    out var caseValue,
                    getSymbolVersion: null) &&
                caseValue != null &&
                CanCompareSmtValues(governingValue, caseValue))
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, governingValue, caseValue);
                return true;
            }

            if (label is CasePatternSwitchLabelSyntax patternLabel)
            {
                return TryCreatePatternAndGuardCondition(
                    governingValue,
                    patternLabel.Pattern,
                    patternLabel.WhenClause,
                    semanticModel,
                    cancellationToken,
                    out formula);
            }

            return false;
        }

        private static bool TryTranslateSwitchGoverningValue(
            ExpressionSyntax governingExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            if (CSharpConditionToFormula.TryTranslateValue(
                    governingExpression,
                    semanticModel,
                    cancellationToken,
                    out var governingValue,
                    getSymbolVersion: null) &&
                governingValue != null &&
                governingValue.Kind is SmtValueKind.Bool or SmtValueKind.Int or SmtValueKind.Reference)
            {
                formula = governingValue;
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreatePatternAndGuardCondition(
            SmtFormula governingValue,
            PatternSyntax pattern,
            WhenClauseSyntax? whenClause,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            formula = null!;
            var conditions = new List<SmtFormula>();
            if (CSharpConditionToFormula.TryTranslatePattern(
                    governingValue,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    out var patternFormula,
                    getSymbolVersion: null) &&
                patternFormula != null)
            {
                conditions.Add(patternFormula);
            }

            if (whenClause != null &&
                CSharpConditionToFormula.TryTranslate(
                    whenClause.Condition,
                    semanticModel,
                    cancellationToken,
                    out var guardFormula,
                    getSymbolVersion: null) &&
                guardFormula != null)
            {
                conditions.Add(guardFormula);
            }

            return TryCreateConjunction(conditions, out formula);
        }

        private static bool TryCreateConjunction(IReadOnlyList<SmtFormula> formulas, out SmtFormula formula)
        {
            return TryCreateAssociativeFormula(SmtBinaryOperator.And, formulas, out formula);
        }

        private static bool TryCreateDisjunction(IReadOnlyList<SmtFormula> formulas, out SmtFormula formula)
        {
            return TryCreateAssociativeFormula(SmtBinaryOperator.Or, formulas, out formula);
        }

        private static bool TryCreateAssociativeFormula(
            SmtBinaryOperator smtOperator,
            IReadOnlyList<SmtFormula> formulas,
            out SmtFormula formula)
        {
            formula = null!;
            if (formulas.Count == 0)
            {
                return false;
            }

            formula = formulas[0];
            for (var index = 1; index < formulas.Count; index++)
            {
                formula = new SmtBinaryFormula(smtOperator, formula, formulas[index]);
            }

            return true;
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
                    ifStatement.Else == null &&
                    StatementDefinitelyExits(ifStatement.Statement) &&
                    !AnySymbolAssignedBetween(block, ifStatement.Span.End, useNode.SpanStart, invalidatedSymbols, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, pathConditions);
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

        private static void AddPriorAssignmentPathConditions(
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            var facts = new List<SmtFormula>();
            foreach (var containingBlock in EnumerateContainingBlocks(useNode).Reverse())
            {
                foreach (var statement in containingBlock.Block.Statements)
                {
                    if (ReferenceEquals(statement, containingBlock.ContainingStatement))
                    {
                        break;
                    }

                    AddPriorStatementFacts(statement, semanticModel, cancellationToken, facts);
                }
            }

            foreach (var fact in facts)
            {
                pathConditions.Add(fact);
            }
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
                }

                return;
            }

            if (statement is ExpressionStatementSyntax expressionStatement &&
                expressionStatement.Expression is AssignmentExpressionSyntax assignment)
            {
                RemoveFactsInvalidatedByNestedMutations(assignment.Left, semanticModel, cancellationToken, facts);
                RemoveFactsInvalidatedByNestedMutations(assignment.Right, semanticModel, cancellationToken, facts);

                if (TryGetMutatedLocalOrParameterSymbol(assignment, semanticModel, cancellationToken, out var assignedSymbol))
                {
                    RemoveFactsReferencingSymbol(facts, assignedSymbol);
                    if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    {
                        AddAssignedValueFacts(assignedSymbol, assignment.Right, semanticModel, cancellationToken, facts);
                    }
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
        }

        private static void AddAssignedValueFacts(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            RemoveFactsReferencingSymbol(facts, targetSymbol);

            if (TryCreateSymbolSmtValue(targetSymbol, out var targetFormula) &&
                !ExpressionReferencesSymbol(valueExpression, targetSymbol, semanticModel, cancellationToken) &&
                CSharpConditionToFormula.TryTranslateValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion: null) &&
                valueFormula != null &&
                CanCompareSmtValues(targetFormula, valueFormula))
            {
                facts.Add(CreateAssignedValueFact(targetFormula, valueFormula));
            }

            if (TryCreateArrayLengthFormula(targetSymbol, out var targetLengthFormula) &&
                !ExpressionReferencesSymbol(valueExpression, targetSymbol, semanticModel, cancellationToken) &&
                TryCreateArrayLengthValueFormula(valueExpression, semanticModel, cancellationToken, out var valueLengthFormula))
            {
                facts.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, targetLengthFormula, valueLengthFormula));
            }
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

        private static bool TryCreateArrayLengthFormula(ISymbol symbol, out SmtFormula formula)
        {
            if (symbol is ILocalSymbol { Type: IArrayTypeSymbol { Rank: 1 } } or
                IParameterSymbol { Type: IArrayTypeSymbol { Rank: 1 } })
            {
                var receiverFormula = new SmtVariable(GetSmtVariableName(symbol), SmtValueKind.Reference);
                formula = new SmtVariable(receiverFormula + ".Length", SmtValueKind.Int);
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateArrayLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            valueExpression = UnwrapFactExpression(valueExpression);
            var valueType = semanticModel.GetTypeInfo(valueExpression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(valueExpression, cancellationToken).Type;
            if (valueType is not IArrayTypeSymbol { Rank: 1 })
            {
                formula = null!;
                return false;
            }

            if (valueExpression is ArrayCreationExpressionSyntax arrayCreation)
            {
                if (arrayCreation.Type.RankSpecifiers.Count == 1 &&
                    arrayCreation.Type.RankSpecifiers[0].Sizes.Count == 1 &&
                    !arrayCreation.Type.RankSpecifiers[0].Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression) &&
                    CSharpConditionToFormula.TryTranslateValue(
                        arrayCreation.Type.RankSpecifiers[0].Sizes[0],
                        semanticModel,
                        cancellationToken,
                        out var sizeFormula,
                        getSymbolVersion: null) &&
                    sizeFormula is { Kind: SmtValueKind.Int })
                {
                    formula = sizeFormula;
                    return true;
                }

                if (arrayCreation.Initializer != null)
                {
                    formula = new SmtIntegerConstant(arrayCreation.Initializer.Expressions.Count);
                    return true;
                }
            }

            if (valueExpression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation)
            {
                formula = new SmtIntegerConstant(implicitArrayCreation.Initializer.Expressions.Count);
                return true;
            }

            if (IsArrayEmptyInvocation(valueExpression, semanticModel, cancellationToken))
            {
                formula = new SmtIntegerConstant(0);
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool IsArrayEmptyInvocation(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return valueExpression is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol
                {
                    Name: "Empty",
                    IsStatic: true,
                    ContainingType.SpecialType: SpecialType.System_Array
                };
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

        private static bool ExpressionMatchesFact(
            ExpressionSyntax expression,
            PathFactKind factKind,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            expression = UnwrapFactExpression(expression);
            if (factKind == PathFactKind.Null)
            {
                return expression.IsKind(SyntaxKind.NullLiteralExpression);
            }

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            return constantValue.HasValue && IsIntegralOrDecimalZero(constantValue.Value);
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
            return typeSymbol.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64;
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

        private static bool PathConditionsImplyFact(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SmtAnalysisService smtAnalysis)
        {
            var query = new PurityProofQuery(
                pathConditions.ToArray(),
                new PurityHazard(
                    PurityHazardKind.BranchReachability,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, factFormula)));

            var proofResult = smtAnalysis.Classify(query);
            return proofResult.Outcome == PurityProofOutcome.ProvablyPure;
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

                if (MutatesSymbol(node, symbol, semanticModel, cancellationToken))
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
                    ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken),
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
                    return true;
                case BlockSyntax block:
                    return block.Statements.LastOrDefault() is ReturnStatementSyntax or ThrowStatementSyntax;
                default:
                    return false;
            }
        }
    }
}
