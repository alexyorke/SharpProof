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

}
