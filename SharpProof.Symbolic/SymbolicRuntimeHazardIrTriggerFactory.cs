using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

using static SharpProof.Symbolic.SymbolicRuntimeHazardSyntaxFacts;
using static SharpProof.Symbolic.SymbolicRuntimeHazardTriggerFactory;
namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeHazardIrTriggerFactory
{
    internal static bool TryCreateDirectThrowTrigger(
        SyntaxNode throwNode,
        out RuntimeHazardTrigger trigger)
    {
        var precondition = SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(
                SymbolicExceptionPreconditionKind.DirectThrow,
                null,
                new SymbolicConstantCondition(true)),
            throwNode,
            "ir.runtime-hazard.direct-throw");

        return RuntimeHazardTrigger.TryCreate(precondition, out trigger);
    }

    internal static bool TryCreateCheckedEqualityOverflowTrigger(
        SyntaxNode site,
        ExpressionSyntax expression,
        long overflowingValue,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (!TryLowerExactIntegerTerm(expression, semanticModel, cancellationToken, out var value))
            return false;

        var overflowCondition = CreateExactIntegerRelationCondition(
            value, SymbolicRelationOperator.Equal, overflowingValue, expression, provenance + ".operand");

        return TryCreateIrExceptionPreconditionTrigger(
            SymbolicExceptionPreconditionKind.CheckedOverflow,
            value,
            overflowCondition,
            site,
            provenance,
            out trigger);
    }

    internal static bool TryCreateIrRelationalExceptionPreconditionTrigger(
        SymbolicExceptionPreconditionKind kind,
        ExpressionSyntax subjectExpression,
        SymbolicRelationOperator relation,
        SymbolicTerm triggeringValue,
        string provenance,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RuntimeHazardTrigger trigger)
    {
        trigger = default;
        if (!TryLowerExactTerm(
                subjectExpression,
                triggeringValue.Kind,
                semanticModel,
                cancellationToken,
                out var subject))
            return false;

        var triggerCondition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                relation,
                subject,
                triggeringValue),
            subjectExpression,
            provenance + ".trigger"));
        return TryCreateIrExceptionPreconditionTrigger(
            kind,
            subject,
            triggerCondition,
            subjectExpression,
            provenance,
            out trigger);
    }

    internal static bool TryCreateIrExceptionPreconditionTrigger(
        SymbolicExceptionPreconditionKind kind,
        SymbolicTerm? subject,
        SymbolicCondition triggerCondition,
        SyntaxNode site,
        string provenance,
        out RuntimeHazardTrigger trigger)
    {
        var precondition = SymbolicFact.Exact(
            new SymbolicExceptionPreconditionAtom(kind, subject, triggerCondition),
            site,
            provenance);

        return RuntimeHazardTrigger.TryCreate(precondition, out trigger);
    }

    internal static bool TryCreateOptionalReferenceSubject(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm? subject)
    {
        return TryLowerOptionalReference(
            expression,
            semanticModel,
            cancellationToken,
            out _,
            out subject,
            out _);
    }

    internal static bool TryCreateReferenceNullCondition(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string provenance,
        out SymbolicCondition condition)
    {
        if (!TryLowerOptionalReference(
                expression,
                semanticModel,
                cancellationToken,
                out var normalizedExpression,
                out var term,
                out var isNull))
        {
            condition = null!;
            return false;
        }

        if (isNull)
        {
            condition = new SymbolicConstantCondition(true);
            return true;
        }

        condition = SymbolicIrLowerer.CreateReferenceNullCondition(
            term!,
            true,
            normalizedExpression,
            provenance);
        return true;
    }

    internal static bool TryLowerOptionalReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax normalizedExpression,
        out SymbolicTerm? term,
        out bool isNull)
    {
        normalizedExpression = UnwrapExpression(expression);
        isNull = normalizedExpression.IsKind(SyntaxKind.NullLiteralExpression) ||
                 (normalizedExpression is DefaultExpressionSyntax defaultExpression &&
                  IsReferenceLikeType(CSharpSyntaxFacts.GetExpressionType(defaultExpression, semanticModel, cancellationToken)));
        if (isNull)
        {
            term = null;
            return true;
        }

        if (TryLowerExactTerm(
                normalizedExpression,
                SmtValueKind.Reference,
                semanticModel,
                cancellationToken,
                out var exactTerm))
        {
            term = exactTerm;
            return true;
        }

        term = null;
        return false;
    }

    internal static bool TryLowerExactTerm(
        ExpressionSyntax expression,
        SmtValueKind expectedKind,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm term)
    {
        return TryLowerExactTerm(
            expression,
            expectedKind,
            new SymbolicLoweringContext(semanticModel, cancellationToken),
            out term);
    }

    internal static bool TryLowerExactIntegerTerm(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicTerm term)
    {
        return TryLowerExactTerm(
            expression,
            SmtValueKind.Int,
            semanticModel,
            cancellationToken,
            out term);
    }

    internal static SymbolicFactCondition CreateExactIntegerRelationCondition(
        SymbolicTerm value,
        SymbolicRelationOperator relation,
        long constant,
        SyntaxNode source,
        string provenance)
    {
        return new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                relation,
                value,
                new SymbolicIntegerConstantTerm(constant)),
            source,
            provenance));
    }

    internal static bool TryLowerExactTerm(
        ExpressionSyntax expression,
        SmtValueKind expectedKind,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var lowering = SymbolicSemanticPipeline.LowerTerm(expression, context);
        if (lowering is { IsExact: true, Value: { } value } && value.Kind == expectedKind)
        {
            term = value;
            return true;
        }

        term = null!;
        return false;
    }
}
