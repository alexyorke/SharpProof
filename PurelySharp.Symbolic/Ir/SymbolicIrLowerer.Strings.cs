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
        private static bool TryLowerStringPredicateInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocation.ArgumentList.Arguments.Count != 1 ||
                method.Parameters.Length != 1 ||
                !TryLowerStringTerm(memberAccess.Expression, context, out var receiver) ||
                !TryLowerStringPredicateArgument(
                    invocation.ArgumentList.Arguments[0].Expression,
                    method.Parameters[0].Type,
                    context,
                    out var argument))
            {
                return false;
            }

            var predicate = method.Name switch
            {
                nameof(string.Contains) => SymbolicStringPredicateKind.Contains,
                nameof(string.StartsWith) => SymbolicStringPredicateKind.StartsWith,
                nameof(string.EndsWith) => SymbolicStringPredicateKind.EndsWith,
                _ => (SymbolicStringPredicateKind?)null,
            };

            if (predicate == null)
            {
                return false;
            }

            if (predicate != SymbolicStringPredicateKind.Contains &&
                method.Parameters[0].Type.SpecialType != SpecialType.System_Char &&
                argument is not SymbolicStringConstantTerm)
            {
                return false;
            }

            condition = CreateFactCondition(
                new SymbolicStringPredicateAtom(
                    predicate.Value,
                    receiver,
                    argument,
                    RegexOptions.None),
                invocation,
                "ir.known-api.string." + method.Name);
            return true;
        }

        private static bool TryLowerRegexIsMatchInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (method.IsStatic != true ||
                invocation.ArgumentList.Arguments.Count is not 2 and not 3 ||
                method.Parameters.Length < 2 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                method.Parameters[1].Type.SpecialType != SpecialType.System_String ||
                !TryLowerStringTerm(invocation.ArgumentList.Arguments[0].Expression, context, out var input) ||
                !TryLowerStringTerm(invocation.ArgumentList.Arguments[1].Expression, context, out var patternTerm) ||
                patternTerm is not SymbolicStringConstantTerm pattern)
            {
                return false;
            }

            var options = RegexOptions.None;
            if (invocation.ArgumentList.Arguments.Count == 3)
            {
                if (!TryGetRegexOptions(invocation.ArgumentList.Arguments[2].Expression, context, out options))
                {
                    return false;
                }
            }

            if (!CanEncodeRegexOptions(options))
            {
                return false;
            }

            condition = CreateFactCondition(
                new SymbolicStringPredicateAtom(
                    SymbolicStringPredicateKind.RegexMatch,
                    input,
                    pattern,
                    options),
                invocation,
                "ir.known-api.regex.is-match");
            return true;
        }

        private static bool TryLowerStringEqualsInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!method.IsStatic)
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    invocation.ArgumentList.Arguments.Count is not 1 and not 2 ||
                    method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
                    method.Parameters[0].Type.SpecialType != SpecialType.System_String)
                {
                    return false;
                }

                if (invocation.ArgumentList.Arguments.Count == 2 &&
                    !IsOrdinalStringComparisonArgument(invocation.ArgumentList.Arguments[1].Expression, context))
                {
                    return false;
                }

                return TryCreateStringEqualityCondition(
                    memberAccess.Expression,
                    invocation.ArgumentList.Arguments[0].Expression,
                    invocation,
                    context,
                    "ir.known-api.string.instance-equals",
                    out condition);
            }

            if (invocation.ArgumentList.Arguments.Count is not 2 and not 3 ||
                method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                method.Parameters[1].Type.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            if (invocation.ArgumentList.Arguments.Count == 3 &&
                !IsOrdinalStringComparisonArgument(invocation.ArgumentList.Arguments[2].Expression, context))
            {
                return false;
            }

            return TryCreateStringEqualityCondition(
                invocation.ArgumentList.Arguments[0].Expression,
                invocation.ArgumentList.Arguments[1].Expression,
                invocation,
                context,
                "ir.known-api.string.equals",
                out condition);
        }

        private static bool TryLowerStringNullOrPredicateInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!method.IsStatic ||
                invocation.ArgumentList.Arguments.Count != 1 ||
                method.Parameters.Length != 1 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                !TryLowerStringValueWithOptionalReference(
                    invocation.ArgumentList.Arguments[0].Expression,
                    context,
                    out var stringValue,
                    out var reference))
            {
                return false;
            }

            SymbolicCondition predicateCondition;
            if (string.Equals(method.Name, nameof(string.IsNullOrEmpty), StringComparison.Ordinal))
            {
                predicateCondition = CreateFactCondition(
                    new SymbolicRelationAtom(
                        SymbolicRelationOperator.Equal,
                        new SymbolicLengthTerm(stringValue),
                        new SymbolicIntegerConstantTerm(0)),
                    invocation,
                    "ir.known-api.string.is-null-or-empty.empty");
            }
            else if (string.Equals(method.Name, nameof(string.IsNullOrWhiteSpace), StringComparison.Ordinal))
            {
                predicateCondition = CreateFactCondition(
                    new SymbolicStringPredicateAtom(
                        SymbolicStringPredicateKind.RegexMatch,
                        stringValue,
                        new SymbolicStringConstantTerm(@"\A\s*\z"),
                        RegexOptions.None),
                    invocation,
                    "ir.known-api.string.is-null-or-white-space.regex");
            }
            else
            {
                return false;
            }

            condition = reference == null
                ? predicateCondition
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.Or,
                    CreateReferenceIsNullCondition(reference, invocation.ArgumentList.Arguments[0].Expression),
                    predicateCondition);
            return true;
        }

        private static bool TryLowerStringExpressionTerm(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            expression = UnwrapExpression(expression);

            if (expression is BinaryExpressionSyntax binary &&
                binary.IsKind(SyntaxKind.AddExpression) &&
                IsStringExpression(binary, context) &&
                TryLowerStringConcatOperand(binary.Left, context, out var left) &&
                TryLowerStringConcatOperand(binary.Right, context, out var right))
            {
                term = new SymbolicStringConcatTerm(left, right);
                return true;
            }

            if (expression is InvocationExpressionSyntax invocation &&
                TryLowerStringConcatInvocationTerm(invocation, context, out term))
            {
                return true;
            }

            if (expression is InterpolatedStringExpressionSyntax interpolatedString &&
                TryLowerInterpolatedStringTerm(interpolatedString, context, out term))
            {
                return true;
            }

            term = null!;
            return false;
        }

        internal static bool TryLowerStringTerm(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            expression = UnwrapExpression(expression);

            var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constantValue.HasValue && constantValue.Value is string stringValue)
            {
                term = new SymbolicStringConstantTerm(stringValue);
                return true;
            }

            if (TryLowerStringExpressionTerm(expression, context, out term))
            {
                return true;
            }

            if (!IsStringExpression(expression, context) ||
                !TryLowerTerm(expression, context, out var reference) ||
                reference.Kind != SmtValueKind.Reference)
            {
                term = null!;
                return false;
            }

            term = new SymbolicStringContentTerm(reference);
            return true;
        }

        private static bool TryLowerStringConcatOperand(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            expression = UnwrapExpression(expression);

            if (expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.NullLiteralExpression))
            {
                term = new SymbolicStringConstantTerm(string.Empty);
                return true;
            }

            var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constantValue.HasValue && constantValue.Value == null)
            {
                term = new SymbolicStringConstantTerm(string.Empty);
                return true;
            }

            if (!TryLowerStringTerm(expression, context, out term))
            {
                return false;
            }

            if (term is SymbolicStringContentTerm stringContent)
            {
                term = new SymbolicConditionalTerm(
                    CreateReferenceIsNullCondition(stringContent.Reference, expression),
                    new SymbolicStringConstantTerm(string.Empty),
                    stringContent);
            }

            return true;
        }

        private static bool TryLowerStringPredicateArgument(
            ExpressionSyntax expression,
            ITypeSymbol parameterType,
            SymbolicLoweringContext context,
            out SymbolicTerm argument)
        {
            if (parameterType.SpecialType == SpecialType.System_String)
            {
                return TryLowerStringTerm(expression, context, out argument);
            }

            if (parameterType.SpecialType == SpecialType.System_Char)
            {
                var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
                if (constantValue.HasValue && constantValue.Value is char charValue)
                {
                    argument = new SymbolicStringConstantTerm(charValue.ToString());
                    return true;
                }
            }

            argument = null!;
            return false;
        }

        private static bool TryLowerStringValueWithOptionalReference(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm stringValue,
            out SymbolicTerm? reference)
        {
            expression = UnwrapExpression(expression);
            reference = null;

            if (!IsStringExpression(expression, context))
            {
                stringValue = null!;
                return false;
            }

            if (TryLowerTerm(expression, context, out var direct))
            {
                if (direct.Kind == SmtValueKind.Reference)
                {
                    reference = direct;
                    stringValue = new SymbolicStringContentTerm(direct);
                    return true;
                }

                if (direct.Kind == SmtValueKind.String)
                {
                    stringValue = direct;
                    return true;
                }
            }

            return TryLowerStringTerm(expression, context, out stringValue);
        }

        private static bool TryLowerStringConcatInvocationTerm(
            InvocationExpressionSyntax invocation,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not Microsoft.CodeAnalysis.Operations.IInvocationOperation operation ||
                !string.Equals(operation.TargetMethod.Name, nameof(string.Concat), StringComparison.Ordinal) ||
                !string.Equals(operation.TargetMethod.ContainingType?.ToDisplayString(), "string", StringComparison.Ordinal))
            {
                return false;
            }

            var parts = ImmutableArray.CreateBuilder<SymbolicTerm>();
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (!TryLowerStringConcatOperand(argument.Expression, context, out var part))
                {
                    return false;
                }

                parts.Add(part);
            }

            return TryCombineStringTerms(parts.ToImmutable(), out term);
        }

        private static bool TryLowerInterpolatedStringTerm(
            InterpolatedStringExpressionSyntax interpolatedString,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            var parts = ImmutableArray.CreateBuilder<SymbolicTerm>();
            foreach (var content in interpolatedString.Contents)
            {
                switch (content)
                {
                    case InterpolatedStringTextSyntax text:
                        parts.Add(new SymbolicStringConstantTerm(text.TextToken.ValueText));
                        break;
                    case InterpolationSyntax interpolation:
                        if (interpolation.AlignmentClause != null ||
                            interpolation.FormatClause != null ||
                            !TryLowerStringConcatOperand(interpolation.Expression, context, out var part))
                        {
                            term = null!;
                            return false;
                        }

                        parts.Add(part);
                        break;
                    default:
                        term = null!;
                        return false;
                }
            }

            return TryCombineStringTerms(parts.ToImmutable(), out term);
        }

        private static bool TryCombineStringTerms(ImmutableArray<SymbolicTerm> parts, out SymbolicTerm term)
        {
            if (parts.Length == 0)
            {
                term = new SymbolicStringConstantTerm(string.Empty);
                return true;
            }

            term = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                term = new SymbolicStringConcatTerm(term, parts[index]);
            }

            return true;
        }

        private static bool TryGetRegexOptions(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out RegexOptions options)
        {
            var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constantValue.HasValue &&
                constantValue.Value != null &&
                TryGetIntegralConstant(constantValue.Value, out var rawOptions))
            {
                options = (RegexOptions)rawOptions;
                return true;
            }

            options = RegexOptions.None;
            return false;
        }

        private static bool CanEncodeRegexOptions(RegexOptions options)
        {
            const RegexOptions supportedOptions =
                RegexOptions.ExplicitCapture |
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant |
                RegexOptions.Singleline |
                RegexOptions.Multiline |
                RegexOptions.IgnorePatternWhitespace |
                RegexOptions.IgnoreCase;

            if ((options & ~supportedOptions) != 0)
            {
                return false;
            }

            return (options & RegexOptions.IgnoreCase) == 0 ||
                (options & RegexOptions.CultureInvariant) != 0;
        }

        private static bool IsOrdinalStringComparisonArgument(
            ExpressionSyntax expression,
            SymbolicLoweringContext context)
        {
            var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
            if (!string.Equals(type?.ToDisplayString(), "System.StringComparison", StringComparison.Ordinal))
            {
                return false;
            }

            var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            return constantValue.HasValue &&
                constantValue.Value != null &&
                TryGetIntegralConstant(constantValue.Value, out var rawComparison) &&
                rawComparison == (int)StringComparison.Ordinal;
        }

        private static bool IsStringExpression(
            ExpressionSyntax expression,
            SymbolicLoweringContext context)
        {
            var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
            return type?.SpecialType == SpecialType.System_String;
        }
    }
}
