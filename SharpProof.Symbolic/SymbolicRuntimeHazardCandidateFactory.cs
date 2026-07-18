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
using static SharpProof.Symbolic.SymbolicRuntimeHazardSyntaxCandidateFactory;
using static SharpProof.Symbolic.SymbolicRuntimeHazardSyntaxFacts;

namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeHazardCandidateFactory
{
    internal static IEnumerable<RuntimeHazardCandidate> EnumerateCandidates(
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
                         !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate)))
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
                foreach (var throwCandidate in CreateThrowCandidates(throwStatement, semanticModel, cancellationToken))
                    yield return throwCandidate;
                break;
            case ThrowExpressionSyntax throwExpression:
                foreach (var throwCandidate in CreateThrowCandidates(throwExpression, semanticModel, cancellationToken))
                    yield return throwCandidate;
                break;
            case BinaryExpressionSyntax binaryExpression:
                if (TryCreateDivideByZeroCandidate(binaryExpression, semanticModel, cancellationToken,
                        out var divideCandidate)) yield return divideCandidate;

                if (TryCreateCheckedOverflowCandidate(binaryExpression, semanticModel, cancellationToken,
                        out var binaryOverflowCandidate)) yield return binaryOverflowCandidate;

                break;
            case PrefixUnaryExpressionSyntax prefixUnaryExpression:
                if (TryCreateCheckedOverflowCandidate(prefixUnaryExpression, semanticModel, cancellationToken,
                        out var unaryOverflowCandidate)) yield return unaryOverflowCandidate;

                if (TryCreateIndexConstructionArgumentOutOfRangeCandidate(
                        prefixUnaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var prefixIndexCandidate))
                    yield return prefixIndexCandidate;

                break;
            case PostfixUnaryExpressionSyntax postfixUnaryExpression:
                if (TryCreateCheckedOverflowCandidate(postfixUnaryExpression, semanticModel, cancellationToken,
                        out var postfixOverflowCandidate)) yield return postfixOverflowCandidate;

                break;
            case CastExpressionSyntax castExpression:
                if (TryCreateCheckedOverflowCandidate(castExpression, semanticModel,
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
                if (TryCreateDivideByZeroCandidate(assignment, semanticModel, cancellationToken,
                        out var compoundDivideCandidate)) yield return compoundDivideCandidate;

                if (TryCreateDeconstructionNullReceiverCandidate(assignment, semanticModel, cancellationToken,
                        out var deconstructionNullCandidate)) yield return deconstructionNullCandidate;

                if (TryCreateArrayTypeMismatchCandidate(assignment, semanticModel, cancellationToken,
                        out var arrayTypeMismatchCandidate)) yield return arrayTypeMismatchCandidate;

                if (TryCreateCheckedOverflowCandidate(assignment, semanticModel,
                        cancellationToken, out var compoundOverflowCandidate)) yield return compoundOverflowCandidate;

                break;
            case ArrayCreationExpressionSyntax arrayCreation:
                if (TryCreateNegativeArrayLengthCandidate(arrayCreation, semanticModel, cancellationToken,
                        out var negativeLengthCandidate)) yield return negativeLengthCandidate;

                break;
            case ObjectCreationExpressionSyntax objectCreation:
                if (TryCreateIndexConstructionArgumentOutOfRangeCandidate(
                        objectCreation,
                        semanticModel,
                        cancellationToken,
                        out var objectIndexCandidate))
                    yield return objectIndexCandidate;

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
                if (TryCreateIndexConstructionArgumentOutOfRangeCandidate(
                        invocation,
                        semanticModel,
                        cancellationToken,
                        out var invocationIndexCandidate))
                    yield return invocationIndexCandidate;

                if (TryCreateMathAbsOverflowCandidate(invocation, semanticModel, cancellationToken,
                        out var mathAbsOverflowCandidate))
                    yield return mathAbsOverflowCandidate;

                if (TryCreateMathClampBoundsCandidate(invocation, semanticModel, cancellationToken,
                        out var mathClampBoundsCandidate))
                    yield return mathClampBoundsCandidate;

                if (TryGetRegexRequiredInputExpression(invocation, semanticModel, cancellationToken, out var regexInput) &&
                    TryCreateArgumentNullCandidate(
                        invocation,
                        regexInput,
                        ExceptionCategories.DefiniteRegexNullInput,
                        semanticModel,
                        cancellationToken,
                        out var regexNullCandidate))
                    yield return regexNullCandidate;

                if (SymbolicOperationLowerer.TryLowerKnownArgumentGuardHazard(
                        invocation,
                        new SymbolicLoweringContext(semanticModel, cancellationToken),
                        out var guardHazard))
                    yield return new RuntimeHazardCandidate(invocation, guardHazard);

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

    private static bool TryCreateDivideByZeroCandidate(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        return TryCreateOperationHazard(
            site,
            semanticModel,
            cancellationToken,
            SymbolicOperationLowerer.TryLowerDivideByZeroHazard,
            out candidate);
    }

    private static bool TryCreateCheckedOverflowCandidate(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardCandidate candidate)
    {
        return TryCreateOperationHazard(
            site,
            semanticModel,
            cancellationToken,
            SymbolicOperationLowerer.TryLowerCheckedOverflowHazard,
            out candidate);
    }

    private static bool TryCreateOperationHazard(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        TryLowerOperationHazard lower,
        out RuntimeHazardCandidate candidate)
    {
        candidate = default;
        var operation = semanticModel.GetOperation(site, cancellationToken);
        if (operation == null ||
            !lower(operation, new SymbolicLoweringContext(semanticModel, cancellationToken), out var hazard))
            return false;

        candidate = new RuntimeHazardCandidate(site, hazard);
        return true;
    }

    private delegate bool TryLowerOperationHazard(
        IOperation operation,
        SymbolicLoweringContext context,
        out SymbolicHazardOperation hazard);
}
