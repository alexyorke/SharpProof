using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
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
                if (declarator.Initializer == null) continue;

                RemoveFactsInvalidatedByNestedMutations(declarator.Initializer.Value, semanticModel, cancellationToken,
                    facts);
                if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol localSymbol)
                    continue;

                AddAssignedValueFacts(localSymbol, declarator.Initializer.Value, semanticModel, cancellationToken,
                    facts);
                AddExpressionNormalCompletionFacts(
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
            if (TryHandleTupleDeconstructionDeclaration(assignment, semanticModel, cancellationToken, facts)) return;

            if (TryHandleTupleAssignment(assignment, semanticModel, cancellationToken, facts)) return;

            SmtFormula? previousAssignedValue = null;
            if (TryGetMutatedLocalOrParameterSymbol(assignment, semanticModel, cancellationToken,
                    out var assignedSymbol))
                SymbolicReachabilityService.TryGetCurrentSymbolValue(facts, assignedSymbol, out previousAssignedValue);

            RemoveFactsInvalidatedByNestedMutations(assignment.Left, semanticModel, cancellationToken, facts);
            RemoveFactsInvalidatedByNestedMutations(assignment.Right, semanticModel, cancellationToken, facts);

            if (TryGetMutatedLocalOrParameterSymbol(assignment, semanticModel, cancellationToken, out assignedSymbol))
            {
                RemoveFactsReferencingSymbol(facts, assignedSymbol);
                if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    AddAssignedValueFacts(assignedSymbol, assignment.Right, semanticModel, cancellationToken, facts);
                else if (previousAssignedValue != null &&
                         SymbolicReachabilityService.TryCreateCompoundAssignmentFact(
                             assignedSymbol,
                             previousAssignedValue,
                             assignment,
                             semanticModel,
                             cancellationToken,
                             ExpressionReferencesSymbol(assignment.Right, assignedSymbol, semanticModel,
                                 cancellationToken),
                             out var compoundAssignmentFact))
                    facts.Add(compoundAssignmentFact);
            }

            AddExpressionNormalCompletionFacts(
                assignment.Right,
                expressionStatement,
                semanticModel,
                cancellationToken,
                facts,
                true);
            return;
        }

        if (statement is ExpressionStatementSyntax unaryExpressionStatement &&
            TryGetIncrementedOrDecrementedSymbol(
                unaryExpressionStatement.Expression,
                semanticModel,
                cancellationToken,
                out var incrementedSymbol,
                out var delta) &&
            SymbolicReachabilityService.TryGetCurrentSymbolValue(facts, incrementedSymbol,
                out var previousIncrementedValue))
        {
            RemoveFactsReferencingSymbol(facts, incrementedSymbol);
            if (SymbolicReachabilityService.TryCreateIncrementOrDecrementFact(
                    incrementedSymbol,
                    previousIncrementedValue,
                    delta,
                    out var mutationFact))
                facts.Add(mutationFact);

            return;
        }

        foreach (var node in statement.DescendantNodesAndSelf(candidate =>
                     !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            if (TryGetMutatedLocalOrParameterSymbol(node, semanticModel, cancellationToken, out var mutatedSymbol))
                RemoveFactsReferencingSymbol(facts, mutatedSymbol);

        if (statement is IfStatementSyntax ifStatement)
            AddCompletedIfStatementFacts(ifStatement, semanticModel, cancellationToken, facts);
        else if (statement is ExpressionStatementSyntax completedExpressionStatement)
            AddExpressionNormalCompletionFacts(
                completedExpressionStatement.Expression,
                completedExpressionStatement,
                semanticModel,
                cancellationToken,
                facts);
        else
            foreach (var loopFact in SymbolicReachabilityService.CollectCompletedLoopExitInvariantFacts(statement,
                         semanticModel, cancellationToken))
                facts.Add(loopFact);
    }

    private static void AddExpressionNormalCompletionFacts(
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        List<SmtFormula> facts,
        bool addArrayFactsFirst = false)
    {
        if (addArrayFactsFirst)
            AddArrayCreationNormalCompletionFacts(expression, statement, semanticModel, cancellationToken, facts);

        AddNotNullParameterNormalCompletionFacts(expression, statement, semanticModel, cancellationToken, facts);
        AddDoesNotReturnIfNormalCompletionFacts(expression, statement, semanticModel, cancellationToken, facts);

        if (!addArrayFactsFirst)
            AddArrayCreationNormalCompletionFacts(expression, statement, semanticModel, cancellationToken, facts);
    }

    private static void AddNotNullParameterNormalCompletionFacts(
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> facts)
    {
        expression = UnwrapAwaitedFactExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation)
            return;

        foreach (var argument in invocationOperation.Arguments)
        {
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                argument.Parameter is not { RefKind: RefKind.None, IsParams: false } parameter ||
                !ParameterHasNotNullAttribute(parameter) ||
                argument.Syntax is not ArgumentSyntax argumentSyntax ||
                !argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.None) ||
                GetLocalOrParameterSymbol(argumentSyntax.Expression, semanticModel, cancellationToken) is not
                { } argumentSymbol ||
                AnyConditionSymbolMutatedInStatement(argumentSyntax.Expression, statement, semanticModel,
                    cancellationToken))
                continue;

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
                SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                NotNullAttributeName,
                StringComparison.Ordinal));
    }

    private static void AddDoesNotReturnIfNormalCompletionFacts(
        ExpressionSyntax expression,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> facts)
    {
        expression = UnwrapAwaitedFactExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation)
            return;

        foreach (var argument in invocationOperation.Arguments)
        {
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                argument.Parameter is not { RefKind: RefKind.None, IsParams: false } parameter ||
                !TryGetDoesNotReturnIfValue(parameter, out var doesNotReturnWhen) ||
                argument.Syntax is not ArgumentSyntax argumentSyntax ||
                !argumentSyntax.RefKindKeyword.IsKind(SyntaxKind.None) ||
                AnyConditionSymbolMutatedInStatement(argumentSyntax.Expression, statement, semanticModel,
                    cancellationToken))
                continue;

            TryAddPathCondition(
                argumentSyntax.Expression,
                !doesNotReturnWhen,
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
                    SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                    DoesNotReturnIfAttributeName,
                    StringComparison.Ordinal) ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not bool attributeValue)
                continue;

            value = attributeValue;
            return true;
        }

        value = false;
        return false;
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
        CancellationToken cancellationToken,
        ICollection<SmtFormula> facts)
    {
        expression = UnwrapFactExpression(expression);
        if (expression is not ArrayCreationExpressionSyntax arrayCreation) return;

        foreach (var sizeExpression in CSharpSyntaxFacts.GetExplicitArraySizeExpressions(arrayCreation))
        {
            if (AnyConditionSymbolMutatedInStatement(sizeExpression, statement, semanticModel, cancellationToken) ||
                !SymbolicReachabilityService.TryCreateExpressionNonNegativeComparison(
                    sizeExpression,
                    semanticModel,
                    cancellationToken,
                    out var nonNegativeSizeFormula))
                continue;

            facts.Add(nonNegativeSizeFormula);
        }
    }

    private static void AddSymbolNonNullFact(
        ISymbol symbol,
        ICollection<SmtFormula> facts)
    {
        if (!SymbolicReachabilityService.TryCreateSymbolReferenceNullComparison(
                symbol,
                false,
                out var formula))
            return;

        facts.Add(formula);
    }

    private static void AddCompletedIfStatementFacts(
        IfStatementSyntax ifStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> facts)
    {
        if (StatementDefinitelyExits(ifStatement.Statement) &&
            (ifStatement.Else?.Statement == null ||
             !AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Else.Statement, semanticModel,
                 cancellationToken)))
            SymbolicReachabilityService.TryAddBranchConditionFacts(
                ifStatement.Condition,
                false,
                semanticModel,
                cancellationToken,
                facts);

        if (ifStatement.Else?.Statement is { } elseStatement &&
            StatementDefinitelyExits(elseStatement) &&
            !AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Statement, semanticModel,
                cancellationToken))
            SymbolicReachabilityService.TryAddBranchConditionFacts(
                ifStatement.Condition,
                true,
                semanticModel,
                cancellationToken,
                facts);
    }

    private static void AddCompletedIfStatementFacts(
        IfStatementSyntax ifStatement,
        IReadOnlyCollection<ISymbol> invalidatedSymbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<SmtFormula> facts)
    {
        if (StatementDefinitelyExits(ifStatement.Statement) &&
            (ifStatement.Else?.Statement == null ||
             !AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Else.Statement, semanticModel,
                 cancellationToken)))
            TryAddPathCondition(ifStatement.Condition, false, semanticModel, cancellationToken, facts);

        if (ifStatement.Else?.Statement is { } elseStatement &&
            StatementDefinitelyExits(elseStatement) &&
            !AnyConditionSymbolMutatedInStatement(ifStatement.Condition, ifStatement.Statement, semanticModel,
                cancellationToken))
            TryAddPathCondition(ifStatement.Condition, true, semanticModel, cancellationToken, facts);
    }

    private static void AddAssignedValueFacts(
        ISymbol targetSymbol,
        ExpressionSyntax valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
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
        var effectiveValueDoesNotReferenceTarget =
            !ExpressionReferencesSymbol(effectiveValueExpression, targetSymbol, semanticModel, cancellationToken);

        if (effectiveValueDoesNotReferenceTarget)
        {
            if (SymbolicReachabilityService.TryCreateAssignedValueFact(
                    targetSymbol,
                    effectiveValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var assignedValueFact))
                facts.Add(assignedValueFact);

            SymbolicReachabilityService.AddNullableAssignedValueFacts(
                targetSymbol,
                effectiveValueExpression,
                semanticModel,
                cancellationToken,
                facts);

            if (SymbolicReachabilityService.TryCreateBuiltInLengthAssignedValueFact(
                    targetSymbol,
                    effectiveValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var builtInLengthFact))
                facts.Add(builtInLengthFact);

            if (SymbolicReachabilityService.TryCreateReferenceBackedLengthFact(
                    targetSymbol,
                    effectiveValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var referenceLengthFact))
                facts.Add(referenceLengthFact);

            if (SymbolicReachabilityService.TryCreateCollectionExpressionLengthLowerBoundFact(
                    targetSymbol,
                    effectiveValueExpression,
                    out var lowerBoundLengthFact))
                facts.Add(lowerBoundLengthFact);

            SymbolicReachabilityService.AddArrayDimensionLengthAssignedValueFacts(
                targetSymbol,
                effectiveValueExpression,
                semanticModel,
                cancellationToken,
                facts);

            SymbolicReachabilityService.AddReferenceBackedArrayDimensionLengthFacts(
                targetSymbol,
                effectiveValueExpression,
                semanticModel,
                cancellationToken,
                facts);

            if (SymbolicReachabilityService.TryCreateStringContentAssignedValueFact(
                    targetSymbol,
                    effectiveValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var stringContentFact))
                facts.Add(stringContentFact);

            if (SymbolicReachabilityService.TryCreateReferenceBackedStringContentFact(
                    targetSymbol,
                    effectiveValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var referenceStringFact))
                facts.Add(referenceStringFact);

            if (SymbolicReachabilityService.TryCreateStringNonNullAssignedValueFact(
                    targetSymbol,
                    effectiveValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var stringNonNullFact))
                facts.Add(stringNonNullFact);
        }

        if (hasThrowGuard &&
            guardExpression != null &&
            (!ExpressionReferencesSymbol(guardExpression, targetSymbol, semanticModel, cancellationToken) ||
             effectiveValueIsTarget))
            SymbolicReachabilityService.TryAddBranchConditionFacts(
                guardExpression,
                guardBranchWhenTrue,
                semanticModel,
                cancellationToken,
                facts);
        else if (hasThrowGuard &&
                 requiresNonNullValue &&
                 effectiveValueDoesNotReferenceTarget)
            AddReferenceNonNullFact(effectiveValueExpression, semanticModel, cancellationToken, facts);
        else if (hasThrowGuard &&
                 requiresNonNullValue &&
                 effectiveValueIsTarget)
            AddSymbolNonNullFact(targetSymbol, facts);
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
        CancellationToken cancellationToken,
        List<SmtFormula> facts)
    {
        if (!SymbolicReachabilityService.TryCreateReferenceNullComparison(
                expression,
                semanticModel,
                cancellationToken,
                false,
                out var formula,
                null))
            return;

        facts.Add(formula);
    }
}