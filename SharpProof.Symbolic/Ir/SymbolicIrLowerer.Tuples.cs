using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    private static bool TryLowerTupleEqualityCondition(
        BinaryExpressionSyntax binaryExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (TupleComparisonUsesUserDefinedElementOperator(binaryExpression.Left, context) ||
            TupleComparisonUsesUserDefinedElementOperator(binaryExpression.Right, context) ||
            !TryLowerTupleElementTerms(binaryExpression.Left, context, out var leftElements) ||
            !TryLowerTupleElementTerms(binaryExpression.Right, context, out var rightElements) ||
            leftElements.Length == 0 ||
            leftElements.Length != rightElements.Length)
            return false;

        SymbolicCondition? equality = null;
        for (var index = 0; index < leftElements.Length; index++)
        {
            if (!CanCompareTerms(leftElements[index], rightElements[index], SymbolicRelationOperator.Equal))
                return false;

            var elementEquality = CreateRelationCondition(
                SymbolicRelationOperator.Equal,
                leftElements[index],
                rightElements[index],
                binaryExpression,
                "ir.tuple.equality.element");
            equality = equality == null
                ? elementEquality
                : new SymbolicBinaryCondition(SymbolicConditionOperator.And, equality, elementEquality);
        }

        condition = binaryExpression.IsKind(SyntaxKind.EqualsExpression)
            ? equality!
            : new SymbolicNotCondition(equality!);
        return true;
    }

    private static bool TupleComparisonUsesUserDefinedElementOperator(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        if ((typeInfo.ConvertedType ?? typeInfo.Type) is not INamedTypeSymbol { IsTupleType: true } tupleType)
            return false;

        return tupleType.TupleElements.Any(static element =>
        {
            var type = element.Type;
            return type.GetMembers("op_Equality").OfType<IMethodSymbol>().Any() ||
                   type.GetMembers("op_Inequality").OfType<IMethodSymbol>().Any();
        });
    }

    internal static bool TryLowerTupleElementMemberTerm(
        MemberAccessExpressionSyntax memberAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IFieldSymbol
                field ||
            !TryGetTupleElementStorageName(field, out var storageName) ||
            !TryGetValueKind(field.Type, out var kind))
            return false;

        if (UnwrapExpression(memberAccess.Expression) is TupleExpressionSyntax tupleExpression &&
            TryGetTupleStoragePosition(storageName, out var position) &&
            position < tupleExpression.Arguments.Count &&
            TryLowerTerm(tupleExpression.Arguments[position].Expression, context, out var tupleElement) &&
            tupleElement.Kind == kind)
        {
            term = tupleElement;
            return true;
        }

        if (!TryGetStableVariableSymbol(memberAccess.Expression, context, out var tupleSymbol)) return false;

        term = CreateTupleStorageTerm(tupleSymbol, storageName, kind, context);
        return true;
    }

    private static bool TryGetTupleStoragePosition(string storageName, out int position)
    {
        position = -1;
        return storageName.StartsWith("Item", StringComparison.Ordinal) &&
               int.TryParse(
                   storageName.Substring("Item".Length),
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var oneBased) &&
               oneBased > 0 &&
               (position = oneBased - 1) >= 0;
    }

    private static bool TryLowerTupleElementTerms(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out ImmutableArray<SymbolicTerm> terms)
    {
        expression = UnwrapExpression(expression);
        if (expression is TupleExpressionSyntax tupleExpression)
        {
            var tupleBuilder = ImmutableArray.CreateBuilder<SymbolicTerm>(tupleExpression.Arguments.Count);
            foreach (var argument in tupleExpression.Arguments)
            {
                if (!TryLowerTerm(argument.Expression, context, out var element))
                {
                    terms = ImmutableArray<SymbolicTerm>.Empty;
                    return false;
                }

                tupleBuilder.Add(element);
            }

            terms = tupleBuilder.MoveToImmutable();
            return terms.Length != 0;
        }

        terms = ImmutableArray<SymbolicTerm>.Empty;
        if (!TryGetStableVariableSymbol(expression, context, out var symbol) ||
            context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type is not INamedTypeSymbol
            {
                IsTupleType: true
            } tupleType ||
            tupleType.TupleElements.Length == 0)
            return false;

        var builder = ImmutableArray.CreateBuilder<SymbolicTerm>(tupleType.TupleElements.Length);
        foreach (var element in tupleType.TupleElements)
        {
            var field = element.CorrespondingTupleField ?? element;
            if (!TryGetTupleElementStorageName(field, out var storageName) ||
                !TryGetValueKind(field.Type, out var kind))
                return false;

            builder.Add(CreateTupleStorageTerm(symbol, storageName, kind, context));
        }

        terms = builder.ToImmutable();
        return true;
    }

    internal static bool TryGetTupleElementStorageName(IFieldSymbol field, out string storageName)
    {
        var storageField = field.CorrespondingTupleField ?? field;
        storageName = storageField.Name;
        return storageName.StartsWith("Item", StringComparison.Ordinal);
    }

    private static SymbolicTerm CreateTupleStorageTerm(
        ISymbol tupleSymbol,
        string storageName,
        SmtValueKind kind,
        SymbolicLoweringContext context)
    {
        return new SymbolicVariableTerm(context.GetVariableName(tupleSymbol) + "." + storageName, kind);
    }
}
