namespace SharpProof.Symbolic;

internal enum NullableFlowFactState {
    Unknown,
    MaybeNull,
    NotNull
}
internal static class NullableFlowFacts {
    private const string AllowNullAttributeName = "System.Diagnostics.CodeAnalysis.AllowNullAttribute";
    private const string DisallowNullAttributeName = "System.Diagnostics.CodeAnalysis.DisallowNullAttribute";
    private const string DoesNotReturnAttributeName =
        "System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute";
    private const string DoesNotReturnIfAttributeName =
        "System.Diagnostics.CodeAnalysis.DoesNotReturnIfAttribute";
    private const string MaybeNullAttributeName = "System.Diagnostics.CodeAnalysis.MaybeNullAttribute";
    private const string MaybeNullWhenAttributeName =
        "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute";
    private const string MemberNotNullAttributeName =
        "System.Diagnostics.CodeAnalysis.MemberNotNullAttribute";
    private const string MemberNotNullWhenAttributeName =
        "System.Diagnostics.CodeAnalysis.MemberNotNullWhenAttribute";
    private const string NotNullAttributeName = "System.Diagnostics.CodeAnalysis.NotNullAttribute";
    private const string NotNullIfNotNullAttributeName =
        "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute";
    private const string NotNullWhenAttributeName =
        "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute";

    internal static NullableFlowFactState GetExpressionState(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        expression = CSharpSyntaxFacts.UnwrapParentheses(expression);
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type == null || !SymbolicTypeFacts.IsReferenceLikeType(type))
            return NullableFlowFactState.Unknown;

        var flowState = typeInfo.Nullability.FlowState != NullableFlowState.None
            ? typeInfo.Nullability.FlowState
            : typeInfo.ConvertedNullability.FlowState;
        if (flowState == NullableFlowState.NotNull) return NullableFlowFactState.NotNull;

        if (flowState == NullableFlowState.MaybeNull) return NullableFlowFactState.MaybeNull;

