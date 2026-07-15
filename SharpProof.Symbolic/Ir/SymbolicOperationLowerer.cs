using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicOperationLowerer
{
    internal static SymbolicLoweringResult<SymbolicOperationSequence> Lower(
        IOperation operation,
        SymbolicLoweringContext targetContext,
        SymbolicLoweringContext valueContext,
        int sequence = 0)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        if (targetContext == null) throw new ArgumentNullException(nameof(targetContext));
        if (valueContext == null) throw new ArgumentNullException(nameof(valueContext));

        targetContext.CancellationToken.ThrowIfCancellationRequested();
        return operation switch
        {
            IExpressionStatementOperation expressionStatement =>
                Lower(expressionStatement.Operation, targetContext, valueContext, sequence),
            IVariableDeclaratorOperation { Initializer.Value: { } value } declarator =>
                LowerSimpleAssignment(
                    declarator.Symbol,
                    value,
                    declarator.Syntax,
                    targetContext,
                    valueContext,
                    sequence,
                    "operation-lowering.declaration"),
            ISimpleAssignmentOperation assignment when TryGetDirectTargetSymbol(assignment.Target, out var target) =>
                LowerSimpleAssignment(
                    target,
                    assignment.Value,
                    assignment.Syntax,
                    targetContext,
                    valueContext,
                    sequence,
                    "operation-lowering.assignment"),
            _ => Unsupported(operation, "operation-lowering.unsupported")
        };
    }

    internal static SymbolicLoweringResult<SymbolicOperationSequence> LowerSimpleAssignment(
        ISymbol targetSymbol,
        IOperation valueOperation,
        SyntaxNode source,
        SymbolicLoweringContext targetContext,
        SymbolicLoweringContext valueContext,
        int sequence,
        string provenance,
        string? bindingProvenance = null,
        string? evidenceKey = null)
    {
        if (!TryCreateSymbolTerm(targetSymbol, targetContext, out var target) ||
            valueOperation.Syntax is not ExpressionSyntax valueExpression)
            return Unsupported(source, provenance + ".target");

        var value = target.Kind switch
        {
            SmtValueKind.Bool => SymbolicSemanticPipeline.LowerBooleanValueTerm(valueExpression, valueContext),
            SmtValueKind.Reference => SymbolicSemanticPipeline.LowerReferenceTerm(valueExpression, valueContext),
            _ => SymbolicSemanticPipeline.LowerTerm(valueExpression, valueContext)
        };
        if (value is not { IsExact: true, Value: { } sourceTerm } ||
            !SymbolicStateFactBuilder.CanCompareIrTerms(target, sourceTerm))
            return Unsupported(source, provenance + ".value");

        var operation = new SymbolicAssignmentOperation(
            System.Collections.Immutable.ImmutableArray.Create(
                new SymbolicAssignmentBinding(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol.OriginalDefinition),
                    target,
                    sourceTerm,
                    bindingProvenance,
                    evidenceKey)),
            System.Collections.Immutable.ImmutableArray<SymbolicCondition>.Empty,
            SymbolicAssignmentOperationKind.Simple,
            IsChecked: false,
            new SymbolicOperationOrigin(source.Span, sequence, provenance));
        return SymbolicLoweringResult<SymbolicOperationSequence>.Exact(
            SymbolicOperationSequence.Single(operation),
            new SymbolicLoweringProvenance("roslyn-to-operation", source.Span, provenance));
    }

    internal static SymbolicLoweringResult<SymbolicOperationSequence> LowerComputedUpdate(
        ISymbol targetSymbol,
        SymbolicTerm sourceTerm,
        SyntaxNode source,
        SymbolicLoweringContext targetContext,
        SymbolicComputedUpdateKind updateKind,
        bool isChecked,
        int sequence,
        string provenance)
    {
        if (!TryCreateSymbolTerm(targetSymbol, targetContext, out var target) ||
            !SymbolicStateFactBuilder.CanCompareIrTerms(target, sourceTerm) ||
            SymbolicIrReferenceScanner.ContainsVariableOrMember(
                sourceTerm,
                SymbolicFactFactory.GetSmtVariableName(targetSymbol.OriginalDefinition)))
            return Unsupported(source, provenance + ".value");

        var bindings = System.Collections.Immutable.ImmutableArray.Create(
                new SymbolicAssignmentBinding(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol.OriginalDefinition),
                    target,
                    sourceTerm,
                    provenance));
        var origin = new SymbolicOperationOrigin(source.Span, sequence, provenance);
        SymbolicOperationDescriptor operation = updateKind switch
        {
            SymbolicComputedUpdateKind.CompoundAssignment => new SymbolicAssignmentOperation(
                bindings,
                System.Collections.Immutable.ImmutableArray<SymbolicCondition>.Empty,
                SymbolicAssignmentOperationKind.Compound,
                isChecked,
                origin),
            _ => new SymbolicMutationOperation(
                bindings,
                System.Collections.Immutable.ImmutableArray<SymbolicInvalidationTarget>.Empty,
                updateKind == SymbolicComputedUpdateKind.Increment
                    ? SymbolicMutationOperationKind.Increment
                    : SymbolicMutationOperationKind.Decrement,
                isChecked,
                CallerVisible: false,
                origin)
        };
        return SymbolicLoweringResult<SymbolicOperationSequence>.Exact(
            SymbolicOperationSequence.Single(operation),
            new SymbolicLoweringProvenance("roslyn-to-operation", source.Span, provenance));
    }

    internal static SymbolicLoweringResult<SymbolicOperationSequence> LowerCoalesceAssignment(
        ISymbol targetSymbol,
        ExpressionSyntax rightExpression,
        SymbolicLoweringContext context,
        int sequence,
        string provenance)
    {
        SymbolicCondition postcondition;
        if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(rightExpression) is ThrowExpressionSyntax)
        {
            if (SymbolicAssignmentStateTransfer.TryCreateNullableSymbolTerms(targetSymbol, out var hasValue, out _))
                postcondition = ExactTruth(hasValue, rightExpression, provenance + ".throw-completion-has-value", targetSymbol);
            else if (TryCreateSymbolTerm(targetSymbol, context, out var reference) &&
                     reference.Kind == SmtValueKind.Reference)
                postcondition = ExactRelation(
                    SymbolicRelationOperator.NotEqual,
                    reference,
                    new SymbolicNullTerm(),
                    rightExpression,
                    provenance + ".throw-completion-non-null",
                    targetSymbol);
            else
                return Unsupported(rightExpression, provenance + ".target");
        }
        else if (SymbolicAssignmentStateTransfer.TryCreateNullableSymbolTerms(
                     targetSymbol,
                     out var targetHasValue,
                     out var targetValue))
        {
            var hasValue = SymbolicSemanticPipeline.LowerNullableHasValueTerm(rightExpression, context);
            SymbolicTerm? rightHasValue = hasValue is { IsExact: true, Value: { } loweredHasValue }
                ? loweredHasValue
                : SymbolicSemanticPipeline.LowerTerm(rightExpression, context) is
                    { IsExact: true, Value: { } rightValue } && rightValue.Kind == targetValue.Kind
                    ? new SymbolicBooleanConstantTerm(true)
                    : null;
            if (rightHasValue == null) return Unsupported(rightExpression, provenance + ".value");

            postcondition = rightHasValue is SymbolicBooleanConstantTerm { Value: true }
                ? ExactTruth(
                    targetHasValue,
                    rightExpression,
                    provenance + ".nullable-has-value",
                    targetSymbol)
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    ExactTruth(
                        targetHasValue,
                        rightExpression,
                        provenance + ".target-has-value",
                        targetSymbol),
                    new SymbolicNotCondition(ExactTruth(
                        rightHasValue,
                        rightExpression,
                        provenance + ".right-has-value")));
        }
        else if (TryCreateSymbolTerm(targetSymbol, context, out var target) &&
                 target.Kind == SmtValueKind.Reference)
        {
            var definitelyNonNull = SymbolicAssignmentStateTransfer.IsDefinitelyNonNullReferenceValue(
                rightExpression,
                context.SemanticModel,
                context.CancellationToken);
            var targetNonNull = ExactRelation(
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm(),
                rightExpression,
                provenance + (definitelyNonNull ? ".non-null" : ".target-non-null"),
                targetSymbol);
            if (definitelyNonNull)
                postcondition = targetNonNull;
            else if (SymbolicSemanticPipeline.LowerReferenceTerm(rightExpression, context) is
                     { IsExact: true, Value: { } right })
                postcondition = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    targetNonNull,
                    ExactRelation(
                        SymbolicRelationOperator.Equal,
                        target,
                        right,
                        rightExpression,
                        provenance + ".target-equals-right",
                        targetSymbol));
            else
                return Unsupported(rightExpression, provenance + ".value");
        }
        else
        {
            return Unsupported(rightExpression, provenance + ".target");
        }

        var operation = new SymbolicAssignmentOperation(
            System.Collections.Immutable.ImmutableArray<SymbolicAssignmentBinding>.Empty,
            System.Collections.Immutable.ImmutableArray.Create(postcondition),
            SymbolicAssignmentOperationKind.Coalesce,
            IsChecked: false,
            new SymbolicOperationOrigin(rightExpression.Span, sequence, provenance));
        return SymbolicLoweringResult<SymbolicOperationSequence>.Exact(
            SymbolicOperationSequence.Single(operation),
            new SymbolicLoweringProvenance("roslyn-to-operation", rightExpression.Span, provenance));
    }

    private static SymbolicCondition ExactTruth(
        SymbolicTerm value,
        SyntaxNode source,
        string provenance,
        ISymbol? symbol = null)
    {
        return new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicTruthAtom(value),
            source,
            provenance,
            symbol));
    }

    private static SymbolicCondition ExactRelation(
        SymbolicRelationOperator relation,
        SymbolicTerm left,
        SymbolicTerm right,
        SyntaxNode source,
        string provenance,
        ISymbol? symbol = null)
    {
        return new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(relation, left, right),
            source,
            provenance,
            symbol));
    }

    private static bool TryGetDirectTargetSymbol(IOperation target, out ISymbol symbol)
    {
        switch (target)
        {
            case ILocalReferenceOperation local:
                symbol = local.Local.OriginalDefinition;
                return true;
            case IParameterReferenceOperation parameter:
                symbol = parameter.Parameter.OriginalDefinition;
                return true;
            default:
                symbol = null!;
                return false;
        }
    }

    private static bool TryCreateSymbolTerm(
        ISymbol symbol,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var type = SymbolicFactFactory.GetTrackedSymbolType(symbol);
        if (type == null ||
            !SymbolicFactFactory.TryGetValueKind(
                type,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsReferenceType,
                out var kind))
        {
            term = null!;
            return false;
        }

        term = new SymbolicVariableTerm(context.GetVariableName(symbol), kind);
        return true;
    }

    private static SymbolicLoweringResult<SymbolicOperationSequence> Unsupported(
        IOperation operation,
        string provenance)
    {
        return Unsupported(operation.Syntax, provenance);
    }

    private static SymbolicLoweringResult<SymbolicOperationSequence> Unsupported(
        SyntaxNode source,
        string provenance)
    {
        return SymbolicLoweringResult<SymbolicOperationSequence>.Unsupported(
            new SymbolicLoweringProvenance("roslyn-to-operation", source.Span, provenance));
    }
}
