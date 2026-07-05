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
using SharpProof.Symbolic.Ir;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Smt
{
    internal static partial class CSharpConditionToFormula
    {
        private const int MaxSourcePredicateInlineDepth = 4;
        private const int MaxConditionalPatternDistributionDepth = 4;
        private const int MaxCollectionExpressionLengthSpreads = 8;
        private const string ImplicitThisVariableName = "this";
        private const string MemberNotNullWhenAttributeMetadataName = "System.Diagnostics.CodeAnalysis.MemberNotNullWhenAttribute";
        private const string NotNullIfNotNullAttributeMetadataName = "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute";
        private const string NotNullWhenAttributeMetadataName = "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute";
        private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<SourceBooleanFormulaCacheKey, SourceBooleanFormulaCacheEntry>> s_sourceBooleanFormulaCache = new();
        private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<ExpressionFormulaCacheKey, SourceBooleanFormulaCacheEntry>> s_expressionFormulaCache = new();

        internal readonly struct NullableSmtValueParts
        {
            internal NullableSmtValueParts(SmtFormula hasValue, SmtFormula? value)
            {
                HasValue = hasValue;
                Value = value;
            }

            internal SmtFormula HasValue { get; }

            internal SmtFormula? Value { get; }
        }

        private readonly struct SourceBooleanFormulaCacheEntry
        {
            internal SourceBooleanFormulaCacheEntry(bool success, SmtFormula? formula)
            {
                Success = success;
                Formula = formula;
            }

            internal bool Success { get; }

            internal SmtFormula? Formula { get; }
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

            if (expression is MemberAccessExpressionSyntax memberAccessExpression &&
                TryTranslateRegexMatchSuccessProperty(
                    memberAccessExpression,
                    semanticModel,
                    cancellationToken,
                    out var regexMatchSuccessFormula,
                    getSymbolVersion,
                    inlineDepth) &&
                regexMatchSuccessFormula != null)
            {
                formula = regexMatchSuccessFormula;
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
                TryTranslateConditionalBooleanExpression(
                    conditionalExpression,
                    semanticModel,
                    cancellationToken,
                    out var conditionalValue,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) &&
                conditionalValue != null)
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

                if (TryTranslateCheckedIntegralCastComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var checkedIntegralCastComparison,
                        getSymbolVersion,
                        inlineDepth,
                        nonZeroDivisors) &&
                    checkedIntegralCastComparison != null)
                {
                    formula = checkedIntegralCastComparison;
                    return true;
                }

                if (TryTranslateAsExpressionNullComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var asExpressionNullComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    asExpressionNullComparison != null)
                {
                    formula = asExpressionNullComparison;
                    return true;
                }

                if (TryTranslateNotNullIfNotNullNullComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var notNullIfNotNullFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    notNullIfNotNullFormula != null)
                {
                    formula = notNullIfNotNullFormula;
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

                if (TryTranslateNullableValueMemberComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var nullableValueMemberComparison,
                        getSymbolVersion,
                        inlineDepth,
                        nonZeroDivisors) &&
                    nullableValueMemberComparison != null)
                {
                    formula = nullableValueMemberComparison;
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

                if (TryTranslateTypeOfComparison(binaryExpression, semanticModel, cancellationToken, out var typeOfComparison) &&
                    typeOfComparison != null)
                {
                    formula = typeOfComparison;
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

                if (TryTranslateRegexMatchesCountComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var regexMatchesCountComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    regexMatchesCountComparison != null)
                {
                    formula = regexMatchesCountComparison;
                    return true;
                }

                if (TryTranslateDecimalZeroComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var decimalZeroComparison,
                        getSymbolVersion) &&
                    decimalZeroComparison != null)
                {
                    formula = decimalZeroComparison;
                    return true;
                }

                if (TryTranslateValueWithSafeDivisors(
                        binaryExpression.Left,
                        semanticModel,
                        cancellationToken,
                        out var leftValue,
                        getSymbolVersion,
                        inlineDepth,
                        Array.Empty<SmtFormula>(),
                        nonZeroDivisors) &&
                    TryTranslateValueWithSafeDivisors(
                        binaryExpression.Right,
                        semanticModel,
                        cancellationToken,
                        out var rightValue,
                        getSymbolVersion,
                        inlineDepth,
                        Array.Empty<SmtFormula>(),
                        nonZeroDivisors) &&
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

            if (!ContainsDivisionOrModulo(expression) &&
                TryTranslateUsingSymbolicIr(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion))
            {
                return true;
            }

            formula = null;
            return false;
        }

        private static bool ContainsDivisionOrModulo(ExpressionSyntax expression)
        {
            return expression.DescendantNodesAndSelf()
                .OfType<BinaryExpressionSyntax>()
                .Any(static binary => binary.IsKind(SyntaxKind.DivideExpression) || binary.IsKind(SyntaxKind.ModuloExpression));
        }

        private static bool TryTranslateUsingSymbolicIr(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null;
            var context = new SymbolicLoweringContext(
                semanticModel,
                cancellationToken,
                getSymbolVersion);
            if (!SymbolicIrLowerer.TryLowerCondition(expression, context, out var condition) ||
                !SymbolicIrFormulaEncoder.TryEncode(condition, out var encoded))
            {
                return false;
            }

            formula = encoded;
            return true;
        }

        private static bool TryTranslateConditionalBooleanExpression(
            ConditionalExpressionSyntax conditionalExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string>? nonZeroDivisors)
        {
            formula = null;
            if (!HasSupportedBooleanType(conditionalExpression, semanticModel, cancellationToken) ||
                !TryTranslateCore(
                    conditionalExpression.Condition,
                    semanticModel,
                    cancellationToken,
                    out var conditionFormula,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) ||
                conditionFormula is not { Kind: SmtValueKind.Bool })
            {
                return false;
            }

            var whenTrueNonZeroDivisors = AddNonZeroDivisorFacts(
                conditionalExpression.Condition,
                branchWhenTrue: true,
                semanticModel,
                cancellationToken,
                nonZeroDivisors,
                getSymbolVersion,
                inlineDepth);
            if (!TryTranslateCore(
                    conditionalExpression.WhenTrue,
                    semanticModel,
                    cancellationToken,
                    out var whenTrueFormula,
                    getSymbolVersion,
                    inlineDepth,
                    whenTrueNonZeroDivisors) ||
                whenTrueFormula is not { Kind: SmtValueKind.Bool })
            {
                return false;
            }

            var whenFalseNonZeroDivisors = AddNonZeroDivisorFacts(
                conditionalExpression.Condition,
                branchWhenTrue: false,
                semanticModel,
                cancellationToken,
                nonZeroDivisors,
                getSymbolVersion,
                inlineDepth);
            if (!TryTranslateCore(
                    conditionalExpression.WhenFalse,
                    semanticModel,
                    cancellationToken,
                    out var whenFalseFormula,
                    getSymbolVersion,
                    inlineDepth,
                    whenFalseNonZeroDivisors) ||
                whenFalseFormula is not { Kind: SmtValueKind.Bool })
            {
                return false;
            }

            formula = new SmtConditionalFormula(
                conditionFormula,
                whenTrueFormula,
                whenFalseFormula,
                SmtValueKind.Bool);
            return true;
        }

        private static bool TryTranslateAsExpressionNullComparison(
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

            if (!TryTranslateAsExpressionNullComparisonSide(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var resultNonNull,
                    getSymbolVersion,
                    inlineDepth) &&
                !TryTranslateAsExpressionNullComparisonSide(
                    binaryExpression.Right,
                    binaryExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out resultNonNull,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            formula = binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)
                ? resultNonNull
                : new SmtUnaryFormula(SmtUnaryOperator.Not, resultNonNull);
            return true;
        }

        private static bool TryTranslateAsExpressionNullComparisonSide(
            ExpressionSyntax candidateExpression,
            ExpressionSyntax nullOperand,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            candidateExpression = UnwrapExpression(candidateExpression);
            if (candidateExpression is not BinaryExpressionSyntax asExpression ||
                !asExpression.IsKind(SyntaxKind.AsExpression) ||
                asExpression.Right is not TypeSyntax targetTypeSyntax ||
                !IsNullReferenceComparisonOperand(nullOperand, semanticModel, cancellationToken) ||
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

            var sourceNonNull = CreateNonNullFormula(sourceFormula);
            if (IsIdentityPreservingReferenceConversion(asExpression.Left, targetTypeSyntax, semanticModel, cancellationToken))
            {
                formula = sourceNonNull;
                return true;
            }

            var targetType = semanticModel.GetTypeInfo(targetTypeSyntax, cancellationToken).Type;
            if (!TryCreateRuntimeTypeTestFormula(sourceFormula, targetType, out var runtimeTypeTest))
            {
                return false;
            }

            formula = Conjoin(sourceNonNull, runtimeTypeTest);
            return true;
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

        private static bool TryTranslateNullableValueMemberComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string>? nonZeroDivisors)
        {
            formula = null;
            if (!IsSupportedNullableValueComparison(binaryExpression.Kind()))
            {
                return false;
            }

            var leftIsNullableValue = TryTranslateNullableValueMemberAccess(
                binaryExpression.Left,
                semanticModel,
                cancellationToken,
                out var leftHasValue,
                out var leftNullableValue,
                getSymbolVersion,
                inlineDepth);
            var rightIsNullableValue = TryTranslateNullableValueMemberAccess(
                binaryExpression.Right,
                semanticModel,
                cancellationToken,
                out var rightHasValue,
                out var rightNullableValue,
                getSymbolVersion,
                inlineDepth);
            if (!leftIsNullableValue && !rightIsNullableValue)
            {
                return false;
            }

            if (!TryTranslateNullableValueMemberComparisonOperand(
                    binaryExpression.Left,
                    leftIsNullableValue,
                    leftNullableValue,
                    semanticModel,
                    cancellationToken,
                    out var leftValue,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) ||
                !TryTranslateNullableValueMemberComparisonOperand(
                    binaryExpression.Right,
                    rightIsNullableValue,
                    rightNullableValue,
                    semanticModel,
                    cancellationToken,
                    out var rightValue,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors) ||
                leftValue == null ||
                rightValue == null ||
                !TryTranslateComparison(binaryExpression.Kind(), leftValue, rightValue, out var comparison) ||
                comparison == null)
            {
                return false;
            }

            formula = comparison;
            if (leftIsNullableValue)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftHasValue, formula);
            }

            if (rightIsNullableValue &&
                (!leftIsNullableValue || !Equals(leftHasValue, rightHasValue)))
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, rightHasValue, formula);
            }

            return true;
        }

        private static bool TryTranslateNullableValueMemberComparisonOperand(
            ExpressionSyntax expression,
            bool isNullableValue,
            SmtFormula? nullableValue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? value,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string>? nonZeroDivisors)
        {
            if (isNullableValue)
            {
                value = nullableValue;
                return value != null;
            }

            return TryTranslateValueWithSafeDivisors(
                expression,
                semanticModel,
                cancellationToken,
                out value,
                getSymbolVersion,
                inlineDepth,
                Array.Empty<SmtFormula>(),
                nonZeroDivisors);
        }

        private static bool TryTranslateNullableValueMemberAccess(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula hasValue,
            out SmtFormula? value,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            expression = UnwrapExpression(expression);
            if (expression is MemberAccessExpressionSyntax memberAccess &&
                semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol is { Name: "Value" } &&
                TryTranslateNullableValueParts(
                    memberAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out hasValue,
                    out value,
                    getSymbolVersion,
                    inlineDepth) &&
                value != null)
            {
                return true;
            }

            hasValue = null!;
            value = null;
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

        private static bool TryTranslateNotNullIfNotNullNullComparison(
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

            ExpressionSyntax resultExpression;
            if (IsNullReferenceComparisonOperand(binaryExpression.Left, semanticModel, cancellationToken))
            {
                resultExpression = binaryExpression.Right;
            }
            else if (IsNullReferenceComparisonOperand(binaryExpression.Right, semanticModel, cancellationToken))
            {
                resultExpression = binaryExpression.Left;
            }
            else
            {
                return false;
            }

            if (!TryCreateNotNullIfNotNullResultNonNullFormula(
                    resultExpression,
                    semanticModel,
                    cancellationToken,
                    out var resultNonNull,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            formula = binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)
                ? resultNonNull
                : new SmtUnaryFormula(SmtUnaryOperator.Not, resultNonNull);
            return true;
        }

        private static bool IsNullReferenceComparisonOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var formula,
                    getSymbolVersion: null) &&
                formula is SmtNullConstant;
        }

        private static bool TryTranslateTypeOfComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
                !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression))
            {
                return false;
            }

            var leftIsTypeOf = TryGetTypeOfType(binaryExpression.Left, semanticModel, cancellationToken, out var leftType);
            var rightIsTypeOf = TryGetTypeOfType(binaryExpression.Right, semanticModel, cancellationToken, out var rightType);
            if (leftIsTypeOf && rightIsTypeOf)
            {
                if (SymbolEqualityComparer.Default.Equals(leftType, rightType))
                {
                    formula = new SmtBooleanConstant(binaryExpression.IsKind(SyntaxKind.EqualsExpression));
                    return true;
                }

                if (ContainsTypeParameter(leftType) ||
                    ContainsTypeParameter(rightType))
                {
                    return false;
                }

                formula = new SmtBooleanConstant(binaryExpression.IsKind(SyntaxKind.NotEqualsExpression));
                return true;
            }

            if ((leftIsTypeOf && IsNullReferenceComparisonOperand(binaryExpression.Right, semanticModel, cancellationToken)) ||
                (rightIsTypeOf && IsNullReferenceComparisonOperand(binaryExpression.Left, semanticModel, cancellationToken)))
            {
                formula = new SmtBooleanConstant(binaryExpression.IsKind(SyntaxKind.NotEqualsExpression));
                return true;
            }

            return false;
        }

        private static bool TryGetTypeOfType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ITypeSymbol type)
        {
            expression = UnwrapExpression(expression);
            if (expression is TypeOfExpressionSyntax typeOfExpression)
            {
                type = semanticModel.GetTypeInfo(typeOfExpression.Type, cancellationToken).Type!;
                return type is { TypeKind: not TypeKind.Error };
            }

            type = null!;
            return false;
        }

        private static bool ContainsTypeParameter(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.TypeParameter)
            {
                return true;
            }

            if (type is IArrayTypeSymbol arrayType)
            {
                return ContainsTypeParameter(arrayType.ElementType);
            }

            if (type is IPointerTypeSymbol pointerType)
            {
                return ContainsTypeParameter(pointerType.PointedAtType);
            }

            if (type.ContainingType != null &&
                ContainsTypeParameter(type.ContainingType))
            {
                return true;
            }

            return type is INamedTypeSymbol namedType &&
                namedType.TypeArguments.Any(ContainsTypeParameter);
        }

        internal static bool TryCreateNotNullIfNotNullResultNonNullFormula(
            ExpressionSyntax resultExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            bool requireLocalOrParameterSource = false)
        {
            formula = null!;
            resultExpression = UnwrapExpression(resultExpression);
            var resultTypeInfo = semanticModel.GetTypeInfo(resultExpression, cancellationToken);
            var resultType = resultTypeInfo.ConvertedType ?? resultTypeInfo.Type;
            if (resultType == null ||
                !IsReferenceLikeType(resultType) ||
                !TryCreateNotNullIfNotNullResultSourceFormula(
                    resultExpression,
                    semanticModel,
                    cancellationToken,
                    out var sourceFormula,
                    getSymbolVersion,
                    inlineDepth,
                    requireLocalOrParameterSource))
            {
                return false;
            }

            var sourceNonNull = CreateNonNullFormula(sourceFormula);
            var fallbackNonNull = new SmtVariable(
                CreateNotNullIfNotNullFallbackVariableName(resultExpression),
                SmtValueKind.Bool);
            formula = new SmtBinaryFormula(SmtBinaryOperator.Or, sourceNonNull, fallbackNonNull);
            return true;
        }

        private static bool TryCreateNotNullIfNotNullResultSourceFormula(
            ExpressionSyntax resultExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            bool requireLocalOrParameterSource)
        {
            formula = null!;
            var operation = semanticModel.GetOperation(resultExpression, cancellationToken);
            if (operation is IInvocationOperation invocationOperation &&
                TryGetNotNullIfNotNullParameterName(invocationOperation.TargetMethod, out var methodParameterName) &&
                TryCreateNotNullIfNotNullInvocationSourceFormula(
                    invocationOperation,
                    methodParameterName,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth,
                    requireLocalOrParameterSource))
            {
                return true;
            }

            if (operation is IPropertyReferenceOperation propertyReferenceOperation &&
                TryGetNotNullIfNotNullParameterName(propertyReferenceOperation.Property, out var propertyParameterName) &&
                TryCreateNotNullIfNotNullPropertySourceFormula(
                    propertyReferenceOperation,
                    propertyParameterName,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth,
                    requireLocalOrParameterSource))
            {
                return true;
            }

            return false;
        }

        private static bool TryCreateNotNullIfNotNullInvocationSourceFormula(
            IInvocationOperation invocationOperation,
            string parameterName,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            bool requireLocalOrParameterSource)
        {
            formula = null!;
            for (var parameterIndex = 0; parameterIndex < invocationOperation.TargetMethod.Parameters.Length; parameterIndex++)
            {
                if (!string.Equals(
                        invocationOperation.TargetMethod.Parameters[parameterIndex].Name,
                        parameterName,
                        StringComparison.Ordinal) ||
                    !TryGetInvocationArgumentExpression(invocationOperation, parameterIndex, out var argumentExpression))
                {
                    continue;
                }

                return TryCreateNotNullIfNotNullSourceFormula(
                    argumentExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth,
                    requireLocalOrParameterSource);
            }

            return false;
        }

        private static bool TryCreateNotNullIfNotNullPropertySourceFormula(
            IPropertyReferenceOperation propertyReferenceOperation,
            string parameterName,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            bool requireLocalOrParameterSource)
        {
            formula = null!;
            if (string.Equals(parameterName, ImplicitThisVariableName, StringComparison.Ordinal) &&
                propertyReferenceOperation.Instance?.Syntax is ExpressionSyntax receiverExpression)
            {
                return TryCreateNotNullIfNotNullSourceFormula(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth,
                    requireLocalOrParameterSource);
            }

            for (var parameterIndex = 0; parameterIndex < propertyReferenceOperation.Property.Parameters.Length; parameterIndex++)
            {
                if (!string.Equals(
                        propertyReferenceOperation.Property.Parameters[parameterIndex].Name,
                        parameterName,
                        StringComparison.Ordinal) ||
                    !TryGetPropertyArgumentExpression(propertyReferenceOperation, parameterIndex, out var argumentExpression))
                {
                    continue;
                }

                return TryCreateNotNullIfNotNullSourceFormula(
                    argumentExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth,
                    requireLocalOrParameterSource);
            }

            return false;
        }

        private static bool TryCreateNotNullIfNotNullSourceFormula(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            bool requireLocalOrParameterSource)
        {
            if (requireLocalOrParameterSource &&
                !IsLocalOrParameterExpression(expression, semanticModel, cancellationToken))
            {
                formula = null!;
                return false;
            }

            if (TryCreateNotNullWhenArgumentFormula(
                    expression,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion,
                    out formula))
            {
                return true;
            }

            if (inlineDepth >= MaxSourcePredicateInlineDepth ||
                !TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var candidate,
                    getSymbolVersion,
                    inlineDepth + 1) ||
                candidate is not { Kind: SmtValueKind.Reference })
            {
                formula = null!;
                return false;
            }

            formula = candidate;
            return true;
        }

        private static bool TryGetNotNullIfNotNullParameterName(IMethodSymbol methodSymbol, out string parameterName)
        {
            if (TryGetNotNullIfNotNullParameterName(methodSymbol.GetReturnTypeAttributes(), out parameterName))
            {
                return true;
            }

            if (!SymbolEqualityComparer.Default.Equals(methodSymbol, methodSymbol.OriginalDefinition) &&
                TryGetNotNullIfNotNullParameterName(methodSymbol.OriginalDefinition.GetReturnTypeAttributes(), out parameterName))
            {
                return true;
            }

            parameterName = string.Empty;
            return false;
        }

        private static bool TryGetNotNullIfNotNullParameterName(IPropertySymbol propertySymbol, out string parameterName)
        {
            if (TryGetNotNullIfNotNullParameterName(propertySymbol.GetAttributes(), out parameterName) ||
                TryGetNotNullIfNotNullParameterName(propertySymbol.GetMethod?.GetReturnTypeAttributes() ?? ImmutableArray<AttributeData>.Empty, out parameterName))
            {
                return true;
            }

            if (!SymbolEqualityComparer.Default.Equals(propertySymbol, propertySymbol.OriginalDefinition) &&
                (TryGetNotNullIfNotNullParameterName(propertySymbol.OriginalDefinition.GetAttributes(), out parameterName) ||
                 TryGetNotNullIfNotNullParameterName(propertySymbol.OriginalDefinition.GetMethod?.GetReturnTypeAttributes() ?? ImmutableArray<AttributeData>.Empty, out parameterName)))
            {
                return true;
            }

            parameterName = string.Empty;
            return false;
        }

        private static bool TryGetNotNullIfNotNullParameterName(
            ImmutableArray<AttributeData> attributes,
            out string parameterName)
        {
            foreach (var attribute in attributes)
            {
                if (!string.Equals(
                        GetFullMetadataName(attribute.AttributeClass),
                        NotNullIfNotNullAttributeMetadataName,
                        StringComparison.Ordinal) ||
                    attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not string candidate ||
                    string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                parameterName = candidate;
                return true;
            }

            parameterName = string.Empty;
            return false;
        }

        private static bool TryGetPropertyArgumentExpression(
            IPropertyReferenceOperation propertyReferenceOperation,
            int parameterIndex,
            out ExpressionSyntax expression)
        {
            expression = null!;
            if (parameterIndex < 0 ||
                parameterIndex >= propertyReferenceOperation.Property.Parameters.Length)
            {
                return false;
            }

            var parameter = propertyReferenceOperation.Property.Parameters[parameterIndex];
            foreach (var argument in propertyReferenceOperation.Arguments)
            {
                if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression)
                {
                    expression = argumentExpression;
                    return true;
                }
            }

            if (parameterIndex < propertyReferenceOperation.Arguments.Length &&
                propertyReferenceOperation.Arguments[parameterIndex].Value.Syntax is ExpressionSyntax fallbackExpression)
            {
                expression = fallbackExpression;
                return true;
            }

            return false;
        }

        private static string CreateNotNullIfNotNullFallbackVariableName(ExpressionSyntax expression)
        {
            return "$notNullIfNotNullResultNonNull#" +
                RuntimeHelpers.GetHashCode(expression.SyntaxTree).ToString(CultureInfo.InvariantCulture) +
                "#" +
                expression.SpanStart.ToString(CultureInfo.InvariantCulture) +
                "#" +
                expression.Span.Length.ToString(CultureInfo.InvariantCulture);
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

        private static bool TryTranslateCheckedIntegralCastComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string>? nonZeroDivisors)
        {
            if (TryTranslateCheckedIntegralCastComparisonSide(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    binaryExpression.Kind(),
                    castOnLeft: true,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors))
            {
                return true;
            }

            return TryTranslateCheckedIntegralCastComparisonSide(
                binaryExpression.Right,
                binaryExpression.Left,
                binaryExpression.Kind(),
                castOnLeft: false,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                nonZeroDivisors);
        }

        private static bool TryTranslateCheckedIntegralCastComparisonSide(
            ExpressionSyntax castCandidate,
            ExpressionSyntax otherExpression,
            SyntaxKind comparisonKind,
            bool castOnLeft,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            ISet<string>? nonZeroDivisors)
        {
            formula = null;
            if (!TryGetCheckedIntegralCastOperand(
                    castCandidate,
                    semanticModel,
                    cancellationToken,
                    out var operandExpression,
                    out var targetSpecialType) ||
                !TryGetSupportedIntegralRange(targetSpecialType, out var targetMin, out var targetMax) ||
                !TryTranslateValueWithSafeDivisors(
                    operandExpression,
                    semanticModel,
                    cancellationToken,
                    out var operandFormula,
                    getSymbolVersion,
                    inlineDepth,
                    Array.Empty<SmtFormula>(),
                    nonZeroDivisors) ||
                operandFormula is not { Kind: SmtValueKind.Int } ||
                !TryTranslateValueWithSafeDivisors(
                    otherExpression,
                    semanticModel,
                    cancellationToken,
                    out var otherFormula,
                    getSymbolVersion,
                    inlineDepth,
                    Array.Empty<SmtFormula>(),
                    nonZeroDivisors) ||
                otherFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var leftFormula = castOnLeft ? operandFormula : otherFormula;
            var rightFormula = castOnLeft ? otherFormula : operandFormula;
            if (!TryTranslateComparison(comparisonKind, leftFormula, rightFormula, out var comparisonFormula) ||
                comparisonFormula == null)
            {
                return false;
            }

            formula = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                CreateIntegralRangeFormula(operandFormula, targetMin, targetMax),
                comparisonFormula);
            return true;
        }

        private static bool TryGetCheckedIntegralCastOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax operand,
            out SpecialType targetSpecialType)
        {
            expression = UnwrapExpression(expression);
            if (expression is not CastExpressionSyntax castExpression ||
                semanticModel.GetOperation(castExpression, cancellationToken) is not IConversionOperation
                {
                    IsChecked: true,
                    OperatorMethod: null
                } conversionOperation ||
                conversionOperation.Operand.Type is not { } sourceType ||
                conversionOperation.Type is not { } targetType ||
                !IsIntegralOrEnumType(sourceType) ||
                !IsIntegralOrEnumType(targetType) ||
                !TryGetIntegralSpecialType(targetType, out targetSpecialType))
            {
                operand = null!;
                targetSpecialType = SpecialType.None;
                return false;
            }

            operand = castExpression.Expression;
            return true;
        }

        private static bool TryGetSupportedIntegralRange(SpecialType specialType, out long min, out long max)
        {
            switch (specialType)
            {
                case SpecialType.System_SByte:
                    min = sbyte.MinValue;
                    max = sbyte.MaxValue;
                    return true;
                case SpecialType.System_Byte:
                    min = byte.MinValue;
                    max = byte.MaxValue;
                    return true;
                case SpecialType.System_Int16:
                    min = short.MinValue;
                    max = short.MaxValue;
                    return true;
                case SpecialType.System_UInt16:
                case SpecialType.System_Char:
                    min = ushort.MinValue;
                    max = ushort.MaxValue;
                    return true;
                case SpecialType.System_Int32:
                    min = int.MinValue;
                    max = int.MaxValue;
                    return true;
                case SpecialType.System_UInt32:
                    min = uint.MinValue;
                    max = uint.MaxValue;
                    return true;
                case SpecialType.System_Int64:
                    min = long.MinValue;
                    max = long.MaxValue;
                    return true;
                default:
                    min = default;
                    max = default;
                    return false;
            }
        }

        private static SmtFormula CreateIntegralRangeFormula(SmtFormula value, long min, long max)
        {
            var lowerBound = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                value,
                new SmtIntegerConstant(min));
            var upperBound = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                value,
                new SmtIntegerConstant(max));
            return new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
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
                IsKnownNonNegativeIntegralMemberAccess(memberAccess, semanticModel, cancellationToken);
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
            internal SmtVariableSubstitution(
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

            internal string ExactName { get; }

            internal string SimpleMemberPrefix { get; }

            internal string FormulaMemberPrefix { get; }

            internal SmtFormula Replacement { get; }
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
                    CreateNonNullFormula(coalesceReference),
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
                CreateNonNullFormula(receiverFormula),
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
                    CreateNonNullFormula(referenceFormula),
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

            if (TryCreatePrefixSubstringComparisonFormula(
                    binaryExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
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

        private static bool TryCreatePrefixSubstringComparisonFormula(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (TryGetPrefixSubstringComparisonParts(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var receiverExpression,
                    out var prefix) ||
                TryGetPrefixSubstringComparisonParts(
                    binaryExpression.Right,
                    binaryExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out receiverExpression,
                    out prefix))
            {
                if (!TryTranslateStringValue(receiverExpression, semanticModel, cancellationToken, out var receiverFormula, getSymbolVersion, inlineDepth) ||
                    receiverFormula == null ||
                    !TryCreateStringNonNullFormula(receiverExpression, semanticModel, cancellationToken, out var receiverNonNull, getSymbolVersion, inlineDepth) ||
                    receiverNonNull == null)
                {
                    return false;
                }

                var prefixMatch = new SmtStringStartsWithFormula(receiverFormula, new SmtStringConstant(prefix));
                SmtFormula predicate = binaryExpression.IsKind(SyntaxKind.EqualsExpression)
                    ? prefixMatch
                    : new SmtUnaryFormula(SmtUnaryOperator.Not, prefixMatch);
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, receiverNonNull, predicate);
                return true;
            }

            return false;
        }

        private static bool TryGetPrefixSubstringComparisonParts(
            ExpressionSyntax substringExpression,
            ExpressionSyntax prefixExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax receiverExpression,
            out string prefix)
        {
            receiverExpression = null!;
            prefix = string.Empty;
            var constantPrefix = TryGetConstantString(prefixExpression, semanticModel, cancellationToken);
            if (constantPrefix == null)
            {
                return false;
            }

            substringExpression = UnwrapExpression(substringExpression);
            if (substringExpression is not InvocationExpressionSyntax invocationExpression ||
                semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod is not
                {
                    IsStatic: false,
                    Name: "Substring",
                    ContainingType.SpecialType: SpecialType.System_String
                } ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax receiver ||
                invocationOperation.Arguments.Length != 2 ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax startExpression ||
                invocationOperation.Arguments[1].Value.Syntax is not ExpressionSyntax lengthExpression ||
                !TryGetIntegralConstantValue(startExpression, semanticModel, cancellationToken, out var start) ||
                start != 0 ||
                !TryGetIntegralConstantValue(lengthExpression, semanticModel, cancellationToken, out var length) ||
                length != constantPrefix.Length)
            {
                return false;
            }

            receiverExpression = receiver;
            prefix = constantPrefix;
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
                    Array.Empty<SmtFormula>(),
                    nonZeroDivisors) ||
                !TryTranslateTupleElementValues(
                    binaryExpression.Right,
                    rightFields,
                    semanticModel,
                    cancellationToken,
                    out var rightValues,
                    getSymbolVersion,
                    inlineDepth,
                    Array.Empty<SmtFormula>(),
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
            IReadOnlyCollection<SmtFormula> pathFacts,
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
                            pathFacts,
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
            IReadOnlyCollection<SmtFormula> pathFacts,
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
                    pathFacts,
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
                formula = CreateNonNullFormula(referenceFormula);
                return true;
            }

            return false;
        }


    }
}
