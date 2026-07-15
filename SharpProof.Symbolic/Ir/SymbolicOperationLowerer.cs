using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Symbolic.Ir;

internal enum SymbolicAssignmentPostconditionProfile
{
    Analyzer,
    Symbolic
}

internal static class SymbolicOperationLowerer
{
    internal static bool TryLowerDivideByZeroHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard)
    {
        var (site, divisor, isRemainder) = operation switch
        {
            IBinaryOperation binary when binary.OperatorKind is BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder =>
                (binary.Syntax, binary.RightOperand.Syntax as ExpressionSyntax,
                    binary.OperatorKind == BinaryOperatorKind.Remainder),
            ICompoundAssignmentOperation assignment when assignment.OperatorKind is
                BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder =>
                (assignment.Syntax, assignment.Value.Syntax as ExpressionSyntax,
                    assignment.OperatorKind == BinaryOperatorKind.Remainder),
            _ => (null, null, false)
        };
        if (site == null || divisor == null ||
            !SymbolicTypeFacts.IsThrowingDivideByZeroType(
                CSharpSyntaxFacts.GetExpressionType(divisor, context.SemanticModel, context.CancellationToken)))
        {
            hazard = null!;
            return false;
        }

        const string provenance = "ir.runtime-hazard.divide-by-zero";
        var zero = SymbolicSemanticPipeline.LowerNumericZeroCondition(divisor, context);
        SymbolicTerm? subject = null;
        SymbolicCondition? trigger = null;
        if (zero is { IsExact: true, Value: { } zeroCondition })
        {
            trigger = zeroCondition;
            subject = zeroCondition is SymbolicFactCondition
                {
                    Fact.Atom: SymbolicRelationAtom { Left: var left }
                }
                ? left
                : null;
        }

        hazard = CreateHazard(
            site,
            SymbolicRuntimeHazardKind.DivideByZero,
            SymbolicExceptionPreconditionKind.DivideByZero,
            subject,
            trigger,
            ExceptionTypes.DivideByZeroException,
            isRemainder ? ExceptionCategories.DefiniteModuloByZero : ExceptionCategories.DefiniteDivideByZero,
            provenance);
        return true;
    }

    internal static bool TryLowerCheckedOverflowHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard)
    {
        return operation switch
        {
            IBinaryOperation binary => TryLowerCheckedBinaryOverflow(binary, context, out hazard),
            IUnaryOperation unary => TryLowerCheckedUnaryOverflow(unary, context, out hazard),
            IIncrementOrDecrementOperation update => TryLowerCheckedUpdateOverflow(update, context, out hazard),
            ICompoundAssignmentOperation assignment =>
                TryLowerCheckedCompoundOverflow(assignment, context, out hazard),
            IConversionOperation conversion => TryLowerCheckedConversionOverflow(conversion, context, out hazard),
            _ => NoHazard(out hazard)
        };
    }

    internal static bool TryLowerReferenceNullHazard(
        ExpressionSyntax subjectExpression,
        SymbolicRuntimeHazardKind hazardKind,
        SymbolicExceptionPreconditionKind preconditionKind,
        string exceptionType,
        string category,
        string provenance,
        SymbolicLoweringContext context,
        bool suppressDefinitelyNotNull,
        out SymbolicHazardOperation hazard)
    {
        if (suppressDefinitelyNotNull &&
            NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                subjectExpression, context.SemanticModel, context.CancellationToken))
            return NoHazard(out hazard);

        var lowering = SymbolicSemanticPipeline.LowerTerm(subjectExpression, context);
        var subject = lowering is
            {
                IsExact: true,
                Value: { } value
            } && (value.Kind == SmtValueKind.Reference || value is SymbolicNullTerm)
                ? value
                : null;
        var trigger = subject == null
            ? null
            : SymbolicIrLowerer.CreateReferenceNullCondition(
                subject, true, subjectExpression, provenance + ".trigger");
        hazard = CreateHazard(
            subjectExpression,
            hazardKind,
            preconditionKind,
            subject,
            trigger,
            exceptionType,
            category,
            provenance);
        return true;
    }

    internal static bool TryLowerNullableValueHazard(
        ExpressionSyntax nullableExpression,
        string exceptionType,
        string category,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard)
    {
        const string provenance = "ir.runtime-hazard.nullable-value.without-value";
        var lowering = SymbolicSemanticPipeline.LowerNullableHasValueTerm(nullableExpression, context);
        SymbolicTerm? subject = null;
        SymbolicCondition? trigger = null;
        if (lowering is { IsExact: true, Value: SymbolicNullableHasValueTerm hasValue })
        {
            subject = new SymbolicVariableTerm(hasValue.NullableName, SmtValueKind.Reference);
            trigger = new SymbolicNotCondition(new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicTruthAtom(hasValue),
                nullableExpression,
                "ir.runtime-hazard.nullable-value.has-value")));
        }

        hazard = CreateHazard(
            nullableExpression,
            SymbolicRuntimeHazardKind.NullableValueWithoutValue,
            SymbolicExceptionPreconditionKind.NullableValueWithoutValue,
            subject,
            trigger,
            exceptionType,
            category,
            provenance);
        return true;
    }

    private static bool TryLowerCheckedBinaryOverflow(
        IBinaryOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard)
    {
        if (operation.Syntax is not BinaryExpressionSyntax expression ||
            operation.OperatorMethod != null ||
            !TryGetCheckedIntegralRange(expression, context, out var minValue, out var maxValue) ||
            !TryGetOverflowOperator(expression.Kind(), operation.IsChecked, minValue, out var smtOperator))
            return NoHazard(out hazard);

        if (smtOperator is SmtIntegerBinaryOperator.Divide or SmtIntegerBinaryOperator.Remainder)
        {
            const string provenance = "ir.runtime-hazard.checked-integral.signed-division-overflow";
            var left = LowerIntegerTerm(expression.Left, context);
            var right = LowerIntegerTerm(expression.Right, context);
            var trigger = left != null && right != null
                ? SymbolicIrLowerer.CreateSignedDivisionOverflowCondition(
                    left, right, minValue, expression, provenance)
                : null;
            hazard = CreateCheckedOverflowHazard(expression, left, trigger, provenance,
                ExceptionCategories.DefiniteCheckedIntegralOverflow);
            return true;
        }

        const string binaryProvenance = "ir.runtime-hazard.checked-integral.binary-overflow";
        var inRange = SymbolicSemanticPipeline.LowerIntegerBinaryInRangeCondition(
            expression.Left,
            expression.Right,
            smtOperator,
            minValue,
            maxValue,
            expression,
            context);
        hazard = CreateCheckedOverflowHazard(
            expression,
            null,
            inRange is { IsExact: true, Value: { } condition } ? new SymbolicNotCondition(condition) : null,
            binaryProvenance,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    private static bool TryLowerCheckedUnaryOverflow(
        IUnaryOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard)
    {
        if (operation.Syntax is not PrefixUnaryExpressionSyntax expression ||
            operation.OperatorKind != UnaryOperatorKind.Minus ||
            operation.OperatorMethod != null ||
            !operation.IsChecked ||
            !TryGetCheckedIntegralRange(expression, context, out var minValue, out _))
            return NoHazard(out hazard);

        const string provenance = "ir.runtime-hazard.checked-integral.unary-minus-overflow";
        var value = LowerIntegerTerm(expression.Operand, context);
        hazard = CreateCheckedOverflowHazard(
            expression,
            value,
            value == null ? null : CreateIntegerEquality(value, minValue, expression.Operand, provenance + ".operand"),
            provenance,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    private static bool TryLowerCheckedUpdateOverflow(
        IIncrementOrDecrementOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard)
    {
        if (operation.Syntax is not ExpressionSyntax expression ||
            operation.Target.Syntax is not ExpressionSyntax operand ||
            operation.OperatorMethod != null ||
            !operation.IsChecked ||
            !SymbolicTypeFacts.TryGetBoundedIntegralRange(operation.Target.Type, out var minValue, out var maxValue))
            return NoHazard(out hazard);

        var increment = operation.Kind == OperationKind.Increment;
        var provenance = increment
            ? "ir.runtime-hazard.checked-integral.increment-overflow"
            : "ir.runtime-hazard.checked-integral.decrement-overflow";
        var value = LowerIntegerTerm(operand, context);
        hazard = CreateCheckedOverflowHazard(
            expression,
            value,
            value == null
                ? null
                : CreateIntegerEquality(value, increment ? maxValue : minValue, operand, provenance + ".operand"),
            provenance,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    private static bool TryLowerCheckedCompoundOverflow(
        ICompoundAssignmentOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard)
    {
        if (operation.Syntax is not AssignmentExpressionSyntax assignment ||
            operation.Target.Syntax is not ExpressionSyntax leftExpression ||
            operation.Value.Syntax is not ExpressionSyntax rightExpression ||
            operation.OperatorMethod != null ||
            !SymbolicTypeFacts.TryGetBoundedIntegralRange(operation.Target.Type, out var minValue, out var maxValue) ||
            !CSharpSyntaxFacts.TryGetCompoundAssignmentBinaryKind(assignment.Kind(), out var binaryKind) ||
            !TryGetOverflowOperator(binaryKind, operation.IsChecked, minValue, out var smtOperator))
            return NoHazard(out hazard);

        var left = LowerIntegerTerm(leftExpression, context);
        if (smtOperator is SmtIntegerBinaryOperator.Divide or SmtIntegerBinaryOperator.Remainder)
        {
            const string provenance = "ir.runtime-hazard.checked-integral.compound-signed-division-overflow";
            var right = LowerIntegerTerm(rightExpression, context);
            var trigger = left != null && right != null
                ? SymbolicIrLowerer.CreateSignedDivisionOverflowCondition(
                    left, right, minValue, assignment, provenance)
                : null;
            hazard = CreateCheckedOverflowHazard(
                assignment, left, trigger, provenance, ExceptionCategories.DefiniteCheckedIntegralOverflow);
            return true;
        }

        const string compoundProvenance = "ir.runtime-hazard.checked-integral.compound-assignment-overflow";
        var inRange = SymbolicSemanticPipeline.LowerIntegerBinaryInRangeCondition(
            leftExpression,
            rightExpression,
            smtOperator,
            minValue,
            maxValue,
            assignment,
            context);
        hazard = CreateCheckedOverflowHazard(
            assignment,
            left,
            inRange is { IsExact: true, Value: { } condition } ? new SymbolicNotCondition(condition) : null,
            compoundProvenance,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    private static bool TryLowerCheckedConversionOverflow(
        IConversionOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard)
    {
        if (operation.Syntax is not CastExpressionSyntax cast ||
            operation.Operand.Syntax is not ExpressionSyntax operand ||
            !operation.IsChecked ||
            operation.Conversion is not
            {
                Exists: true,
                IsIdentity: false,
                IsImplicit: false,
                IsNumeric: true,
                IsUserDefined: false,
                MethodSymbol: null
            } ||
            !SymbolicTypeFacts.TryGetCheckedNumericConversionRange(
                SymbolicRuntimeTypeFacts.GetNaturalExpressionType(cast, context.SemanticModel, context.CancellationToken),
                out var minValue,
                out var maxValue))
            return NoHazard(out hazard);

        if (SymbolicTypeFacts.TryGetCheckedNumericConversionRange(
                SymbolicRuntimeTypeFacts.GetNaturalExpressionType(
                    operand, context.SemanticModel, context.CancellationToken),
                out var sourceMinValue,
                out var sourceMaxValue) &&
            sourceMinValue >= minValue &&
            sourceMaxValue <= maxValue)
            return NoHazard(out hazard);

        const string provenance = "ir.runtime-hazard.checked-conversion.overflow";
        var value = LowerIntegerTerm(operand, context);
        var trigger = value == null
            ? null
            : new SymbolicNotCondition(SymbolicIrLowerer.CreateIntegerInRangeCondition(
                value, minValue, maxValue, operand, provenance));
        hazard = CreateCheckedOverflowHazard(
            cast,
            value,
            trigger,
            provenance,
            ExceptionCategories.DefiniteCheckedNumericConversionOverflow);
        return true;
    }

    private static SymbolicHazardOperation CreateCheckedOverflowHazard(
        SyntaxNode site,
        SymbolicTerm? subject,
        SymbolicCondition? trigger,
        string provenance,
        string category)
        => CreateHazard(
            site,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            subject,
            trigger,
            ExceptionTypes.OverflowException,
            category,
            provenance);

    private static SymbolicHazardOperation CreateHazard(
        SyntaxNode site,
        SymbolicRuntimeHazardKind hazardKind,
        SymbolicExceptionPreconditionKind preconditionKind,
        SymbolicTerm? subject,
        SymbolicCondition? trigger,
        string exceptionType,
        string category,
        string provenance)
    {
        var confidence = trigger == null ? SymbolicFactConfidence.Unsupported : SymbolicFactConfidence.Exact;
        if (trigger == null)
        {
            provenance += ".unsupported";
            subject = null;
            trigger = CreateUnsupportedHazardCondition(site, provenance);
        }

        return new SymbolicHazardOperation(
            hazardKind,
            preconditionKind,
            subject,
            trigger,
            confidence,
            exceptionType,
            category,
            new SymbolicOperationOrigin(site.Span, 0, provenance));
    }

    private static SymbolicCondition CreateIntegerEquality(
        SymbolicTerm value,
        long constant,
        SyntaxNode source,
        string provenance) =>
        SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.Equal,
            value,
            new SymbolicIntegerConstantTerm(constant),
            source,
            provenance);

    private static SymbolicTerm? LowerIntegerTerm(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        return lowering is { IsExact: true, Value: { Kind: SmtValueKind.Int } value } ? value : null;
    }

    private static bool TryGetCheckedIntegralRange(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out long minValue,
        out long maxValue)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return SymbolicTypeFacts.TryGetCheckedIntegralRange(
            typeInfo.ConvertedType ?? typeInfo.Type, out minValue, out maxValue);
    }

    private static bool TryGetOverflowOperator(
        SyntaxKind syntaxKind,
        bool isChecked,
        long minimum,
        out SmtIntegerBinaryOperator smtOperator)
    {
        smtOperator = default;
        if (!SymbolicOperatorLowerer.TryGetBinaryTermOperator(syntaxKind, out var binaryOperator) ||
            (binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Subtract or
                SymbolicBinaryTermOperator.Multiply) && !isChecked ||
            (binaryOperator is SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder) &&
            minimum >= 0)
            return false;

        smtOperator = SymbolicOperatorLowerer.GetSmtIntegerBinaryOperator(binaryOperator);
        return true;
    }

    private static bool NoHazard(out SymbolicHazardOperation hazard)
    {
        hazard = null!;
        return false;
    }

    private static SymbolicCondition CreateUnsupportedHazardCondition(SyntaxNode site, string provenance)
    {
        var name = "unsupported_typed_projection#" + site.SpanStart.ToString(CultureInfo.InvariantCulture) +
                   "_" + site.Span.End.ToString(CultureInfo.InvariantCulture);
        return new SymbolicFactCondition(new SymbolicFact(
            new SymbolicTruthAtom(new SymbolicVariableTerm(name, SmtValueKind.Bool)),
            true,
            SymbolicFactConfidence.Exact,
            provenance + ".trigger",
            site.Span,
            null,
            provenance + ".trigger"));
    }

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
                    value.Syntax,
                    targetContext,
                    valueContext,
                    sequence,
                    "operation-lowering.declaration"),
            ISimpleAssignmentOperation assignment when TryGetDirectTargetSymbol(assignment.Target, out var target) =>
                LowerSimpleAssignment(
                    target,
                    assignment.Value.Syntax,
                    targetContext,
                    valueContext,
                    sequence,
                    "operation-lowering.assignment"),
            _ => Unsupported(operation, "operation-lowering.unsupported")
        };
    }

    internal static SymbolicLoweringResult<SymbolicOperationSequence> LowerSimpleAssignment(
        ISymbol targetSymbol,
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
        if (source is not ExpressionSyntax valueExpression)
            return Unsupported(source, provenance + ".target");

        var bindings = ImmutableArray.CreateBuilder<SymbolicAssignmentBinding>(1);
        var postconditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
        var propagations = ImmutableArray.CreateBuilder<SymbolicTermPropagation>();
        if (postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic)
        {
            AddSymbolicNullableAssignmentPostconditions(
                postconditions,
                targetSymbol,
                valueExpression,
                targetContext,
                valueContext,
                provenance);
            AddSymbolicNullableAssignmentPropagations(
                propagations,
                targetSymbol,
                valueExpression,
                targetContext,
                valueContext);
            AddSymbolicTupleAssignmentPostconditions(
                postconditions,
                targetSymbol,
                valueExpression,
                targetContext,
                valueContext,
                provenance);
        }

        if (TryCreateSymbolTerm(targetSymbol, targetContext, out var target))
        {
            var value = target.Kind == SmtValueKind.Bool
                ? SymbolicSemanticPipeline.LowerBooleanValueTerm(valueExpression, valueContext)
                : SymbolicSemanticPipeline.LowerTerm(valueExpression, valueContext);
            if (value is { IsExact: true, Value: { } sourceTerm } &&
                SymbolicStateFactBuilder.CanCompareIrTerms(target, sourceTerm))
            {
                var isSymbolicReference =
                    postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic &&
                    target.Kind == SmtValueKind.Reference;
                bindings.Add(new SymbolicAssignmentBinding(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol.OriginalDefinition),
                    target,
                    sourceTerm,
                    isSymbolicReference ? provenance + ".assigned-reference" : bindingProvenance,
                    isSymbolicReference ? null : evidenceKey,
                    PropagateSourceFacts:
                        postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic &&
                        SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                            valueContext.SemanticModel,
                            valueContext.CancellationToken,
                            out _),
                    DeriveIntegerBounds:
                        postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic &&
                        target.Kind == SmtValueKind.Int));
            }

            AddReferenceAssignmentPostconditions(
                postconditions,
                targetSymbol,
                target,
                valueExpression,
                valueContext,
                provenance,
                postconditionProfile);
            if (postconditionProfile == SymbolicAssignmentPostconditionProfile.Symbolic)
            {
                AddSymbolicFiniteArrayElementPostconditions(
                    postconditions,
                    targetSymbol,
                    target,
                    valueExpression,
                    valueContext,
                    provenance);
                AddSymbolicSwitchExpressionPostconditions(
                    postconditions,
                    targetSymbol,
                    target,
                    valueExpression,
                    valueContext,
                    provenance);
            }
            var asExpressionFacts = SymbolicSemanticPipeline.LowerAsExpressionAssignmentFacts(
                targetSymbol,
                valueExpression,
                valueContext,
                targetContext.GetSymbolVersion,
                asExpressionProvenanceRoot ?? "ir.as");
            if (asExpressionFacts is { IsExact: true, Value: { } asExpressionState })
                postconditions.AddRange(asExpressionState.PathConditions);
        }
        if (bindings.Count == 0 && postconditions.Count == 0 && propagations.Count == 0)
            return Unsupported(source, provenance + ".value");

        var operation = new SymbolicAssignmentOperation(
            bindings.ToImmutable(),
            postconditions.ToImmutable(),
            SymbolicAssignmentOperationKind.Simple,
            IsChecked: false,
            new SymbolicOperationOrigin(source.Span, sequence, provenance),
            propagations.ToImmutable());
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
            AddSymbolicReferenceAssignmentPostconditions(
                conditions,
                targetSymbol,
                target,
                valueExpression,
                valueContext,
                provenance);
            conditions.AddRange(LowerSymbolicReferenceBackedPostconditions(
                target,
                valueExpression,
                valueContext,
                provenance));
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

    private static void AddSymbolicNullableAssignmentPostconditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ISymbol targetSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext targetContext,
        SymbolicLoweringContext valueContext,
        string provenance)
    {
        if (!SymbolicNullableLowerer.TryCreateSymbolTerms(
                targetSymbol,
                targetContext,
                out var targetHasValue,
                out var targetValue))
            return;

        SymbolicTerm sourceHasValue;
        SymbolicTerm? sourceValue = null;
        if (SymbolicSemanticPipeline.LowerNullableHasValueTerm(valueExpression, valueContext) is
            { IsExact: true, Value: { } nullableHasValue })
        {
            sourceHasValue = nullableHasValue;
            if (SymbolicSemanticPipeline.LowerNullableValueTerm(valueExpression, valueContext) is
                { IsExact: true, Value: { } nullableValue })
                sourceValue = nullableValue;
        }
        else if (SymbolicSemanticPipeline.LowerTerm(valueExpression, valueContext) is
                 { IsExact: true, Value: { } wrappedValue } &&
                 wrappedValue.Kind == targetValue.Kind)
        {
            sourceHasValue = new SymbolicBooleanConstantTerm(true);
            sourceValue = wrappedValue;
        }
        else
        {
            return;
        }

        conditions.Add(ExactRelation(
            SymbolicRelationOperator.Equal,
            targetHasValue,
            sourceHasValue,
            valueExpression,
            provenance + ".nullable.has-value"));
        if (sourceValue == null ||
            !SymbolicStateFactBuilder.CanCompareIrTerms(targetValue, sourceValue))
            return;

        conditions.Add(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicNotCondition(ExactTruth(
                targetHasValue,
                valueExpression,
                provenance + ".nullable.value-present",
                targetSymbol)),
            ExactRelation(
                SymbolicRelationOperator.Equal,
                targetValue,
                sourceValue,
                valueExpression,
                provenance + ".nullable.value",
                targetSymbol)));
    }

    private static void AddSymbolicNullableAssignmentPropagations(
        ImmutableArray<SymbolicTermPropagation>.Builder propagations,
        ISymbol targetSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext targetContext,
        SymbolicLoweringContext valueContext)
    {
        if (!SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                valueContext.SemanticModel,
                valueContext.CancellationToken,
                out var sourceSymbol) ||
            SymbolEqualityComparer.Default.Equals(sourceSymbol, targetSymbol) ||
            !SymbolicNullableLowerer.TryCreateSymbolTerms(
                sourceSymbol,
                valueContext,
                out var sourceHasValue,
                out var sourceValue) ||
            !SymbolicNullableLowerer.TryCreateSymbolTerms(
                targetSymbol,
                targetContext,
                out var targetHasValue,
                out var targetValue) ||
            !SymbolicStateFactBuilder.CanCompareIrTerms(sourceHasValue, targetHasValue) ||
            !SymbolicStateFactBuilder.CanCompareIrTerms(sourceValue, targetValue))
            return;

        propagations.Add(new SymbolicTermPropagation(sourceHasValue, targetHasValue));
        propagations.Add(new SymbolicTermPropagation(sourceValue, targetValue));
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

    private static void AddSymbolicReferenceAssignmentPostconditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ISymbol targetSymbol,
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenance)
    {
        if (SymbolicSemanticPipeline.LowerReferenceTerm(valueExpression, context) is
            { IsExact: true, Value: { } value } &&
            SymbolicStateFactBuilder.CanCompareIrTerms(target, value))
        {
            conditions.Add(ExactRelation(
                SymbolicRelationOperator.Equal,
                target,
                value,
                valueExpression,
                provenance + ".assigned-reference"));
            AddConditionalReferencePostconditions(conditions, target, value, valueExpression, provenance);
        }

        var nullRelation = NullableFlowFacts.IsDefinitelyNullReferenceValue(
            valueExpression,
            context.SemanticModel,
            context.CancellationToken)
            ? SymbolicRelationOperator.Equal
            : NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                valueExpression,
                context.SemanticModel,
                context.CancellationToken)
                ? SymbolicRelationOperator.NotEqual
                : (SymbolicRelationOperator?)null;
        if (nullRelation.HasValue)
            conditions.Add(ExactRelation(
                nullRelation.Value,
                target,
                new SymbolicNullTerm(),
                valueExpression,
                provenance + (nullRelation == SymbolicRelationOperator.Equal
                    ? ".assigned-null"
                    : ".assigned-non-null")));

        if (SymbolicSemanticPipeline.LowerNotNullIfNotNullAssignedResultTerm(valueExpression, context) is
            { IsExact: true, Value: { Kind: SmtValueKind.Bool } resultNonNull })
        {
            var targetNonNull = ExactRelation(
                SymbolicRelationOperator.NotEqual,
                target,
                new SymbolicNullTerm(),
                valueExpression,
                provenance + ".not-null-if-not-null.target",
                targetSymbol);
            conditions.Add(ExactRelation(
                SymbolicRelationOperator.Equal,
                new SymbolicConditionalTerm(
                    targetNonNull,
                    new SymbolicBooleanConstantTerm(true),
                    new SymbolicBooleanConstantTerm(false)),
                resultNonNull,
                valueExpression,
                provenance + ".not-null-if-not-null.result"));
        }
    }

    private static void AddConditionalReferencePostconditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        SymbolicTerm target,
        SymbolicTerm value,
        ExpressionSyntax source,
        string provenance)
    {
        if (value is not SymbolicConditionalTerm
            {
                WhenTrue.Kind: SmtValueKind.Reference,
                WhenFalse.Kind: SmtValueKind.Reference
            } conditional)
            return;

        var targetNonNull = ExactRelation(
            SymbolicRelationOperator.NotEqual,
            target,
            new SymbolicNullTerm(),
            source,
            provenance + ".conditional-reference.target-non-null");
        var trueValueNull = ExactRelation(
            SymbolicRelationOperator.Equal,
            conditional.WhenTrue,
            new SymbolicNullTerm(),
            source,
            provenance + ".conditional-reference.true-value-null");
        var falseValueNull = ExactRelation(
            SymbolicRelationOperator.Equal,
            conditional.WhenFalse,
            new SymbolicNullTerm(),
            source,
            provenance + ".conditional-reference.false-value-null");
        conditions.Add(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicNotCondition(conditional.Condition),
            new SymbolicBinaryCondition(SymbolicConditionOperator.Or, targetNonNull, trueValueNull)));
        conditions.Add(new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            conditional.Condition,
            new SymbolicBinaryCondition(SymbolicConditionOperator.Or, targetNonNull, falseValueNull)));
    }

    internal static ImmutableArray<SymbolicCondition> LowerSymbolicReferenceBackedPostconditions(
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenance,
        ITypeSymbol? targetType = null,
        string lengthSuffix = ".reference-backed-length",
        string? countSuffix = ".reference-backed-count",
        string stringSuffix = ".reference-backed-string",
        string arrayDimensionSuffix = ".reference-backed-array-length")
    {
        if (target.Kind != SmtValueKind.Reference ||
            NullableFlowFacts.IsDefinitelyNullReferenceValue(
                valueExpression,
                context.SemanticModel,
                context.CancellationToken))
            return ImmutableArray<SymbolicCondition>.Empty;

        var typeInfo = context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken);
        var sourceType = typeInfo.Type;
        var valueType = targetType ?? typeInfo.ConvertedType ?? sourceType;
        if (targetType == null && sourceType != null &&
            ShouldUseReferenceBackedSourceType(sourceType, valueType))
            valueType = sourceType;
        if (valueType == null) return ImmutableArray<SymbolicCondition>.Empty;

        var conditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
        AddEquality(
            conditions,
            SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(valueType, target, valueExpression),
            SymbolicSemanticPipeline.LowerBuiltInLengthTerm(valueExpression, context),
            valueExpression,
            provenance + lengthSuffix);

        if (countSuffix != null &&
            SymbolicSemanticPipeline.ProjectBuiltInLengthTerm(valueType, target, valueExpression) is
                { IsExact: true, Value: SymbolicCountTerm targetCount } &&
            GetExactListCreationCount(valueExpression, sourceType ?? valueType) is { } count)
            conditions.Add(ExactRelation(
                SymbolicRelationOperator.Equal,
                targetCount,
                new SymbolicIntegerConstantTerm(count),
                valueExpression,
                provenance + countSuffix));

        if (valueType.SpecialType == SpecialType.System_String)
            AddEquality(
                conditions,
                SymbolicSemanticPipeline.ProjectStringContentTerm(target, valueExpression),
                SymbolicSemanticPipeline.LowerStringTerm(valueExpression, context),
                valueExpression,
                provenance + stringSuffix);

        if (valueType is IArrayTypeSymbol { Rank: > 1 } arrayType)
            for (var dimension = 0; dimension < arrayType.Rank; dimension++)
                AddEquality(
                    conditions,
                    new SymbolicArrayDimensionLengthTerm(target, dimension),
                    SymbolicSemanticPipeline.LowerArrayDimensionLengthTerm(
                        valueExpression,
                        dimension,
                        context),
                    valueExpression,
                    provenance + arrayDimensionSuffix);

        return conditions.ToImmutable();
    }

    private static void AddSymbolicFiniteArrayElementPostconditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ISymbol targetSymbol,
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext valueContext,
        string provenance)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(targetSymbol) is not IArrayTypeSymbol { Rank: 1 } arrayType ||
            !SymbolicFactFactory.TryGetValueKind(
                arrayType.ElementType,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsSymbolicReferenceLikeType,
                out var elementKind) ||
            !SymbolicProgramPointFacts.TryGetFiniteElementExpressions(valueExpression, out var elementExpressions) ||
            target.Kind != SmtValueKind.Reference)
            return;

        for (var index = 0; index < elementExpressions.Length; index++)
        {
            var elementExpression = elementExpressions[index];
            if (SymbolMutationFacts.ExpressionReferencesSymbol(
                    elementExpression,
                    targetSymbol,
                    valueContext.SemanticModel,
                    valueContext.CancellationToken) ||
                SymbolicSemanticPipeline.LowerTerm(elementExpression, valueContext) is not
                    { IsExact: true, Value: { } elementValue } ||
                elementValue.Kind != elementKind)
                continue;

            var targetElement = new SymbolicElementTerm(
                target,
                new SymbolicIntegerConstantTerm(index),
                elementKind);
            conditions.Add(ExactRelation(
                SymbolicRelationOperator.Equal,
                targetElement,
                elementValue,
                elementExpression,
                provenance + ".finite-array-element"));
            conditions.Add(ExactRelation(
                SymbolicRelationOperator.Equal,
                new SymbolicElementTerm(
                    target,
                    new SymbolicFromEndIndexTerm(
                        new SymbolicIntegerConstantTerm(elementExpressions.Length - index)),
                    elementKind),
                elementValue,
                elementExpression,
                provenance + ".finite-array-element.from-end"));

            if (elementKind == SmtValueKind.Reference &&
                NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                    elementExpression,
                    valueContext.SemanticModel,
                    valueContext.CancellationToken))
                conditions.Add(ExactRelation(
                    SymbolicRelationOperator.NotEqual,
                    targetElement,
                    new SymbolicNullTerm(),
                    elementExpression,
                    provenance + ".finite-array-element.non-null"));
        }
    }

    private static void AddSymbolicTupleAssignmentPostconditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ISymbol targetSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext targetContext,
        SymbolicLoweringContext valueContext,
        string provenance)
    {
        var unwrappedValue = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression);
        if (unwrappedValue is TupleExpressionSyntax tupleExpression &&
            TryGetTupleElementStorageNames(targetSymbol, tupleExpression.Arguments.Count, out var targetNames))
        {
            for (var index = 0; index < targetNames.Length; index++)
            {
                var elementExpression = tupleExpression.Arguments[index].Expression;
                if (SymbolMutationFacts.ExpressionReferencesSymbol(
                        elementExpression,
                        targetSymbol,
                        valueContext.SemanticModel,
                        valueContext.CancellationToken) ||
                    !TryCreateTupleElementTerm(targetSymbol, targetNames[index], targetContext, out var targetElement) ||
                    !TryGetTupleElementType(targetSymbol, targetNames[index], out var elementType))
                    continue;

                if (elementType.SpecialType != SpecialType.System_String)
                    AddEquality(
                        conditions,
                        targetElement,
                        SymbolicSemanticPipeline.LowerTerm(elementExpression, valueContext),
                        elementExpression,
                        provenance + ".tuple-element.assigned-value");

                if (targetElement.Kind == SmtValueKind.Reference &&
                    NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                        elementExpression,
                        valueContext.SemanticModel,
                        valueContext.CancellationToken))
                    conditions.Add(ExactRelation(
                        SymbolicRelationOperator.NotEqual,
                        targetElement,
                        new SymbolicNullTerm(),
                        elementExpression,
                        provenance + ".tuple-element.assigned-non-null"));

                conditions.AddRange(LowerSymbolicReferenceBackedPostconditions(
                    targetElement,
                    elementExpression,
                    valueContext,
                    provenance + ".tuple-element",
                    elementType,
                    ".assigned-length",
                    countSuffix: null,
                    ".assigned-string",
                    ".assigned-dimension-length"));
            }

            return;
        }

        if (!SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
                unwrappedValue,
                valueContext.SemanticModel,
                valueContext.CancellationToken,
                out var sourceSymbol) ||
            SymbolEqualityComparer.Default.Equals(sourceSymbol, targetSymbol) ||
            !TryGetTupleElementStorageNames(targetSymbol, 0, out var targetElementNames) ||
            !TryGetTupleElementStorageNames(sourceSymbol, targetElementNames.Length, out var sourceElementNames))
            return;

        for (var index = 0; index < targetElementNames.Length; index++)
            if (TryCreateTupleElementTerm(
                    targetSymbol,
                    targetElementNames[index],
                    targetContext,
                    out var targetElement) &&
                TryCreateTupleElementTerm(
                    sourceSymbol,
                    sourceElementNames[index],
                    valueContext,
                    out var sourceElement) &&
                SymbolicStateFactBuilder.CanCompareIrTerms(targetElement, sourceElement))
                conditions.Add(ExactRelation(
                    SymbolicRelationOperator.Equal,
                    targetElement,
                    sourceElement,
                    valueExpression,
                    provenance + ".tuple-element.snapshot"));
    }

    private static void AddSymbolicSwitchExpressionPostconditions(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        ISymbol targetSymbol,
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        string provenance)
    {
        if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression) is not
                SwitchExpressionSyntax { Arms.Count: > 0 } switchExpression ||
            SymbolicLoopStateTransfer.ExpressionMutatesAnySymbol(
                switchExpression,
                SymbolicBranchCompletionStateTransfer.GetSwitchExpressionConditionSymbols(
                    switchExpression,
                    context.SemanticModel,
                    context.CancellationToken),
                context.SemanticModel,
                context.CancellationToken))
            return;

        var addedCount = 0;
        foreach (var arm in switchExpression.Arms)
        {
            if (!SwitchPathConditionBuilder.TryCreateSwitchExpressionArmSymbolicCondition(
                    switchExpression.GoverningExpression,
                    arm,
                    context.SemanticModel,
                    context.CancellationToken,
                    out var armCondition))
                continue;

            SymbolicCondition? armValue = null;
            if (CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(arm.Expression) is ThrowExpressionSyntax)
                armValue = new SymbolicConstantCondition(false);
            else if (SymbolicSemanticPipeline.LowerTerm(arm.Expression, context) is
                         { IsExact: true, Value: { } value } &&
                     SymbolicStateFactBuilder.CanCompareIrTerms(target, value))
                armValue = ExactRelation(
                    SymbolicRelationOperator.Equal,
                    target,
                    value,
                    arm.Expression,
                    provenance + ".switch-expression-assigned-value",
                    targetSymbol);

            if (armValue == null) continue;
            if (!SymbolicAnalysisLimitContext.CanAddMergedSwitchFact(
                    addedCount,
                    switchExpression,
                    "program_point.switch_expression_state_fact_merge"))
                return;
            conditions.Add(SymbolicStateMerger.CreateGuardedChoice(armCondition, armValue));
            addedCount++;
        }
    }

    internal static bool TryGetTupleElementStorageNames(
        ISymbol symbol,
        int expectedCount,
        out string[] elementNames)
    {
        elementNames = Array.Empty<string>();
        if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not INamedTypeSymbol { IsTupleType: true } tupleType ||
            expectedCount > 0 && tupleType.TupleElements.Length != expectedCount)
            return false;

        elementNames = new string[tupleType.TupleElements.Length];
        for (var index = 0; index < elementNames.Length; index++)
        {
            var field = tupleType.TupleElements[index].CorrespondingTupleField ?? tupleType.TupleElements[index];
            if (string.IsNullOrWhiteSpace(field.Name)) return false;
            elementNames[index] = field.Name;
        }

        return true;
    }

    private static bool TryGetTupleElementType(
        ISymbol tupleSymbol,
        string elementName,
        out ITypeSymbol elementType)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(tupleSymbol) is not INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            elementType = null!;
            return false;
        }

        var element = tupleType.TupleElements.FirstOrDefault(field =>
            string.Equals((field.CorrespondingTupleField ?? field).Name, elementName, StringComparison.Ordinal));
        elementType = element?.Type!;
        return element != null;
    }

    internal static bool TryCreateTupleElementTerm(
        ISymbol tupleSymbol,
        string elementName,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        if (!TryGetTupleElementType(tupleSymbol, elementName, out var elementType) ||
            !SymbolicFactFactory.TryGetValueKind(
                elementType,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsSymbolicReferenceLikeType,
                out var elementKind))
        {
            term = null!;
            return false;
        }

        term = new SymbolicMemberTerm(
            new SymbolicVariableTerm(context.GetVariableName(tupleSymbol), SmtValueKind.Reference),
            elementName,
            elementKind);
        return true;
    }

    private static bool ShouldUseReferenceBackedSourceType(ITypeSymbol sourceType, ITypeSymbol? convertedType)
    {
        return convertedType == null ||
               !SymbolEqualityComparer.Default.Equals(sourceType, convertedType) &&
               HasBuiltInLengthShape(sourceType) &&
               !HasBuiltInLengthShape(convertedType);
    }

    private static bool HasBuiltInLengthShape(ITypeSymbol? type)
    {
        return type?.SpecialType == SpecialType.System_String ||
               type is IArrayTypeSymbol { Rank: >= 1 };
    }

    private static int? GetExactListCreationCount(ExpressionSyntax valueExpression, ITypeSymbol? sourceType)
    {
        if (sourceType is not INamedTypeSymbol namedType ||
            !string.Equals(
                namedType.OriginalDefinition.ToDisplayString(),
                "System.Collections.Generic.List<T>",
                StringComparison.Ordinal))
            return null;

        valueExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression);
        return valueExpression switch
        {
            ObjectCreationExpressionSyntax creation when creation.ArgumentList?.Arguments.Count is null or 0 =>
                GetCollectionInitializerCount(creation.Initializer),
            ImplicitObjectCreationExpressionSyntax creation when creation.ArgumentList.Arguments.Count == 0 =>
                GetCollectionInitializerCount(creation.Initializer),
            _ => null
        };
    }

    private static int? GetCollectionInitializerCount(InitializerExpressionSyntax? initializer)
    {
        return initializer == null
            ? 0
            : initializer.IsKind(SyntaxKind.CollectionInitializerExpression)
                ? initializer.Expressions.Count
                : null;
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

    private static void AddEquality(
        ImmutableArray<SymbolicCondition>.Builder conditions,
        SymbolicLoweringResult<SymbolicTerm> target,
        SymbolicLoweringResult<SymbolicTerm> value,
        ExpressionSyntax source,
        string provenance)
    {
        if (target is { IsExact: true, Value: { } targetTerm })
            AddEquality(conditions, targetTerm, value, source, provenance);
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

    internal static SymbolicLoweringResult<SymbolicOperationSequence> LowerExplicitTargetAssignment(
        SymbolicTerm target,
        ExpressionSyntax valueExpression,
        SyntaxNode source,
        SymbolicLoweringContext context,
        string provenance,
        string bindingProvenance,
        bool includeReferencePostconditions)
    {
        var bindings = ImmutableArray.CreateBuilder<SymbolicAssignmentBinding>(1);
        var postconditions = ImmutableArray.CreateBuilder<SymbolicCondition>();
        if (SymbolicSemanticPipeline.LowerTerm(valueExpression, context) is
                { IsExact: true, Value: { } value } &&
            SymbolicStateFactBuilder.CanCompareIrTerms(target, value))
            bindings.Add(new SymbolicAssignmentBinding(
                SymbolicState.CreateProofTermKey(target),
                target,
                value,
                bindingProvenance,
                InvalidateTarget: false));

        if (includeReferencePostconditions && target.Kind == SmtValueKind.Reference)
        {
            if (NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                    valueExpression,
                    context.SemanticModel,
                    context.CancellationToken))
                postconditions.Add(ExactRelation(
                    SymbolicRelationOperator.NotEqual,
                    target,
                    new SymbolicNullTerm(),
                    valueExpression,
                    provenance + ".assigned-non-null"));
            postconditions.AddRange(LowerSymbolicReferenceBackedPostconditions(
                target,
                valueExpression,
                context,
                provenance));
        }

        if (bindings.Count == 0 && postconditions.Count == 0)
            return Unsupported(source, provenance + ".value");

        return SymbolicLoweringResult<SymbolicOperationSequence>.Exact(
            SymbolicOperationSequence.Single(new SymbolicAssignmentOperation(
                bindings.ToImmutable(),
                postconditions.ToImmutable(),
                SymbolicAssignmentOperationKind.Simple,
                IsChecked: false,
                new SymbolicOperationOrigin(source.Span, 0, provenance))),
            new SymbolicLoweringProvenance("explicit-target-assignment", source.Span, provenance));
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
            if (SymbolicNullableLowerer.TryCreateSymbolTerms(targetSymbol, context, out var hasValue, out _))
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
        else if (SymbolicNullableLowerer.TryCreateSymbolTerms(
                     targetSymbol,
                     context,
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
            var definitelyNonNull = NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
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
                SymbolicTypeFacts.IsSymbolicReferenceLikeType,
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
