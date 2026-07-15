using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Ir;

internal enum SymbolicAssignmentPostconditionProfile
{
    Analyzer,
    Symbolic
}

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
        string? evidenceKey = null,
        string? asExpressionProvenanceRoot = null,
        SymbolicAssignmentPostconditionProfile postconditionProfile =
            SymbolicAssignmentPostconditionProfile.Analyzer)
    {
        if (!TryCreateSymbolTerm(targetSymbol, targetContext, out var target) ||
            valueOperation.Syntax is not ExpressionSyntax valueExpression)
            return Unsupported(source, provenance + ".target");

        var value = target.Kind switch
        {
            SmtValueKind.Bool => SymbolicSemanticPipeline.LowerBooleanValueTerm(valueExpression, valueContext),
            SmtValueKind.Reference => SymbolicSemanticPipeline.LowerTerm(valueExpression, valueContext),
            _ => SymbolicSemanticPipeline.LowerTerm(valueExpression, valueContext)
        };
        var bindings = ImmutableArray.CreateBuilder<SymbolicAssignmentBinding>(1);
        var suppressValueBinding = postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic &&
                                   target.Kind == SmtValueKind.Reference;
        if (!suppressValueBinding &&
            value is { IsExact: true, Value: { } sourceTerm } &&
            SymbolicStateFactBuilder.CanCompareIrTerms(target, sourceTerm))
            bindings.Add(new SymbolicAssignmentBinding(
                SymbolicFactFactory.GetSmtVariableName(targetSymbol.OriginalDefinition),
                target,
                sourceTerm,
                bindingProvenance,
                evidenceKey));

        var postconditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
        AddReferenceAssignmentPostconditions(
            postconditions,
            targetSymbol,
            target,
            valueExpression,
            valueContext,
            provenance,
            postconditionProfile);
        var asExpressionFacts = SymbolicSemanticPipeline.LowerAsExpressionAssignmentFacts(
            targetSymbol,
            valueExpression,
            valueContext,
            targetContext.GetSymbolVersion,
            asExpressionProvenanceRoot ?? "ir.as");
        if (asExpressionFacts is { IsExact: true, Value: { } asExpressionState })
            postconditions.AddRange(asExpressionState.PathConditions);
        if (bindings.Count == 0 && postconditions.Count == 0)
            return Unsupported(source, provenance + ".value");

        var operation = new SymbolicAssignmentOperation(
            bindings.ToImmutable(),
            postconditions.ToImmutable(),
            SymbolicAssignmentOperationKind.Simple,
            IsChecked: false,
            new SymbolicOperationOrigin(source.Span, sequence, provenance));
        return SymbolicLoweringResult<SymbolicOperationSequence>.Exact(
            SymbolicOperationSequence.Single(operation),
            new SymbolicLoweringProvenance("roslyn-to-operation", source.Span, provenance));
    }

    private static void AddReferenceAssignmentPostconditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ISymbol targetSymbol,
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext valueContext,
        string provenance,
        SymbolicAssignmentPostconditionProfile profile)
    {
        if (target.Kind != SmtValueKind.Reference) return;
        if (profile == SymbolicAssignmentPostconditionProfile.Symbolic)
        {
            AddSymbolicStringAssignmentPostconditions(
                conditions,
                targetSymbol,
                target,
                valueExpression,
                valueContext,
                provenance);
            AddSymbolicCollectionLowerBound(
                conditions,
                targetSymbol,
                target,
                valueExpression,
                provenance);
            return;
        }

        AddEquality(
            conditions,
            new SymbolicLengthTerm(target),
            SymbolicSemanticPipeline.LowerLengthProjectionTerm(valueExpression, valueContext),
            valueExpression,
            provenance + ".length",
            provenance + ".length");
        if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression) is
            CollectionExpressionSyntax collection &&
            collection.Elements.Any(static element => element is SpreadElementSyntax))
        {
            var lowerBound = collection.Elements.Count(static element => element is ExpressionElementSyntax);
            if (lowerBound != 0)
                conditions.Add(ExactRelation(
                    SymbolicRelationOperator.GreaterThanOrEqual,
                    new SymbolicLengthTerm(target),
                    new SymbolicIntegerConstantTerm(lowerBound),
                    valueExpression,
                    provenance + ".collection_length",
                    evidenceKey: provenance + ".collection_length"));
        }

        if (SymbolicFactFactory.GetTrackedSymbolType(targetSymbol)?.SpecialType != SpecialType.System_String)
            return;

        AddEquality(
            conditions,
            new SymbolicStringContentTerm(target),
            SymbolicSemanticPipeline.LowerStringTerm(valueExpression, valueContext),
            valueExpression,
            provenance + ".string",
            provenance + ".string");
        if (SymbolicSemanticPipeline.LowerStringNonNullCondition(valueExpression, valueContext) is not
            { IsExact: true, Value: { } valueNonNull })
            return;

        var targetNonNull = ExactRelation(
            SymbolicRelationOperator.NotEqual,
            target,
            new SymbolicNullTerm(),
            valueExpression,
            provenance + ".string_nonnull",
            evidenceKey: provenance + ".string_nonnull");
        conditions.Add(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, targetNonNull, valueNonNull),
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicNotCondition(targetNonNull),
                new SymbolicNotCondition(valueNonNull))));
    }

    private static void AddSymbolicStringAssignmentPostconditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ISymbol targetSymbol,
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenance)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(targetSymbol)?.SpecialType != SpecialType.System_String)
            return;

        AddEquality(
            conditions,
            new SymbolicStringContentTerm(target),
            SymbolicSemanticPipeline.LowerStringTerm(valueExpression, context),
            valueExpression,
            provenance + ".assigned-string");
        if (IsDefinitelyNonNullStringExpression(valueExpression, context))
            conditions.Add(ExactRelation(
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm(),
                valueExpression,
                provenance + ".assigned-string.non-null"));
    }

    private static bool IsDefinitelyNonNullStringExpression(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constant.HasValue) return constant.Value is string;
        return expression switch
        {
            CastExpressionSyntax cast when
                context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type?.SpecialType ==
                SpecialType.System_String => IsDefinitelyNonNullStringExpression(cast.Expression, context),
            BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression) =>
                IsDefinitelyNonNullStringExpression(coalesce.Left, context) ||
                IsDefinitelyNonNullStringExpression(coalesce.Right, context),
            ConditionalExpressionSyntax conditional =>
                IsDefinitelyNonNullStringExpression(conditional.WhenTrue, context) &&
                IsDefinitelyNonNullStringExpression(conditional.WhenFalse, context),
            _ => SymbolicSemanticPipeline.LowerStringTerm(expression, context) is
                { IsExact: true, Value: SymbolicStringConstantTerm or SymbolicStringConcatTerm }
        };
    }

    private static void AddSymbolicCollectionLowerBound(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ISymbol targetSymbol,
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        string provenance)
    {
        if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression) is not
                CollectionExpressionSyntax collection ||
            !collection.Elements.Any(static element => element is SpreadElementSyntax))
            return;
        var fixedCount = collection.Elements.Count(static element => element is ExpressionElementSyntax);
        if (fixedCount != 0 &&
            SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(
                SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                target,
                valueExpression) is { IsExact: true, Value: { } length })
            conditions.Add(ExactRelation(
                SymbolicRelationOperator.GreaterThanOrEqual,
                length,
                new SymbolicIntegerConstantTerm(fixedCount),
                valueExpression,
                provenance + ".collection-expression.fixed-lower-bound"));
    }

    private static void AddEquality(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        SymbolicTerm target,
        SymbolicLoweringResult<SymbolicTerm> value,
        ExpressionSyntax source,
        string provenance,
        string? evidenceKey = null)
    {
        if (value is { IsExact: true, Value: { } valueTerm } &&
            SymbolicStateFactBuilder.CanCompareIrTerms(target, valueTerm))
            conditions.Add(ExactRelation(
                SymbolicRelationOperator.Equal,
                target,
                valueTerm,
                source,
                provenance,
                evidenceKey: evidenceKey));
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
                null,
                updateKind == SymbolicComputedUpdateKind.Increment
                    ? SymbolicMutationOperationKind.Increment
                    : SymbolicMutationOperationKind.Decrement,
                isChecked,
                CallerVisible: false,
                null,
                null,
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
        ISymbol? symbol = null,
        string? evidenceKey = null)
    {
        return new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicTruthAtom(value),
            source,
            provenance,
            symbol,
            evidenceKey));
    }

    private static SymbolicCondition ExactRelation(
        SymbolicRelationOperator relation,
        SymbolicTerm left,
        SymbolicTerm right,
        SyntaxNode source,
        string provenance,
        ISymbol? symbol = null,
        string? evidenceKey = null)
    {
        return new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(relation, left, right),
            source,
            provenance,
            symbol,
            evidenceKey));
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
