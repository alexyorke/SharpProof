namespace SharpProof.Symbolic;
internal enum NullableFlowFactState {
    Unknown,
    MaybeNull,
    NotNull
}
internal static class NullableFlowFacts {
    private const string AttributePrefix = "System.Diagnostics.CodeAnalysis.";
    private enum ContractProjection {
        Input,
        Output,
        Return
    }
    private readonly record struct NullableAttributeSet(ImmutableArray<AttributeData> Values) {
        internal bool Has(string name) => Values.Any(attribute => IsAttribute(attribute, name));
        internal bool? GetBoolean(string name) {
            foreach (var attribute in Values.Where(attribute => IsAttribute(attribute, name)))
                if (GetSingleBoolean(attribute) is { } value)
                    return value;
            return null;
        }
        internal NullableFlowFactState Project(
            ContractProjection projection,
            NullableAnnotation annotation,
            RefKind refKind = RefKind.None,
            bool? methodReturnValue = null) {
            if (projection == ContractProjection.Input) {
                if (Has("AllowNullAttribute") == Has("DisallowNullAttribute"))
                    return Has("AllowNullAttribute")
                        ? NullableFlowFactState.Unknown
                        : FromAnnotation(annotation);
                return Has("AllowNullAttribute")
                    ? NullableFlowFactState.MaybeNull
                    : NullableFlowFactState.NotNull;
            }
            if (projection == ContractProjection.Output) {
                if (methodReturnValue.HasValue &&
                    GetBoolean("MaybeNullWhenAttribute") == methodReturnValue)
                    return NullableFlowFactState.MaybeNull;
                if (methodReturnValue.HasValue &&
                    GetBoolean("NotNullWhenAttribute") == methodReturnValue)
                    return NullableFlowFactState.NotNull;
                if (Has("MaybeNullAttribute")) return NullableFlowFactState.MaybeNull;
                if (Has("NotNullAttribute")) return NullableFlowFactState.NotNull;
                return refKind is RefKind.Ref or RefKind.Out
                    ? FromAnnotation(annotation)
                    : NullableFlowFactState.Unknown;
            }
            if (Has("MaybeNullAttribute")) return NullableFlowFactState.MaybeNull;
            return Has("NotNullAttribute")
                ? NullableFlowFactState.NotNull
                : FromAnnotation(annotation);
        }
        internal ImmutableArray<string> GetMemberTargets(string name, bool? returnValue = null) {
            var targets = ImmutableArray.CreateBuilder<string>();
            foreach (var attribute in Values.Where(attribute => IsAttribute(attribute, name))) {
                var start = 0;
                if (returnValue.HasValue) {
                    if (attribute.ConstructorArguments.Length < 2 ||
                        attribute.ConstructorArguments[0].Value is not bool actual ||
                        actual != returnValue.Value)
                        continue;
                    start = 1;
                }
                targets.AddRange(FlattenMemberTargets(attribute.ConstructorArguments.Skip(start)));
            }
            return [.. targets.Distinct(StringComparer.Ordinal)];
        }
        internal ImmutableArray<string> GetParameterNames(string name) =>
            [.. Values
                .Where(attribute =>
                    IsAttribute(attribute, name) &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is string parameterName &&
                    !string.IsNullOrEmpty(parameterName))
                .Select(static attribute => (string)attribute.ConstructorArguments[0].Value!)
                .Distinct(StringComparer.Ordinal)];
    }
    internal static NullableFlowFactState GetExpressionState(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));
        expression = CSharpSyntaxFacts.UnwrapParentheses(expression);
        var state = GetTypeInfoState(semanticModel.GetTypeInfo(expression, cancellationToken));
        return state != NullableFlowFactState.Unknown
            ? state
            : GetStructuralExpressionState(expression, semanticModel, cancellationToken, exactContract: false);
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
            return GetTypeInfoState(semanticModel.GetSpeculativeTypeInfo(
                position,
                CSharpSyntaxFacts.UnwrapParentheses(expression).WithoutTrivia(),
                SpeculativeBindingOption.BindAsExpression));
        }
        catch (ArgumentException) {
            return NullableFlowFactState.Unknown;
        }
    }
    internal static bool IsDefinitelyNotNullReferenceValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) =>
        GetExpressionState(expression, semanticModel, cancellationToken) == NullableFlowFactState.NotNull;
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
                : CSharpSyntaxFacts.IsNullLiteral(binary.Right) ? binary.Left : null;
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
        return SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
            expression, semanticModel, cancellationToken, out symbol);
    }
    internal static NullableFlowFactState GetParameterInputState(IParameterSymbol parameter) {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));
        return SymbolicTypeFacts.IsReferenceLikeType(parameter.Type)
            ? Decode(GetInputAttributes(parameter)).Project(ContractProjection.Input, parameter.NullableAnnotation)
            : NullableFlowFactState.Unknown;
    }
    internal static bool HasExplicitNotNullInputContract(IParameterSymbol parameter) {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));
        var contract = Decode(GetInputAttributes(parameter));
        return contract.Has("DisallowNullAttribute") && !contract.Has("AllowNullAttribute");
    }
    internal static NullableFlowFactState GetParameterOutputState(
        IParameterSymbol parameter,
        bool? methodReturnValue = null) {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));
        if (!SymbolicTypeFacts.IsReferenceLikeType(parameter.Type) &&
            parameter.Type.TypeKind != TypeKind.TypeParameter)
            return NullableFlowFactState.Unknown;
        var contract = Decode(GetAttributes(parameter));
        if (parameter.Type.TypeKind == TypeKind.TypeParameter &&
            parameter.NullableAnnotation != NullableAnnotation.Annotated &&
            methodReturnValue.HasValue &&
            contract.GetBoolean("MaybeNullWhenAttribute") is { } maybeNullWhen &&
            maybeNullWhen != methodReturnValue.Value &&
            !contract.Has("MaybeNullAttribute"))
            return NullableFlowFactState.NotNull;
        return contract.Project(
            ContractProjection.Output,
            parameter.NullableAnnotation,
            parameter.RefKind,
            methodReturnValue);
    }
    internal static bool HasNotNullPostcondition(IParameterSymbol parameter) {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));
        return Decode(GetAttributes(parameter)).Has("NotNullAttribute");
    }
    internal static bool HasInferredNotNullNormalCompletionPostcondition(
        IParameterSymbol parameter,
        CancellationToken cancellationToken) {
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
                if (IsNullGuardForParameter(condition, parameter)) return true;
                if (!method.Parameters.Any(candidate => IsNullGuardForParameter(condition, candidate)))
                    break;
            }
        }
        return false;
    }
    internal static bool TryGetNotNullWhenValue(IParameterSymbol parameter, out bool value) =>
        TryGetBoolean(Decode(GetAttributes(parameter)).GetBoolean("NotNullWhenAttribute"), out value);
    internal static bool TryGetMaybeNullWhenValue(IParameterSymbol parameter, out bool value) =>
        TryGetBoolean(Decode(GetAttributes(parameter)).GetBoolean("MaybeNullWhenAttribute"), out value);
    internal static bool TryGetDoesNotReturnIfValue(IParameterSymbol parameter, out bool value) =>
        TryGetBoolean(Decode(GetAttributes(parameter)).GetBoolean("DoesNotReturnIfAttribute"), out value);
    internal static bool HasDoesNotReturn(IMethodSymbol method) {
        if (method == null) throw new ArgumentNullException(nameof(method));
        return Decode(GetAttributes(method)).Has("DoesNotReturnAttribute");
    }
    internal static NullableFlowFactState GetMethodReturnState(IMethodSymbol method) {
        if (method == null) throw new ArgumentNullException(nameof(method));
        return SymbolicTypeFacts.IsReferenceLikeType(method.ReturnType)
            ? Decode(GetReturnAttributes(method)).Project(
                ContractProjection.Return, method.ReturnNullableAnnotation)
            : NullableFlowFactState.Unknown;
    }
    internal static NullableFlowFactState GetMethodBodyReturnState(IMethodSymbol method) {
        if (method == null) throw new ArgumentNullException(nameof(method));
        return GetMethodBodyReturnState(method, method.IsAsync);
    }
    internal static NullableFlowFactState GetMethodBodyReturnState(IMethodSymbol method, bool isAsyncBody) {
        if (method == null) throw new ArgumentNullException(nameof(method));
        if (!isAsyncBody) return GetMethodReturnState(method);
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
        return Decode(GetAttributes(method)).GetMemberTargets("MemberNotNullAttribute");
    }
    internal static ImmutableArray<string> GetMemberNotNullWhenTargets(
        IMethodSymbol method,
        bool methodReturnValue) {
        if (method == null) throw new ArgumentNullException(nameof(method));
        return Decode(GetAttributes(method))
            .GetMemberTargets("MemberNotNullWhenAttribute", methodReturnValue);
    }
    internal static ImmutableArray<string> GetMemberNotNullWhenTargets(IMethodSymbol method) =>
        [.. GetMemberNotNullWhenTargets(method, true)
            .Concat(GetMemberNotNullWhenTargets(method, false))
            .Distinct(StringComparer.Ordinal)];
    internal static bool TryResolveInstanceMemberTarget(
        INamedTypeSymbol containingType,
        string target,
        out ISymbol member) {
        if (containingType == null) throw new ArgumentNullException(nameof(containingType));
        member = null!;
        var memberName = NormalizeMemberTarget(target);
        if (memberName == null) return false;
        for (var current = containingType; current != null; current = current.BaseType) {
            var candidates = current.GetMembers(memberName)
                .Where(candidate =>
                    candidate is IFieldSymbol or IPropertySymbol &&
                    !candidate.IsStatic &&
                    TryGetMemberType(candidate, out var type) &&
                    SymbolicTypeFacts.IsReferenceLikeType(type))
                .ToArray();
            if (candidates.Length == 0) continue;
            if (candidates.Length != 1) return false;
            member = candidates[0].OriginalDefinition;
            return true;
        }
        return false;
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
    internal static ImmutableArray<string> GetNotNullIfNotNullParameterNames(IMethodSymbol method) =>
        Decode(GetReturnAttributes(method)).GetParameterNames("NotNullIfNotNullAttribute");
    internal static bool TryGetNotNullIfNotNullParameterName(
        IMethodSymbol method,
        out string parameterName) =>
        TryGetFirst(
            Decode(GetReturnAttributes(method)).GetParameterNames("NotNullIfNotNullAttribute"),
            out parameterName);
    internal static bool TryGetNotNullIfNotNullParameterName(
        IPropertySymbol property,
        out string parameterName) =>
        TryGetFirst(
            Decode(GetReadAttributes(property)).GetParameterNames("NotNullIfNotNullAttribute"),
            out parameterName);
    private static NullableFlowFactState GetTypeInfoState(TypeInfo typeInfo) {
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
        return contractState != NullableFlowFactState.Unknown
            ? contractState
            : GetStructuralExpressionState(expression, semanticModel, cancellationToken, exactContract: true);
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
        if (expression is ConditionalExpressionSyntax conditional) {
            var whenTrue = GetNested(conditional.WhenTrue);
            var whenFalse = GetNested(conditional.WhenFalse);
            if (whenTrue == NullableFlowFactState.NotNull && whenFalse == NullableFlowFactState.NotNull)
                return NullableFlowFactState.NotNull;
            var maybeNull = exactContract
                ? whenTrue == NullableFlowFactState.MaybeNull && whenFalse == NullableFlowFactState.MaybeNull
                : whenTrue == NullableFlowFactState.MaybeNull || whenFalse == NullableFlowFactState.MaybeNull;
            if (maybeNull) return NullableFlowFactState.MaybeNull;
        }
        if (expression is BinaryExpressionSyntax coalesce &&
            coalesce.IsKind(SyntaxKind.CoalesceExpression) &&
            (GetNested(coalesce.Left) == NullableFlowFactState.NotNull ||
             GetNested(coalesce.Right) == NullableFlowFactState.NotNull))
            return NullableFlowFactState.NotNull;
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
        Decode(GetReturnAttributes(method)).Project(ContractProjection.Return, NullableAnnotation.None);
    private static NullableFlowFactState GetPropertyReadContractState(IPropertySymbol property) =>
        Decode(GetReadAttributes(property)).Project(ContractProjection.Return, NullableAnnotation.None);
    private static NullableFlowFactState GetFieldReadContractState(IFieldSymbol field) {
        if (field is {
            IsStatic: true,
            Name: "Empty",
            Type.SpecialType: SpecialType.System_String,
            ContainingType.SpecialType: SpecialType.System_String
        })
            return NullableFlowFactState.NotNull;
        return Decode(GetAttributes(field)).Project(ContractProjection.Return, NullableAnnotation.None);
    }
    private static NullableFlowFactState FromAnnotation(NullableAnnotation annotation) => annotation switch {
        NullableAnnotation.NotAnnotated => NullableFlowFactState.NotNull,
        NullableAnnotation.Annotated => NullableFlowFactState.MaybeNull,
        _ => NullableFlowFactState.Unknown
    };
    private static bool IsNullGuardForParameter(ExpressionSyntax condition, IParameterSymbol parameter) {
        condition = CSharpSyntaxFacts.UnwrapParentheses(condition);
        if (condition is IsPatternExpressionSyntax {
            Expression: IdentifierNameSyntax identifier,
            Pattern: ConstantPatternSyntax {
                Expression.RawKind: (int)SyntaxKind.NullLiteralExpression
            }
        })
            return identifier.Identifier.ValueText == parameter.Name;
        if (condition is not BinaryExpressionSyntax binary ||
            !binary.IsKind(SyntaxKind.EqualsExpression) ||
            HasUserDefinedEqualityOperator(parameter.Type))
            return false;
        return binary.Left is IdentifierNameSyntax left &&
               left.Identifier.ValueText == parameter.Name &&
               CSharpSyntaxFacts.IsNullLiteral(binary.Right) ||
               binary.Right is IdentifierNameSyntax right &&
               right.Identifier.ValueText == parameter.Name &&
               CSharpSyntaxFacts.IsNullLiteral(binary.Left);
    }
    private static bool HasUserDefinedEqualityOperator(ITypeSymbol type) {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            if (current.GetMembers("op_Equality").OfType<IMethodSymbol>().Any())
                return true;
        return false;
    }
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
    private static NullableAttributeSet Decode(IEnumerable<AttributeData> source) => new([.. source]);
    private static bool IsAttribute(AttributeData attribute, string name) =>
        string.Equals(
            SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
            AttributePrefix + name,
            StringComparison.Ordinal);
    private static bool? GetSingleBoolean(AttributeData attribute) =>
        attribute.ConstructorArguments.Length == 1 &&
        attribute.ConstructorArguments[0].Value is bool value
            ? value
            : null;
    private static IEnumerable<string> FlattenMemberTargets(IEnumerable<TypedConstant> arguments) {
        foreach (var argument in arguments) {
            if (argument.Kind == TypedConstantKind.Array) {
                foreach (var target in FlattenMemberTargets(argument.Values)) yield return target;
            }
            else if (argument.Value is string target && !string.IsNullOrWhiteSpace(target)) {
                yield return target;
            }
        }
    }
    private static bool TryGetBoolean(bool? candidate, out bool value) {
        value = candidate.GetValueOrDefault();
        return candidate.HasValue;
    }
    private static bool TryGetFirst(ImmutableArray<string> values, out string value) {
        value = values.IsEmpty ? string.Empty : values[0];
        return !values.IsEmpty;
    }
    private static string? NormalizeMemberTarget(string target) {
        target = target.Trim();
        if (target.StartsWith("this.", StringComparison.Ordinal))
            target = target.Substring("this.".Length);
        return target.Length != 0 && target.IndexOf(".", StringComparison.Ordinal) < 0
            ? target
            : null;
    }
}
