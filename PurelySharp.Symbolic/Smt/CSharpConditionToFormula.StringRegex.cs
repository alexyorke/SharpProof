using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Smt
{
    internal static partial class CSharpConditionToFormula
    {        private static bool TryTranslateSourceBooleanInvocation(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (inlineDepth >= MaxSourcePredicateInlineDepth ||
                semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                !CanInlineSourceBooleanPredicate(invocationOperation.TargetMethod) ||
                !TryGetReturnedBooleanFormula(
                    invocationOperation.TargetMethod,
                    semanticModel.Compilation,
                    cancellationToken,
                    inlineDepth + 1,
                    out var returnedFormula) ||
                returnedFormula is not { Kind: SmtValueKind.Bool } ||
                !TryCreateSourcePredicateSubstitutions(
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion,
                    inlineDepth,
                    out var substitutions))
            {
                return false;
            }

            formula = SubstituteVariables(returnedFormula, substitutions);
            return true;
        }

        private static bool TryTranslateKnownStringBooleanInvocation(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation)
            {
                return false;
            }

            return TryTranslateRegexIsMatchInvocation(
                    invocationExpression,
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth) ||
                TryTranslateStringEqualsInvocation(
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth) ||
                TryTranslateStringPredicateInvocation(
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth) ||
                TryTranslateStringIsNullOrEmptyInvocation(
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
        }

        private static bool TryTranslateStringEqualsInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var method = invocationOperation.TargetMethod;
            if (method.Name != "Equals" ||
                method.ReturnType.SpecialType != SpecialType.System_Boolean ||
                method.ContainingType?.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            var isOrdinal = IsSupportedOrdinalStringEqualsInvocation(invocationOperation, semanticModel, cancellationToken);
            var isOrdinalIgnoreCase = IsSupportedOrdinalIgnoreCaseStringEqualsInvocation(invocationOperation, semanticModel, cancellationToken);
            if (!isOrdinal && !isOrdinalIgnoreCase)
            {
                return false;
            }

            if (method.IsStatic)
            {
                if (invocationOperation.Arguments.Length < 2 ||
                    invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax leftExpression ||
                    invocationOperation.Arguments[1].Value.Syntax is not ExpressionSyntax rightExpression ||
                    !TryTranslateStringValue(leftExpression, semanticModel, cancellationToken, out var left, getSymbolVersion, inlineDepth) ||
                    left == null ||
                    !TryTranslateStringValue(rightExpression, semanticModel, cancellationToken, out var right, getSymbolVersion, inlineDepth) ||
                    right == null)
                {
                    return false;
                }

                if (isOrdinal)
                {
                    formula = CreateNullSafeStringEqualityFormula(
                        leftExpression,
                        rightExpression,
                        left,
                        right,
                        semanticModel,
                        cancellationToken,
                        getSymbolVersion,
                        inlineDepth);
                    return true;
                }

                return TryCreateOrdinalIgnoreCaseStringEqualityFormula(
                    leftExpression,
                    rightExpression,
                    left,
                    right,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            if (invocationOperation.Arguments.Length < 1 ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax argumentExpression ||
                !TryTranslateStringValue(receiverExpression, semanticModel, cancellationToken, out var receiver, getSymbolVersion, inlineDepth) ||
                receiver == null ||
                !TryTranslateStringValue(argumentExpression, semanticModel, cancellationToken, out var argument, getSymbolVersion, inlineDepth) ||
                argument == null ||
                !TryCreateStringNonNullFormula(receiverExpression, semanticModel, cancellationToken, out var receiverNonNull, getSymbolVersion, inlineDepth) ||
                receiverNonNull == null)
            {
                return false;
            }

            if (isOrdinalIgnoreCase)
            {
                return TryCreateOrdinalIgnoreCaseStringEqualityFormula(
                    receiverExpression,
                    argumentExpression,
                    receiver,
                    argument,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            formula = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                receiverNonNull,
                new SmtBinaryFormula(SmtBinaryOperator.Equal, receiver, argument));
            return true;
        }

        private static bool IsSupportedOrdinalStringEqualsInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var method = invocationOperation.TargetMethod;
            if (method.ContainingType?.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            var parameters = method.Parameters;
            if (method.IsStatic)
            {
                if (parameters.Length == 2)
                {
                    return IsStringParameter(parameters[0]) &&
                        IsStringParameter(parameters[1]);
                }

                return parameters.Length == 3 &&
                    IsStringParameter(parameters[0]) &&
                    IsStringParameter(parameters[1]) &&
                    IsStringComparisonParameter(parameters[2]) &&
                    HasOrdinalStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken);
            }

            if (parameters.Length == 1)
            {
                return IsStringParameter(parameters[0]);
            }

            return parameters.Length == 2 &&
                IsStringParameter(parameters[0]) &&
                IsStringComparisonParameter(parameters[1]) &&
                HasOrdinalStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken);
        }

        private static bool IsSupportedOrdinalIgnoreCaseStringEqualsInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var method = invocationOperation.TargetMethod;
            if (method.ContainingType?.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            var parameters = method.Parameters;
            if (method.IsStatic)
            {
                return parameters.Length == 3 &&
                    IsStringParameter(parameters[0]) &&
                    IsStringParameter(parameters[1]) &&
                    IsStringComparisonParameter(parameters[2]) &&
                    HasOrdinalIgnoreCaseStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken);
            }

            return parameters.Length == 2 &&
                IsStringParameter(parameters[0]) &&
                IsStringComparisonParameter(parameters[1]) &&
                HasOrdinalIgnoreCaseStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken);
        }

        private static bool TryCreateOrdinalIgnoreCaseStringEqualityFormula(
            ExpressionSyntax leftExpression,
            ExpressionSyntax rightExpression,
            SmtFormula left,
            SmtFormula right,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            SmtFormula subject;
            ExpressionSyntax subjectExpression;
            string constant;
            var rightConstant = TryGetConstantString(rightExpression, semanticModel, cancellationToken);
            if (rightConstant != null)
            {
                subject = left;
                subjectExpression = leftExpression;
                constant = rightConstant;
            }
            else
            {
                var leftConstant = TryGetConstantString(leftExpression, semanticModel, cancellationToken);
                if (leftConstant == null)
                {
                    return false;
                }

                subject = right;
                subjectExpression = rightExpression;
                constant = leftConstant;
            }

            if (!TryCreateStringNonNullFormula(subjectExpression, semanticModel, cancellationToken, out var subjectNonNull, getSymbolVersion, inlineDepth) ||
                subjectNonNull == null)
            {
                return false;
            }

            formula = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                subjectNonNull,
                new SmtRegexMatchFormula(
                    subject,
                    "\\A" + Regex.Escape(constant) + "\\z",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            return true;
        }

        private static bool TryTranslateRegexIsMatchInvocation(
            InvocationExpressionSyntax invocationExpression,
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var method = invocationOperation.TargetMethod;
            if (method.Name != "IsMatch" ||
                !IsRegexType(method.ContainingType))
            {
                return false;
            }

            ExpressionSyntax? inputExpression = null;
            string? pattern = null;
            RegexOptions options = RegexOptions.None;
            if (method.IsStatic)
            {
                if (!TryGetRegexOptions(
                        invocationOperation.Arguments,
                        startIndex: 2,
                        semanticModel,
                        cancellationToken,
                        out options))
                {
                    return false;
                }

                if (invocationOperation.Arguments.Length < 2 ||
                    invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax staticInputExpression)
                {
                    return false;
                }

                inputExpression = staticInputExpression;
                pattern = TryGetConstantString(invocationOperation.Arguments[1].Value.Syntax as ExpressionSyntax, semanticModel, cancellationToken);
                if (pattern != null && CanEncodeRegexOptions(options))
                {
                    pattern = WrapRegexPatternWithInlineOptions(pattern, CreateInlineRegexOptionLetters(options));
                }
            }
            else
            {
                if (!TryGetRegexInstanceInputExpression(
                        invocationOperation,
                        semanticModel,
                        cancellationToken,
                        out var instanceInputExpression,
                        out var requiresEncodableOptions))
                {
                    return false;
                }

                inputExpression = instanceInputExpression;
                if (!TryGetRegexPatternFromReceiver(invocationExpression, semanticModel, cancellationToken, out pattern, out options))
                {
                    return false;
                }

                if (requiresEncodableOptions && !CanEncodeRegexOptions(options))
                {
                    return false;
                }
            }

            if (inputExpression == null ||
                pattern == null ||
                !TryTranslateStringValue(inputExpression, semanticModel, cancellationToken, out var inputFormula, getSymbolVersion, inlineDepth) ||
                inputFormula == null)
            {
                return false;
            }

            formula = new SmtRegexMatchFormula(inputFormula, pattern, options);
            return true;
        }

        private static bool TryGetRegexInstanceInputExpression(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax inputExpression,
            out bool requiresEncodableOptions)
        {
            inputExpression = null!;
            requiresEncodableOptions = false;
            var method = invocationOperation.TargetMethod;
            if (method.IsStatic ||
                method.Parameters.Length < 1 ||
                !IsStringParameter(method.Parameters[0]) ||
                invocationOperation.Arguments.Length < 1 ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax candidateInputExpression)
            {
                return false;
            }

            inputExpression = candidateInputExpression;
            if (invocationOperation.Arguments.Length == 1)
            {
                return true;
            }

            if (invocationOperation.Arguments.Length == 2 &&
                method.Parameters.Length == 2 &&
                method.Parameters[1].Type.SpecialType == SpecialType.System_Int32 &&
                TryGetIntegralConstantValue(invocationOperation.Arguments[1].Value.Syntax as ExpressionSyntax, semanticModel, cancellationToken, out var startAt) &&
                startAt == 0)
            {
                requiresEncodableOptions = true;
                return true;
            }

            inputExpression = null!;
            return false;
        }

        private static bool TryTranslateStringPredicateInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var method = invocationOperation.TargetMethod;
            if (method.ReturnType.SpecialType != SpecialType.System_Boolean ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                method.IsStatic ||
                method.Parameters.Length < 1 ||
                invocationOperation.Arguments.Length < 1 ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression)
            {
                return false;
            }

            if (method.Name is not "Contains" and not "StartsWith" and not "EndsWith")
            {
                return false;
            }

            var firstParameterType = method.Parameters[0].Type;
            var isCharPredicateArgument = firstParameterType.SpecialType == SpecialType.System_Char;
            if (method.Name is "StartsWith" or "EndsWith")
            {
                if (!isCharPredicateArgument &&
                    !HasOrdinalStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken))
                {
                    return false;
                }
            }
            else if (invocationOperation.Arguments.Length > 1 &&
                     !HasOrdinalStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken))
            {
                return false;
            }

            if (invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax searchExpression ||
                !TryTranslateStringPredicateArgument(
                    searchExpression,
                    invocationOperation.Arguments[0].Parameter?.Type,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion,
                    inlineDepth,
                    out var searchFormula) ||
                searchFormula == null ||
                !TryTranslateStringValue(receiverExpression, semanticModel, cancellationToken, out var receiverFormula, getSymbolVersion, inlineDepth) ||
                receiverFormula == null)
            {
                return false;
            }

            formula = method.Name switch
            {
                "Contains" => new SmtStringContainsFormula(receiverFormula, searchFormula),
                "StartsWith" => new SmtStringStartsWithFormula(receiverFormula, searchFormula),
                "EndsWith" => new SmtStringEndsWithFormula(receiverFormula, searchFormula),
                _ => null
            };
            return formula != null;
        }

        private static bool TryTranslateStringPredicateArgument(
            ExpressionSyntax argumentExpression,
            ITypeSymbol? parameterType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            if (parameterType?.SpecialType == SpecialType.System_String)
            {
                return TryTranslateStringValue(
                        argumentExpression,
                        semanticModel,
                        cancellationToken,
                        out formula,
                        getSymbolVersion,
                        inlineDepth) &&
                    formula != null;
            }

            if (parameterType?.SpecialType != SpecialType.System_Char)
            {
                return false;
            }

            var constantValue = semanticModel.GetConstantValue(argumentExpression, cancellationToken);
            if (constantValue is not { HasValue: true, Value: char character })
            {
                return false;
            }

            formula = new SmtStringConstant(character.ToString());
            return true;
        }

        private static bool TryTranslateStringIndexOfComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (TryTranslateStringIndexOfComparisonOperand(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    binaryExpression.Kind(),
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            return TryTranslateStringIndexOfComparisonOperand(
                binaryExpression.Right,
                binaryExpression.Left,
                ReverseComparisonKind(binaryExpression.Kind()),
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        private static bool TryTranslateStringIndexOfComparisonOperand(
            ExpressionSyntax indexExpression,
            ExpressionSyntax constantExpression,
            SyntaxKind comparisonKind,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryTranslateStringIndexOfContainsFormula(
                    indexExpression,
                    semanticModel,
                    cancellationToken,
                    out var containsFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                containsFormula == null ||
                !TryGetIntegralConstantValue(constantExpression, semanticModel, cancellationToken, out var constantValue) ||
                !TryClassifyStringIndexOfComparison(comparisonKind, constantValue, out var isContains))
            {
                return false;
            }

            formula = isContains
                ? containsFormula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, containsFormula);
            return true;
        }

        private static bool TryTranslateStringIndexOfContainsFormula(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            expression = UnwrapExpression(expression);
            if (expression is not InvocationExpressionSyntax invocationExpression ||
                semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                invocationOperation.Arguments.Length < 1 ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax searchExpression ||
                !TryTranslateStringValue(receiverExpression, semanticModel, cancellationToken, out var receiverFormula, getSymbolVersion, inlineDepth) ||
                receiverFormula == null)
            {
                return false;
            }

            if (IsSupportedOrdinalStringIndexOfInvocation(invocationOperation, semanticModel, cancellationToken))
            {
                if (!TryTranslateStringPredicateArgument(
                        searchExpression,
                        invocationOperation.Arguments[0].Parameter?.Type,
                        semanticModel,
                        cancellationToken,
                        getSymbolVersion,
                        inlineDepth,
                        out var searchFormula) ||
                    searchFormula == null)
                {
                    return false;
                }

                formula = new SmtStringContainsFormula(receiverFormula, searchFormula);
                return true;
            }

            if (IsSupportedOrdinalIgnoreCaseStringIndexOfInvocation(invocationOperation, semanticModel, cancellationToken) &&
                TryGetConstantStringPredicateArgument(searchExpression, invocationOperation.Arguments[0].Parameter?.Type, semanticModel, cancellationToken, out var constantSearch))
            {
                formula = new SmtRegexMatchFormula(
                    receiverFormula,
                    Regex.Escape(constantSearch),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return true;
            }

            return false;
        }

        private static bool IsSupportedOrdinalStringIndexOfInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var method = invocationOperation.TargetMethod;
            if (method.Name != "IndexOf" ||
                method.ReturnType.SpecialType != SpecialType.System_Int32 ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                method.IsStatic ||
                method.Parameters.Length == 0 ||
                invocationOperation.Arguments.Length == 0)
            {
                return false;
            }

            var firstParameter = method.Parameters[0];
            if (firstParameter.Type.SpecialType == SpecialType.System_Char)
            {
                if (method.Parameters.Length == 1)
                {
                    return true;
                }

                return method.Parameters.Length == 2 &&
                    IsStringComparisonParameter(method.Parameters[1]) &&
                    HasOrdinalStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken);
            }

            return method.Parameters.Length == 2 &&
                firstParameter.Type.SpecialType == SpecialType.System_String &&
                IsStringComparisonParameter(method.Parameters[1]) &&
                HasOrdinalStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken);
        }

        private static bool IsSupportedOrdinalIgnoreCaseStringIndexOfInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var method = invocationOperation.TargetMethod;
            if (method.Name != "IndexOf" ||
                method.ReturnType.SpecialType != SpecialType.System_Int32 ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                method.IsStatic ||
                method.Parameters.Length != 2 ||
                invocationOperation.Arguments.Length < 2 ||
                !IsStringComparisonParameter(method.Parameters[1]) ||
                !HasOrdinalIgnoreCaseStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken))
            {
                return false;
            }

            var firstParameter = method.Parameters[0];
            return firstParameter.Type.SpecialType is SpecialType.System_Char or SpecialType.System_String;
        }

        private static bool TryClassifyStringIndexOfComparison(
            SyntaxKind comparisonKind,
            long constantValue,
            out bool isContains)
        {
            isContains = default;
            switch (comparisonKind)
            {
                case SyntaxKind.EqualsExpression when constantValue == -1:
                case SyntaxKind.LessThanExpression when constantValue == 0:
                case SyntaxKind.LessThanOrEqualExpression when constantValue == -1:
                    isContains = false;
                    return true;

                case SyntaxKind.NotEqualsExpression when constantValue == -1:
                case SyntaxKind.GreaterThanExpression when constantValue == -1:
                case SyntaxKind.GreaterThanOrEqualExpression when constantValue == 0:
                    isContains = true;
                    return true;

                default:
                    return false;
            }
        }

        private static bool TryTranslateStringIsNullOrEmptyInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var method = invocationOperation.TargetMethod;
            if (!method.IsStatic ||
                method.Name != "IsNullOrEmpty" ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                invocationOperation.Arguments.Length != 1 ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax valueExpression ||
                !TryTranslateValue(valueExpression, semanticModel, cancellationToken, out var referenceFormula, getSymbolVersion, inlineDepth) ||
                referenceFormula is not { Kind: SmtValueKind.Reference } ||
                !TryTranslateStringValue(valueExpression, semanticModel, cancellationToken, out var stringFormula, getSymbolVersion, inlineDepth) ||
                stringFormula == null)
            {
                return false;
            }

            var isNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, referenceFormula, new SmtNullConstant());
            var isEmpty = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(stringFormula),
                new SmtIntegerConstant(0));
            formula = new SmtBinaryFormula(SmtBinaryOperator.Or, isNull, isEmpty);
            return true;
        }

        private static bool IsRegexType(INamedTypeSymbol? type)
        {
            return type?.ToDisplayString() == "System.Text.RegularExpressions.Regex";
        }

        private static bool TryGetRegexPatternFromReceiver(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out string? pattern,
            out RegexOptions options)
        {
            pattern = null;
            options = RegexOptions.None;
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var receiver = UnwrapExpression(memberAccess.Expression);
            if (TryGetRegexPatternFromObjectCreation(receiver, semanticModel, cancellationToken, out pattern, out options))
            {
                return true;
            }

            if (semanticModel.GetSymbolInfo(receiver, cancellationToken).Symbol is not ILocalSymbol localSymbol ||
                localSymbol.Type is not INamedTypeSymbol localType ||
                !IsRegexType(localType))
            {
                return false;
            }

            return TryResolveAssignedRegexObjectCreation(
                receiver,
                localSymbol.OriginalDefinition,
                semanticModel,
                cancellationToken,
                out pattern,
                out options);
        }

        private static bool TryGetRegexPatternFromObjectCreation(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out string? pattern,
            out RegexOptions options)
        {
            pattern = null;
            options = RegexOptions.None;
            expression = UnwrapExpression(expression);
            if (expression is not ObjectCreationExpressionSyntax objectCreation ||
                semanticModel.GetOperation(objectCreation, cancellationToken) is not IObjectCreationOperation objectCreationOperation ||
                objectCreationOperation.Constructor?.ContainingType is not { } constructedType ||
                !IsRegexType(constructedType) ||
                objectCreationOperation.Arguments.Length < 1 ||
                !TryGetRegexOptions(
                    objectCreationOperation.Arguments,
                    startIndex: 1,
                    semanticModel,
                    cancellationToken,
                    out options))
            {
                return false;
            }

            var rawPattern = TryGetConstantString(objectCreationOperation.Arguments[0].Value.Syntax as ExpressionSyntax, semanticModel, cancellationToken);
            if (rawPattern == null)
            {
                return false;
            }

            pattern = CanEncodeRegexOptions(options)
                ? WrapRegexPatternWithInlineOptions(rawPattern, CreateInlineRegexOptionLetters(options))
                : rawPattern;
            return true;
        }

        private static bool TryResolveAssignedRegexObjectCreation(
            ExpressionSyntax useExpression,
            ISymbol regexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out string? pattern,
            out RegexOptions options)
        {
            pattern = null;
            options = RegexOptions.None;
            var foundAssignment = false;
            foreach (var containingBlock in EnumerateContainingBlocks(useExpression).Reverse())
            {
                foreach (var statement in containingBlock.Block.Statements)
                {
                    if (statement == containingBlock.ContainingStatement)
                    {
                        break;
                    }

                    TryGetRegexAssignmentFromPrecedingStatement(
                        statement,
                        regexSymbol,
                        semanticModel,
                        cancellationToken,
                        out var writesRegexSymbol,
                        out var assignedPattern,
                        out var assignedOptions);
                    if (!writesRegexSymbol)
                    {
                        continue;
                    }

                    if (foundAssignment ||
                        assignedPattern == null)
                    {
                        pattern = null;
                        options = RegexOptions.None;
                        return false;
                    }

                    pattern = assignedPattern;
                    options = assignedOptions;
                    foundAssignment = true;
                }
            }

            return foundAssignment;
        }

        private static void TryGetRegexAssignmentFromPrecedingStatement(
            StatementSyntax statement,
            ISymbol regexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool writesRegexSymbol,
            out string? pattern,
            out RegexOptions options)
        {
            pattern = null;
            options = RegexOptions.None;
            writesRegexSymbol = false;

            if (TryGetRegexAssignmentFromLocalDeclaration(
                    statement,
                    regexSymbol,
                    semanticModel,
                    cancellationToken,
                    out writesRegexSymbol,
                    out pattern,
                    out options))
            {
                return;
            }

            if (TryGetRegexAssignmentFromExpressionStatement(
                    statement,
                    regexSymbol,
                    semanticModel,
                    cancellationToken,
                    out writesRegexSymbol,
                    out pattern,
                    out options))
            {
                return;
            }

            writesRegexSymbol = ContainsRegexSymbolWrite(statement, regexSymbol, semanticModel, cancellationToken);
        }

        private static bool TryGetRegexAssignmentFromLocalDeclaration(
            StatementSyntax statement,
            ISymbol regexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool writesRegexSymbol,
            out string? pattern,
            out RegexOptions options)
        {
            pattern = null;
            options = RegexOptions.None;
            writesRegexSymbol = false;
            if (statement is not LocalDeclarationStatementSyntax localDeclaration)
            {
                return false;
            }

            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);
                if (!IsSameSymbol(declaredSymbol, regexSymbol))
                {
                    continue;
                }

                writesRegexSymbol = true;
                if (localDeclaration.Declaration.Variables.Count != 1 ||
                    variable.Initializer == null ||
                    !TryGetRegexPatternFromObjectCreation(
                        variable.Initializer.Value,
                        semanticModel,
                        cancellationToken,
                        out pattern,
                        out options))
                {
                    pattern = null;
                }

                return true;
            }

            return false;
        }

        private static bool TryGetRegexAssignmentFromExpressionStatement(
            StatementSyntax statement,
            ISymbol regexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool writesRegexSymbol,
            out string? pattern,
            out RegexOptions options)
        {
            pattern = null;
            options = RegexOptions.None;
            writesRegexSymbol = false;
            if (statement is not ExpressionStatementSyntax
                {
                    Expression: AssignmentExpressionSyntax assignment
                } ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                !IsRegexSymbolReference(assignment.Left, regexSymbol, semanticModel, cancellationToken))
            {
                return false;
            }

            writesRegexSymbol = true;
            if (!TryGetRegexPatternFromObjectCreation(
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    out pattern,
                    out options))
            {
                pattern = null;
            }

            return true;
        }

        private static bool ContainsRegexSymbolWrite(
            SyntaxNode node,
            ISymbol regexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var assignment in node.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (IsRegexSymbolReference(assignment.Left, regexSymbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var argument in node.DescendantNodes().OfType<ArgumentSyntax>())
            {
                if ((argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                     argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                    IsRegexSymbolReference(argument.Expression, regexSymbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRegexSymbolReference(
            ExpressionSyntax expression,
            ISymbol regexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return IsSameSymbol(
                semanticModel.GetSymbolInfo(UnwrapExpression(expression), cancellationToken).Symbol,
                regexSymbol);
        }

        private static bool TryGetRegexOptions(
            ImmutableArray<IArgumentOperation> arguments,
            int startIndex,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RegexOptions options)
        {
            options = RegexOptions.None;
            for (var index = startIndex; index < arguments.Length; index++)
            {
                var parameterType = arguments[index].Parameter?.Type;
                if (parameterType == null ||
                    parameterType.ToDisplayString() != "System.Text.RegularExpressions.RegexOptions")
                {
                    continue;
                }

                if (!TryGetIntegralConstantValue(arguments[index].Value.Syntax as ExpressionSyntax, semanticModel, cancellationToken, out var argumentOptions))
                {
                    return false;
                }

                options |= (RegexOptions)argumentOptions;
            }

            return true;
        }

        private static string CreateInlineRegexOptionLetters(RegexOptions options)
        {
            var letters = string.Empty;
            if ((options & RegexOptions.ExplicitCapture) != 0)
            {
                letters += "n";
            }

            if ((options & RegexOptions.Singleline) != 0)
            {
                letters += "s";
            }

            if ((options & RegexOptions.IgnorePatternWhitespace) != 0)
            {
                letters += "x";
            }

            return letters;
        }

        private static bool CanEncodeRegexOptions(RegexOptions options)
        {
            const RegexOptions supportedOptions =
                RegexOptions.ExplicitCapture |
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant |
                RegexOptions.Singleline |
                RegexOptions.IgnorePatternWhitespace;

            return (options & ~supportedOptions) == 0;
        }

        private static string WrapRegexPatternWithInlineOptions(string pattern, string optionLetters)
        {
            if (optionLetters.Length == 0)
            {
                return pattern;
            }

            var bodyStart = pattern.StartsWith(@"\A", StringComparison.Ordinal)
                ? 2
                : pattern.StartsWith("^", StringComparison.Ordinal)
                    ? 1
                    : 0;
            var bodyEndTrim = EndsWithUnescapedRegexAnchor(pattern, @"\z") ||
                EndsWithUnescapedRegexAnchor(pattern, @"\Z")
                    ? 2
                    : pattern.EndsWith("$", StringComparison.Ordinal) && !IsRegexCharacterEscaped(pattern, pattern.Length - 1)
                        ? 1
                        : 0;
            var bodyEnd = pattern.Length - bodyEndTrim;
            if (bodyEnd < bodyStart)
            {
                return pattern;
            }

            return pattern.Substring(0, bodyStart) +
                "(?" +
                optionLetters +
                ":" +
                pattern.Substring(bodyStart, bodyEnd - bodyStart) +
                ")" +
                pattern.Substring(bodyEnd);
        }

        private static bool EndsWithUnescapedRegexAnchor(string value, string anchor)
        {
            return value.EndsWith(anchor, StringComparison.Ordinal) &&
                !IsRegexCharacterEscaped(value, value.Length - anchor.Length);
        }

        private static bool IsRegexCharacterEscaped(string value, int index)
        {
            var slashCount = 0;
            for (var current = index - 1; current >= 0 && value[current] == '\\'; current--)
            {
                slashCount++;
            }

            return slashCount % 2 == 1;
        }

        private static bool HasOrdinalStringComparison(
            ImmutableArray<IArgumentOperation> arguments,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var argument in arguments)
            {
                var parameterType = argument.Parameter?.Type;
                if (parameterType == null ||
                    parameterType.ToDisplayString() != "System.StringComparison")
                {
                    continue;
                }

                return TryGetIntegralConstantValue(argument.Value.Syntax as ExpressionSyntax, semanticModel, cancellationToken, out var comparison) &&
                    comparison == (int)StringComparison.Ordinal;
            }

            return false;
        }

        private static bool HasOrdinalIgnoreCaseStringComparison(
            ImmutableArray<IArgumentOperation> arguments,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var argument in arguments)
            {
                var parameterType = argument.Parameter?.Type;
                if (parameterType == null ||
                    parameterType.ToDisplayString() != "System.StringComparison")
                {
                    continue;
                }

                return TryGetIntegralConstantValue(argument.Value.Syntax as ExpressionSyntax, semanticModel, cancellationToken, out var comparison) &&
                    comparison == (int)StringComparison.OrdinalIgnoreCase;
            }

            return false;
        }

        private static bool IsStringParameter(IParameterSymbol parameter)
        {
            return parameter.Type.SpecialType == SpecialType.System_String;
        }

        private static bool IsStringComparisonParameter(IParameterSymbol parameter)
        {
            return string.Equals(parameter.Type.ToDisplayString(), "System.StringComparison", StringComparison.Ordinal);
        }

        private static string? TryGetConstantString(
            ExpressionSyntax? expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (expression == null)
            {
                return null;
            }

            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue is { HasValue: true, Value: string value })
            {
                return value;
            }

            return IsStringEmptyMemberAccess(expression, semanticModel, cancellationToken)
                ? string.Empty
                : null;
        }

        private static bool TryGetConstantStringPredicateArgument(
            ExpressionSyntax argumentExpression,
            ITypeSymbol? parameterType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out string value)
        {
            value = string.Empty;
            if (parameterType?.SpecialType == SpecialType.System_String)
            {
                var stringValue = TryGetConstantString(argumentExpression, semanticModel, cancellationToken);
                if (stringValue == null)
                {
                    return false;
                }

                value = stringValue;
                return true;
            }

            if (parameterType?.SpecialType != SpecialType.System_Char)
            {
                return false;
            }

            var constantValue = semanticModel.GetConstantValue(argumentExpression, cancellationToken);
            if (constantValue is not { HasValue: true, Value: char character })
            {
                return false;
            }

            value = character.ToString();
            return true;
        }

        private static bool IsStringExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(UnwrapExpression(expression), cancellationToken);
            return (typeInfo.ConvertedType ?? typeInfo.Type)?.SpecialType == SpecialType.System_String;
        }

        private static bool TryGetIntegralConstantValue(
            ExpressionSyntax? expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out long value)
        {
            value = default;
            if (expression == null)
            {
                return false;
            }

            var constantValue = semanticModel.GetConstantValue(UnwrapExpression(expression), cancellationToken);
            return constantValue.HasValue &&
                constantValue.Value != null &&
                TryGetIntegralConstant(constantValue.Value, out value);
        }

    }
}
