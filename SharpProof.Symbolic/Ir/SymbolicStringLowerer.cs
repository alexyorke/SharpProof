namespace SharpProof.Symbolic.Ir;
internal static class SymbolicStringLowerer {
    private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<string, string>>
        OrdinalIgnoreCaseConstants = new();
    internal static bool TryLowerStringPredicateInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        var method = operation.TargetMethod;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            invocation.ArgumentList.Arguments.Count is not 1 and not 2 ||
            method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 0, out var argumentExpression) ||
            !TryLowerStringTerm(memberAccess.Expression, context, out var receiver) ||
            LowerStringPredicateArgument(argumentExpression, method.Parameters[0].Type, context) is not { } argument)
            return false;
        var predicate = method.Name switch {
            nameof(string.Contains) => SymbolicStringPredicateKind.Contains,
            nameof(string.StartsWith) => SymbolicStringPredicateKind.StartsWith,
            nameof(string.EndsWith) => SymbolicStringPredicateKind.EndsWith,
            _ => (SymbolicStringPredicateKind?)null
        };
        if (predicate == null) return false;
        if (invocation.ArgumentList.Arguments.Count == 1 &&
            predicate != SymbolicStringPredicateKind.Contains &&
            method.Parameters[0].Type.SpecialType != SpecialType.System_Char)
            return false;
        if (!TryGetOptionalOrdinalStringComparison(operation, 1, context, out var ignoreCase))
            return false;
        if (predicate != SymbolicStringPredicateKind.Contains &&
            method.Parameters[0].Type.SpecialType != SpecialType.System_Char &&
            argument is not SymbolicStringConstantTerm)
            return false;
        condition = CreateStringPredicateCondition(
            predicate.Value, receiver, argument, ignoreCase, invocation, context,
            "ir.known-api.string." + method.Name)!;
        return condition != null;
    }
    internal static bool TryLowerRegexIsMatchInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        return string.Equals(operation.TargetMethod.Name, nameof(Regex.IsMatch), StringComparison.Ordinal) &&
               SymbolicRegexLowerer.TryLowerRegexInvocationPredicate(invocation, context, out condition);
    }
    internal static bool TryLowerStringEqualsInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        var method = operation.TargetMethod;
        ExpressionSyntax leftExpression;
        ExpressionSyntax rightExpression;
        int requiredArgumentCount;
        string provenance;
        if (method.IsStatic) {
            if (invocation.ArgumentList.Arguments.Count is not 2 and not 3 ||
                method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                method.Parameters[1].Type.SpecialType != SpecialType.System_String ||
                !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 0, out leftExpression) ||
                !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 1, out rightExpression))
                return false;
            requiredArgumentCount = 2;
            provenance = "ir.known-api.string.equals";
        }
        else {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocation.ArgumentList.Arguments.Count is not 1 and not 2 ||
                method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 0, out rightExpression))
                return false;
            leftExpression = memberAccess.Expression;
            requiredArgumentCount = 1;
            provenance = "ir.known-api.string.instance-equals";
        }
        if (!TryGetOptionalOrdinalStringComparison(operation, requiredArgumentCount, context, out var ignoreCase))
            return false;
        condition = (ignoreCase
            ? CreateOrdinalIgnoreCaseStringEqualityCondition(leftExpression, rightExpression, invocation, context)
            : CreateStringEqualityCondition(leftExpression, rightExpression, invocation, context, provenance))!;
        return condition != null;
    }
    internal static bool TryLowerStringEqualityCondition(
        BinaryExpressionSyntax binaryExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = CreateStringEqualityCondition(
            binaryExpression.Left, binaryExpression.Right, binaryExpression, context, "ir.string.equality")!;
        if (condition == null)
            return false;
        if (binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)) condition = new SymbolicNotCondition(condition);
        return true;
    }
    private static SymbolicCondition? CreateOrdinalIgnoreCaseStringEqualityCondition(
        ExpressionSyntax leftExpression,
        ExpressionSyntax rightExpression,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context) {
        ExpressionSyntax subjectExpression;
        string constant;
        if (TryGetConstantString(rightExpression, context, out var rightString)) {
            subjectExpression = leftExpression;
            constant = rightString;
        }
        else if (TryGetConstantString(leftExpression, context, out var leftString)) {
            subjectExpression = rightExpression;
            constant = leftString;
        }
        else return null;
        if (LowerStringValueWithOptionalReference(subjectExpression, context) is not { } subject) return null;
        var canonicalConstant = GetOrdinalIgnoreCaseConstant(context.Compilation, constant);
        var matches = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.Equal,
                new SymbolicVariableTerm(
                    "$ordinal-ignore-case:value:" + SymbolicState.CreateProofTermKey(subject.Value),
                    SmtValueKind.String),
                new SymbolicStringConstantTerm(canonicalConstant)),
            sourceNode,
            "ir.string.equals.ordinal-ignore-case");
        return subject.Reference == null
            ? matches
            : new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                SymbolicIrLowerer.CreateReferenceNullCondition(
                    subject.Reference,
                    false,
                    subjectExpression,
                    "ir.string.equals.ordinal-ignore-case.non-null"),
                matches);
    }
    private static SymbolicCondition? CreateStringPredicateCondition(
        SymbolicStringPredicateKind predicate,
        SymbolicTerm value,
        SymbolicTerm argument,
        bool ignoreCase,
        SyntaxNode source,
        SymbolicLoweringContext context,
        string provenance) =>
        ignoreCase
            ? argument is SymbolicStringConstantTerm constant
                ? CreateOrdinalIgnoreCasePredicateCondition(predicate, value, constant.Value, source, context)
                : null
            : SymbolicIrLowerer.CreateFactCondition(
                new SymbolicStringPredicateAtom(predicate, value, argument), source, provenance);
    private static SymbolicCondition CreateOrdinalIgnoreCasePredicateCondition(
        SymbolicStringPredicateKind predicate,
        SymbolicTerm value,
        string argument,
        SyntaxNode source,
        SymbolicLoweringContext context) {
        var canonicalArgument = GetOrdinalIgnoreCaseConstant(context.Compilation, argument);
        var name = "$ordinal-ignore-case:" + predicate + ":" +
                   canonicalArgument.Length.ToString(CultureInfo.InvariantCulture) + ":" + canonicalArgument + ":" +
                   SymbolicState.CreateProofTermKey(value);
        return SymbolicIrLowerer.CreateFactCondition(
            new SymbolicTruthAtom(new SymbolicVariableTerm(name, SmtValueKind.Bool)),
            source,
            "ir.known-api.string." + predicate + ".ordinal-ignore-case");
    }
    private static string GetOrdinalIgnoreCaseConstant(Compilation compilation, string value) =>
        OrdinalIgnoreCaseConstants
            .GetValue(compilation, static _ => new(StringComparer.OrdinalIgnoreCase))
            .GetOrAdd(value, static candidate => candidate);
    internal static bool TryLowerStringSearchComparison(
        BinaryExpressionSyntax comparison,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        var comparisonKind = comparison.Kind();
        if (TryLowerStringSearchComparisonOperand(comparison.Left, comparison.Right, comparisonKind, context, out condition))
            return true;
        return TryLowerStringSearchComparisonOperand(
            comparison.Right,
            comparison.Left,
            ReverseStringComparisonKind(comparisonKind),
            context,
            out condition);
    }
    private static bool TryLowerStringSearchComparisonOperand(
        ExpressionSyntax searchResultExpression,
        ExpressionSyntax constantExpression,
        SyntaxKind comparisonKind,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        var constantValue = context.SemanticModel.GetConstantValue(constantExpression, context.CancellationToken);
        if (!constantValue.HasValue ||
            constantValue.Value == null ||
            !SymbolicLoweringValueFacts.TryGetIntegralConstant(constantValue.Value, out var comparisonConstant) ||
            !SymbolicLoweringValueFacts.TryClassifyThresholdComparison(
                comparisonKind, comparisonConstant, 0, out var found) ||
            LowerStringSearchPredicate(searchResultExpression, context) is not { } predicate)
            return false;
        condition = found ? predicate : new SymbolicNotCondition(predicate);
        return true;
    }
    private static SymbolicCondition? LowerStringSearchPredicate(
        ExpressionSyntax expression,
        SymbolicLoweringContext context) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation {
                    Instance.Syntax: ExpressionSyntax receiverExpression
                } operation ||
            operation.TargetMethod is not {
                IsStatic: false,
                Name: "IndexOf" or "LastIndexOf",
                ReturnType.SpecialType: SpecialType.System_Int32,
                ContainingType.SpecialType: SpecialType.System_String
            } method ||
            operation.Arguments.Length == 0 ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 0, out var searchExpression) ||
            !TryLowerStringTerm(receiverExpression, context, out var receiver) ||
            LowerStringPredicateArgument(searchExpression, method.Parameters[0].Type, context) is not { } search)
            return null;
        var isCharacterDefault = method.Parameters.Length == 1 &&
                                 method.Parameters[0].Type.SpecialType == SpecialType.System_Char;
        var isIgnoreCase = false;
        var hasOrdinalComparison = method.Parameters.Length == 2 &&
                                   SymbolicValueFacts.TryGetInvocationArgumentExpression(
                                        operation, 1, out var comparisonExpression) &&
                                    TryGetOrdinalStringComparison(comparisonExpression, context, out isIgnoreCase);
        return isCharacterDefault || hasOrdinalComparison
            ? CreateStringPredicateCondition(
                SymbolicStringPredicateKind.Contains, receiver, search, isIgnoreCase, invocation, context,
                "ir.string-search.ordinal")
            : null;
    }
    internal static SyntaxKind ReverseStringComparisonKind(SyntaxKind kind) => kind switch {
        SyntaxKind.LessThanExpression => SyntaxKind.GreaterThanExpression,
        SyntaxKind.LessThanOrEqualExpression => SyntaxKind.GreaterThanOrEqualExpression,
        SyntaxKind.GreaterThanExpression => SyntaxKind.LessThanExpression,
        SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.LessThanOrEqualExpression,
        _ => kind
    };
    internal static bool TryLowerPrefixSubstringComparison(
        BinaryExpressionSyntax comparison,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!comparison.IsKind(SyntaxKind.EqualsExpression) &&
            !comparison.IsKind(SyntaxKind.NotEqualsExpression))
            return false;
        var parts = GetPrefixSubstringParts(comparison.Left, comparison.Right, context) ??
                    GetPrefixSubstringParts(comparison.Right, comparison.Left, context);
        if (parts is not { } match ||
            !TryLowerStringTerm(match.Receiver, context, out var receiverTerm))
            return false;
        condition = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.StartsWith, receiverTerm, new SymbolicStringConstantTerm(match.Prefix)),
            comparison,
            "ir.string.substring-prefix");
        if (comparison.IsKind(SyntaxKind.NotEqualsExpression)) condition = new SymbolicNotCondition(condition);
        return true;
    }
    private static (ExpressionSyntax Receiver, string Prefix)? GetPrefixSubstringParts(
        ExpressionSyntax substringExpression,
        ExpressionSyntax prefixExpression,
        SymbolicLoweringContext context) {
        if (!TryGetConstantString(prefixExpression, context, out var prefix)) return null;
        substringExpression = SymbolicLoweringValueFacts.UnwrapExpression(substringExpression);
        if (substringExpression is not InvocationExpressionSyntax invocation ||
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation {
                    Instance.Syntax: ExpressionSyntax receiverExpression,
                    TargetMethod: {
                        IsStatic: false,
                        Name: "Substring",
                        ContainingType.SpecialType: SpecialType.System_String
                    },
                    Arguments.Length: 2
                } operation ||
            !TryGetArgument(operation.Arguments, 0, out var startArgument) ||
            startArgument.Value.Syntax is not ExpressionSyntax startExpression ||
            !TryGetArgument(operation.Arguments, 1, out var lengthArgument) ||
            lengthArgument.Value.Syntax is not ExpressionSyntax lengthExpression)
            return null;
        if (!SymbolicLoweringValueFacts.TryGetIntegralConstant(
                startExpression, context.SemanticModel, context.CancellationToken, out var startValue) ||
            startValue != 0 ||
            !SymbolicLoweringValueFacts.TryGetIntegralConstant(
                lengthExpression, context.SemanticModel, context.CancellationToken, out var lengthValue) ||
            lengthValue != prefix.Length)
            return null;
        return (receiverExpression, prefix);
    }
    private static bool TryGetArgument(
        ImmutableArray<IArgumentOperation> arguments,
        int ordinal,
        out IArgumentOperation argument) {
        argument = arguments.FirstOrDefault(candidate =>
            candidate.Parameter?.Ordinal == ordinal)!;
        return argument != null;
    }
    private static SymbolicCondition? CreateStringEqualityCondition(
        ExpressionSyntax leftExpression,
        ExpressionSyntax rightExpression,
        SyntaxNode sourceNode,
        SymbolicLoweringContext context,
        string provenancePrefix) {
        if (!IsStringExpression(leftExpression, context) ||
            !IsStringExpression(rightExpression, context) ||
            !TryLowerStringTerm(leftExpression, context, out var leftValue) ||
            !TryLowerStringTerm(rightExpression, context, out var rightValue))
            return null;
        var valuesEqual = SymbolicIrLowerer.CreateRelationCondition(
            SymbolicRelationOperator.Equal,
            leftValue,
            rightValue,
            sourceNode,
            provenancePrefix + ".value");
        var leftReference = LowerReferenceTerm(leftExpression, context);
        var rightReference = LowerReferenceTerm(rightExpression, context);
        if (leftReference != null && rightReference != null) {
            var bothNull = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                CreateStringReferenceNullCondition(leftReference, true, leftExpression, provenancePrefix, "left"),
                CreateStringReferenceNullCondition(rightReference, true, rightExpression, provenancePrefix, "right"));
            var bothNonNull = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                CreateStringReferenceNullCondition(leftReference, false, leftExpression, provenancePrefix, "left"),
                CreateStringReferenceNullCondition(rightReference, false, rightExpression, provenancePrefix, "right"));
            return new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                bothNull,
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, bothNonNull, valuesEqual));
        }
        if (leftReference != null && rightValue is SymbolicStringConstantTerm)
            return new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                CreateStringReferenceNullCondition(leftReference, false, leftExpression, provenancePrefix, "left"),
                valuesEqual);
        if (rightReference != null && leftValue is SymbolicStringConstantTerm)
            return new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                CreateStringReferenceNullCondition(rightReference, false, rightExpression, provenancePrefix, "right"),
                valuesEqual);
        return valuesEqual;
    }
    private static SymbolicTerm? LowerReferenceTerm(ExpressionSyntax expression, SymbolicLoweringContext context) =>
        SymbolicIrLowerer.TryLowerReferenceTerm(expression, context, out var term) ? term : null;
    private static SymbolicCondition CreateStringReferenceNullCondition(
        SymbolicTerm reference,
        bool isNull,
        ExpressionSyntax expression,
        string provenancePrefix,
        string side) =>
        SymbolicIrLowerer.CreateReferenceNullCondition(
            reference, isNull, expression, provenancePrefix + "." + side + (isNull ? "-null" : "-not-null"));
    internal static bool TryLowerStringNullOrPredicateInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        var method = operation.TargetMethod;
        if (!method.IsStatic ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            method.Parameters.Length != 1 ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 0, out var argumentExpression) ||
            LowerStringValueWithOptionalReference(argumentExpression, context) is not { } value)
            return false;
        var predicateCondition = method.Name switch {
            nameof(string.IsNullOrEmpty) => SymbolicIrLowerer.CreateIntegerZeroCondition(
                new SymbolicLengthTerm(value.Value),
                invocation,
                "ir.known-api.string.is-null-or-empty.empty"),
            nameof(string.IsNullOrWhiteSpace) => SymbolicIrLowerer.CreateFactCondition(
                new SymbolicStringPredicateAtom(
                    SymbolicStringPredicateKind.RegexMatch,
                    value.Value,
                    new SymbolicStringConstantTerm(@"\A\s*\z")),
                invocation,
                "ir.known-api.string.is-null-or-white-space.regex"),
            _ => null
        };
        if (predicateCondition == null) return false;
        condition = value.Reference == null
            ? predicateCondition
            : new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                SymbolicIrLowerer.CreateReferenceIsNullCondition(value.Reference, argumentExpression),
                predicateCondition);
        return true;
    }
    internal static bool TryLowerStringExpressionTerm(ExpressionSyntax expression, SymbolicLoweringContext context, out SymbolicTerm term) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.AddExpression) &&
            IsStringExpression(binary, context) &&
            LowerStringConcatOperand(binary.Left, context) is { } left &&
            LowerStringConcatOperand(binary.Right, context) is { } right) {
            term = new SymbolicStringConcatTerm(left, right);
            return true;
        }
        if (expression is InvocationExpressionSyntax invocation &&
            TryLowerStringConcatInvocationTerm(invocation, context, out term))
            return true;
        if (TryLowerStringSliceTerm(expression, context, out term)) return true;
        if (expression is InterpolatedStringExpressionSyntax interpolatedString &&
            TryLowerInterpolatedStringTerm(interpolatedString, context, out term))
            return true;
        term = null!;
        return false;
    }
    /// <summary>
    /// Lowers <c>string.Substring</c> to a slice of the receiver. Carrying the result as a
    /// string rather than only its length is what lets the solver answer questions about
    /// its contents; the requested length rides along on the node so the length projection
    /// stays exactly what it was when the result was a bare arithmetic term.
    /// </summary>
    private static bool TryLowerStringSliceTerm(ExpressionSyntax expression, SymbolicLoweringContext context, out SymbolicTerm term) {
        term = null!;
        if (!SymbolicIndexingLowerer.TryGetInvocationOperation(expression, context, out _, out var invocationOperation))
            return false;
        var method = invocationOperation.TargetMethod;
        if (method.IsStatic ||
            method.ContainingType?.SpecialType != SpecialType.System_String ||
            !string.Equals(method.Name, nameof(string.Substring), StringComparison.Ordinal) ||
            method.Parameters.Length is not (1 or 2) ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax sourceExpression ||
            !TryLowerStringTerm(sourceExpression, context, out var source) ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var startExpression) ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(startExpression, context), out var start) ||
            start.Kind != SmtValueKind.Int)
            return false;
        if (method.Parameters.Length == 2) {
            if (!SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 1, out var countExpression) ||
                !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(countExpression, context), out var count) ||
                count.Kind != SmtValueKind.Int)
                return false;
            term = new SymbolicStringSliceTerm(source, start, count);
            return true;
        }
        // Substring(start) runs to the end, so its length is the receiver's length less
        // the offset. The receiver length comes from the same helper the arithmetic
        // projection used, keeping the projected term identical.
        if (!SymbolicIndexingLowerer.TryLowerBuiltInLengthTerm(sourceExpression, context, out var sourceLength))
            return false;
        term = new SymbolicStringSliceTerm(source, start, new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, start));
        return true;
    }
    internal static bool TryCreateStringContentReferenceTerm(SymbolicTerm reference, out SymbolicTerm term) {
        if (reference.Kind != SmtValueKind.Reference) {
            term = null!;
            return false;
        }
        term = new SymbolicStringContentTerm(reference);
        return true;
    }
    internal static bool TryLowerStringTerm(ExpressionSyntax expression, SymbolicLoweringContext context, out SymbolicTerm term) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is CastExpressionSyntax castExpression &&
            context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type?.SpecialType ==
            SpecialType.System_String) {
            if (TryLowerStringTerm(castExpression.Expression, context, out term)) return true;
            if (SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(castExpression.Expression, context), out var castReference) &&
                castReference.Kind == SmtValueKind.Reference &&
                TryCreateStringContentReferenceTerm(castReference, out term))
                return true;
        }
        if (TryGetConstantString(expression, context, out var stringValue)) {
            term = new SymbolicStringConstantTerm(stringValue);
            return true;
        }
        if (expression is MemberAccessExpressionSyntax stringEmptyMemberAccess &&
            TryLowerStringStaticValueMember(
                context.SemanticModel.GetSymbolInfo(stringEmptyMemberAccess, context.CancellationToken).Symbol, out term))
            return true;
        if (TryLowerStringExpressionTerm(expression, context, out term)) return true;
        if (!IsStringExpression(expression, context) ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(expression, context), out var reference)) {
            term = null!;
            return false;
        }
        return TryCreateStringContentReferenceTerm(reference, out term);
    }
    private static SymbolicTerm? LowerStringConcatOperand(
        ExpressionSyntax expression,
        SymbolicLoweringContext context) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.NullLiteralExpression))
            return new SymbolicStringConstantTerm(string.Empty);
        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue && constantValue.Value == null)
            return new SymbolicStringConstantTerm(string.Empty);
        if (!TryLowerStringTerm(expression, context, out var term)) return null;
        return term is SymbolicStringContentTerm stringContent
            ? new SymbolicConditionalTerm(
                SymbolicIrLowerer.CreateReferenceIsNullCondition(stringContent.Reference, expression),
                new SymbolicStringConstantTerm(string.Empty),
                stringContent)
            : term;
    }
    private static SymbolicTerm? LowerStringPredicateArgument(
        ExpressionSyntax expression,
        ITypeSymbol parameterType,
        SymbolicLoweringContext context) {
        if (parameterType.SpecialType == SpecialType.System_String)
            return TryLowerStringTerm(expression, context, out var value) ? value : null;
        if (parameterType.SpecialType == SpecialType.System_Char) {
            var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constantValue.HasValue && constantValue.Value is char charValue)
                return new SymbolicStringConstantTerm(charValue.ToString());
        }
        return null;
    }
    private static (SymbolicTerm Value, SymbolicTerm? Reference)? LowerStringValueWithOptionalReference(
        ExpressionSyntax expression,
        SymbolicLoweringContext context) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (!IsStringExpression(expression, context)) return null;
        if (SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(expression, context), out var direct)) {
            if (direct.Kind == SmtValueKind.Reference)
                return (new SymbolicStringContentTerm(direct), direct);
            if (direct.Kind == SmtValueKind.String) return (direct, null);
        }
        return TryLowerStringTerm(expression, context, out var value) ? (value, null) : null;
    }
    private static bool TryLowerStringConcatInvocationTerm(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation
                operation ||
            !string.Equals(operation.TargetMethod.Name, nameof(string.Concat), StringComparison.Ordinal) ||
            !string.Equals(operation.TargetMethod.ContainingType?.ToDisplayString(), "string", StringComparison.Ordinal))
            return false;
        var parts = ImmutableArray.CreateBuilder<SymbolicTerm>();
        foreach (var argument in invocation.ArgumentList.Arguments) {
            if (LowerStringConcatOperand(argument.Expression, context) is not { } part) return false;
            parts.Add(part);
        }
        return TryCombineStringTerms(parts.ToImmutable(), out term);
    }
    private static bool TryLowerInterpolatedStringTerm(
        InterpolatedStringExpressionSyntax interpolatedString,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        var parts = ImmutableArray.CreateBuilder<SymbolicTerm>();
        foreach (var content in interpolatedString.Contents)
            switch (content) {
                case InterpolatedStringTextSyntax text:
                    parts.Add(new SymbolicStringConstantTerm(text.TextToken.ValueText));
                    break;
                case InterpolationSyntax interpolation:
                    if (interpolation.AlignmentClause != null ||
                        interpolation.FormatClause != null ||
                        LowerStringConcatOperand(interpolation.Expression, context) is not { } part) {
                        term = null!;
                        return false;
                    }
                    parts.Add(part);
                    break;
                default:
                    term = null!;
                    return false;
            }
        return TryCombineStringTerms(parts.ToImmutable(), out term);
    }
    private static bool TryCombineStringTerms(ImmutableArray<SymbolicTerm> parts, out SymbolicTerm term) {
        if (parts.Length == 0) {
            term = new SymbolicStringConstantTerm(string.Empty);
            return true;
        }
        var level = parts;
        while (level.Length > 1) {
            var next = ImmutableArray.CreateBuilder<SymbolicTerm>((level.Length + 1) / 2);
            for (var index = 0; index < level.Length; index += 2)
                next.Add(index + 1 < level.Length
                    ? new SymbolicStringConcatTerm(level[index], level[index + 1])
                    : level[index]);
            level = next.MoveToImmutable();
        }
        term = level[0];
        return true;
    }
    internal static bool TryGetRegexOptions(ExpressionSyntax expression, SymbolicLoweringContext context, out RegexOptions options) {
        if (SymbolicLoweringValueFacts.TryGetIntegralConstant(
                expression, context.SemanticModel, context.CancellationToken, out var rawOptions)) {
            options = (RegexOptions)rawOptions;
            return CanRepresentRegexOptions(options);
        }
        options = RegexOptions.None;
        return false;
    }
    internal static bool CanRepresentRegexOptions(RegexOptions options) =>
        SmtRegexSemantics.CanPreserveOptions(options);
    private static bool TryGetOptionalOrdinalStringComparison(
        IInvocationOperation operation,
        int requiredParameterCount,
        SymbolicLoweringContext context,
        out bool ignoreCase) {
        ignoreCase = false;
        return operation.TargetMethod.Parameters.Length == requiredParameterCount ||
               operation.TargetMethod.Parameters.Length == requiredParameterCount + 1 &&
               SymbolicValueFacts.TryGetInvocationArgumentExpression(
                   operation, requiredParameterCount, out var comparisonExpression) &&
               TryGetOrdinalStringComparison(comparisonExpression, context, out ignoreCase);
    }
    private static bool TryGetOrdinalStringComparison(ExpressionSyntax expression, SymbolicLoweringContext context, out bool ignoreCase) {
        ignoreCase = false;
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        if (type is not INamedTypeSymbol namedType ||
            !string.Equals(SymbolicTypeFacts.GetFullMetadataName(namedType), "System.StringComparison", StringComparison.Ordinal))
            return false;
        if (!SymbolicLoweringValueFacts.TryGetIntegralConstant(
                expression, context.SemanticModel, context.CancellationToken, out var rawComparison))
            return false;
        ignoreCase = rawComparison == (int)StringComparison.OrdinalIgnoreCase;
        return ignoreCase || rawComparison == (int)StringComparison.Ordinal;
    }
    internal static bool TryGetConstantString(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out string value) {
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constant is { HasValue: true, Value: string text }) {
            value = text;
            return true;
        }
        value = string.Empty;
        return false;
    }
    private static bool IsStringExpression(ExpressionSyntax expression, SymbolicLoweringContext context) {
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        return type?.SpecialType == SpecialType.System_String;
    }
    internal static bool TryLowerStringStaticValueMember(ISymbol? memberSymbol, out SymbolicTerm term) {
        if (memberSymbol is IFieldSymbol {
            IsStatic: true,
            Name: nameof(string.Empty),
            Type.SpecialType: SpecialType.System_String,
            ContainingType.SpecialType: SpecialType.System_String
        }) {
            term = new SymbolicStringConstantTerm(string.Empty);
            return true;
        }
        term = null!;
        return false;
    }
}
