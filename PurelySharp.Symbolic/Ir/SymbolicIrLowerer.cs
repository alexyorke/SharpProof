using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static readonly ImmutableArray<KnownApiLoweringDescriptor> KnownApiLowerings =
            ImmutableArray.Create(
                new KnownApiLoweringDescriptor("string", nameof(string.Contains), TryLowerStringPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.StartsWith), TryLowerStringPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.EndsWith), TryLowerStringPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.IsNullOrEmpty), TryLowerStringNullOrPredicateInvocation),
                new KnownApiLoweringDescriptor("string", nameof(string.IsNullOrWhiteSpace), TryLowerStringNullOrPredicateInvocation),
                new KnownApiLoweringDescriptor("System.Text.RegularExpressions.Regex", nameof(Regex.IsMatch), TryLowerRegexIsMatchInvocation));

        public static bool TryLowerCondition(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            expression = UnwrapExpression(expression);
            context.CancellationToken.ThrowIfCancellationRequested();

            var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constantValue.HasValue && constantValue.Value is bool booleanValue)
            {
                condition = new SymbolicConstantCondition(booleanValue);
                return true;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                TryLowerCondition(prefixUnary.Operand, context, out var operand))
            {
                condition = new SymbolicNotCondition(operand);
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                    TryLowerCondition(binaryExpression.Left, context, out var leftAnd) &&
                    TryLowerCondition(binaryExpression.Right, context, out var rightAnd))
                {
                    condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, leftAnd, rightAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                    TryLowerCondition(binaryExpression.Left, context, out var leftOr) &&
                    TryLowerCondition(binaryExpression.Right, context, out var rightOr))
                {
                    condition = new SymbolicBinaryCondition(SymbolicConditionOperator.Or, leftOr, rightOr);
                    return true;
                }

                if (IsEqualityExpression(binaryExpression) &&
                    TryLowerStringEqualityCondition(binaryExpression, context, out condition))
                {
                    return true;
                }

                if (IsEqualityExpression(binaryExpression) &&
                    TryLowerTupleEqualityCondition(binaryExpression, context, out condition))
                {
                    return true;
                }

                if (TryGetRelationOperator(binaryExpression.Kind(), out var relationOperator) &&
                    TryLowerTerm(binaryExpression.Left, context, out var left) &&
                    TryLowerTerm(binaryExpression.Right, context, out var right) &&
                    CanCompareTerms(left, right, relationOperator))
                {
                    condition = CreateFactCondition(
                        new SymbolicRelationAtom(relationOperator, left, right),
                        binaryExpression,
                        "ir.relation");
                    return true;
                }
            }

            if (expression is InvocationExpressionSyntax invocation &&
                TryLowerKnownApiInvocation(invocation, context, out condition))
            {
                return true;
            }

            if (TryLowerTerm(expression, context, out var term) &&
                term.Kind == SmtValueKind.Bool)
            {
                condition = CreateFactCondition(new SymbolicTruthAtom(term), expression, "ir.truth");
                return true;
            }

            condition = null!;
            return false;
        }

        private static bool TryLowerStringEqualityCondition(
            BinaryExpressionSyntax binaryExpression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!IsStringExpression(binaryExpression.Left, context) ||
                !IsStringExpression(binaryExpression.Right, context) ||
                !TryLowerStringTerm(binaryExpression.Left, context, out var leftValue) ||
                !TryLowerStringTerm(binaryExpression.Right, context, out var rightValue))
            {
                return false;
            }

            var valuesEqual = CreateRelationCondition(
                SymbolicRelationOperator.Equal,
                leftValue,
                rightValue,
                binaryExpression,
                "ir.string.equality.value");
            if (TryLowerTerm(binaryExpression.Left, context, out var leftReference) &&
                leftReference.Kind == SmtValueKind.Reference &&
                TryLowerTerm(binaryExpression.Right, context, out var rightReference) &&
                rightReference.Kind == SmtValueKind.Reference)
            {
                var bothNull = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    CreateRelationCondition(
                        SymbolicRelationOperator.Equal,
                        leftReference,
                        new SymbolicNullTerm(),
                        binaryExpression.Left,
                        "ir.string.equality.left-null"),
                    CreateRelationCondition(
                        SymbolicRelationOperator.Equal,
                        rightReference,
                        new SymbolicNullTerm(),
                        binaryExpression.Right,
                        "ir.string.equality.right-null"));
                var bothNonNull = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    CreateRelationCondition(
                        SymbolicRelationOperator.NotEqual,
                        leftReference,
                        new SymbolicNullTerm(),
                        binaryExpression.Left,
                        "ir.string.equality.left-not-null"),
                    CreateRelationCondition(
                        SymbolicRelationOperator.NotEqual,
                        rightReference,
                        new SymbolicNullTerm(),
                        binaryExpression.Right,
                        "ir.string.equality.right-not-null"));
                valuesEqual = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    bothNull,
                    new SymbolicBinaryCondition(SymbolicConditionOperator.And, bothNonNull, valuesEqual));
            }
            else if (TryLowerTerm(binaryExpression.Left, context, out leftReference) &&
                     leftReference.Kind == SmtValueKind.Reference &&
                     rightValue is SymbolicStringConstantTerm)
            {
                valuesEqual = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    CreateRelationCondition(
                        SymbolicRelationOperator.NotEqual,
                        leftReference,
                        new SymbolicNullTerm(),
                        binaryExpression.Left,
                        "ir.string.equality.left-not-null"),
                    valuesEqual);
            }
            else if (TryLowerTerm(binaryExpression.Right, context, out rightReference) &&
                     rightReference.Kind == SmtValueKind.Reference &&
                     leftValue is SymbolicStringConstantTerm)
            {
                valuesEqual = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    CreateRelationCondition(
                        SymbolicRelationOperator.NotEqual,
                        rightReference,
                        new SymbolicNullTerm(),
                        binaryExpression.Right,
                        "ir.string.equality.right-not-null"),
                    valuesEqual);
            }

            condition = binaryExpression.IsKind(SyntaxKind.EqualsExpression)
                ? valuesEqual
                : new SymbolicNotCondition(valuesEqual);
            return true;
        }

        private static bool TryLowerTupleEqualityCondition(
            BinaryExpressionSyntax binaryExpression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerTupleElementTerms(binaryExpression.Left, context, out var leftElements) ||
                !TryLowerTupleElementTerms(binaryExpression.Right, context, out var rightElements) ||
                leftElements.Length == 0 ||
                leftElements.Length != rightElements.Length)
            {
                return false;
            }

            SymbolicCondition? equality = null;
            for (var index = 0; index < leftElements.Length; index++)
            {
                if (!CanCompareTerms(leftElements[index], rightElements[index], SymbolicRelationOperator.Equal))
                {
                    return false;
                }

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

        public static bool TryLowerTerm(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            expression = UnwrapExpression(expression);
            context.CancellationToken.ThrowIfCancellationRequested();

            var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constantValue.HasValue)
            {
                if (constantValue.Value is bool booleanValue)
                {
                    term = new SymbolicBooleanConstantTerm(booleanValue);
                    return true;
                }

                if (constantValue.Value == null)
                {
                    term = new SymbolicNullTerm();
                    return true;
                }

                if (constantValue.Value is string stringValue)
                {
                    term = new SymbolicStringConstantTerm(stringValue);
                    return true;
                }

                if (TryGetIntegralConstant(constantValue.Value, out var integralValue))
                {
                    term = new SymbolicIntegerConstantTerm(integralValue);
                    return true;
                }
            }

            if (expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.NullLiteralExpression))
            {
                term = new SymbolicNullTerm();
                return true;
            }

            if (TryLowerSupportedConversionTerm(expression, context, out term))
            {
                return true;
            }

            if (expression is ThisExpressionSyntax)
            {
                term = new SymbolicVariableTerm("this", SmtValueKind.Reference);
                return true;
            }

            if (TryLowerStringExpressionTerm(expression, context, out term))
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax binary &&
                TryGetBinaryTermOperator(binary.Kind(), out var binaryOperator) &&
                TryLowerTerm(binary.Left, context, out var left) &&
                TryLowerTerm(binary.Right, context, out var right) &&
                left.Kind == SmtValueKind.Int &&
                right.Kind == SmtValueKind.Int)
            {
                term = new SymbolicBinaryTerm(binaryOperator, left, right);
                return true;
            }

            if (expression is MemberAccessExpressionSyntax memberAccess &&
                TryLowerMemberTerm(memberAccess, context, out term))
            {
                return true;
            }

            var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
            if ((symbol is ILocalSymbol || symbol is IParameterSymbol) &&
                TryGetSymbolType(symbol, out var symbolType) &&
                TryGetValueKind(symbolType, out var kind))
            {
                term = new SymbolicVariableTerm(context.GetVariableName(symbol), kind);
                return true;
            }

            term = null!;
            return false;
        }

        private static bool TryLowerMemberTerm(
            MemberAccessExpressionSyntax memberAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;

            var memberName = memberAccess.Name.Identifier.ValueText;
            if (TryLowerKnownStaticValueMember(memberAccess, context, out term))
            {
                return true;
            }

            var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
            if (TryLowerTupleElementMemberTerm(memberAccess, context, out term))
            {
                return true;
            }

            if (string.Equals(memberName, "HasValue", StringComparison.Ordinal) &&
                TryLowerNullableHasValueTerm(memberAccess.Expression, context, out term))
            {
                return true;
            }

            if (string.Equals(memberName, nameof(string.Length), StringComparison.Ordinal))
            {
                if (receiverType?.SpecialType == SpecialType.System_String)
                {
                    if (!TryLowerStringTerm(memberAccess.Expression, context, out var stringValue))
                    {
                        return false;
                    }

                    term = new SymbolicLengthTerm(stringValue);
                    return true;
                }

                if (receiverType is IArrayTypeSymbol { Rank: 1 } ||
                    IsBuiltInSpanOrMemoryType(receiverType))
                {
                    if (!TryLowerTerm(memberAccess.Expression, context, out var lengthReceiver))
                    {
                        return false;
                    }

                    term = new SymbolicLengthTerm(lengthReceiver);
                    return true;
                }
            }

            if (!TryLowerTerm(memberAccess.Expression, context, out var receiver))
            {
                return false;
            }

            if (string.Equals(memberName, "Count", StringComparison.Ordinal) &&
                receiver.Kind == SmtValueKind.Reference)
            {
                term = new SymbolicCountTerm(receiver);
                return true;
            }

            if (TryGetInstanceMemberValueKind(memberAccess, context, out var memberKind) &&
                receiver.Kind == SmtValueKind.Reference &&
                memberKind == SmtValueKind.Reference)
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
            {
                return true;
            }

            if (symbol is IFieldSymbol { IsStatic: false } field &&
                TryGetValueKind(field.Type, out kind))
            {
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryLowerSupportedConversionTerm(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (expression is CheckedExpressionSyntax checkedExpression &&
                checkedExpression.IsKind(SyntaxKind.UncheckedExpression))
            {
                if (checkedExpression.Expression is CastExpressionSyntax)
                {
                    return TryLowerSupportedConversionTerm(checkedExpression.Expression, context, out term);
                }

                term = null!;
                return false;
            }

            if (expression is CastExpressionSyntax castExpression)
            {
                var sourceType = context.SemanticModel.GetTypeInfo(castExpression.Expression, context.CancellationToken).Type;
                var targetType = context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type;
                if (sourceType?.TypeKind == TypeKind.Enum &&
                    sourceType is INamedTypeSymbol { EnumUnderlyingType.SpecialType: SpecialType.System_Int32 } &&
                    targetType?.SpecialType == SpecialType.System_Int32 &&
                    TryLowerTerm(castExpression.Expression, context, out var operand) &&
                    operand.Kind == SmtValueKind.Int)
                {
                    term = operand;
                    return true;
                }
            }

            term = null!;
            return false;
        }

        private static bool TryLowerTupleElementMemberTerm(
            MemberAccessExpressionSyntax memberAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (!TryGetStableVariableSymbol(memberAccess.Expression, context, out var tupleSymbol) ||
                context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IFieldSymbol field ||
                !TryGetTupleElementStorageName(field, out var storageName) ||
                !TryGetValueKind(field.Type, out var kind))
            {
                return false;
            }

            term = new SymbolicVariableTerm(context.GetVariableName(tupleSymbol) + "." + storageName, kind);
            return true;
        }

        public static bool TryLowerNullableHasValueTerm(
            ExpressionSyntax nullableExpression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            nullableExpression = UnwrapExpression(nullableExpression);
            if (!SymbolicTypeFacts.TryGetNullableUnderlyingType(
                    context.SemanticModel.GetTypeInfo(nullableExpression, context.CancellationToken).Type,
                    out _) ||
                !TryGetStableVariableSymbol(nullableExpression, context, out var symbol))
            {
                term = null!;
                return false;
            }

            term = new SymbolicNullableHasValueTerm(context.GetVariableName(symbol));
            return true;
        }

        private static bool TryGetStableVariableSymbol(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out ISymbol symbol)
        {
            if (expression is IdentifierNameSyntax)
            {
                symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol!;
                return symbol is ILocalSymbol or IParameterSymbol;
            }

            symbol = null!;
            return false;
        }

        private static bool TryLowerKnownApiInvocation(
            InvocationExpressionSyntax invocation,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not Microsoft.CodeAnalysis.Operations.IInvocationOperation operation)
            {
                return false;
            }

            foreach (var descriptor in KnownApiLowerings)
            {
                if (descriptor.Matches(operation.TargetMethod) &&
                    descriptor.Handler(invocation, operation.TargetMethod, context, out condition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryLowerKnownStaticValueMember(
            MemberAccessExpressionSyntax memberAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is IPropertySymbol property &&
                IsBigIntegerType(property.Type))
            {
                if (string.Equals(property.Name, "Zero", StringComparison.Ordinal))
                {
                    term = new SymbolicIntegerConstantTerm(0);
                    return true;
                }

                if (string.Equals(property.Name, "One", StringComparison.Ordinal))
                {
                    term = new SymbolicIntegerConstantTerm(1);
                    return true;
                }
            }

            term = null!;
            return false;
        }

        private static SymbolicCondition CreateFactCondition(SymbolicAtom atom, SyntaxNode node, string provenance)
        {
            return new SymbolicFactCondition(SymbolicFact.Exact(atom, node, provenance));
        }

        private static SymbolicCondition CreateRelationCondition(
            SymbolicRelationOperator op,
            SymbolicTerm left,
            SymbolicTerm right,
            SyntaxNode node,
            string provenance)
        {
            return CreateFactCondition(new SymbolicRelationAtom(op, left, right), node, provenance);
        }

        private static SymbolicCondition CreateReferenceIsNullCondition(SymbolicTerm reference, SyntaxNode node)
        {
            return CreateFactCondition(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    reference,
                    new SymbolicNullTerm()),
                node,
                "ir.string.concat.null-empty");
        }

        private static bool CanCompareTerms(SymbolicTerm left, SymbolicTerm right, SymbolicRelationOperator op)
        {
            if (op is not SymbolicRelationOperator.Equal and not SymbolicRelationOperator.NotEqual &&
                left.Kind != SmtValueKind.Int)
            {
                return false;
            }

            return left.Kind == right.Kind ||
                left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference ||
                right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference;
        }

        private static bool IsEqualityExpression(BinaryExpressionSyntax binaryExpression)
        {
            return binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
                binaryExpression.IsKind(SyntaxKind.NotEqualsExpression);
        }

        private static bool TryLowerTupleElementTerms(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out ImmutableArray<SymbolicTerm> terms)
        {
            terms = ImmutableArray<SymbolicTerm>.Empty;
            if (!TryGetStableVariableSymbol(expression, context, out var symbol) ||
                context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type is not INamedTypeSymbol { IsTupleType: true } tupleType ||
                tupleType.TupleElements.Length == 0)
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<SymbolicTerm>(tupleType.TupleElements.Length);
            foreach (var element in tupleType.TupleElements)
            {
                var field = element.CorrespondingTupleField ?? element;
                if (!TryGetTupleElementStorageName(field, out var storageName) ||
                    !TryGetValueKind(field.Type, out var kind))
                {
                    return false;
                }

                builder.Add(new SymbolicVariableTerm(context.GetVariableName(symbol) + "." + storageName, kind));
            }

            terms = builder.ToImmutable();
            return true;
        }

        private static bool TryGetTupleElementStorageName(IFieldSymbol field, out string storageName)
        {
            var storageField = field.CorrespondingTupleField ?? field;
            storageName = storageField.Name;
            return storageName.StartsWith("Item", StringComparison.Ordinal);
        }

        private static bool TryGetRelationOperator(SyntaxKind kind, out SymbolicRelationOperator op)
        {
            switch (kind)
            {
                case SyntaxKind.EqualsExpression:
                    op = SymbolicRelationOperator.Equal;
                    return true;
                case SyntaxKind.NotEqualsExpression:
                    op = SymbolicRelationOperator.NotEqual;
                    return true;
                case SyntaxKind.LessThanExpression:
                    op = SymbolicRelationOperator.LessThan;
                    return true;
                case SyntaxKind.LessThanOrEqualExpression:
                    op = SymbolicRelationOperator.LessThanOrEqual;
                    return true;
                case SyntaxKind.GreaterThanExpression:
                    op = SymbolicRelationOperator.GreaterThan;
                    return true;
                case SyntaxKind.GreaterThanOrEqualExpression:
                    op = SymbolicRelationOperator.GreaterThanOrEqual;
                    return true;
                default:
                    op = default;
                    return false;
            }
        }

        private static bool TryGetBinaryTermOperator(SyntaxKind kind, out SymbolicBinaryTermOperator op)
        {
            switch (kind)
            {
                case SyntaxKind.AddExpression:
                    op = SymbolicBinaryTermOperator.Add;
                    return true;
                case SyntaxKind.SubtractExpression:
                    op = SymbolicBinaryTermOperator.Subtract;
                    return true;
                case SyntaxKind.MultiplyExpression:
                    op = SymbolicBinaryTermOperator.Multiply;
                    return true;
                case SyntaxKind.DivideExpression:
                    op = SymbolicBinaryTermOperator.Divide;
                    return true;
                case SyntaxKind.ModuloExpression:
                    op = SymbolicBinaryTermOperator.Remainder;
                    return true;
                default:
                    op = default;
                    return false;
            }
        }

        private static bool TryGetSymbolType(ISymbol symbol, out ITypeSymbol type)
        {
            switch (symbol)
            {
                case ILocalSymbol local:
                    type = local.Type;
                    return true;
                case IParameterSymbol parameter:
                    type = parameter.Type;
                    return true;
                case IPropertySymbol property:
                    type = property.Type;
                    return true;
                case IFieldSymbol field:
                    type = field.Type;
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }

        private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                kind = SmtValueKind.Bool;
                return true;
            }

            if (IsIntegerSmtType(type))
            {
                kind = SmtValueKind.Int;
                return true;
            }

            if (type.TypeKind == TypeKind.Dynamic ||
                type.IsReferenceType ||
                IsSupportedTupleCarrierType(type))
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool IsIntegerSmtType(ITypeSymbol type)
        {
            return type.SpecialType is
                SpecialType.System_Char or
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 ||
                type.TypeKind == TypeKind.Enum ||
                IsBigIntegerType(type);
        }

        private static bool IsBigIntegerType(ITypeSymbol type)
        {
            return string.Equals(type.ContainingNamespace?.ToDisplayString(), "System.Numerics", StringComparison.Ordinal) &&
                string.Equals(type.Name, "BigInteger", StringComparison.Ordinal);
        }

        private static bool IsSupportedTupleCarrierType(ITypeSymbol type)
        {
            return type is INamedTypeSymbol { IsTupleType: true, TupleElements.Length: > 0 };
        }

        private static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol namedType)
            {
                return false;
            }

            var metadataName = namedType.ConstructedFrom.ToDisplayString();
            return string.Equals(metadataName, "System.Span<T>", StringComparison.Ordinal) ||
                string.Equals(metadataName, "System.ReadOnlySpan<T>", StringComparison.Ordinal) ||
                string.Equals(metadataName, "System.Memory<T>", StringComparison.Ordinal) ||
                string.Equals(metadataName, "System.ReadOnlyMemory<T>", StringComparison.Ordinal);
        }

        private static bool TryGetIntegralConstant(object value, out long result)
        {
            try
            {
                switch (value)
                {
                    case char charValue:
                        result = charValue;
                        return true;
                    case sbyte sbyteValue:
                        result = sbyteValue;
                        return true;
                    case byte byteValue:
                        result = byteValue;
                        return true;
                    case short shortValue:
                        result = shortValue;
                        return true;
                    case ushort ushortValue:
                        result = ushortValue;
                        return true;
                    case int intValue:
                        result = intValue;
                        return true;
                    case uint uintValue:
                        result = uintValue;
                        return true;
                    case long longValue:
                        result = longValue;
                        return true;
                    case ulong ulongValue when ulongValue <= long.MaxValue:
                        result = (long)ulongValue;
                        return true;
                }
            }
            catch (OverflowException)
            {
            }

            result = 0;
            return false;
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        continue;
                    case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                        expression = postfix.Operand;
                        continue;
                    case CastExpressionSyntax castExpression
                        when castExpression.Type is NullableTypeSyntax:
                        expression = castExpression.Expression;
                        continue;
                    default:
                        return expression;
                }
            }
        }
    }
}
