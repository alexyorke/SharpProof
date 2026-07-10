using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Smt;

internal static partial class CSharpConditionToFormula
{
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
        var pathFactArray = pathFacts == null
            ? Array.Empty<SmtFormula>()
            : pathFacts as SmtFormula[] ?? pathFacts.ToArray();

        return TryTranslateValueWithSafeDivisors(
            expression,
            semanticModel,
            cancellationToken,
            out formula,
            getSymbolVersion,
            inlineDepth,
            pathFactArray,
            CollectNonZeroDivisorFacts(pathFactArray));
    }

    private static bool TryTranslateValueWithSafeDivisors(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth,
        IReadOnlyCollection<SmtFormula> pathFacts,
        ISet<string>? nonZeroDivisors)
    {
        if (TryTranslateValue(expression, semanticModel, cancellationToken, out formula, getSymbolVersion,
                inlineDepth)) return true;

        if ((nonZeroDivisors == null || nonZeroDivisors.Count == 0) &&
            pathFacts.Count == 0)
            return false;

        return TryTranslateIntegralTermWithSafeDivisors(
            expression,
            semanticModel,
            cancellationToken,
            out formula,
            getSymbolVersion,
            inlineDepth,
            nonZeroDivisors ?? new HashSet<string>(StringComparer.Ordinal),
            pathFacts);
    }

    private static ISet<string>? CollectNonZeroDivisorFacts(IEnumerable<SmtFormula>? pathFacts)
    {
        if (pathFacts == null) return null;

        HashSet<string>? facts = null;
        foreach (var pathFact in pathFacts) CollectNonZeroDivisorFacts(pathFact, ref facts);

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
        if (expression.Kind != SmtValueKind.Int) return;

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
            _ => op
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
            _ => false
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

        if (TryTranslateDefaultValue(expression, semanticModel, cancellationToken, out formula)) return true;

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
            return true;

        if (expression is BinaryExpressionSyntax asExpression &&
            asExpression.IsKind(SyntaxKind.AsExpression) &&
            TryTranslateIdentityPreservingAsValue(
                asExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (expression is ElementAccessExpressionSyntax elementAccessExpression &&
            TryTranslateBuiltInElementAccessValue(
                elementAccessExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (expression is ConditionalAccessExpressionSyntax conditionalAccessExpression &&
            TryTranslateConditionalAccessReferenceValue(
                conditionalAccessExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (expression is ConditionalExpressionSyntax conditionalExpression &&
            TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula,
                getSymbolVersion, inlineDepth) &&
            conditionFormula != null &&
            TryTranslateValue(conditionalExpression.WhenTrue, semanticModel, cancellationToken, out var whenTrueFormula,
                getSymbolVersion, inlineDepth) &&
            whenTrueFormula != null &&
            TryTranslateValue(conditionalExpression.WhenFalse, semanticModel, cancellationToken,
                out var whenFalseFormula, getSymbolVersion, inlineDepth) &&
            whenFalseFormula != null &&
            whenTrueFormula.Kind == whenFalseFormula.Kind)
        {
            formula = new SmtConditionalFormula(conditionFormula, whenTrueFormula, whenFalseFormula,
                whenTrueFormula.Kind);
            return true;
        }

        if (expression is AssignmentExpressionSyntax coalesceAssignmentExpression &&
            TryTranslateCoalesceAssignmentValue(
                coalesceAssignmentExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (expression is SwitchExpressionSyntax switchExpression &&
            TryTranslateSwitchExpressionValue(switchExpression, semanticModel, cancellationToken, out formula,
                getSymbolVersion, inlineDepth))
            return true;

        if (expression is BinaryExpressionSyntax nullableCoalesceExpression &&
            nullableCoalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            TryTranslateNullableCoalesceValue(
                nullableCoalesceExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (expression is InvocationExpressionSyntax nullableGetValueOrDefaultInvocation &&
            TryTranslateNullableGetValueOrDefaultValue(
                nullableGetValueOrDefaultInvocation,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (expression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            TryTranslateValue(coalesceExpression.Left, semanticModel, cancellationToken, out var coalesceLeft,
                getSymbolVersion, inlineDepth) &&
            coalesceLeft is { Kind: SmtValueKind.Reference } &&
            TryTranslateValue(coalesceExpression.Right, semanticModel, cancellationToken, out var coalesceRight,
                getSymbolVersion, inlineDepth) &&
            coalesceRight is { Kind: SmtValueKind.Reference })
        {
            formula = new SmtConditionalFormula(
                CreateNonNullFormula(coalesceLeft),
                coalesceLeft,
                coalesceRight,
                SmtValueKind.Reference);
            return true;
        }

        if (TryTranslateBooleanTerm(expression, semanticModel, cancellationToken, out formula, getSymbolVersion,
                inlineDepth)) return true;

        if (TryTranslateIntegralTerm(expression, semanticModel, cancellationToken, out formula, getSymbolVersion,
                inlineDepth)) return true;

        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is not ILocalSymbol && symbol is not IParameterSymbol)
            return TryTranslateMemberValue(expression, semanticModel, cancellationToken, out formula, getSymbolVersion,
                inlineDepth);

        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (type == null) return false;

        if (type.SpecialType == SpecialType.System_Boolean)
        {
            formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Bool);
            return true;
        }

        if (IsIntegerSmtType(type))
        {
            formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Int);
            return true;
        }

        if (IsReferenceLikeType(type))
        {
            formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Reference);
            return true;
        }

        if (SymbolicTypeFacts.IsSupportedTupleCarrierType(type))
        {
            formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Reference);
            return true;
        }

        return false;
    }

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
                continue;

            var symbol = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol?.OriginalDefinition;
            if (symbol is not ILocalSymbol &&
                symbol is not IParameterSymbol &&
                symbol is not IFieldSymbol &&
                symbol is not IPropertySymbol)
                continue;

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
        return symbol.Kind +
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
            return false;

        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type == null) return false;

        if (type.SpecialType == SpecialType.System_Boolean)
        {
            formula = new SmtBooleanConstant(false);
            return true;
        }

        if (IsIntegerSmtType(type))
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

        if (IsIntegerSmtType(type))
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
            return false;

        var armConditions = new List<SmtFormula>();
        var armValues = new List<SmtFormula>();
        foreach (var arm in switchExpression.Arms)
        {
            if (!TryTranslateValue(arm.Expression, semanticModel, cancellationToken, out var armValue, getSymbolVersion,
                    inlineDepth) ||
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
            result = new SmtConditionalFormula(
                armConditions[index],
                armValues[index],
                result,
                result.Kind);

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
        if (!HasSupportedBooleanType(expression, semanticModel, cancellationToken)) return false;

        if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
            prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
            TryTranslate(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion,
                inlineDepth) &&
            operand != null)
        {
            formula = new SmtUnaryFormula(SmtUnaryOperator.Not, operand);
            return true;
        }

        if (expression is CastExpressionSyntax booleanCastExpression &&
            IsIdentityPreservingBooleanCast(booleanCastExpression, semanticModel, cancellationToken) &&
            TryTranslate(booleanCastExpression.Expression, semanticModel, cancellationToken, out formula,
                getSymbolVersion, inlineDepth) &&
            formula is { Kind: SmtValueKind.Bool })
            return true;

        if (expression is ConditionalExpressionSyntax conditionalExpression &&
            TryTranslateConditionalBooleanExpression(
                conditionalExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                null))
            return formula != null;

        if (expression is MemberAccessExpressionSyntax memberAccessExpression &&
            TryTranslateRegexMatchSuccessProperty(
                memberAccessExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth))
            return formula != null;

        if (expression is BinaryExpressionSyntax binaryExpression)
        {
            if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftAnd, getSymbolVersion,
                    inlineDepth) &&
                TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightAnd,
                    getSymbolVersion, inlineDepth) &&
                leftAnd != null &&
                rightAnd != null)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftAnd, rightAnd);
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.BitwiseAndExpression) &&
                TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftBitwiseAnd,
                    getSymbolVersion, inlineDepth) &&
                TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightBitwiseAnd,
                    getSymbolVersion, inlineDepth) &&
                leftBitwiseAnd != null &&
                rightBitwiseAnd != null)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftBitwiseAnd, rightBitwiseAnd);
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftOr, getSymbolVersion,
                    inlineDepth) &&
                TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightOr,
                    getSymbolVersion, inlineDepth) &&
                leftOr != null &&
                rightOr != null)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftOr, rightOr);
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.BitwiseOrExpression) &&
                TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftBitwiseOr,
                    getSymbolVersion, inlineDepth) &&
                TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightBitwiseOr,
                    getSymbolVersion, inlineDepth) &&
                leftBitwiseOr != null &&
                rightBitwiseOr != null)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftBitwiseOr, rightBitwiseOr);
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.ExclusiveOrExpression) &&
                TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftExclusiveOr,
                    getSymbolVersion, inlineDepth) &&
                TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightExclusiveOr,
                    getSymbolVersion, inlineDepth) &&
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

            if (TryTranslateNullableValueMemberComparison(
                    binaryExpression,
                    semanticModel,
                    cancellationToken,
                    out var nullableValueMemberComparison,
                    getSymbolVersion,
                    inlineDepth,
                    null) &&
                nullableValueMemberComparison != null)
            {
                formula = nullableValueMemberComparison;
                return true;
            }

            if (TryTranslateTypeOfComparison(binaryExpression, semanticModel, cancellationToken,
                    out var typeOfComparison) &&
                typeOfComparison != null)
            {
                formula = typeOfComparison;
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

            if (IsTupleEqualityComparison(binaryExpression, semanticModel, cancellationToken))
                return TryTranslateTupleEqualityComparison(
                           binaryExpression,
                           semanticModel,
                           cancellationToken,
                           out formula,
                           getSymbolVersion,
                           inlineDepth,
                           null) &&
                       formula != null;

            if (TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue,
                    getSymbolVersion, inlineDepth) &&
                TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue,
                    getSymbolVersion, inlineDepth) &&
                leftValue != null &&
                rightValue != null &&
                TryTranslateComparison(binaryExpression.Kind(), leftValue, rightValue, out var comparison))
            {
                formula = comparison;
                return true;
            }
        }

        if (expression is IsPatternExpressionSyntax isPatternExpression)
            return TryTranslatePatternExpression(isPatternExpression, semanticModel, cancellationToken, out formula,
                getSymbolVersion, inlineDepth);

        if (expression is InvocationExpressionSyntax invocationExpression)
        {
            if (TryTranslateKnownStringBooleanInvocation(invocationExpression, semanticModel, cancellationToken,
                    out formula, getSymbolVersion, inlineDepth)) return formula != null;

            return TryTranslateSourceBooleanInvocation(invocationExpression, semanticModel, cancellationToken,
                out formula, getSymbolVersion, inlineDepth);
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
        if (!HasSupportedIntegralType(expression, semanticModel, cancellationToken)) return false;

        if (expression is InvocationExpressionSyntax invocationExpression &&
            TryTranslateArrayGetLengthInvocation(
                invocationExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (expression is InvocationExpressionSyntax mathInvocationExpression &&
            TryTranslateIntegralMathInvocation(
                mathInvocationExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                null,
                null))
            return true;

        if (expression is PrefixUnaryExpressionSyntax prefixUnary)
        {
            if (prefixUnary.IsKind(SyntaxKind.UnaryPlusExpression))
                return TryTranslateValue(prefixUnary.Operand, semanticModel, cancellationToken, out formula,
                           getSymbolVersion, inlineDepth) &&
                       formula is { Kind: SmtValueKind.Int };

            if (prefixUnary.IsKind(SyntaxKind.UnaryMinusExpression) &&
                TryTranslateValue(prefixUnary.Operand, semanticModel, cancellationToken, out var operand,
                    getSymbolVersion, inlineDepth) &&
                operand is { Kind: SmtValueKind.Int })
            {
                formula = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, operand);
                return true;
            }
        }

        if (expression is CastExpressionSyntax castExpression &&
            IsRepresentationPreservingIntegralCast(castExpression, semanticModel, cancellationToken) &&
            TryTranslateValue(castExpression.Expression, semanticModel, cancellationToken, out var castOperand,
                getSymbolVersion, inlineDepth) &&
            castOperand is { Kind: SmtValueKind.Int })
        {
            formula = castOperand;
            return true;
        }

        if (expression is BinaryExpressionSyntax binaryExpression)
        {
            if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var addLeft,
                    getSymbolVersion, inlineDepth) &&
                TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var addRight,
                    getSymbolVersion, inlineDepth) &&
                addLeft is { Kind: SmtValueKind.Int } &&
                addRight is { Kind: SmtValueKind.Int })
            {
                formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, addLeft, addRight);
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var subtractLeft,
                    getSymbolVersion, inlineDepth) &&
                TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var subtractRight,
                    getSymbolVersion, inlineDepth) &&
                subtractLeft is { Kind: SmtValueKind.Int } &&
                subtractRight is { Kind: SmtValueKind.Int })
            {
                formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, subtractLeft, subtractRight);
                return true;
            }

            if (binaryExpression.IsKind(SyntaxKind.MultiplyExpression) &&
                TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var multiplyLeft,
                    getSymbolVersion, inlineDepth) &&
                TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var multiplyRight,
                    getSymbolVersion, inlineDepth) &&
                multiplyLeft is { Kind: SmtValueKind.Int } &&
                multiplyRight is { Kind: SmtValueKind.Int } &&
                (multiplyLeft is SmtIntegerConstant || multiplyRight is SmtIntegerConstant))
            {
                formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, multiplyLeft, multiplyRight);
                return true;
            }

            if ((binaryExpression.IsKind(SyntaxKind.DivideExpression) ||
                 binaryExpression.IsKind(SyntaxKind.ModuloExpression)) &&
                TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var dividend,
                    getSymbolVersion, inlineDepth) &&
                TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var divisor,
                    getSymbolVersion, inlineDepth) &&
                dividend is { Kind: SmtValueKind.Int } &&
                divisor is { Kind: SmtValueKind.Int } &&
                IsSafeIntegerDivisor(divisor, null))
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
        if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation
                invocationOperation ||
            invocationOperation.TargetMethod.Name != "GetLength" ||
            invocationOperation.TargetMethod.Parameters.Length != 1 ||
            invocationOperation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Int32 ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
            invocationOperation.Instance.Type is not IArrayTypeSymbol arrayType ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0,
                out var dimensionExpression) ||
            !TryGetConstantNonNegativeInt(dimensionExpression, semanticModel, cancellationToken, out var dimension) ||
            dimension >= arrayType.Rank)
            return false;

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
        ISet<string> nonZeroDivisors,
        IReadOnlyCollection<SmtFormula> pathFacts)
    {
        formula = null;
        expression = UnwrapExpression(expression);
        if (!HasSupportedIntegralType(expression, semanticModel, cancellationToken)) return false;

        if (expression is InvocationExpressionSyntax invocationExpression &&
            TryTranslateIntegralMathInvocation(
                invocationExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                nonZeroDivisors,
                pathFacts))
            return true;

        if (expression is InvocationExpressionSyntax mathAbsInvocation &&
            TryTranslateSafeMathAbsRemainder(
                mathAbsInvocation,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                nonZeroDivisors,
                pathFacts))
            return true;

        if (expression is PrefixUnaryExpressionSyntax prefixUnary)
        {
            if (prefixUnary.IsKind(SyntaxKind.UnaryPlusExpression))
                return TryTranslateIntegralOperandWithSafeDivisors(
                           prefixUnary.Operand,
                           semanticModel,
                           cancellationToken,
                           out formula,
                           getSymbolVersion,
                           inlineDepth,
                           nonZeroDivisors,
                           pathFacts) &&
                       formula is { Kind: SmtValueKind.Int };

            if (prefixUnary.IsKind(SyntaxKind.UnaryMinusExpression) &&
                TryTranslateIntegralOperandWithSafeDivisors(
                    prefixUnary.Operand,
                    semanticModel,
                    cancellationToken,
                    out var operand,
                    getSymbolVersion,
                    inlineDepth,
                    nonZeroDivisors,
                    pathFacts) &&
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
                nonZeroDivisors,
                pathFacts) &&
            castOperand is { Kind: SmtValueKind.Int })
        {
            formula = castOperand;
            return true;
        }

        if (expression is not BinaryExpressionSyntax binaryExpression) return false;

        if (!TryTranslateIntegralOperandWithSafeDivisors(
                binaryExpression.Left,
                semanticModel,
                cancellationToken,
                out var left,
                getSymbolVersion,
                inlineDepth,
                nonZeroDivisors,
                pathFacts) ||
            left is not { Kind: SmtValueKind.Int } ||
            !TryTranslateIntegralOperandWithSafeDivisors(
                binaryExpression.Right,
                semanticModel,
                cancellationToken,
                out var right,
                getSymbolVersion,
                inlineDepth,
                nonZeroDivisors,
                pathFacts) ||
            right is not { Kind: SmtValueKind.Int })
            return false;

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

    private static bool TryTranslateIntegralMathInvocation(
        InvocationExpressionSyntax invocationExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth,
        ISet<string>? nonZeroDivisors,
        IReadOnlyCollection<SmtFormula>? pathFacts)
    {
        formula = null;
        if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation
                invocationOperation ||
            invocationOperation.TargetMethod.ContainingType?.ToDisplayString() != "System.Math" ||
            !invocationOperation.TargetMethod.IsStatic ||
            !IsIntegralOrEnumType(invocationOperation.TargetMethod.ReturnType))
            return false;

        var facts = pathFacts ?? Array.Empty<SmtFormula>();
        var safeDivisors = nonZeroDivisors ?? new HashSet<string>(StringComparer.Ordinal);
        var method = invocationOperation.TargetMethod;

        if ((method.Name == "Min" || method.Name == "Max") &&
            method.Parameters.Length == 2 &&
            TryTranslateIntegralMathArgument(
                invocationOperation,
                0,
                semanticModel,
                cancellationToken,
                out var left,
                getSymbolVersion,
                inlineDepth,
                safeDivisors,
                facts) &&
            TryTranslateIntegralMathArgument(
                invocationOperation,
                1,
                semanticModel,
                cancellationToken,
                out var right,
                getSymbolVersion,
                inlineDepth,
                safeDivisors,
                facts))
        {
            var comparison = method.Name == "Min"
                ? new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, left, right)
                : new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, left, right);
            formula = new SmtConditionalFormula(comparison, left, right, SmtValueKind.Int);
            return true;
        }

        if (method.Name == "Clamp" &&
            method.Parameters.Length == 3 &&
            TryTranslateIntegralMathArgument(
                invocationOperation,
                0,
                semanticModel,
                cancellationToken,
                out var value,
                getSymbolVersion,
                inlineDepth,
                safeDivisors,
                facts) &&
            TryTranslateIntegralMathArgument(
                invocationOperation,
                1,
                semanticModel,
                cancellationToken,
                out var min,
                getSymbolVersion,
                inlineDepth,
                safeDivisors,
                facts) &&
            TryTranslateIntegralMathArgument(
                invocationOperation,
                2,
                semanticModel,
                cancellationToken,
                out var max,
                getSymbolVersion,
                inlineDepth,
                safeDivisors,
                facts) &&
            IsKnownLessThanOrEqual(min, max, facts))
        {
            var belowMin = new SmtBinaryFormula(SmtBinaryOperator.LessThan, value, min);
            var aboveMax = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, value, max);
            formula = new SmtConditionalFormula(
                belowMin,
                min,
                new SmtConditionalFormula(aboveMax, max, value, SmtValueKind.Int),
                SmtValueKind.Int);
            return true;
        }

        return false;
    }

    private static bool TryTranslateIntegralMathArgument(
        IInvocationOperation invocationOperation,
        int parameterIndex,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula argument,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth,
        ISet<string> nonZeroDivisors,
        IReadOnlyCollection<SmtFormula> pathFacts)
    {
        argument = null!;
        if (parameterIndex < 0 ||
            parameterIndex >= invocationOperation.TargetMethod.Parameters.Length ||
            !IsIntegralOrEnumType(invocationOperation.TargetMethod.Parameters[parameterIndex].Type) ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, parameterIndex,
                out var argumentExpression) ||
            !TryTranslateIntegralOperandWithSafeDivisors(
                argumentExpression,
                semanticModel,
                cancellationToken,
                out var candidate,
                getSymbolVersion,
                inlineDepth,
                nonZeroDivisors,
                pathFacts) ||
            candidate is not { Kind: SmtValueKind.Int })
            return false;

        argument = candidate;
        return true;
    }

    private static bool IsKnownLessThanOrEqual(
        SmtFormula left,
        SmtFormula right,
        IReadOnlyCollection<SmtFormula> pathFacts)
    {
        if (Equals(left, right)) return true;

        if (left is SmtIntegerConstant leftConstant &&
            right is SmtIntegerConstant rightConstant)
            return leftConstant.Value <= rightConstant.Value;

        if (left is SmtIntegerConstant requiredLowerBound &&
            IsKnownAtLeast(right, requiredLowerBound.Value, pathFacts))
            return true;

        foreach (var fact in pathFacts)
        {
            if (fact is not SmtBinaryFormula comparison) continue;

            if (Equals(comparison.Left, left) &&
                Equals(comparison.Right, right) &&
                comparison.Operator is SmtBinaryOperator.LessThan or SmtBinaryOperator.LessThanOrEqual
                    or SmtBinaryOperator.Equal)
                return true;

            if (Equals(comparison.Left, right) &&
                Equals(comparison.Right, left) &&
                comparison.Operator is SmtBinaryOperator.GreaterThan or SmtBinaryOperator.GreaterThanOrEqual
                    or SmtBinaryOperator.Equal)
                return true;
        }

        return false;
    }

    private static bool IsKnownAtLeast(
        SmtFormula value,
        long lowerBound,
        IReadOnlyCollection<SmtFormula> pathFacts)
    {
        if (value is SmtIntegerConstant constant) return constant.Value >= lowerBound;

        if (PathFactsProveLowerBound(value, lowerBound, pathFacts)) return true;

        if (value is SmtIntegerBinaryTerm
            {
                Operator: SmtIntegerBinaryOperator.Add,
                Left: var addLeft,
                Right: SmtIntegerConstant addRight
            })
            return TrySubtract(lowerBound, addRight.Value, out var adjustedLowerBound) &&
                   IsKnownAtLeast(addLeft, adjustedLowerBound, pathFacts);

        if (value is SmtIntegerBinaryTerm
            {
                Operator: SmtIntegerBinaryOperator.Subtract,
                Left: var subtractLeft,
                Right: SmtIntegerConstant subtractRight
            })
            return TryAdd(lowerBound, subtractRight.Value, out var adjustedLowerBound) &&
                   IsKnownAtLeast(subtractLeft, adjustedLowerBound, pathFacts);

        return false;
    }

    private static bool PathFactsProveLowerBound(
        SmtFormula value,
        long lowerBound,
        IReadOnlyCollection<SmtFormula> pathFacts)
    {
        foreach (var fact in pathFacts)
        {
            if (fact is not SmtBinaryFormula comparison) continue;

            if (Equals(comparison.Left, value) &&
                comparison.Right is SmtIntegerConstant rightConstant)
            {
                if (comparison.Operator == SmtBinaryOperator.GreaterThanOrEqual &&
                    rightConstant.Value >= lowerBound)
                    return true;

                if (comparison.Operator == SmtBinaryOperator.GreaterThan &&
                    TryAdd(rightConstant.Value, 1, out var exclusiveLowerBound) &&
                    exclusiveLowerBound >= lowerBound)
                    return true;
            }

            if (Equals(comparison.Right, value) &&
                comparison.Left is SmtIntegerConstant leftConstant)
            {
                if (comparison.Operator == SmtBinaryOperator.LessThanOrEqual &&
                    leftConstant.Value >= lowerBound)
                    return true;

                if (comparison.Operator == SmtBinaryOperator.LessThan &&
                    TryAdd(leftConstant.Value, 1, out var exclusiveLowerBound) &&
                    exclusiveLowerBound >= lowerBound)
                    return true;
            }
        }

        return false;
    }

    private static bool TryAdd(long left, long right, out long result)
    {
        try
        {
            result = checked(left + right);
            return true;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static bool TrySubtract(long left, long right, out long result)
    {
        try
        {
            result = checked(left - right);
            return true;
        }
        catch (OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static bool TryTranslateSafeMathAbsRemainder(
        InvocationExpressionSyntax invocationExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth,
        ISet<string> nonZeroDivisors,
        IReadOnlyCollection<SmtFormula> pathFacts)
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
                nonZeroDivisors,
                pathFacts) ||
            dividend is not { Kind: SmtValueKind.Int } ||
            !TryTranslateIntegralOperandWithSafeDivisors(
                divisorExpression,
                semanticModel,
                cancellationToken,
                out var divisor,
                getSymbolVersion,
                inlineDepth,
                nonZeroDivisors,
                pathFacts) ||
            divisor is not { Kind: SmtValueKind.Int } ||
            !IsSafeIntegerDivisor(divisor, nonZeroDivisors))
            return false;

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
        return CSharpMathPatternRecognizer.TryGetMathAbsRemainderOperands(
            invocationExpression,
            semanticModel,
            cancellationToken,
            out dividendExpression,
            out divisorExpression);
    }

    private static bool TryTranslateIntegralOperandWithSafeDivisors(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth,
        ISet<string> nonZeroDivisors,
        IReadOnlyCollection<SmtFormula> pathFacts)
    {
        if (TryTranslateValue(expression, semanticModel, cancellationToken, out formula, getSymbolVersion,
                inlineDepth) &&
            formula is { Kind: SmtValueKind.Int })
            return true;

        return TryTranslateIntegralTermWithSafeDivisors(
                   expression,
                   semanticModel,
                   cancellationToken,
                   out formula,
                   getSymbolVersion,
                   inlineDepth,
                   nonZeroDivisors,
                   pathFacts) &&
               formula is { Kind: SmtValueKind.Int };
    }

    private static bool TryTranslateCoalesceAssignmentValue(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        formula = null;
        if (!assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) ||
            ContainsNestedAssignment(assignment.Right))
            return false;

        if (TryTranslateNullableCoalesceAssignmentValue(
                assignment,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth))
            return true;

        return TryTranslateReferenceCoalesceAssignmentValue(
            assignment,
            semanticModel,
            cancellationToken,
            out formula,
            getSymbolVersion,
            inlineDepth);
    }

    private static bool TryTranslateNullableCoalesceAssignmentValue(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        formula = null;
        if (!TryTranslateNullableValueParts(
                assignment.Left,
                semanticModel,
                cancellationToken,
                out var hasValueFormula,
                out var nullableValueFormula,
                getSymbolVersion,
                inlineDepth) ||
            nullableValueFormula == null ||
            !TryTranslateValue(
                assignment.Right,
                semanticModel,
                cancellationToken,
                out var fallbackFormula,
                getSymbolVersion,
                inlineDepth) ||
            fallbackFormula == null ||
            nullableValueFormula.Kind != fallbackFormula.Kind)
            return false;

        formula = new SmtConditionalFormula(
            hasValueFormula,
            nullableValueFormula,
            fallbackFormula,
            fallbackFormula.Kind);
        return true;
    }

    private static bool TryTranslateReferenceCoalesceAssignmentValue(
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        formula = null;
        if (!IsLocalOrParameterExpression(assignment.Left, semanticModel, cancellationToken) ||
            !TryTranslateValue(assignment.Left, semanticModel, cancellationToken, out var targetFormula,
                getSymbolVersion, inlineDepth) ||
            targetFormula is not { Kind: SmtValueKind.Reference } ||
            !TryTranslateValue(assignment.Right, semanticModel, cancellationToken, out var fallbackFormula,
                getSymbolVersion, inlineDepth) ||
            fallbackFormula is not { Kind: SmtValueKind.Reference })
            return false;

        formula = new SmtConditionalFormula(
            CreateNonNullFormula(targetFormula),
            targetFormula,
            fallbackFormula,
            SmtValueKind.Reference);
        return true;
    }

    private static bool IsLocalOrParameterExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapExpression(expression);
        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition is ILocalSymbol
            or IParameterSymbol;
    }

    private static bool IsSafeIntegerDivisor(SmtFormula divisor, ISet<string>? nonZeroDivisors)
    {
        if (divisor is SmtIntegerConstant integerConstant) return integerConstant.Value != 0;

        return nonZeroDivisors != null &&
               nonZeroDivisors.Contains(CreateDivisorKey(divisor));
    }

    private static bool TryTranslateDecimalZeroComparison(
        BinaryExpressionSyntax binaryExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion)
    {
        formula = null;
        if (!TryGetDecimalZeroComparisonOperator(
                binaryExpression.Kind(),
                false,
                out var operatorKind))
            return false;

        if (!TryCreateDecimalZeroComparisonOperands(
                binaryExpression.Left,
                binaryExpression.Right,
                semanticModel,
                cancellationToken,
                out var value,
                getSymbolVersion) &&
            (!TryGetDecimalZeroComparisonOperator(
                 binaryExpression.Kind(),
                 true,
                 out operatorKind) ||
             !TryCreateDecimalZeroComparisonOperands(
                 binaryExpression.Right,
                 binaryExpression.Left,
                 semanticModel,
                 cancellationToken,
                 out value,
                 getSymbolVersion)))
            return false;

        formula = new SmtBinaryFormula(
            operatorKind,
            value,
            new SmtIntegerConstant(0));
        return true;
    }

    private static bool TryGetDecimalZeroComparisonOperator(
        SyntaxKind syntaxKind,
        bool swappedOperands,
        out SmtBinaryOperator operatorKind)
    {
        operatorKind = default;
        switch (syntaxKind)
        {
            case SyntaxKind.EqualsExpression:
                operatorKind = SmtBinaryOperator.Equal;
                return true;
            case SyntaxKind.NotEqualsExpression:
                operatorKind = SmtBinaryOperator.NotEqual;
                return true;
            case SyntaxKind.LessThanExpression:
                operatorKind = swappedOperands ? SmtBinaryOperator.GreaterThan : SmtBinaryOperator.LessThan;
                return true;
            case SyntaxKind.LessThanOrEqualExpression:
                operatorKind = swappedOperands
                    ? SmtBinaryOperator.GreaterThanOrEqual
                    : SmtBinaryOperator.LessThanOrEqual;
                return true;
            case SyntaxKind.GreaterThanExpression:
                operatorKind = swappedOperands ? SmtBinaryOperator.LessThan : SmtBinaryOperator.GreaterThan;
                return true;
            case SyntaxKind.GreaterThanOrEqualExpression:
                operatorKind = swappedOperands
                    ? SmtBinaryOperator.LessThanOrEqual
                    : SmtBinaryOperator.GreaterThanOrEqual;
                return true;
            default:
                return false;
        }
    }

    private static bool TryCreateDecimalZeroComparisonOperands(
        ExpressionSyntax valueExpression,
        ExpressionSyntax zeroExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula value,
        Func<ISymbol, int>? getSymbolVersion)
    {
        value = null!;
        if (!IsDecimalZeroExpression(zeroExpression, semanticModel, cancellationToken)) return false;

        valueExpression = UnwrapExpression(valueExpression);
        var symbol = semanticModel.GetSymbolInfo(valueExpression, cancellationToken).Symbol;
        if (symbol is not ILocalSymbol and not IParameterSymbol ||
            !IsDecimalType(semanticModel.GetTypeInfo(valueExpression, cancellationToken).Type))
            return false;

        value = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Int);
        return true;
    }

    private static bool IsDecimalZeroExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = UnwrapExpression(expression);
        if (expression is DefaultExpressionSyntax defaultExpression &&
            IsDecimalType(semanticModel.GetTypeInfo(defaultExpression, cancellationToken).Type))
            return true;

        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        if (!IsDecimalType(typeInfo.ConvertedType ?? typeInfo.Type)) return false;

        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        return constant.HasValue &&
               constant.Value is decimal decimalValue &&
               decimalValue == 0m;
    }

    private static bool IsDecimalType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.SpecialType == SpecialType.System_Decimal;
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
            return false;

        formula = new SmtConditionalFormula(
            hasValueFormula,
            nullableValueFormula,
            fallbackFormula,
            fallbackFormula.Kind);
        return true;
    }

    private static bool TryTranslateNullableGetValueOrDefaultValue(
        InvocationExpressionSyntax invocationExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        formula = null;
        if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation
                invocationOperation ||
            invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess ||
            invocationOperation.TargetMethod.Name != "GetValueOrDefault" ||
            invocationOperation.TargetMethod.IsStatic ||
            invocationOperation.TargetMethod.ContainingType is not INamedTypeSymbol containingType ||
            containingType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T ||
            containingType.TypeArguments.Length != 1 ||
            !TryGetValueKind(containingType.TypeArguments[0], out var underlyingKind) ||
            !TryTranslateNullableValueParts(
                memberAccess.Expression,
                semanticModel,
                cancellationToken,
                out var hasValueFormula,
                out var nullableValueFormula,
                getSymbolVersion,
                inlineDepth) ||
            nullableValueFormula is null ||
            nullableValueFormula.Kind != underlyingKind)
            return false;

        SmtFormula? fallbackFormula;
        if (invocationOperation.TargetMethod.Parameters.Length == 0)
        {
            if (!TryCreateDefaultValueFormula(containingType.TypeArguments[0], out fallbackFormula) ||
                fallbackFormula is null ||
                fallbackFormula.Kind != underlyingKind)
                return false;
        }
        else if (invocationOperation.TargetMethod.Parameters.Length == 1)
        {
            if (!SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0,
                    out var fallbackExpression) ||
                !TryTranslateValue(
                    fallbackExpression,
                    semanticModel,
                    cancellationToken,
                    out fallbackFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                fallbackFormula is null ||
                fallbackFormula.Kind != underlyingKind)
                return false;
        }
        else
        {
            return false;
        }

        formula = new SmtConditionalFormula(
            hasValueFormula,
            nullableValueFormula,
            fallbackFormula,
            underlyingKind);
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
            !TryTranslateValue(castExpression.Expression, semanticModel, cancellationToken, out var operand,
                getSymbolVersion, inlineDepth) ||
            operand is not { Kind: SmtValueKind.Reference })
            return false;

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
            semanticModel.GetOperation(castExpression, cancellationToken) is IConversionOperation
            {
                OperatorMethod: not null
            })
            return false;

        if (!TryTranslateValue(castExpression.Expression, semanticModel, cancellationToken, out var candidateOperand,
                getSymbolVersion, inlineDepth) ||
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
            !TryTranslateValue(asExpression.Left, semanticModel, cancellationToken, out var operand, getSymbolVersion,
                inlineDepth) ||
            operand is not { Kind: SmtValueKind.Reference })
            return false;

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
            return false;

        formula = new SmtConditionalFormula(
            CreateNonNullFormula(receiverFormula),
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
            TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula,
                getSymbolVersion, inlineDepth) &&
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
            TryTranslateValue(nullableCastExpression.Expression, semanticModel, cancellationToken,
                out var castUnderlyingValue, getSymbolVersion, inlineDepth) &&
            castUnderlyingValue is not null &&
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
            hasValueFormula = CreateNonNullFormula(receiverFormula);
            return true;
        }

        if (TryGetNullableUnderlyingType(expressionType, out var wrappedUnderlyingType) &&
            !TryGetNullableUnderlyingType(typeInfo.Type, out _) &&
            TryGetValueKind(wrappedUnderlyingType, out var wrappedKind) &&
            TryTranslateValue(expression, semanticModel, cancellationToken, out var wrappedValue, getSymbolVersion,
                inlineDepth) &&
            wrappedValue is not null &&
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
            return TryTranslateNullableValueParts(
                expression,
                semanticModel,
                cancellationToken,
                out hasValueFormula,
                out valueFormula,
                getSymbolVersion,
                inlineDepth);

        if (IsNullLikeNullableComparisonOperand(expression, semanticModel, cancellationToken) &&
            TryCreateDefaultValueFormula(expectedUnderlyingType, out valueFormula) &&
            valueFormula != null)
        {
            hasValueFormula = new SmtBooleanConstant(false);
            return true;
        }

        if (TryGetValueKind(expectedUnderlyingType, out var expectedKind) &&
            TryTranslateValue(expression, semanticModel, cancellationToken, out valueFormula, getSymbolVersion,
                inlineDepth) &&
            valueFormula is not null &&
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
                !SymbolicTypeFacts.TryGetMemberType(memberSymbol, out var memberType) ||
                !SymbolEqualityComparer.Default.Equals(memberType, expectedType))
                return false;

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
            semanticModel.GetTypeInfo(conditionalAccess.Expression, cancellationToken).Type is IArrayTypeSymbol
            {
                Rank: 1
            } arrayType &&
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
            return false;

        return TryGetIntegralSpecialType(sourceType, out var sourceSpecialType) &&
               TryGetIntegralSpecialType(targetType, out var targetSpecialType) &&
               IsSameOrWideningIntegralConversion(sourceSpecialType, targetSpecialType);
    }

    private static bool IsSameOrWideningIntegralConversion(
        SpecialType sourceType,
        SpecialType targetType)
    {
        if (sourceType == targetType) return true;

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
        if (TryTranslateImplicitThisMemberValue(expression, semanticModel, cancellationToken, out formula)) return true;

        if (expression is not MemberAccessExpressionSyntax memberAccess) return false;

        var memberSymbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol;
        if (memberSymbol is not IPropertySymbol and not IFieldSymbol) return false;

        if (memberSymbol.Name == "Length" &&
            TryGetKnownStringLength(memberAccess.Expression, semanticModel, cancellationToken, out var stringLength))
        {
            formula = new SmtIntegerConstant(stringLength);
            return true;
        }

        if (memberSymbol.Name == "Length" &&
            IsStringExpression(memberAccess.Expression, semanticModel, cancellationToken) &&
            TryTranslateStringValue(memberAccess.Expression, semanticModel, cancellationToken, out var stringValue,
                getSymbolVersion, inlineDepth) &&
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

        if (TryTranslateTupleElementValue(memberAccess, memberSymbol, semanticModel, cancellationToken, out formula,
                getSymbolVersion, inlineDepth)) return true;

        if (TryTranslateNullableMemberValue(memberAccess, memberSymbol, semanticModel, cancellationToken, out formula,
                getSymbolVersion, inlineDepth)) return true;

        if (!TryTranslateValue(memberAccess.Expression, semanticModel, cancellationToken, out var receiver,
                getSymbolVersion, inlineDepth) ||
            receiver == null)
            return false;

        if (memberSymbol is IPropertySymbol propertySymbol &&
            TryTranslateSourceBooleanProperty(
                propertySymbol,
                receiver,
                semanticModel,
                cancellationToken,
                out formula,
                inlineDepth + 1))
            return true;

        if (TryTranslateConditionalReceiverMemberValue(memberAccess, memberSymbol, semanticModel, cancellationToken,
                out formula, getSymbolVersion, inlineDepth)) return true;

        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        if (type == null) return false;

        return TryCreateMemberFormula(receiver, memberSymbol.Name, type, out formula);
    }

    private static bool TryTranslateConditionalReceiverMemberValue(
        MemberAccessExpressionSyntax memberAccess,
        ISymbol memberSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        formula = null;
        if (!SymbolicTypeFacts.TryGetMemberType(memberSymbol, out var memberType)) return false;

        var receiverExpression = UnwrapExpression(memberAccess.Expression);
        if (receiverExpression is ConditionalExpressionSyntax conditionalExpression &&
            TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula,
                getSymbolVersion, inlineDepth) &&
            conditionFormula != null &&
            TryCreateReceiverMemberFormula(
                conditionalExpression.WhenTrue,
                memberSymbol.Name,
                memberType,
                semanticModel,
                cancellationToken,
                out var whenTrue,
                getSymbolVersion,
                inlineDepth) &&
            whenTrue != null &&
            TryCreateReceiverMemberFormula(
                conditionalExpression.WhenFalse,
                memberSymbol.Name,
                memberType,
                semanticModel,
                cancellationToken,
                out var whenFalse,
                getSymbolVersion,
                inlineDepth) &&
            whenFalse != null &&
            whenTrue.Kind == whenFalse.Kind)
        {
            formula = new SmtConditionalFormula(conditionFormula, whenTrue, whenFalse, whenTrue.Kind);
            return true;
        }

        if (receiverExpression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            TryTranslateValue(coalesceExpression.Left, semanticModel, cancellationToken, out var leftReceiver,
                getSymbolVersion, inlineDepth) &&
            leftReceiver is { Kind: SmtValueKind.Reference } &&
            TryCreateReceiverMemberFormula(
                coalesceExpression.Left,
                memberSymbol.Name,
                memberType,
                semanticModel,
                cancellationToken,
                out var leftMember,
                getSymbolVersion,
                inlineDepth) &&
            leftMember != null &&
            TryCreateReceiverMemberFormula(
                coalesceExpression.Right,
                memberSymbol.Name,
                memberType,
                semanticModel,
                cancellationToken,
                out var rightMember,
                getSymbolVersion,
                inlineDepth) &&
            rightMember != null &&
            leftMember.Kind == rightMember.Kind)
        {
            formula = new SmtConditionalFormula(
                CreateNonNullFormula(leftReceiver),
                leftMember,
                rightMember,
                leftMember.Kind);
            return true;
        }

        return false;
    }

    private static bool TryCreateReceiverMemberFormula(
        ExpressionSyntax receiverExpression,
        string memberName,
        ITypeSymbol memberType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        formula = null;
        return TryTranslateValue(receiverExpression, semanticModel, cancellationToken, out var receiver,
                   getSymbolVersion, inlineDepth) &&
               receiver is { Kind: SmtValueKind.Reference } &&
               TryCreateMemberFormula(receiver, memberName, memberType, out formula) &&
               formula != null;
    }

    private static bool TryTranslateImplicitThisMemberValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula)
    {
        formula = null;
        if (expression is not IdentifierNameSyntax ||
            semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not IPropertySymbol
                and not IFieldSymbol ||
            semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not { IsStatic: false } memberSymbol ||
            !SymbolicTypeFacts.TryGetMemberType(memberSymbol, out var memberType))
            return false;

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
            return false;

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
        if (propertySyntax == null) return false;

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
                        null,
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
                        null,
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
            return false;

        if (memberSymbol.Name == "HasValue")
        {
            formula = hasValue;
            return true;
        }

        if (value == null) return false;

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
            return false;

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
            formula = new SmtVariable(
                GetVariableName(receiverSymbol.OriginalDefinition, getSymbolVersion) + "." + storageName, kind);
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
                Array.Empty<SmtFormula>(),
                null) &&
            formula is not null &&
            formula.Kind == kind)
            return true;

        if (receiverExpression is ConditionalExpressionSyntax conditionalExpression &&
            TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula,
                getSymbolVersion, inlineDepth) &&
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
        if (SymbolicTypeFacts.IsTupleElementStorageName(tupleField.Name))
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
        if (TryGetTupleElementStorageName(fieldSymbol, out storageName)) return true;

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
                continue;

            var tupleField = element.CorrespondingTupleField ?? element;
            if (SymbolicTypeFacts.IsTupleElementStorageName(tupleField.Name))
            {
                storageName = tupleField.Name;
                return true;
            }
        }

        storageName = string.Empty;
        return false;
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

        if (IsIntegerSmtType(type))
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
        if (SymbolicTypeFacts.IsSupportedTupleCarrierType(type))
        {
            kind = SmtValueKind.Reference;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool TryGetNullableUnderlyingType(ITypeSymbol? type, out ITypeSymbol underlyingType)
    {
        return SymbolicTypeFacts.TryGetNullableUnderlyingType(type, out underlyingType);
    }

    private static string GetVariableName(ISymbol symbol, Func<ISymbol, int>? getSymbolVersion)
    {
        var name = SymbolicFactFactory.GetSmtVariableName(symbol);
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

    private static bool IsIntegerSmtType(ITypeSymbol typeSymbol)
    {
        return IsIntegralOrEnumType(typeSymbol) ||
               IsBigIntegerType(typeSymbol);
    }

    private static bool IsBigIntegerType(ITypeSymbol typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType &&
               namedType.ToDisplayString() == "System.Numerics.BigInteger";
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
            value = Convert.ChangeType(enumValue, enumValue.GetTypeCode(), CultureInfo.InvariantCulture);

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
        return type != null && IsIntegerSmtType(type);
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
        return CSharpSyntaxFacts.UnwrapConditionExpression(expression);
    }

    private delegate bool FormulaTranslator(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth);
}