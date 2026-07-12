using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionTypes = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionTypes;

namespace SharpProof.Symbolic;

internal sealed partial class SymbolicRuntimeHazardQueryService
{
    private static IEnumerable<RuntimeHazardCandidate> EnumerateCandidates(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool includeNestedCallables)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodesAndSelf(
                     descendIntoTrivia: false,
                     descendIntoChildren: candidate =>
                         includeNestedCallables ||
                         ReferenceEquals(candidate, root) ||
                         !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var candidate in EnumerateCandidatesForNode(node, semanticModel, cancellationToken))
            {
                var key = candidate.Kind + ":" + candidate.Site.SpanStart + ":" + candidate.Site.Span.End;
                if (seen.Add(key)) yield return candidate;
            }
        }
    }

    private static IEnumerable<RuntimeHazardCandidate> EnumerateCandidatesForNode(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        switch (node)
        {
            case ThrowStatementSyntax throwStatement:
                yield return CreateThrowCandidate(throwStatement, semanticModel, cancellationToken);
                break;
            case ThrowExpressionSyntax throwExpression:
                yield return CreateThrowCandidate(throwExpression, semanticModel, cancellationToken);
                break;
            case BinaryExpressionSyntax binaryExpression:
                if (TryCreateDivideByZeroCandidate(binaryExpression, semanticModel, cancellationToken,
                        out var divideCandidate)) yield return divideCandidate;

                if (TryCreateCheckedIntegralOverflowCandidate(binaryExpression, semanticModel, cancellationToken,
                        out var binaryOverflowCandidate)) yield return binaryOverflowCandidate;

                break;
            case PrefixUnaryExpressionSyntax prefixUnaryExpression:
                if (TryCreateCheckedIntegralOverflowCandidate(prefixUnaryExpression, semanticModel, cancellationToken,
                        out var unaryOverflowCandidate)) yield return unaryOverflowCandidate;

                break;
            case PostfixUnaryExpressionSyntax postfixUnaryExpression:
                if (TryCreateCheckedIntegralOverflowCandidate(postfixUnaryExpression, semanticModel, cancellationToken,
                        out var postfixOverflowCandidate)) yield return postfixOverflowCandidate;

                break;
            case CastExpressionSyntax castExpression:
                if (TryCreateCheckedExplicitNumericConversionOverflowCandidate(castExpression, semanticModel,
                        cancellationToken, out var conversionOverflowCandidate))
                    yield return conversionOverflowCandidate;

                if (TryCreateNullableValueCastCandidate(castExpression, semanticModel, cancellationToken,
                        out var nullableCastCandidate)) yield return nullableCastCandidate;

                if (TryCreateUnboxNullCastCandidate(castExpression, semanticModel, cancellationToken,
                        out var unboxNullCandidate)) yield return unboxNullCandidate;

                if (TryCreateInvalidCastCandidate(castExpression, semanticModel, cancellationToken,
                        out var invalidCastCandidate)) yield return invalidCastCandidate;

                break;
            case MemberAccessExpressionSyntax memberAccess:
                if (TryCreateNullableValueCandidate(memberAccess, semanticModel, cancellationToken,
                        out var nullableCandidate)) yield return nullableCandidate;

                if (SymbolicDynamicNullBindingFacts.TryGetDynamicNullBindingShape(
                        memberAccess,
                        UnwrapDynamicExpression,
                        out var memberDynamicSite,
                        out var memberDynamicReceiver,
                        out var memberDynamicCategory,
                        out _) &&
                    TryCreateDynamicNullBindingCandidate(
                        memberDynamicSite,
                        memberDynamicReceiver,
                        memberDynamicCategory,
                        semanticModel,
                        cancellationToken,
                        out var memberDynamicCandidate))
                    yield return memberDynamicCandidate;

                if (TryCreateNullDereferenceCandidate(memberAccess, memberAccess.Expression, semanticModel,
                        cancellationToken, out var memberNullCandidate)) yield return memberNullCandidate;

                break;
            case ElementAccessExpressionSyntax elementAccess:
                if (SymbolicDynamicNullBindingFacts.TryGetDynamicNullBindingShape(
                        elementAccess,
                        UnwrapDynamicExpression,
                        out var elementDynamicSite,
                        out var elementDynamicReceiver,
                        out var elementDynamicCategory,
                        out _) &&
                    TryCreateDynamicNullBindingCandidate(
                        elementDynamicSite,
                        elementDynamicReceiver,
                        elementDynamicCategory,
                        semanticModel,
                        cancellationToken,
                        out var elementDynamicCandidate))
                    yield return elementDynamicCandidate;

                if (TryCreateNullDereferenceCandidate(elementAccess, elementAccess.Expression, semanticModel,
                        cancellationToken, out var elementNullCandidate)) yield return elementNullCandidate;

                if (TryCreateIndexOrRangeCandidate(elementAccess, semanticModel, cancellationToken,
                        out var indexCandidate)) yield return indexCandidate;

                break;
            case AssignmentExpressionSyntax assignment:
                if (TryCreateCompoundAssignmentDivideByZeroCandidate(assignment, semanticModel, cancellationToken,
                        out var compoundDivideCandidate)) yield return compoundDivideCandidate;

                if (TryCreateDeconstructionNullReceiverCandidate(assignment, semanticModel, cancellationToken,
                        out var deconstructionNullCandidate)) yield return deconstructionNullCandidate;

                if (TryCreateArrayTypeMismatchCandidate(assignment, semanticModel, cancellationToken,
                        out var arrayTypeMismatchCandidate)) yield return arrayTypeMismatchCandidate;

                if (TryCreateCheckedIntegralCompoundAssignmentOverflowCandidate(assignment, semanticModel,
                        cancellationToken, out var compoundOverflowCandidate)) yield return compoundOverflowCandidate;

                break;
            case ArrayCreationExpressionSyntax arrayCreation:
                if (TryCreateNegativeArrayLengthCandidate(arrayCreation, semanticModel, cancellationToken,
                        out var negativeLengthCandidate)) yield return negativeLengthCandidate;

                break;
            case StackAllocArrayCreationExpressionSyntax stackAllocCreation:
                if (TryCreateNegativeStackAllocLengthCandidate(
                        stackAllocCreation,
                        semanticModel,
                        cancellationToken,
                        out var negativeStackAllocLengthCandidate))
                    yield return negativeStackAllocLengthCandidate;

                break;
            case SwitchExpressionSyntax switchExpression:
                if (TryCreateSwitchExpressionNoMatchCandidate(
                        switchExpression,
                        semanticModel,
                        cancellationToken,
                        out var switchNoMatchCandidate))
                    yield return switchNoMatchCandidate;

                break;
            case ForEachStatementSyntax forEachStatement:
                if (TryCreateNullDereferenceCandidate(forEachStatement, forEachStatement.Expression, semanticModel,
                        cancellationToken, out var foreachNullCandidate)) yield return foreachNullCandidate;

                break;
            case ForEachVariableStatementSyntax forEachVariableStatement:
                if (TryCreateNullDereferenceCandidate(forEachVariableStatement, forEachVariableStatement.Expression,
                        semanticModel, cancellationToken, out var foreachVariableNullCandidate))
                    yield return foreachVariableNullCandidate;

                break;
            case LockStatementSyntax lockStatement:
                if (TryCreateArgumentNullCandidate(
                        lockStatement,
                        lockStatement.Expression,
                        ExceptionCategories.DefiniteLockNull,
                        semanticModel,
                        cancellationToken,
                        out var lockNullCandidate))
                    yield return lockNullCandidate;

                break;
            case InvocationExpressionSyntax invocation:
                if (TryCreateMathAbsOverflowCandidate(invocation, semanticModel, cancellationToken,
                        out var mathAbsOverflowCandidate))
                    yield return mathAbsOverflowCandidate;

                if (TryCreateArgumentOutOfRangeGuardCandidate(invocation, semanticModel, cancellationToken,
                        out var guardCandidate)) yield return guardCandidate;

                if (TryCreateDynamicInvocationNullBindingCandidate(invocation, semanticModel, cancellationToken,
                        out var invocationDynamicCandidate)) yield return invocationDynamicCandidate;

                if (TryCreateArrayGetValueIndexOutOfRangeCandidate(invocation, semanticModel, cancellationToken,
                        out var arrayGetValueCandidate)) yield return arrayGetValueCandidate;

                if (TryCreateSlicingArgumentOutOfRangeCandidate(invocation, semanticModel, cancellationToken,
                        out var slicingCandidate)) yield return slicingCandidate;

                if (TryCreateInvalidCollectionCardinalityCandidate(
                        invocation,
                        semanticModel,
                        cancellationToken,
                        out var collectionCardinalityCandidate))
                    yield return collectionCardinalityCandidate;

                if (invocation.Expression is not MemberAccessExpressionSyntax &&
                    TryCreateNullDereferenceCandidate(invocation, invocation.Expression, semanticModel,
                        cancellationToken, out var invocationNullCandidate))
                    yield return invocationNullCandidate;

                break;
            case AwaitExpressionSyntax awaitExpression:
                if (TryCreateAwaitNullDereferenceCandidate(awaitExpression, semanticModel, cancellationToken,
                        out var awaitNullCandidate)) yield return awaitNullCandidate;

                break;
            case WithExpressionSyntax withExpression:
                if (TryCreateNullDereferenceCandidate(
                        withExpression,
                        withExpression.Expression,
                        ExceptionCategories.DefiniteWithNull,
                        semanticModel,
                        cancellationToken,
                        out var withNullCandidate))
                    yield return withNullCandidate;

                break;
        }
    }

    private static bool TryCreateMathAbsOverflowCandidate(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (semanticModel.GetOperation(invocation, cancellationToken) is not IInvocationOperation operation ||
            !operation.TargetMethod.IsStatic ||
            !string.Equals(operation.TargetMethod.Name, nameof(Math.Abs), StringComparison.Ordinal) ||
            !string.Equals(
                SymbolicTypeFacts.GetFullMetadataName(operation.TargetMethod.ContainingType),
                "System.Math",
                StringComparison.Ordinal) ||
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

    private static RuntimeHazardCandidate CreateThrowCandidate(
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
        if (!TryCreateDirectThrowTrigger(throwNode, out var trigger))
            throw new InvalidOperationException("Could not encode direct-throw runtime-hazard precondition.");
        return new RuntimeHazardCandidate(
            throwNode,
            isRethrow ? SymbolicRuntimeHazardKind.Rethrow : SymbolicRuntimeHazardKind.DirectThrow,
            trigger,
            exceptionType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty) ??
            (isRethrow ? ExceptionTypes.Unknown : ExceptionTypes.Exception),
            isRethrow ? ExceptionCategories.Rethrow : ExceptionCategories.DirectThrow);
    }

    private static bool TryCreateDivideByZeroCandidate(
        BinaryExpressionSyntax binaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!binaryExpression.IsKind(SyntaxKind.DivideExpression) &&
            !binaryExpression.IsKind(SyntaxKind.ModuloExpression))
            return false;

        var rightTypeInfo = semanticModel.GetTypeInfo(binaryExpression.Right, cancellationToken);
        var rightType = rightTypeInfo.ConvertedType ?? rightTypeInfo.Type;
        if (!IsThrowingDivideByZeroType(rightType) ||
            !TryCreateDivideByZeroTrigger(binaryExpression.Right, semanticModel, cancellationToken, out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            binaryExpression,
            SymbolicRuntimeHazardKind.DivideByZero,
            trigger,
            ExceptionTypes.DivideByZeroException,
            binaryExpression.IsKind(SyntaxKind.ModuloExpression)
                ? ExceptionCategories.DefiniteModuloByZero
                : ExceptionCategories.DefiniteDivideByZero);
        return true;
    }

    private static bool TryCreateCheckedIntegralOverflowCandidate(
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

        var trigger = TryCreateCheckedIntegralBinaryOverflowTrigger(
            binaryExpression,
            smtOperator,
            minValue,
            maxValue,
            semanticModel,
            cancellationToken,
            out var overflowTrigger)
            ? overflowTrigger
            : CreateUnsupportedExceptionPreconditionTrigger(
                binaryExpression,
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                "ir.runtime-hazard.checked-integral-overflow.unsupported");

        candidate = new RuntimeHazardCandidate(
            binaryExpression,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            trigger,
            ExceptionTypes.OverflowException,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    private static bool TryCreateCheckedIntegralOverflowCandidate(
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

    private static bool TryCreateCheckedIntegralUnaryMinusOverflowCandidate(
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

        var trigger = TryCreateCheckedIntegralUnaryOverflowTrigger(
            unaryExpression,
            minValue,
            maxValue,
            semanticModel,
            cancellationToken,
            out var overflowTrigger)
            ? overflowTrigger
            : CreateUnsupportedExceptionPreconditionTrigger(
                unaryExpression,
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                "ir.runtime-hazard.checked-integral-overflow.unsupported");

        candidate = new RuntimeHazardCandidate(
            unaryExpression,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            trigger,
            ExceptionTypes.OverflowException,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    private static bool TryCreateCheckedIntegralOverflowCandidate(
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

    private static bool TryCreateCheckedIntegralUpdateOverflowCandidate(
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

        var trigger = TryCreateCheckedIntegralUpdateOverflowTrigger(
            updateExpression,
            operand,
            smtOperator,
            minValue,
            maxValue,
            semanticModel,
            cancellationToken,
            out var overflowTrigger)
            ? overflowTrigger
            : CreateUnsupportedExceptionPreconditionTrigger(
                updateExpression,
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                "ir.runtime-hazard.checked-integral-overflow.unsupported");

        candidate = new RuntimeHazardCandidate(
            updateExpression,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            trigger,
            ExceptionTypes.OverflowException,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    private static bool TryCreateCheckedIntegralCompoundAssignmentOverflowCandidate(
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

        var trigger = TryCreateCheckedIntegralCompoundAssignmentOverflowTrigger(
            assignment,
            smtOperator,
            minValue,
            maxValue,
            semanticModel,
            cancellationToken,
            out var overflowTrigger)
            ? overflowTrigger
            : CreateUnsupportedExceptionPreconditionTrigger(
                assignment,
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                "ir.runtime-hazard.checked-integral-overflow.unsupported");

        candidate = new RuntimeHazardCandidate(
            assignment,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            trigger,
            ExceptionTypes.OverflowException,
            ExceptionCategories.DefiniteCheckedIntegralOverflow);
        return true;
    }

    private static bool TryCreateCompoundAssignmentDivideByZeroCandidate(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!assignment.IsKind(SyntaxKind.DivideAssignmentExpression) &&
            !assignment.IsKind(SyntaxKind.ModuloAssignmentExpression))
            return false;

        var rightTypeInfo = semanticModel.GetTypeInfo(assignment.Right, cancellationToken);
        var rightType = rightTypeInfo.ConvertedType ?? rightTypeInfo.Type;
        if (!IsThrowingDivideByZeroType(rightType) ||
            !TryCreateDivideByZeroTrigger(assignment.Right, semanticModel, cancellationToken, out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            assignment,
            SymbolicRuntimeHazardKind.DivideByZero,
            trigger,
            ExceptionTypes.DivideByZeroException,
            assignment.IsKind(SyntaxKind.ModuloAssignmentExpression)
                ? ExceptionCategories.DefiniteModuloByZero
                : ExceptionCategories.DefiniteDivideByZero);
        return true;
    }

    private static bool TryCreateDeconstructionNullReceiverCandidate(
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

        var receiver = assignment.Right;
        var receiverType = GetExpressionType(receiver, semanticModel, cancellationToken);
        if (IsDynamicExpression(receiver, semanticModel, cancellationToken) ||
            !IsReferenceType(receiverType) ||
            !TryCreateNullDereferenceTrigger(receiver, semanticModel, cancellationToken, out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            assignment,
            SymbolicRuntimeHazardKind.NullDereference,
            trigger,
            ExceptionTypes.NullReferenceException,
            ExceptionCategories.DefiniteDeconstructionNull);
        return true;
    }

    private static bool TryCreateArrayTypeMismatchCandidate(
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

    private static bool TryCreateCheckedExplicitNumericConversionOverflowCandidate(
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

        var trigger = TryCreateCheckedExplicitNumericConversionOverflowTrigger(
            castExpression,
            minValue,
            maxValue,
            semanticModel,
            cancellationToken,
            out var overflowTrigger)
            ? overflowTrigger
            : CreateUnsupportedExceptionPreconditionTrigger(
                castExpression,
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                "ir.runtime-hazard.checked-numeric-conversion-overflow.unsupported");

        candidate = new RuntimeHazardCandidate(
            castExpression,
            SymbolicRuntimeHazardKind.CheckedIntegralOverflow,
            trigger,
            ExceptionTypes.OverflowException,
            ExceptionCategories.DefiniteCheckedNumericConversionOverflow);
        return true;
    }

    private static bool TryCreateUnboxNullCastCandidate(
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

    private static bool TryCreateNullableValueCastCandidate(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
            conversionOperation.Conversion.IsUserDefined ||
            conversionOperation.Conversion.IsIdentity ||
            !IsNullableValueCastShape(castExpression, conversionOperation.Type, semanticModel, cancellationToken) ||
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

    private static bool TryCreateInvalidCastCandidate(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!TryGetConversionOperation(castExpression, semanticModel, cancellationToken, out var conversionOperation) ||
            conversionOperation.Conversion.IsUserDefined ||
            conversionOperation.Conversion.IsIdentity ||
            conversionOperation.Type is not { } targetType ||
            targetType.TypeKind == TypeKind.Dynamic)
            return false;

        if (IsUnboxingCastShape(castExpression, targetType, semanticModel, cancellationToken))
        {
            if (TryCreateReferenceNullCondition(
                    castExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    "ir.runtime-hazard.invalid-cast.null-operand",
                    out var nullCondition) &&
                nullCondition is SymbolicConstantCondition { Value: true })
                return false;

            if (SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                    castExpression.Expression,
                    castExpression,
                    semanticModel,
                    cancellationToken,
                    out var exactRuntimeType))
            {
                if (SymbolicRuntimeTypeFacts.CanUnboxExactRuntimeTypeToValueType(exactRuntimeType, targetType))
                    return false;

                if (TryCreateExactRuntimeInvalidCastTrigger(
                        castExpression.Expression,
                        semanticModel,
                        cancellationToken,
                        out var exactInvalidCastTrigger))
                {
                    candidate = new RuntimeHazardCandidate(
                        castExpression,
                        SymbolicRuntimeHazardKind.InvalidCast,
                        exactInvalidCastTrigger,
                        ExceptionTypes.InvalidCastException,
                        ExceptionCategories.DefiniteInvalidCast);
                    return true;
                }
            }

        }
        else
        {
            var operandType = GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
            if (!IsReferenceType(targetType) ||
                !IsReferenceType(operandType))
                return false;

            if (TryCreateReferenceNullCondition(
                    castExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    "ir.runtime-hazard.invalid-cast.null-operand",
                    out var nullCondition) &&
                nullCondition is SymbolicConstantCondition { Value: true })
                return false;

            if (!SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                    castExpression.Expression,
                    castExpression,
                    semanticModel,
                    cancellationToken,
                    out var exactRuntimeType) &&
                TryCreateRuntimeReferenceInvalidCastTrigger(
                    castExpression.Expression,
                    targetType,
                    semanticModel,
                    cancellationToken,
                    out var irInvalidCastTrigger))
            {
                candidate = new RuntimeHazardCandidate(
                    castExpression,
                    SymbolicRuntimeHazardKind.InvalidCast,
                    irInvalidCastTrigger,
                    ExceptionTypes.InvalidCastException,
                    ExceptionCategories.DefiniteInvalidCast);
                return true;
            }

            if (exactRuntimeType != null)
            {
                if (SymbolicRuntimeTypeFacts.CanCastExactRuntimeTypeToReferenceType(
                        exactRuntimeType,
                        targetType,
                        semanticModel.Compilation))
                    return false;

                if (TryCreateExactRuntimeInvalidCastTrigger(
                        castExpression.Expression,
                        semanticModel,
                        cancellationToken,
                        out var exactInvalidCastTrigger))
                {
                    candidate = new RuntimeHazardCandidate(
                        castExpression,
                        SymbolicRuntimeHazardKind.InvalidCast,
                        exactInvalidCastTrigger,
                        ExceptionTypes.InvalidCastException,
                        ExceptionCategories.DefiniteInvalidCast);
                    return true;
                }
            }

        }

        var unsupportedTrigger = CreateUnsupportedExceptionPreconditionTrigger(
            castExpression.Expression,
            SymbolicExceptionPreconditionKind.InvalidCast,
            null,
            "ir.runtime-hazard.invalid-cast.unsupported");

        candidate = new RuntimeHazardCandidate(
            castExpression,
            SymbolicRuntimeHazardKind.InvalidCast,
            unsupportedTrigger,
            ExceptionTypes.InvalidCastException,
            ExceptionCategories.DefiniteInvalidCast);
        return true;
    }

    private static bool TryCreateNullDereferenceCandidate(
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

    private static bool TryCreateAwaitNullDereferenceCandidate(
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

    private static bool TryCreateInvalidCollectionCardinalityCandidate(
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

    private static bool TryGetCollectionCardinalityContract(
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

    private static bool IsKnownCardinalityCheckedCollection(INamedTypeSymbol type)
    {
        return type.ContainingNamespace.ToDisplayString() == "System.Collections.Generic" &&
               type.OriginalDefinition.MetadataName is "Queue`1" or "Stack`1" or "PriorityQueue`2";
    }

    private static bool TryCreateNullDereferenceCandidate(
        SyntaxNode site,
        ExpressionSyntax receiver,
        string category,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        var receiverType = GetExpressionType(receiver, semanticModel, cancellationToken);
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

    private static bool TryCreateArgumentNullCandidate(
        SyntaxNode site,
        ExpressionSyntax expression,
        string category,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        var expressionType = GetExpressionType(expression, semanticModel, cancellationToken);
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

    private static bool TryCreateDynamicInvocationNullBindingCandidate(
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

    private static bool TryCreateDynamicNullBindingCandidate(
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

    private static bool TryCreateNullableValueCandidate(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!IsNullableValueAccess(memberAccess, semanticModel, cancellationToken) ||
            !TryCreateNullableValueWithoutValueTrigger(
                memberAccess.Expression,
                semanticModel,
                cancellationToken,
                out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            memberAccess,
            SymbolicRuntimeHazardKind.NullableValueWithoutValue,
            trigger,
            ExceptionTypes.InvalidOperationException,
            ExceptionCategories.DefiniteNullableValueWithoutValue);
        return true;
    }

    private static bool TryCreateIndexOrRangeCandidate(
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

    private static bool TryCreateSlicingArgumentOutOfRangeCandidate(
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
        if (SymbolicIrLowerer.TryCreateSubsequenceInRangeCondition(
                sourceExpression,
                startExpression,
                countExpression,
                invocation,
                "ir.runtime-hazard.slicing.in-range",
                context,
                oneArgumentUpperBoundIsInclusive,
                out var inRangeCondition) &&
            TryEncodeIrExceptionPreconditionTrigger(
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

    private static bool TryCreateArrayGetValueIndexOutOfRangeCandidate(
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

    private static bool TryCreateNegativeArrayLengthCandidate(
        ArrayCreationExpressionSyntax arrayCreation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        var triggerCondition = default(SymbolicCondition);
        var subject = default(SymbolicTerm);
        var allTriggersAreExact = true;
        var hasTrigger = false;
        foreach (var lengthExpression in CSharpSyntaxFacts.GetExplicitArraySizeExpressions(arrayCreation))
        {
            if (!TryCreateNegativeLengthTrigger(
                    lengthExpression,
                    SymbolicExceptionPreconditionKind.NegativeLength,
                    "ir.runtime-hazard.array.negative-length",
                    semanticModel,
                    cancellationToken,
                    out var negativeLength))
                continue;

            hasTrigger = true;
            if (TryGetExceptionPrecondition(
                    negativeLength,
                    SymbolicExceptionPreconditionKind.NegativeLength,
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
            arrayCreation,
            SymbolicRuntimeHazardKind.NegativeArrayLength,
            CreateAggregateExceptionPreconditionTrigger(
                arrayCreation,
                SymbolicExceptionPreconditionKind.NegativeLength,
                subject,
                triggerCondition,
                allTriggersAreExact,
                "ir.runtime-hazard.array.negative-length.aggregate"),
            ExceptionTypes.OverflowException,
            ExceptionCategories.DefiniteNegativeArrayLength);
        return true;
    }

    private static bool TryCreateNegativeStackAllocLengthCandidate(
        StackAllocArrayCreationExpressionSyntax stackAllocCreation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        var triggerCondition = default(SymbolicCondition);
        var subject = default(SymbolicTerm);
        var allTriggersAreExact = true;
        var hasTrigger = false;
        foreach (var lengthExpression in GetStackAllocLengthExpressions(stackAllocCreation))
        {
            if (!TryCreateNegativeLengthTrigger(
                    lengthExpression,
                    SymbolicExceptionPreconditionKind.NegativeStackAllocLength,
                    "ir.runtime-hazard.stackalloc.negative-length",
                    semanticModel,
                    cancellationToken,
                    out var negativeLength))
                continue;

            hasTrigger = true;
            if (TryGetExceptionPrecondition(
                    negativeLength,
                    SymbolicExceptionPreconditionKind.NegativeStackAllocLength,
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
            stackAllocCreation,
            SymbolicRuntimeHazardKind.NegativeStackAllocLength,
            CreateAggregateExceptionPreconditionTrigger(
                stackAllocCreation,
                SymbolicExceptionPreconditionKind.NegativeStackAllocLength,
                subject,
                triggerCondition,
                allTriggersAreExact,
                "ir.runtime-hazard.stackalloc.negative-length.aggregate"),
            ExceptionTypes.OverflowException,
            ExceptionCategories.DefiniteNegativeStackAllocLength);
        return true;
    }

    private static RuntimeHazardTrigger CreateAggregateExceptionPreconditionTrigger(
        SyntaxNode site,
        SymbolicExceptionPreconditionKind kind,
        SymbolicTerm? subject,
        SymbolicCondition? triggerCondition,
        bool allTriggersAreExact,
        string provenance)
    {
        if (triggerCondition == null)
            return CreateUnsupportedExceptionPreconditionTrigger(
                site,
                kind,
                subject,
                provenance + ".unsupported");

        if (!allTriggersAreExact)
            return CreateUnsupportedExceptionPreconditionTrigger(
                site,
                kind,
                subject,
                provenance + ".unsupported");

        var precondition = new SymbolicFact(
            new SymbolicExceptionPreconditionAtom(kind, subject, triggerCondition),
            true,
            SymbolicFactConfidence.Exact,
            provenance,
            site.Span,
            null,
            provenance);
        if (!RuntimeHazardTrigger.TryCreate(precondition, out var trigger))
            throw new InvalidOperationException("Could not encode aggregate runtime-hazard precondition.");

        return trigger;
    }

    private static bool TryGetExceptionPrecondition(
        RuntimeHazardTrigger trigger,
        SymbolicExceptionPreconditionKind kind,
        out SymbolicExceptionPreconditionAtom precondition)
    {
        if (trigger.Precondition.Atom is SymbolicExceptionPreconditionAtom candidate &&
            candidate.Kind == kind)
        {
            precondition = candidate;
            return true;
        }

        precondition = null!;
        return false;
    }

    private static bool TryCreateSwitchExpressionNoMatchCandidate(
        SwitchExpressionSyntax switchExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        SymbolicCondition? anyArmSelected = null;
        foreach (var arm in switchExpression.Arms)
        {
            if (!SwitchPathConditionBuilder.TryCreateSwitchExpressionArmSymbolicCondition(
                    switchExpression.GoverningExpression,
                    arm,
                    semanticModel,
                    cancellationToken,
                    out var armCondition))
            {
                candidate = new RuntimeHazardCandidate(
                    switchExpression,
                    SymbolicRuntimeHazardKind.SwitchExpressionNoMatch,
                    CreateUnsupportedExceptionPreconditionTrigger(
                        switchExpression,
                        SymbolicExceptionPreconditionKind.SwitchExpressionNoMatch,
                        null,
                        "ir.runtime-hazard.switch-expression.no-match.unsupported"),
                    ExceptionTypes.SwitchExpressionException,
                    ExceptionCategories.DefiniteSwitchExpressionNoMatch);
                return true;
            }

            anyArmSelected = anyArmSelected == null
                ? armCondition
                : new SymbolicBinaryCondition(SymbolicConditionOperator.Or, anyArmSelected, armCondition);
        }

        if (anyArmSelected == null) return false;
        var triggerCondition = new SymbolicNotCondition(anyArmSelected);
        if (!TryEncodeIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.SwitchExpressionNoMatch,
                null,
                triggerCondition,
                switchExpression,
                "ir.runtime-hazard.switch-expression.no-match",
                out var trigger))
            return false;

        candidate = new RuntimeHazardCandidate(
            switchExpression,
            SymbolicRuntimeHazardKind.SwitchExpressionNoMatch,
            trigger,
            ExceptionTypes.SwitchExpressionException,
            ExceptionCategories.DefiniteSwitchExpressionNoMatch);
        return true;
    }

    private static RuntimeHazardTrigger CreateUnsupportedExceptionPreconditionTrigger(
        SyntaxNode site,
        SymbolicExceptionPreconditionKind kind,
        SymbolicTerm? subject,
        string provenance)
    {
        var unknownVariableName =
            "unsupported_typed_projection#" + site.SpanStart.ToString(CultureInfo.InvariantCulture) +
            "_" + site.Span.End.ToString(CultureInfo.InvariantCulture);
        var unsupportedTriggerFact = new SymbolicFact(
            new SymbolicTruthAtom(new SymbolicVariableTerm(unknownVariableName, SmtValueKind.Bool)),
            true,
            SymbolicFactConfidence.Exact,
            provenance + ".trigger",
            site.Span,
            null,
            provenance + ".trigger");
        var unsupportedPrecondition = new SymbolicFact(
            new SymbolicExceptionPreconditionAtom(
                kind,
                subject,
                new SymbolicFactCondition(unsupportedTriggerFact)),
            true,
            SymbolicFactConfidence.Unsupported,
            provenance,
            site.Span,
            null,
            provenance);
        if (!RuntimeHazardTrigger.TryCreate(unsupportedPrecondition, out var trigger))
            throw new InvalidOperationException("Could not encode unsupported runtime-hazard precondition.");

        return trigger;
    }

    private static bool TryCreateCheckedIntegralBinaryOverflowTrigger(
        BinaryExpressionSyntax binaryExpression,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (IsSignedDivisionOverflowOperator(smtOperator) &&
            TryCreateCheckedSignedDivisionOverflowTrigger(
                binaryExpression,
                binaryExpression.Left,
                binaryExpression.Right,
                minValue,
                "ir.runtime-hazard.checked-integral.signed-division-overflow",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        if (!IsSignedDivisionOverflowOperator(smtOperator) &&
            TryCreateCheckedIntegralOutOfRangeTrigger(
                binaryExpression,
                minValue,
                maxValue,
                "ir.runtime-hazard.checked-integral.binary-overflow",
                semanticModel,
                cancellationToken,
                out var irTrigger))
        {
            trigger = irTrigger;
            return true;
        }

        if (IsSignedDivisionOverflowOperator(smtOperator))
        {
            trigger = CreateUnsupportedExceptionPreconditionTrigger(
                binaryExpression,
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                "ir.runtime-hazard.checked-integral.signed-division-overflow.unsupported");
            return true;
        }

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            binaryExpression,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            null,
            "ir.runtime-hazard.checked-integral.binary-overflow.unsupported");
        return true;
    }

    private static bool IsSignedDivisionOverflowOperator(SmtIntegerBinaryOperator smtOperator)
    {
        return smtOperator is SmtIntegerBinaryOperator.Divide or SmtIntegerBinaryOperator.Remainder;
    }

    private static bool TryCreateCheckedIntegralUnaryOverflowTrigger(
        PrefixUnaryExpressionSyntax unaryExpression,
        long minValue,
        long maxValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (TryCreateCheckedEqualityOverflowTrigger(
                unaryExpression,
                unaryExpression.Operand,
                minValue,
                "ir.runtime-hazard.checked-integral.unary-minus-overflow",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            unaryExpression,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            null,
            "ir.runtime-hazard.checked-integral.unary-minus-overflow.unsupported");
        return true;
    }

    private static bool TryCreateCheckedIntegralUpdateOverflowTrigger(
        ExpressionSyntax site,
        ExpressionSyntax operand,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        var overflowingOperand = smtOperator == SmtIntegerBinaryOperator.Add ? maxValue : minValue;
        if (TryCreateCheckedEqualityOverflowTrigger(
                site,
                operand,
                overflowingOperand,
                smtOperator == SmtIntegerBinaryOperator.Add
                    ? "ir.runtime-hazard.checked-integral.increment-overflow"
                    : "ir.runtime-hazard.checked-integral.decrement-overflow",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        var unsupportedProvenance = smtOperator == SmtIntegerBinaryOperator.Add
            ? "ir.runtime-hazard.checked-integral.increment-overflow.unsupported"
            : "ir.runtime-hazard.checked-integral.decrement-overflow.unsupported";
        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            site,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            null,
            unsupportedProvenance);
        return true;
    }

    private static bool TryCreateCheckedIntegralCompoundAssignmentOverflowTrigger(
        AssignmentExpressionSyntax assignment,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (IsSignedDivisionOverflowOperator(smtOperator) &&
            TryCreateCheckedSignedDivisionOverflowTrigger(
                assignment,
                assignment.Left,
                assignment.Right,
                minValue,
                "ir.runtime-hazard.checked-integral.compound-signed-division-overflow",
                semanticModel,
                cancellationToken,
                out trigger))
            return true;

        if (IsSignedDivisionOverflowOperator(smtOperator))
        {
            trigger = CreateUnsupportedExceptionPreconditionTrigger(
                assignment,
                SymbolicExceptionPreconditionKind.CheckedOverflow,
                null,
                "ir.runtime-hazard.checked-integral.compound-signed-division-overflow.unsupported");
            return true;
        }

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            assignment,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            null,
            "ir.runtime-hazard.checked-integral.compound-assignment-overflow.unsupported");
        return true;
    }

    private static bool TryCreateCheckedExplicitNumericConversionOverflowTrigger(
        CastExpressionSyntax castExpression,
        long minValue,
        long maxValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (TryCreateCheckedIntegralOutOfRangeTrigger(
                castExpression.Expression,
                minValue,
                maxValue,
                "ir.runtime-hazard.checked-conversion.overflow",
                semanticModel,
                cancellationToken,
                out var irTrigger))
        {
            trigger = irTrigger;
            return true;
        }

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            castExpression,
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            null,
            "ir.runtime-hazard.checked-conversion.overflow.unsupported");
        return true;
    }

    private static bool TryGetCheckedIntegralBinaryOperator(
        BinaryExpressionSyntax binaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtIntegerBinaryOperator smtOperator,
        out long minValue,
        out long maxValue)
    {
        smtOperator = default;
        minValue = default;
        maxValue = default;

        if (!TryGetCheckedIntegralRange(binaryExpression, semanticModel, cancellationToken, out minValue,
                out maxValue) ||
            semanticModel.GetOperation(binaryExpression, cancellationToken) is not IBinaryOperation
            {
                OperatorMethod: null
            } operation)
            return false;

        switch (binaryExpression.Kind())
        {
            case SyntaxKind.AddExpression when operation.IsChecked:
                smtOperator = SmtIntegerBinaryOperator.Add;
                return true;
            case SyntaxKind.SubtractExpression when operation.IsChecked:
                smtOperator = SmtIntegerBinaryOperator.Subtract;
                return true;
            case SyntaxKind.MultiplyExpression when operation.IsChecked:
                smtOperator = SmtIntegerBinaryOperator.Multiply;
                return true;
            case SyntaxKind.DivideExpression when minValue < 0:
                smtOperator = SmtIntegerBinaryOperator.Divide;
                return true;
            case SyntaxKind.ModuloExpression when minValue < 0:
                smtOperator = SmtIntegerBinaryOperator.Remainder;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetCheckedIntegralUnaryOperator(
        PrefixUnaryExpressionSyntax unaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long minValue,
        out long maxValue)
    {
        minValue = default;
        maxValue = default;
        return unaryExpression.IsKind(SyntaxKind.UnaryMinusExpression) &&
               TryGetCheckedIntegralRange(unaryExpression, semanticModel, cancellationToken, out minValue,
                   out maxValue) &&
               semanticModel.GetOperation(unaryExpression, cancellationToken) is IUnaryOperation
               {
                   IsChecked: true,
                   OperatorMethod: null
               };
    }

    private static bool TryGetCheckedIntegralIncrementOrDecrementOperator(
        ExpressionSyntax updateExpression,
        ExpressionSyntax operand,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtIntegerBinaryOperator smtOperator,
        out long minValue,
        out long maxValue)
    {
        smtOperator = default;
        minValue = default;
        maxValue = default;

        if (semanticModel.GetOperation(updateExpression, cancellationToken) is not IIncrementOrDecrementOperation
            {
                IsChecked: true,
                OperatorMethod: null
            } operation)
            return false;

        var operandType = operation.Target.Type ?? semanticModel.GetTypeInfo(operand, cancellationToken).Type;
        if (!TryGetBoundedIntegralRange(operandType, out minValue, out maxValue)) return false;

        switch (updateExpression.Kind())
        {
            case SyntaxKind.PreIncrementExpression:
            case SyntaxKind.PostIncrementExpression:
                smtOperator = SmtIntegerBinaryOperator.Add;
                return true;
            case SyntaxKind.PreDecrementExpression:
            case SyntaxKind.PostDecrementExpression:
                smtOperator = SmtIntegerBinaryOperator.Subtract;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetCheckedIntegralCompoundAssignmentOperator(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtIntegerBinaryOperator smtOperator,
        out long minValue,
        out long maxValue)
    {
        smtOperator = default;
        minValue = default;
        maxValue = default;

        if (semanticModel.GetOperation(assignment, cancellationToken) is not ICompoundAssignmentOperation
            {
                OperatorMethod: null
            } operation)
            return false;

        var targetType = operation.Target.Type ?? semanticModel.GetTypeInfo(assignment.Left, cancellationToken).Type;
        if (!TryGetBoundedIntegralRange(targetType, out minValue, out maxValue)) return false;

        switch (assignment.Kind())
        {
            case SyntaxKind.AddAssignmentExpression when operation.IsChecked:
                smtOperator = SmtIntegerBinaryOperator.Add;
                return true;
            case SyntaxKind.SubtractAssignmentExpression when operation.IsChecked:
                smtOperator = SmtIntegerBinaryOperator.Subtract;
                return true;
            case SyntaxKind.MultiplyAssignmentExpression when operation.IsChecked:
                smtOperator = SmtIntegerBinaryOperator.Multiply;
                return true;
            case SyntaxKind.DivideAssignmentExpression when minValue < 0:
                smtOperator = SmtIntegerBinaryOperator.Divide;
                return true;
            case SyntaxKind.ModuloAssignmentExpression when minValue < 0:
                smtOperator = SmtIntegerBinaryOperator.Remainder;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetCheckedExplicitNumericConversionRange(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long minValue,
        out long maxValue)
    {
        minValue = default;
        maxValue = default;
        if (semanticModel.GetOperation(castExpression, cancellationToken) is not IConversionOperation
            {
                IsChecked: true,
                Conversion:
                {
                    Exists: true,
                    IsIdentity: false,
                    IsImplicit: false,
                    IsNumeric: true,
                    IsUserDefined: false,
                    MethodSymbol: null
                }
            } ||
            !TryGetCheckedNumericConversionRange(
                SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression, semanticModel, cancellationToken),
                out minValue,
                out maxValue))
            return false;

        if (TryGetCheckedNumericConversionRange(
                SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression.Expression, semanticModel,
                    cancellationToken),
                out var sourceMinValue,
                out var sourceMaxValue) &&
            sourceMinValue >= minValue &&
            sourceMaxValue <= maxValue)
            return false;

        return true;
    }

    private static bool TryGetCheckedIntegralRange(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out long minValue,
        out long maxValue)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return TryGetCheckedIntegralRange(typeInfo.ConvertedType ?? typeInfo.Type, out minValue, out maxValue);
    }

    private static bool TryGetCheckedIntegralRange(
        ITypeSymbol? typeSymbol,
        out long minValue,
        out long maxValue)
    {
        return SymbolicTypeFacts.TryGetCheckedIntegralRange(typeSymbol, out minValue, out maxValue);
    }

    private static bool TryGetBoundedIntegralRange(
        ITypeSymbol? typeSymbol,
        out long minValue,
        out long maxValue)
    {
        return SymbolicTypeFacts.TryGetBoundedIntegralRange(typeSymbol, out minValue, out maxValue);
    }

    private static bool TryGetCheckedNumericConversionRange(
        ITypeSymbol? typeSymbol,
        out long minValue,
        out long maxValue)
    {
        return SymbolicTypeFacts.TryGetCheckedNumericConversionRange(typeSymbol, out minValue, out maxValue);
    }

    private static bool TryGetArrayElementStoreType(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IArrayTypeSymbol arrayType)
    {
        arrayType = null!;
        var argumentCount = elementAccess.ArgumentList.Arguments.Count;
        if (argumentCount == 0 ||
            GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken) is not IArrayTypeSymbol
                candidate ||
            candidate.Rank != argumentCount)
            return false;

        arrayType = candidate;
        return true;
    }

    private static bool TryCreateArrayStoreMismatchTrigger(
        AssignmentExpressionSyntax assignment,
        ElementAccessExpressionSyntax elementAccess,
        IArrayTypeSymbol declaredArrayType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
        SymbolicTerm? subject = null;
        var receiverLowering = SymbolicSemanticPipeline.LowerTerm(elementAccess.Expression, context);
        if (receiverLowering is { IsExact: true, Value: { } receiver } &&
            receiver.Kind == SmtValueKind.Reference)
            subject = receiver;

        if (declaredArrayType.Rank != 1 ||
            elementAccess.ArgumentList.Arguments.Count != 1 ||
            subject == null ||
            !SymbolicIrLowerer.TryCreateBuiltInElementAccessInRangeCondition(
                elementAccess.Expression,
                elementAccess.ArgumentList.Arguments[0].Expression,
                elementAccess,
                "ir.runtime-hazard.array-type-mismatch.in-range",
                context,
                out var inRangeCondition))
        {
            trigger = CreateUnsupportedExceptionPreconditionTrigger(
                assignment,
                SymbolicExceptionPreconditionKind.ArrayTypeMismatch,
                subject,
                "ir.runtime-hazard.array-type-mismatch.unsupported");
            return true;
        }

        SymbolicCondition mismatchCondition;
        if (TryCreateReferenceNullCondition(
                assignment.Right,
                semanticModel,
                cancellationToken,
                "ir.runtime-hazard.array-type-mismatch.assigned-null",
                out var assignedNullCondition) &&
            assignedNullCondition is SymbolicConstantCondition { Value: true })
        {
            mismatchCondition = new SymbolicConstantCondition(false);
        }
        else if (SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                     elementAccess.Expression,
                     assignment,
                     semanticModel,
                     cancellationToken,
                     out var exactRuntimeArrayType) &&
                 exactRuntimeArrayType is IArrayTypeSymbol exactArrayType &&
                 exactArrayType.Rank == 1 &&
                 IsReferenceType(exactArrayType.ElementType) &&
                 SymbolicRuntimeTypeFacts.TryGetExactRuntimeType(
                     assignment.Right,
                     assignment,
                     semanticModel,
                     cancellationToken,
                     out var exactAssignedType))
        {
            mismatchCondition = new SymbolicConstantCondition(
                !SymbolicRuntimeTypeFacts.CanStoreExactRuntimeTypeInArrayElement(
                    exactAssignedType,
                    exactArrayType.ElementType,
                    semanticModel.Compilation));
        }
        else
        {
            trigger = CreateUnsupportedExceptionPreconditionTrigger(
                assignment,
                SymbolicExceptionPreconditionKind.ArrayTypeMismatch,
                subject,
                "ir.runtime-hazard.array-type-mismatch.unsupported");
            return true;
        }

        var receiverNotNull = SymbolicIrLowerer.CreateReferenceNullCondition(
            subject,
            false,
            elementAccess.Expression,
            "ir.runtime-hazard.array-type-mismatch.receiver-not-null");
        var triggerCondition = new SymbolicBinaryCondition(
            SymbolicConditionOperator.And,
            receiverNotNull,
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                inRangeCondition,
                mismatchCondition));
        if (TryEncodeIrExceptionPreconditionTrigger(
                SymbolicExceptionPreconditionKind.ArrayTypeMismatch,
                subject,
                triggerCondition,
                assignment,
                "ir.runtime-hazard.array-type-mismatch",
                out trigger))
            return true;

        trigger = CreateUnsupportedExceptionPreconditionTrigger(
            assignment,
            SymbolicExceptionPreconditionKind.ArrayTypeMismatch,
            subject,
            "ir.runtime-hazard.array-type-mismatch.unsupported");
        return true;
    }
    private static bool IsBuiltInSequenceElementAccess(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var argumentCount = elementAccess.ArgumentList.Arguments.Count;
        if (argumentCount == 0) return false;

        var receiverType = GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken);
        if (receiverType is IArrayTypeSymbol arrayType) return arrayType.Rank == argumentCount;

        return argumentCount == 1 &&
               (receiverType?.SpecialType == SpecialType.System_String ||
                IsBuiltInSpanType(receiverType));
    }

    private static bool TryGetIndexOrRangeHazardMetadata(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicRuntimeHazardKind kind,
        out string exceptionType,
        out string category)
    {
        kind = default;
        exceptionType = string.Empty;
        category = string.Empty;

        if (IsBuiltInSequenceElementAccess(elementAccess, semanticModel, cancellationToken))
        {
            var isRange = elementAccess.ArgumentList.Arguments.Count == 1 &&
                          IsBuiltInRangeAccessArgument(
                              elementAccess.ArgumentList.Arguments[0].Expression,
                              semanticModel,
                              cancellationToken);
            if (isRange)
            {
                kind = SymbolicRuntimeHazardKind.ArgumentOutOfRange;
                exceptionType = ExceptionTypes.ArgumentOutOfRangeException;
                category = ExceptionCategories.DefiniteRangeOutOfRange;
                return true;
            }

            kind = SymbolicRuntimeHazardKind.IndexOutOfRange;
            exceptionType = ExceptionTypes.IndexOutOfRangeException;
            category = ExceptionCategories.DefiniteIndexOutOfRange;
            return true;
        }

        if (IsCountBackedIntIndexerElementAccess(elementAccess, semanticModel, cancellationToken))
        {
            kind = SymbolicRuntimeHazardKind.ArgumentOutOfRange;
            exceptionType = ExceptionTypes.ArgumentOutOfRangeException;
            category = ExceptionCategories.DefiniteCountIndexOutOfRange;
            return true;
        }

        return false;
    }

    private static bool TryGetSlicingInvocationShape(
        IInvocationOperation invocationOperation,
        out ExpressionSyntax sourceExpression,
        out ExpressionSyntax startExpression,
        out ExpressionSyntax? countExpression,
        out bool oneArgumentUpperBoundIsInclusive,
        out string category)
    {
        sourceExpression = null!;
        startExpression = null!;
        countExpression = null;
        oneArgumentUpperBoundIsInclusive = true;
        category = string.Empty;

        var method = invocationOperation.TargetMethod;
        if (TryGetMemoryExtensionsViewSlicingShape(
                invocationOperation,
                method,
                out sourceExpression,
                out startExpression,
                out countExpression))
        {
            category = method.Name == "AsMemory"
                ? ExceptionCategories.DefiniteMemoryExtensionsAsMemoryOutOfRange
                : ExceptionCategories.DefiniteMemoryExtensionsAsSpanOutOfRange;
            return true;
        }

        if (method.IsStatic ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax instanceExpression ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpressionByOrdinal(invocationOperation, 0,
                out var firstArgument))
            return false;

        if (IsStringSubstringInvocation(method))
        {
            sourceExpression = instanceExpression;
            startExpression = firstArgument;
            oneArgumentUpperBoundIsInclusive = true;
            category = ExceptionCategories.DefiniteStringSubstringOutOfRange;
            return TryGetOptionalSecondIntArgument(invocationOperation, method, out countExpression);
        }

        if (IsStringRemoveInvocation(method))
        {
            sourceExpression = instanceExpression;
            startExpression = firstArgument;
            category = ExceptionCategories.DefiniteStringRemoveOutOfRange;
            if (!TryGetOptionalSecondIntArgument(invocationOperation, method, out countExpression)) return false;

            oneArgumentUpperBoundIsInclusive = countExpression != null;
            return true;
        }

        if (IsBuiltInSpanOrMemorySliceInvocation(method))
        {
            sourceExpression = instanceExpression;
            startExpression = firstArgument;
            oneArgumentUpperBoundIsInclusive = true;
            category = ExceptionCategories.DefiniteSliceOutOfRange;
            return TryGetOptionalSecondIntArgument(invocationOperation, method, out countExpression);
        }

        return false;
    }

    private static bool TryGetMemoryExtensionsViewSlicingShape(
        IInvocationOperation invocationOperation,
        IMethodSymbol method,
        out ExpressionSyntax sourceExpression,
        out ExpressionSyntax startExpression,
        out ExpressionSyntax? countExpression)
    {
        sourceExpression = null!;
        startExpression = null!;
        countExpression = null;

        if (!IsMemoryExtensionsViewInvocation(method)) return false;

        if (!TryGetMemoryExtensionsViewSourceExpression(invocationOperation, out sourceExpression)) return false;

        var intArguments = invocationOperation.Arguments
            .Where(static argument => argument.Parameter?.Type.SpecialType == SpecialType.System_Int32)
            .Select(static argument => argument.Value.Syntax)
            .OfType<ExpressionSyntax>()
            .ToArray();
        if (intArguments.Length is not (1 or 2)) return false;

        startExpression = intArguments[0];
        countExpression = intArguments.Length == 2 ? intArguments[1] : null;
        return true;
    }

    private static bool TryGetMemoryExtensionsViewSourceExpression(
        IInvocationOperation invocationOperation,
        out ExpressionSyntax sourceExpression)
    {
        if (invocationOperation.Instance?.Syntax is ExpressionSyntax instanceExpression &&
            IsMemoryExtensionsViewSourceType(invocationOperation.Instance.Type))
        {
            sourceExpression = instanceExpression;
            return true;
        }

        foreach (var argument in invocationOperation.Arguments)
            if ((argument.Parameter?.Ordinal == 0 ||
                 IsMemoryExtensionsViewSourceType(argument.Value.Type)) &&
                argument.Value.Syntax is ExpressionSyntax argumentExpression &&
                IsMemoryExtensionsViewSourceType(argument.Value.Type))
            {
                sourceExpression = argumentExpression;
                return true;
            }

        sourceExpression = null!;
        return false;
    }

    private static bool TryGetOptionalSecondIntArgument(
        IInvocationOperation invocationOperation,
        IMethodSymbol method,
        out ExpressionSyntax? secondArgument)
    {
        secondArgument = null;
        if (method.Parameters.Length == 1)
            return invocationOperation.Arguments.Length == 1 &&
                   method.Parameters[0].Type.SpecialType == SpecialType.System_Int32;

        if (method.Parameters.Length != 2 ||
            invocationOperation.Arguments.Length != 2 ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_Int32 ||
            method.Parameters[1].Type.SpecialType != SpecialType.System_Int32)
            return false;

        return SymbolicValueFacts.TryGetInvocationArgumentExpressionByOrdinal(invocationOperation, 1,
            out secondArgument);
    }

    private static bool IsStringSubstringInvocation(IMethodSymbol method)
    {
        return method.Name == "Substring" &&
               method.ContainingType?.SpecialType == SpecialType.System_String &&
               method.ReturnType.SpecialType == SpecialType.System_String &&
               (method.Parameters.Length == 1 || method.Parameters.Length == 2) &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
    }

    private static bool IsStringRemoveInvocation(IMethodSymbol method)
    {
        return method.Name == "Remove" &&
               method.ContainingType?.SpecialType == SpecialType.System_String &&
               method.ReturnType.SpecialType == SpecialType.System_String &&
               (method.Parameters.Length == 1 || method.Parameters.Length == 2) &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
    }

    private static bool IsBuiltInSpanOrMemorySliceInvocation(IMethodSymbol method)
    {
        return method.Name == "Slice" &&
               (method.Parameters.Length == 1 || method.Parameters.Length == 2) &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) &&
               IsBuiltInSpanOrMemoryType(method.ContainingType) &&
               IsBuiltInSpanOrMemoryType(method.ReturnType);
    }

    private static bool IsMemoryExtensionsViewInvocation(IMethodSymbol method)
    {
        return method.Name is "AsSpan" or "AsMemory" &&
               method.ContainingType?.OriginalDefinition.ToDisplayString() == "System.MemoryExtensions" &&
               IsBuiltInSpanOrMemoryType(method.ReturnType) &&
               method.Parameters.Count(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32) is
                   1 or 2 &&
               method.Parameters.Any(static parameter => IsMemoryExtensionsViewSourceType(parameter.Type));
    }

    private static bool IsMemoryExtensionsViewSourceType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.SpecialType == SpecialType.System_String ||
               typeSymbol is IArrayTypeSymbol;
    }

    private static bool IsArrayGetValueInvocation(IMethodSymbol method)
    {
        return method.Name == "GetValue" &&
               !method.IsStatic &&
               method.ContainingType?.SpecialType == SpecialType.System_Array &&
               method.ReturnType.SpecialType == SpecialType.System_Object &&
               method.Parameters.Length > 0 &&
               method.Parameters.All(static parameter => parameter.Type.SpecialType == SpecialType.System_Int32);
    }

    private static bool IsCountBackedIntIndexerElementAccess(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (elementAccess.ArgumentList.Arguments.Count != 1) return false;

        var argumentType = GetExpressionType(
            elementAccess.ArgumentList.Arguments[0].Expression,
            semanticModel,
            cancellationToken);
        if (argumentType?.SpecialType != SpecialType.System_Int32 &&
            !IsSystemIndexType(argumentType))
            return false;

        var receiverType = GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken);
        return SymbolicTypeFacts.HasInstanceInt32Member(receiverType, "Count") &&
               SymbolicTypeFacts.HasInt32Indexer(receiverType);
    }

    private static bool IsBuiltInRangeAccessArgument(
        ExpressionSyntax argumentExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        argumentExpression = UnwrapExpression(argumentExpression);
        if (argumentExpression is RangeExpressionSyntax) return true;

        var typeInfo = semanticModel.GetTypeInfo(argumentExpression, cancellationToken);
        return IsSystemRangeType(typeInfo.ConvertedType ?? typeInfo.Type);
    }

    private static bool IsBuiltInSpanType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsBuiltInSpanType(typeSymbol);
    }

    private static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(typeSymbol);
    }

    private static bool IsSystemRangeType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsSystemRangeType(typeSymbol);
    }

    private static bool IsSystemIndexType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsSystemIndexType(typeSymbol);
    }

    private static bool IsNullableValueAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicTypeFacts.IsNullableValueAccess(memberAccess, semanticModel, cancellationToken);
    }

    private static bool IsNullableValueCastShape(
        CastExpressionSyntax castExpression,
        ITypeSymbol? targetType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsNonNullableValueType(targetType) &&
               TryGetNullableUnderlyingType(
                   SymbolicRuntimeTypeFacts.GetNaturalExpressionType(castExpression.Expression, semanticModel,
                       cancellationToken), out _);
    }

    private static bool IsUnboxingCastShape(
        CastExpressionSyntax castExpression,
        ITypeSymbol? targetType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var operandType = GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
        return IsNonNullableValueType(targetType) &&
               IsReferenceType(operandType);
    }

    private static bool TryGetConversionOperation(
        CastExpressionSyntax castExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IConversionOperation conversionOperation)
    {
        if (semanticModel.GetOperation(castExpression, cancellationToken) is IConversionOperation operation)
        {
            conversionOperation = operation;
            return true;
        }

        conversionOperation = null!;
        return false;
    }

    private static bool IsThrowingDivideByZeroType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsThrowingDivideByZeroType(typeSymbol);
    }

    private static bool IsIntegralOrDecimalZero(object? value)
    {
        return SymbolicValueFacts.IsIntegralOrDecimalZero(value);
    }

    private static bool IsReferenceType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsReferenceType(typeSymbol);
    }

    private static bool IsReferenceLikeType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsReferenceLikeType(typeSymbol);
    }

    private static bool IsDynamicExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicTypeFacts.IsDynamicExpression(
            expression,
            semanticModel,
            cancellationToken,
            UnwrapDynamicExpression);
    }

    private static bool IsNonNullableValueType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol is { IsValueType: true, TypeKind: not TypeKind.TypeParameter } &&
               typeSymbol.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T;
    }

    private static bool TryGetNullableUnderlyingType(ITypeSymbol? typeSymbol, out ITypeSymbol underlyingType)
    {
        return SymbolicTypeFacts.TryGetNullableUnderlyingType(typeSymbol, out underlyingType);
    }

    private static ITypeSymbol? GetExpressionType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return typeInfo.ConvertedType ?? typeInfo.Type;
    }

    private static IEnumerable<ExpressionSyntax> GetStackAllocLengthExpressions(
        StackAllocArrayCreationExpressionSyntax stackAllocCreation)
    {
        if (stackAllocCreation.Type is not ArrayTypeSyntax arrayType) yield break;

        foreach (var rankSpecifier in arrayType.RankSpecifiers)
            foreach (var size in rankSpecifier.Sizes)
                if (!size.IsKind(SyntaxKind.OmittedArraySizeExpression))
                    yield return size;
    }

    private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
    {
        while (true)
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax castExpression:
                    expression = castExpression.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfixUnary
                    when postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfixUnary.Operand;
                    continue;
                default:
                    return expression;
            }
    }

    private static ExpressionSyntax UnwrapDynamicExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }

    private readonly struct RuntimeHazardCandidate
    {
        public RuntimeHazardCandidate(
            SyntaxNode site,
            SymbolicRuntimeHazardKind kind,
            RuntimeHazardTrigger trigger,
            string exceptionType,
            string category)
        {
            Site = site;
            Kind = kind;
            TriggerPrecondition = trigger.Precondition;
            ExceptionType = exceptionType;
            Category = category;
        }

        public SyntaxNode Site { get; }

        public SymbolicRuntimeHazardKind Kind { get; }

        public SymbolicFact TriggerPrecondition { get; }

        public string ExceptionType { get; }

        public string Category { get; }
    }

    private readonly struct RuntimeHazardTrigger
    {
        private RuntimeHazardTrigger(SymbolicFact precondition)
        {
            Precondition = precondition ?? throw new ArgumentNullException(nameof(precondition));
        }

        internal static bool TryCreate(SymbolicFact precondition, out RuntimeHazardTrigger trigger)
        {
            if (precondition == null)
            {
                trigger = default;
                return false;
            }

            trigger = new RuntimeHazardTrigger(precondition);
            return true;
        }

        internal SymbolicFact Precondition { get; }
    }
}
