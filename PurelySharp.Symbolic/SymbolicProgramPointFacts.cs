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
                    if (ifStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                    {
                        AddReachabilityCondition(builder, ifStatementSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    }
                    else if (ifStatementSyntax.Else?.Statement.Span.Contains(syntaxNode.Span) == true)
                    {
                        AddReachabilityCondition(builder, ifStatementSyntax.Condition, mustBeTrue: false, semanticModel, cancellationToken);
                    }
                }
                else if (ancestor is ConditionalExpressionSyntax conditionalExpressionSyntax)
                {
                    if (conditionalExpressionSyntax.WhenTrue.Span.Contains(syntaxNode.Span))
                    {
                        AddReachabilityCondition(builder, conditionalExpressionSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                    }
                    else if (conditionalExpressionSyntax.WhenFalse.Span.Contains(syntaxNode.Span))
                    {
                        AddReachabilityCondition(builder, conditionalExpressionSyntax.Condition, mustBeTrue: false, semanticModel, cancellationToken);
                    }
                }
                else if (ancestor is BinaryExpressionSyntax binaryExpressionSyntax &&
                         binaryExpressionSyntax.Right.Span.Contains(syntaxNode.Span))
                {
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
                         conditionalAccessExpressionSyntax.WhenNotNull.Span.Contains(syntaxNode.SpanStart))
                {
                    AddReferenceNullCondition(
                        builder,
                        conditionalAccessExpressionSyntax.Expression,
                        isNull: false,
                        semanticModel,
                        cancellationToken);
                }
                else if (ancestor is WhileStatementSyntax whileStatementSyntax &&
                         whileStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    AddReachabilityCondition(builder, whileStatementSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                }
                else if (ancestor is ForStatementSyntax forStatementSyntax &&
                         forStatementSyntax.Condition != null &&
                         forStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    AddReachabilityCondition(builder, forStatementSyntax.Condition, mustBeTrue: true, semanticModel, cancellationToken);
                }
                else if (ancestor is SwitchStatementSyntax switchStatementSyntax)
                {
                    var matchingSection = switchStatementSyntax.Sections
                        .FirstOrDefault(section => section.Span.Contains(syntaxNode.SpanStart));
                    if (matchingSection != null &&
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
            valueExpression = UnwrapExpression(valueExpression);
            var valueType = semanticModel.GetTypeInfo(valueExpression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(valueExpression, cancellationToken).Type;
            if (valueType is IArrayTypeSymbol { Rank: 1 })
            {
                return TryCreateArrayLengthValueFormula(valueExpression, semanticModel, cancellationToken, out formula);
            }

            if (valueType?.SpecialType == SpecialType.System_String)
            {
                return TryCreateStringLengthValueFormula(valueExpression, semanticModel, cancellationToken, out formula);
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateArrayLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
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
                        getSymbolVersion: null,
                        inlineDepth: 0) &&
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

            if (TryCreateCollectionExpressionLengthFormula(valueExpression, out formula))
            {
                return true;
            }

            return TryCreateReferenceLengthValueFormula(valueExpression, semanticModel, cancellationToken, out formula);
        }

        private static bool TryCreateStringLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            if (CSharpConditionToFormula.TryGetKnownStringLength(valueExpression, semanticModel, cancellationToken, out var stringLength))
            {
                formula = new SmtIntegerConstant(stringLength);
                return true;
            }

            return TryCreateReferenceLengthValueFormula(valueExpression, semanticModel, cancellationToken, out formula);
        }

        private static bool TryCreateReferenceLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            if (CSharpConditionToFormula.TryTranslateValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion: null,
                    inlineDepth: 0) &&
                receiverFormula is SmtVariable { Kind: SmtValueKind.Reference })
            {
                formula = new SmtVariable(receiverFormula + ".Length", SmtValueKind.Int);
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateCollectionExpressionLengthFormula(
            ExpressionSyntax valueExpression,
            out SmtFormula formula)
        {
            if (valueExpression is not CollectionExpressionSyntax collectionExpression ||
                collectionExpression.Elements.Any(static element => element is not ExpressionElementSyntax))
            {
                formula = null!;
                return false;
            }

            formula = new SmtIntegerConstant(collectionExpression.Elements.Count);
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
