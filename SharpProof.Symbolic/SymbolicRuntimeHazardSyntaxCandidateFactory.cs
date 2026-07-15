using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;
using static SharpProof.Symbolic.SymbolicRuntimeHazardIrTriggerFactory;
using static SharpProof.Symbolic.SymbolicRuntimeHazardSyntaxFacts;
using static SharpProof.Symbolic.SymbolicRuntimeHazardTriggerFactory;

namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeHazardSyntaxCandidateFactory
{
    internal static bool TryCreateIndexConstructionArgumentOutOfRangeCandidate(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerIndexConstructionArgumentOutOfRangeCondition(
            expression,
            context);
        if (lowering is not { IsExact: true, Value: { } condition } ||
            !TryCreateIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.ArgumentOutOfRange,
                null,
                condition,
                expression,
                "ir.runtime-hazard.index.constructor-argument-out-of-range",
                out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            expression,
            SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            trigger,
            ExceptionTypes.ArgumentOutOfRangeException,
            ExceptionCategories.DefiniteIndexConstructionArgumentOutOfRange);
        return true;
    }

    internal static bool TryCreateMathAbsOverflowCandidate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
            !operation.TargetMethod.IsStatic ||
            !SymbolicKnownApiLowerer.IsMathAbs(operation.TargetMethod) ||
            operation.TargetMethod.Parameters.Length != 1 ||
            !TryGetBoundedIntegralRange(operation.TargetMethod.ReturnType, out var minValue, out _) ||
            minValue >= 0 ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 0, out var operand) ||
            !TryCreateCheckedEqualityOverflowTrigger(
                invocation,
                operand,
                minValue,
                "ir.runtime-hazard.math.abs-overflow",
                semanticModel,
                cancellationToken,
                out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            invocation,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            trigger,
            ExceptionTypes.OverflowException,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    internal static bool TryCreateMathClampBoundsCandidate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
            !operation.TargetMethod.IsStatic ||
            !SymbolicKnownApiLowerer.IsMathClamp(operation.TargetMethod) ||
            operation.TargetMethod.Parameters.Length != 3 ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 1, out var minExpression) ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 2, out var maxExpression))
            return false;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        var min = SymbolicSemanticPipeline.LowerTerm(minExpression, context);
        var max = SymbolicSemanticPipeline.LowerTerm(maxExpression, context);
        if (min is not { IsExact: true, Value: { Kind: SmtValueKind.Int } minTerm } ||
            max is not { IsExact: true, Value: { Kind: SmtValueKind.Int } maxTerm })
            return false;

        var invalidBounds = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(SymbolicRelationOperator.GreaterThan, minTerm, maxTerm),
            invocation,
            "ir.runtime-hazard.math.clamp.invalid-bounds"));
        if (!TryCreateIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.ArgumentOutOfRange,
                null,
                invalidBounds,
                invocation,
                "ir.runtime-hazard.math.clamp.invalid-bounds",
                out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            invocation,
            SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            trigger,
            ExceptionTypes.ArgumentException,
            ExceptionCategories.DefiniteInvalidClampBounds);
        return true;
    }

    internal static bool TryGetRegexRequiredInputExpression(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax inputExpression)
    {
        inputExpression = null!;
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
            operation.TargetMethod.Name is not ("IsMatch" or "Match" or "Matches") ||
            !string.Equals(
                SymbolicTypeFacts.GetFullMetadataName(operation.TargetMethod.ContainingType),
                "System.Text.RegularExpressions.Regex",
                StringComparison.Ordinal))
            return false;

        for (var index = 0; index < operation.TargetMethod.Parameters.Length; index++)
            if (string.Equals(operation.TargetMethod.Parameters[index].Name, "input", StringComparison.Ordinal) &&
                SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, index, out inputExpression))
                return true;

        return false;
    }

    internal static IEnumerable<RuntimeHazardCandidate> CreateThrowCandidates(
        SyntaxNode throwNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var exceptionType = SymbolicRuntimeExceptionFacts.GetThrownExceptionType(
            throwNode,
            semanticModel,
            cancellationToken,
            false);
        var isRethrow = throwNode is ThrowStatementSyntax { Expression: null };
        if (!TryCreateDirectThrowTrigger(throwNode, out var directTrigger))
            throw new InvalidOperationException("Could not encode direct-throw runtime-hazard precondition.");

        if (!isRethrow &&
            SymbolicRuntimeExceptionFacts.TryGetThrowExpression(throwNode, out var thrownExpression) &&
            TryCreateReferenceNullCondition(
                thrownExpression,
                semanticModel,
                cancellationToken,
                "ir.runtime-hazard.throw-null.trigger",
                out var nullCondition))
        {
            var subject = nullCondition is SymbolicFactCondition
                {
                    Fact.Atom: SymbolicRelationAtom { Left: var left }
                }
                ? left
                : null;
            if (TryCreateIrExceptionPreconditionTrigger(
                    SymbolicExceptionPreconditionKind.NullDereference,
                    subject,
                    nullCondition,
                    throwNode,
                    "ir.runtime-hazard.throw-null",
                    out var nullTrigger))
                yield return new RuntimeHazardCandidate(
                    throwNode,
                    SymbolicRuntimeHazardKind.DirectThrow,
                    nullTrigger,
                    ExceptionTypes.NullReferenceException,
                    ExceptionCategories.DefiniteThrowNull);

            if (TryCreateIrExceptionPreconditionTrigger(
                    SymbolicExceptionPreconditionKind.DirectThrow,
                    subject,
                    new SymbolicNotCondition(nullCondition),
                    throwNode,
                    "ir.runtime-hazard.direct-throw.non-null",
                    out var nonNullTrigger))
                directTrigger = nonNullTrigger;
        }

        yield return new RuntimeHazardCandidate(
            throwNode,
            isRethrow ? SymbolicRuntimeHazardKind.Rethrow : SymbolicRuntimeHazardKind.DirectThrow,
            directTrigger,
            exceptionType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty) ??
            (isRethrow ? ExceptionTypes.Unknown : ExceptionTypes.Exception),
            isRethrow ? ExceptionCategories.Rethrow : ExceptionCategories.DirectThrow);
    }

    internal static bool TryCreateDivideByZeroCandidate(
        BinaryExpressionSyntax binaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!binaryExpression.IsKind(SyntaxKind.DivideExpression) &&
            !binaryExpression.IsKind(SyntaxKind.ModuloExpression))
            return false;

        return TryCreateOperationDivideByZeroCandidate(
            binaryExpression, semanticModel, cancellationToken, out candidate);
    }

    internal static bool TryCreateCheckedIntegralOverflowCandidate(
        BinaryExpressionSyntax binaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetCheckedIntegralBinaryOperator(
                binaryExpression,
                semanticModel,
                cancellationToken,
                out var smtOperator,
                out var minValue,
                out var maxValue))
            return false;

        var hasExactTrigger = TryCreateCheckedIntegralBinaryOverflowTrigger(
            binaryExpression,
            smtOperator,
            minValue,
            maxValue,
            semanticModel,
            cancellationToken,
            out var overflowTrigger);
        candidate = CreateCheckedOverflowCandidate(
            binaryExpression,
            hasExactTrigger,
            overflowTrigger,
            "ir.runtime-hazard.checked-integral-overflow.unsupported",
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    internal static bool TryCreateCheckedIntegralOverflowCandidate(
        PrefixUnaryExpressionSyntax unaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        if (TryCreateCheckedIntegralUnaryMinusOverflowCandidate(
                unaryExpression,
                semanticModel,
                cancellationToken,
                out candidate))
            return true;

        return TryCreateCheckedIntegralUpdateOverflowCandidate(
            unaryExpression,
            unaryExpression.Operand,
            semanticModel,
            cancellationToken,
            out candidate);
    }

    internal static bool TryCreateCheckedIntegralUnaryMinusOverflowCandidate(
        PrefixUnaryExpressionSyntax unaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetCheckedIntegralUnaryOperator(
                unaryExpression,
                semanticModel,
                cancellationToken,
                out var minValue,
                out var maxValue))
            return false;

        var hasExactTrigger = TryCreateCheckedIntegralUnaryOverflowTrigger(
            unaryExpression,
            minValue,
            maxValue,
            semanticModel,
            cancellationToken,
            out var overflowTrigger);
        candidate = CreateCheckedOverflowCandidate(
            unaryExpression,
            hasExactTrigger,
            overflowTrigger,
            "ir.runtime-hazard.checked-integral-overflow.unsupported",
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    internal static bool TryCreateCheckedIntegralOverflowCandidate(
        PostfixUnaryExpressionSyntax unaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        return TryCreateCheckedIntegralUpdateOverflowCandidate(
            unaryExpression,
            unaryExpression.Operand,
            semanticModel,
            cancellationToken,
            out candidate);
    }

    internal static bool TryCreateCheckedIntegralUpdateOverflowCandidate(
        ExpressionSyntax updateExpression,
        ExpressionSyntax operand,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetCheckedIntegralIncrementOrDecrementOperator(
                updateExpression,
                operand,
                semanticModel,
                cancellationToken,
                out var smtOperator,
                out var minValue,
                out var maxValue))
            return false;

        var hasExactTrigger = TryCreateCheckedIntegralUpdateOverflowTrigger(
            updateExpression,
            operand,
            smtOperator,
            minValue,
            maxValue,
            semanticModel,
            cancellationToken,
            out var overflowTrigger);
        candidate = CreateCheckedOverflowCandidate(
            updateExpression,
            hasExactTrigger,
            overflowTrigger,
            "ir.runtime-hazard.checked-integral-overflow.unsupported",
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    internal static bool TryCreateCheckedIntegralCompoundAssignmentOverflowCandidate(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetCheckedIntegralCompoundAssignmentOperator(
                assignment,
                semanticModel,
                cancellationToken,
                out var smtOperator,
                out var minValue,
                out var maxValue))
            return false;

        var hasExactTrigger = TryCreateCheckedIntegralCompoundAssignmentOverflowTrigger(
            assignment,
            smtOperator,
            minValue,
            maxValue,
            semanticModel,
            cancellationToken,
            out var overflowTrigger);
        candidate = CreateCheckedOverflowCandidate(
            assignment,
            hasExactTrigger,
            overflowTrigger,
            "ir.runtime-hazard.checked-integral-overflow.unsupported",
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    internal static bool TryCreateCompoundAssignmentDivideByZeroCandidate(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!assignment.IsKind(SyntaxKind.DivideAssignmentExpression) &&
            !assignment.IsKind(SyntaxKind.ModuloAssignmentExpression))
            return false;

        return TryCreateOperationDivideByZeroCandidate(
            assignment, semanticModel, cancellationToken, out candidate);
    }

    private static bool TryCreateOperationDivideByZeroCandidate(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        var operation = semanticModel.GetOperation(site, cancellationToken);
        if (operation == null ||
            !SymbolicOperationLowerer.TryLowerDivideByZeroHazard(
                operation,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(site, hazard);
        return true;
    }

    internal static bool TryCreateDeconstructionNullReceiverCandidate(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            UnwrapExpression(assignment.Left) is not TupleExpressionSyntax and not DeclarationExpressionSyntax)
            return false;

        var deconstructionInfo = semanticModel.GetDeconstructionInfo(assignment);
        if (deconstructionInfo.Method is not IMethodSymbol { IsStatic: false }) return false;

        return TryCreateNullDereferenceCandidate(
            assignment,
            assignment.Right,
            ExceptionCategories.DefiniteDeconstructionNull,
            semanticModel,
            cancellationToken,
            out candidate);
    }

    internal static bool TryCreateArrayTypeMismatchCandidate(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            UnwrapExpression(assignment.Left) is not ElementAccessExpressionSyntax elementAccess ||
            !TryGetArrayElementStoreType(elementAccess, semanticModel, cancellationToken, out var arrayType) ||
            !IsReferenceType(arrayType.ElementType))
            return false;

        if (!TryCreateArrayStoreMismatchTrigger(
            assignment,
            elementAccess,
            arrayType,
            semanticModel,
            cancellationToken,
            out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            assignment,
            SymbolicRuntimeHazardKind.ArrayTypeMismatch,
            trigger,
            ExceptionTypes.ArrayTypeMismatchException,
            ExceptionCategories.DefiniteArrayTypeMismatch);
        return true;
    }

    internal static bool TryCreateCheckedExplicitNumericConversionOverflowCandidate(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetCheckedExplicitNumericConversionRange(
                castExpression,
                semanticModel,
                cancellationToken,
                out var minValue,
                out var maxValue))
            return false;

        var hasExactTrigger = TryCreateCheckedExplicitNumericConversionOverflowTrigger(
            castExpression,
            minValue,
            maxValue,
            semanticModel,
            cancellationToken,
            out var overflowTrigger);
        candidate = CreateCheckedOverflowCandidate(
            castExpression,
            hasExactTrigger,
            overflowTrigger,
            "ir.runtime-hazard.checked-numeric-conversion-overflow.unsupported",
            ExceptionCategories.DefiniteCheckedNumericConversionOverflow);
        return true;
    }

    internal static RuntimeHazardCandidate CreateCheckedOverflowCandidate(
        SyntaxNode site,
        bool hasExactTrigger,
        RuntimeHazardTrigger exactTrigger,
        string unsupportedProvenance,
        string category)
    {
        var trigger = hasExactTrigger
            ? exactTrigger
            : CreateUnsupportedExceptionPreconditionTrigger(
                site,
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                unsupportedProvenance);
        return new RuntimeHazardCandidate(
            site,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            trigger,
            ExceptionTypes.OverflowException,
            category);
    }

    internal static bool TryCreateUnboxNullCastCandidate(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
            conversionOperation.Conversion.IsUserDefined ||
            !IsUnboxingCastShape(castExpression, conversionOperation.Type, semanticModel, cancellationToken))
            return false;

        var trigger = TryCreateUnboxNullTrigger(
            castExpression.Expression,
            semanticModel,
            cancellationToken,
            out var unboxNullTrigger)
            ? unboxNullTrigger
            : CreateUnsupportedExceptionPreconditionTrigger(
                castExpression,
                SymbolicExceptionPreconditionKind.UnboxNull,
                null,
                "ir.runtime-hazard.unbox-null.unsupported");

        candidate = new RuntimeHazardCandidate(
            castExpression,
            SymbolicRuntimeHazardKind.UnboxNull,
            trigger,
            ExceptionTypes.NullReferenceException,
            ExceptionCategories.DefiniteUnboxNull);
        return true;
    }

    internal static bool TryCreateNullableValueCastCandidate(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetBuiltInNonIdentityConversion(
                castExpression,
                semanticModel,
                cancellationToken,
                out _,
                out var targetType) ||
            !IsNullableValueCastShape(castExpression, targetType, semanticModel, cancellationToken) ||
            !TryCreateNullableValueWithoutValueTrigger(
                castExpression.Expression,
                semanticModel,
                cancellationToken,
                out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            castExpression,
            SymbolicRuntimeHazardKind.NullableValueWithoutValue,
            trigger,
            ExceptionTypes.InvalidOperationException,
            ExceptionCategories.DefiniteNullableValueWithoutValue);
        return true;
    }

    internal static bool TryCreateInvalidCastCandidate(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetBuiltInNonIdentityConversion(
                castExpression,
                semanticModel,
                cancellationToken,
                out _,
                out var targetType))
            return false;

        var isUnboxing = IsUnboxingCastShape(castExpression, targetType, semanticModel, cancellationToken);
        if (!isUnboxing)
        {
            var operandType = CSharpSyntaxFacts.GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
            if (!IsReferenceType(targetType) ||
                !IsReferenceType(operandType))
                return false;
        }

        if (IsDefinitelyNullInvalidCastOperand(castExpression, semanticModel, cancellationToken))
            return false;

        if (SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                castExpression.Expression,
                castExpression,
                semanticModel,
                cancellationToken,
                out var exactRuntimeType))
        {
            var conversionIsValid = isUnboxing
                ? SymbolicRuntimeTypeFacts.CanUnboxExactRuntimeTypeToValueType(exactRuntimeType, targetType)
                : SymbolicRuntimeTypeFacts.CanCastExactRuntimeTypeToReferenceType(
                        exactRuntimeType,
                        targetType,
                        semanticModel.Compilation);
            return TryCreateExactRuntimeInvalidCastCandidate(
                castExpression,
                conversionIsValid,
                semanticModel,
                cancellationToken,
                out candidate);
        }

        if (!isUnboxing &&
            TryCreateRuntimeReferenceInvalidCastTrigger(
                castExpression.Expression,
                targetType,
                semanticModel,
                cancellationToken,
                out var irInvalidCastTrigger))
        {
            candidate = CreateInvalidCastCandidate(castExpression, irInvalidCastTrigger);
            return true;
        }

        candidate = CreateInvalidCastCandidate(
            castExpression,
            CreateUnsupportedInvalidCastTrigger(castExpression));
        return true;
    }

    private static bool IsDefinitelyNullInvalidCastOperand(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        TryCreateReferenceNullCondition(
            castExpression.Expression,
            semanticModel,
            cancellationToken,
            "ir.runtime-hazard.invalid-cast.null-operand",
            out var nullCondition) &&
        nullCondition is SymbolicConstantCondition { Value: true };

    private static bool TryCreateExactRuntimeInvalidCastCandidate(
        CastExpressionSyntax castExpression,
        bool conversionIsValid,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (conversionIsValid) return false;

        var trigger = TryCreateExactRuntimeInvalidCastTrigger(
            castExpression.Expression,
            semanticModel,
            cancellationToken,
            out var exactTrigger)
            ? exactTrigger
            : CreateUnsupportedInvalidCastTrigger(castExpression);

        candidate = CreateInvalidCastCandidate(castExpression, trigger);
        return true;
    }

    private static RuntimeHazardTrigger CreateUnsupportedInvalidCastTrigger(CastExpressionSyntax castExpression) =>
        CreateUnsupportedExceptionPreconditionTrigger(
            castExpression.Expression,
            SymbolicExceptionPreconditionKind.InvalidCast,
            null,
            "ir.runtime-hazard.invalid-cast.unsupported");

    private static RuntimeHazardCandidate CreateInvalidCastCandidate(
        CastExpressionSyntax castExpression,
        RuntimeHazardTrigger trigger) =>
        new(
            castExpression,
            SymbolicRuntimeHazardKind.InvalidCast,
            trigger,
            ExceptionTypes.InvalidCastException,
            ExceptionCategories.DefiniteInvalidCast);

    internal static bool TryCreateNullDereferenceCandidate(
        SyntaxNode site,
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        return TryCreateNullDereferenceCandidate(
            site,
            receiver,
            ExceptionCategories.DefiniteNullDereference,
            semanticModel,
            cancellationToken,
            out candidate);
    }

    internal static bool TryCreateAwaitNullDereferenceCandidate(
        AwaitExpressionSyntax awaitExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        return TryCreateNullDereferenceCandidate(
            awaitExpression,
            awaitExpression.Expression,
            ExceptionCategories.DefiniteAwaitNull,
            semanticModel,
            cancellationToken,
            out candidate);
    }

    internal static bool TryCreateInvalidCollectionCardinalityCandidate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
            !TryGetCollectionCardinalityContract(
                invocation,
                operation,
                out var receiver,
                out var relation,
                out var triggeringCount) ||
            !TryCreateInvalidCollectionCardinalityTrigger(
                receiver,
                relation,
                triggeringCount,
                semanticModel,
                cancellationToken,
                out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            invocation,
            SymbolicRuntimeHazardKind.InvalidCollectionCardinality,
            trigger,
            ExceptionTypes.InvalidOperationException,
            ExceptionCategories.DefiniteInvalidCollectionCardinality);
        return true;
    }

    internal static bool TryGetCollectionCardinalityContract(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        out ExpressionSyntax receiver,
        out SymbolicRelationOperator relation,
        out long triggeringCount)
    {
        receiver = null!;
        relation = default;
        triggeringCount = 0;
        var method = operation.TargetMethod;
        if (!method.IsStatic &&
            method.Parameters.Length == 0 &&
            method.Name is "Dequeue" or "Peek" or "Pop" &&
            IsKnownCardinalityCheckedCollection(method.ContainingType) &&
            invocation.Expression is MemberAccessExpressionSyntax instanceMember)
        {
            receiver = instanceMember.Expression;
            relation = SymbolicRelationOperator.Equal;
            return true;
        }

        return false;
    }

    internal static bool IsKnownCardinalityCheckedCollection(INamedTypeSymbol type)
    {
        return type.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" &&
               type.OriginalDefinition.MetadataName is "Queue`1" or "Stack`1" or "PriorityQueue`2";
    }

    internal static bool TryCreateNullDereferenceCandidate(
        SyntaxNode site,
        ExpressionSyntax receiver,
        string category,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        var receiverType = CSharpSyntaxFacts.GetExpressionType(receiver, semanticModel, cancellationToken);
        if (IsDynamicExpression(receiver, semanticModel, cancellationToken) ||
            !IsReferenceType(receiverType) ||
            !TryCreateNullDereferenceTrigger(receiver, semanticModel, cancellationToken, out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            site,
            SymbolicRuntimeHazardKind.NullDereference,
            trigger,
            ExceptionTypes.NullReferenceException,
            category);
        return true;
    }

    internal static bool TryCreateArgumentNullCandidate(
        SyntaxNode site,
        ExpressionSyntax expression,
        string category,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        var expressionType = CSharpSyntaxFacts.GetExpressionType(expression, semanticModel, cancellationToken);
        if (IsDynamicExpression(expression, semanticModel, cancellationToken) ||
            !IsReferenceType(expressionType) ||
            !TryCreateArgumentNullTrigger(expression, semanticModel, cancellationToken, out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            site,
            SymbolicRuntimeHazardKind.ArgumentNull,
            trigger,
            ExceptionTypes.ArgumentNullException,
            category);
        return true;
    }

    internal static bool TryCreateDynamicInvocationNullBindingCandidate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!SymbolicDynamicNullBindingFacts.TryGetDynamicNullBindingShape(
                invocation,
                UnwrapExpression,
                out var site,
                out var receiver,
                out var category,
                out _))
            return false;

        return TryCreateDynamicNullBindingCandidate(
            site,
            receiver,
            category,
            semanticModel,
            cancellationToken,
            out candidate);
    }

    internal static bool TryCreateDynamicNullBindingCandidate(
        SyntaxNode site,
        ExpressionSyntax receiver,
        string category,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!IsDynamicExpression(receiver, semanticModel, cancellationToken) ||
            !TryCreateDynamicNullBindingTrigger(receiver, semanticModel, cancellationToken, out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            site,
            SymbolicRuntimeHazardKind.DynamicNullBinding,
            trigger,
            SymbolicDynamicNullBindingFacts.RuntimeBinderExceptionType,
            category);
        return true;
    }

    internal static bool TryCreateNullableValueCandidate(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!SymbolicTypeFacts.IsNullableValueAccess(memberAccess, semanticModel, cancellationToken))
            return false;

        RuntimeHazardTrigger trigger;
        if (HasLaterLoopAssignmentOfMissingNullableValue(
                memberAccess.Expression,
                memberAccess,
                semanticModel,
                cancellationToken))
        {
            if (!TryCreateIrExceptionPreconditionTrigger(
                    SymbolicExceptionPreconditionKind.NullableValueWithoutValue,
                    null,
                    new SymbolicConstantCondition(true),
                    memberAccess,
                    "ir.runtime-hazard.nullable-value.loop-carried",
                    out trigger))
                return false;
        }
        else if (!TryCreateNullableValueWithoutValueTrigger(
                     memberAccess.Expression,
                     semanticModel,
                     cancellationToken,
                     out trigger))
        {
            return false;
        }

        candidate = new RuntimeHazardCandidate(
            memberAccess,
            SymbolicRuntimeHazardKind.NullableValueWithoutValue,
            trigger,
            ExceptionTypes.InvalidOperationException,
            ExceptionCategories.DefiniteNullableValueWithoutValue);
        return true;
    }

    internal static bool TryCreateIndexOrRangeCandidate(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetIndexOrRangeHazardMetadata(
                elementAccess,
                semanticModel,
                cancellationToken,
                out var kind,
                out var exceptionType,
                out var category) ||
            !TryCreateIndexOrRangeTrigger(
                elementAccess,
                kind,
                semanticModel,
                cancellationToken,
                out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            elementAccess,
            kind,
            trigger,
            exceptionType,
            category);
        return true;
    }

    internal static bool TryCreateSlicingArgumentOutOfRangeCandidate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
            !TryGetSlicingInvocationShape(
                invocationOperation,
                out var sourceExpression,
                out var startExpression,
                out var countExpression,
                out var oneArgumentUpperBoundIsInclusive,
                out var category))
            return false;

        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        if (SymbolicSemanticPipeline.LowerSubsequenceInRangeCondition(
                sourceExpression,
                startExpression,
                countExpression,
                invocation,
                context,
                oneArgumentUpperBoundIsInclusive) is { IsExact: true, Value: { } inRangeCondition } &&
            TryCreateIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.ArgumentOutOfRange,
                null,
                new SymbolicNotCondition(inRangeCondition),
                invocation,
                "ir.runtime-hazard.slicing.argument-out-of-range",
                out var irTrigger))
        {
            candidate = new RuntimeHazardCandidate(
                invocation,
                SymbolicRuntimeHazardKind.ArgumentOutOfRange,
                irTrigger,
                ExceptionTypes.ArgumentOutOfRangeException,
                category);
            return true;
        }

        candidate = new RuntimeHazardCandidate(
            invocation,
            SymbolicRuntimeHazardKind.ArgumentOutOfRange,
            CreateUnsupportedExceptionPreconditionTrigger(
                invocation,
                SymbolicExceptionPreconditionKind.ArgumentOutOfRange,
                null,
                "ir.runtime-hazard.slicing.argument-out-of-range.unsupported"),
            ExceptionTypes.ArgumentOutOfRangeException,
            category);
        return true;
    }

    internal static bool TryCreateArrayGetValueIndexOutOfRangeCandidate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation invocationOperation ||
            !IsArrayGetValueInvocation(invocationOperation.TargetMethod) ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
            invocationOperation.Instance.Type is not IArrayTypeSymbol arrayType ||
            invocationOperation.Arguments.Length != arrayType.Rank)
            return false;

        if (TryCreateIrArrayGetValueIndexOutOfRangeTrigger(
                invocation,
                invocationOperation,
                receiverExpression,
                arrayType,
                semanticModel,
                cancellationToken,
                out var trigger))
        {
            candidate = new RuntimeHazardCandidate(
                invocation,
                SymbolicRuntimeHazardKind.IndexOutOfRange,
                trigger,
                ExceptionTypes.IndexOutOfRangeException,
                ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange);
            return true;
        }

        candidate = new RuntimeHazardCandidate(
            invocation,
            SymbolicRuntimeHazardKind.IndexOutOfRange,
            CreateUnsupportedExceptionPreconditionTrigger(
                invocation,
                SymbolicExceptionPreconditionKind.IndexOutOfRange,
                null,
                "ir.runtime-hazard.array-get-value.index-out-of-range.unsupported"),
            ExceptionTypes.IndexOutOfRangeException,
            ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange);
        return true;
    }

    internal static bool TryCreateNegativeArrayLengthCandidate(
        ArrayCreationExpressionSyntax arrayCreation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        return TryCreateNegativeLengthCandidate(
            arrayCreation,
            CSharpSyntaxFacts.GetExplicitArraySizeExpressions(arrayCreation),
            SymbolicExceptionPreconditionKind.NegativeLength,
            SymbolicRuntimeHazardKind.NegativeArrayLength,
            "ir.runtime-hazard.array.negative-length",
            ExceptionCategories.DefiniteNegativeArrayLength,
            semanticModel,
            cancellationToken,
            out candidate);
    }

    internal static bool TryCreateNegativeStackAllocLengthCandidate(
        StackAllocArrayCreationExpressionSyntax stackAllocCreation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        return TryCreateNegativeLengthCandidate(
            stackAllocCreation,
            GetStackAllocLengthExpressions(stackAllocCreation),
            SymbolicExceptionPreconditionKind.NegativeStackAllocLength,
            SymbolicRuntimeHazardKind.NegativeStackAllocLength,
            "ir.runtime-hazard.stackalloc.negative-length",
            ExceptionCategories.DefiniteNegativeStackAllocLength,
            semanticModel,
            cancellationToken,
            out candidate);
    }

    internal static bool TryCreateNegativeLengthCandidate(
        SyntaxNode site,
        IEnumerable<ExpressionSyntax> lengthExpressions,
        SymbolicExceptionPreconditionKind preconditionKind,
        SymbolicRuntimeHazardKind hazardKind,
        string provenance,
        string category,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        var triggerCondition = default(SymbolicCondition);
        var subject = default(SymbolicTerm);
        var allTriggersAreExact = true;
        var hasTrigger = false;
        foreach (var lengthExpression in lengthExpressions)
        {
            if (!TryCreateNegativeLengthTrigger(
                    lengthExpression,
                    preconditionKind,
                    provenance,
                    semanticModel,
                    cancellationToken,
                    out var negativeLength))
                continue;

            hasTrigger = true;
            if (TryGetExceptionPrecondition(
                    negativeLength,
                    preconditionKind,
                    out var precondition))
            {
                triggerCondition = triggerCondition == null
                    ? precondition.Trigger
                    : new SymbolicBinaryCondition(SymbolicConditionOperator.Or, triggerCondition, precondition.Trigger);
                subject ??= precondition.Subject;
                allTriggersAreExact &= negativeLength.Precondition.Confidence == SymbolicFactConfidence.Exact;
            }
            else
            {
                allTriggersAreExact = false;
            }
        }

        if (!hasTrigger) return false;

        candidate = new RuntimeHazardCandidate(
            site,
            hazardKind,
            CreateAggregateExceptionPreconditionTrigger(
                site,
                preconditionKind,
                subject,
                triggerCondition,
                allTriggersAreExact,
                provenance + ".aggregate"),
            ExceptionTypes.OverflowException,
            category);
        return true;
    }
}
