using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    internal static class SymbolicTranslatorCompatibility
    {
        internal static bool TryCollectDomainFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            var originalCount = formulas.Count;
            expression = UnwrapExpression(expression);

            foreach (var memberAccess in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
            {
                if (!IsKnownNonNegativeIntegralMemberAccess(memberAccess, semanticModel, cancellationToken) ||
                    !SymbolicReachabilityService.TryTranslateValue(
                        memberAccess,
                        semanticModel,
                        cancellationToken,
                        out var lengthFormula,
                        getSymbolVersion) ||
                    lengthFormula.Kind != SmtValueKind.Int)
                {
                    continue;
                }

                formulas.Add(SmtFormulaFactory.CreateIntegerGreaterThanOrEqualZero(lengthFormula));
            }

            foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation)
                {
                    AddKnownStringInvocationDomainFacts(
                        invocationOperation,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                }
            }

            return formulas.Count > originalCount;
        }

        internal static bool TryCollectBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            return LegacyFormulaCompatibility.TryCollectBranchAssumptions(
                expression,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        internal static bool TryCollectPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            return LegacyFormulaCompatibility.TryCollectPatternBindingFacts(
                matchedValue,
                matchedValueType,
                pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        internal static bool TryTranslatePatternLegacy(
            SmtFormula value,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            ITypeSymbol? valueType = null,
            int inlineDepth = 0)
        {
            return LegacyFormulaCompatibility.TryTranslatePattern(
                value,
                pattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                valueType,
                inlineDepth);
        }

        internal static bool TryTranslateConditionLegacy(
            ExpressionSyntax condition,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return LegacyFormulaCompatibility.TryTranslateCondition(
                condition,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryTranslateValueLegacy(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return LegacyFormulaCompatibility.TryTranslateValue(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        private static void AddKnownStringInvocationDomainFacts(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var method = invocationOperation.TargetMethod;
            if (method.Name == "IsMatch" &&
                IsRegexType(method.ContainingType) &&
                TryGetRegexInputExpression(invocationOperation, out var regexInputExpression))
            {
                AddStringNonNullDomainFact(
                    regexInputExpression,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
                return;
            }

            if (method.ReturnType.SpecialType != SpecialType.System_Boolean ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                method.IsStatic ||
                method.Name is not "Contains" and not "StartsWith" and not "EndsWith")
            {
                return;
            }

            if (invocationOperation.Instance?.Syntax is ExpressionSyntax receiverExpression)
            {
                AddStringNonNullDomainFact(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }

            if (method.Parameters.Length >= 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                invocationOperation.Arguments.Length >= 1 &&
                invocationOperation.Arguments[0].Value.Syntax is ExpressionSyntax searchExpression)
            {
                AddStringNonNullDomainFact(
                    searchExpression,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static bool TryGetRegexInputExpression(
            IInvocationOperation invocationOperation,
            out ExpressionSyntax inputExpression)
        {
            inputExpression = null!;
            var method = invocationOperation.TargetMethod;
            if (method.Name != "IsMatch" ||
                !IsRegexType(method.ContainingType))
            {
                return false;
            }

            if (method.Parameters.Length < 1 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                invocationOperation.Arguments.Length < 1 ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax matchedInputExpression)
            {
                return false;
            }

            inputExpression = matchedInputExpression;
            return true;
        }

        private static void AddStringNonNullDomainFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (TryCreateStringNonNullFormula(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var nonNullFormula,
                    getSymbolVersion) &&
                nonNullFormula != null)
            {
                formulas.Add(nonNullFormula);
            }
        }

        private static bool TryCreateStringNonNullFormula(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion);
            if (SymbolicIrLowerer.TryLowerStringNonNullCondition(expression, context, out var condition) &&
                SymbolicIrFormulaEncoder.TryEncode(condition, out var encoded))
            {
                formula = encoded;
                return true;
            }

            formula = null;
            return false;
        }

        private static bool IsKnownNonNegativeIntegralMemberAccess(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return IsBuiltInNonNegativeLengthAccess(memberAccess, semanticModel, cancellationToken) ||
                IsKnownNonNegativeCollectionCountAccess(memberAccess, semanticModel, cancellationToken);
        }

        private static bool IsBuiltInNonNegativeLengthAccess(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (memberAccess.Name.Identifier.ValueText != "Length")
            {
                return false;
            }

            var memberSymbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol;
            if (memberSymbol is not IPropertySymbol and not IFieldSymbol)
            {
                return false;
            }

            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            return IsSupportedBuiltInLengthReceiver(receiverType);
        }

        private static bool IsKnownNonNegativeCollectionCountAccess(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (memberAccess.Name.Identifier.ValueText != "Count" ||
                semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol is not IPropertySymbol
                {
                    IsStatic: false,
                    Parameters.Length: 0,
                    Type.SpecialType: SpecialType.System_Int32
                } propertySymbol)
            {
                return false;
            }

            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            return IsKnownNonNegativeCollectionCountProperty(propertySymbol, receiverType, semanticModel.Compilation);
        }

        private static bool IsKnownNonNegativeCollectionCountProperty(
            IPropertySymbol propertySymbol,
            ITypeSymbol? receiverType,
            Compilation compilation)
        {
            if (receiverType == null)
            {
                return false;
            }

            foreach (var interfaceType in EnumerateKnownNonNegativeCountInterfaces(receiverType, compilation))
            {
                foreach (var interfaceCount in interfaceType.GetMembers("Count").OfType<IPropertySymbol>())
                {
                    if (interfaceCount is not
                        {
                            IsStatic: false,
                            Parameters.Length: 0,
                            Type.SpecialType: SpecialType.System_Int32
                        })
                    {
                        continue;
                    }

                    if (SymbolEqualityComparer.Default.Equals(propertySymbol, interfaceCount))
                    {
                        return true;
                    }

                    if (receiverType is INamedTypeSymbol namedReceiver &&
                        namedReceiver.FindImplementationForInterfaceMember(interfaceCount) is { } implementation &&
                        implementation.DeclaringSyntaxReferences.Length == 0 &&
                        SymbolEqualityComparer.Default.Equals(propertySymbol, implementation))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateKnownNonNegativeCountInterfaces(
            ITypeSymbol receiverType,
            Compilation compilation)
        {
            if (receiverType is INamedTypeSymbol namedReceiver &&
                IsKnownNonNegativeCountInterface(namedReceiver, compilation))
            {
                yield return namedReceiver;
            }

            foreach (var interfaceType in receiverType.AllInterfaces)
            {
                if (IsKnownNonNegativeCountInterface(interfaceType, compilation))
                {
                    yield return interfaceType;
                }
            }
        }

        private static bool IsKnownNonNegativeCountInterface(INamedTypeSymbol typeSymbol, Compilation compilation)
        {
            return IsSameOriginalType(typeSymbol, compilation.GetTypeByMetadataName("System.Collections.ICollection")) ||
                IsSameOriginalType(typeSymbol, compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1")) ||
                IsSameOriginalType(typeSymbol, compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyCollection`1"));
        }

        private static bool IsSameOriginalType(INamedTypeSymbol candidate, INamedTypeSymbol? target)
        {
            return target != null &&
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, target);
        }

        private static bool IsSupportedBuiltInLengthReceiver(ITypeSymbol? type)
        {
            if (type == null)
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_String ||
                type is IArrayTypeSymbol)
            {
                return true;
            }

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

        private static bool IsRegexType(INamedTypeSymbol? type)
        {
            return string.Equals(
                type?.ToDisplayString(),
                "System.Text.RegularExpressions.Regex",
                StringComparison.Ordinal);
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesizedExpression:
                        expression = parenthesizedExpression.Expression;
                        continue;
                    case CastExpressionSyntax castExpression:
                        expression = castExpression.Expression;
                        continue;
                    case CheckedExpressionSyntax checkedExpression
                        when checkedExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CheckedExpression) ||
                             checkedExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.UncheckedExpression):
                        expression = checkedExpression.Expression;
                        continue;
                    default:
                        return expression;
                }
            }
        }
    }
}