        return GetStructuralExpressionState(expression, semanticModel, cancellationToken, exactContract: false);
    }
    internal static NullableFlowFactState GetExpressionStateAtPosition(
        ExpressionSyntax expression,
        int position,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        cancellationToken.ThrowIfCancellationRequested();
        try {
            var typeInfo = semanticModel.GetSpeculativeTypeInfo(
                position,
                CSharpSyntaxFacts.UnwrapParentheses(expression).WithoutTrivia(),
                SpeculativeBindingOption.BindAsExpression);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type == null || !SymbolicTypeFacts.IsReferenceLikeType(type))
                return NullableFlowFactState.Unknown;

            var flowState = typeInfo.Nullability.FlowState != NullableFlowState.None
                ? typeInfo.Nullability.FlowState
                : typeInfo.ConvertedNullability.FlowState;
            return flowState switch {
                NullableFlowState.NotNull => NullableFlowFactState.NotNull,
                NullableFlowState.MaybeNull => NullableFlowFactState.MaybeNull,
                _ => NullableFlowFactState.Unknown
            };
        }
        catch (ArgumentException) {
            return NullableFlowFactState.Unknown;
        }
    }
    internal static bool IsDefinitelyNotNullReferenceValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) => GetExpressionState(expression, semanticModel, cancellationToken) ==
               NullableFlowFactState.NotNull;
    internal static bool IsDefinitelyNullReferenceValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return constant is { HasValue: true, Value: null } &&
               (typeInfo.ConvertedType ?? typeInfo.Type)?.IsReferenceType == true;
    }
    internal static bool TryEvaluateNullTest(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool value) {
        expression = CSharpSyntaxFacts.UnwrapParentheses(expression);
        if (expression is PrefixUnaryExpressionSyntax negation &&
            negation.IsKind(SyntaxKind.LogicalNotExpression) &&
            TryEvaluateNullTest(negation.Operand, semanticModel, cancellationToken, out var operandValue)) {
            value = !operandValue;
            return true;
        }
        if (expression is BinaryExpressionSyntax binary &&
            (binary.IsKind(SyntaxKind.EqualsExpression) ||
             binary.IsKind(SyntaxKind.NotEqualsExpression)) &&
            semanticModel.GetOperation(binary, cancellationToken) is IBinaryOperation { OperatorMethod: null }) {
            var target = CSharpSyntaxFacts.IsNullLiteral(binary.Left)
                ? binary.Right
                : CSharpSyntaxFacts.IsNullLiteral(binary.Right)
                    ? binary.Left
                    : null;
            if (target != null &&
                GetExactExpressionState(target, semanticModel, cancellationToken) == NullableFlowFactState.NotNull) {
                value = binary.IsKind(SyntaxKind.NotEqualsExpression);
                return true;
            }
        }
        if (expression is IsPatternExpressionSyntax isPattern &&
            CSharpSyntaxFacts.TryGetNullPatternPolarity(isPattern.Pattern, out var matchesNonNull) &&
            GetExactExpressionState(isPattern.Expression, semanticModel, cancellationToken) ==
            NullableFlowFactState.NotNull) {
            value = matchesNonNull;
            return true;
        }
        value = false;
        return false;
    }
    internal static bool TryGetArgumentTargetSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol symbol) {
        expression = CSharpSyntaxFacts.UnwrapParentheses(expression);
        if (expression is DeclarationExpressionSyntax {
            Designation: SingleVariableDesignationSyntax designation
        } &&
            semanticModel.GetDeclaredSymbol(designation, cancellationToken) is ILocalSymbol declaredLocal) {
            symbol = declaredLocal.OriginalDefinition;
            return true;
        }
        return SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(expression, semanticModel, cancellationToken, out symbol);
    }
    internal static NullableFlowFactState GetParameterInputState(IParameterSymbol parameter) {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));

        if (!SymbolicTypeFacts.IsReferenceLikeType(parameter.Type)) return NullableFlowFactState.Unknown;

        var allowsNull = HasParameterAttribute(parameter, AllowNullAttributeName);
        var disallowsNull = HasParameterAttribute(parameter, DisallowNullAttributeName);
        if (allowsNull && disallowsNull) return NullableFlowFactState.Unknown;

        if (allowsNull) return NullableFlowFactState.MaybeNull;

        if (disallowsNull) return NullableFlowFactState.NotNull;

        return FromAnnotation(parameter.NullableAnnotation);
    }
    internal static bool HasExplicitNotNullInputContract(IParameterSymbol parameter) {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));

        return HasParameterAttribute(parameter, DisallowNullAttributeName) &&
               !HasParameterAttribute(parameter, AllowNullAttributeName);
    }
    internal static NullableFlowFactState GetParameterOutputState(IParameterSymbol parameter, bool? methodReturnValue = null) {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));

        if (!SymbolicTypeFacts.IsReferenceLikeType(parameter.Type)) return NullableFlowFactState.Unknown;

        if (methodReturnValue.HasValue) {
            if (TryGetMaybeNullWhenValue(parameter, out var maybeNullWhen) &&
                maybeNullWhen == methodReturnValue.Value)
                return NullableFlowFactState.MaybeNull;

            if (TryGetNotNullWhenValue(parameter, out var notNullWhen) &&
                notNullWhen == methodReturnValue.Value)
                return NullableFlowFactState.NotNull;
        }
        if (HasParameterAttribute(parameter, MaybeNullAttributeName)) return NullableFlowFactState.MaybeNull;

        if (HasParameterAttribute(parameter, NotNullAttributeName)) return NullableFlowFactState.NotNull;

        return parameter.RefKind is RefKind.Ref or RefKind.Out
            ? FromAnnotation(parameter.NullableAnnotation)
            : NullableFlowFactState.Unknown;
    }
    internal static bool HasNotNullPostcondition(IParameterSymbol parameter) =>
        HasParameterAttribute(parameter, NotNullAttributeName);

    internal static bool HasInferredNotNullNormalCompletionPostcondition(IParameterSymbol parameter, CancellationToken cancellationToken) {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));

        if (parameter.RefKind != RefKind.None ||
            parameter.ContainingSymbol is not IMethodSymbol method)
            return false;

        foreach (var syntaxReference in method.DeclaringSyntaxReferences) {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = syntaxReference.GetSyntax(cancellationToken);
            var body = declaration switch {
                MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body,
                LocalFunctionStatementSyntax localFunction => localFunction.Body,
                _ => null
            };
            if (body == null) continue;
            foreach (var statement in body.Statements) {
                if (statement is not IfStatementSyntax {
                    Else: null,
                    Condition: { } condition,
                    Statement: { } guardedStatement
                } ||
                    !CSharpSyntaxFacts.IsThrowOnlyStatement(guardedStatement))
                    break;

                if (IsNullGuardForParameter(condition, parameter.Name)) return true;
                if (!method.Parameters.Any(candidate => IsNullGuardForParameter(condition, candidate.Name)))
                    break;
            }
        }
        return false;
    }
    internal static bool TryGetNotNullWhenValue(IParameterSymbol parameter, out bool value) =>
        TryGetParameterBooleanAttributeValue(parameter, NotNullWhenAttributeName, out value);

    internal static bool TryGetMaybeNullWhenValue(IParameterSymbol parameter, out bool value) =>
        TryGetParameterBooleanAttributeValue(parameter, MaybeNullWhenAttributeName, out value);

    internal static bool TryGetDoesNotReturnIfValue(IParameterSymbol parameter, out bool value) =>
        TryGetParameterBooleanAttributeValue(parameter, DoesNotReturnIfAttributeName, out value);

    internal static bool HasDoesNotReturn(IMethodSymbol method) {
        if (method == null) throw new ArgumentNullException(nameof(method));

        return HasAttribute(GetAttributes(method), DoesNotReturnAttributeName);
    }
    internal static NullableFlowFactState GetMethodReturnState(IMethodSymbol method) {
        if (method == null) throw new ArgumentNullException(nameof(method));

        if (!SymbolicTypeFacts.IsReferenceLikeType(method.ReturnType)) return NullableFlowFactState.Unknown;

        var contractState = GetMethodReturnContractState(method);
        if (contractState != NullableFlowFactState.Unknown) return contractState;

        return FromAnnotation(method.ReturnNullableAnnotation);
    }
    internal static NullableFlowFactState GetMethodBodyReturnState(IMethodSymbol method) {
        if (method == null) throw new ArgumentNullException(nameof(method));
        if (!method.IsAsync) return GetMethodReturnState(method);

        if (method.ReturnType is not INamedTypeSymbol {
            TypeArguments.Length: 1,
            TypeArgumentNullableAnnotations.Length: 1
        } taskLike ||
            !SymbolicTypeFacts.IsReferenceLikeType(taskLike.TypeArguments[0]))
            return NullableFlowFactState.Unknown;

        return FromAnnotation(taskLike.TypeArgumentNullableAnnotations[0]);
    }
    internal static ImmutableArray<string> GetMemberNotNullTargets(IMethodSymbol method) {
        if (method == null) throw new ArgumentNullException(nameof(method));

        var targets = ImmutableArray.CreateBuilder<string>();
        AddMemberTargets(GetAttributes(method), MemberNotNullAttributeName, null, targets);

        return [.. targets.Distinct(StringComparer.Ordinal)];
    }
    internal static ImmutableArray<string> GetMemberNotNullWhenTargets(IMethodSymbol method, bool methodReturnValue) {
        if (method == null) throw new ArgumentNullException(nameof(method));

        var targets = ImmutableArray.CreateBuilder<string>();
        AddMemberTargets(GetAttributes(method), MemberNotNullWhenAttributeName, methodReturnValue, targets);

        return [.. targets.Distinct(StringComparer.Ordinal)];
    }
    internal static ImmutableArray<string> GetMemberNotNullWhenTargets(IMethodSymbol method)
        => [.. GetMemberNotNullWhenTargets(method, true)
            .Concat(GetMemberNotNullWhenTargets(method, false))
            .Distinct(StringComparer.Ordinal)];
    internal static bool TryResolveInstanceMemberTarget(INamedTypeSymbol containingType, string target, out ISymbol member) {
        if (containingType == null) throw new ArgumentNullException(nameof(containingType));

        member = null!;
        var memberName = NormalizeMemberTarget(target);
        if (memberName == null) return false;

        var candidates = containingType.GetMembers(memberName)
            .Where(candidate =>
                candidate is IFieldSymbol or IPropertySymbol &&
                !candidate.IsStatic &&
                TryGetMemberType(candidate, out var type) &&
                SymbolicTypeFacts.IsReferenceLikeType(type))
            .ToArray();
        if (candidates.Length != 1) return false;

        member = candidates[0].OriginalDefinition;
        return true;
    }
    internal static bool TryGetMemberType(ISymbol member, out ITypeSymbol type) {
        switch (member) {
            case IFieldSymbol field:
                type = field.Type;
                return true;
            case IPropertySymbol property:
                type = property.Type;
                return true;
            default:
                type = null!;
                return false;
        }
    }
    internal static bool TryGetNotNullIfNotNullParameterName(IMethodSymbol method, out string parameterName) =>
        TryGetNotNullIfNotNullParameterName(GetReturnAttributes(method), out parameterName);

    internal static bool TryGetNotNullIfNotNullParameterName(IPropertySymbol property, out string parameterName) =>
        TryGetNotNullIfNotNullParameterName(GetReadAttributes(property), out parameterName);

    private static NullableFlowFactState GetExactExpressionState(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        expression = CSharpSyntaxFacts.UnwrapParentheses(expression);
        var operation = semanticModel.GetOperation(expression, cancellationToken);
        while (operation is IConversionOperation conversion) operation = conversion.Operand;

        var contractState = operation switch {
            IInvocationOperation invocation => GetMethodReturnContractState(invocation.TargetMethod),
            IPropertyReferenceOperation property => GetPropertyReadContractState(property.Property),
            IFieldReferenceOperation field => GetFieldReadContractState(field.Field),
            IParameterReferenceOperation parameter when HasExplicitNotNullInputContract(parameter.Parameter) =>
                NullableFlowFactState.NotNull,
            IInstanceReferenceOperation => NullableFlowFactState.NotNull,
            _ => NullableFlowFactState.Unknown
        };
        if (contractState != NullableFlowFactState.Unknown) return contractState;

        return GetStructuralExpressionState(expression, semanticModel, cancellationToken, exactContract: true);
    }
    private static NullableFlowFactState GetStructuralExpressionState(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool exactContract) {
        NullableFlowFactState GetNested(ExpressionSyntax nested) => exactContract
            ? GetExactExpressionState(nested, semanticModel, cancellationToken)
            : GetExpressionState(nested, semanticModel, cancellationToken);

        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue.HasValue)
            return constantValue.Value == null
                ? NullableFlowFactState.MaybeNull
                : NullableFlowFactState.NotNull;

        if (expression is ConditionalExpressionSyntax conditionalExpression) {
            var whenTrue = GetNested(conditionalExpression.WhenTrue);
            var whenFalse = GetNested(conditionalExpression.WhenFalse);
            if (whenTrue == NullableFlowFactState.NotNull && whenFalse == NullableFlowFactState.NotNull)
                return NullableFlowFactState.NotNull;

            var maybeNull = exactContract
                ? whenTrue == NullableFlowFactState.MaybeNull && whenFalse == NullableFlowFactState.MaybeNull
                : whenTrue == NullableFlowFactState.MaybeNull || whenFalse == NullableFlowFactState.MaybeNull;
            if (maybeNull) return NullableFlowFactState.MaybeNull;
        }
        if (expression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression)) {
            var left = GetNested(coalesceExpression.Left);
            var right = GetNested(coalesceExpression.Right);
            if (left == NullableFlowFactState.NotNull || right == NullableFlowFactState.NotNull)
                return NullableFlowFactState.NotNull;
        }
        return expression is ObjectCreationExpressionSyntax or
            AnonymousObjectCreationExpressionSyntax or
            ArrayCreationExpressionSyntax or
            ImplicitArrayCreationExpressionSyntax or
            CollectionExpressionSyntax or
            InterpolatedStringExpressionSyntax or
            TypeOfExpressionSyntax
                ? NullableFlowFactState.NotNull
                : NullableFlowFactState.Unknown;
    }
    private static NullableFlowFactState GetMethodReturnContractState(IMethodSymbol method) =>
        GetAttributeState(GetReturnAttributes(method));

    private static NullableFlowFactState GetAttributeState(IEnumerable<AttributeData> source) {
        var attributes = source.ToArray();
        if (HasAttribute(attributes, MaybeNullAttributeName))
            return NullableFlowFactState.MaybeNull;

        return HasAttribute(attributes, NotNullAttributeName)
            ? NullableFlowFactState.NotNull
            : NullableFlowFactState.Unknown;
    }
    private static NullableFlowFactState GetPropertyReadContractState(IPropertySymbol property) =>
        GetAttributeState(GetReadAttributes(property));

    private static NullableFlowFactState GetFieldReadContractState(IFieldSymbol field) {
        if (field is {
            IsStatic: true,
            Name: "Empty",
            Type.SpecialType: SpecialType.System_String,
            ContainingType.SpecialType: SpecialType.System_String
        })
            return NullableFlowFactState.NotNull;

        return GetAttributeState(GetAttributes(field));
    }
    private static NullableFlowFactState FromAnnotation(NullableAnnotation annotation) => annotation switch {
        NullableAnnotation.NotAnnotated => NullableFlowFactState.NotNull,
        NullableAnnotation.Annotated => NullableFlowFactState.MaybeNull,
        _ => NullableFlowFactState.Unknown
    };
    private static bool HasParameterAttribute(IParameterSymbol parameter, string attributeName) =>
        HasAttribute(GetInputAttributes(parameter), attributeName);

    private static bool IsNullGuardForParameter(ExpressionSyntax condition, string parameterName) {
        condition = CSharpSyntaxFacts.UnwrapParentheses(condition);
        if (condition is IsPatternExpressionSyntax {
            Expression: IdentifierNameSyntax identifier,
            Pattern: ConstantPatternSyntax {
                Expression.RawKind: (int)SyntaxKind.NullLiteralExpression
            }
        })
            return identifier.Identifier.ValueText == parameterName;

        if (condition is not BinaryExpressionSyntax binary ||
            !binary.IsKind(SyntaxKind.EqualsExpression))
            return false;

        return binary.Left is IdentifierNameSyntax left &&
               left.Identifier.ValueText == parameterName &&
               CSharpSyntaxFacts.IsNullLiteral(binary.Right) ||
               binary.Right is IdentifierNameSyntax right &&
               right.Identifier.ValueText == parameterName &&
               CSharpSyntaxFacts.IsNullLiteral(binary.Left);
    }
    private static bool TryGetParameterBooleanAttributeValue(IParameterSymbol parameter, string attributeName, out bool value) =>
        TryGetBooleanAttributeValue(GetAttributes(parameter), attributeName, out value);

    private static IEnumerable<AttributeData> GetInputAttributes(IParameterSymbol parameter) {
        foreach (var attribute in GetAttributes(parameter)) yield return attribute;

        if (parameter.ContainingSymbol is not IMethodSymbol {
            MethodKind: MethodKind.PropertySet,
            AssociatedSymbol: IPropertySymbol property
        } setter ||
            parameter.Ordinal != setter.Parameters.Length - 1)
            yield break;

        foreach (var attribute in GetReadAttributes(property)) yield return attribute;
    }
    private static IEnumerable<AttributeData> GetReturnAttributes(IMethodSymbol method) {
        foreach (var attribute in method.GetReturnTypeAttributes()) yield return attribute;
        if (SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition)) yield break;
        foreach (var attribute in method.OriginalDefinition.GetReturnTypeAttributes()) yield return attribute;
    }
    private static IEnumerable<AttributeData> GetReadAttributes(IPropertySymbol property) {
        foreach (var attribute in GetAttributes(property)) yield return attribute;
        if (property.GetMethod is { } getter)
            foreach (var attribute in GetReturnAttributes(getter))
                yield return attribute;
    }
    private static IEnumerable<AttributeData> GetAttributes(ISymbol symbol) {
        foreach (var attribute in symbol.GetAttributes()) yield return attribute;
        if (SymbolEqualityComparer.Default.Equals(symbol, symbol.OriginalDefinition)) yield break;
        foreach (var attribute in symbol.OriginalDefinition.GetAttributes()) yield return attribute;
    }
    private static bool HasAttribute(IEnumerable<AttributeData> attributes, string attributeName) => attributes.Any(attribute =>
            string.Equals(SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass), attributeName, StringComparison.Ordinal));
    private static bool TryGetBooleanAttributeValue(IEnumerable<AttributeData> attributes, string attributeName, out bool value) {
        foreach (var attribute in attributes) {
            if (!string.Equals(SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass), attributeName, StringComparison.Ordinal) ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not bool attributeValue)
                continue;

            value = attributeValue;
            return true;
        }
        value = false;
        return false;
    }
    private static void AddMemberTargets(
        IEnumerable<AttributeData> attributes,
        string attributeName,
        bool? methodReturnValue,
        ICollection<string> targets) {
        foreach (var attribute in attributes) {
            if (!string.Equals(SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass), attributeName, StringComparison.Ordinal))
                continue;

            var startIndex = 0;
            if (methodReturnValue.HasValue) {
                if (attribute.ConstructorArguments.Length < 2 ||
                    attribute.ConstructorArguments[0].Value is not bool attributeReturnValue ||
                    attributeReturnValue != methodReturnValue.Value)
                    continue;

                startIndex = 1;
            }
            for (var index = startIndex; index < attribute.ConstructorArguments.Length; index++)
                AddMemberTarget(attribute.ConstructorArguments[index], targets);
        }
    }
    private static void AddMemberTarget(TypedConstant argument, ICollection<string> targets) {
        if (argument.Kind == TypedConstantKind.Array) {
            foreach (var item in argument.Values) AddMemberTarget(item, targets);

            return;
        }
        if (argument.Value is string target && !string.IsNullOrWhiteSpace(target)) targets.Add(target);
    }
    private static string? NormalizeMemberTarget(string target) {
        target = target.Trim();
        if (target.StartsWith("this.", StringComparison.Ordinal)) target = target.Substring("this.".Length);

        return target.Length != 0 && target.IndexOf(".", StringComparison.Ordinal) < 0
            ? target
            : null;
    }
    private static bool TryGetNotNullIfNotNullParameterName(IEnumerable<AttributeData> attributes, out string parameterName) {
        foreach (var attribute in attributes) {
            if (!string.Equals(
                    SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                    NotNullIfNotNullAttributeName,
                    StringComparison.Ordinal) ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not string candidate ||
                string.IsNullOrEmpty(candidate))
                continue;

            parameterName = candidate;
            return true;
        }
        parameterName = string.Empty;
        return false;
    }
}
