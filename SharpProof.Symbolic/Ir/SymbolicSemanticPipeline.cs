using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicSemanticPipeline
{
    internal static SymbolicLoweringResult<SymbolicTerm> LowerTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (!IsStructuralReferenceDepthSupported(expression, context, 0))
            return Unsupported<SymbolicTerm>(expression, "term");

        if (SymbolicIrLowerer.LowerTerm(expression, context) is { } term)
            return Exact(term, expression, "term");

        return Unsupported<SymbolicTerm>(expression, "term");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerCondition(expression, context) is { } condition)
            return Exact(condition, expression, "condition");

        return Unsupported<SymbolicCondition>(expression, "condition");
    }

    internal static SymbolicLoweringResult<SymbolicState> LowerBranchFacts(
        ExpressionSyntax expression,
        bool branchWhenTrue,
        SymbolicLoweringContext context)
    {
        if (TryLowerNotNullWhenBranchCondition(expression, branchWhenTrue, context, out var flowCondition) ||
            TryLowerMemberNotNullWhenBranchCondition(expression, branchWhenTrue, context, out flowCondition))
            return Exact(
                new SymbolicState(pathConditions: new[] { flowCondition }),
                expression,
                "branch-facts");

        var lowered = LowerCondition(expression, context);
        if (!lowered.IsExact || lowered.Value == null)
            return Unsupported<SymbolicState>(expression, "branch-facts");

        var condition = branchWhenTrue
            ? lowered.Value
            : new SymbolicNotCondition(lowered.Value);
        return Exact(new SymbolicState(pathConditions: new[] { condition }), expression, "branch-facts");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerArrayLengthCountAliasCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type is not IArrayTypeSymbol ||
            LowerTerm(expression, context) is not
                { IsExact: true, Value: { Kind: SmtValueKind.Reference } receiver })
            return Unsupported<SymbolicCondition>(expression, "array-length-count-alias");

        return Exact(
            CreateRelationCondition(
                SymbolicRelationOperator.Equal,
                new SymbolicLengthTerm(receiver),
                new SymbolicCountTerm(receiver),
                expression,
                "ir.array.length-count-alias"),
            expression,
            "array-length-count-alias");
    }

    internal static SymbolicLoweringResult<SymbolicState> LowerAsExpressionAssignmentFacts(
        ISymbol targetSymbol,
        ExpressionSyntax valueExpression,
        SymbolicLoweringContext context,
        Func<ISymbol, int>? getTargetSymbolVersion = null)
    {
        valueExpression = CSharpSyntaxFacts.UnwrapParentheses(valueExpression);
        var targetContext = new SymbolicLoweringContext(
            context.SemanticModel,
            context.CancellationToken,
            getTargetSymbolVersion,
            context.SmtAnalysis,
            context.InvocationTermLowerer,
            context.ImplicitThis,
            context.InlineDepth,
            context.SymbolSubstitutions,
            context.InvocationTermTypeResolver);
        if (valueExpression is not BinaryExpressionSyntax asExpression ||
            !asExpression.IsKind(SyntaxKind.AsExpression) ||
            asExpression.Right is not TypeSyntax typeSyntax ||
            !TryCreateReferenceSymbolTerm(targetSymbol, targetContext, out var target))
            return Unsupported<SymbolicState>(valueExpression, "as-expression-assignment");

        var targetType = context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;
        var sourceLowering = LowerTerm(asExpression.Left, context);
        if (!SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(targetType, out var typeKey) ||
            sourceLowering is not { IsExact: true, Value: { Kind: SmtValueKind.Reference } source })
            return Unsupported<SymbolicState>(valueExpression, "as-expression-assignment");

        var targetIsNull = CreateRelationCondition(
            SymbolicRelationOperator.Equal,
            target,
            new SymbolicNullTerm(),
            valueExpression,
            "ir.as.target-null");
        var targetNonNull = CreateRelationCondition(
            SymbolicRelationOperator.NotEqual,
            target,
            new SymbolicNullTerm(),
            valueExpression,
            "ir.as.target-non-null");
        var sourceNonNull = CreateRelationCondition(
            SymbolicRelationOperator.NotEqual,
            source,
            new SymbolicNullTerm(),
            valueExpression,
            "ir.as.source-non-null");
        var runtimeTypeTest = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicTypeTestAtom(source, typeKey),
            valueExpression,
            "ir.as.runtime-type",
            evidenceKey: "ir.as.runtime-type"));
        var conditions = new SymbolicCondition[]
        {
            new SymbolicBinaryCondition(SymbolicConditionOperator.Or, targetIsNull, sourceNonNull),
            new SymbolicBinaryCondition(SymbolicConditionOperator.Or, targetIsNull, runtimeTypeTest),
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicNotCondition(new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    sourceNonNull,
                    runtimeTypeTest)),
                targetNonNull),
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicNotCondition(new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    sourceNonNull,
                    new SymbolicNotCondition(runtimeTypeTest))),
                targetIsNull)
        };
        return Exact(new SymbolicState(pathConditions: conditions), valueExpression, "as-expression-assignment");
    }

    private static bool TryLowerNotNullWhenBranchCondition(
        ExpressionSyntax expression,
        bool branchWhenTrue,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        expression = UnwrapBranchExpression(expression);
        if (expression is PrefixUnaryExpressionSyntax negation &&
            negation.IsKind(SyntaxKind.LogicalNotExpression))
            return TryLowerNotNullWhenBranchCondition(
                negation.Operand,
                !branchWhenTrue,
                context,
                out condition);

        if (expression is BinaryExpressionSyntax binaryExpression &&
            (binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
             binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)))
        {
            if (TryGetBooleanLiteral(binaryExpression.Left, out var leftValue))
                return TryLowerNotNullWhenBranchCondition(
                    binaryExpression.Right,
                    GetComparedBranchValue(leftValue, binaryExpression, branchWhenTrue),
                    context,
                    out condition);

            if (TryGetBooleanLiteral(binaryExpression.Right, out var rightValue))
                return TryLowerNotNullWhenBranchCondition(
                    binaryExpression.Left,
                    GetComparedBranchValue(rightValue, binaryExpression, branchWhenTrue),
                    context,
                    out condition);
        }

        return expression is InvocationExpressionSyntax invocation &&
               TryLowerNotNullWhenInvocationBranchCondition(invocation, branchWhenTrue, context, out condition);
    }

    private static bool TryLowerNotNullWhenInvocationBranchCondition(
        InvocationExpressionSyntax invocation,
        bool branchWhenTrue,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation invocationOperation ||
            invocationOperation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Boolean)
            return false;

        var conditions = new List<SymbolicCondition>();
        foreach (var argument in invocationOperation.Arguments)
        {
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                argument.Parameter is not { IsParams: false } parameter ||
                argument.Syntax is not ArgumentSyntax argumentSyntax ||
                !IsSupportedNotNullWhenArgument(parameter, argumentSyntax) ||
                NullableFlowFacts.GetParameterOutputState(parameter, branchWhenTrue) != NullableFlowFactState.NotNull ||
                !TryLowerNotNullWhenArgumentCondition(argumentSyntax.Expression, context, out var argumentCondition))
                continue;

            conditions.Add(argumentCondition);
        }

        return TryCombineConditions(conditions, out condition);
    }

    private static bool IsSupportedNotNullWhenArgument(IParameterSymbol parameter, ArgumentSyntax argument)
    {
        return parameter.RefKind switch
        {
            RefKind.None => argument.RefKindKeyword.IsKind(SyntaxKind.None),
            RefKind.Out => argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword),
            _ => false
        };
    }

    private static bool TryLowerNotNullWhenArgumentCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!TryGetLocalOrParameterArgumentSymbol(expression, context, out var symbol) ||
            !TryCreateReferenceSymbolTerm(symbol, context, out var referenceTerm))
            return false;

        condition = CreateRelationCondition(
            SymbolicRelationOperator.NotEqual,
            referenceTerm,
            new SymbolicNullTerm(),
            expression,
            "ir.not-null-when.argument.non-null");
        return true;
    }

    private static bool TryGetLocalOrParameterArgumentSymbol(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out ISymbol symbol)
    {
        expression = UnwrapBranchExpression(expression);
        if (expression is DeclarationExpressionSyntax
            {
                Designation: SingleVariableDesignationSyntax singleVariableDesignation
            } &&
            context.SemanticModel.GetDeclaredSymbol(singleVariableDesignation, context.CancellationToken) is
                ILocalSymbol declaredLocal)
        {
            symbol = declaredLocal.OriginalDefinition;
            return true;
        }

        var candidate = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol
            ?.OriginalDefinition;
        if (candidate is ILocalSymbol or IParameterSymbol)
        {
            symbol = candidate;
            return true;
        }

        symbol = null!;
        return false;
    }

    private static bool TryLowerMemberNotNullWhenBranchCondition(
        ExpressionSyntax expression,
        bool branchWhenTrue,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        expression = UnwrapBranchExpression(expression);
        if (expression is PrefixUnaryExpressionSyntax negation &&
            negation.IsKind(SyntaxKind.LogicalNotExpression))
            return TryLowerMemberNotNullWhenBranchCondition(
                negation.Operand,
                !branchWhenTrue,
                context,
                out condition);

        if (expression is BinaryExpressionSyntax binaryExpression &&
            (binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
             binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)))
        {
            if (TryGetBooleanLiteral(binaryExpression.Left, out var leftValue))
                return TryLowerMemberNotNullWhenBranchCondition(
                    binaryExpression.Right,
                    GetComparedBranchValue(leftValue, binaryExpression, branchWhenTrue),
                    context,
                    out condition);

            if (TryGetBooleanLiteral(binaryExpression.Right, out var rightValue))
                return TryLowerMemberNotNullWhenBranchCondition(
                    binaryExpression.Left,
                    GetComparedBranchValue(rightValue, binaryExpression, branchWhenTrue),
                    context,
                    out condition);
        }

        return expression is InvocationExpressionSyntax invocation &&
               TryLowerMemberNotNullWhenInvocationBranchCondition(invocation, branchWhenTrue, context, out condition);
    }

    private static bool TryLowerMemberNotNullWhenInvocationBranchCondition(
        InvocationExpressionSyntax invocation,
        bool branchWhenTrue,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation invocationOperation ||
            invocationOperation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Boolean ||
            invocationOperation.TargetMethod.IsStatic ||
            !IsCurrentInstanceInvocation(invocation))
            return false;

        var conditions = new List<SymbolicCondition>();
        foreach (var memberTarget in NullableFlowFacts.GetMemberNotNullWhenTargets(
                     invocationOperation.TargetMethod,
                     branchWhenTrue))
        {
            if (!NullableFlowFacts.TryResolveInstanceMemberTarget(
                    invocationOperation.TargetMethod.ContainingType,
                    memberTarget,
                    out var member) ||
                !TryLowerImplicitThisMemberNonNullCondition(member, invocation, context, out var memberCondition))
                continue;

            conditions.Add(memberCondition);
        }

        return TryCombineConditions(conditions, out condition);
    }

    private static bool TryLowerImplicitThisMemberNonNullCondition(
        ISymbol member,
        SyntaxNode source,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!NullableFlowFacts.TryGetMemberType(member, out var memberType) ||
            !SymbolicFactFactory.TryGetValueKind(
                memberType,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsReferenceType,
                out var memberKind) ||
            memberKind != SmtValueKind.Reference)
            return false;

        condition = CreateRelationCondition(
            SymbolicRelationOperator.NotEqual,
            new SymbolicMemberTerm(context.ImplicitThis, member.Name, memberKind),
            new SymbolicNullTerm(),
            source,
            "ir.member-not-null-when.target.non-null");
        return true;
    }

    private static bool TryCreateReferenceSymbolTerm(
        ISymbol symbol,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        if (SymbolicFactFactory.GetTrackedSymbolType(symbol) is not { } type ||
            !SymbolicFactFactory.TryGetValueKind(
                type,
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsReferenceType,
                out var kind) ||
            kind != SmtValueKind.Reference)
        {
            term = null!;
            return false;
        }

        term = new SymbolicVariableTerm(context.GetVariableName(symbol), kind);
        return true;
    }

    private static bool IsStructuralReferenceDepthSupported(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        int depth)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type?.IsReferenceType != true) return true;

        var limit = SymbolicAnalysisLimitContext.Limits.MaxStructuralNullStateDepth;
        if (depth > limit)
        {
            SymbolicAnalysisLimitContext.Record(
                SymbolicAnalysisLimitKind.StructuralNullStateDepth,
                limit,
                depth,
                expression,
                "semantic_pipeline.structural_reference_depth");
            return false;
        }

        switch (expression)
        {
            case ConditionalExpressionSyntax conditional:
                return IsStructuralReferenceDepthSupported(
                           conditional.WhenTrue,
                           context,
                           depth + 1) &&
                       IsStructuralReferenceDepthSupported(
                           conditional.WhenFalse,
                           context,
                           depth + 1);
            case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                return IsStructuralReferenceDepthSupported(
                           coalesce.Left,
                           context,
                           depth + 1) &&
                       IsStructuralReferenceDepthSupported(
                           coalesce.Right,
                           context,
                           depth + 1);
            case ConditionalAccessExpressionSyntax conditionalAccess:
                if (depth >= limit)
                {
                    SymbolicAnalysisLimitContext.Record(
                        SymbolicAnalysisLimitKind.StructuralNullStateDepth,
                        limit,
                        depth + 1,
                        conditionalAccess,
                        "semantic_pipeline.conditional_access_reference_depth");
                    return false;
                }

                return IsStructuralReferenceDepthSupported(
                    conditionalAccess.Expression,
                    context,
                    depth + 1);
            default:
                return true;
        }
    }

    private static bool TryCombineConditions(
        IReadOnlyList<SymbolicCondition> conditions,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (conditions.Count == 0) return false;

        condition = conditions[0];
        for (var index = 1; index < conditions.Count; index++)
            condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, condition, conditions[index]);

        return true;
    }

    private static bool TryGetBooleanLiteral(ExpressionSyntax expression, out bool value)
    {
        expression = UnwrapBranchExpression(expression);
        if (expression.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            value = true;
            return true;
        }

        if (expression.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static bool GetComparedBranchValue(
        bool literalValue,
        BinaryExpressionSyntax comparison,
        bool branchWhenTrue)
    {
        return comparison.IsKind(SyntaxKind.EqualsExpression)
            ? literalValue == branchWhenTrue
            : literalValue != branchWhenTrue;
    }

    private static bool IsCurrentInstanceInvocation(InvocationExpressionSyntax invocation)
    {
        var invokedExpression = UnwrapBranchExpression(invocation.Expression);
        return invokedExpression is IdentifierNameSyntax ||
               invokedExpression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax };
    }

    private static ExpressionSyntax UnwrapBranchExpression(ExpressionSyntax expression)
    {
        while (true)
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesizedExpression:
                    expression = parenthesizedExpression.Expression;
                    continue;
                case CastExpressionSyntax castExpression:
                    expression = castExpression.Expression;
                    continue;
                case CheckedExpressionSyntax checkedExpression
                    when checkedExpression.IsKind(SyntaxKind.CheckedExpression) ||
                         checkedExpression.IsKind(SyntaxKind.UncheckedExpression):
                    expression = checkedExpression.Expression;
                    continue;
                default:
                    return expression;
            }
    }

    private static SymbolicCondition CreateRelationCondition(
        SymbolicRelationOperator relationOperator,
        SymbolicTerm left,
        SymbolicTerm right,
        SyntaxNode source,
        string provenance)
    {
        return new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(relationOperator, left, right),
            source,
            provenance,
            evidenceKey: provenance));
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerPattern(
        ExpressionSyntax valueExpression,
        PatternSyntax pattern,
        SymbolicLoweringContext context)
    {
        var value = LowerTerm(valueExpression, context);
        if (value.IsExact &&
            value.Value != null &&
            SymbolicIrLowerer.LowerPatternCondition(
                value.Value,
                context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken).ConvertedType ??
                context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken).Type,
                pattern,
                pattern,
                context) is { } condition)
            return Exact(condition, pattern, "pattern");

        return Unsupported<SymbolicCondition>(pattern, "pattern");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerPatternCondition(
                value,
                valueType,
                pattern,
                source,
                context) is { } condition)
            return Exact(condition, source, "pattern");

        return Unsupported<SymbolicCondition>(source, "pattern");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerPatternCondition(
        SymbolicTerm value,
        PatternSyntax pattern,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerPatternCondition(
                value,
                pattern,
                source,
                context) is { } condition)
            return Exact(condition, source, "pattern");

        return Unsupported<SymbolicCondition>(source, "pattern");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerMemberOrIndexAccess(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (expression is not MemberAccessExpressionSyntax and not ElementAccessExpressionSyntax)
            return Unsupported<SymbolicTerm>(expression, "member-or-index");

        if (SymbolicIrLowerer.LowerTerm(expression, context) is { } term)
            return Exact(term, expression, "member-or-index");

        return Unsupported<SymbolicTerm>(expression, "member-or-index");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerConversion(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (expression is not CastExpressionSyntax and not CheckedExpressionSyntax)
            return Unsupported<SymbolicTerm>(expression, "conversion");

        if (SymbolicIrLowerer.LowerTerm(expression, context) is { } term)
            return Exact(term, expression, "conversion");

        return Unsupported<SymbolicTerm>(expression, "conversion");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerReferenceTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (!IsStructuralReferenceDepthSupported(expression, context, 0))
            return Unsupported<SymbolicTerm>(expression, "reference-term");

        if (SymbolicIrLowerer.LowerReferenceTerm(expression, context) is { } term)
            return Exact(term, expression, "reference-term");

        return Unsupported<SymbolicTerm>(expression, "reference-term");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerStringTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerStringTerm(expression, context) is { } term)
            return Exact(term, expression, "string-term");

        return Unsupported<SymbolicTerm>(expression, "string-term");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerBooleanValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerBooleanValueTerm(expression, context) is { } term)
            return Exact(term, expression, "boolean-term");

        return Unsupported<SymbolicTerm>(expression, "boolean-term");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerNotNullIfNotNullAssignedResultTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerNotNullIfNotNullAssignedResultTerm(expression, context) is { } term)
            return Exact(term, expression, "not-null-if-not-null-assigned-result");

        return Unsupported<SymbolicTerm>(expression, "not-null-if-not-null-assigned-result");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerBuiltInLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerBuiltInLengthTerm(expression, context) is { } term)
            return Exact(term, expression, "built-in-length");

        return Unsupported<SymbolicTerm>(expression, "built-in-length");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerLengthProjectionTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var value = LowerTerm(expression, context);
        if (value is { IsExact: true, Value: { } valueTerm } &&
            valueTerm.Kind is SmtValueKind.String or SmtValueKind.Reference)
            return Exact<SymbolicTerm>(new SymbolicLengthTerm(valueTerm), expression, "length-projection");

        return Unsupported<SymbolicTerm>(expression, "length-projection");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> ProjectBuiltInLengthTerm(
        ITypeSymbol? receiverType,
        SymbolicTerm receiver,
        SyntaxNode source)
    {
        if (SymbolicIrLowerer.ProjectBuiltInLengthTerm(receiverType, receiver) is { } term)
            return Exact(term, source, "built-in-length-projection");

        return Unsupported<SymbolicTerm>(source, "built-in-length-projection");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> ProjectStringContentTerm(
        SymbolicTerm receiver,
        SyntaxNode source)
    {
        if (SymbolicIrLowerer.ProjectStringContentTerm(receiver) is { } term)
            return Exact(term, source, "string-content-projection");

        return Unsupported<SymbolicTerm>(source, "string-content-projection");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerArrayDimensionLengthTerm(
        ExpressionSyntax expression,
        int dimension,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerArrayDimensionLengthTerm(expression, dimension, context) is { } term)
            return Exact(term, expression, "array-dimension-length");

        return Unsupported<SymbolicTerm>(expression, "array-dimension-length");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerNullableHasValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerNullableHasValueTerm(expression, context) is { } term)
            return Exact(term, expression, "nullable-has-value");

        return Unsupported<SymbolicTerm>(expression, "nullable-has-value");
    }

    internal static SymbolicLoweringResult<SymbolicTerm> LowerNullableValueTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerNullableValueTerm(expression, context) is { } term)
            return Exact(term, expression, "nullable-value");

        return Unsupported<SymbolicTerm>(expression, "nullable-value");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerStringNonNullCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerStringNonNullCondition(expression, context) is { } condition)
            return Exact(condition, expression, "string-non-null");

        return Unsupported<SymbolicCondition>(expression, "string-non-null");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerBuiltInElementAccessInRangeCondition(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicLoweringContext context)
    {
        var receiverType = context.SemanticModel.GetTypeInfo(
                               elementAccess.Expression,
                               context.CancellationToken).ConvertedType ??
                           context.SemanticModel.GetTypeInfo(
                               elementAccess.Expression,
                               context.CancellationToken).Type;
        if (receiverType is IArrayTypeSymbol { Rank: > 1 } &&
            SymbolicIrLowerer.LowerArrayElementBoundsCondition(
                elementAccess.Expression,
                elementAccess.ArgumentList.Arguments.Select(static argument => argument.Expression).ToArray(),
                elementAccess,
                "ir.element-access.multidimensional-bounds.in-range",
                context) is { } multidimensionalCondition)
            return Exact(multidimensionalCondition, elementAccess, "element-access-in-range");

        if (elementAccess.ArgumentList.Arguments.Count == 1)
            return LowerBuiltInElementAccessInRangeCondition(
                elementAccess.Expression,
                elementAccess.ArgumentList.Arguments[0].Expression,
                elementAccess,
                context);

        return Unsupported<SymbolicCondition>(elementAccess, "element-access-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerBuiltInElementAccessOutOfRangeCondition(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicLoweringContext context)
    {
        var inRangeLowering = LowerBuiltInElementAccessInRangeCondition(elementAccess, context);
        if (inRangeLowering is not { IsExact: true, Value: { } inRangeCondition })
            return Unsupported<SymbolicCondition>(elementAccess, "element-access-out-of-range");

        var condition = (SymbolicCondition)new SymbolicNotCondition(inRangeCondition);
        foreach (var candidate in elementAccess.ArgumentList.Arguments
                     .SelectMany(static argument => argument.Expression.DescendantNodesAndSelf()))
        {
            if (!TryGetIndexConstructionValueExpression(candidate, context, out var valueExpression) ||
                LowerTerm(valueExpression, context) is not
                    { IsExact: true, Value: { Kind: SmtValueKind.Int } value })
                continue;

            var normalCompletion = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.GreaterThanOrEqual,
                    value,
                    new SymbolicIntegerConstantTerm(0)),
                candidate,
                "ir.runtime-hazard.index.constructor-normal-completion"));
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                normalCompletion,
                condition);
        }

        return Exact(condition, elementAccess, "element-access-out-of-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerIndexConstructionArgumentOutOfRangeCondition(
        ExpressionSyntax indexConstructionExpression,
        SymbolicLoweringContext context)
    {
        if (!TryGetIndexConstructionValueExpression(
                indexConstructionExpression,
                context,
                out var valueExpression) ||
            LowerTerm(valueExpression, context) is not
                { IsExact: true, Value: { Kind: SmtValueKind.Int } value })
            return Unsupported<SymbolicCondition>(
                indexConstructionExpression,
                "index-construction-argument-out-of-range");

        SymbolicCondition condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThan,
                value,
                new SymbolicIntegerConstantTerm(0)),
            indexConstructionExpression,
            "ir.runtime-hazard.index.constructor-argument-out-of-range"));
        return Exact(condition, indexConstructionExpression, "index-construction-argument-out-of-range");
    }

    private static bool TryGetIndexConstructionValueExpression(
        SyntaxNode candidate,
        SymbolicLoweringContext context,
        out ExpressionSyntax valueExpression)
    {
        if (candidate is PrefixUnaryExpressionSyntax prefix &&
            (prefix.IsKind(SyntaxKind.IndexExpression) ||
             prefix.OperatorToken.IsKind(SyntaxKind.CaretToken)))
        {
            valueExpression = prefix.Operand;
            return true;
        }

        if (candidate is InvocationExpressionSyntax invocation &&
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is IInvocationOperation
            {
                TargetMethod.Name: "FromStart" or "FromEnd",
                TargetMethod.ContainingType: { } containingType
            } invocationOperation &&
            SymbolicTypeFacts.IsSystemIndexType(containingType) &&
            SymbolicValueFacts.TryGetInvocationArgumentExpression(
                invocationOperation,
                0,
                out valueExpression))
            return true;

        if (candidate is ObjectCreationExpressionSyntax objectCreation &&
            context.SemanticModel.GetOperation(objectCreation, context.CancellationToken) is IObjectCreationOperation
            {
                Constructor.ContainingType: { } objectType
            } objectCreationOperation &&
            SymbolicTypeFacts.IsSystemIndexType(objectType))
        {
            var argument = objectCreationOperation.Arguments
                .FirstOrDefault(static item => item.Parameter?.Ordinal == 0);
            if (argument?.Syntax is ArgumentSyntax argumentSyntax)
            {
                valueExpression = argumentSyntax.Expression;
                return true;
            }

            if (argument?.Value.Syntax is ExpressionSyntax argumentExpression)
            {
                valueExpression = argumentExpression;
                return true;
            }
        }

        valueExpression = null!;
        return false;
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerBuiltInElementAccessInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax indexExpression,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerBuiltInElementAccessInRangeCondition(
                receiverExpression,
                indexExpression,
                source,
                "ir.element-access.bounds.in-range",
                context) is { } condition)
            return Exact(condition, source, "element-access-in-range");

        return Unsupported<SymbolicCondition>(source, "element-access-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerArrayElementBoundsCondition(
        ExpressionSyntax arrayExpression,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        if (SymbolicIrLowerer.LowerArrayElementBoundsCondition(
                arrayExpression,
                indexExpressions,
                source,
                "ir.array-element.bounds.in-range",
                context) is { } condition)
            return Exact(condition, source, "array-element-in-range");

        return Unsupported<SymbolicCondition>(source, "array-element-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerSubsequenceInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax startExpression,
        ExpressionSyntax? lengthExpression,
        SyntaxNode source,
        SymbolicLoweringContext context,
        bool oneArgumentUpperBoundIsInclusive = true)
    {
        if (SymbolicIrLowerer.LowerSubsequenceInRangeCondition(
                receiverExpression,
                startExpression,
                lengthExpression,
                source,
                "ir.subsequence.in-range",
                context,
                oneArgumentUpperBoundIsInclusive) is { } condition)
            return Exact(condition, source, "subsequence-in-range");

        return Unsupported<SymbolicCondition>(source, "subsequence-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerIntegerInRangeCondition(
        ExpressionSyntax expression,
        long minValue,
        long maxValue,
        SymbolicLoweringContext context)
    {
        var lowering = LowerTerm(expression, context);
        if (lowering is { IsExact: true, Value: { Kind: SmtValueKind.Int } value })
            return Exact<SymbolicCondition>(
                SymbolicIrLowerer.CreateIntegerInRangeCondition(
                    value,
                    minValue,
                    maxValue,
                    expression,
                    "ir.integer.in-range"),
                expression,
                "integer-in-range");

        return Unsupported<SymbolicCondition>(expression, "integer-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerIntegerBinaryInRangeCondition(
        ExpressionSyntax leftExpression,
        ExpressionSyntax rightExpression,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        var left = LowerTerm(leftExpression, context);
        var right = LowerTerm(rightExpression, context);
        if (SymbolicIrLowerer.GetBinaryTermOperator(smtOperator) is { } binaryOperator &&
            binaryOperator is not (SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder) &&
            left is { IsExact: true, Value: { Kind: SmtValueKind.Int } leftTerm } &&
            right is { IsExact: true, Value: { Kind: SmtValueKind.Int } rightTerm })
            return Exact<SymbolicCondition>(
                SymbolicIrLowerer.CreateIntegerInRangeCondition(
                    new SymbolicBinaryTerm(binaryOperator, leftTerm, rightTerm),
                    minValue,
                    maxValue,
                    source,
                    "ir.integer.binary.in-range"),
                source,
                "integer-binary-in-range");

        return Unsupported<SymbolicCondition>(source, "integer-binary-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerNegatedIntegerInRangeCondition(
        ExpressionSyntax expression,
        long minValue,
        long maxValue,
        SymbolicLoweringContext context)
    {
        var operand = LowerTerm(expression, context);
        if (operand is { IsExact: true, Value: { Kind: SmtValueKind.Int } operandTerm })
            return Exact(
                SymbolicIrLowerer.CreateIntegerInRangeCondition(
                    new SymbolicBinaryTerm(
                        SymbolicBinaryTermOperator.Subtract,
                        new SymbolicIntegerConstantTerm(0),
                        operandTerm),
                    minValue,
                    maxValue,
                    expression,
                    "ir.integer.unary.in-range"),
                expression,
                "integer-unary-in-range");

        return Unsupported<SymbolicCondition>(expression, "integer-unary-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerIntegerUpdateInRangeCondition(
        ExpressionSyntax expression,
        SmtIntegerBinaryOperator smtOperator,
        long minValue,
        long maxValue,
        SymbolicLoweringContext context)
    {
        var operand = LowerTerm(expression, context);
        if (SymbolicIrLowerer.GetBinaryTermOperator(smtOperator) is { } binaryOperator &&
            binaryOperator is SymbolicBinaryTermOperator.Add or SymbolicBinaryTermOperator.Subtract &&
            operand is { IsExact: true, Value: { Kind: SmtValueKind.Int } operandTerm })
            return Exact(
                SymbolicIrLowerer.CreateIntegerInRangeCondition(
                    new SymbolicBinaryTerm(
                        binaryOperator,
                        operandTerm,
                        new SymbolicIntegerConstantTerm(1)),
                    minValue,
                    maxValue,
                    expression,
                    "ir.integer.update.in-range"),
                expression,
                "integer-update-in-range");

        return Unsupported<SymbolicCondition>(expression, "integer-update-in-range");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerNegativeIntegerCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var operand = LowerTerm(expression, context);
        if (operand is { IsExact: true, Value: { Kind: SmtValueKind.Int } operandTerm })
            return Exact<SymbolicCondition>(
                new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicRelationAtom(
                        SymbolicRelationOperator.LessThan,
                        operandTerm,
                        new SymbolicIntegerConstantTerm(0)),
                    expression,
                    "ir.integer.negative")),
                expression,
                "integer-negative");

        return Unsupported<SymbolicCondition>(expression, "integer-negative");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerNumericZeroCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (expression is PrefixUnaryExpressionSyntax unary &&
            unary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.UnaryMinusExpression) &&
            context.SemanticModel.GetOperation(unary, context.CancellationToken) is
                Microsoft.CodeAnalysis.Operations.IUnaryOperation { OperatorMethod: null })
            return LowerNumericZeroCondition(unary.Operand, context);

        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constant.HasValue)
        {
            if (SymbolicValueFacts.IsIntegralOrDecimalZero(constant.Value))
                return Exact<SymbolicCondition>(new SymbolicConstantCondition(true), expression, "numeric-zero");

            if (constant.Value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal)
                return Exact<SymbolicCondition>(new SymbolicConstantCondition(false), expression, "numeric-zero");
        }

        var lowered = LowerTerm(expression, context);
        SymbolicTerm? value = lowered is { IsExact: true, Value: { Kind: SmtValueKind.Int } integer }
            ? integer
            : null;
        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        if (value == null &&
            symbol is ILocalSymbol or IParameterSymbol &&
            context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type?.SpecialType ==
            SpecialType.System_Decimal)
            value = context.TryGetSubstitution(symbol, out var substituted)
                ? substituted
                : new SymbolicVariableTerm(context.GetVariableName(symbol), SmtValueKind.Int);

        if (value is { Kind: SmtValueKind.Int })
            return Exact(
                SymbolicIrLowerer.CreateIntegerZeroCondition(value, expression, "ir.numeric-zero"),
                expression,
                "numeric-zero");

        return Unsupported<SymbolicCondition>(expression, "numeric-zero");
    }

    internal static SymbolicLoweringResult<SymbolicCondition> LowerNullableHasValueCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var hasValue = LowerNullableHasValueTerm(expression, context);
        if (hasValue is { IsExact: true, Value: { } term })
            return Exact<SymbolicCondition>(
                new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicTruthAtom(term),
                    expression,
                    "ir.nullable.has-value")),
                expression,
                "nullable-has-value-condition");

        return Unsupported<SymbolicCondition>(expression, "nullable-has-value-condition");
    }

    internal static SymbolicLoweringResult<SymbolicFact> LowerRuntimeHazardTrigger(
        SymbolicExceptionPreconditionKind kind,
        SymbolicTerm? subject,
        SymbolicCondition trigger,
        SyntaxNode source,
        string detail)
    {
        if (trigger == null) throw new ArgumentNullException(nameof(trigger));

        var provenance = "ir.runtime-hazard." + detail;
        return Exact(
            SymbolicFact.Exact(
                new SymbolicExceptionPreconditionAtom(kind, subject, trigger),
                source,
                provenance,
                evidenceKey: provenance),
            source,
            "runtime-hazard");
    }

    private static SymbolicLoweringResult<T> Exact<T>(T value, SyntaxNode source, string stage)
        where T : class
    {
        return SymbolicLoweringResult<T>.Exact(value, CreateProvenance(source, stage, "exact"));
    }

    private static SymbolicLoweringResult<T> Unsupported<T>(SyntaxNode source, string stage)
        where T : class
    {
        return SymbolicLoweringResult<T>.Unsupported(CreateProvenance(source, stage, "unsupported"));
    }

    private static SymbolicLoweringProvenance CreateProvenance(
        SyntaxNode source,
        string stage,
        string detail)
    {
        return new SymbolicLoweringProvenance("roslyn-to-ir." + stage, source.Span, detail);
    }
}
