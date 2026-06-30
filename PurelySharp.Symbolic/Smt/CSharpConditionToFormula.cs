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
    public static class CSharpConditionToFormula
    {
        private const int MaxSourcePredicateInlineDepth = 4;
        private const string ImplicitThisVariableName = "this";
        private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<SourceBooleanFormulaCacheKey, SourceBooleanFormulaCacheEntry>> s_sourceBooleanFormulaCache = new();
        private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<ExpressionFormulaCacheKey, SourceBooleanFormulaCacheEntry>> s_expressionFormulaCache = new();

        public readonly struct NullableSmtValueParts
        {
            public NullableSmtValueParts(SmtFormula hasValue, SmtFormula? value)
            {
                HasValue = hasValue;
                Value = value;
            }

            public SmtFormula HasValue { get; }

            public SmtFormula? Value { get; }
        }

        private readonly struct SourceBooleanFormulaCacheEntry
        {
            public SourceBooleanFormulaCacheEntry(bool success, SmtFormula? formula)
            {
                Success = success;
                Formula = formula;
            }

            public bool Success { get; }

            public SmtFormula? Formula { get; }
        }

        private readonly struct SourceBooleanFormulaCacheKey : IEquatable<SourceBooleanFormulaCacheKey>
        {
            private readonly string _kind;
            private readonly string _filePath;
            private readonly int _spanStart;
            private readonly int _spanLength;
            private readonly int _inlineDepth;

            public SourceBooleanFormulaCacheKey(
                string kind,
                string filePath,
                int spanStart,
                int spanLength,
                int inlineDepth)
            {
                _kind = kind;
                _filePath = filePath;
                _spanStart = spanStart;
                _spanLength = spanLength;
                _inlineDepth = inlineDepth;
            }

            public bool Equals(SourceBooleanFormulaCacheKey other)
            {
                return string.Equals(_kind, other._kind, StringComparison.Ordinal) &&
                    string.Equals(_filePath, other._filePath, StringComparison.Ordinal) &&
                    _spanStart == other._spanStart &&
                    _spanLength == other._spanLength &&
                    _inlineDepth == other._inlineDepth;
            }

            public override bool Equals(object? obj)
            {
                return obj is SourceBooleanFormulaCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_kind);
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_filePath);
                    hash = (hash * 31) + _spanStart;
                    hash = (hash * 31) + _spanLength;
                    hash = (hash * 31) + _inlineDepth;
                    return hash;
                }
            }
        }

        private readonly struct ExpressionFormulaCacheKey : IEquatable<ExpressionFormulaCacheKey>
        {
            private readonly string _kind;
            private readonly SyntaxTree _syntaxTree;
            private readonly int _spanStart;
            private readonly int _spanLength;
            private readonly int _inlineDepth;
            private readonly string _symbolVersionKey;

            public ExpressionFormulaCacheKey(
                string kind,
                SyntaxTree syntaxTree,
                int spanStart,
                int spanLength,
                int inlineDepth,
                string symbolVersionKey)
            {
                _kind = kind;
                _syntaxTree = syntaxTree;
                _spanStart = spanStart;
                _spanLength = spanLength;
                _inlineDepth = inlineDepth;
                _symbolVersionKey = symbolVersionKey;
            }

            public bool Equals(ExpressionFormulaCacheKey other)
            {
                return string.Equals(_kind, other._kind, StringComparison.Ordinal) &&
                    ReferenceEquals(_syntaxTree, other._syntaxTree) &&
                    _spanStart == other._spanStart &&
                    _spanLength == other._spanLength &&
                    _inlineDepth == other._inlineDepth &&
                    string.Equals(_symbolVersionKey, other._symbolVersionKey, StringComparison.Ordinal);
            }

            public override bool Equals(object? obj)
            {
                return obj is ExpressionFormulaCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_kind);
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(_syntaxTree);
                    hash = (hash * 31) + _spanStart;
                    hash = (hash * 31) + _spanLength;
                    hash = (hash * 31) + _inlineDepth;
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_symbolVersionKey);
                    return hash;
                }
            }
        }

        private readonly struct IndexExpressionShape
        {
            public IndexExpressionShape(ExpressionSyntax valueExpression, bool fromEnd, bool requiresNonNegativeValue)
            {
                ValueExpression = valueExpression;
                FromEnd = fromEnd;
                RequiresNonNegativeValue = requiresNonNegativeValue;
            }

            public ExpressionSyntax ValueExpression { get; }

            public bool FromEnd { get; }

            public bool RequiresNonNegativeValue { get; }
        }

        private readonly struct RangeExpressionShape
        {
            public RangeExpressionShape(
                bool hasStart,
                IndexExpressionShape start,
                bool hasEnd,
                IndexExpressionShape end)
            {
                HasStart = hasStart;
                Start = start;
                HasEnd = hasEnd;
                End = end;
            }

            public bool HasStart { get; }

            public IndexExpressionShape Start { get; }

            public bool HasEnd { get; }

            public IndexExpressionShape End { get; }
        }

        public static bool TryTranslate(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return TryTranslateCached(
                "condition",
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                TryTranslateCore);
        }

        private static bool TryTranslateCore(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return TryTranslateCore(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                nonZeroDivisors: null);
        }

        private static bool TryTranslateCore(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string>? nonZeroDivisors)
        {
            expression = UnwrapExpression(expression);

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue && constantValue.Value is bool booleanValue)
            {
                formula = new SmtBooleanConstant(booleanValue);
                return true;
            }

            if (TryTranslateValue(expression, semanticModel, cancellationToken, out var directValue, getSymbolVersion, inlineDepth) &&
                directValue is { Kind: SmtValueKind.Bool })
            {
                formula = directValue;
                return true;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                TryTranslateCore(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth, nonZeroDivisors) &&
                operand != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, operand);
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslateValue(conditionalExpression, semanticModel, cancellationToken, out var conditionalValue, getSymbolVersion, inlineDepth) &&
                conditionalValue is { Kind: SmtValueKind.Bool })
            {
                formula = conditionalValue;
                return true;
            }

            if (expression is InvocationExpressionSyntax invocationExpression)
            {
                if (TryTranslateKnownStringBooleanInvocation(invocationExpression, semanticModel, cancellationToken, out var knownStringInvocationFormula, getSymbolVersion, inlineDepth) &&
                    knownStringInvocationFormula != null)
                {
                    formula = knownStringInvocationFormula;
                    return true;
                }

                if (TryTranslateSourceBooleanInvocation(invocationExpression, semanticModel, cancellationToken, out var invocationFormula, getSymbolVersion, inlineDepth) &&
                    invocationFormula != null)
                {
                    formula = invocationFormula;
                    return true;
                }
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.IsExpression) &&
                    binaryExpression.Right is TypeSyntax typeSyntax &&
                    TryTranslateNonNullTypeTestCondition(
                        binaryExpression.Left,
                        typeSyntax,
                        semanticModel,
                        cancellationToken,
                        out var typeTestFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    typeTestFormula != null)
                {
                    formula = typeTestFormula;
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                    TryTranslateCore(binaryExpression.Left, semanticModel, cancellationToken, out var leftAnd, getSymbolVersion, inlineDepth, nonZeroDivisors) &&
                    leftAnd != null)
                {
                    var rightNonZeroDivisors = AddNonZeroDivisorFacts(
                        binaryExpression.Left,
                        branchWhenTrue: true,
                        semanticModel,
                        cancellationToken,
                        nonZeroDivisors,
                        getSymbolVersion,
                        inlineDepth);
                    if (TryTranslateCore(binaryExpression.Right, semanticModel, cancellationToken, out var rightAnd, getSymbolVersion, inlineDepth, rightNonZeroDivisors) &&
                        rightAnd != null)
                    {
                        formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftAnd, rightAnd);
                        return true;
                    }
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                    TryTranslateCore(binaryExpression.Left, semanticModel, cancellationToken, out var leftOr, getSymbolVersion, inlineDepth, nonZeroDivisors) &&
                    leftOr != null)
                {
                    var rightNonZeroDivisors = AddNonZeroDivisorFacts(
                        binaryExpression.Left,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        nonZeroDivisors,
                        getSymbolVersion,
                        inlineDepth);
                    if (TryTranslateCore(binaryExpression.Right, semanticModel, cancellationToken, out var rightOr, getSymbolVersion, inlineDepth, rightNonZeroDivisors) &&
                        rightOr != null)
                    {
                        formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftOr, rightOr);
                        return true;
                    }
                }

                if (TryTranslateUnsignedCastBoundsComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var unsignedBoundsFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    unsignedBoundsFormula != null)
                {
                    formula = unsignedBoundsFormula;
                    return true;
                }

                if (TryTranslateNullableNullComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var nullableNullComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    nullableNullComparison != null)
                {
                    formula = nullableNullComparison;
                    return true;
                }

                if (TryTranslateNullableValueComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var nullableValueComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    nullableValueComparison != null)
                {
                    formula = nullableValueComparison;
                    return true;
                }

                if (IsTupleEqualityComparison(binaryExpression, semanticModel, cancellationToken))
                {
                    return TryTranslateTupleEqualityComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out formula,
                        getSymbolVersion,
                        inlineDepth,
                        nonZeroDivisors) &&
                        formula != null;
                }

                if (TryTranslateStringComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var stringComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    stringComparison != null)
                {
                    formula = stringComparison;
                    return true;
                }

                if (TryTranslateStringIndexOfComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var stringIndexOfComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    stringIndexOfComparison != null)
                {
                    formula = stringIndexOfComparison;
                    return true;
                }

                if (TryTranslateValueWithSafeDivisors(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue, getSymbolVersion, inlineDepth, nonZeroDivisors) &&
                    TryTranslateValueWithSafeDivisors(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue, getSymbolVersion, inlineDepth, nonZeroDivisors) &&
                    leftValue != null &&
                    rightValue != null &&
                    TryTranslateComparison(binaryExpression.Kind(), leftValue, rightValue, out var comparison))
                {
                    formula = comparison;
                    return true;
                }
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression &&
                TryTranslatePatternExpression(isPatternExpression, semanticModel, cancellationToken, out var patternFormula, getSymbolVersion, inlineDepth))
            {
                formula = patternFormula;
                return true;
            }

            formula = null;
            return false;
        }

        private static bool TryTranslateNullableValueComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!IsSupportedNullableValueComparison(binaryExpression.Kind()))
            {
                return false;
            }

            var leftIsNullable = TryTranslateNullableValueParts(
                binaryExpression.Left,
                semanticModel,
                cancellationToken,
                out var leftHasValue,
                out var leftNullableValue,
                getSymbolVersion,
                inlineDepth);
            var rightIsNullable = TryTranslateNullableValueParts(
                binaryExpression.Right,
                semanticModel,
                cancellationToken,
                out var rightHasValue,
                out var rightNullableValue,
                getSymbolVersion,
                inlineDepth);

            if (leftIsNullable &&
                rightIsNullable &&
                leftNullableValue != null &&
                rightNullableValue != null &&
                TryTranslateComparison(binaryExpression.Kind(), leftNullableValue, rightNullableValue, out var nullableComparison) &&
                nullableComparison != null)
            {
                formula = CreateLiftedNullableComparison(
                    binaryExpression.Kind(),
                    leftHasValue,
                    rightHasValue,
                    nullableComparison);
                return true;
            }

            if (leftIsNullable &&
                leftNullableValue != null &&
                TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue, getSymbolVersion, inlineDepth) &&
                rightValue != null &&
                TryTranslateComparison(binaryExpression.Kind(), leftNullableValue, rightValue, out var leftComparison) &&
                leftComparison != null)
            {
                formula = CreateLiftedNullableComparison(binaryExpression.Kind(), leftHasValue, leftComparison);
                return true;
            }

            if (rightIsNullable &&
                rightNullableValue != null &&
                TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue, getSymbolVersion, inlineDepth) &&
                leftValue != null &&
                TryTranslateComparison(binaryExpression.Kind(), leftValue, rightNullableValue, out var rightComparison) &&
                rightComparison != null)
            {
                formula = CreateLiftedNullableComparison(binaryExpression.Kind(), rightHasValue, rightComparison);
                return true;
            }

            return false;
        }

        private static bool IsSupportedNullableValueComparison(SyntaxKind kind)
        {
            return kind is SyntaxKind.EqualsExpression or
                SyntaxKind.NotEqualsExpression or
                SyntaxKind.LessThanExpression or
                SyntaxKind.LessThanOrEqualExpression or
                SyntaxKind.GreaterThanExpression or
                SyntaxKind.GreaterThanOrEqualExpression;
        }

        private static SmtFormula CreateLiftedNullableComparison(
            SyntaxKind kind,
            SmtFormula hasValue,
            SmtFormula comparison)
        {
            return kind == SyntaxKind.NotEqualsExpression
                ? new SmtBinaryFormula(
                    SmtBinaryOperator.Or,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, hasValue),
                    comparison)
                : new SmtBinaryFormula(SmtBinaryOperator.And, hasValue, comparison);
        }

        private static SmtFormula CreateLiftedNullableComparison(
            SyntaxKind kind,
            SmtFormula leftHasValue,
            SmtFormula rightHasValue,
            SmtFormula comparison)
        {
            var bothHaveValue = new SmtBinaryFormula(SmtBinaryOperator.And, leftHasValue, rightHasValue);
            if (kind == SyntaxKind.EqualsExpression)
            {
                var neitherHasValue = new SmtBinaryFormula(
                    SmtBinaryOperator.And,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, leftHasValue),
                    new SmtUnaryFormula(SmtUnaryOperator.Not, rightHasValue));
                return new SmtBinaryFormula(
                    SmtBinaryOperator.Or,
                    neitherHasValue,
                    new SmtBinaryFormula(SmtBinaryOperator.And, bothHaveValue, comparison));
            }

            if (kind == SyntaxKind.NotEqualsExpression)
            {
                return new SmtUnaryFormula(
                    SmtUnaryOperator.Not,
                    CreateLiftedNullableComparison(SyntaxKind.EqualsExpression, leftHasValue, rightHasValue, comparison));
            }

            return new SmtBinaryFormula(SmtBinaryOperator.And, bothHaveValue, comparison);
        }

        private static bool TryTranslateNullableNullComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
                !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression))
            {
                return false;
            }

            var comparesNotNull = binaryExpression.IsKind(SyntaxKind.NotEqualsExpression);
            if (TryTranslateNullableValueParts(
                    binaryExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var leftHasValue,
                    out _,
                    getSymbolVersion,
                    inlineDepth) &&
                IsNullLikeNullableComparisonOperand(binaryExpression.Right, semanticModel, cancellationToken))
            {
                formula = comparesNotNull
                    ? leftHasValue
                    : new SmtUnaryFormula(SmtUnaryOperator.Not, leftHasValue);
                return true;
            }

            if (TryTranslateNullableValueParts(
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightHasValue,
                    out _,
                    getSymbolVersion,
                    inlineDepth) &&
                IsNullLikeNullableComparisonOperand(binaryExpression.Left, semanticModel, cancellationToken))
            {
                formula = comparesNotNull
                    ? rightHasValue
                    : new SmtUnaryFormula(SmtUnaryOperator.Not, rightHasValue);
                return true;
            }

            return false;
        }

        private static bool TryTranslateNonNullTypeTestCondition(
            ExpressionSyntax expression,
            TypeSyntax typeSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryTranslateValue(expression, semanticModel, cancellationToken, out var value, getSymbolVersion, inlineDepth) ||
                value is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            var testedType = semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
            if (IsTypeTestEquivalentToNonNull(expression, typeSyntax, semanticModel, cancellationToken))
            {
                formula = CreateNonNullFormula(value);
                return true;
            }

            if (!TryCreateRuntimeTypeTestFormula(value, testedType, out var runtimeTypeTest))
            {
                return false;
            }

            formula = Conjoin(CreateNonNullFormula(value), runtimeTypeTest);
            return true;
        }

        private static bool IsTypeTestEquivalentToNonNull(
            ExpressionSyntax expression,
            TypeSyntax typeSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var expressionTypeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var expressionType = expressionTypeInfo.ConvertedType ?? expressionTypeInfo.Type;
            var testedType = semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
            return IsTypeKnownAssignableTo(expressionType, testedType);
        }

        private static bool IsTypeKnownAssignableTo(ITypeSymbol? sourceType, ITypeSymbol? targetType)
        {
            if (sourceType == null ||
                targetType == null ||
                !sourceType.IsReferenceType ||
                !targetType.IsReferenceType)
            {
                return false;
            }

            if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
            {
                return true;
            }

            if (targetType.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            if (sourceType is INamedTypeSymbol sourceNamedType)
            {
                for (var current = sourceNamedType.BaseType; current != null; current = current.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(current, targetType))
                    {
                        return true;
                    }
                }
            }

            return sourceType.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, targetType));
        }

        public static bool TryCreateRuntimeTypeTestFormula(
            SmtFormula value,
            ITypeSymbol? targetType,
            out SmtFormula formula)
        {
            formula = null!;
            if (value.Kind != SmtValueKind.Reference ||
                !CanUseRuntimeTypeTest(targetType))
            {
                return false;
            }

            formula = new SmtRuntimeTypeTestFormula(value, GetRuntimeTypeTestKey(targetType!));
            return true;
        }

        public static bool TryCreateAsExpressionAssignmentFacts(
            ExpressionSyntax valueExpression,
            SmtFormula targetFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ImmutableArray<SmtFormula> facts,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            facts = ImmutableArray<SmtFormula>.Empty;
            valueExpression = UnwrapExpression(valueExpression);
            if (valueExpression is not BinaryExpressionSyntax asExpression ||
                !asExpression.IsKind(SyntaxKind.AsExpression) ||
                asExpression.Right is not TypeSyntax typeSyntax ||
                targetFormula.Kind != SmtValueKind.Reference ||
                !TryTranslateValue(
                    asExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var sourceFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                sourceFormula is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            var targetType = semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
            if (!TryCreateRuntimeTypeTestFormula(sourceFormula, targetType, out var runtimeTypeTest))
            {
                return false;
            }

            var targetIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, new SmtNullConstant());
            var targetNonNull = CreateNonNullFormula(targetFormula);
            var sourceNonNull = CreateNonNullFormula(sourceFormula);
            var builder = ImmutableArray.CreateBuilder<SmtFormula>(4);

            builder.Add(new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                targetIsNull,
                sourceNonNull));
            builder.Add(new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                targetIsNull,
                runtimeTypeTest));
            builder.Add(new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtUnaryFormula(SmtUnaryOperator.Not, new SmtBinaryFormula(SmtBinaryOperator.And, sourceNonNull, runtimeTypeTest)),
                targetNonNull));
            builder.Add(new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtUnaryFormula(SmtUnaryOperator.Not, new SmtBinaryFormula(
                    SmtBinaryOperator.And,
                    sourceNonNull,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, runtimeTypeTest))),
                targetIsNull));

            facts = builder.MoveToImmutable();
            return true;
        }

        private static bool CanUseRuntimeTypeTest(ITypeSymbol? targetType)
        {
            if (targetType == null ||
                targetType.TypeKind is TypeKind.Dynamic or TypeKind.Error or TypeKind.TypeParameter)
            {
                return false;
            }

            return targetType.IsReferenceType;
        }

        private static string GetRuntimeTypeTestKey(ITypeSymbol targetType)
        {
            return targetType
                .WithNullableAnnotation(NullableAnnotation.None)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
        }

        private static SmtFormula CreateNonNullFormula(SmtFormula value)
        {
            return new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtNullConstant());
        }

        private static bool IsNullLikeNullableComparisonOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (semanticModel.GetConstantValue(expression, cancellationToken) is { HasValue: true, Value: null })
            {
                return true;
            }

            if (!expression.IsKind(SyntaxKind.DefaultLiteralExpression) &&
                expression is not DefaultExpressionSyntax)
            {
                return false;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return TryGetNullableUnderlyingType(typeInfo.ConvertedType ?? typeInfo.Type, out _);
        }

        private static bool TryTranslateUnsignedCastBoundsComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!binaryExpression.IsKind(SyntaxKind.LessThanExpression) &&
                !binaryExpression.IsKind(SyntaxKind.GreaterThanOrEqualExpression))
            {
                return false;
            }

            if (!TryCreateUnsignedCastBoundsInRangeFormula(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            formula = binaryExpression.IsKind(SyntaxKind.LessThanExpression)
                ? inRangeFormula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula);
            return true;
        }

        private static bool TryCreateUnsignedCastBoundsInRangeFormula(
            ExpressionSyntax leftExpression,
            ExpressionSyntax rightExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            if (!TryGetUnsignedCastOperand(leftExpression, semanticModel, cancellationToken, out var indexExpression, out var leftUnsignedType) ||
                !TryGetUnsignedCastOperand(rightExpression, semanticModel, cancellationToken, out var lengthExpression, out var rightUnsignedType) ||
                leftUnsignedType != rightUnsignedType ||
                !IsKnownNonNegativeIntegralExpression(lengthExpression, semanticModel, cancellationToken) ||
                !TryTranslateValue(indexExpression, semanticModel, cancellationToken, out var indexFormula, getSymbolVersion, inlineDepth) ||
                indexFormula is not { Kind: SmtValueKind.Int } ||
                !TryTranslateValue(lengthExpression, semanticModel, cancellationToken, out var lengthFormula, getSymbolVersion, inlineDepth) ||
                lengthFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var lowerBound = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                indexFormula,
                new SmtIntegerConstant(0));
            var upperBound = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                indexFormula,
                lengthFormula);
            formula = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
            return true;
        }

        private static bool TryGetUnsignedCastOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax operand,
            out SpecialType unsignedType)
        {
            expression = UnwrapExpression(expression);
            if (expression is not CastExpressionSyntax castExpression)
            {
                operand = null!;
                unsignedType = SpecialType.None;
                return false;
            }

            var castType = semanticModel.GetTypeInfo(castExpression.Type, cancellationToken).Type;
            if (castType?.SpecialType is not SpecialType.System_UInt32 and not SpecialType.System_UInt64)
            {
                operand = null!;
                unsignedType = SpecialType.None;
                return false;
            }

            operand = castExpression.Expression;
            unsignedType = castType.SpecialType;
            return true;
        }

        private static bool IsKnownNonNegativeIntegralExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue &&
                TryGetIntegralConstant(constantValue.Value!, out var integralValue))
            {
                return integralValue >= 0;
            }

            return expression is MemberAccessExpressionSyntax memberAccess &&
                IsBuiltInNonNegativeLengthAccess(memberAccess, semanticModel, cancellationToken);
        }

        private static bool TryTranslateSourceBooleanInvocation(
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
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                !IsSupportedOrdinalStringEqualsInvocation(invocationOperation, semanticModel, cancellationToken))
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
                if (invocationOperation.Arguments.Length != 1 ||
                    invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax instanceInputExpression)
                {
                    return false;
                }

                inputExpression = instanceInputExpression;
                if (!TryGetRegexPatternFromReceiver(invocationExpression, semanticModel, cancellationToken, out pattern, out options))
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
                !IsSupportedOrdinalStringIndexOfInvocation(invocationOperation, semanticModel, cancellationToken) ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                invocationOperation.Arguments.Length < 1 ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax searchExpression ||
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

            formula = new SmtStringContainsFormula(receiverFormula, searchFormula);
            return true;
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

        private static bool CanInlineSourceBooleanPredicate(IMethodSymbol methodSymbol)
        {
            return methodSymbol is
            {
                ReturnsVoid: false,
                ReturnsByRef: false,
                ReturnsByRefReadonly: false,
                ReturnType.SpecialType: SpecialType.System_Boolean,
                DeclaringSyntaxReferences.Length: > 0
            } &&
                methodSymbol.Parameters.All(static parameter => parameter.RefKind == RefKind.None);
        }

        private static bool TryGetReturnedBooleanFormula(
            IMethodSymbol methodSymbol,
            Compilation compilation,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;

            var callableSyntax = methodSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .FirstOrDefault();
            if (callableSyntax == null)
            {
                return false;
            }

            var cache = GetSourceBooleanFormulaCache(compilation);
            var cacheKey = CreateSourceBooleanFormulaCacheKey("method", callableSyntax, inlineDepth);
            var entry = cache.GetOrAdd(
                cacheKey,
                _ =>
                {
                    var returnedSemanticModel = compilation.GetSemanticModel(callableSyntax.SyntaxTree);
                    var success = TryTranslateReturnedBooleanSyntax(
                        callableSyntax,
                        returnedSemanticModel,
                        cancellationToken,
                        inlineDepth,
                        out var cachedFormula);
                    return new SourceBooleanFormulaCacheEntry(success, cachedFormula);
                });

            formula = entry.Formula;
            return entry.Success;
        }

        private static bool TryTranslateReturnedBooleanSyntax(
            SyntaxNode callableSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            switch (callableSyntax)
            {
                case MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.ExpressionBody?.Expression != null:
                    return TryTranslate(
                        methodDeclarationSyntax.ExpressionBody.Expression,
                        semanticModel,
                        cancellationToken,
                        out formula,
                        getSymbolVersion: null,
                        inlineDepth);
                case MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.Body != null:
                    return TryTranslateReturnedBooleanBlock(
                        methodDeclarationSyntax.Body,
                        semanticModel,
                        cancellationToken,
                        inlineDepth,
                        out formula);
                case LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.ExpressionBody?.Expression != null:
                    return TryTranslate(
                        localFunctionStatementSyntax.ExpressionBody.Expression,
                        semanticModel,
                        cancellationToken,
                        out formula,
                        getSymbolVersion: null,
                        inlineDepth);
                case LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.Body != null:
                    return TryTranslateReturnedBooleanBlock(
                        localFunctionStatementSyntax.Body,
                        semanticModel,
                        cancellationToken,
                        inlineDepth,
                        out formula);
                default:
                    formula = null;
                    return false;
            }
        }

        private static ConcurrentDictionary<SourceBooleanFormulaCacheKey, SourceBooleanFormulaCacheEntry> GetSourceBooleanFormulaCache(
            Compilation compilation)
        {
            return s_sourceBooleanFormulaCache.GetValue(
                compilation,
                static _ => new ConcurrentDictionary<SourceBooleanFormulaCacheKey, SourceBooleanFormulaCacheEntry>());
        }

        private static SourceBooleanFormulaCacheKey CreateSourceBooleanFormulaCacheKey(
            string kind,
            SyntaxNode syntax,
            int inlineDepth)
        {
            var filePath = syntax.SyntaxTree.FilePath ?? string.Empty;
            return new SourceBooleanFormulaCacheKey(kind, filePath, syntax.SpanStart, syntax.Span.Length, inlineDepth);
        }

        private static bool TryTranslateReturnedBooleanBlock(
            BlockSyntax bodySyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            var substitutions = new List<SmtVariableSubstitution>();
            var localVariableNames = new HashSet<string>(StringComparer.Ordinal);
            var statementIndex = 0;
            if (!TryCollectLeadingLocalSubstitutions(
                    bodySyntax.Statements,
                    ref statementIndex,
                    semanticModel,
                    cancellationToken,
                    inlineDepth,
                    substitutions,
                    localVariableNames))
            {
                return false;
            }

            return TryTranslateReturnedBooleanStatements(
                bodySyntax.Statements,
                statementIndex,
                substitutions,
                semanticModel,
                cancellationToken,
                inlineDepth,
                out formula) &&
                formula != null &&
                !FormulaReferencesAnyVariableName(formula, localVariableNames);
        }

        private static bool TryTranslateReturnedBooleanStatements(
            SyntaxList<StatementSyntax> statements,
            int statementIndex,
            IReadOnlyList<SmtVariableSubstitution> substitutions,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            var remainingStatementCount = statements.Count - statementIndex;
            if (remainingStatementCount == 1)
            {
                if (statements[statementIndex] is ReturnStatementSyntax returnStatement &&
                    returnStatement.Expression != null)
                {
                    if (!TryTranslate(
                        returnStatement.Expression,
                        semanticModel,
                        cancellationToken,
                        out formula,
                        getSymbolVersion: null,
                        inlineDepth))
                    {
                        return false;
                    }

                    formula = SubstituteVariables(formula!, substitutions);
                    return true;
                }

                if (statements[statementIndex] is IfStatementSyntax ifStatement &&
                    ifStatement.Else?.Statement != null &&
                    TryGetSingleReturnExpression(ifStatement.Statement, out var trueReturn) &&
                    TryGetSingleReturnExpression(ifStatement.Else.Statement, out var falseReturn))
                {
                    return TryTranslateReturnedBooleanConditional(
                        ifStatement.Condition,
                        trueReturn,
                        falseReturn,
                        semanticModel,
                        cancellationToken,
                        inlineDepth,
                        substitutions,
                        out formula);
                }

                if (statements[statementIndex] is SwitchStatementSyntax switchStatement)
                {
                    return TryTranslateReturnedBooleanSwitchStatement(
                        switchStatement,
                        fallbackReturnExpression: null,
                        requireFinalDefaultSection: true,
                        substitutions,
                        semanticModel,
                        cancellationToken,
                        inlineDepth,
                        out formula);
                }
            }

            if (remainingStatementCount >= 2 &&
                TryTranslateGuardReturnChain(
                    statements,
                    statementIndex,
                    substitutions,
                    semanticModel,
                    cancellationToken,
                    inlineDepth,
                    out formula))
            {
                return true;
            }

            if (remainingStatementCount == 2 &&
                statements[statementIndex] is SwitchStatementSyntax switchWithFallback &&
                statements[statementIndex + 1] is ReturnStatementSyntax fallbackReturn &&
                fallbackReturn.Expression != null)
            {
                return TryTranslateReturnedBooleanSwitchStatement(
                    switchWithFallback,
                    fallbackReturn.Expression,
                    requireFinalDefaultSection: false,
                    substitutions,
                    semanticModel,
                    cancellationToken,
                    inlineDepth,
                    out formula);
            }

            return false;
        }

        private static bool TryTranslateReturnedBooleanSwitchStatement(
            SwitchStatementSyntax switchStatement,
            ExpressionSyntax? fallbackReturnExpression,
            bool requireFinalDefaultSection,
            IReadOnlyList<SmtVariableSubstitution> substitutions,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            if (switchStatement.Sections.Count == 0)
            {
                return false;
            }

            if (requireFinalDefaultSection)
            {
                if (switchStatement.Sections.Count < 2 ||
                    !HasDefaultLabel(switchStatement.Sections[switchStatement.Sections.Count - 1]))
                {
                    return false;
                }
            }
            else if (switchStatement.Sections.Any(HasDefaultLabel))
            {
                return false;
            }

            var sectionConditions = new List<SmtFormula>();
            var sectionValues = new List<SmtFormula>();
            foreach (var section in switchStatement.Sections)
            {
                if (!TryGetSwitchSectionReturnExpression(section, out var returnExpression) ||
                    !SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                        switchStatement.Expression,
                        section,
                        semanticModel,
                        cancellationToken,
                        out var sectionCondition) ||
                    !TryTranslate(returnExpression, semanticModel, cancellationToken, out var sectionValue, getSymbolVersion: null, inlineDepth) ||
                    sectionValue is not { Kind: SmtValueKind.Bool })
                {
                    formula = null;
                    return false;
                }

                sectionConditions.Add(sectionCondition);
                sectionValues.Add(sectionValue);
            }

            SmtFormula result;
            var startIndex = sectionValues.Count - 1;
            if (fallbackReturnExpression != null)
            {
                if (!TryTranslate(
                        fallbackReturnExpression,
                        semanticModel,
                        cancellationToken,
                        out var fallbackValue,
                        getSymbolVersion: null,
                        inlineDepth) ||
                    fallbackValue is not { Kind: SmtValueKind.Bool })
                {
                    formula = null;
                    return false;
                }

                result = fallbackValue;
            }
            else
            {
                result = sectionValues[sectionValues.Count - 1];
                startIndex--;
            }

            for (var index = startIndex; index >= 0; index--)
            {
                result = new SmtConditionalFormula(sectionConditions[index], sectionValues[index], result, SmtValueKind.Bool);
            }

            formula = SubstituteVariables(result, substitutions);
            return true;
        }

        private static bool TryGetSwitchSectionReturnExpression(
            SwitchSectionSyntax section,
            out ExpressionSyntax returnExpression)
        {
            returnExpression = null!;
            if (section.Statements.Count != 1 ||
                !TryGetSingleReturnExpression(section.Statements[0], out returnExpression))
            {
                return false;
            }

            return true;
        }

        private static bool HasDefaultLabel(SwitchSectionSyntax section)
        {
            return section.Labels.Any(static label => label is DefaultSwitchLabelSyntax);
        }

        private static bool TryTranslateGuardReturnChain(
            SyntaxList<StatementSyntax> statements,
            int statementIndex,
            IReadOnlyList<SmtVariableSubstitution> substitutions,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            if (statements[statements.Count - 1] is not ReturnStatementSyntax finalReturnStatement ||
                finalReturnStatement.Expression == null)
            {
                return false;
            }

            var guards = new List<(ExpressionSyntax Condition, ExpressionSyntax ReturnExpression)>();
            for (var index = statementIndex; index < statements.Count - 1; index++)
            {
                if (statements[index] is not IfStatementSyntax guard ||
                    guard.Else != null ||
                    !TryGetSingleReturnExpression(guard.Statement, out var guardReturn))
                {
                    return false;
                }

                guards.Add((guard.Condition, guardReturn));
            }

            if (guards.Count == 0 ||
                !TryTranslate(
                    finalReturnStatement.Expression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion: null,
                    inlineDepth) ||
                formula is not { Kind: SmtValueKind.Bool })
            {
                formula = null;
                return false;
            }

            for (var index = guards.Count - 1; index >= 0; index--)
            {
                var guard = guards[index];
                if (!TryTranslate(guard.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion: null, inlineDepth) ||
                    conditionFormula is not { Kind: SmtValueKind.Bool } ||
                    !TryTranslate(guard.ReturnExpression, semanticModel, cancellationToken, out var guardReturnFormula, getSymbolVersion: null, inlineDepth) ||
                    guardReturnFormula is not { Kind: SmtValueKind.Bool })
                {
                    formula = null;
                    return false;
                }

                formula = new SmtConditionalFormula(conditionFormula, guardReturnFormula, formula, SmtValueKind.Bool);
            }

            formula = SubstituteVariables(formula, substitutions);
            return true;
        }

        private static bool TryCollectLeadingLocalSubstitutions(
            SyntaxList<StatementSyntax> statements,
            ref int statementIndex,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            IList<SmtVariableSubstitution> substitutions,
            ISet<string> localVariableNames)
        {
            while (statementIndex < statements.Count)
            {
                switch (statements[statementIndex])
                {
                    case LocalDeclarationStatementSyntax localDeclaration:
                        if (!TryCollectLocalDeclarationSubstitutions(
                                localDeclaration,
                                semanticModel,
                                cancellationToken,
                                inlineDepth,
                                substitutions,
                                localVariableNames))
                        {
                            return false;
                        }

                        statementIndex++;
                        continue;
                    case ExpressionStatementSyntax expressionStatement
                        when expressionStatement.Expression is AssignmentExpressionSyntax assignment &&
                            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                        if (!TryCollectLocalAssignmentSubstitution(
                                assignment,
                                semanticModel,
                                cancellationToken,
                                inlineDepth,
                                substitutions,
                                localVariableNames))
                        {
                            return false;
                        }

                        statementIndex++;
                        continue;
                    default:
                        return true;
                }
            }

            return true;
        }

        private static bool TryCollectLocalDeclarationSubstitutions(
            LocalDeclarationStatementSyntax localDeclaration,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            IList<SmtVariableSubstitution> substitutions,
            ISet<string> localVariableNames)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not ILocalSymbol localSymbol ||
                    !TryGetValueKind(localSymbol.Type, out var localKind))
                {
                    return false;
                }

                var localVariableName = GetVariableName(localSymbol, getSymbolVersion: null);
                localVariableNames.Add(localVariableName);
                if (variable.Initializer == null)
                {
                    continue;
                }

                if (!TryTranslateValue(
                        variable.Initializer.Value,
                        semanticModel,
                        cancellationToken,
                        out var initializerFormula,
                        getSymbolVersion: null,
                        inlineDepth) ||
                    initializerFormula == null ||
                    initializerFormula.Kind != localKind)
                {
                    return false;
                }

                var replacement = SubstituteVariables(initializerFormula, substitutions.ToArray());
                SetLocalSubstitution(localVariableName, localKind, replacement, substitutions);
            }

            return true;
        }

        private static bool TryCollectLocalAssignmentSubstitution(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            IList<SmtVariableSubstitution> substitutions,
            ISet<string> localVariableNames)
        {
            var left = UnwrapExpression(assignment.Left);
            if (semanticModel.GetSymbolInfo(left, cancellationToken).Symbol is not ILocalSymbol localSymbol ||
                !TryGetValueKind(localSymbol.Type, out var localKind))
            {
                return false;
            }

            var localVariableName = GetVariableName(localSymbol, getSymbolVersion: null);
            if (!localVariableNames.Contains(localVariableName) ||
                !TryTranslateValue(
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    out var assignedFormula,
                    getSymbolVersion: null,
                    inlineDepth) ||
                assignedFormula == null ||
                assignedFormula.Kind != localKind)
            {
                return false;
            }

            var replacement = SubstituteVariables(assignedFormula, substitutions.ToArray());
            SetLocalSubstitution(localVariableName, localKind, replacement, substitutions);
            return true;
        }

        private static void SetLocalSubstitution(
            string localVariableName,
            SmtValueKind localKind,
            SmtFormula replacement,
            IList<SmtVariableSubstitution> substitutions)
        {
            for (var index = substitutions.Count - 1; index >= 0; index--)
            {
                if (string.Equals(substitutions[index].ExactName, localVariableName, StringComparison.Ordinal))
                {
                    substitutions.RemoveAt(index);
                }
            }

            var localVariable = new SmtVariable(localVariableName, localKind);
            substitutions.Add(new SmtVariableSubstitution(
                localVariable.Name,
                localVariable.Name + ".",
                GetFormulaMemberName(localVariable) + ".",
                replacement));
        }

        private static bool TryTranslateReturnedBooleanConditional(
            ExpressionSyntax condition,
            ExpressionSyntax whenTrue,
            ExpressionSyntax whenFalse,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            IReadOnlyList<SmtVariableSubstitution> substitutions,
            out SmtFormula? formula)
        {
            formula = null;
            if (!TryTranslate(condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion: null, inlineDepth) ||
                conditionFormula is not { Kind: SmtValueKind.Bool } ||
                !TryTranslate(whenTrue, semanticModel, cancellationToken, out var whenTrueFormula, getSymbolVersion: null, inlineDepth) ||
                whenTrueFormula is not { Kind: SmtValueKind.Bool } ||
                !TryTranslate(whenFalse, semanticModel, cancellationToken, out var whenFalseFormula, getSymbolVersion: null, inlineDepth) ||
                whenFalseFormula is not { Kind: SmtValueKind.Bool })
            {
                return false;
            }

            formula = SubstituteVariables(
                new SmtConditionalFormula(conditionFormula, whenTrueFormula, whenFalseFormula, SmtValueKind.Bool),
                substitutions);
            return true;
        }

        private static bool TryGetSingleReturnExpression(
            StatementSyntax statement,
            out ExpressionSyntax expression)
        {
            if (statement is ReturnStatementSyntax returnStatement &&
                returnStatement.Expression != null)
            {
                expression = returnStatement.Expression;
                return true;
            }

            if (statement is BlockSyntax block &&
                block.Statements.Count == 1 &&
                block.Statements[0] is ReturnStatementSyntax blockReturnStatement &&
                blockReturnStatement.Expression != null)
            {
                expression = blockReturnStatement.Expression;
                return true;
            }

            expression = null!;
            return false;
        }

        private static bool TryCreateSourcePredicateSubstitutions(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            out IReadOnlyList<SmtVariableSubstitution> substitutions)
        {
            var builder = new List<SmtVariableSubstitution>(invocationOperation.Arguments.Length + 1);
            if (!invocationOperation.TargetMethod.IsStatic)
            {
                if (invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                    !TryTranslateValue(
                        receiverExpression,
                        semanticModel,
                        cancellationToken,
                        out var receiverFormula,
                        getSymbolVersion,
                        inlineDepth) ||
                    receiverFormula is not { Kind: SmtValueKind.Reference })
                {
                    substitutions = Array.Empty<SmtVariableSubstitution>();
                    return false;
                }

                builder.Add(CreateImplicitThisSubstitution(receiverFormula));
            }

            foreach (var argument in invocationOperation.Arguments)
            {
                var parameter = argument.Parameter;
                if (parameter == null ||
                    !TryGetValueKind(parameter.Type, out var parameterKind) ||
                    argument.Value.Syntax is not ExpressionSyntax argumentExpression)
                {
                    substitutions = Array.Empty<SmtVariableSubstitution>();
                    return false;
                }

                if (!TryTranslateValue(argumentExpression, semanticModel, cancellationToken, out var argumentFormula, getSymbolVersion, inlineDepth) ||
                    argumentFormula == null ||
                    argumentFormula.Kind != parameterKind)
                {
                    substitutions = Array.Empty<SmtVariableSubstitution>();
                    return false;
                }

                var formalVariable = new SmtVariable(GetVariableName(parameter, getSymbolVersion: null), parameterKind);
                builder.Add(new SmtVariableSubstitution(
                    formalVariable.Name,
                    formalVariable.Name + ".",
                    GetFormulaMemberName(formalVariable) + ".",
                    argumentFormula));

                if (parameter.Type.SpecialType == SpecialType.System_String &&
                    TryTranslateStringValue(argumentExpression, semanticModel, cancellationToken, out var argumentStringFormula, getSymbolVersion, inlineDepth) &&
                    argumentStringFormula != null)
                {
                    var formalStringVariable = new SmtVariable(formalVariable.Name + ".String", SmtValueKind.String);
                    builder.Add(new SmtVariableSubstitution(
                        formalStringVariable.Name,
                        formalStringVariable.Name + ".",
                        GetFormulaMemberName(formalStringVariable) + ".",
                        argumentStringFormula));
                }
            }

            substitutions = builder;
            return true;
        }

        private static SmtVariableSubstitution CreateImplicitThisSubstitution(SmtFormula receiver)
        {
            return new SmtVariableSubstitution(
                ImplicitThisVariableName,
                ImplicitThisVariableName + ".",
                GetFormulaMemberName(new SmtVariable(ImplicitThisVariableName, SmtValueKind.Reference)) + ".",
                receiver);
        }

        private static SmtFormula SubstituteVariables(
            SmtFormula formula,
            IReadOnlyList<SmtVariableSubstitution> substitutions)
        {
            switch (formula)
            {
                case SmtVariable variable:
                    return SubstituteVariable(variable, substitutions);
                case SmtUnaryFormula unary:
                    return new SmtUnaryFormula(unary.Operator, SubstituteVariables(unary.Operand, substitutions));
                case SmtBinaryFormula binary:
                    return new SmtBinaryFormula(
                        binary.Operator,
                        SubstituteVariables(binary.Left, substitutions),
                        SubstituteVariables(binary.Right, substitutions));
                case SmtIntegerUnaryTerm unary:
                    return new SmtIntegerUnaryTerm(unary.Operator, SubstituteVariables(unary.Operand, substitutions));
                case SmtIntegerBinaryTerm binary:
                    return new SmtIntegerBinaryTerm(
                        binary.Operator,
                        SubstituteVariables(binary.Left, substitutions),
                        SubstituteVariables(binary.Right, substitutions));
                case SmtStringLengthTerm stringLength:
                    return new SmtStringLengthTerm(SubstituteVariables(stringLength.Value, substitutions));
                case SmtStringConcatTerm stringConcat:
                    return new SmtStringConcatTerm(
                        SubstituteVariables(stringConcat.Left, substitutions),
                        SubstituteVariables(stringConcat.Right, substitutions));
                case SmtStringContainsFormula stringContains:
                    return new SmtStringContainsFormula(
                        SubstituteVariables(stringContains.Value, substitutions),
                        SubstituteVariables(stringContains.Search, substitutions));
                case SmtStringStartsWithFormula stringStartsWith:
                    return new SmtStringStartsWithFormula(
                        SubstituteVariables(stringStartsWith.Value, substitutions),
                        SubstituteVariables(stringStartsWith.Prefix, substitutions));
                case SmtStringEndsWithFormula stringEndsWith:
                    return new SmtStringEndsWithFormula(
                        SubstituteVariables(stringEndsWith.Value, substitutions),
                        SubstituteVariables(stringEndsWith.Suffix, substitutions));
                case SmtRegexMatchFormula regexMatch:
                    return new SmtRegexMatchFormula(
                        SubstituteVariables(regexMatch.Value, substitutions),
                        regexMatch.Pattern,
                        regexMatch.Options);
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    return new SmtRuntimeTypeTestFormula(
                        SubstituteVariables(runtimeTypeTest.Value, substitutions),
                        runtimeTypeTest.TypeKey);
                case SmtConditionalFormula conditional:
                    return new SmtConditionalFormula(
                        SubstituteVariables(conditional.Condition, substitutions),
                        SubstituteVariables(conditional.WhenTrue, substitutions),
                        SubstituteVariables(conditional.WhenFalse, substitutions),
                        conditional.ResultKind);
                default:
                    return formula;
            }
        }

        private static SmtFormula SubstituteVariable(
            SmtVariable variable,
            IReadOnlyList<SmtVariableSubstitution> substitutions)
        {
            foreach (var substitution in substitutions)
            {
                if (string.Equals(variable.Name, substitution.ExactName, StringComparison.Ordinal))
                {
                    return substitution.Replacement;
                }
            }

            foreach (var substitution in substitutions)
            {
                if (TrySubstituteMemberVariable(variable, substitution.SimpleMemberPrefix, substitution.Replacement, out var simpleMemberReplacement) ||
                    TrySubstituteMemberVariable(variable, substitution.FormulaMemberPrefix, substitution.Replacement, out simpleMemberReplacement))
                {
                    return simpleMemberReplacement;
                }

                var renderedReceiver = substitution.FormulaMemberPrefix.TrimEnd('.');
                if (renderedReceiver.Length > 0 &&
                    variable.Name.Contains(renderedReceiver, StringComparison.Ordinal))
                {
                    return new SmtVariable(
                        variable.Name.Replace(renderedReceiver, substitution.Replacement.ToString()),
                        variable.Kind);
                }
            }

            return variable;
        }

        private static bool TrySubstituteMemberVariable(
            SmtVariable variable,
            string memberPrefix,
            SmtFormula replacement,
            out SmtFormula substituted)
        {
            if (!variable.Name.StartsWith(memberPrefix, StringComparison.Ordinal))
            {
                substituted = null!;
                return false;
            }

            var suffix = variable.Name.Substring(memberPrefix.Length - 1);
            substituted = new SmtVariable(GetFormulaMemberName(replacement) + suffix, variable.Kind);
            return true;
        }

        private static string GetFormulaMemberName(SmtFormula formula)
        {
            return formula is SmtVariable variable
                ? variable.Name
                : formula.ToString() ?? string.Empty;
        }

        private static bool FormulaReferencesAnyVariableName(
            SmtFormula formula,
            ISet<string> variableNames)
        {
            if (variableNames.Count == 0)
            {
                return false;
            }

            switch (formula)
            {
                case SmtVariable variable:
                    return variableNames.Any(name =>
                        string.Equals(variable.Name, name, StringComparison.Ordinal) ||
                        variable.Name.StartsWith(name + ".", StringComparison.Ordinal));
                case SmtUnaryFormula unary:
                    return FormulaReferencesAnyVariableName(unary.Operand, variableNames);
                case SmtBinaryFormula binary:
                    return FormulaReferencesAnyVariableName(binary.Left, variableNames) ||
                        FormulaReferencesAnyVariableName(binary.Right, variableNames);
                case SmtIntegerUnaryTerm unary:
                    return FormulaReferencesAnyVariableName(unary.Operand, variableNames);
                case SmtIntegerBinaryTerm binary:
                    return FormulaReferencesAnyVariableName(binary.Left, variableNames) ||
                        FormulaReferencesAnyVariableName(binary.Right, variableNames);
                case SmtStringLengthTerm stringLength:
                    return FormulaReferencesAnyVariableName(stringLength.Value, variableNames);
                case SmtStringConcatTerm stringConcat:
                    return FormulaReferencesAnyVariableName(stringConcat.Left, variableNames) ||
                        FormulaReferencesAnyVariableName(stringConcat.Right, variableNames);
                case SmtStringContainsFormula stringContains:
                    return FormulaReferencesAnyVariableName(stringContains.Value, variableNames) ||
                        FormulaReferencesAnyVariableName(stringContains.Search, variableNames);
                case SmtStringStartsWithFormula stringStartsWith:
                    return FormulaReferencesAnyVariableName(stringStartsWith.Value, variableNames) ||
                        FormulaReferencesAnyVariableName(stringStartsWith.Prefix, variableNames);
                case SmtStringEndsWithFormula stringEndsWith:
                    return FormulaReferencesAnyVariableName(stringEndsWith.Value, variableNames) ||
                        FormulaReferencesAnyVariableName(stringEndsWith.Suffix, variableNames);
                case SmtRegexMatchFormula regexMatch:
                    return FormulaReferencesAnyVariableName(regexMatch.Value, variableNames);
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    return FormulaReferencesAnyVariableName(runtimeTypeTest.Value, variableNames);
                case SmtConditionalFormula conditional:
                    return FormulaReferencesAnyVariableName(conditional.Condition, variableNames) ||
                        FormulaReferencesAnyVariableName(conditional.WhenTrue, variableNames) ||
                        FormulaReferencesAnyVariableName(conditional.WhenFalse, variableNames);
                default:
                    return false;
            }
        }

        private sealed class SmtVariableSubstitution
        {
            public SmtVariableSubstitution(
                string exactName,
                string simpleMemberPrefix,
                string formulaMemberPrefix,
                SmtFormula replacement)
            {
                ExactName = exactName;
                SimpleMemberPrefix = simpleMemberPrefix;
                FormulaMemberPrefix = formulaMemberPrefix;
                Replacement = replacement;
            }

            public string ExactName { get; }

            public string SimpleMemberPrefix { get; }

            public string FormulaMemberPrefix { get; }

            public SmtFormula Replacement { get; }
        }

        public static bool TryGetKnownStringLength(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out int length)
        {
            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue is { HasValue: true, Value: string stringValue })
            {
                length = stringValue.Length;
                return true;
            }

            if (IsStringEmptyMemberAccess(expression, semanticModel, cancellationToken))
            {
                length = 0;
                return true;
            }

            length = default;
            return false;
        }

        private static bool IsStringEmptyMemberAccess(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "Empty" &&
                semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IFieldSymbol
                {
                    IsStatic: true,
                    ContainingType.SpecialType: SpecialType.System_String
                };
        }

        public static bool TryTranslateStringValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            expression = UnwrapExpression(expression);
            formula = null;

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue is { HasValue: true, Value: string stringValue })
            {
                formula = new SmtStringConstant(stringValue);
                return true;
            }

            if (IsStringEmptyMemberAccess(expression, semanticModel, cancellationToken))
            {
                formula = new SmtStringConstant(string.Empty);
                return true;
            }

            if (TryTranslateImplicitThisStringMemberValue(expression, semanticModel, cancellationToken, out formula) &&
                formula != null)
            {
                return true;
            }

            if (expression is CastExpressionSyntax stringCastExpression &&
                TryTranslateNonUserDefinedReferenceCastOperand(
                    stringCastExpression,
                    semanticModel,
                    cancellationToken,
                    out var castReference,
                    out var castTargetType,
                    getSymbolVersion,
                    inlineDepth) &&
                castTargetType.SpecialType == SpecialType.System_String)
            {
                formula = CreateStringValueFormulaForReference(castReference);
                return true;
            }

            if (expression is BinaryExpressionSyntax stringAsExpression &&
                stringAsExpression.IsKind(SyntaxKind.AsExpression) &&
                stringAsExpression.Right is TypeSyntax stringAsType &&
                IsIdentityPreservingReferenceConversion(stringAsExpression.Left, stringAsType, semanticModel, cancellationToken) &&
                TryTranslateValue(stringAsExpression.Left, semanticModel, cancellationToken, out var asReference, getSymbolVersion, inlineDepth) &&
                asReference is { Kind: SmtValueKind.Reference })
            {
                formula = CreateStringValueFormulaForReference(asReference);
                return true;
            }

            if (expression is ConditionalAccessExpressionSyntax conditionalAccessExpression &&
                TryTranslateConditionalAccessStringValue(
                    conditionalAccessExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion, inlineDepth) &&
                conditionFormula != null &&
                TryTranslateStringValue(conditionalExpression.WhenTrue, semanticModel, cancellationToken, out var whenTrue, getSymbolVersion, inlineDepth) &&
                whenTrue != null &&
                TryTranslateStringValue(conditionalExpression.WhenFalse, semanticModel, cancellationToken, out var whenFalse, getSymbolVersion, inlineDepth) &&
                whenFalse != null)
            {
                formula = new SmtConditionalFormula(conditionFormula, whenTrue, whenFalse, SmtValueKind.String);
                return true;
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryTranslateValue(coalesceExpression.Left, semanticModel, cancellationToken, out var coalesceReference, getSymbolVersion, inlineDepth) &&
                coalesceReference is { Kind: SmtValueKind.Reference } &&
                TryTranslateStringValue(coalesceExpression.Left, semanticModel, cancellationToken, out var coalesceLeft, getSymbolVersion, inlineDepth) &&
                coalesceLeft != null &&
                TryTranslateStringValue(coalesceExpression.Right, semanticModel, cancellationToken, out var coalesceRight, getSymbolVersion, inlineDepth) &&
                coalesceRight != null)
            {
                formula = new SmtConditionalFormula(
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, coalesceReference, new SmtNullConstant()),
                    coalesceLeft,
                    coalesceRight,
                    SmtValueKind.String);
                return true;
            }

            if (expression is BinaryExpressionSyntax addExpression &&
                addExpression.IsKind(SyntaxKind.AddExpression) &&
                IsStringExpression(expression, semanticModel, cancellationToken) &&
                TryTranslateStringConcatOperand(addExpression.Left, semanticModel, cancellationToken, out var concatLeft, getSymbolVersion, inlineDepth) &&
                concatLeft != null &&
                TryTranslateStringConcatOperand(addExpression.Right, semanticModel, cancellationToken, out var concatRight, getSymbolVersion, inlineDepth) &&
                concatRight != null)
            {
                formula = new SmtStringConcatTerm(concatLeft, concatRight);
                return true;
            }

            if (expression is InvocationExpressionSyntax invocationExpression &&
                TryTranslateStringConcatInvocation(
                    invocationExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (expression is InterpolatedStringExpressionSyntax interpolatedStringExpression &&
                TryTranslateInterpolatedStringValue(
                    interpolatedStringExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type?.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is ILocalSymbol or IParameterSymbol)
            {
                formula = new SmtVariable(GetVariableName(symbol.OriginalDefinition, getSymbolVersion) + ".String", SmtValueKind.String);
                return true;
            }

            if (expression is MemberAccessExpressionSyntax memberAccess &&
                semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol is IPropertySymbol or IFieldSymbol &&
                TryTranslateValue(memberAccess.Expression, semanticModel, cancellationToken, out var receiver, getSymbolVersion, inlineDepth) &&
                receiver != null)
            {
                var receiverName = receiver is SmtVariable receiverVariable ? receiverVariable.Name : receiver.ToString();
                if (semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol is IFieldSymbol fieldSymbol &&
                    TryGetTupleElementStorageName(memberAccess, fieldSymbol, semanticModel, cancellationToken, out var storageName))
                {
                    formula = new SmtVariable(receiverName + "." + storageName + ".String", SmtValueKind.String);
                    return true;
                }

                formula = new SmtVariable(receiverName + "." + memberAccess.Name.Identifier.ValueText + ".String", SmtValueKind.String);
                return true;
            }

            return false;
        }

        private static bool TryTranslateStringConcatInvocation(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                !IsSupportedStringConcatInvocation(invocationOperation.TargetMethod))
            {
                return false;
            }

            var arguments = invocationExpression.ArgumentList.Arguments;
            if (arguments.Count == 0)
            {
                return false;
            }

            var parts = new List<SmtFormula>(arguments.Count);
            foreach (var argument in arguments)
            {
                if (argument.NameColon != null ||
                    !TryTranslateStringConcatOperand(
                        argument.Expression,
                        semanticModel,
                        cancellationToken,
                        out var part,
                        getSymbolVersion,
                        inlineDepth) ||
                    part == null)
                {
                    return false;
                }

                parts.Add(part);
            }

            formula = CreateStringConcatTerm(parts);
            return true;
        }

        private static bool IsSupportedStringConcatInvocation(IMethodSymbol method)
        {
            if (!method.IsStatic ||
                method.Name != "Concat" ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                method.ReturnType.SpecialType != SpecialType.System_String ||
                method.Parameters.Length == 0)
            {
                return false;
            }

            foreach (var parameter in method.Parameters)
            {
                if (parameter.Type.SpecialType == SpecialType.System_String)
                {
                    continue;
                }

                if (parameter.IsParams &&
                    parameter.Type is IArrayTypeSymbol
                    {
                        ElementType.SpecialType: SpecialType.System_String
                    })
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool TryTranslateInterpolatedStringValue(
            InterpolatedStringExpressionSyntax interpolatedStringExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!IsStringExpression(interpolatedStringExpression, semanticModel, cancellationToken))
            {
                return false;
            }

            var parts = new List<SmtFormula>(interpolatedStringExpression.Contents.Count);
            foreach (var content in interpolatedStringExpression.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    parts.Add(new SmtStringConstant(text.TextToken.ValueText));
                    continue;
                }

                if (content is not InterpolationSyntax interpolation ||
                    interpolation.AlignmentClause != null ||
                    interpolation.FormatClause != null ||
                    !TryTranslateStringConcatOperand(
                        interpolation.Expression,
                        semanticModel,
                        cancellationToken,
                        out var part,
                        getSymbolVersion,
                        inlineDepth) ||
                    part == null)
                {
                    return false;
                }

                parts.Add(part);
            }

            formula = CreateStringConcatTerm(parts);
            return true;
        }

        private static SmtFormula CreateStringConcatTerm(IReadOnlyList<SmtFormula> parts)
        {
            if (parts.Count == 0)
            {
                return new SmtStringConstant(string.Empty);
            }

            var formula = parts[0];
            for (var index = 1; index < parts.Count; index++)
            {
                formula = new SmtStringConcatTerm(formula, parts[index]);
            }

            return formula;
        }

        private static bool TryTranslateConditionalAccessStringValue(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var resultTypeInfo = semanticModel.GetTypeInfo(conditionalAccess, cancellationToken);
            var resultType = resultTypeInfo.ConvertedType ?? resultTypeInfo.Type;
            if (resultType?.SpecialType != SpecialType.System_String ||
                !TryTranslateValue(
                    conditionalAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                receiverFormula is not { Kind: SmtValueKind.Reference } ||
                !TryCreateConditionalAccessWhenNotNullValueFormula(
                    conditionalAccess,
                    receiverFormula,
                    resultType,
                    semanticModel,
                    cancellationToken,
                    out var whenNotNullReference,
                    getSymbolVersion,
                    inlineDepth) ||
                whenNotNullReference is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formula = new SmtConditionalFormula(
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, receiverFormula, new SmtNullConstant()),
                CreateStringValueFormulaForReference(whenNotNullReference),
                CreateConditionalAccessNullBranchStringFormula(receiverFormula),
                SmtValueKind.String);
            return true;
        }

        private static SmtFormula CreateStringValueFormulaForReference(SmtFormula referenceFormula)
        {
            var referenceName = referenceFormula is SmtVariable variable
                ? variable.Name
                : referenceFormula.ToString();
            return new SmtVariable(referenceName + ".String", SmtValueKind.String);
        }

        private static SmtFormula CreateConditionalAccessNullBranchStringFormula(SmtFormula receiverFormula)
        {
            var receiverName = receiverFormula is SmtVariable variable
                ? variable.Name
                : receiverFormula.ToString();
            return new SmtVariable(receiverName + "?.String", SmtValueKind.String);
        }

        private static bool TryTranslateStringConcatOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryTranslateStringValue(expression, semanticModel, cancellationToken, out var stringFormula, getSymbolVersion, inlineDepth) ||
                stringFormula == null)
            {
                return false;
            }

            if (TryTranslateValue(expression, semanticModel, cancellationToken, out var referenceFormula, getSymbolVersion, inlineDepth) &&
                referenceFormula is { Kind: SmtValueKind.Reference })
            {
                formula = new SmtConditionalFormula(
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, referenceFormula, new SmtNullConstant()),
                    stringFormula,
                    new SmtStringConstant(string.Empty),
                    SmtValueKind.String);
                return true;
            }

            formula = stringFormula;
            return true;
        }

        private static bool TryTranslateImplicitThisStringMemberValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (expression is not IdentifierNameSyntax ||
                semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not IPropertySymbol and not IFieldSymbol ||
                semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not { IsStatic: false } memberSymbol ||
                !TryGetMemberType(memberSymbol, out var memberType) ||
                memberType.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            var receiverName = GetFormulaMemberName(new SmtVariable(ImplicitThisVariableName, SmtValueKind.Reference));
            formula = new SmtVariable(
                receiverName + "." + memberSymbol.Name + ".String",
                SmtValueKind.String);
            return true;
        }

        private static bool TryTranslateStringComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
                !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression))
            {
                return false;
            }

            if (!TryTranslateStringValue(binaryExpression.Left, semanticModel, cancellationToken, out var left, getSymbolVersion, inlineDepth) ||
                left == null ||
                !TryTranslateStringValue(binaryExpression.Right, semanticModel, cancellationToken, out var right, getSymbolVersion, inlineDepth) ||
                right == null)
            {
                return false;
            }

            var equality = CreateNullSafeStringEqualityFormula(
                binaryExpression.Left,
                binaryExpression.Right,
                left,
                right,
                semanticModel,
                cancellationToken,
                getSymbolVersion,
                inlineDepth);

            formula = binaryExpression.IsKind(SyntaxKind.EqualsExpression)
                ? equality
                : new SmtUnaryFormula(SmtUnaryOperator.Not, equality);
            return true;
        }

        private static SmtFormula CreateNullSafeStringEqualityFormula(
            ExpressionSyntax leftExpression,
            ExpressionSyntax rightExpression,
            SmtFormula left,
            SmtFormula right,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            var valuesEqual = new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);
            if (!TryCreateStringNonNullFormula(leftExpression, semanticModel, cancellationToken, out var leftNonNull, getSymbolVersion, inlineDepth) ||
                leftNonNull == null ||
                !TryCreateStringNonNullFormula(rightExpression, semanticModel, cancellationToken, out var rightNonNull, getSymbolVersion, inlineDepth) ||
                rightNonNull == null)
            {
                return valuesEqual;
            }

            var bothNull = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                new SmtUnaryFormula(SmtUnaryOperator.Not, leftNonNull),
                new SmtUnaryFormula(SmtUnaryOperator.Not, rightNonNull));
            var bothNonNull = new SmtBinaryFormula(SmtBinaryOperator.And, leftNonNull, rightNonNull);
            return new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                bothNull,
                new SmtBinaryFormula(SmtBinaryOperator.And, bothNonNull, valuesEqual));
        }

        private static bool IsTupleEqualityComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
                !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression))
            {
                return false;
            }

            return semanticModel.GetOperation(binaryExpression, cancellationToken) is ITupleBinaryOperation tupleOperation &&
                tupleOperation.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals;
        }

        private static bool TryTranslateTupleEqualityComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string>? nonZeroDivisors)
        {
            formula = null;
            if (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
                !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression))
            {
                return false;
            }

            if (!TryGetTupleEqualityElementFields(
                    binaryExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var leftFields) ||
                !TryGetTupleEqualityElementFields(
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightFields) ||
                leftFields.Length == 0 ||
                leftFields.Length != rightFields.Length ||
                !TryTranslateTupleElementValues(
                    binaryExpression.Left,
                    leftFields,
                    semanticModel,
                    cancellationToken,
                    out var leftValues,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) ||
                !TryTranslateTupleElementValues(
                    binaryExpression.Right,
                    rightFields,
                    semanticModel,
                    cancellationToken,
                    out var rightValues,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) ||
                leftValues.Length != rightValues.Length)
            {
                return false;
            }

            var elementEqualities = ImmutableArray.CreateBuilder<SmtFormula>(leftValues.Length);
            for (var index = 0; index < leftValues.Length; index++)
            {
                if (!TryTranslateComparison(
                        SyntaxKind.EqualsExpression,
                        leftValues[index],
                        rightValues[index],
                        out var elementEquality) ||
                    elementEquality == null)
                {
                    return false;
                }

                elementEqualities.Add(elementEquality);
            }

            var equality = CreateTupleElementConjunction(elementEqualities.ToImmutable());
            formula = binaryExpression.IsKind(SyntaxKind.EqualsExpression)
                ? equality
                : new SmtUnaryFormula(SmtUnaryOperator.Not, equality);
            return true;
        }

        private static bool TryGetTupleEqualityElementFields(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ImmutableArray<IFieldSymbol> fields)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return TryGetTupleEqualityElementFields(typeInfo.ConvertedType ?? typeInfo.Type, out fields);
        }

        private static bool TryGetTupleEqualityElementFields(
            ITypeSymbol? type,
            out ImmutableArray<IFieldSymbol> fields)
        {
            fields = ImmutableArray<IFieldSymbol>.Empty;
            if (type is not INamedTypeSymbol namedType)
            {
                return false;
            }

            if (namedType.IsTupleType)
            {
                var builder = ImmutableArray.CreateBuilder<IFieldSymbol>(namedType.TupleElements.Length);
                foreach (var field in namedType.TupleElements)
                {
                    if (!IsSupportedTupleEqualityElementField(field))
                    {
                        fields = ImmutableArray<IFieldSymbol>.Empty;
                        return false;
                    }

                    builder.Add(field);
                }

                fields = builder.ToImmutable();
                return fields.Length > 0;
            }

            if (!IsSystemValueTupleType(namedType))
            {
                return false;
            }

            var candidates = new List<(int Index, IFieldSymbol Field)>();
            foreach (var field in namedType.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsStatic)
                {
                    continue;
                }

                if (string.Equals(field.Name, "Rest", StringComparison.Ordinal))
                {
                    return false;
                }

                if (TryGetTupleElementIndex(field.Name, out var elementIndex))
                {
                    candidates.Add((elementIndex, field));
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            candidates.Sort(static (left, right) => left.Index.CompareTo(right.Index));
            var valueTupleBuilder = ImmutableArray.CreateBuilder<IFieldSymbol>(candidates.Count);
            for (var index = 0; index < candidates.Count; index++)
            {
                if (candidates[index].Index != index + 1 ||
                    !IsSupportedTupleEqualityElementField(candidates[index].Field))
                {
                    fields = ImmutableArray<IFieldSymbol>.Empty;
                    return false;
                }

                valueTupleBuilder.Add(candidates[index].Field);
            }

            fields = valueTupleBuilder.ToImmutable();
            return true;
        }

        private static bool IsSupportedTupleEqualityElementField(IFieldSymbol field)
        {
            return TryGetTupleElementStorageName(field, out _) &&
                IsSupportedTupleEqualityElementType(field.Type);
        }

        private static bool TryTranslateTupleElementValues(
            ExpressionSyntax expression,
            ImmutableArray<IFieldSymbol> fields,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ImmutableArray<SmtFormula> values,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string>? nonZeroDivisors)
        {
            expression = UnwrapExpression(expression);
            values = ImmutableArray<SmtFormula>.Empty;
            if (expression is TupleExpressionSyntax tupleExpression)
            {
                if (tupleExpression.Arguments.Count != fields.Length)
                {
                    return false;
                }

                var tupleBuilder = ImmutableArray.CreateBuilder<SmtFormula>(fields.Length);
                for (var index = 0; index < fields.Length; index++)
                {
                    if (!TryTranslateTupleElementExpressionValue(
                            tupleExpression.Arguments[index].Expression,
                            fields[index].Type,
                            semanticModel,
                            cancellationToken,
                            out var elementValue,
                            getSymbolVersion,
                            inlineDepth,
                            nonZeroDivisors) ||
                        elementValue == null)
                    {
                        values = ImmutableArray<SmtFormula>.Empty;
                        return false;
                    }

                    tupleBuilder.Add(elementValue);
                }

                values = tupleBuilder.ToImmutable();
                return true;
            }

            var tupleSymbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition;
            if (tupleSymbol is not ILocalSymbol and not IParameterSymbol)
            {
                return false;
            }

            var symbolBuilder = ImmutableArray.CreateBuilder<SmtFormula>(fields.Length);
            foreach (var field in fields)
            {
                if (!TryGetTupleElementStorageName(field, out var storageName) ||
                    !TryGetValueKind(field.Type, out var kind))
                {
                    values = ImmutableArray<SmtFormula>.Empty;
                    return false;
                }

                symbolBuilder.Add(new SmtVariable(GetVariableName(tupleSymbol, getSymbolVersion) + "." + storageName, kind));
            }

            values = symbolBuilder.ToImmutable();
            return true;
        }

        private static bool TryTranslateTupleElementExpressionValue(
            ExpressionSyntax expression,
            ITypeSymbol elementType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? value,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string>? nonZeroDivisors)
        {
            value = null;
            if (!IsSupportedTupleEqualityElementType(elementType) ||
                !TryGetValueKind(elementType, out var expectedKind) ||
                !TryTranslateValueWithSafeDivisors(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out value,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) ||
                value == null)
            {
                return false;
            }

            return value.Kind == expectedKind ||
                value is SmtNullConstant && expectedKind == SmtValueKind.Reference;
        }

        private static bool IsSupportedTupleEqualityElementType(ITypeSymbol type)
        {
            return type.SpecialType == SpecialType.System_Boolean ||
                IsIntegralOrEnumType(type) ||
                IsReferenceIdentityTupleEqualityElementType(type);
        }

        private static bool IsReferenceIdentityTupleEqualityElementType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol)
            {
                return true;
            }

            if (!type.IsReferenceType ||
                type.SpecialType == SpecialType.System_String ||
                type.TypeKind == TypeKind.Delegate ||
                type.TypeKind == TypeKind.Interface)
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            return type.TypeKind == TypeKind.Class &&
                !HasUserDefinedEqualityOperator(type);
        }

        private static bool HasUserDefinedEqualityOperator(ITypeSymbol type)
        {
            for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                if (HasUserDefinedEqualityOperator(current, "op_Equality") ||
                    HasUserDefinedEqualityOperator(current, "op_Inequality"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasUserDefinedEqualityOperator(INamedTypeSymbol type, string operatorName)
        {
            return type.GetMembers(operatorName)
                .OfType<IMethodSymbol>()
                .Any(static method => method.MethodKind == MethodKind.UserDefinedOperator);
        }

        private static bool IsSystemValueTupleType(INamedTypeSymbol type)
        {
            var originalDefinition = type.OriginalDefinition;
            return string.Equals(originalDefinition.ContainingNamespace.ToDisplayString(), "System", StringComparison.Ordinal) &&
                originalDefinition.MetadataName.StartsWith("ValueTuple`", StringComparison.Ordinal);
        }

        private static bool TryGetTupleElementIndex(string name, out int index)
        {
            if (!IsTupleElementStorageName(name) ||
                !int.TryParse(name.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out index) ||
                index <= 0)
            {
                index = 0;
                return false;
            }

            return true;
        }

        private static SmtFormula CreateTupleElementConjunction(ImmutableArray<SmtFormula> formulas)
        {
            var result = formulas[0];
            for (var index = 1; index < formulas.Length; index++)
            {
                result = new SmtBinaryFormula(SmtBinaryOperator.And, result, formulas[index]);
            }

            return result;
        }

        public static bool TryCreateStringNonNullFormula(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            expression = UnwrapExpression(expression);
            formula = null;

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue)
            {
                if (constantValue.Value is string)
                {
                    formula = new SmtBooleanConstant(true);
                    return true;
                }

                if (constantValue.Value == null)
                {
                    formula = new SmtBooleanConstant(false);
                    return true;
                }
            }

            if (IsStringEmptyMemberAccess(expression, semanticModel, cancellationToken))
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            if (expression is BinaryExpressionSyntax addExpression &&
                addExpression.IsKind(SyntaxKind.AddExpression) &&
                IsStringExpression(expression, semanticModel, cancellationToken))
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                IsStringExpression(expression, semanticModel, cancellationToken) &&
                TryCreateStringNonNullFormula(coalesceExpression.Left, semanticModel, cancellationToken, out var coalesceLeftNonNull, getSymbolVersion, inlineDepth) &&
                coalesceLeftNonNull != null &&
                TryCreateStringNonNullFormula(coalesceExpression.Right, semanticModel, cancellationToken, out var coalesceRightNonNull, getSymbolVersion, inlineDepth) &&
                coalesceRightNonNull != null)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Or, coalesceLeftNonNull, coalesceRightNonNull);
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion, inlineDepth) &&
                conditionFormula != null &&
                TryCreateStringNonNullFormula(conditionalExpression.WhenTrue, semanticModel, cancellationToken, out var whenTrueNonNull, getSymbolVersion, inlineDepth) &&
                whenTrueNonNull != null &&
                TryCreateStringNonNullFormula(conditionalExpression.WhenFalse, semanticModel, cancellationToken, out var whenFalseNonNull, getSymbolVersion, inlineDepth) &&
                whenFalseNonNull != null)
            {
                formula = new SmtConditionalFormula(conditionFormula, whenTrueNonNull, whenFalseNonNull, SmtValueKind.Bool);
                return true;
            }

            if (TryTranslateValue(expression, semanticModel, cancellationToken, out var referenceFormula, getSymbolVersion, inlineDepth) &&
                referenceFormula is { Kind: SmtValueKind.Reference })
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, referenceFormula, new SmtNullConstant());
                return true;
            }

            return false;
        }

        public static bool TryCollectBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas)
        {
            return TryCollectBranchAssumptions(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion: null);
        }

        public static bool TryCollectBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var originalCount = formulas.Count;
            TryCollectDomainFacts(expression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            AddBranchAssumptions(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
            return formulas.Count > originalCount;
        }

        public static bool TryCollectPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            var originalCount = formulas.Count;
            AddPatternBindingFacts(
                matchedValue,
                matchedValueType,
                pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
            return formulas.Count > originalCount;
        }

        public static bool TryTranslateBuiltInElementAccessInRange(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            formula = null!;
            var receiverType = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
            if (receiverType is IArrayTypeSymbol { Rank: > 1 } multidimensionalArrayType &&
                elementAccess.ArgumentList.Arguments.Count == multidimensionalArrayType.Rank)
            {
                return TryTranslateMultidimensionalArrayElementAccessInRange(
                    elementAccess,
                    multidimensionalArrayType,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            if (!IsSupportedBuiltInElementAccessReceiver(receiverType))
            {
                return false;
            }

            if (!TryCreateBuiltInElementAccessLengthFormula(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            var indexArgumentExpression = elementAccess.ArgumentList.Arguments[0].Expression;
            if (TryCreateBuiltInRangeAccessInRangeFormula(
                    indexArgumentExpression,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (TryCreateAbsRemainderIndexAccessInRangeFormula(
                    indexArgumentExpression,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (!TryResolveBuiltInIndexAccessIndexShape(
                    indexArgumentExpression,
                    semanticModel,
                    cancellationToken,
                    out var indexShape) ||
                !TryCreateEffectiveBuiltInIndexFormula(
                    indexShape,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out var indexFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            var lowerBound = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                indexFormula,
                new SmtIntegerConstant(0));
            var upperBound = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                indexFormula,
                lengthFormula);
            formula = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
            if (!TryCreateIndexShapeWellFormedFormula(
                    indexShape,
                    semanticModel,
                    cancellationToken,
                    out var indexWellFormed,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            formula = ApplyWellFormedPrecondition(indexWellFormed, formula);
            return true;
        }

        private static bool TryCreateAbsRemainderIndexAccessInRangeFormula(
            ExpressionSyntax indexExpression,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            if (indexExpression is not InvocationExpressionSyntax invocationExpression ||
                !TryGetMathAbsRemainderOperands(
                    invocationExpression,
                    semanticModel,
                    cancellationToken,
                    out _,
                    out var divisorExpression) ||
                !TryTranslateValue(
                    divisorExpression,
                    semanticModel,
                    cancellationToken,
                    out var divisorFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                divisorFormula is not { Kind: SmtValueKind.Int } ||
                !Equals(divisorFormula, lengthFormula))
            {
                return false;
            }

            formula = new SmtBooleanConstant(true);
            return true;
        }

        private static bool TryTranslateMultidimensionalArrayElementAccessInRange(
            ElementAccessExpressionSyntax elementAccess,
            IArrayTypeSymbol arrayType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            if (arrayType.Rank <= 1 ||
                elementAccess.ArgumentList.Arguments.Count != arrayType.Rank)
            {
                return false;
            }

            SmtFormula? combined = null;
            for (var dimension = 0; dimension < arrayType.Rank; dimension++)
            {
                if (!TryTranslateValue(
                        elementAccess.ArgumentList.Arguments[dimension].Expression,
                        semanticModel,
                        cancellationToken,
                        out var indexFormula,
                        getSymbolVersion,
                        inlineDepth) ||
                    indexFormula is not { Kind: SmtValueKind.Int } ||
                    !TryCreateArrayDimensionLengthFormula(
                        elementAccess.Expression,
                        dimension,
                        semanticModel,
                        cancellationToken,
                        out var lengthFormula,
                        getSymbolVersion,
                        inlineDepth) ||
                    lengthFormula is not { Kind: SmtValueKind.Int })
                {
                    return false;
                }

                var lowerBound = new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    indexFormula,
                    new SmtIntegerConstant(0));
                var upperBound = new SmtBinaryFormula(
                    SmtBinaryOperator.LessThan,
                    indexFormula,
                    lengthFormula);
                var dimensionInRange = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
                combined = combined == null
                    ? dimensionInRange
                    : new SmtBinaryFormula(SmtBinaryOperator.And, combined, dimensionInRange);
            }

            if (combined == null)
            {
                return false;
            }

            formula = combined;
            return true;
        }

        public static bool TryTranslateBuiltInLengthValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return TryCreateBuiltInElementAccessLengthFormula(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        public static bool TryTranslateArrayDimensionLengthValue(
            ExpressionSyntax expression,
            int dimension,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return TryCreateArrayDimensionLengthFormula(
                expression,
                dimension,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        public static bool TryTranslateNullableHasValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            if (TryTranslateNullableValueParts(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var parts,
                    getSymbolVersion,
                    inlineDepth))
            {
                formula = parts.HasValue;
                return true;
            }

            formula = null!;
            return false;
        }

        public static bool TryTranslateNullableValueParts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out NullableSmtValueParts parts,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            if (TryTranslateNullableValueParts(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula,
                    out var valueFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                parts = new NullableSmtValueParts(hasValueFormula, valueFormula);
                return true;
            }

            parts = default;
            return false;
        }

        public static bool TryCollectDomainFacts(
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
                if (!IsBuiltInNonNegativeLengthAccess(memberAccess, semanticModel, cancellationToken) ||
                    !TryTranslateValue(memberAccess, semanticModel, cancellationToken, out var lengthFormula, getSymbolVersion) ||
                    lengthFormula is not { Kind: SmtValueKind.Int })
                {
                    continue;
                }

                formulas.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    lengthFormula,
                    new SmtIntegerConstant(0)));
            }

            foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation)
                {
                    AddKnownStringInvocationDomainFacts(invocationOperation, semanticModel, cancellationToken, formulas, getSymbolVersion);
                }
            }

            return formulas.Count > originalCount;
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
                AddStringNonNullDomainFact(regexInputExpression, semanticModel, cancellationToken, formulas, getSymbolVersion);
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
                AddStringNonNullDomainFact(receiverExpression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            }

            if (method.Parameters.Length >= 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                invocationOperation.Arguments.Length >= 1 &&
                invocationOperation.Arguments[0].Value.Syntax is ExpressionSyntax searchExpression)
            {
                AddStringNonNullDomainFact(searchExpression, semanticModel, cancellationToken, formulas, getSymbolVersion);
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

            if (method.IsStatic)
            {
                if (method.Parameters.Length < 1 ||
                    method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                    invocationOperation.Arguments.Length < 1 ||
                    invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax staticInputExpression)
                {
                    return false;
                }

                inputExpression = staticInputExpression;
                return true;
            }

            if (method.Parameters.Length < 1 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                invocationOperation.Arguments.Length < 1 ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax instanceInputExpression)
            {
                return false;
            }

            inputExpression = instanceInputExpression;
            return true;
        }

        private static void AddStringNonNullDomainFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (TryCreateStringNonNullFormula(expression, semanticModel, cancellationToken, out var nonNullFormula, getSymbolVersion) &&
                nonNullFormula != null)
            {
                formulas.Add(nonNullFormula);
            }
        }

        private static void AddBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                AddBranchAssumptions(prefixUnary.Operand, !branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
                return;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }

                if (branchWhenTrue &&
                    binaryExpression.IsKind(SyntaxKind.BitwiseAndExpression) &&
                    HasSupportedBooleanType(binaryExpression, semanticModel, cancellationToken))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }

                if (!branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }

                if (!branchWhenTrue &&
                    binaryExpression.IsKind(SyntaxKind.BitwiseOrExpression) &&
                    HasSupportedBooleanType(binaryExpression, semanticModel, cancellationToken))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }
            }

            if (branchWhenTrue)
            {
                AddPatternBindingFacts(expression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            }

            if (branchWhenTrue &&
                TryCreateTypeTestNonNullBranchFact(expression, semanticModel, cancellationToken, out var typeTestNonNull, getSymbolVersion))
            {
                formulas.Add(typeTestNonNull);
            }

            AddNullComparisonOperandImplications(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
            AddConditionalAccessStringEqualityBranchFacts(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
            if (TryAddInlineAssignmentBranchFacts(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion))
            {
                return;
            }

            if (!TryTranslate(expression, semanticModel, cancellationToken, out var formula, getSymbolVersion) ||
                formula == null)
            {
                return;
            }

            formulas.Add(branchWhenTrue
                ? formula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, formula));
        }

        private static bool TryAddInlineAssignmentBranchFacts(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);
            if (expression is AssignmentExpressionSyntax directAssignment)
            {
                return TryAddDirectBooleanAssignmentBranchFacts(
                    directAssignment,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }

            if (expression is not BinaryExpressionSyntax binaryExpression ||
                !IsSupportedInlineAssignmentComparison(binaryExpression.Kind()))
            {
                return false;
            }

            if (TryAddInlineAssignmentComparisonBranchFacts(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    binaryExpression.Kind(),
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion,
                    rejectOtherReferencesAssignedSymbol: false))
            {
                return true;
            }

            return TryAddInlineAssignmentComparisonBranchFacts(
                binaryExpression.Right,
                binaryExpression.Left,
                ReverseComparisonKind(binaryExpression.Kind()),
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion,
                rejectOtherReferencesAssignedSymbol: true);
        }

        private static bool TryAddDirectBooleanAssignmentBranchFacts(
            AssignmentExpressionSyntax assignment,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var candidateFormulas = formulas.ToList();
            if (!TryCreateSimpleInlineAssignmentFact(
                    assignment,
                    semanticModel,
                    cancellationToken,
                    candidateFormulas,
                    getSymbolVersion,
                    out var targetFormula,
                    out _) ||
                targetFormula is not { Kind: SmtValueKind.Bool })
            {
                return false;
            }

            candidateFormulas.Add(branchWhenTrue
                ? targetFormula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, targetFormula));
            ReplaceFormulas(formulas, candidateFormulas);
            return true;
        }

        private static bool TryAddInlineAssignmentComparisonBranchFacts(
            ExpressionSyntax assignmentCandidate,
            ExpressionSyntax otherExpression,
            SyntaxKind comparisonKind,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion,
            bool rejectOtherReferencesAssignedSymbol)
        {
            assignmentCandidate = UnwrapExpression(assignmentCandidate);
            otherExpression = UnwrapExpression(otherExpression);
            var candidateFormulas = formulas.ToList();
            if (assignmentCandidate is not AssignmentExpressionSyntax assignment ||
                UnwrapExpression(otherExpression) is AssignmentExpressionSyntax ||
                !TryCreateSimpleInlineAssignmentFact(
                    assignment,
                    semanticModel,
                    cancellationToken,
                    candidateFormulas,
                    getSymbolVersion,
                    out var targetFormula,
                    out var assignedSymbol) ||
                (rejectOtherReferencesAssignedSymbol &&
                 ExpressionReferencesSymbol(otherExpression, assignedSymbol, semanticModel, cancellationToken)) ||
                !TryTranslateValue(otherExpression, semanticModel, cancellationToken, out var otherFormula, getSymbolVersion) ||
                otherFormula == null ||
                !TryTranslateComparison(comparisonKind, targetFormula, otherFormula, out var comparisonFormula) ||
                comparisonFormula == null)
            {
                return false;
            }

            candidateFormulas.Add(branchWhenTrue
                ? comparisonFormula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, comparisonFormula));
            ReplaceFormulas(formulas, candidateFormulas);
            return true;
        }

        private static bool TryCreateSimpleInlineAssignmentFact(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula targetFormula,
            out ISymbol assignedSymbol)
        {
            targetFormula = null!;
            assignedSymbol = null!;
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                ContainsNestedAssignment(assignment.Right) ||
                semanticModel.GetSymbolInfo(UnwrapExpression(assignment.Left), cancellationToken).Symbol is not ISymbol assignmentTarget ||
                assignmentTarget is not ILocalSymbol and not IParameterSymbol ||
                ExpressionReferencesSymbol(assignment.Right, assignmentTarget.OriginalDefinition, semanticModel, cancellationToken) ||
                !TryCreateSymbolFormula(assignmentTarget.OriginalDefinition, getSymbolVersion, out targetFormula))
            {
                targetFormula = null!;
                return false;
            }

            assignedSymbol = assignmentTarget.OriginalDefinition;
            RemoveFactsReferencingSymbol(formulas, assignedSymbol, getSymbolVersion);

            if (targetFormula is { Kind: SmtValueKind.Reference } &&
                TryCreateAsExpressionAssignmentFacts(
                    assignment.Right,
                    targetFormula,
                    semanticModel,
                    cancellationToken,
                    out var asFacts,
                    getSymbolVersion))
            {
                foreach (var fact in asFacts)
                {
                    formulas.Add(fact);
                }

                return true;
            }

            if (!TryTranslateValue(assignment.Right, semanticModel, cancellationToken, out var assignedValue, getSymbolVersion) ||
                assignedValue == null ||
                !AreComparable(targetFormula, assignedValue))
            {
                targetFormula = null!;
                return false;
            }

            formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, assignedValue));
            return true;
        }

        private static bool IsSupportedInlineAssignmentComparison(SyntaxKind kind)
        {
            return kind is
                SyntaxKind.EqualsExpression or
                SyntaxKind.NotEqualsExpression or
                SyntaxKind.LessThanExpression or
                SyntaxKind.LessThanOrEqualExpression or
                SyntaxKind.GreaterThanExpression or
                SyntaxKind.GreaterThanOrEqualExpression;
        }

        private static SyntaxKind ReverseComparisonKind(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.LessThanExpression => SyntaxKind.GreaterThanExpression,
                SyntaxKind.LessThanOrEqualExpression => SyntaxKind.GreaterThanOrEqualExpression,
                SyntaxKind.GreaterThanExpression => SyntaxKind.LessThanExpression,
                SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.LessThanOrEqualExpression,
                _ => kind,
            };
        }

        private static void ReplaceFormulas(ICollection<SmtFormula> formulas, IEnumerable<SmtFormula> replacement)
        {
            formulas.Clear();
            foreach (var formula in replacement)
            {
                formulas.Add(formula);
            }
        }

        private static bool ContainsNestedAssignment(SyntaxNode node)
        {
            return node.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>().Any();
        }

        private static bool ExpressionReferencesSymbol(
            SyntaxNode node,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var expression in node.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
            {
                var expressionSymbol = semanticModel.GetSymbolInfo(UnwrapExpression(expression), cancellationToken).Symbol;
                if (expressionSymbol != null &&
                    SymbolEqualityComparer.Default.Equals(expressionSymbol.OriginalDefinition, symbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveFactsReferencingSymbol(
            ICollection<SmtFormula> formulas,
            ISymbol symbol,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var variableName = GetVariableName(symbol.OriginalDefinition, getSymbolVersion);
            foreach (var formula in formulas.ToArray())
            {
                if (ReferencesVariable(formula, variableName))
                {
                    formulas.Remove(formula);
                }
            }
        }

        private static bool ReferencesVariable(SmtFormula formula, string variableName)
        {
            switch (formula)
            {
                case SmtVariable variable:
                    return IsVariableOrMemberOf(variable.Name, variableName);
                case SmtUnaryFormula unary:
                    return ReferencesVariable(unary.Operand, variableName);
                case SmtBinaryFormula binary:
                    return ReferencesVariable(binary.Left, variableName) ||
                        ReferencesVariable(binary.Right, variableName);
                case SmtIntegerUnaryTerm unary:
                    return ReferencesVariable(unary.Operand, variableName);
                case SmtIntegerBinaryTerm binary:
                    return ReferencesVariable(binary.Left, variableName) ||
                        ReferencesVariable(binary.Right, variableName);
                case SmtStringLengthTerm stringLength:
                    return ReferencesVariable(stringLength.Value, variableName);
                case SmtStringConcatTerm stringConcat:
                    return ReferencesVariable(stringConcat.Left, variableName) ||
                        ReferencesVariable(stringConcat.Right, variableName);
                case SmtStringContainsFormula stringContains:
                    return ReferencesVariable(stringContains.Value, variableName) ||
                        ReferencesVariable(stringContains.Search, variableName);
                case SmtStringStartsWithFormula stringStartsWith:
                    return ReferencesVariable(stringStartsWith.Value, variableName) ||
                        ReferencesVariable(stringStartsWith.Prefix, variableName);
                case SmtStringEndsWithFormula stringEndsWith:
                    return ReferencesVariable(stringEndsWith.Value, variableName) ||
                        ReferencesVariable(stringEndsWith.Suffix, variableName);
                case SmtRegexMatchFormula regexMatch:
                    return ReferencesVariable(regexMatch.Value, variableName);
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    return ReferencesVariable(runtimeTypeTest.Value, variableName);
                case SmtConditionalFormula conditional:
                    return ReferencesVariable(conditional.Condition, variableName) ||
                        ReferencesVariable(conditional.WhenTrue, variableName) ||
                        ReferencesVariable(conditional.WhenFalse, variableName);
                default:
                    return false;
            }
        }

        private static bool IsVariableOrMemberOf(string candidateName, string variableName)
        {
            return string.Equals(candidateName, variableName, StringComparison.Ordinal) ||
                candidateName.StartsWith(variableName + ".", StringComparison.Ordinal) ||
                candidateName.StartsWith(variableName + "[", StringComparison.Ordinal);
        }

        private static void AddConditionalAccessStringEqualityBranchFacts(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (expression is not BinaryExpressionSyntax binaryExpression ||
                (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
                 !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)) ||
                branchWhenTrue != binaryExpression.IsKind(SyntaxKind.EqualsExpression))
            {
                return;
            }

            if (TryAddConditionalAccessStringEqualityBranchFacts(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion))
            {
                return;
            }

            TryAddConditionalAccessStringEqualityBranchFacts(
                binaryExpression.Right,
                binaryExpression.Left,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        private static bool TryAddConditionalAccessStringEqualityBranchFacts(
            ExpressionSyntax conditionalCandidate,
            ExpressionSyntax otherCandidate,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            conditionalCandidate = UnwrapExpression(conditionalCandidate);
            otherCandidate = UnwrapExpression(otherCandidate);
            if (conditionalCandidate is not ConditionalAccessExpressionSyntax conditionalAccess ||
                !TryCreateStringNonNullFormula(otherCandidate, semanticModel, cancellationToken, out var otherNonNull, getSymbolVersion) ||
                otherNonNull is not SmtBooleanConstant { Value: true } ||
                !TryTranslateStringValue(otherCandidate, semanticModel, cancellationToken, out var otherString, getSymbolVersion) ||
                otherString == null ||
                !TryTranslateValue(conditionalAccess.Expression, semanticModel, cancellationToken, out var receiver, getSymbolVersion) ||
                receiver is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            var resultTypeInfo = semanticModel.GetTypeInfo(conditionalAccess, cancellationToken);
            var resultType = resultTypeInfo.ConvertedType ?? resultTypeInfo.Type;
            if (resultType?.SpecialType != SpecialType.System_String ||
                !TryCreateConditionalAccessWhenNotNullValueFormula(
                    conditionalAccess,
                    receiver,
                    resultType,
                    semanticModel,
                    cancellationToken,
                    out var whenNotNullReference,
                    getSymbolVersion,
                    inlineDepth: 0) ||
                whenNotNullReference is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.NotEqual, receiver, new SmtNullConstant()));
            formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.NotEqual, whenNotNullReference, new SmtNullConstant()));
            formulas.Add(new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                CreateStringValueFormulaForReference(whenNotNullReference),
                otherString));
            return true;
        }

        private static bool TryCreateTypeTestNonNullBranchFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null!;
            expression = UnwrapExpression(expression);

            ExpressionSyntax? testedExpression = null;
            if (expression is BinaryExpressionSyntax binaryExpression &&
                binaryExpression.IsKind(SyntaxKind.IsExpression) &&
                binaryExpression.Right is TypeSyntax)
            {
                testedExpression = binaryExpression.Left;
            }
            else if (expression is IsPatternExpressionSyntax isPatternExpression &&
                PatternMatchImpliesReferenceNonNull(isPatternExpression.Pattern))
            {
                testedExpression = isPatternExpression.Expression;
            }

            if (testedExpression == null ||
                !TryTranslateValue(testedExpression, semanticModel, cancellationToken, out var testedValue, getSymbolVersion) ||
                testedValue is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, testedValue, new SmtNullConstant());
            return true;
        }

        private static bool PatternMatchImpliesReferenceNonNull(PatternSyntax pattern)
        {
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                return PatternMatchImpliesReferenceNonNull(parenthesizedPattern.Pattern);
            }

            if (pattern is DeclarationPatternSyntax or TypePatternSyntax or RecursivePatternSyntax)
            {
                return true;
            }

            if (pattern is BinaryPatternSyntax binaryPattern)
            {
                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
                {
                    return PatternMatchImpliesReferenceNonNull(binaryPattern.Left) ||
                        PatternMatchImpliesReferenceNonNull(binaryPattern.Right);
                }

                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword))
                {
                    return PatternMatchImpliesReferenceNonNull(binaryPattern.Left) &&
                        PatternMatchImpliesReferenceNonNull(binaryPattern.Right);
                }
            }

            return false;
        }

        private static void AddNullComparisonOperandImplications(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);
            if (expression is not BinaryExpressionSyntax binaryExpression ||
                !ComparisonBranchImpliesComparedValueNonNull(binaryExpression.Kind(), branchWhenTrue))
            {
                return;
            }

            if (IsNullLikeReferenceComparisonOperand(binaryExpression.Left, semanticModel, cancellationToken) &&
                TryCreateOperandNonNullImplication(binaryExpression.Right, semanticModel, cancellationToken, out var rightImplication, getSymbolVersion))
            {
                formulas.Add(rightImplication);
                return;
            }

            if (IsNullLikeReferenceComparisonOperand(binaryExpression.Right, semanticModel, cancellationToken) &&
                TryCreateOperandNonNullImplication(binaryExpression.Left, semanticModel, cancellationToken, out var leftImplication, getSymbolVersion))
            {
                formulas.Add(leftImplication);
            }
        }

        private static bool ComparisonBranchImpliesComparedValueNonNull(SyntaxKind comparisonKind, bool branchWhenTrue)
        {
            return branchWhenTrue
                ? comparisonKind == SyntaxKind.NotEqualsExpression
                : comparisonKind == SyntaxKind.EqualsExpression;
        }

        private static bool IsNullLikeReferenceComparisonOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (semanticModel.GetConstantValue(expression, cancellationToken) is { HasValue: true, Value: null })
            {
                return true;
            }

            if (!expression.IsKind(SyntaxKind.DefaultLiteralExpression) &&
                expression is not DefaultExpressionSyntax)
            {
                return false;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            return type?.IsReferenceType == true;
        }

        private static bool TryCreateOperandNonNullImplication(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null!;
            expression = UnwrapExpression(expression);
            ExpressionSyntax? sourceExpression = null;

            if (expression is BinaryExpressionSyntax asExpression &&
                asExpression.IsKind(SyntaxKind.AsExpression))
            {
                sourceExpression = asExpression.Left;
            }
            else if (expression is CastExpressionSyntax castExpression &&
                IsIdentityPreservingReferenceCast(castExpression, semanticModel, cancellationToken))
            {
                sourceExpression = castExpression.Expression;
            }
            else if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
            {
                sourceExpression = conditionalAccess.Expression;
            }

            if (sourceExpression == null ||
                !TryTranslateValue(sourceExpression, semanticModel, cancellationToken, out var sourceFormula, getSymbolVersion) ||
                sourceFormula is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, sourceFormula, new SmtNullConstant());
            return true;
        }

        private static void AddPatternBindingFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);
            if (expression is not IsPatternExpressionSyntax isPatternExpression)
            {
                return;
            }

            if (TryAddNullablePatternBindingFacts(isPatternExpression, semanticModel, cancellationToken, formulas, getSymbolVersion))
            {
                return;
            }

            if (!TryTranslatePatternInputValue(
                    isPatternExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    out var matchedValue,
                    out var valueType,
                    getSymbolVersion,
                    inlineDepth: 0) ||
                matchedValue == null)
            {
                return;
            }

            AddPatternBindingFacts(
                matchedValue,
                valueType,
                isPatternExpression.Pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        private static bool TryAddNullablePatternBindingFacts(
            IsPatternExpressionSyntax isPatternExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (!TryTranslateNullableValueParts(
                    isPatternExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    out _,
                    out var nullableValue,
                    getSymbolVersion,
                    inlineDepth: 0) ||
                nullableValue == null)
            {
                return false;
            }

            AddNullablePatternBindingFacts(
                nullableValue,
                isPatternExpression.Pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
            return true;
        }

        private static void AddNullablePatternBindingFacts(
            SmtFormula nullableValue,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                AddNullablePatternBindingFacts(
                    nullableValue,
                    parenthesizedPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
                return;
            }

            if (pattern is DeclarationPatternSyntax declarationPattern)
            {
                AddDesignationBindingFact(nullableValue, declarationPattern.Designation, semanticModel, formulas, getSymbolVersion, out _);
                return;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
            {
                AddNullablePatternBindingFacts(
                    nullableValue,
                    binaryPattern.Left,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
                AddNullablePatternBindingFacts(
                    nullableValue,
                    binaryPattern.Right,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static void AddPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                AddPatternBindingFacts(
                    matchedValue,
                    matchedValueType,
                    parenthesizedPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
                return;
            }

            switch (pattern)
            {
                case VarPatternSyntax varPattern:
                    AddDesignationBindingFact(matchedValue, varPattern.Designation, semanticModel, formulas, getSymbolVersion, out _);
                    return;
                case DeclarationPatternSyntax declarationPattern:
                    AddDesignationBindingFact(matchedValue, declarationPattern.Designation, semanticModel, formulas, getSymbolVersion, out _);
                    AddDesignationNonNullFact(declarationPattern.Designation, semanticModel, formulas, getSymbolVersion);
                    return;
                case RecursivePatternSyntax recursivePattern:
                    AddDesignationBindingFact(
                        matchedValue,
                        recursivePattern.Designation,
                        semanticModel,
                        formulas,
                        getSymbolVersion,
                        out var designationValue);
                    AddDesignationNonNullFact(recursivePattern.Designation, semanticModel, formulas, getSymbolVersion);
                    AddRecursivePropertyPatternBindingFacts(
                        matchedValue,
                        recursivePattern,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    AddRecursiveTuplePositionalPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        recursivePattern,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    if (designationValue != null &&
                        !Equals(designationValue, matchedValue))
                    {
                        AddSubstitutedPatternFactsForDesignationReceiver(
                            matchedValue,
                            designationValue,
                            matchedValueType,
                            recursivePattern,
                            semanticModel,
                            cancellationToken,
                            formulas,
                            getSymbolVersion);
                        AddRecursivePropertyPatternBindingFacts(
                            designationValue,
                            recursivePattern,
                            semanticModel,
                            cancellationToken,
                            formulas,
                            getSymbolVersion);
                        AddRecursiveTuplePositionalPatternBindingFacts(
                            designationValue,
                            matchedValueType,
                            recursivePattern,
                            semanticModel,
                            cancellationToken,
                            formulas,
                            getSymbolVersion);
                    }

                    return;
                case BinaryPatternSyntax binaryPattern when binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword):
                    AddPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        binaryPattern.Left,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    AddPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        binaryPattern.Right,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    return;
                case ListPatternSyntax listPattern:
                    AddListPatternLengthFacts(
                        matchedValue,
                        matchedValueType,
                        listPattern,
                        semanticModel,
                        formulas);
                    AddListPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        listPattern,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    return;
            }
        }

        private static void AddSubstitutedPatternFactsForDesignationReceiver(
            SmtFormula matchedValue,
            SmtFormula designationValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (matchedValue is not SmtVariable matchedVariable ||
                !TryTranslatePattern(
                    matchedValue,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    out var patternFormula,
                    getSymbolVersion,
                    matchedValueType,
                    inlineDepth: 0) ||
                patternFormula == null)
            {
                return;
            }

            var substitutions = new[]
            {
                new SmtVariableSubstitution(
                    matchedVariable.Name,
                    matchedVariable.Name + ".",
                    GetFormulaMemberName(matchedVariable) + ".",
                    designationValue)
            };
            formulas.Add(SubstituteVariables(patternFormula, substitutions));
        }

        private static void AddListPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (!TryGetBuiltInElementAccessElementType(matchedValueType, semanticModel.Compilation, out var elementType) ||
                !TryGetValueKind(elementType, out var elementKind))
            {
                return;
            }

            for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
            {
                var subpattern = listPattern.Patterns[patternIndex];
                if (subpattern is SlicePatternSyntax)
                {
                    continue;
                }

                if (!TryGetListPatternElementPosition(listPattern, patternIndex, out var elementIndex, out var fromEnd))
                {
                    continue;
                }

                var elementValue = CreateListPatternElementFormula(matchedValue, elementIndex, fromEnd, elementKind);
                AddPatternBindingFacts(
                    elementValue,
                    elementType,
                    subpattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static void AddRecursivePropertyPatternBindingFacts(
            SmtFormula matchedValue,
            RecursivePatternSyntax recursivePattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var subpatterns = recursivePattern.PropertyPatternClause?.Subpatterns;
            if (subpatterns == null)
            {
                return;
            }

            foreach (var subpattern in subpatterns.Value)
            {
                if (!TryResolvePropertySubpatternValue(
                        matchedValue,
                        subpattern,
                        semanticModel,
                        cancellationToken,
                        out var memberValue,
                        out var memberType,
                        out var pathCondition) ||
                    memberValue == null ||
                    memberType == null)
                {
                    continue;
                }

                if (pathCondition != null)
                {
                    formulas.Add(pathCondition);
                }

                AddPatternBindingFacts(
                    memberValue,
                    memberType,
                    subpattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static void AddRecursiveTuplePositionalPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            RecursivePatternSyntax recursivePattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var subpatterns = recursivePattern.PositionalPatternClause?.Subpatterns;
            if (subpatterns == null)
            {
                return;
            }

            for (var position = 0; position < subpatterns.Value.Count; position++)
            {
                if (!TryResolveTuplePositionalSubpatternValue(
                        matchedValue,
                        matchedValueType,
                        position,
                        out var memberValue,
                        out var memberType) ||
                    memberValue == null ||
                    memberType == null)
                {
                    continue;
                }

                AddPatternBindingFacts(
                    memberValue,
                    memberType,
                    subpatterns.Value[position].Pattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static void AddDesignationBindingFact(
            SmtFormula matchedValue,
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula? localValue)
        {
            localValue = null;
            if (!TryCreateDesignationFormula(designation, semanticModel, getSymbolVersion, out var designationValue) ||
                designationValue == null ||
                !AreComparable(designationValue, matchedValue))
            {
                return;
            }

            localValue = designationValue;
            formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, localValue, matchedValue));

            if (TryCreateDesignationStringFormula(designation, semanticModel, getSymbolVersion, out var designationString) &&
                TryCreateStringContentFormula(matchedValue, out var matchedString))
            {
                formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, designationString, matchedString));
            }
        }

        private static void AddDesignationNonNullFact(
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (!TryCreateDesignationFormula(designation, semanticModel, getSymbolVersion, out var designationValue) ||
                designationValue is not { Kind: SmtValueKind.Reference })
            {
                return;
            }

            formulas.Add(new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                designationValue,
                new SmtNullConstant()));
        }

        private static bool TryCreateDesignationStringFormula(
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula formula)
        {
            formula = null!;
            if (designation is not SingleVariableDesignationSyntax singleVariableDesignation ||
                singleVariableDesignation.Identifier.ValueText == "_" ||
                semanticModel.GetDeclaredSymbol(singleVariableDesignation) is not ILocalSymbol localSymbol ||
                localSymbol.Type.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            formula = new SmtVariable(GetVariableName(localSymbol, getSymbolVersion) + ".String", SmtValueKind.String);
            return true;
        }

        private static bool TryCreateStringContentFormula(SmtFormula referenceFormula, out SmtFormula formula)
        {
            formula = null!;
            if (referenceFormula.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var receiverName = referenceFormula is SmtVariable variable
                ? variable.Name
                : referenceFormula.ToString();
            if (string.IsNullOrEmpty(receiverName))
            {
                return false;
            }

            formula = new SmtVariable(receiverName + ".String", SmtValueKind.String);
            return true;
        }

        private static bool TryCreateDesignationFormula(
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula formula)
        {
            formula = null!;
            if (designation is not SingleVariableDesignationSyntax singleVariableDesignation ||
                singleVariableDesignation.Identifier.ValueText == "_" ||
                semanticModel.GetDeclaredSymbol(singleVariableDesignation) is not ILocalSymbol localSymbol)
            {
                return false;
            }

            return TryCreateSymbolFormula(localSymbol, getSymbolVersion, out formula);
        }

        private static bool TryTranslatePatternExpression(
            IsPatternExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (TryTranslateNullablePatternExpression(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (!TryTranslatePatternInputValue(
                    expression.Expression,
                    semanticModel,
                    cancellationToken,
                    out var value,
                    out var valueType,
                    getSymbolVersion,
                    inlineDepth) ||
                value == null)
            {
                return false;
            }

            return TryTranslatePattern(value, expression.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, valueType, inlineDepth);
        }

        private static bool TryTranslatePatternInputValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? value,
            out ITypeSymbol? valueType,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            expression = UnwrapExpression(expression);
            var valueTypeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            valueType = valueTypeInfo.ConvertedType ?? valueTypeInfo.Type;

            if (TryTranslateValue(expression, semanticModel, cancellationToken, out value, getSymbolVersion, inlineDepth) &&
                value != null)
            {
                return true;
            }

            if (IsBuiltInSpanType(valueType) &&
                TryCreateBuiltInElementAccessReceiverFormula(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var spanValue,
                    getSymbolVersion,
                    inlineDepth) &&
                spanValue is { Kind: SmtValueKind.Reference })
            {
                value = spanValue;
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryTranslateNullablePatternExpression(
            IsPatternExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryTranslateNullableValueParts(
                    expression.Expression,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula,
                    out var valueFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                valueFormula == null ||
                !TryGetNullableUnderlyingType(
                    semanticModel.GetTypeInfo(expression.Expression, cancellationToken).Type,
                    out var underlyingType))
            {
                return false;
            }

            return TryTranslateNullablePattern(
                hasValueFormula,
                valueFormula,
                underlyingType,
                expression.Pattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        private static bool TryTranslateNullablePattern(
            SmtFormula hasValueFormula,
            SmtFormula valueFormula,
            ITypeSymbol underlyingType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                return TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    parenthesizedPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            if (pattern is DiscardPatternSyntax or VarPatternSyntax)
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            if (pattern is DeclarationPatternSyntax declarationPattern &&
                PatternTypeMatchesUnderlyingType(declarationPattern.Type, underlyingType, semanticModel, cancellationToken))
            {
                formula = hasValueFormula;
                return true;
            }

            if (pattern is TypePatternSyntax typePattern &&
                PatternTypeMatchesUnderlyingType(typePattern.Type, underlyingType, semanticModel, cancellationToken))
            {
                formula = hasValueFormula;
                return true;
            }

            if (pattern is ConstantPatternSyntax nullConstantPattern &&
                IsNullLikeNullableComparisonOperand(nullConstantPattern.Expression, semanticModel, cancellationToken))
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, hasValueFormula);
                return true;
            }

            if (pattern is RecursivePatternSyntax recursivePattern)
            {
                if (IsEmptyRecursivePattern(recursivePattern))
                {
                    formula = hasValueFormula;
                    return true;
                }

                if (TryTranslateRecursivePattern(
                        valueFormula,
                        underlyingType,
                        recursivePattern,
                        semanticModel,
                        cancellationToken,
                        out var recursiveFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    recursiveFormula != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, hasValueFormula, recursiveFormula);
                    return true;
                }
            }

            if (pattern is ConstantPatternSyntax constantPattern &&
                TryTranslateValue(constantPattern.Expression, semanticModel, cancellationToken, out var constantValue, getSymbolVersion, inlineDepth) &&
                constantValue != null &&
                AreComparable(valueFormula, constantValue))
            {
                formula = new SmtBinaryFormula(
                    SmtBinaryOperator.And,
                    hasValueFormula,
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, valueFormula, constantValue));
                return true;
            }

            if (pattern is RelationalPatternSyntax relationalPattern &&
                valueFormula.Kind == SmtValueKind.Int &&
                TryTranslateValue(relationalPattern.Expression, semanticModel, cancellationToken, out var relationalValue, getSymbolVersion, inlineDepth) &&
                relationalValue is { Kind: SmtValueKind.Int } &&
                TryTranslateRelationalPatternComparison(relationalPattern.OperatorToken.Kind(), valueFormula, relationalValue, out var comparison))
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, hasValueFormula, comparison);
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    unaryPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    out var negatedPattern,
                    getSymbolVersion,
                    inlineDepth) &&
                negatedPattern != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, negatedPattern);
                return true;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    binaryPattern.Left,
                    semanticModel,
                    cancellationToken,
                    out var leftPattern,
                    getSymbolVersion,
                    inlineDepth) &&
                leftPattern != null &&
                TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    binaryPattern.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightPattern,
                    getSymbolVersion,
                    inlineDepth) &&
                rightPattern != null)
            {
                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftPattern, rightPattern);
                    return true;
                }

                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftPattern, rightPattern);
                    return true;
                }
            }

            return false;
        }

        private static bool IsEmptyRecursivePattern(RecursivePatternSyntax recursivePattern)
        {
            return recursivePattern.PropertyPatternClause is not { Subpatterns.Count: > 0 } &&
                recursivePattern.PositionalPatternClause is not { Subpatterns.Count: > 0 };
        }

        private static bool TryTranslateRelationalPatternComparison(
            SyntaxKind operatorKind,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula formula)
        {
            formula = null!;
            switch (operatorKind)
            {
                case SyntaxKind.GreaterThanToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, left, right);
                    return true;
                case SyntaxKind.GreaterThanEqualsToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, left, right);
                    return true;
                case SyntaxKind.LessThanToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.LessThan, left, right);
                    return true;
                case SyntaxKind.LessThanEqualsToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, left, right);
                    return true;
                default:
                    return false;
            }
        }

        private static bool PatternTypeMatchesUnderlyingType(
            TypeSyntax patternTypeSyntax,
            ITypeSymbol underlyingType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var patternType = semanticModel.GetTypeInfo(patternTypeSyntax, cancellationToken).Type;
            return patternType != null &&
                SymbolEqualityComparer.Default.Equals(patternType, underlyingType);
        }

        public static bool TryTranslatePattern(
            SmtFormula value,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            ITypeSymbol? valueType = null,
            int inlineDepth = 0)
        {
            formula = null;

            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                return TryTranslatePattern(value, parenthesizedPattern.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, valueType, inlineDepth);
            }

            if (pattern is DiscardPatternSyntax or VarPatternSyntax)
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            if (pattern is ConstantPatternSyntax constantPattern &&
                TryTranslateValue(constantPattern.Expression, semanticModel, cancellationToken, out var constantValue, getSymbolVersion, inlineDepth) &&
                constantValue != null &&
                AreComparable(value, constantValue))
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, value, constantValue);
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                TryTranslatePattern(value, unaryPattern.Pattern, semanticModel, cancellationToken, out var negatedPattern, getSymbolVersion, valueType, inlineDepth) &&
                negatedPattern != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, negatedPattern);
                return true;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                TryTranslatePattern(value, binaryPattern.Left, semanticModel, cancellationToken, out var leftPattern, getSymbolVersion, valueType, inlineDepth) &&
                TryTranslatePattern(value, binaryPattern.Right, semanticModel, cancellationToken, out var rightPattern, getSymbolVersion, valueType, inlineDepth) &&
                leftPattern != null &&
                rightPattern != null)
            {
                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftPattern, rightPattern);
                    return true;
                }

                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftPattern, rightPattern);
                    return true;
                }
            }

            if (pattern is RelationalPatternSyntax relationalPattern &&
                value.Kind == SmtValueKind.Int &&
                TryTranslateValue(relationalPattern.Expression, semanticModel, cancellationToken, out var relationalValue, getSymbolVersion, inlineDepth) &&
                relationalValue is { Kind: SmtValueKind.Int })
            {
                switch (relationalPattern.OperatorToken.Kind())
                {
                    case SyntaxKind.GreaterThanToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, value, relationalValue);
                        return true;
                    case SyntaxKind.GreaterThanEqualsToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, value, relationalValue);
                        return true;
                    case SyntaxKind.LessThanToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.LessThan, value, relationalValue);
                        return true;
                    case SyntaxKind.LessThanEqualsToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, value, relationalValue);
                        return true;
                }
            }

            if (pattern is RecursivePatternSyntax recursivePattern)
            {
                return TryTranslateRecursivePattern(value, valueType, recursivePattern, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            if (pattern is ListPatternSyntax listPattern)
            {
                return TryTranslateListPattern(
                    value,
                    valueType,
                    listPattern,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            if (pattern is DeclarationPatternSyntax declarationPattern)
            {
                if (!TryTranslateReferenceTypePattern(
                        value,
                        valueType,
                        declarationPattern.Type,
                        semanticModel,
                        cancellationToken,
                        out formula))
                {
                    return false;
                }

                return true;
            }

            if (pattern is TypePatternSyntax typePattern)
            {
                if (!TryTranslateReferenceTypePattern(
                        value,
                        valueType,
                        typePattern.Type,
                        semanticModel,
                        cancellationToken,
                        out formula))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static bool TryTranslateReferenceTypePattern(
            SmtFormula value,
            ITypeSymbol? valueType,
            TypeSyntax patternTypeSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (value.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var patternType = semanticModel.GetTypeInfo(patternTypeSyntax, cancellationToken).Type;
            if (IsTypeKnownAssignableTo(valueType, patternType))
            {
                formula = CreateNonNullFormula(value);
                return true;
            }

            if (!TryCreateRuntimeTypeTestFormula(value, patternType, out var runtimeTypeTest))
            {
                return false;
            }

            formula = Conjoin(CreateNonNullFormula(value), runtimeTypeTest);
            return true;
        }

        private static bool TryTranslateRecursivePattern(
            SmtFormula value,
            ITypeSymbol? valueType,
            RecursivePatternSyntax recursivePattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            SmtFormula? current = ShouldRequireRecursivePatternNonNull(value, valueType)
                ? new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtNullConstant())
                : null;

            var positionalSubpatterns = recursivePattern.PositionalPatternClause?.Subpatterns;
            if (positionalSubpatterns != null)
            {
                for (var position = 0; position < positionalSubpatterns.Value.Count; position++)
                {
                    if (!TryTranslateTuplePositionalSubpattern(
                            value,
                            valueType,
                            positionalSubpatterns.Value[position],
                            position,
                            semanticModel,
                            cancellationToken,
                            out var positionalFormula,
                            getSymbolVersion,
                            inlineDepth) ||
                        positionalFormula == null)
                    {
                        return false;
                    }

                    current = Conjoin(current, positionalFormula);
                }
            }

            var propertySubpatterns = recursivePattern.PropertyPatternClause?.Subpatterns;
            if (propertySubpatterns != null)
            {
                foreach (var subpattern in propertySubpatterns.Value)
                {
                    if (!TryTranslatePropertySubpattern(value, subpattern, semanticModel, cancellationToken, out var subpatternFormula, getSymbolVersion, inlineDepth) ||
                        subpatternFormula == null)
                    {
                        return false;
                    }

                    current = Conjoin(current, subpatternFormula);
                }
            }

            formula = current;
            return formula != null;
        }

        private static bool ShouldRequireRecursivePatternNonNull(SmtFormula value, ITypeSymbol? valueType)
        {
            return value.Kind == SmtValueKind.Reference &&
                (valueType == null || valueType.IsReferenceType);
        }

        private static SmtFormula Conjoin(SmtFormula? left, SmtFormula right)
        {
            return left == null
                ? right
                : new SmtBinaryFormula(SmtBinaryOperator.And, left, right);
        }

        private static bool TryTranslateTuplePositionalSubpattern(
            SmtFormula receiver,
            ITypeSymbol? receiverType,
            SubpatternSyntax subpattern,
            int position,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryResolveTuplePositionalSubpatternValue(
                    receiver,
                    receiverType,
                    position,
                    out var memberValue,
                    out var memberType) ||
                memberValue == null ||
                memberType == null)
            {
                return false;
            }

            return TryTranslatePattern(
                memberValue,
                subpattern.Pattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                memberType,
                inlineDepth) &&
                formula != null;
        }

        private static bool TryResolveTuplePositionalSubpatternValue(
            SmtFormula receiver,
            ITypeSymbol? receiverType,
            int position,
            out SmtFormula? memberValue,
            out ITypeSymbol? memberType)
        {
            memberValue = null;
            memberType = null;
            if (!TryGetTuplePositionalField(receiverType, position, out var fieldSymbol) ||
                !TryGetTupleElementStorageName(fieldSymbol, out var storageName) ||
                !TryGetValueKind(fieldSymbol.Type, out var kind))
            {
                return false;
            }

            memberType = fieldSymbol.Type;
            memberValue = new SmtVariable(GetFormulaVariableName(receiver) + "." + storageName, kind);
            return true;
        }

        private static string GetFormulaVariableName(SmtFormula formula)
        {
            return formula is SmtVariable variable
                ? variable.Name
                : formula.ToString() ?? string.Empty;
        }

        private static bool TryGetTuplePositionalField(
            ITypeSymbol? receiverType,
            int position,
            out IFieldSymbol fieldSymbol)
        {
            fieldSymbol = null!;
            if (receiverType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            if (namedType.IsTupleType)
            {
                if (position < 0 || position >= namedType.TupleElements.Length)
                {
                    return false;
                }

                fieldSymbol = namedType.TupleElements[position];
                return true;
            }

            var storageName = "Item" + (position + 1).ToString(CultureInfo.InvariantCulture);
            fieldSymbol = namedType
                .GetMembers(storageName)
                .OfType<IFieldSymbol>()
                .FirstOrDefault(static field => !field.IsStatic)!;
            return fieldSymbol != null;
        }

        private static bool TryTranslatePropertySubpattern(
            SmtFormula receiver,
            SubpatternSyntax subpattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryResolvePropertySubpatternValue(
                    receiver,
                    subpattern,
                    semanticModel,
                    cancellationToken,
                    out var memberValue,
                    out var memberType,
                    out var pathCondition) ||
                memberValue == null ||
                memberType == null)
            {
                return false;
            }

            if (!TryTranslatePattern(memberValue, subpattern.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, memberType, inlineDepth) ||
                formula == null)
            {
                return false;
            }

            if (pathCondition != null)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, pathCondition, formula);
            }

            return true;
        }

        private static bool TryResolvePropertySubpatternValue(
            SmtFormula receiver,
            SubpatternSyntax subpattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? memberValue,
            out ITypeSymbol? memberType,
            out SmtFormula? pathCondition)
        {
            memberValue = null;
            memberType = null;
            pathCondition = null;

            var propertyPath = (SyntaxNode?)subpattern.NameColon?.Name ?? subpattern.ExpressionColon?.Expression;
            if (propertyPath == null ||
                !TryGetPropertySubpatternMemberNames(propertyPath, out var memberNames))
            {
                return false;
            }

            var currentValue = receiver;
            for (var index = 0; index < memberNames.Length; index++)
            {
                var memberName = memberNames[index];
                var memberSymbol = semanticModel.GetSymbolInfo(memberName, cancellationToken).Symbol;
                if (!TryGetMemberType(memberSymbol, out memberType))
                {
                    return false;
                }

                SmtFormula? nextValue;
                if (memberSymbol?.Name == "Length" &&
                    memberSymbol.ContainingType?.SpecialType == SpecialType.System_String &&
                    TryCreateStringLengthFormula(currentValue, out var stringLengthFormula))
                {
                    memberType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
                    nextValue = stringLengthFormula;
                }
                else if (!TryCreateMemberFormula(currentValue, memberSymbol!.Name, memberType, out nextValue) ||
                         nextValue == null)
                {
                    return false;
                }

                currentValue = nextValue;
                if (index < memberNames.Length - 1 &&
                    memberType.IsReferenceType)
                {
                    var nonNull = new SmtBinaryFormula(
                        SmtBinaryOperator.NotEqual,
                        currentValue,
                        new SmtNullConstant());
                    pathCondition = pathCondition == null
                        ? nonNull
                        : new SmtBinaryFormula(SmtBinaryOperator.And, pathCondition, nonNull);
                }
            }

            memberValue = currentValue;
            return memberType != null;
        }

        private static bool TryGetPropertySubpatternMemberNames(
            SyntaxNode propertyPath,
            out ImmutableArray<SimpleNameSyntax> memberNames)
        {
            var builder = ImmutableArray.CreateBuilder<SimpleNameSyntax>();
            if (!AddPropertySubpatternMemberNames(propertyPath, builder) ||
                builder.Count == 0)
            {
                memberNames = ImmutableArray<SimpleNameSyntax>.Empty;
                return false;
            }

            memberNames = builder.ToImmutable();
            return true;
        }

        private static bool AddPropertySubpatternMemberNames(
            SyntaxNode propertyPath,
            ImmutableArray<SimpleNameSyntax>.Builder memberNames)
        {
            switch (propertyPath)
            {
                case SimpleNameSyntax simpleName:
                    memberNames.Add(simpleName);
                    return true;
                case QualifiedNameSyntax qualifiedName:
                    return AddPropertySubpatternMemberNames(qualifiedName.Left, memberNames) &&
                        AddPropertySubpatternMemberNames(qualifiedName.Right, memberNames);
                case MemberAccessExpressionSyntax memberAccess:
                    return AddPropertySubpatternMemberNames(memberAccess.Expression, memberNames) &&
                        AddPropertySubpatternMemberNames(memberAccess.Name, memberNames);
                default:
                    return false;
            }
        }

        private static bool TryTranslateListPattern(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (value.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var canTranslateElementConditions = IsSupportedBuiltInElementAccessReceiver(valueType);
            if (!canTranslateElementConditions &&
                !ListPatternHasOnlySelectionNeutralElements(listPattern))
            {
                return false;
            }

            if (!TryCreateListPatternLengthCondition(
                    value,
                    valueType,
                    listPattern,
                    semanticModel,
                    out var lengthFormulaCondition) ||
                lengthFormulaCondition == null)
            {
                return false;
            }

            var nonNullFormula = new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                value,
                new SmtNullConstant());
            formula = new SmtBinaryFormula(SmtBinaryOperator.And, nonNullFormula, lengthFormulaCondition);
            if (canTranslateElementConditions)
            {
                AddListPatternElementConditions(
                    value,
                    valueType,
                    listPattern,
                    semanticModel,
                    cancellationToken,
                    ref formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            return true;
        }

        private static void AddListPatternLengthFacts(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            ICollection<SmtFormula> formulas)
        {
            if (value.Kind != SmtValueKind.Reference ||
                !TryCreateListPatternLengthCondition(
                    value,
                    valueType,
                    listPattern,
                    semanticModel,
                    out var lengthFormulaCondition) ||
                lengthFormulaCondition == null)
            {
                return;
            }

            formulas.Add(new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                value,
                new SmtNullConstant()));
            formulas.Add(lengthFormulaCondition);
        }

        private static bool TryCreateListPatternLengthCondition(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            out SmtFormula? lengthFormulaCondition)
        {
            lengthFormulaCondition = null;
            if (!TryCreateListPatternLengthFormula(value, valueType, semanticModel, out var lengthFormula) ||
                lengthFormula == null)
            {
                return false;
            }

            GetListPatternLengthShape(listPattern, out var minimumLength, out var exactLength);
            lengthFormulaCondition = exactLength
                ? new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    lengthFormula,
                    new SmtIntegerConstant(minimumLength))
                : new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    lengthFormula,
                    new SmtIntegerConstant(minimumLength));
            return true;
        }

        private static bool TryCreateListPatternLengthFormula(
            SmtFormula value,
            ITypeSymbol? valueType,
            SemanticModel semanticModel,
            out SmtFormula? lengthFormula)
        {
            lengthFormula = null;
            if (valueType?.SpecialType == SpecialType.System_String)
            {
                return TryCreateStringLengthFormula(value, out lengthFormula);
            }

            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (IsSupportedBuiltInElementAccessReceiver(valueType) &&
                !HasCountBackedIntIndexer(valueType))
            {
                return TryCreateMemberFormula(value, "Length", intType, out lengthFormula) &&
                    lengthFormula != null;
            }

            if (!TryGetListPatternLengthMemberName(valueType, out var memberName))
            {
                return false;
            }

            return TryCreateMemberFormula(value, memberName, intType, out lengthFormula) &&
                lengthFormula != null;
        }

        private static bool TryGetListPatternLengthMemberName(ITypeSymbol? valueType, out string memberName)
        {
            if (HasInstanceInt32Member(valueType, "Length"))
            {
                memberName = "Length";
                return true;
            }

            if (HasInstanceInt32Member(valueType, "Count"))
            {
                memberName = "Count";
                return true;
            }

            memberName = string.Empty;
            return false;
        }

        private static bool HasInstanceInt32Member(ITypeSymbol? valueType, string memberName)
        {
            if (valueType == null)
            {
                return false;
            }

            for (var current = valueType; current != null; current = (current as INamedTypeSymbol)?.BaseType)
            {
                if (HasDeclaredInstanceInt32Member(current, memberName))
                {
                    return true;
                }
            }

            foreach (var interfaceType in valueType.AllInterfaces)
            {
                if (HasDeclaredInstanceInt32Member(interfaceType, memberName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDeclaredInstanceInt32Member(ITypeSymbol type, string memberName)
        {
            foreach (var member in type.GetMembers(memberName))
            {
                switch (member)
                {
                    case IPropertySymbol { IsStatic: false, Parameters.Length: 0, Type.SpecialType: SpecialType.System_Int32 }:
                    case IFieldSymbol { IsStatic: false, Type.SpecialType: SpecialType.System_Int32 }:
                        return true;
                }
            }

            return false;
        }

        private static bool TryCreateStringLengthFormula(SmtFormula receiver, out SmtFormula formula)
        {
            formula = null!;
            if (receiver.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var receiverName = receiver is SmtVariable variable
                ? variable.Name
                : receiver.ToString();
            if (string.IsNullOrEmpty(receiverName))
            {
                return false;
            }

            formula = new SmtStringLengthTerm(new SmtVariable(receiverName + ".String", SmtValueKind.String));
            return true;
        }

        private static void AddListPatternElementConditions(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ref SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (formula == null ||
                !TryGetBuiltInElementAccessElementType(valueType, semanticModel.Compilation, out var elementType) ||
                !TryGetValueKind(elementType, out var elementKind))
            {
                return;
            }

            for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
            {
                var subpattern = listPattern.Patterns[patternIndex];
                if (subpattern is SlicePatternSyntax)
                {
                    continue;
                }

                if (!TryGetListPatternElementPosition(listPattern, patternIndex, out var elementIndex, out var fromEnd))
                {
                    continue;
                }

                var elementValue = CreateListPatternElementFormula(value, elementIndex, fromEnd, elementKind);
                if (TryTranslatePattern(
                        elementValue,
                        subpattern,
                        semanticModel,
                        cancellationToken,
                        out var elementCondition,
                        getSymbolVersion,
                        elementType,
                        inlineDepth) &&
                    elementCondition != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, formula, elementCondition);
                }
            }
        }

        private static bool TryGetListPatternElementPosition(
            ListPatternSyntax listPattern,
            int patternIndex,
            out int elementIndex,
            out bool fromEnd)
        {
            elementIndex = 0;
            fromEnd = false;

            if (listPattern.Patterns[patternIndex] is SlicePatternSyntax)
            {
                return false;
            }

            var sliceIndex = -1;
            for (var index = 0; index < listPattern.Patterns.Count; index++)
            {
                if (listPattern.Patterns[index] is SlicePatternSyntax)
                {
                    sliceIndex = index;
                    break;
                }
            }

            if (sliceIndex < 0 || patternIndex < sliceIndex)
            {
                elementIndex = patternIndex;
                return true;
            }

            elementIndex = listPattern.Patterns.Count - patternIndex;
            fromEnd = true;
            return true;
        }

        private static SmtFormula CreateListPatternElementFormula(
            SmtFormula receiver,
            int elementIndex,
            bool fromEnd,
            SmtValueKind elementKind)
        {
            var indexText = fromEnd
                ? "^" + elementIndex.ToString(CultureInfo.InvariantCulture)
                : elementIndex.ToString(CultureInfo.InvariantCulture);
            return new SmtVariable(receiver + "[" + indexText + "]", elementKind);
        }

        private static void GetListPatternLengthShape(
            ListPatternSyntax listPattern,
            out int minimumLength,
            out bool exactLength)
        {
            minimumLength = 0;
            exactLength = true;
            foreach (var subpattern in listPattern.Patterns)
            {
                if (subpattern is SlicePatternSyntax slicePattern)
                {
                    if (TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                    {
                        GetListPatternLengthShape(nestedListPattern, out var nestedMinimumLength, out var nestedExactLength);
                        minimumLength += nestedMinimumLength;
                        exactLength &= nestedExactLength;
                    }
                    else
                    {
                        exactLength = false;
                    }

                    continue;
                }

                minimumLength++;
            }
        }

        private static bool ListPatternHasOnlySelectionNeutralElements(ListPatternSyntax listPattern)
        {
            foreach (var subpattern in listPattern.Patterns)
            {
                if (subpattern is SlicePatternSyntax slicePattern)
                {
                    if (IsSelectionNeutralSlicePattern(slicePattern.Pattern))
                    {
                        continue;
                    }

                    if (!TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern) ||
                        !ListPatternHasOnlySelectionNeutralElements(nestedListPattern))
                    {
                        return false;
                    }

                    continue;
                }

                if (!IsSelectionNeutralPattern(subpattern))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSelectionNeutralSlicePattern(PatternSyntax? pattern)
        {
            return pattern == null ||
                IsSelectionNeutralPattern(pattern);
        }

        private static bool IsSelectionNeutralPattern(PatternSyntax pattern)
        {
            while (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                pattern = parenthesizedPattern.Pattern;
            }

            return pattern is DiscardPatternSyntax or VarPatternSyntax;
        }

        private static int GetListPatternMinimumLength(ListPatternSyntax listPattern)
        {
            GetListPatternLengthShape(listPattern, out var minimumLength, out _);
            return minimumLength;
        }

        private static bool TryGetNestedListPattern(PatternSyntax? pattern, out ListPatternSyntax listPattern)
        {
            while (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                pattern = parenthesizedPattern.Pattern;
            }

            if (pattern is ListPatternSyntax candidate)
            {
                listPattern = candidate;
                return true;
            }

            listPattern = null!;
            return false;
        }

        private static bool TryTranslateBuiltInElementAccessValue(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var receiverType = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
            if (!TryGetBuiltInElementAccessElementType(receiverType, semanticModel.Compilation, out var elementType) ||
                !TryGetValueKind(elementType, out var elementKind) ||
                !TryCreateBuiltInElementAccessReceiverFormula(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                receiverFormula is not { Kind: SmtValueKind.Reference } ||
                !TryCreateElementAccessIndexVectorText(
                    elementAccess,
                    receiverType,
                    semanticModel,
                    cancellationToken,
                    out var indexText,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            formula = new SmtVariable(receiverFormula + "[" + indexText + "]", elementKind);
            return true;
        }

        private static bool TryCreateElementAccessIndexVectorText(
            ElementAccessExpressionSyntax elementAccess,
            ITypeSymbol? receiverType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out string indexText,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (receiverType is IArrayTypeSymbol { Rank: > 1 } arrayType)
            {
                if (elementAccess.ArgumentList.Arguments.Count != arrayType.Rank)
                {
                    indexText = string.Empty;
                    return false;
                }

                var indexTexts = new List<string>(arrayType.Rank);
                foreach (var argument in elementAccess.ArgumentList.Arguments)
                {
                    if (!TryCreateOrdinaryElementAccessIndexText(
                            argument.Expression,
                            semanticModel,
                            cancellationToken,
                            out var dimensionIndexText,
                            getSymbolVersion,
                            inlineDepth))
                    {
                        indexText = string.Empty;
                        return false;
                    }

                    indexTexts.Add(dimensionIndexText);
                }

                indexText = string.Join(",", indexTexts);
                return indexTexts.Count != 0;
            }

            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                indexText = string.Empty;
                return false;
            }

            return TryCreateElementAccessIndexText(
                elementAccess.ArgumentList.Arguments[0].Expression,
                semanticModel,
                cancellationToken,
                out indexText,
                getSymbolVersion,
                inlineDepth);
        }

        private static bool TryCreateOrdinaryElementAccessIndexText(
            ExpressionSyntax indexExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out string indexText,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            indexExpression = UnwrapElementAccessIndexExpression(indexExpression);
            if (!TryTranslateValue(
                    indexExpression,
                    semanticModel,
                    cancellationToken,
                    out var indexFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                indexFormula is not { Kind: SmtValueKind.Int })
            {
                indexText = string.Empty;
                return false;
            }

            indexText = CreateElementAccessIndexText(indexFormula);
            return indexText.Length > 0;
        }

        private static bool TryCreateElementAccessIndexText(
            ExpressionSyntax indexExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out string indexText,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (!TryResolveBuiltInIndexAccessIndexShape(
                    indexExpression,
                    semanticModel,
                    cancellationToken,
                    out var indexShape))
            {
                indexText = string.Empty;
                return false;
            }

            if (!TryTranslateValue(
                    indexShape.ValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var indexFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                indexFormula is not { Kind: SmtValueKind.Int })
            {
                indexText = string.Empty;
                return false;
            }

            indexText = indexShape.FromEnd
                ? "^" + CreateElementAccessIndexText(indexFormula)
                : CreateElementAccessIndexText(indexFormula);
            return indexText.Length > 0;
        }

        private static string CreateElementAccessIndexText(SmtFormula indexFormula)
        {
            return indexFormula is SmtIntegerConstant integerConstant
                ? integerConstant.Value.ToString(CultureInfo.InvariantCulture)
                : indexFormula.ToString() ?? string.Empty;
        }

        private static bool TryCreateBuiltInElementAccessLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            receiverExpression = UnwrapExpression(receiverExpression);
            if (receiverExpression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslate(
                    conditionalExpression.Condition,
                    semanticModel,
                    cancellationToken,
                    out var conditionFormula,
                    getSymbolVersion,
                    inlineDepth) &&
                conditionFormula != null &&
                TryCreateBuiltInElementAccessLengthFormula(
                    conditionalExpression.WhenTrue,
                    semanticModel,
                    cancellationToken,
                    out var whenTrueLength,
                    getSymbolVersion,
                    inlineDepth) &&
                TryCreateBuiltInElementAccessLengthFormula(
                    conditionalExpression.WhenFalse,
                    semanticModel,
                    cancellationToken,
                    out var whenFalseLength,
                    getSymbolVersion,
                    inlineDepth))
            {
                lengthFormula = new SmtConditionalFormula(conditionFormula, whenTrueLength, whenFalseLength, SmtValueKind.Int);
                return true;
            }

            if (receiverExpression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryTranslateValue(
                    coalesceExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var coalesceLeft,
                    getSymbolVersion,
                    inlineDepth) &&
                coalesceLeft is { Kind: SmtValueKind.Reference } &&
                TryCreateBuiltInElementAccessLengthFormula(
                    coalesceExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var coalesceLeftLength,
                    getSymbolVersion,
                    inlineDepth) &&
                TryCreateBuiltInElementAccessLengthFormula(
                    coalesceExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var coalesceRightLength,
                    getSymbolVersion,
                    inlineDepth))
            {
                lengthFormula = new SmtConditionalFormula(
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, coalesceLeft, new SmtNullConstant()),
                    coalesceLeftLength,
                    coalesceRightLength,
                    SmtValueKind.Int);
                return true;
            }

            if (TryCreateBuiltInRangeAccessResultLengthFormula(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out lengthFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (TryCreateBuiltInSliceInvocationResultLengthFormula(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out lengthFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (TryCreateStringCreationResultLengthFormula(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out lengthFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (TryCreateStringInvocationResultLengthFormula(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out lengthFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (TryCreateReferenceCastBuiltInLengthFormula(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out lengthFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            var receiverTypeInfo = semanticModel.GetTypeInfo(receiverExpression, cancellationToken);
            if ((receiverTypeInfo.Type is IArrayTypeSymbol { Rank: 1 } ||
                 receiverTypeInfo.ConvertedType is IArrayTypeSymbol { Rank: 1 }) &&
                TryCreateArrayLengthFormula(receiverExpression, semanticModel, cancellationToken, out lengthFormula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (TryGetKnownStringLength(receiverExpression, semanticModel, cancellationToken, out var knownStringLength))
            {
                lengthFormula = new SmtIntegerConstant(knownStringLength);
                return true;
            }

            if (IsStringExpression(receiverExpression, semanticModel, cancellationToken) &&
                TryTranslateStringValue(receiverExpression, semanticModel, cancellationToken, out var stringValue, getSymbolVersion, inlineDepth) &&
                stringValue != null)
            {
                lengthFormula = new SmtStringLengthTerm(stringValue);
                return true;
            }

            if (IsBuiltInSpanOrMemoryType(receiverTypeInfo.ConvertedType ?? receiverTypeInfo.Type) &&
                TryCreateBuiltInLengthReceiverFormula(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out var lengthReceiverFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
                if (TryCreateMemberFormula(lengthReceiverFormula, "Length", intType, out var receiverLength) &&
                    receiverLength is { Kind: SmtValueKind.Int })
                {
                    lengthFormula = receiverLength;
                    return true;
                }
            }

            if (HasCountBackedIntIndexer(receiverTypeInfo.ConvertedType ?? receiverTypeInfo.Type) &&
                TryCreateBuiltInLengthReceiverFormula(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out var countReceiverFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
                if (TryCreateMemberFormula(countReceiverFormula, "Count", intType, out var receiverCount) &&
                    receiverCount is { Kind: SmtValueKind.Int })
                {
                    lengthFormula = receiverCount;
                    return true;
                }
            }

            if (!TryTranslateValue(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                receiverFormula is not { Kind: SmtValueKind.Reference })
            {
                lengthFormula = null!;
                return false;
            }

            var fallbackIntType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (!TryCreateMemberFormula(receiverFormula, "Length", fallbackIntType, out var candidate) ||
                candidate is not { Kind: SmtValueKind.Int })
            {
                lengthFormula = null!;
                return false;
            }

            lengthFormula = candidate;
            return true;
        }

        private static bool TryCreateReferenceCastBuiltInLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            lengthFormula = null!;
            if (UnwrapExpression(receiverExpression) is not CastExpressionSyntax castExpression ||
                !TryTranslateNonUserDefinedReferenceCastOperand(
                    castExpression,
                    semanticModel,
                    cancellationToken,
                    out var operandReference,
                    out var targetType,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            if (targetType is IArrayTypeSymbol { Rank: 1 })
            {
                var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
                if (TryCreateMemberFormula(operandReference, "Length", intType, out var candidate) &&
                    candidate is { Kind: SmtValueKind.Int })
                {
                    lengthFormula = candidate;
                    return true;
                }

                return false;
            }

            if (targetType.SpecialType == SpecialType.System_String)
            {
                lengthFormula = new SmtStringLengthTerm(CreateStringValueFormulaForReference(operandReference));
                return true;
            }

            return false;
        }

        private static bool TryCreateBuiltInSliceInvocationResultLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            lengthFormula = null!;
            if (receiverExpression is not InvocationExpressionSyntax invocationExpression ||
                semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation)
            {
                return false;
            }

            var method = invocationOperation.TargetMethod;
            if (method.IsStatic ||
                method.Name != "Slice" ||
                !IsBuiltInSpanOrMemoryType(method.ContainingType) ||
                !IsBuiltInSpanOrMemoryType(method.ReturnType) ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax sourceExpression)
            {
                return false;
            }

            if (method.Parameters.Length == 1)
            {
                if (invocationOperation.Arguments.Length != 1 ||
                    !TryCreateBuiltInElementAccessLengthFormula(
                        sourceExpression,
                        semanticModel,
                        cancellationToken,
                        out var sourceLength,
                        getSymbolVersion,
                        inlineDepth) ||
                    !TryTranslateIntInvocationArgument(
                        invocationOperation,
                        parameterIndex: 0,
                        semanticModel,
                        cancellationToken,
                        out var start,
                        getSymbolVersion,
                        inlineDepth))
                {
                    return false;
                }

                lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, sourceLength, start);
                return true;
            }

            if (method.Parameters.Length != 2 ||
                invocationOperation.Arguments.Length != 2 ||
                !TryCreateBuiltInElementAccessLengthFormula(
                    sourceExpression,
                    semanticModel,
                    cancellationToken,
                    out _,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryTranslateIntInvocationArgument(
                    invocationOperation,
                    parameterIndex: 0,
                    semanticModel,
                    cancellationToken,
                    out _,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryTranslateIntInvocationArgument(
                    invocationOperation,
                    parameterIndex: 1,
                    semanticModel,
                    cancellationToken,
                    out var resultLength,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            lengthFormula = resultLength;
            return true;
        }

        private static bool TryCreateStringCreationResultLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            lengthFormula = null!;
            if (receiverExpression is not ObjectCreationExpressionSyntax objectCreationExpression ||
                semanticModel.GetOperation(objectCreationExpression, cancellationToken) is not IObjectCreationOperation objectCreationOperation)
            {
                return false;
            }

            var constructor = objectCreationOperation.Constructor;
            if (constructor == null ||
                constructor.ContainingType.SpecialType != SpecialType.System_String ||
                constructor.Parameters.Length != 2 ||
                constructor.Parameters[0].Type.SpecialType != SpecialType.System_Char ||
                constructor.Parameters[1].Type.SpecialType != SpecialType.System_Int32 ||
                objectCreationOperation.Arguments.Length != 2 ||
                !TryGetObjectCreationArgumentExpression(objectCreationOperation, parameterIndex: 1, out var countExpression) ||
                !TryTranslateValue(
                    countExpression,
                    semanticModel,
                    cancellationToken,
                    out var countFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                countFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            lengthFormula = countFormula;
            return true;
        }

        private static bool TryCreateStringInvocationResultLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            lengthFormula = null!;
            if (receiverExpression is not InvocationExpressionSyntax invocationExpression ||
                semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation)
            {
                return false;
            }

            var method = invocationOperation.TargetMethod;
            if (method.IsStatic ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                method.ReturnType.SpecialType != SpecialType.System_String ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax sourceExpression)
            {
                return false;
            }

            if (method.Name == "Substring")
            {
                if (method.Parameters.Length == 1)
                {
                    if (invocationOperation.Arguments.Length != 1 ||
                        !TryCreateBuiltInElementAccessLengthFormula(
                            sourceExpression,
                            semanticModel,
                            cancellationToken,
                            out var sourceLength,
                            getSymbolVersion,
                            inlineDepth) ||
                        !TryTranslateIntInvocationArgument(
                            invocationOperation,
                            parameterIndex: 0,
                            semanticModel,
                            cancellationToken,
                            out var start,
                            getSymbolVersion,
                            inlineDepth))
                    {
                        return false;
                    }

                    lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, sourceLength, start);
                    return true;
                }

                if (method.Parameters.Length != 2 ||
                    invocationOperation.Arguments.Length != 2 ||
                    !TryCreateBuiltInElementAccessLengthFormula(
                        sourceExpression,
                        semanticModel,
                        cancellationToken,
                        out _,
                        getSymbolVersion,
                        inlineDepth) ||
                    !TryTranslateIntInvocationArgument(
                        invocationOperation,
                        parameterIndex: 0,
                        semanticModel,
                        cancellationToken,
                        out _,
                        getSymbolVersion,
                        inlineDepth) ||
                    !TryTranslateIntInvocationArgument(
                        invocationOperation,
                        parameterIndex: 1,
                        semanticModel,
                        cancellationToken,
                        out var candidateLengthFormula,
                        getSymbolVersion,
                        inlineDepth))
                {
                    lengthFormula = null!;
                    return false;
                }

                lengthFormula = candidateLengthFormula;
                return true;
            }

            if (method.Name == "Remove")
            {
                if (!TryCreateBuiltInElementAccessLengthFormula(
                        sourceExpression,
                        semanticModel,
                        cancellationToken,
                        out var sourceLength,
                        getSymbolVersion,
                        inlineDepth))
                {
                    return false;
                }

                if (method.Parameters.Length == 1)
                {
                    if (invocationOperation.Arguments.Length != 1 ||
                        !TryTranslateIntInvocationArgument(
                            invocationOperation,
                            parameterIndex: 0,
                            semanticModel,
                            cancellationToken,
                            out var start,
                            getSymbolVersion,
                            inlineDepth))
                    {
                        return false;
                    }

                    lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, sourceLength, start);
                    return true;
                }

                if (method.Parameters.Length != 2 ||
                    invocationOperation.Arguments.Length != 2 ||
                    !TryTranslateIntInvocationArgument(
                        invocationOperation,
                        parameterIndex: 0,
                        semanticModel,
                        cancellationToken,
                        out _,
                        getSymbolVersion,
                        inlineDepth) ||
                    !TryTranslateIntInvocationArgument(
                        invocationOperation,
                        parameterIndex: 1,
                        semanticModel,
                        cancellationToken,
                        out var count,
                        getSymbolVersion,
                        inlineDepth))
                {
                    return false;
                }

                lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, sourceLength, count);
                return true;
            }

            if (method.Name == "Insert")
            {
                if (method.Parameters.Length != 2 ||
                    method.Parameters[1].Type.SpecialType != SpecialType.System_String ||
                    invocationOperation.Arguments.Length != 2 ||
                    !TryCreateBuiltInElementAccessLengthFormula(
                        sourceExpression,
                        semanticModel,
                        cancellationToken,
                        out var sourceLength,
                        getSymbolVersion,
                        inlineDepth) ||
                    !TryTranslateIntInvocationArgument(
                        invocationOperation,
                        parameterIndex: 0,
                        semanticModel,
                        cancellationToken,
                        out _,
                        getSymbolVersion,
                        inlineDepth) ||
                    !TryGetInvocationArgumentExpression(invocationOperation, parameterIndex: 1, out var valueExpression) ||
                    !TryCreateBuiltInElementAccessLengthFormula(
                        valueExpression,
                        semanticModel,
                        cancellationToken,
                        out var valueLength,
                        getSymbolVersion,
                        inlineDepth))
                {
                    return false;
                }

                lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, sourceLength, valueLength);
                return true;
            }

            if (method.Name is "PadLeft" or "PadRight")
            {
                if ((method.Parameters.Length != 1 &&
                        (method.Parameters.Length != 2 ||
                            method.Parameters[1].Type.SpecialType != SpecialType.System_Char)) ||
                    invocationOperation.Arguments.Length != method.Parameters.Length ||
                    !TryCreateBuiltInElementAccessLengthFormula(
                        sourceExpression,
                        semanticModel,
                        cancellationToken,
                        out var sourceLength,
                        getSymbolVersion,
                        inlineDepth) ||
                    !TryTranslateIntInvocationArgument(
                        invocationOperation,
                        parameterIndex: 0,
                        semanticModel,
                        cancellationToken,
                        out var totalWidth,
                        getSymbolVersion,
                        inlineDepth))
                {
                    return false;
                }

                lengthFormula = new SmtConditionalFormula(
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, totalWidth, sourceLength),
                    totalWidth,
                    sourceLength,
                    SmtValueKind.Int);
                return true;
            }

            return false;
        }

        private static bool TryTranslateIntInvocationArgument(
            IInvocationOperation invocationOperation,
            int parameterIndex,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula argument,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            argument = null!;
            if (parameterIndex < 0 ||
                parameterIndex >= invocationOperation.TargetMethod.Parameters.Length ||
                invocationOperation.TargetMethod.Parameters[parameterIndex].Type.SpecialType != SpecialType.System_Int32 ||
                !TryGetInvocationArgumentExpression(invocationOperation, parameterIndex, out var argumentExpression) ||
                !TryTranslateValue(
                    argumentExpression,
                    semanticModel,
                    cancellationToken,
                    out var candidate,
                    getSymbolVersion,
                    inlineDepth) ||
                candidate is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            argument = candidate;
            return true;
        }

        private static bool TryGetInvocationArgumentExpression(
            IInvocationOperation invocationOperation,
            int parameterIndex,
            out ExpressionSyntax expression)
        {
            expression = null!;
            if (parameterIndex < 0 ||
                parameterIndex >= invocationOperation.TargetMethod.Parameters.Length)
            {
                return false;
            }

            var parameter = invocationOperation.TargetMethod.Parameters[parameterIndex];
            foreach (var argument in invocationOperation.Arguments)
            {
                if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression)
                {
                    expression = argumentExpression;
                    return true;
                }
            }

            if (parameterIndex < invocationOperation.Arguments.Length &&
                invocationOperation.Arguments[parameterIndex].Value.Syntax is ExpressionSyntax fallbackExpression)
            {
                expression = fallbackExpression;
                return true;
            }

            return false;
        }

        private static bool TryGetObjectCreationArgumentExpression(
            IObjectCreationOperation objectCreationOperation,
            int parameterIndex,
            out ExpressionSyntax expression)
        {
            expression = null!;
            if (objectCreationOperation.Constructor == null ||
                parameterIndex < 0 ||
                parameterIndex >= objectCreationOperation.Constructor.Parameters.Length)
            {
                return false;
            }

            var parameter = objectCreationOperation.Constructor.Parameters[parameterIndex];
            foreach (var argument in objectCreationOperation.Arguments)
            {
                if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression)
                {
                    expression = argumentExpression;
                    return true;
                }
            }

            if (parameterIndex < objectCreationOperation.Arguments.Length &&
                objectCreationOperation.Arguments[parameterIndex].Value.Syntax is ExpressionSyntax fallbackExpression)
            {
                expression = fallbackExpression;
                return true;
            }

            return false;
        }

        private static bool TryCreateBuiltInRangeAccessResultLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            lengthFormula = null!;
            if (receiverExpression is not ElementAccessExpressionSyntax elementAccess ||
                elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            var sourceType = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
            if (!IsSupportedBuiltInElementAccessReceiver(sourceType))
            {
                return false;
            }

            if (!TryResolveBuiltInRangeAccessRangeShape(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken,
                    out var rangeShape) ||
                !TryCreateBuiltInElementAccessLengthFormula(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var sourceLengthFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryCreateEffectiveRangeEndpointFormula(
                    rangeShape,
                    useStart: true,
                    sourceLengthFormula,
                    defaultWhenOmitted: new SmtIntegerConstant(0),
                    semanticModel,
                    cancellationToken,
                    out var startFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryCreateEffectiveRangeEndpointFormula(
                    rangeShape,
                    useStart: false,
                    sourceLengthFormula,
                    defaultWhenOmitted: sourceLengthFormula,
                    semanticModel,
                    cancellationToken,
                    out var endFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, endFormula, startFormula);
            return true;
        }

        private static bool TryResolveBuiltInRangeAccessRangeShape(
            ExpressionSyntax argumentExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RangeExpressionShape rangeShape)
        {
            argumentExpression = UnwrapElementAccessIndexExpression(argumentExpression);
            if (TryCreateDirectRangeExpressionShape(
                    argumentExpression,
                    semanticModel,
                    cancellationToken,
                    out rangeShape))
            {
                return true;
            }

            if (!IsSystemRangeExpression(argumentExpression, semanticModel, cancellationToken) ||
                !TryGetLocalOrParameterRangeSymbol(argumentExpression, semanticModel, cancellationToken, out var rangeSymbol))
            {
                rangeShape = default;
                return false;
            }

            return TryResolveAssignedRangeShape(
                argumentExpression,
                rangeSymbol,
                semanticModel,
                cancellationToken,
                out rangeShape);
        }

        private static bool TryGetLocalOrParameterRangeSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ISymbol rangeSymbol)
        {
            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is ILocalSymbol localSymbol &&
                IsSystemRangeType(localSymbol.Type, semanticModel.Compilation))
            {
                rangeSymbol = localSymbol;
                return true;
            }

            if (symbol is IParameterSymbol { RefKind: RefKind.None } parameterSymbol &&
                IsSystemRangeType(parameterSymbol.Type, semanticModel.Compilation))
            {
                rangeSymbol = parameterSymbol;
                return true;
            }

            rangeSymbol = null!;
            return false;
        }

        private static bool TryResolveAssignedRangeShape(
            ExpressionSyntax useExpression,
            ISymbol rangeSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RangeExpressionShape rangeShape)
        {
            rangeShape = default;
            var foundAssignment = false;
            foreach (var containingBlock in EnumerateContainingBlocks(useExpression).Reverse())
            {
                foreach (var statement in containingBlock.Block.Statements)
                {
                    if (statement == containingBlock.ContainingStatement)
                    {
                        break;
                    }

                    TryGetRangeAssignmentFromPrecedingStatement(
                        statement,
                        rangeSymbol,
                        semanticModel,
                        cancellationToken,
                        out var writesRangeSymbol,
                        out var assignedRangeShape);
                    if (!writesRangeSymbol)
                    {
                        continue;
                    }

                    if (foundAssignment ||
                        !assignedRangeShape.HasValue)
                    {
                        rangeShape = default;
                        return false;
                    }

                    rangeShape = assignedRangeShape.GetValueOrDefault();
                    foundAssignment = true;
                }
            }

            if (!foundAssignment)
            {
                rangeShape = default;
                return false;
            }

            return true;
        }

        private static IEnumerable<(BlockSyntax Block, StatementSyntax ContainingStatement)> EnumerateContainingBlocks(SyntaxNode site)
        {
            for (SyntaxNode? current = site; current != null; current = current.Parent)
            {
                if (current is StatementSyntax statement &&
                    statement.Parent is BlockSyntax block)
                {
                    yield return (block, statement);
                }
            }
        }

        private static void TryGetRangeAssignmentFromPrecedingStatement(
            StatementSyntax statement,
            ISymbol rangeSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool writesRangeSymbol,
            out RangeExpressionShape? rangeShape)
        {
            rangeShape = null;
            writesRangeSymbol = false;

            if (TryGetRangeAssignmentFromLocalDeclaration(
                    statement,
                    rangeSymbol,
                    semanticModel,
                    cancellationToken,
                    out writesRangeSymbol,
                    out rangeShape))
            {
                return;
            }

            if (TryGetRangeAssignmentFromExpressionStatement(
                    statement,
                    rangeSymbol,
                    semanticModel,
                    cancellationToken,
                    out writesRangeSymbol,
                    out rangeShape))
            {
                return;
            }

            writesRangeSymbol = ContainsRangeSymbolWrite(statement, rangeSymbol, semanticModel, cancellationToken);
        }

        private static bool TryGetRangeAssignmentFromLocalDeclaration(
            StatementSyntax statement,
            ISymbol rangeSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool writesRangeSymbol,
            out RangeExpressionShape? rangeShape)
        {
            rangeShape = null;
            writesRangeSymbol = false;
            if (statement is not LocalDeclarationStatementSyntax localDeclaration)
            {
                return false;
            }

            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);
                if (!IsSameSymbol(declaredSymbol, rangeSymbol))
                {
                    continue;
                }

                if (variable.Initializer == null)
                {
                    return true;
                }

                writesRangeSymbol = true;
                if (localDeclaration.Declaration.Variables.Count != 1 ||
                    !TryCreateDirectRangeExpressionShape(
                        variable.Initializer.Value,
                        semanticModel,
                        cancellationToken,
                        out var assignedRangeShape))
                {
                    return true;
                }

                rangeShape = assignedRangeShape;
                return true;
            }

            return false;
        }

        private static bool TryGetRangeAssignmentFromExpressionStatement(
            StatementSyntax statement,
            ISymbol rangeSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool writesRangeSymbol,
            out RangeExpressionShape? rangeShape)
        {
            rangeShape = null;
            writesRangeSymbol = false;
            if (statement is not ExpressionStatementSyntax
                {
                    Expression: AssignmentExpressionSyntax assignment
                } ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                !IsRangeSymbolReference(assignment.Left, rangeSymbol, semanticModel, cancellationToken))
            {
                return false;
            }

            writesRangeSymbol = true;
            if (TryCreateDirectRangeExpressionShape(
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    out var assignedRangeShape))
            {
                rangeShape = assignedRangeShape;
            }

            return true;
        }

        private static bool ContainsRangeSymbolWrite(
            SyntaxNode node,
            ISymbol rangeSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var assignment in node.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (IsRangeSymbolReference(assignment.Left, rangeSymbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var unary in node.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>())
            {
                if ((unary.IsKind(SyntaxKind.PreIncrementExpression) ||
                     unary.IsKind(SyntaxKind.PreDecrementExpression)) &&
                    IsRangeSymbolReference(unary.Operand, rangeSymbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var unary in node.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
            {
                if ((unary.IsKind(SyntaxKind.PostIncrementExpression) ||
                     unary.IsKind(SyntaxKind.PostDecrementExpression)) &&
                    IsRangeSymbolReference(unary.Operand, rangeSymbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var argument in node.DescendantNodes().OfType<ArgumentSyntax>())
            {
                if ((argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                     argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                    IsRangeSymbolReference(argument.Expression, rangeSymbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsRangeSymbolReference(
            ExpressionSyntax expression,
            ISymbol rangeSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return IsSameSymbol(
                semanticModel.GetSymbolInfo(UnwrapElementAccessIndexExpression(expression), cancellationToken).Symbol,
                rangeSymbol);
        }

        private static bool TryCreateDirectRangeExpressionShape(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RangeExpressionShape rangeShape)
        {
            expression = UnwrapElementAccessIndexExpression(expression);
            if (expression is RangeExpressionSyntax rangeExpression)
            {
                if (!TryCreateRangeEndpointShape(
                        rangeExpression.LeftOperand,
                        semanticModel,
                        cancellationToken,
                        out var hasStart,
                        out var start) ||
                    !TryCreateRangeEndpointShape(
                        rangeExpression.RightOperand,
                        semanticModel,
                        cancellationToken,
                        out var hasEnd,
                        out var end))
                {
                    rangeShape = default;
                    return false;
                }

                rangeShape = new RangeExpressionShape(hasStart, start, hasEnd, end);
                return true;
            }

            if (TryCreateRangeInvocationShape(expression, semanticModel, cancellationToken, out rangeShape) ||
                TryCreateRangeObjectCreationShape(expression, semanticModel, cancellationToken, out rangeShape) ||
                TryCreateRangeAllPropertyShape(expression, semanticModel, cancellationToken, out rangeShape))
            {
                return true;
            }

            rangeShape = default;
            return false;
        }

        private static bool TryCreateRangeEndpointShape(
            ExpressionSyntax? expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool hasEndpoint,
            out IndexExpressionShape endpoint)
        {
            if (expression == null)
            {
                hasEndpoint = false;
                endpoint = default;
                return true;
            }

            if (!TryResolveBuiltInIndexAccessIndexShape(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out endpoint))
            {
                hasEndpoint = false;
                return false;
            }

            hasEndpoint = true;
            return true;
        }

        private static bool TryCreateRangeInvocationShape(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RangeExpressionShape rangeShape)
        {
            rangeShape = default;
            if (expression is not InvocationExpressionSyntax invocationExpression ||
                semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod.MethodKind != MethodKind.Ordinary ||
                invocationOperation.TargetMethod.ReturnType is not { } returnType ||
                !IsSystemRangeType(returnType, semanticModel.Compilation) ||
                invocationOperation.TargetMethod.ContainingType is not { } containingType ||
                !IsSystemRangeType(containingType, semanticModel.Compilation))
            {
                return false;
            }

            if (invocationOperation.TargetMethod.Name == "StartAt")
            {
                if (!TryGetInvocationArgumentExpression(invocationOperation, parameterIndex: 0, out var startExpression) ||
                    !TryResolveBuiltInIndexAccessIndexShape(
                        startExpression,
                        semanticModel,
                        cancellationToken,
                        out var start))
                {
                    return false;
                }

                rangeShape = new RangeExpressionShape(hasStart: true, start, hasEnd: false, end: default);
                return true;
            }

            if (invocationOperation.TargetMethod.Name == "EndAt")
            {
                if (!TryGetInvocationArgumentExpression(invocationOperation, parameterIndex: 0, out var endExpression) ||
                    !TryResolveBuiltInIndexAccessIndexShape(
                        endExpression,
                        semanticModel,
                        cancellationToken,
                        out var end))
                {
                    return false;
                }

                rangeShape = new RangeExpressionShape(hasStart: false, start: default, hasEnd: true, end);
                return true;
            }

            return false;
        }

        private static bool TryCreateRangeObjectCreationShape(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RangeExpressionShape rangeShape)
        {
            rangeShape = default;
            if (expression is not ObjectCreationExpressionSyntax objectCreation ||
                semanticModel.GetOperation(objectCreation, cancellationToken) is not IObjectCreationOperation objectCreationOperation ||
                objectCreationOperation.Constructor == null ||
                !IsSystemRangeType(objectCreationOperation.Constructor.ContainingType, semanticModel.Compilation) ||
                !TryGetObjectCreationArgumentExpression(objectCreationOperation, parameterIndex: 0, out var startExpression) ||
                !TryGetObjectCreationArgumentExpression(objectCreationOperation, parameterIndex: 1, out var endExpression) ||
                !TryResolveBuiltInIndexAccessIndexShape(
                    startExpression,
                    semanticModel,
                    cancellationToken,
                    out var start) ||
                !TryResolveBuiltInIndexAccessIndexShape(
                    endExpression,
                    semanticModel,
                    cancellationToken,
                    out var end))
            {
                return false;
            }

            rangeShape = new RangeExpressionShape(hasStart: true, start, hasEnd: true, end);
            return true;
        }

        private static bool TryCreateRangeAllPropertyShape(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out RangeExpressionShape rangeShape)
        {
            rangeShape = default;
            if (semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not IPropertySymbol
                {
                    Name: "All",
                    IsStatic: true
                } propertySymbol ||
                !IsSystemRangeType(propertySymbol.ContainingType, semanticModel.Compilation) ||
                !IsSystemRangeType(propertySymbol.Type, semanticModel.Compilation))
            {
                return false;
            }

            rangeShape = new RangeExpressionShape(hasStart: false, start: default, hasEnd: false, end: default);
            return true;
        }

        private static bool IsSameSymbol(ISymbol? candidate, ISymbol target)
        {
            return candidate != null &&
                (SymbolEqualityComparer.Default.Equals(candidate, target) ||
                 SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, target.OriginalDefinition));
        }

        private static bool IsSystemRangeExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return IsSystemRangeType(typeInfo.ConvertedType ?? typeInfo.Type, semanticModel.Compilation);
        }

        private static bool IsSystemRangeType(ITypeSymbol? typeSymbol, Compilation compilation)
        {
            var rangeType = compilation.GetTypeByMetadataName("System.Range");
            return typeSymbol != null &&
                rangeType != null &&
                SymbolEqualityComparer.Default.Equals(typeSymbol, rangeType);
        }

        private static bool IsSupportedBuiltInElementAccessReceiver(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is IArrayTypeSymbol { Rank: 1 } ||
                typeSymbol?.SpecialType == SpecialType.System_String ||
                IsBuiltInSpanType(typeSymbol) ||
                HasCountBackedIntIndexer(typeSymbol);
        }

        private static bool IsSupportedBuiltInLengthReceiver(ITypeSymbol? typeSymbol)
        {
            return IsSupportedBuiltInElementAccessReceiver(typeSymbol) ||
                IsBuiltInMemoryType(typeSymbol);
        }

        private static bool HasCountBackedIntIndexer(ITypeSymbol? typeSymbol)
        {
            return TryGetCountBackedIndexerElementType(typeSymbol, out _);
        }

        private static bool TryGetCountBackedIndexerElementType(ITypeSymbol? typeSymbol, out ITypeSymbol elementType)
        {
            elementType = null!;
            if (typeSymbol == null ||
                !HasInstanceInt32Member(typeSymbol, "Count"))
            {
                return false;
            }

            return TryGetIntIndexerElementType(typeSymbol, out elementType);
        }

        private static bool TryGetIntIndexerElementType(ITypeSymbol typeSymbol, out ITypeSymbol elementType)
        {
            for (var current = typeSymbol; current != null; current = (current as INamedTypeSymbol)?.BaseType)
            {
                if (TryGetDeclaredIntIndexerElementType(current, out elementType))
                {
                    return true;
                }
            }

            foreach (var interfaceType in typeSymbol.AllInterfaces)
            {
                if (TryGetDeclaredIntIndexerElementType(interfaceType, out elementType))
                {
                    return true;
                }
            }

            elementType = null!;
            return false;
        }

        private static bool TryGetDeclaredIntIndexerElementType(ITypeSymbol typeSymbol, out ITypeSymbol elementType)
        {
            foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (property is { IsIndexer: true, IsStatic: false, Parameters.Length: 1 } &&
                    property.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
                {
                    elementType = property.Type;
                    return true;
                }
            }

            elementType = null!;
            return false;
        }

        private static bool TryGetBuiltInElementAccessElementType(
            ITypeSymbol? receiverType,
            Compilation compilation,
            out ITypeSymbol elementType)
        {
            if (receiverType is IArrayTypeSymbol arrayType)
            {
                elementType = arrayType.ElementType;
                return true;
            }

            if (receiverType?.SpecialType == SpecialType.System_String)
            {
                elementType = compilation.GetSpecialType(SpecialType.System_Char);
                return true;
            }

            if (receiverType is INamedTypeSymbol namedType &&
                IsBuiltInSpanType(namedType) &&
                namedType.TypeArguments.Length == 1)
            {
                elementType = namedType.TypeArguments[0];
                return true;
            }

            if (TryGetCountBackedIndexerElementType(receiverType, out elementType))
            {
                return true;
            }

            elementType = null!;
            return false;
        }

        private static bool TryCreateBuiltInElementAccessReceiverFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula receiverFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (TryTranslateValue(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out var translatedReceiver,
                    getSymbolVersion,
                    inlineDepth) &&
                translatedReceiver is { Kind: SmtValueKind.Reference })
            {
                receiverFormula = translatedReceiver;
                return true;
            }

            receiverExpression = UnwrapExpression(receiverExpression);
            var receiverType = semanticModel.GetTypeInfo(receiverExpression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(receiverExpression, cancellationToken).Type;
            if (!IsBuiltInSpanType(receiverType))
            {
                receiverFormula = null!;
                return false;
            }

            var receiverSymbol = semanticModel.GetSymbolInfo(receiverExpression, cancellationToken).Symbol;
            if (receiverSymbol is not ILocalSymbol and not IParameterSymbol)
            {
                receiverFormula = null!;
                return false;
            }

            receiverFormula = new SmtVariable(GetVariableName(receiverSymbol, getSymbolVersion), SmtValueKind.Reference);
            return true;
        }

        private static bool TryCreateBuiltInLengthReceiverFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula receiverFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (TryTranslateValue(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out var translatedReceiver,
                    getSymbolVersion,
                    inlineDepth) &&
                translatedReceiver is { Kind: SmtValueKind.Reference })
            {
                receiverFormula = translatedReceiver;
                return true;
            }

            receiverExpression = UnwrapExpression(receiverExpression);
            var receiverType = semanticModel.GetTypeInfo(receiverExpression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(receiverExpression, cancellationToken).Type;
            if (!IsBuiltInSpanOrMemoryType(receiverType))
            {
                receiverFormula = null!;
                return false;
            }

            var receiverSymbol = semanticModel.GetSymbolInfo(receiverExpression, cancellationToken).Symbol;
            if (receiverSymbol is not ILocalSymbol and not IParameterSymbol)
            {
                receiverFormula = null!;
                return false;
            }

            receiverFormula = new SmtVariable(GetVariableName(receiverSymbol, getSymbolVersion), SmtValueKind.Reference);
            return true;
        }

        private static bool IsBuiltInSpanType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>";
        }

        private static bool IsBuiltInMemoryType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() is "System.Memory<T>" or "System.ReadOnlyMemory<T>";
        }

        private static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? typeSymbol)
        {
            return IsBuiltInSpanType(typeSymbol) ||
                IsBuiltInMemoryType(typeSymbol);
        }

        private static bool TryResolveBuiltInIndexAccessIndexShape(
            ExpressionSyntax argumentExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out IndexExpressionShape indexShape)
        {
            argumentExpression = UnwrapElementAccessIndexExpression(argumentExpression);
            if (TryCreateDirectIndexExpressionShape(
                    argumentExpression,
                    semanticModel,
                    cancellationToken,
                    out indexShape))
            {
                return true;
            }

            if (!IsSystemIndexExpression(argumentExpression, semanticModel, cancellationToken) ||
                !TryGetLocalOrParameterIndexSymbol(argumentExpression, semanticModel, cancellationToken, out var indexSymbol))
            {
                indexShape = default;
                return false;
            }

            return TryResolveAssignedIndexShape(
                argumentExpression,
                indexSymbol,
                semanticModel,
                cancellationToken,
                out indexShape);
        }

        private static bool TryGetLocalOrParameterIndexSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ISymbol indexSymbol)
        {
            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is ILocalSymbol localSymbol &&
                IsSystemIndexType(localSymbol.Type, semanticModel.Compilation))
            {
                indexSymbol = localSymbol;
                return true;
            }

            if (symbol is IParameterSymbol { RefKind: RefKind.None } parameterSymbol &&
                IsSystemIndexType(parameterSymbol.Type, semanticModel.Compilation))
            {
                indexSymbol = parameterSymbol;
                return true;
            }

            indexSymbol = null!;
            return false;
        }

        private static bool TryResolveAssignedIndexShape(
            ExpressionSyntax useExpression,
            ISymbol indexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out IndexExpressionShape indexShape)
        {
            indexShape = default;
            var foundAssignment = false;
            foreach (var containingBlock in EnumerateContainingBlocks(useExpression).Reverse())
            {
                foreach (var statement in containingBlock.Block.Statements)
                {
                    if (statement == containingBlock.ContainingStatement)
                    {
                        break;
                    }

                    TryGetIndexAssignmentFromPrecedingStatement(
                        statement,
                        indexSymbol,
                        semanticModel,
                        cancellationToken,
                        out var writesIndexSymbol,
                        out var assignedIndexShape);
                    if (!writesIndexSymbol)
                    {
                        continue;
                    }

                    if (foundAssignment ||
                        !assignedIndexShape.HasValue)
                    {
                        indexShape = default;
                        return false;
                    }

                    indexShape = assignedIndexShape.GetValueOrDefault();
                    foundAssignment = true;
                }
            }

            if (!foundAssignment)
            {
                indexShape = default;
                return false;
            }

            return true;
        }

        private static void TryGetIndexAssignmentFromPrecedingStatement(
            StatementSyntax statement,
            ISymbol indexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool writesIndexSymbol,
            out IndexExpressionShape? indexShape)
        {
            indexShape = null;
            writesIndexSymbol = false;

            if (TryGetIndexAssignmentFromLocalDeclaration(
                    statement,
                    indexSymbol,
                    semanticModel,
                    cancellationToken,
                    out writesIndexSymbol,
                    out indexShape))
            {
                return;
            }

            if (TryGetIndexAssignmentFromExpressionStatement(
                    statement,
                    indexSymbol,
                    semanticModel,
                    cancellationToken,
                    out writesIndexSymbol,
                    out indexShape))
            {
                return;
            }

            writesIndexSymbol = ContainsIndexSymbolWrite(statement, indexSymbol, semanticModel, cancellationToken);
        }

        private static bool TryGetIndexAssignmentFromLocalDeclaration(
            StatementSyntax statement,
            ISymbol indexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool writesIndexSymbol,
            out IndexExpressionShape? indexShape)
        {
            indexShape = null;
            writesIndexSymbol = false;
            if (statement is not LocalDeclarationStatementSyntax localDeclaration)
            {
                return false;
            }

            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);
                if (!IsSameSymbol(declaredSymbol, indexSymbol))
                {
                    continue;
                }

                if (variable.Initializer == null)
                {
                    return true;
                }

                writesIndexSymbol = true;
                if (localDeclaration.Declaration.Variables.Count != 1 ||
                    !TryCreateDirectIndexExpressionShape(
                        variable.Initializer.Value,
                        semanticModel,
                        cancellationToken,
                        out var assignedIndexShape))
                {
                    return true;
                }

                indexShape = assignedIndexShape;
                return true;
            }

            return false;
        }

        private static bool TryGetIndexAssignmentFromExpressionStatement(
            StatementSyntax statement,
            ISymbol indexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool writesIndexSymbol,
            out IndexExpressionShape? indexShape)
        {
            indexShape = null;
            writesIndexSymbol = false;
            if (statement is not ExpressionStatementSyntax
                {
                    Expression: AssignmentExpressionSyntax assignment
                } ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                !IsIndexSymbolReference(assignment.Left, indexSymbol, semanticModel, cancellationToken))
            {
                return false;
            }

            writesIndexSymbol = true;
            if (TryCreateDirectIndexExpressionShape(
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    out var assignedIndexShape))
            {
                indexShape = assignedIndexShape;
            }

            return true;
        }

        private static bool TryCreateDirectIndexExpressionShape(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out IndexExpressionShape indexShape)
        {
            expression = UnwrapElementAccessIndexExpression(expression);
            if (expression is PrefixUnaryExpressionSyntax fromEndIndex &&
                fromEndIndex.OperatorToken.IsKind(SyntaxKind.CaretToken))
            {
                indexShape = new IndexExpressionShape(
                    fromEndIndex.Operand,
                    fromEnd: true,
                    requiresNonNegativeValue: true);
                return true;
            }

            if (TryCreateIndexInvocationShape(expression, semanticModel, cancellationToken, out indexShape) ||
                TryCreateIndexObjectCreationShape(expression, semanticModel, cancellationToken, out indexShape))
            {
                return true;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            if (typeInfo.Type != null &&
                IsIntegralOrEnumType(typeInfo.Type))
            {
                indexShape = new IndexExpressionShape(
                    expression,
                    fromEnd: false,
                    requiresNonNegativeValue: false);
                return true;
            }

            indexShape = default;
            return false;
        }

        private static bool TryCreateIndexInvocationShape(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out IndexExpressionShape indexShape)
        {
            indexShape = default;
            if (expression is not InvocationExpressionSyntax invocationExpression ||
                semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod.MethodKind != MethodKind.Ordinary ||
                invocationOperation.TargetMethod.ReturnType is not { } returnType ||
                !IsSystemIndexType(returnType, semanticModel.Compilation) ||
                invocationOperation.TargetMethod.ContainingType is not { } containingType ||
                !IsSystemIndexType(containingType, semanticModel.Compilation) ||
                !TryGetInvocationArgumentExpression(invocationOperation, parameterIndex: 0, out var valueExpression))
            {
                return false;
            }

            if (invocationOperation.TargetMethod.Name == "FromStart")
            {
                indexShape = new IndexExpressionShape(
                    valueExpression,
                    fromEnd: false,
                    requiresNonNegativeValue: true);
                return true;
            }

            if (invocationOperation.TargetMethod.Name == "FromEnd")
            {
                indexShape = new IndexExpressionShape(
                    valueExpression,
                    fromEnd: true,
                    requiresNonNegativeValue: true);
                return true;
            }

            return false;
        }

        private static bool TryCreateIndexObjectCreationShape(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out IndexExpressionShape indexShape)
        {
            indexShape = default;
            if (expression is not ObjectCreationExpressionSyntax objectCreation ||
                semanticModel.GetOperation(objectCreation, cancellationToken) is not IObjectCreationOperation objectCreationOperation ||
                objectCreationOperation.Constructor == null ||
                !IsSystemIndexType(objectCreationOperation.Constructor.ContainingType, semanticModel.Compilation) ||
                !TryGetObjectCreationArgumentExpression(objectCreationOperation, parameterIndex: 0, out var valueExpression))
            {
                return false;
            }

            if (!TryGetObjectCreationArgumentExpression(objectCreationOperation, parameterIndex: 1, out var fromEndExpression))
            {
                indexShape = new IndexExpressionShape(
                    valueExpression,
                    fromEnd: false,
                    requiresNonNegativeValue: true);
                return true;
            }

            if (!TryGetConstantBool(fromEndExpression, semanticModel, cancellationToken, out var fromEnd))
            {
                return false;
            }

            indexShape = new IndexExpressionShape(
                valueExpression,
                fromEnd,
                requiresNonNegativeValue: true);
            return true;
        }

        private static bool ContainsIndexSymbolWrite(
            SyntaxNode node,
            ISymbol indexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var assignment in node.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (IsIndexSymbolReference(assignment.Left, indexSymbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            foreach (var argument in node.DescendantNodes().OfType<ArgumentSyntax>())
            {
                if ((argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                     argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                    IsIndexSymbolReference(argument.Expression, indexSymbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsIndexSymbolReference(
            ExpressionSyntax expression,
            ISymbol indexSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return IsSameSymbol(
                semanticModel.GetSymbolInfo(UnwrapElementAccessIndexExpression(expression), cancellationToken).Symbol,
                indexSymbol);
        }

        private static bool IsSystemIndexExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return IsSystemIndexType(typeInfo.ConvertedType ?? typeInfo.Type, semanticModel.Compilation);
        }

        private static bool IsSystemIndexType(ITypeSymbol? typeSymbol, Compilation compilation)
        {
            var indexType = compilation.GetTypeByMetadataName("System.Index");
            return typeSymbol != null &&
                indexType != null &&
                SymbolEqualityComparer.Default.Equals(typeSymbol, indexType);
        }

        private static bool TryCreateArrayLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (receiverExpression is ArrayCreationExpressionSyntax arrayCreation)
            {
                if (arrayCreation.Type.RankSpecifiers.Count == 1 &&
                    arrayCreation.Type.RankSpecifiers[0].Sizes.Count == 1 &&
                    !arrayCreation.Type.RankSpecifiers[0].Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression) &&
                    TryTranslateValue(
                        arrayCreation.Type.RankSpecifiers[0].Sizes[0],
                        semanticModel,
                        cancellationToken,
                        out var sizeFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    sizeFormula is { Kind: SmtValueKind.Int })
                {
                    lengthFormula = sizeFormula;
                    return true;
                }

                if (arrayCreation.Initializer != null)
                {
                    lengthFormula = new SmtIntegerConstant(arrayCreation.Initializer.Expressions.Count);
                    return true;
                }
            }

            if (receiverExpression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation)
            {
                lengthFormula = new SmtIntegerConstant(implicitArrayCreation.Initializer.Expressions.Count);
                return true;
            }

            if (TryCreateCollectionExpressionLengthFormula(receiverExpression, out lengthFormula))
            {
                return true;
            }

            if (IsArrayEmptyInvocation(receiverExpression, semanticModel, cancellationToken))
            {
                lengthFormula = new SmtIntegerConstant(0);
                return true;
            }

            lengthFormula = null!;
            return false;
        }

        private static bool TryCreateArrayDimensionLengthFormula(
            ExpressionSyntax receiverExpression,
            int dimension,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            lengthFormula = null!;
            if (dimension < 0)
            {
                return false;
            }

            receiverExpression = UnwrapExpression(receiverExpression);
            if (receiverExpression is ArrayCreationExpressionSyntax arrayCreation &&
                arrayCreation.Type.RankSpecifiers.Count == 1 &&
                arrayCreation.Type.RankSpecifiers[0].Sizes.Count > dimension &&
                !arrayCreation.Type.RankSpecifiers[0].Sizes[dimension].IsKind(SyntaxKind.OmittedArraySizeExpression) &&
                TryTranslateValue(
                    arrayCreation.Type.RankSpecifiers[0].Sizes[dimension],
                    semanticModel,
                    cancellationToken,
                    out var dimensionSize,
                    getSymbolVersion,
                    inlineDepth) &&
                dimensionSize is { Kind: SmtValueKind.Int })
            {
                lengthFormula = dimensionSize;
                return true;
            }

            if (TryCreateReferenceCastArrayDimensionLengthFormula(
                    receiverExpression,
                    dimension,
                    semanticModel,
                    cancellationToken,
                    out lengthFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            var receiverType = semanticModel.GetTypeInfo(receiverExpression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(receiverExpression, cancellationToken).Type;
            if (receiverType is not IArrayTypeSymbol arrayType ||
                dimension >= arrayType.Rank ||
                !TryTranslateValue(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                receiverFormula is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (!TryCreateMemberFormula(
                    receiverFormula,
                    "GetLength(" + dimension.ToString(CultureInfo.InvariantCulture) + ")",
                    intType,
                    out var candidate) ||
                candidate is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            lengthFormula = candidate;
            return true;
        }

        private static bool TryCreateReferenceCastArrayDimensionLengthFormula(
            ExpressionSyntax receiverExpression,
            int dimension,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            lengthFormula = null!;
            if (UnwrapExpression(receiverExpression) is not CastExpressionSyntax castExpression ||
                !TryTranslateNonUserDefinedReferenceCastOperand(
                    castExpression,
                    semanticModel,
                    cancellationToken,
                    out var operandReference,
                    out var targetType,
                    getSymbolVersion,
                    inlineDepth) ||
                targetType is not IArrayTypeSymbol arrayType ||
                dimension >= arrayType.Rank)
            {
                return false;
            }

            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (TryCreateMemberFormula(
                    operandReference,
                    "GetLength(" + dimension.ToString(CultureInfo.InvariantCulture) + ")",
                    intType,
                    out var candidate) &&
                candidate is { Kind: SmtValueKind.Int })
            {
                lengthFormula = candidate;
                return true;
            }

            return false;
        }

        private static bool TryGetConstantNonNegativeInt(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out int value)
        {
            value = 0;
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (!constantValue.HasValue ||
                constantValue.Value == null ||
                !TryGetIntegralConstant(constantValue.Value, out var integralValue) ||
                integralValue < 0 ||
                integralValue > int.MaxValue)
            {
                return false;
            }

            value = (int)integralValue;
            return true;
        }

        private static bool TryGetConstantBool(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out bool value)
        {
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue &&
                constantValue.Value is bool booleanValue)
            {
                value = booleanValue;
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryCreateCollectionExpressionLengthFormula(
            ExpressionSyntax receiverExpression,
            out SmtFormula lengthFormula)
        {
            if (receiverExpression is not CollectionExpressionSyntax collectionExpression ||
                collectionExpression.Elements.Any(static element => element is not ExpressionElementSyntax))
            {
                lengthFormula = null!;
                return false;
            }

            lengthFormula = new SmtIntegerConstant(collectionExpression.Elements.Count);
            return true;
        }

        private static bool IsArrayEmptyInvocation(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return receiverExpression is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol
                {
                    Name: "Empty",
                    IsStatic: true,
                    ContainingType.SpecialType: SpecialType.System_Array
                };
        }

        private static bool TryCreateEffectiveBuiltInIndexFormula(
            ExpressionSyntax indexExpression,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula indexFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (!TryResolveBuiltInIndexAccessIndexShape(
                    indexExpression,
                    semanticModel,
                    cancellationToken,
                    out var indexShape) ||
                !TryCreateEffectiveBuiltInIndexFormula(
                    indexShape,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out indexFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                indexFormula = null!;
                return false;
            }

            return true;
        }

        private static bool TryCreateEffectiveBuiltInIndexFormula(
            IndexExpressionShape indexShape,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula indexFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (!TryTranslateValue(
                    indexShape.ValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var rawIndex,
                    getSymbolVersion,
                    inlineDepth) ||
                rawIndex is not { Kind: SmtValueKind.Int })
            {
                indexFormula = null!;
                return false;
            }

            indexFormula = indexShape.FromEnd
                ? new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, lengthFormula, rawIndex)
                : rawIndex;
            return true;
        }

        private static bool TryCreateIndexShapeWellFormedFormula(
            IndexExpressionShape indexShape,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            if (!indexShape.RequiresNonNegativeValue)
            {
                return true;
            }

            if (!TryTranslateValue(
                    indexShape.ValueExpression,
                    semanticModel,
                    cancellationToken,
                    out var rawIndex,
                    getSymbolVersion,
                    inlineDepth) ||
                rawIndex is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            formula = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                rawIndex,
                new SmtIntegerConstant(0));
            return true;
        }

        private static bool TryCreateRangeShapeWellFormedFormula(
            RangeExpressionShape rangeShape,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            SmtFormula? startWellFormed = null;
            SmtFormula? endWellFormed = null;
            if (rangeShape.HasStart &&
                !TryCreateIndexShapeWellFormedFormula(
                    rangeShape.Start,
                    semanticModel,
                    cancellationToken,
                    out startWellFormed,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            if (rangeShape.HasEnd &&
                !TryCreateIndexShapeWellFormedFormula(
                    rangeShape.End,
                    semanticModel,
                    cancellationToken,
                    out endWellFormed,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            if (startWellFormed != null)
            {
                formula = startWellFormed;
            }

            if (endWellFormed != null)
            {
                formula = CombineConjunction(formula, endWellFormed);
            }

            return true;
        }

        private static SmtFormula CombineConjunction(SmtFormula? left, SmtFormula? right)
        {
            if (left == null)
            {
                return right ?? new SmtBooleanConstant(true);
            }

            if (right == null)
            {
                return left;
            }

            return new SmtBinaryFormula(SmtBinaryOperator.And, left, right);
        }

        private static SmtFormula ApplyWellFormedPrecondition(SmtFormula? wellFormed, SmtFormula inRange)
        {
            if (wellFormed == null)
            {
                return inRange;
            }

            return new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                new SmtUnaryFormula(SmtUnaryOperator.Not, wellFormed),
                inRange);
        }

        private static bool TryCreateBuiltInRangeAccessInRangeFormula(
            ExpressionSyntax argumentExpression,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            if (!TryResolveBuiltInRangeAccessRangeShape(
                    argumentExpression,
                    semanticModel,
                    cancellationToken,
                    out var rangeShape) ||
                !TryCreateEffectiveRangeEndpointFormula(
                    rangeShape,
                    useStart: true,
                    lengthFormula,
                    defaultWhenOmitted: new SmtIntegerConstant(0),
                    semanticModel,
                    cancellationToken,
                    out var startFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryCreateEffectiveRangeEndpointFormula(
                    rangeShape,
                    useStart: false,
                    lengthFormula,
                    defaultWhenOmitted: lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out var endFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            var nonNegativeStart = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                startFormula,
                new SmtIntegerConstant(0));
            var orderedEndpoints = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                startFormula,
                endFormula);
            var endWithinLength = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                endFormula,
                lengthFormula);
            formula = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                nonNegativeStart,
                new SmtBinaryFormula(SmtBinaryOperator.And, orderedEndpoints, endWithinLength));
            if (!TryCreateRangeShapeWellFormedFormula(
                    rangeShape,
                    semanticModel,
                    cancellationToken,
                    out var rangeWellFormed,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            formula = ApplyWellFormedPrecondition(rangeWellFormed, formula);
            return true;
        }

        private static bool TryCreateEffectiveRangeEndpointFormula(
            RangeExpressionShape rangeShape,
            bool useStart,
            SmtFormula lengthFormula,
            SmtFormula defaultWhenOmitted,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula endpointFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            var hasEndpoint = useStart ? rangeShape.HasStart : rangeShape.HasEnd;
            if (!hasEndpoint)
            {
                endpointFormula = defaultWhenOmitted;
                return true;
            }

            return TryCreateEffectiveBuiltInIndexFormula(
                useStart ? rangeShape.Start : rangeShape.End,
                lengthFormula,
                semanticModel,
                cancellationToken,
                out endpointFormula,
                getSymbolVersion,
                inlineDepth);
        }

        private static ExpressionSyntax UnwrapElementAccessIndexExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is CheckedExpressionSyntax checkedExpression &&
                    (checkedExpression.IsKind(SyntaxKind.CheckedExpression) ||
                     checkedExpression.IsKind(SyntaxKind.UncheckedExpression)))
                {
                    expression = checkedExpression.Expression;
                    continue;
                }

                return expression;
            }
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

        private static bool TryTranslateComparison(
            SyntaxKind kind,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula? formula)
        {
            formula = null;
            switch (kind)
            {
                case SyntaxKind.EqualsExpression:
                    if (AreComparable(left, right))
                    {
                        formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);
                        return true;
                    }

                    return false;
                case SyntaxKind.NotEqualsExpression:
                    if (AreComparable(left, right))
                    {
                        formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, right);
                        return true;
                    }

                    return false;
                case SyntaxKind.LessThanExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.LessThan, left, right, out formula);
                case SyntaxKind.LessThanOrEqualExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.LessThanOrEqual, left, right, out formula);
                case SyntaxKind.GreaterThanExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.GreaterThan, left, right, out formula);
                case SyntaxKind.GreaterThanOrEqualExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.GreaterThanOrEqual, left, right, out formula);
                default:
                    return false;
            }
        }

        private static bool TryCreateIntegralComparison(
            SmtBinaryOperator comparison,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula? formula)
        {
            formula = null;
            if (left.Kind != SmtValueKind.Int || right.Kind != SmtValueKind.Int)
            {
                return false;
            }

            formula = new SmtBinaryFormula(comparison, left, right);
            return true;
        }

        private static bool AreComparable(SmtFormula left, SmtFormula right)
        {
            if (left.Kind == right.Kind)
            {
                return true;
            }

            return (left is SmtNullConstant && right.Kind == SmtValueKind.Reference) ||
                (right is SmtNullConstant && left.Kind == SmtValueKind.Reference);
        }

        private static ISet<string>? AddNonZeroDivisorFacts(
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ISet<string>? currentFacts,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            var facts = currentFacts == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(currentFacts, StringComparer.Ordinal);
            var initialCount = facts.Count;
            CollectNonZeroDivisorFacts(
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                facts,
                getSymbolVersion,
                inlineDepth);

            return facts.Count == initialCount ? currentFacts : facts;
        }

        private static void CollectNonZeroDivisorFacts(
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ISet<string> facts,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            condition = UnwrapExpression(condition);

            if (condition is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                CollectNonZeroDivisorFacts(
                    prefixUnary.Operand,
                    !branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    facts,
                    getSymbolVersion,
                    inlineDepth);
                return;
            }

            if (condition is not BinaryExpressionSyntax binaryExpression)
            {
                return;
            }

            if (branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
            {
                CollectNonZeroDivisorFacts(binaryExpression.Left, branchWhenTrue: true, semanticModel, cancellationToken, facts, getSymbolVersion, inlineDepth);
                CollectNonZeroDivisorFacts(binaryExpression.Right, branchWhenTrue: true, semanticModel, cancellationToken, facts, getSymbolVersion, inlineDepth);
                return;
            }

            if (!branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
            {
                CollectNonZeroDivisorFacts(binaryExpression.Left, branchWhenTrue: false, semanticModel, cancellationToken, facts, getSymbolVersion, inlineDepth);
                CollectNonZeroDivisorFacts(binaryExpression.Right, branchWhenTrue: false, semanticModel, cancellationToken, facts, getSymbolVersion, inlineDepth);
                return;
            }

            if (!IsNonZeroComparisonKind(binaryExpression.Kind(), branchWhenTrue))
            {
                return;
            }

            if (!TryGetZeroComparisonCandidate(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var candidate))
            {
                return;
            }

            if (TryTranslateValue(candidate, semanticModel, cancellationToken, out var candidateFormula, getSymbolVersion, inlineDepth) &&
                candidateFormula is { Kind: SmtValueKind.Int } &&
                !IsZeroIntegerConstant(candidateFormula))
            {
                facts.Add(CreateDivisorKey(candidateFormula));
            }
        }

        private static bool IsNonZeroComparisonKind(SyntaxKind kind, bool branchWhenTrue)
        {
            return branchWhenTrue
                ? kind is SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression or SyntaxKind.GreaterThanExpression
                : kind is SyntaxKind.EqualsExpression or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanOrEqualExpression;
        }

        private static bool TryGetZeroComparisonCandidate(
            ExpressionSyntax left,
            ExpressionSyntax right,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax candidate)
        {
            if (IsZeroIntegralExpression(right, semanticModel, cancellationToken))
            {
                candidate = left;
                return true;
            }

            if (IsZeroIntegralExpression(left, semanticModel, cancellationToken))
            {
                candidate = right;
                return true;
            }

            candidate = null!;
            return false;
        }

        private static bool IsZeroIntegralExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return TryTranslateValue(expression, semanticModel, cancellationToken, out var formula, getSymbolVersion: null) &&
                IsZeroIntegerConstant(formula);
        }

        private static bool IsZeroIntegerConstant(SmtFormula? formula)
        {
            return formula is SmtIntegerConstant integerConstant && integerConstant.Value == 0;
        }

        private static string CreateDivisorKey(SmtFormula formula)
        {
            return formula.ToString();
        }

        public static bool TryTranslateValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth = 0)
        {
            return TryTranslateCached(
                "value",
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                TryTranslateValueCore);
        }

        public static bool TryTranslateValueWithPathFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            IEnumerable<SmtFormula>? pathFacts,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return TryTranslateValueWithSafeDivisors(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                CollectNonZeroDivisorFacts(pathFacts));
        }

        private static bool TryTranslateValueWithSafeDivisors(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string>? nonZeroDivisors)
        {
            if (TryTranslateValue(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (nonZeroDivisors == null ||
                nonZeroDivisors.Count == 0)
            {
                return false;
            }

            return TryTranslateIntegralTermWithSafeDivisors(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                nonZeroDivisors);
        }

        private static ISet<string>? CollectNonZeroDivisorFacts(IEnumerable<SmtFormula>? pathFacts)
        {
            if (pathFacts == null)
            {
                return null;
            }

            HashSet<string>? facts = null;
            foreach (var pathFact in pathFacts)
            {
                CollectNonZeroDivisorFacts(pathFact, ref facts);
            }

            return facts;
        }

        private static void CollectNonZeroDivisorFacts(SmtFormula formula, ref HashSet<string>? facts)
        {
            switch (formula)
            {
                case SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula:
                    CollectNonZeroDivisorFacts(andFormula.Left, ref facts);
                    CollectNonZeroDivisorFacts(andFormula.Right, ref facts);
                    return;
                case SmtUnaryFormula { Operator: SmtUnaryOperator.Not, Operand: SmtBinaryFormula negatedComparison }
                    when TryNormalizeIntegerComparisonToConstant(
                        negatedComparison,
                        out var negatedExpression,
                        out var negatedOperator,
                        out var negatedConstant) &&
                        TryNegateIntegerComparison(negatedOperator, out var inverseOperator) &&
                        IntegerComparisonExcludesZero(inverseOperator, negatedConstant):
                    AddNonZeroDivisorFact(negatedExpression, ref facts);
                    return;
                case SmtBinaryFormula comparison
                    when TryNormalizeIntegerComparisonToConstant(
                        comparison,
                        out var expression,
                        out var comparisonOperator,
                        out var constant) &&
                        IntegerComparisonExcludesZero(comparisonOperator, constant):
                    AddNonZeroDivisorFact(expression, ref facts);
                    return;
            }
        }

        private static void AddNonZeroDivisorFact(SmtFormula expression, ref HashSet<string>? facts)
        {
            if (expression.Kind != SmtValueKind.Int)
            {
                return;
            }

            facts ??= new HashSet<string>(StringComparer.Ordinal);
            facts.Add(CreateDivisorKey(expression));
        }

        private static bool TryNormalizeIntegerComparisonToConstant(
            SmtBinaryFormula formula,
            out SmtFormula expression,
            out SmtBinaryOperator op,
            out long constant)
        {
            if (formula.Left is SmtIntegerConstant leftConstant && formula.Right.Kind == SmtValueKind.Int)
            {
                expression = formula.Right;
                op = SwapIntegerComparisonOperator(formula.Operator);
                constant = leftConstant.Value;
                return IsIntegerComparisonOperator(op);
            }

            if (formula.Right is SmtIntegerConstant rightConstant && formula.Left.Kind == SmtValueKind.Int)
            {
                expression = formula.Left;
                op = formula.Operator;
                constant = rightConstant.Value;
                return IsIntegerComparisonOperator(op);
            }

            expression = null!;
            op = default;
            constant = default;
            return false;
        }

        private static bool IsIntegerComparisonOperator(SmtBinaryOperator op)
        {
            return op is SmtBinaryOperator.Equal or
                SmtBinaryOperator.NotEqual or
                SmtBinaryOperator.LessThan or
                SmtBinaryOperator.LessThanOrEqual or
                SmtBinaryOperator.GreaterThan or
                SmtBinaryOperator.GreaterThanOrEqual;
        }

        private static SmtBinaryOperator SwapIntegerComparisonOperator(SmtBinaryOperator op)
        {
            return op switch
            {
                SmtBinaryOperator.LessThan => SmtBinaryOperator.GreaterThan,
                SmtBinaryOperator.LessThanOrEqual => SmtBinaryOperator.GreaterThanOrEqual,
                SmtBinaryOperator.GreaterThan => SmtBinaryOperator.LessThan,
                SmtBinaryOperator.GreaterThanOrEqual => SmtBinaryOperator.LessThanOrEqual,
                _ => op,
            };
        }

        private static bool TryNegateIntegerComparison(SmtBinaryOperator op, out SmtBinaryOperator negated)
        {
            switch (op)
            {
                case SmtBinaryOperator.Equal:
                    negated = SmtBinaryOperator.NotEqual;
                    return true;
                case SmtBinaryOperator.NotEqual:
                    negated = SmtBinaryOperator.Equal;
                    return true;
                case SmtBinaryOperator.LessThan:
                    negated = SmtBinaryOperator.GreaterThanOrEqual;
                    return true;
                case SmtBinaryOperator.LessThanOrEqual:
                    negated = SmtBinaryOperator.GreaterThan;
                    return true;
                case SmtBinaryOperator.GreaterThan:
                    negated = SmtBinaryOperator.LessThanOrEqual;
                    return true;
                case SmtBinaryOperator.GreaterThanOrEqual:
                    negated = SmtBinaryOperator.LessThan;
                    return true;
                default:
                    negated = default;
                    return false;
            }
        }

        private static bool IntegerComparisonExcludesZero(SmtBinaryOperator op, long constant)
        {
            return !EvaluateIntegerComparison(op, 0, constant);
        }

        private static bool EvaluateIntegerComparison(SmtBinaryOperator op, long left, long right)
        {
            return op switch
            {
                SmtBinaryOperator.Equal => left == right,
                SmtBinaryOperator.NotEqual => left != right,
                SmtBinaryOperator.LessThan => left < right,
                SmtBinaryOperator.LessThanOrEqual => left <= right,
                SmtBinaryOperator.GreaterThan => left > right,
                SmtBinaryOperator.GreaterThanOrEqual => left >= right,
                _ => false,
            };
        }

        private static bool TryTranslateValueCore(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth = 0)
        {
            expression = UnwrapExpression(expression);
            formula = null;

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue)
            {
                if (constantValue.Value is bool booleanValue)
                {
                    formula = new SmtBooleanConstant(booleanValue);
                    return true;
                }

                if (constantValue.Value == null)
                {
                    formula = new SmtNullConstant();
                    return true;
                }

                if (TryGetIntegralConstant(constantValue.Value, out var integralValue))
                {
                    formula = new SmtIntegerConstant(integralValue);
                    return true;
                }
            }

            if (TryTranslateDefaultValue(expression, semanticModel, cancellationToken, out formula))
            {
                return true;
            }

            if (expression is ThisExpressionSyntax)
            {
                formula = new SmtVariable(ImplicitThisVariableName, SmtValueKind.Reference);
                return true;
            }

            if (expression is CastExpressionSyntax referenceCastExpression &&
                TryTranslateIdentityPreservingReferenceCastValue(
                    referenceCastExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax asExpression &&
                asExpression.IsKind(SyntaxKind.AsExpression) &&
                TryTranslateIdentityPreservingAsValue(
                    asExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (expression is ElementAccessExpressionSyntax elementAccessExpression &&
                TryTranslateBuiltInElementAccessValue(
                    elementAccessExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (expression is ConditionalAccessExpressionSyntax conditionalAccessExpression &&
                TryTranslateConditionalAccessReferenceValue(
                    conditionalAccessExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion, inlineDepth) &&
                conditionFormula != null &&
                TryTranslateValue(conditionalExpression.WhenTrue, semanticModel, cancellationToken, out var whenTrueFormula, getSymbolVersion, inlineDepth) &&
                whenTrueFormula != null &&
                TryTranslateValue(conditionalExpression.WhenFalse, semanticModel, cancellationToken, out var whenFalseFormula, getSymbolVersion, inlineDepth) &&
                whenFalseFormula != null &&
                whenTrueFormula.Kind == whenFalseFormula.Kind)
            {
                formula = new SmtConditionalFormula(conditionFormula, whenTrueFormula, whenFalseFormula, whenTrueFormula.Kind);
                return true;
            }

            if (expression is SwitchExpressionSyntax switchExpression &&
                TryTranslateSwitchExpressionValue(switchExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax nullableCoalesceExpression &&
                nullableCoalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryTranslateNullableCoalesceValue(
                    nullableCoalesceExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryTranslateValue(coalesceExpression.Left, semanticModel, cancellationToken, out var coalesceLeft, getSymbolVersion, inlineDepth) &&
                coalesceLeft is { Kind: SmtValueKind.Reference } &&
                TryTranslateValue(coalesceExpression.Right, semanticModel, cancellationToken, out var coalesceRight, getSymbolVersion, inlineDepth) &&
                coalesceRight is { Kind: SmtValueKind.Reference })
            {
                formula = new SmtConditionalFormula(
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, coalesceLeft, new SmtNullConstant()),
                    coalesceLeft,
                    coalesceRight,
                    SmtValueKind.Reference);
                return true;
            }

            if (TryTranslateBooleanTerm(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (TryTranslateIntegralTerm(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is not ILocalSymbol && symbol is not IParameterSymbol)
            {
                return TryTranslateMemberValue(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            if (type == null)
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Bool);
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Int);
                return true;
            }

            if (IsReferenceLikeType(type))
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Reference);
                return true;
            }

            if (IsSupportedTupleCarrierType(type))
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Reference);
                return true;
            }

            return false;
        }

        private delegate bool FormulaTranslator(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth);

        private static bool TryTranslateCached(
            string kind,
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            FormulaTranslator translator)
        {
            expression = UnwrapExpression(expression);
            var cache = s_expressionFormulaCache.GetValue(
                semanticModel.Compilation,
                static _ => new ConcurrentDictionary<ExpressionFormulaCacheKey, SourceBooleanFormulaCacheEntry>());
            var cacheKey = new ExpressionFormulaCacheKey(
                kind,
                expression.SyntaxTree,
                expression.SpanStart,
                expression.Span.Length,
                inlineDepth,
                getSymbolVersion == null
                    ? string.Empty
                    : CreateSymbolVersionCacheKey(expression, semanticModel, cancellationToken, getSymbolVersion));
            var entry = cache.GetOrAdd(
                cacheKey,
                _ =>
                {
                    var success = translator(
                        expression,
                        semanticModel,
                        cancellationToken,
                        out var translatedFormula,
                        getSymbolVersion,
                        inlineDepth);
                    return new SourceBooleanFormulaCacheEntry(success, translatedFormula);
                });

            formula = entry.Formula;
            return entry.Success;
        }

        private static string CreateSymbolVersionCacheKey(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int> getSymbolVersion)
        {
            var symbols = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in expression.DescendantNodesAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is not IdentifierNameSyntax &&
                    node is not MemberAccessExpressionSyntax &&
                    node is not MemberBindingExpressionSyntax)
                {
                    continue;
                }

                var symbol = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol?.OriginalDefinition;
                if (symbol is not ILocalSymbol &&
                    symbol is not IParameterSymbol &&
                    symbol is not IFieldSymbol &&
                    symbol is not IPropertySymbol)
                {
                    continue;
                }

                symbols.Add(GetVersionedSymbolCachePart(symbol, getSymbolVersion));
            }

            return symbols.Count == 0
                ? string.Empty
                : string.Join(";", symbols.OrderBy(static symbol => symbol, StringComparer.Ordinal));
        }

        private static string GetVersionedSymbolCachePart(ISymbol symbol, Func<ISymbol, int> getSymbolVersion)
        {
            var start = symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;
            var version = getSymbolVersion(symbol.OriginalDefinition);
            return symbol.Kind.ToString() +
                ":" +
                symbol.Name +
                "#" +
                start.ToString(CultureInfo.InvariantCulture) +
                "@v" +
                version.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryTranslateDefaultValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (!expression.IsKind(SyntaxKind.DefaultLiteralExpression) &&
                expression is not DefaultExpressionSyntax)
            {
                return false;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type == null)
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtBooleanConstant(false);
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                formula = new SmtIntegerConstant(0);
                return true;
            }

            if (IsReferenceLikeType(type))
            {
                formula = new SmtNullConstant();
                return true;
            }

            return false;
        }

        private static bool TryCreateDefaultValueFormula(ITypeSymbol type, out SmtFormula? formula)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtBooleanConstant(false);
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                formula = new SmtIntegerConstant(0);
                return true;
            }

            if (IsReferenceLikeType(type))
            {
                formula = new SmtNullConstant();
                return true;
            }

            formula = null;
            return false;
        }

        private static bool TryTranslateSwitchExpressionValue(
            SwitchExpressionSyntax switchExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (switchExpression.Arms.Count < 2 ||
                !HasUnguardedDiscardFallback(switchExpression.Arms[switchExpression.Arms.Count - 1]))
            {
                return false;
            }

            var armConditions = new List<SmtFormula>();
            var armValues = new List<SmtFormula>();
            foreach (var arm in switchExpression.Arms)
            {
                if (!TryTranslateValue(arm.Expression, semanticModel, cancellationToken, out var armValue, getSymbolVersion, inlineDepth) ||
                    armValue == null ||
                    !SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                        switchExpression.GoverningExpression,
                        arm,
                        semanticModel,
                        cancellationToken,
                        out var armCondition,
                        getSymbolVersion))
                {
                    formula = null;
                    return false;
                }

                if (armValues.Count > 0 &&
                    armValues[0].Kind != armValue.Kind)
                {
                    formula = null;
                    return false;
                }

                armConditions.Add(armCondition);
                armValues.Add(armValue);
            }

            var result = armValues[armValues.Count - 1];
            for (var index = armValues.Count - 2; index >= 0; index--)
            {
                result = new SmtConditionalFormula(
                    armConditions[index],
                    armValues[index],
                    result,
                    result.Kind);
            }

            formula = result;
            return true;
        }

        private static bool HasUnguardedDiscardFallback(SwitchExpressionArmSyntax arm)
        {
            return arm.WhenClause == null &&
                arm.Pattern is DiscardPatternSyntax;
        }

        private static bool TryTranslateBooleanTerm(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!HasSupportedBooleanType(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                TryTranslate(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth) &&
                operand != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, operand);
                return true;
            }

            if (expression is CastExpressionSyntax booleanCastExpression &&
                IsIdentityPreservingBooleanCast(booleanCastExpression, semanticModel, cancellationToken) &&
                TryTranslate(booleanCastExpression.Expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth) &&
                formula is { Kind: SmtValueKind.Bool })
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftAnd, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightAnd, getSymbolVersion, inlineDepth) &&
                    leftAnd != null &&
                    rightAnd != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftAnd, rightAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.BitwiseAndExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftBitwiseAnd, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightBitwiseAnd, getSymbolVersion, inlineDepth) &&
                    leftBitwiseAnd != null &&
                    rightBitwiseAnd != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftBitwiseAnd, rightBitwiseAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightOr, getSymbolVersion, inlineDepth) &&
                    leftOr != null &&
                    rightOr != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftOr, rightOr);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.BitwiseOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftBitwiseOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightBitwiseOr, getSymbolVersion, inlineDepth) &&
                    leftBitwiseOr != null &&
                    rightBitwiseOr != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftBitwiseOr, rightBitwiseOr);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.ExclusiveOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftExclusiveOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightExclusiveOr, getSymbolVersion, inlineDepth) &&
                    leftExclusiveOr is { Kind: SmtValueKind.Bool } &&
                    rightExclusiveOr is { Kind: SmtValueKind.Bool })
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, leftExclusiveOr, rightExclusiveOr);
                    return true;
                }

                if (TryTranslateUnsignedCastBoundsComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var unsignedBoundsFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    unsignedBoundsFormula != null)
                {
                    formula = unsignedBoundsFormula;
                    return true;
                }

                if (TryTranslateStringComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var stringComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    stringComparison != null)
                {
                    formula = stringComparison;
                    return true;
                }

                if (IsTupleEqualityComparison(binaryExpression, semanticModel, cancellationToken))
                {
                    return TryTranslateTupleEqualityComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out formula,
                        getSymbolVersion,
                        inlineDepth,
                        nonZeroDivisors: null) &&
                        formula != null;
                }

                if (TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue, getSymbolVersion, inlineDepth) &&
                    leftValue != null &&
                    rightValue != null &&
                    TryTranslateComparison(binaryExpression.Kind(), leftValue, rightValue, out var comparison))
                {
                    formula = comparison;
                    return true;
                }
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression)
            {
                return TryTranslatePatternExpression(isPatternExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            if (expression is InvocationExpressionSyntax invocationExpression)
            {
                if (TryTranslateKnownStringBooleanInvocation(invocationExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
                {
                    return formula != null;
                }

                return TryTranslateSourceBooleanInvocation(invocationExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            return false;
        }

        private static bool TryTranslateIntegralTerm(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!HasSupportedIntegralType(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            if (expression is InvocationExpressionSyntax invocationExpression &&
                TryTranslateArrayGetLengthInvocation(
                    invocationExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary)
            {
                if (prefixUnary.IsKind(SyntaxKind.UnaryPlusExpression))
                {
                    return TryTranslateValue(prefixUnary.Operand, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth) &&
                        formula is { Kind: SmtValueKind.Int };
                }

                if (prefixUnary.IsKind(SyntaxKind.UnaryMinusExpression) &&
                    TryTranslateValue(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth) &&
                    operand is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, operand);
                    return true;
                }
            }

            if (expression is CastExpressionSyntax castExpression &&
                IsRepresentationPreservingIntegralCast(castExpression, semanticModel, cancellationToken) &&
                TryTranslateValue(castExpression.Expression, semanticModel, cancellationToken, out var castOperand, getSymbolVersion, inlineDepth) &&
                castOperand is { Kind: SmtValueKind.Int })
            {
                formula = castOperand;
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var addLeft, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var addRight, getSymbolVersion, inlineDepth) &&
                    addLeft is { Kind: SmtValueKind.Int } &&
                    addRight is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, addLeft, addRight);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var subtractLeft, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var subtractRight, getSymbolVersion, inlineDepth) &&
                    subtractLeft is { Kind: SmtValueKind.Int } &&
                    subtractRight is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, subtractLeft, subtractRight);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.MultiplyExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var multiplyLeft, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var multiplyRight, getSymbolVersion, inlineDepth) &&
                    multiplyLeft is { Kind: SmtValueKind.Int } &&
                    multiplyRight is { Kind: SmtValueKind.Int } &&
                    (multiplyLeft is SmtIntegerConstant || multiplyRight is SmtIntegerConstant))
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, multiplyLeft, multiplyRight);
                    return true;
                }

                if ((binaryExpression.IsKind(SyntaxKind.DivideExpression) ||
                        binaryExpression.IsKind(SyntaxKind.ModuloExpression)) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var dividend, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var divisor, getSymbolVersion, inlineDepth) &&
                    dividend is { Kind: SmtValueKind.Int } &&
                    divisor is { Kind: SmtValueKind.Int } &&
                    IsSafeIntegerDivisor(divisor, nonZeroDivisors: null))
                {
                    formula = new SmtIntegerBinaryTerm(
                        binaryExpression.IsKind(SyntaxKind.DivideExpression)
                            ? SmtIntegerBinaryOperator.Divide
                            : SmtIntegerBinaryOperator.Remainder,
                        dividend,
                        divisor);
                    return true;
                }
            }

            return false;
        }

        private static bool TryTranslateArrayGetLengthInvocation(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod.Name != "GetLength" ||
                invocationOperation.TargetMethod.Parameters.Length != 1 ||
                invocationOperation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Int32 ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                invocationOperation.Instance.Type is not IArrayTypeSymbol arrayType ||
                !TryGetInvocationArgumentExpression(invocationOperation, parameterIndex: 0, out var dimensionExpression) ||
                !TryGetConstantNonNegativeInt(dimensionExpression, semanticModel, cancellationToken, out var dimension) ||
                dimension >= arrayType.Rank)
            {
                return false;
            }

            return TryCreateArrayDimensionLengthFormula(
                receiverExpression,
                dimension,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        private static bool TryTranslateIntegralTermWithSafeDivisors(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string> nonZeroDivisors)
        {
            formula = null;
            expression = UnwrapExpression(expression);
            if (!HasSupportedIntegralType(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            if (expression is InvocationExpressionSyntax mathAbsInvocation &&
                TryTranslateSafeMathAbsRemainder(
                    mathAbsInvocation,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors))
            {
                return true;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary)
            {
                if (prefixUnary.IsKind(SyntaxKind.UnaryPlusExpression))
                {
                    return TryTranslateIntegralOperandWithSafeDivisors(
                            prefixUnary.Operand,
                            semanticModel,
                            cancellationToken,
                            out formula,
                            getSymbolVersion,
                            inlineDepth,
                            nonZeroDivisors) &&
                        formula is { Kind: SmtValueKind.Int };
                }

                if (prefixUnary.IsKind(SyntaxKind.UnaryMinusExpression) &&
                    TryTranslateIntegralOperandWithSafeDivisors(
                        prefixUnary.Operand,
                        semanticModel,
                        cancellationToken,
                        out var operand,
                        getSymbolVersion,
                        inlineDepth,
                        nonZeroDivisors) &&
                    operand is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, operand);
                    return true;
                }
            }

            if (expression is CastExpressionSyntax castExpression &&
                IsRepresentationPreservingIntegralCast(castExpression, semanticModel, cancellationToken) &&
                TryTranslateIntegralOperandWithSafeDivisors(
                    castExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    out var castOperand,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) &&
                castOperand is { Kind: SmtValueKind.Int })
            {
                formula = castOperand;
                return true;
            }

            if (expression is not BinaryExpressionSyntax binaryExpression)
            {
                return false;
            }

            if (!TryTranslateIntegralOperandWithSafeDivisors(
                    binaryExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var left,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) ||
                left is not { Kind: SmtValueKind.Int } ||
                !TryTranslateIntegralOperandWithSafeDivisors(
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var right,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) ||
                right is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            if (binaryExpression.IsKind(SyntaxKind.AddExpression))
            {
                formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, left, right);
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.SubtractExpression))
            {
                formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, left, right);
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.MultiplyExpression) &&
                (left is SmtIntegerConstant || right is SmtIntegerConstant))
            {
                formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, left, right);
                return true;
            }

            if ((binaryExpression.IsKind(SyntaxKind.DivideExpression) ||
                    binaryExpression.IsKind(SyntaxKind.ModuloExpression)) &&
                IsSafeIntegerDivisor(right, nonZeroDivisors))
            {
                formula = new SmtIntegerBinaryTerm(
                    binaryExpression.IsKind(SyntaxKind.DivideExpression)
                        ? SmtIntegerBinaryOperator.Divide
                        : SmtIntegerBinaryOperator.Remainder,
                    left,
                    right);
                return true;
            }

            return false;
        }

        private static bool TryTranslateSafeMathAbsRemainder(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string> nonZeroDivisors)
        {
            formula = null;
            if (!TryGetMathAbsRemainderOperands(
                    invocationExpression,
                    semanticModel,
                    cancellationToken,
                    out var dividendExpression,
                    out var divisorExpression) ||
                !TryTranslateIntegralOperandWithSafeDivisors(
                    dividendExpression,
                    semanticModel,
                    cancellationToken,
                    out var dividend,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) ||
                dividend is not { Kind: SmtValueKind.Int } ||
                !TryTranslateIntegralOperandWithSafeDivisors(
                    divisorExpression,
                    semanticModel,
                    cancellationToken,
                    out var divisor,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) ||
                divisor is not { Kind: SmtValueKind.Int } ||
                !IsSafeIntegerDivisor(divisor, nonZeroDivisors))
            {
                return false;
            }

            var remainder = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Remainder, dividend, divisor);
            var isNonNegative = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                remainder,
                new SmtIntegerConstant(0));
            formula = new SmtConditionalFormula(
                isNonNegative,
                remainder,
                new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, remainder),
                SmtValueKind.Int);
            return true;
        }

        internal static bool TryGetMathAbsRemainderOperands(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax dividendExpression,
            out ExpressionSyntax divisorExpression)
        {
            dividendExpression = null!;
            divisorExpression = null!;
            if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod.Name != "Abs" ||
                !invocationOperation.TargetMethod.IsStatic ||
                invocationOperation.TargetMethod.ContainingType?.ToDisplayString() != "System.Math" ||
                invocationOperation.TargetMethod.Parameters.Length != 1 ||
                !IsIntegralOrEnumType(invocationOperation.TargetMethod.ReturnType) ||
                !TryGetInvocationArgumentExpression(invocationOperation, parameterIndex: 0, out var argumentExpression))
            {
                return false;
            }

            argumentExpression = UnwrapExpression(argumentExpression);
            if (argumentExpression is not BinaryExpressionSyntax remainderExpression ||
                !remainderExpression.IsKind(SyntaxKind.ModuloExpression) ||
                !HasSupportedIntegralType(remainderExpression.Left, semanticModel, cancellationToken) ||
                !HasSupportedIntegralType(remainderExpression.Right, semanticModel, cancellationToken))
            {
                return false;
            }

            dividendExpression = remainderExpression.Left;
            divisorExpression = remainderExpression.Right;
            return true;
        }

        private static bool TryTranslateIntegralOperandWithSafeDivisors(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string> nonZeroDivisors)
        {
            if (TryTranslateValue(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth) &&
                formula is { Kind: SmtValueKind.Int })
            {
                return true;
            }

            return TryTranslateIntegralTermWithSafeDivisors(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                nonZeroDivisors) &&
                formula is { Kind: SmtValueKind.Int };
        }

        private static bool IsSafeIntegerDivisor(SmtFormula divisor, ISet<string>? nonZeroDivisors)
        {
            if (divisor is SmtIntegerConstant integerConstant)
            {
                return integerConstant.Value != 0;
            }

            return nonZeroDivisors != null &&
                nonZeroDivisors.Contains(CreateDivisorKey(divisor));
        }

        private static bool TryTranslateNullableCoalesceValue(
            BinaryExpressionSyntax coalesceExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryTranslateNullableValueParts(
                    coalesceExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula,
                    out var nullableValueFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                nullableValueFormula == null ||
                !TryTranslateValue(
                    coalesceExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var fallbackFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                fallbackFormula == null ||
                nullableValueFormula.Kind != fallbackFormula.Kind)
            {
                return false;
            }

            formula = new SmtConditionalFormula(
                hasValueFormula,
                nullableValueFormula,
                fallbackFormula,
                fallbackFormula.Kind);
            return true;
        }

        private static bool TryTranslateIdentityPreservingReferenceCastValue(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!IsIdentityPreservingReferenceCast(castExpression, semanticModel, cancellationToken) ||
                !TryTranslateValue(castExpression.Expression, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth) ||
                operand is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formula = operand;
            return true;
        }

        private static bool TryTranslateNonUserDefinedReferenceCastOperand(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula operand,
            out ITypeSymbol targetType,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            operand = null!;
            targetType = null!;
            var targetTypeInfo = semanticModel.GetTypeInfo(castExpression.Type, cancellationToken);
            var candidateTargetType = targetTypeInfo.Type ?? targetTypeInfo.ConvertedType;
            var sourceTypeInfo = semanticModel.GetTypeInfo(castExpression.Expression, cancellationToken);
            var sourceType = sourceTypeInfo.Type ?? sourceTypeInfo.ConvertedType;
            if (candidateTargetType?.IsReferenceType != true ||
                sourceType?.IsReferenceType != true ||
                semanticModel.GetOperation(castExpression, cancellationToken) is IConversionOperation { OperatorMethod: not null })
            {
                return false;
            }

            if (!TryTranslateValue(castExpression.Expression, semanticModel, cancellationToken, out var candidateOperand, getSymbolVersion, inlineDepth) ||
                candidateOperand is not { Kind: SmtValueKind.Reference })
            {
                operand = null!;
                return false;
            }

            operand = candidateOperand;
            targetType = candidateTargetType;
            return true;
        }

        private static bool TryTranslateIdentityPreservingAsValue(
            BinaryExpressionSyntax asExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (asExpression.Right is not TypeSyntax typeSyntax ||
                !IsIdentityPreservingReferenceConversion(asExpression.Left, typeSyntax, semanticModel, cancellationToken) ||
                !TryTranslateValue(asExpression.Left, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth) ||
                operand is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formula = operand;
            return true;
        }

        private static bool IsIdentityPreservingReferenceCast(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return IsIdentityPreservingReferenceConversion(
                castExpression.Expression,
                castExpression.Type,
                semanticModel,
                cancellationToken);
        }

        private static bool IsIdentityPreservingBooleanCast(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var sourceType = semanticModel.GetTypeInfo(castExpression.Expression, cancellationToken).Type;
            var targetType = semanticModel.GetTypeInfo(castExpression.Type, cancellationToken).Type;
            return sourceType?.SpecialType == SpecialType.System_Boolean &&
                targetType?.SpecialType == SpecialType.System_Boolean;
        }

        private static bool IsIdentityPreservingReferenceConversion(
            ExpressionSyntax expression,
            TypeSyntax targetTypeSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var sourceType = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            var targetType = semanticModel.GetTypeInfo(targetTypeSyntax, cancellationToken).Type;
            return IsTypeKnownAssignableTo(sourceType, targetType);
        }

        private static bool TryTranslateConditionalAccessReferenceValue(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var resultTypeInfo = semanticModel.GetTypeInfo(conditionalAccess, cancellationToken);
            var resultType = resultTypeInfo.ConvertedType ?? resultTypeInfo.Type;
            if (resultType == null ||
                !resultType.IsReferenceType ||
                !TryTranslateValue(
                    conditionalAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                receiverFormula is not { Kind: SmtValueKind.Reference } ||
                !TryCreateConditionalAccessWhenNotNullValueFormula(
                    conditionalAccess,
                    receiverFormula,
                    resultType,
                    semanticModel,
                    cancellationToken,
                    out var whenNotNullValue,
                    getSymbolVersion,
                    inlineDepth) ||
                whenNotNullValue is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formula = new SmtConditionalFormula(
                new SmtBinaryFormula(SmtBinaryOperator.NotEqual, receiverFormula, new SmtNullConstant()),
                whenNotNullValue,
                new SmtNullConstant(),
                SmtValueKind.Reference);
            return true;
        }

        private static bool TryTranslateNullableValueParts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula hasValueFormula,
            out SmtFormula? valueFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            expression = UnwrapExpression(expression);
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var expressionType = typeInfo.ConvertedType ?? typeInfo.Type;
            if (semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is { } symbol &&
                symbol is ILocalSymbol or IParameterSymbol &&
                TryGetNullableUnderlyingType(
                    typeInfo.Type,
                    out var underlyingType) &&
                TryGetValueKind(underlyingType, out var nullableValueKind))
            {
                var variableName = GetVariableName(symbol.OriginalDefinition, getSymbolVersion);
                hasValueFormula = new SmtVariable(variableName + ".HasValue", SmtValueKind.Bool);
                valueFormula = new SmtVariable(variableName + ".Value", nullableValueKind);
                return true;
            }

            if (TryGetNullableUnderlyingType(expressionType, out var nullableUnderlyingType) &&
                IsNullLikeNullableComparisonOperand(expression, semanticModel, cancellationToken) &&
                TryCreateDefaultValueFormula(nullableUnderlyingType, out valueFormula) &&
                valueFormula != null)
            {
                hasValueFormula = new SmtBooleanConstant(false);
                return true;
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryGetNullableUnderlyingType(expressionType, out var coalesceUnderlyingType) &&
                TryTranslateNullableValuePartsForUnderlyingType(
                    coalesceExpression.Left,
                    coalesceUnderlyingType,
                    semanticModel,
                    cancellationToken,
                    out var coalesceLeftHasValue,
                    out var coalesceLeftValue,
                    getSymbolVersion,
                    inlineDepth) &&
                coalesceLeftValue != null &&
                TryTranslateNullableValuePartsForUnderlyingType(
                    coalesceExpression.Right,
                    coalesceUnderlyingType,
                    semanticModel,
                    cancellationToken,
                    out var coalesceRightHasValue,
                    out var coalesceRightValue,
                    getSymbolVersion,
                    inlineDepth) &&
                coalesceRightValue != null &&
                coalesceLeftValue.Kind == coalesceRightValue.Kind)
            {
                hasValueFormula = new SmtBinaryFormula(SmtBinaryOperator.Or, coalesceLeftHasValue, coalesceRightHasValue);
                valueFormula = new SmtConditionalFormula(
                    coalesceLeftHasValue,
                    coalesceLeftValue,
                    coalesceRightValue,
                    coalesceLeftValue.Kind);
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryGetNullableUnderlyingType(expressionType, out var conditionalUnderlyingType) &&
                TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion, inlineDepth) &&
                conditionFormula != null &&
                TryTranslateNullableValuePartsForUnderlyingType(
                    conditionalExpression.WhenTrue,
                    conditionalUnderlyingType,
                    semanticModel,
                    cancellationToken,
                    out var whenTrueHasValue,
                    out var whenTrueValue,
                    getSymbolVersion,
                    inlineDepth) &&
                whenTrueValue != null &&
                TryTranslateNullableValuePartsForUnderlyingType(
                    conditionalExpression.WhenFalse,
                    conditionalUnderlyingType,
                    semanticModel,
                    cancellationToken,
                    out var whenFalseHasValue,
                    out var whenFalseValue,
                    getSymbolVersion,
                    inlineDepth) &&
                whenFalseValue != null &&
                whenTrueValue.Kind == whenFalseValue.Kind)
            {
                hasValueFormula = new SmtConditionalFormula(
                    conditionFormula,
                    whenTrueHasValue,
                    whenFalseHasValue,
                    SmtValueKind.Bool);
                valueFormula = new SmtConditionalFormula(
                    conditionFormula,
                    whenTrueValue,
                    whenFalseValue,
                    whenTrueValue.Kind);
                return true;
            }

            if (expression is CastExpressionSyntax nullableCastExpression &&
                TryGetNullableUnderlyingType(expressionType, out var castUnderlyingType) &&
                TryGetValueKind(castUnderlyingType, out var castUnderlyingKind) &&
                TryTranslateValue(nullableCastExpression.Expression, semanticModel, cancellationToken, out var castUnderlyingValue, getSymbolVersion, inlineDepth) &&
                castUnderlyingValue is { } &&
                castUnderlyingValue.Kind == castUnderlyingKind)
            {
                hasValueFormula = new SmtBooleanConstant(true);
                valueFormula = castUnderlyingValue;
                return true;
            }

            if (expression is ConditionalAccessExpressionSyntax conditionalAccess &&
                TryGetNullableUnderlyingType(
                    expressionType,
                    out var conditionalAccessUnderlyingType) &&
                TryTranslateValue(
                    conditionalAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion,
                    inlineDepth) &&
                receiverFormula is { Kind: SmtValueKind.Reference } &&
                TryCreateConditionalAccessWhenNotNullValueFormula(
                    conditionalAccess,
                    receiverFormula,
                    conditionalAccessUnderlyingType,
                    semanticModel,
                    cancellationToken,
                    out valueFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                hasValueFormula = new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    receiverFormula,
                    new SmtNullConstant());
                return true;
            }

            if (TryGetNullableUnderlyingType(expressionType, out var wrappedUnderlyingType) &&
                !TryGetNullableUnderlyingType(typeInfo.Type, out _) &&
                TryGetValueKind(wrappedUnderlyingType, out var wrappedKind) &&
                TryTranslateValue(expression, semanticModel, cancellationToken, out var wrappedValue, getSymbolVersion, inlineDepth) &&
                wrappedValue is { } &&
                wrappedValue.Kind == wrappedKind)
            {
                hasValueFormula = new SmtBooleanConstant(true);
                valueFormula = wrappedValue;
                return true;
            }

            hasValueFormula = null!;
            valueFormula = null;
            return false;
        }

        private static bool TryTranslateNullableValuePartsForUnderlyingType(
            ExpressionSyntax expression,
            ITypeSymbol expectedUnderlyingType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula hasValueFormula,
            out SmtFormula? valueFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            expression = UnwrapExpression(expression);
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var expressionType = typeInfo.ConvertedType ?? typeInfo.Type;
            if (TryGetNullableUnderlyingType(expressionType, out var actualUnderlyingType) &&
                SymbolEqualityComparer.Default.Equals(actualUnderlyingType, expectedUnderlyingType))
            {
                return TryTranslateNullableValueParts(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out hasValueFormula,
                    out valueFormula,
                    getSymbolVersion,
                    inlineDepth);
            }

            if (IsNullLikeNullableComparisonOperand(expression, semanticModel, cancellationToken) &&
                TryCreateDefaultValueFormula(expectedUnderlyingType, out valueFormula) &&
                valueFormula != null)
            {
                hasValueFormula = new SmtBooleanConstant(false);
                return true;
            }

            if (TryGetValueKind(expectedUnderlyingType, out var expectedKind) &&
                TryTranslateValue(expression, semanticModel, cancellationToken, out valueFormula, getSymbolVersion, inlineDepth) &&
                valueFormula is { } &&
                valueFormula.Kind == expectedKind)
            {
                hasValueFormula = new SmtBooleanConstant(true);
                return true;
            }

            hasValueFormula = null!;
            valueFormula = null;
            return false;
        }

        private static bool TryCreateConditionalAccessWhenNotNullValueFormula(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SmtFormula receiverFormula,
            ITypeSymbol expectedType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding)
            {
                if (semanticModel.GetSymbolInfo(memberBinding.Name, cancellationToken).Symbol is not { } memberSymbol ||
                    !TryGetMemberType(memberSymbol, out var memberType) ||
                    !SymbolEqualityComparer.Default.Equals(memberType, expectedType))
                {
                    return false;
                }

                if (memberSymbol.Name == "Length" &&
                    IsStringExpression(conditionalAccess.Expression, semanticModel, cancellationToken) &&
                    TryTranslateStringValue(
                        conditionalAccess.Expression,
                        semanticModel,
                        cancellationToken,
                        out var stringFormula) &&
                    stringFormula != null)
                {
                    formula = new SmtStringLengthTerm(stringFormula);
                    return true;
                }

                return TryCreateMemberFormula(receiverFormula, memberSymbol.Name, memberType, out formula) &&
                    formula != null;
            }

            if (conditionalAccess.WhenNotNull is ElementBindingExpressionSyntax elementBinding &&
                elementBinding.ArgumentList.Arguments.Count == 1 &&
                semanticModel.GetTypeInfo(conditionalAccess.Expression, cancellationToken).Type is IArrayTypeSymbol { Rank: 1 } arrayType &&
                SymbolEqualityComparer.Default.Equals(arrayType.ElementType, expectedType) &&
                TryGetValueKind(arrayType.ElementType, out var elementKind) &&
                TryCreateElementAccessIndexText(
                    elementBinding.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken,
                    out var indexText,
                    getSymbolVersion,
                    inlineDepth))
            {
                formula = new SmtVariable(receiverFormula + "[" + indexText + "]", elementKind);
                return true;
            }

            return false;
        }

        private static bool IsRepresentationPreservingIntegralCast(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var sourceType = semanticModel.GetTypeInfo(castExpression.Expression, cancellationToken).Type;
            var targetType = semanticModel.GetTypeInfo(castExpression, cancellationToken).Type;
            if (sourceType == null ||
                targetType == null ||
                !IsIntegralOrEnumType(sourceType) ||
                !IsIntegralOrEnumType(targetType))
            {
                return false;
            }

            return TryGetIntegralSpecialType(sourceType, out var sourceSpecialType) &&
                TryGetIntegralSpecialType(targetType, out var targetSpecialType) &&
                IsSameOrWideningIntegralConversion(sourceSpecialType, targetSpecialType);
        }

        private static bool IsSameOrWideningIntegralConversion(
            SpecialType sourceType,
            SpecialType targetType)
        {
            if (sourceType == targetType)
            {
                return true;
            }

            return sourceType switch
            {
                SpecialType.System_Char => targetType is
                    SpecialType.System_UInt16 or
                    SpecialType.System_Int32 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64,
                SpecialType.System_SByte => targetType is
                    SpecialType.System_Int16 or
                    SpecialType.System_Int32 or
                    SpecialType.System_Int64,
                SpecialType.System_Byte => targetType is
                    SpecialType.System_Int16 or
                    SpecialType.System_UInt16 or
                    SpecialType.System_Int32 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64,
                SpecialType.System_Int16 => targetType is
                    SpecialType.System_Int32 or
                    SpecialType.System_Int64,
                SpecialType.System_UInt16 => targetType is
                    SpecialType.System_Int32 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64,
                SpecialType.System_Int32 => targetType == SpecialType.System_Int64,
                SpecialType.System_UInt32 => targetType is
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64,
                _ => false
            };
        }

        private static bool TryTranslateMemberValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (TryTranslateImplicitThisMemberValue(expression, semanticModel, cancellationToken, out formula))
            {
                return true;
            }

            if (expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var memberSymbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol;
            if (memberSymbol is not IPropertySymbol and not IFieldSymbol)
            {
                return false;
            }

            if (memberSymbol.Name == "Length" &&
                TryGetKnownStringLength(memberAccess.Expression, semanticModel, cancellationToken, out var stringLength))
            {
                formula = new SmtIntegerConstant(stringLength);
                return true;
            }

            if (memberSymbol.Name == "Length" &&
                IsStringExpression(memberAccess.Expression, semanticModel, cancellationToken) &&
                TryTranslateStringValue(memberAccess.Expression, semanticModel, cancellationToken, out var stringValue, getSymbolVersion, inlineDepth) &&
                stringValue != null)
            {
                formula = new SmtStringLengthTerm(stringValue);
                return true;
            }

            if (memberSymbol.Name == "Length" &&
                TryCreateBuiltInElementAccessLengthFormula(
                    memberAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var builtInLength,
                    getSymbolVersion,
                    inlineDepth))
            {
                formula = builtInLength;
                return true;
            }

            if (memberSymbol is IFieldSymbol { HasConstantValue: true } constantField &&
                constantField.ConstantValue != null &&
                TryGetIntegralConstant(constantField.ConstantValue, out var integralConstant))
            {
                formula = new SmtIntegerConstant(integralConstant);
                return true;
            }

            if (TryTranslateTupleElementValue(memberAccess, memberSymbol, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (TryTranslateNullableMemberValue(memberAccess, memberSymbol, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (!TryTranslateValue(memberAccess.Expression, semanticModel, cancellationToken, out var receiver, getSymbolVersion, inlineDepth) ||
                receiver == null)
            {
                return false;
            }

            if (memberSymbol is IPropertySymbol propertySymbol &&
                TryTranslateSourceBooleanProperty(
                    propertySymbol,
                    receiver,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    inlineDepth + 1))
            {
                return true;
            }

            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            if (type == null)
            {
                return false;
            }

            return TryCreateMemberFormula(receiver, memberSymbol.Name, type, out formula);
        }

        private static bool TryTranslateImplicitThisMemberValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (expression is not IdentifierNameSyntax ||
                semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not IPropertySymbol and not IFieldSymbol ||
                semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not { IsStatic: false } memberSymbol ||
                !TryGetMemberType(memberSymbol, out var memberType))
            {
                return false;
            }

            return TryCreateMemberFormula(
                new SmtVariable(ImplicitThisVariableName, SmtValueKind.Reference),
                memberSymbol.Name,
                memberType,
                out formula);
        }

        private static bool TryTranslateSourceBooleanProperty(
            IPropertySymbol propertySymbol,
            SmtFormula receiver,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            int inlineDepth)
        {
            formula = null;
            if (inlineDepth >= MaxSourcePredicateInlineDepth ||
                !CanInlineSourceBooleanProperty(propertySymbol) ||
                !TryGetSourceBooleanPropertyFormula(
                    propertySymbol,
                    semanticModel.Compilation,
                    cancellationToken,
                    inlineDepth,
                    out var propertyFormula) ||
                propertyFormula is not { Kind: SmtValueKind.Bool })
            {
                return false;
            }

            formula = SubstituteVariables(
                propertyFormula,
                new[]
                {
                    CreateImplicitThisSubstitution(receiver)
                });
            return true;
        }

        private static bool CanInlineSourceBooleanProperty(IPropertySymbol propertySymbol)
        {
            return propertySymbol is
            {
                IsStatic: false,
                IsIndexer: false,
                Type.SpecialType: SpecialType.System_Boolean,
                DeclaringSyntaxReferences.Length: > 0
            };
        }

        private static bool TryGetSourceBooleanPropertyFormula(
            IPropertySymbol propertySymbol,
            Compilation compilation,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            var propertySyntax = propertySymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault();
            if (propertySyntax == null)
            {
                return false;
            }

            var cache = GetSourceBooleanFormulaCache(compilation);
            var cacheKey = CreateSourceBooleanFormulaCacheKey("property", propertySyntax, inlineDepth);
            var entry = cache.GetOrAdd(
                cacheKey,
                _ =>
                {
                    var propertySemanticModel = compilation.GetSemanticModel(propertySyntax.SyntaxTree);
                    if (propertySyntax.ExpressionBody?.Expression != null)
                    {
                        var success = TryTranslate(
                            propertySyntax.ExpressionBody.Expression,
                            propertySemanticModel,
                            cancellationToken,
                            out var cachedFormula,
                            getSymbolVersion: null,
                            inlineDepth);
                        return new SourceBooleanFormulaCacheEntry(success, cachedFormula);
                    }

                    var getter = propertySyntax.AccessorList?.Accessors
                        .FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                    if (getter?.ExpressionBody?.Expression != null)
                    {
                        var success = TryTranslate(
                            getter.ExpressionBody.Expression,
                            propertySemanticModel,
                            cancellationToken,
                            out var cachedFormula,
                            getSymbolVersion: null,
                            inlineDepth);
                        return new SourceBooleanFormulaCacheEntry(success, cachedFormula);
                    }

                    if (getter?.Body != null)
                    {
                        var success = TryTranslateReturnedBooleanBlock(
                            getter.Body,
                            propertySemanticModel,
                            cancellationToken,
                            inlineDepth,
                            out var cachedFormula);
                        return new SourceBooleanFormulaCacheEntry(success, cachedFormula);
                    }

                    return new SourceBooleanFormulaCacheEntry(false, null);
                });

            formula = entry.Formula;
            return entry.Success;
        }

        private static bool TryTranslateNullableMemberValue(
            MemberAccessExpressionSyntax memberAccess,
            ISymbol memberSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (memberSymbol.Name is not "HasValue" and not "Value" ||
                !TryTranslateNullableValueParts(
                    memberAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var hasValue,
                    out var value,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            if (memberSymbol.Name == "HasValue")
            {
                formula = hasValue;
                return true;
            }

            if (value == null)
            {
                return false;
            }

            formula = value;
            return true;
        }

        private static bool TryTranslateTupleElementValue(
            MemberAccessExpressionSyntax memberAccess,
            ISymbol memberSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (memberSymbol is not IFieldSymbol fieldSymbol ||
                !TryGetTupleElementStorageName(fieldSymbol, out var storageName) ||
                !TryGetValueKind(fieldSymbol.Type, out var kind))
            {
                return false;
            }

            return TryTranslateTupleElementReceiverValue(
                memberAccess.Expression,
                fieldSymbol,
                storageName,
                kind,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        private static bool TryTranslateTupleElementReceiverValue(
            ExpressionSyntax receiverExpression,
            IFieldSymbol fieldSymbol,
            string storageName,
            SmtValueKind kind,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            receiverExpression = UnwrapExpression(receiverExpression);

            if (semanticModel.GetSymbolInfo(receiverExpression, cancellationToken).Symbol is { } receiverSymbol &&
                receiverSymbol is ILocalSymbol or IParameterSymbol)
            {
                formula = new SmtVariable(GetVariableName(receiverSymbol.OriginalDefinition, getSymbolVersion) + "." + storageName, kind);
                return true;
            }

            if (receiverExpression is TupleExpressionSyntax tupleExpression &&
                TryGetTupleElementIndex(storageName, out var elementIndex) &&
                elementIndex <= tupleExpression.Arguments.Count &&
                TryTranslateTupleElementExpressionValue(
                    tupleExpression.Arguments[elementIndex - 1].Expression,
                    fieldSymbol.Type,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors: null) &&
                formula is { } &&
                formula.Kind == kind)
            {
                return true;
            }

            if (receiverExpression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion, inlineDepth) &&
                conditionFormula != null &&
                TryTranslateTupleElementReceiverValue(
                    conditionalExpression.WhenTrue,
                    fieldSymbol,
                    storageName,
                    kind,
                    semanticModel,
                    cancellationToken,
                    out var whenTrue,
                    getSymbolVersion,
                    inlineDepth) &&
                whenTrue is { Kind: var whenTrueKind } &&
                whenTrueKind == kind &&
                TryTranslateTupleElementReceiverValue(
                    conditionalExpression.WhenFalse,
                    fieldSymbol,
                    storageName,
                    kind,
                    semanticModel,
                    cancellationToken,
                    out var whenFalse,
                    getSymbolVersion,
                    inlineDepth) &&
                whenFalse is { Kind: var whenFalseKind } &&
                whenFalseKind == kind)
            {
                formula = new SmtConditionalFormula(conditionFormula, whenTrue, whenFalse, kind);
                return true;
            }

            return false;
        }

        private static bool TryGetTupleElementStorageName(IFieldSymbol fieldSymbol, out string storageName)
        {
            var tupleField = fieldSymbol.CorrespondingTupleField ?? fieldSymbol;
            if (IsTupleElementStorageName(tupleField.Name))
            {
                storageName = tupleField.Name;
                return true;
            }

            storageName = string.Empty;
            return false;
        }

        private static bool TryGetTupleElementStorageName(
            MemberAccessExpressionSyntax memberAccess,
            IFieldSymbol fieldSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out string storageName)
        {
            if (TryGetTupleElementStorageName(fieldSymbol, out storageName))
            {
                return true;
            }

            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            if (receiverType is not INamedTypeSymbol { IsTupleType: true } tupleType)
            {
                storageName = string.Empty;
                return false;
            }

            foreach (var element in tupleType.TupleElements)
            {
                if (!string.Equals(element.Name, fieldSymbol.Name, StringComparison.Ordinal) &&
                    !string.Equals(element.Name, memberAccess.Name.Identifier.ValueText, StringComparison.Ordinal))
                {
                    continue;
                }

                var tupleField = element.CorrespondingTupleField ?? element;
                if (IsTupleElementStorageName(tupleField.Name))
                {
                    storageName = tupleField.Name;
                    return true;
                }
            }

            storageName = string.Empty;
            return false;
        }

        private static bool IsTupleElementStorageName(string name)
        {
            return name.Length > 4 &&
                name.StartsWith("Item", StringComparison.Ordinal) &&
                name.Skip(4).All(char.IsDigit);
        }

        private static bool TryCreateMemberFormula(
            SmtFormula receiver,
            string memberName,
            ITypeSymbol type,
            out SmtFormula? formula)
        {
            formula = null;
            var receiverName = receiver is SmtVariable variable
                ? variable.Name
                : receiver.ToString() ?? string.Empty;
            var variableName = receiverName + "." + memberName;
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Bool);
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                formula = new SmtVariable(variableName, SmtValueKind.Int);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Reference);
                return true;
            }

            return false;
        }

        private static bool TryCreateSymbolFormula(
            ISymbol symbol,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula formula)
        {
            var type = symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type == null ||
                (!TryGetValueKind(type, out var kind) &&
                 !TryGetTupleCarrierKind(type, out kind)))
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), kind);
            return true;
        }

        private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                kind = SmtValueKind.Bool;
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                kind = SmtValueKind.Int;
                return true;
            }

            if (IsReferenceLikeType(type))
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool IsReferenceLikeType(ITypeSymbol type)
        {
            return type.TypeKind == TypeKind.Dynamic ||
                type.IsReferenceType;
        }

        private static bool TryGetTupleCarrierKind(ITypeSymbol type, out SmtValueKind kind)
        {
            if (IsSupportedTupleCarrierType(type))
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool IsSupportedTupleCarrierType(ITypeSymbol type)
        {
            if (type is not INamedTypeSymbol namedType)
            {
                return false;
            }

            if (namedType.IsTupleType && namedType.TupleElements.Length > 0)
            {
                return true;
            }

            return namedType
                .GetMembers()
                .OfType<IFieldSymbol>()
                .Any(static field => !field.IsStatic && IsTupleElementStorageName(field.Name));
        }

        private static bool TryGetNullableUnderlyingType(ITypeSymbol? type, out ITypeSymbol underlyingType)
        {
            if (type is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                namedType.TypeArguments.Length == 1)
            {
                underlyingType = namedType.TypeArguments[0];
                return true;
            }

            underlyingType = null!;
            return false;
        }

        private static bool TryGetMemberType(ISymbol? memberSymbol, out ITypeSymbol type)
        {
            switch (memberSymbol)
            {
                case IPropertySymbol propertySymbol:
                    type = propertySymbol.Type;
                    return true;
                case IFieldSymbol fieldSymbol:
                    type = fieldSymbol.Type;
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }

        private static string GetVariableName(ISymbol symbol, Func<ISymbol, int>? getSymbolVersion)
        {
            var start = symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;
            var name = symbol.Name + "#" + start.ToString(CultureInfo.InvariantCulture);
            var version = getSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
            return version > 0
                ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
                : name;
        }

        private static bool IsIntegralType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType is
                SpecialType.System_Char or
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64;
        }

        private static bool IsIntegralOrEnumType(ITypeSymbol typeSymbol)
        {
            return IsIntegralType(typeSymbol) ||
                typeSymbol.TypeKind == TypeKind.Enum;
        }

        private static bool TryGetIntegralSpecialType(ITypeSymbol typeSymbol, out SpecialType specialType)
        {
            if (IsIntegralType(typeSymbol))
            {
                specialType = typeSymbol.SpecialType;
                return true;
            }

            if (typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType } &&
                IsIntegralType(underlyingType))
            {
                specialType = underlyingType.SpecialType;
                return true;
            }

            specialType = SpecialType.None;
            return false;
        }

        private static bool TryGetIntegralConstant(object value, out long integralValue)
        {
            if (value is Enum enumValue)
            {
                value = Convert.ChangeType(enumValue, enumValue.GetTypeCode(), CultureInfo.InvariantCulture);
            }

            switch (value)
            {
                case sbyte signedByte:
                    integralValue = signedByte;
                    return true;
                case byte unsignedByte:
                    integralValue = unsignedByte;
                    return true;
                case short signedShort:
                    integralValue = signedShort;
                    return true;
                case ushort unsignedShort:
                    integralValue = unsignedShort;
                    return true;
                case int signedInt:
                    integralValue = signedInt;
                    return true;
                case uint unsignedInt:
                    integralValue = unsignedInt;
                    return true;
                case long signedLong:
                    integralValue = signedLong;
                    return true;
                case ulong unsignedLong when unsignedLong <= long.MaxValue:
                    integralValue = (long)unsignedLong;
                    return true;
                case char character:
                    integralValue = character;
                    return true;
                default:
                    integralValue = default;
                    return false;
            }
        }

        private static bool HasSupportedIntegralType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            return type != null && IsIntegralOrEnumType(type);
        }

        private static bool HasSupportedBooleanType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            return type?.SpecialType == SpecialType.System_Boolean;
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
                {
                    expression = parenthesizedExpression.Expression;
                    continue;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                if (expression is CheckedExpressionSyntax checkedExpression &&
                    (checkedExpression.IsKind(SyntaxKind.CheckedExpression) ||
                     checkedExpression.IsKind(SyntaxKind.UncheckedExpression)))
                {
                    expression = checkedExpression.Expression;
                    continue;
                }

                return expression;
            }
        }
    }
}
