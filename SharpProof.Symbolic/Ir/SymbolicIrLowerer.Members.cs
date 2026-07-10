using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    private static bool TryGetInstanceMemberSymbol(
        SyntaxNode syntax,
        SymbolicLoweringContext context,
        out ISymbol memberSymbol)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(syntax, context.CancellationToken).Symbol;
        switch (symbol)
        {
            case IPropertySymbol { IsStatic: false } property:
                memberSymbol = property;
                return true;
            case IFieldSymbol { IsStatic: false } field:
                memberSymbol = field;
                return true;
        }

        switch (context.SemanticModel.GetOperation(syntax, context.CancellationToken))
        {
            case IPropertyReferenceOperation { Property.IsStatic: false } propertyReference:
                memberSymbol = propertyReference.Property;
                return true;
            case IFieldReferenceOperation { Field.IsStatic: false } fieldReference:
                memberSymbol = fieldReference.Field;
                return true;
        }

        memberSymbol = null!;
        return false;
    }

    private static bool TryLowerImplicitThisMemberTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (expression is not IdentifierNameSyntax ||
            !TryGetInstanceMemberSymbol(expression, context, out var memberSymbol) ||
            !TryGetSymbolType(memberSymbol, out var memberType) ||
            !TryGetValueKind(memberType, out var memberKind))
            return false;

        term = new SymbolicMemberTerm(
            new SymbolicVariableTerm("this", SmtValueKind.Reference),
            memberSymbol.Name,
            memberKind);
        return true;
    }

    private static bool TryLowerMemberTerm(
        MemberAccessExpressionSyntax memberAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;

        var memberName = memberAccess.Name.Identifier.ValueText;
        if (TryLowerKnownStaticValueMember(memberAccess, context, out term)) return true;

        var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (TryLowerTupleElementMemberTerm(memberAccess, context, out term)) return true;

        if (string.Equals(memberName, nameof(Array.Rank), StringComparison.Ordinal) &&
            receiverType is IArrayTypeSymbol { Rank: > 0 } arrayType)
        {
            term = new SymbolicIntegerConstantTerm(arrayType.Rank);
            return true;
        }

        if (string.Equals(memberName, "HasValue", StringComparison.Ordinal) &&
            TryLowerNullableHasValueTerm(memberAccess.Expression, context, out term))
            return true;

        if (string.Equals(memberName, "Value", StringComparison.Ordinal) &&
            TryLowerNullableValueTerm(memberAccess.Expression, context, out term))
            return true;

        if (string.Equals(memberName, nameof(string.Length), StringComparison.Ordinal))
        {
            if (receiverType?.SpecialType == SpecialType.System_String)
            {
                if (!TryLowerStringTerm(memberAccess.Expression, context, out var stringValue)) return false;

                term = new SymbolicLengthTerm(stringValue);
                return true;
            }

            if (receiverType is IArrayTypeSymbol { Rank: 1 } ||
                IsBuiltInSpanOrMemoryType(receiverType))
            {
                if (!TryLowerTerm(memberAccess.Expression, context, out var lengthReceiver)) return false;

                term = new SymbolicLengthTerm(lengthReceiver);
                return true;
            }

            if (receiverType is IArrayTypeSymbol { Rank: > 1 } multiDimensionalArray &&
                TryLowerArrayTotalLengthTerm(memberAccess.Expression, multiDimensionalArray, context, out term))
                return true;
        }

        if (!TryLowerTerm(memberAccess.Expression, context, out var receiver)) return false;

        if (string.Equals(memberName, "Count", StringComparison.Ordinal) &&
            receiver.Kind == SmtValueKind.Reference)
        {
            term = new SymbolicCountTerm(receiver);
            return true;
        }

        if (TryGetInstanceMemberValueKind(memberAccess, context, out var memberKind) &&
            receiver.Kind == SmtValueKind.Reference)
        {
            term = new SymbolicMemberTerm(receiver, memberName, memberKind);
            return true;
        }

        return false;
    }

    private static bool TryGetInstanceMemberValueKind(
        MemberAccessExpressionSyntax memberAccess,
        SymbolicLoweringContext context,
        out SmtValueKind kind)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        if (symbol is IPropertySymbol { IsStatic: false } property &&
            TryGetValueKind(property.Type, out kind))
            return true;

        if (symbol is IFieldSymbol { IsStatic: false } field &&
            TryGetValueKind(field.Type, out kind))
            return true;

        kind = default;
        return false;
    }

    private static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? type)
    {
        return SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(type);
    }
}