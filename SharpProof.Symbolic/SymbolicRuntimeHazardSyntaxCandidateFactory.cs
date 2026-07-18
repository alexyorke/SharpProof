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
using static SharpProof.Symbolic.SymbolicRuntimeHazardSyntaxFacts;

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
        if (!SymbolicOperationLowerer.TryLowerIndexConstructionBoundsHazard(
            expression,
            new SymbolicLoweringContext(semanticModel, cancellationToken),
            out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(expression, hazard);
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
            !SymbolicTypeFacts.TryGetBoundedIntegralRange(operation.TargetMethod.ReturnType, out var minValue, out _) ||
            minValue >= 0 ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 0, out var operand) ||
            !SymbolicOperationLowerer.TryLowerMathAbsOverflowHazard(
                invocation,
                operand,
                minValue,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(invocation, hazard);
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

        if (!SymbolicOperationLowerer.TryLowerMathClampBoundsHazard(
                invocation,
                minExpression,
                maxExpression,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(invocation, hazard);
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
        var exceptionTypeName =
            exceptionType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty) ??
            (isRethrow ? ExceptionTypes.Unknown : ExceptionTypes.Exception);
        foreach (var hazard in SymbolicOperationLowerer.LowerThrowHazards(
                     throwNode,
                     isRethrow,
                     exceptionTypeName,
                     new SymbolicLoweringContext(semanticModel, cancellationToken)))
            yield return new RuntimeHazardCandidate(throwNode, hazard);
    }

    internal static bool TryCreateSwitchExpressionNoMatchCandidate(
        SwitchExpressionSyntax switchExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!SymbolicOperationLowerer.TryLowerSwitchNoMatchHazard(
                switchExpression,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(switchExpression, hazard);
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

        if (!SymbolicOperationLowerer.TryLowerArrayStoreMismatchHazard(
            assignment,
            elementAccess,
            arrayType,
            new SymbolicLoweringContext(semanticModel, cancellationToken),
            out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(assignment, hazard);
        return true;
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

        return TryCreateOperationReferenceNullCandidate(
            castExpression,
            castExpression.Expression,
            SymbolicRuntimeHazardKind.UnboxNull,
            SymbolicExceptionPreconditionKind.UnboxNull,
            ExceptionTypes.NullReferenceException,
            ExceptionCategories.DefiniteUnboxNull,
            "ir.runtime-hazard.unbox-null",
            semanticModel,
            cancellationToken,
            suppressDefinitelyNotNull: false,
            out candidate);
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
            !IsNullableValueCastShape(castExpression, targetType, semanticModel, cancellationToken))
            return false;

        return TryCreateOperationNullableValueCandidate(
            castExpression,
            castExpression.Expression,
            ExceptionTypes.InvalidOperationException,
            ExceptionCategories.DefiniteNullableValueWithoutValue,
            semanticModel,
            cancellationToken,
            out candidate);
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

        if (!SymbolicOperationLowerer.TryLowerInvalidCastHazard(
                castExpression,
                targetType,
                isUnboxing,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(castExpression, hazard);
        return true;
    }

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
            !SymbolicOperationLowerer.TryLowerInvalidCollectionCardinalityHazard(
                receiver,
                relation,
                triggeringCount,
                ExceptionCategories.DefiniteInvalidCollectionCardinality,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(invocation, hazard);
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
        if (IsDynamicExpression(receiver, semanticModel, cancellationToken) || !IsReferenceType(receiverType))
            return false;

        return TryCreateOperationReferenceNullCandidate(
            site,
            receiver,
            SymbolicRuntimeHazardKind.NullDereference,
            SymbolicExceptionPreconditionKind.NullDereference,
            ExceptionTypes.NullReferenceException,
            category,
            "ir.runtime-hazard.null-dereference",
            semanticModel,
            cancellationToken,
            suppressDefinitelyNotNull: true,
            out candidate);
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
        if (IsDynamicExpression(expression, semanticModel, cancellationToken) || !IsReferenceType(expressionType))
            return false;

        return TryCreateOperationReferenceNullCandidate(
            site,
            expression,
            SymbolicRuntimeHazardKind.ArgumentNull,
            SymbolicExceptionPreconditionKind.ArgumentNull,
            ExceptionTypes.ArgumentNullException,
            category,
            "ir.runtime-hazard.argument-null",
            semanticModel,
            cancellationToken,
            suppressDefinitelyNotNull: false,
            out candidate);
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
        if (!IsDynamicExpression(receiver, semanticModel, cancellationToken))
            return false;

        return TryCreateOperationReferenceNullCandidate(
            site,
            receiver,
            SymbolicRuntimeHazardKind.DynamicNullBinding,
            SymbolicExceptionPreconditionKind.DynamicNullBinding,
            SymbolicDynamicNullBindingFacts.RuntimeBinderExceptionType,
            category,
            "ir.runtime-hazard.dynamic-null-binding",
            semanticModel,
            cancellationToken,
            suppressDefinitelyNotNull: false,
            out candidate);
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

        if (HasLaterLoopAssignmentOfMissingNullableValue(
                memberAccess.Expression,
                memberAccess,
                semanticModel,
                cancellationToken))
        {
            candidate = new RuntimeHazardCandidate(
                memberAccess,
                SymbolicOperationLowerer.LowerLoopCarriedNullableValueHazard(memberAccess));
            return true;
        }

        return TryCreateOperationNullableValueCandidate(
            memberAccess,
            memberAccess.Expression,
            ExceptionTypes.InvalidOperationException,
            ExceptionCategories.DefiniteNullableValueWithoutValue,
            semanticModel,
            cancellationToken,
            out candidate);
    }

    private static bool TryCreateOperationReferenceNullCandidate(
        SyntaxNode site,
        ExpressionSyntax subject,
        SymbolicRuntimeHazardKind hazardKind,
        SymbolicExceptionPreconditionKind preconditionKind,
        string exceptionType,
        string category,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool suppressDefinitelyNotNull,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!SymbolicOperationLowerer.TryLowerReferenceNullHazard(
                subject,
                hazardKind,
                preconditionKind,
                exceptionType,
                category,
                provenance,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                suppressDefinitelyNotNull,
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(site, hazard);
        return true;
    }

    private static bool TryCreateOperationNullableValueCandidate(
        SyntaxNode site,
        ExpressionSyntax subject,
        string exceptionType,
        string category,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        if (!SymbolicOperationLowerer.TryLowerNullableValueHazard(
                subject,
                exceptionType,
                category,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(site, hazard);
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
            !SymbolicOperationLowerer.TryLowerElementAccessBoundsHazard(
                elementAccess,
                kind,
                exceptionType,
                category,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(elementAccess, hazard);
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

        if (!SymbolicOperationLowerer.TryLowerSlicingBoundsHazard(
                invocation,
                sourceExpression,
                startExpression,
                countExpression,
                oneArgumentUpperBoundIsInclusive,
                category,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(invocation, hazard);
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

        if (!SymbolicOperationLowerer.TryLowerArrayGetValueBoundsHazard(
                invocation,
                invocationOperation,
                receiverExpression,
                arrayType,
                ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(invocation, hazard);
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
        if (!SymbolicOperationLowerer.TryLowerNegativeLengthHazard(
            site,
                lengthExpressions,
                preconditionKind,
                hazardKind,
                provenance,
                category,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(site, hazard);
        return true;
    }
}
