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

namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeHazardTriggerFactory
{
    internal static RuntimeHazardTrigger CreateUnsupportedExceptionPreconditionTrigger(
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

    internal static bool TryGetArrayElementStoreType(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IArrayTypeSymbol arrayType)
    {
        arrayType = null!;
        var argumentCount = elementAccess.ArgumentList.Arguments.Count;
        if (argumentCount == 0 ||
            CSharpSyntaxFacts.GetExpressionType(elementAccess.Expression, semanticModel, cancellationToken) is not IArrayTypeSymbol
                candidate ||
            candidate.Rank != argumentCount)
            return false;

        arrayType = candidate;
        return true;
    }

}
