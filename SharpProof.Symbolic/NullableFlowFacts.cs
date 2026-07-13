using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Symbolic;

internal enum NullableFlowFactState
{
    Unknown,
    MaybeNull,
    NotNull
}

internal static class NullableFlowFacts
{
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
        CancellationToken cancellationToken)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));

        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        expression = CSharpSyntaxFacts.UnwrapParentheses(expression);
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type == null || !SymbolicTypeFacts.IsReferenceLikeType(type))
            return NullableFlowFactState.Unknown;

        if (TryGetExplicitExpressionState(expression, semanticModel, cancellationToken, out var explicitState))
            return explicitState;

        var flowState = typeInfo.Nullability.FlowState != NullableFlowState.None
            ? typeInfo.Nullability.FlowState
            : typeInfo.ConvertedNullability.FlowState;
        if (flowState == NullableFlowState.NotNull) return NullableFlowFactState.NotNull;

        if (flowState == NullableFlowState.MaybeNull) return NullableFlowFactState.MaybeNull;

        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue.HasValue)
            return constantValue.Value == null
                ? NullableFlowFactState.MaybeNull
                : NullableFlowFactState.NotNull;

        if (expression is ConditionalExpressionSyntax conditionalExpression)
        {
            var whenTrue = GetExpressionState(conditionalExpression.WhenTrue, semanticModel, cancellationToken);
            var whenFalse = GetExpressionState(conditionalExpression.WhenFalse, semanticModel, cancellationToken);
            if (whenTrue == NullableFlowFactState.NotNull && whenFalse == NullableFlowFactState.NotNull)
                return NullableFlowFactState.NotNull;

            if (whenTrue == NullableFlowFactState.MaybeNull || whenFalse == NullableFlowFactState.MaybeNull)
                return NullableFlowFactState.MaybeNull;
        }

        if (expression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression))
        {
            var left = GetExpressionState(coalesceExpression.Left, semanticModel, cancellationToken);
            var right = GetExpressionState(coalesceExpression.Right, semanticModel, cancellationToken);
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

    internal static NullableFlowFactState GetExpressionStateAtPosition(
        ExpressionSyntax expression,
        int position,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        if (semanticModel == null) throw new ArgumentNullException(nameof(semanticModel));

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
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
            return flowState switch
            {
                NullableFlowState.NotNull => NullableFlowFactState.NotNull,
                NullableFlowState.MaybeNull => NullableFlowFactState.MaybeNull,
                _ => NullableFlowFactState.Unknown
            };
        }
        catch (ArgumentException)
        {
            return NullableFlowFactState.Unknown;
        }
    }

    internal static bool IsDefinitelyNotNullReferenceValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return GetExpressionState(expression, semanticModel, cancellationToken) ==
               NullableFlowFactState.NotNull;
    }

    internal static bool TryEvaluateNullTest(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool value)
    {
        expression = CSharpSyntaxFacts.UnwrapParentheses(expression);
        if (expression is PrefixUnaryExpressionSyntax negation &&
            negation.IsKind(SyntaxKind.LogicalNotExpression) &&
            TryEvaluateNullTest(negation.Operand, semanticModel, cancellationToken, out var operandValue))
        {
            value = !operandValue;
            return true;
        }

        if (expression is BinaryExpressionSyntax binary &&
            (binary.IsKind(SyntaxKind.EqualsExpression) ||
             binary.IsKind(SyntaxKind.NotEqualsExpression)) &&
            semanticModel.GetOperation(binary, cancellationToken) is IBinaryOperation { OperatorMethod: null })
        {
            var target = CSharpSyntaxFacts.IsNullLiteral(binary.Left)
                ? binary.Right
                : CSharpSyntaxFacts.IsNullLiteral(binary.Right)
                    ? binary.Left
                    : null;
            if (target != null &&
                GetExactExpressionState(target, semanticModel, cancellationToken) == NullableFlowFactState.NotNull)
            {
                value = binary.IsKind(SyntaxKind.NotEqualsExpression);
                return true;
            }
        }

        if (expression is IsPatternExpressionSyntax isPattern &&
            CSharpSyntaxFacts.TryGetNullPatternPolarity(isPattern.Pattern, out var matchesNonNull) &&
            GetExactExpressionState(isPattern.Expression, semanticModel, cancellationToken) ==
            NullableFlowFactState.NotNull)
        {
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
        out ISymbol symbol)
    {
        expression = CSharpSyntaxFacts.UnwrapParentheses(expression);
        if (expression is DeclarationExpressionSyntax
            {
                Designation: SingleVariableDesignationSyntax designation
            } &&
            semanticModel.GetDeclaredSymbol(designation, cancellationToken) is ILocalSymbol declaredLocal)
        {
            symbol = declaredLocal.OriginalDefinition;
            return true;
        }

        var candidate = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition;
        if (candidate is ILocalSymbol or IParameterSymbol)
        {
            symbol = candidate;
            return true;
        }

        symbol = null!;
        return false;
    }

    internal static NullableFlowFactState GetParameterInputState(IParameterSymbol parameter)
    {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));

        if (!SymbolicTypeFacts.IsReferenceLikeType(parameter.Type)) return NullableFlowFactState.Unknown;

        var allowsNull = HasParameterAttribute(parameter, AllowNullAttributeName);
        var disallowsNull = HasParameterAttribute(parameter, DisallowNullAttributeName);
        if (allowsNull && disallowsNull) return NullableFlowFactState.Unknown;

        if (allowsNull) return NullableFlowFactState.MaybeNull;

        if (disallowsNull) return NullableFlowFactState.NotNull;

        return FromAnnotation(parameter.NullableAnnotation);
    }

    internal static bool HasExplicitNotNullInputContract(IParameterSymbol parameter)
    {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));

        return HasParameterAttribute(parameter, DisallowNullAttributeName) &&
               !HasParameterAttribute(parameter, AllowNullAttributeName);
    }

    internal static NullableFlowFactState GetParameterOutputState(
        IParameterSymbol parameter,
        bool? methodReturnValue = null)
    {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));

        if (!SymbolicTypeFacts.IsReferenceLikeType(parameter.Type)) return NullableFlowFactState.Unknown;

        if (methodReturnValue.HasValue)
        {
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

    internal static bool HasNotNullPostcondition(IParameterSymbol parameter)
    {
        return HasParameterAttribute(parameter, NotNullAttributeName);
    }

    internal static bool HasInferredNotNullNormalCompletionPostcondition(
        IParameterSymbol parameter,
        CancellationToken cancellationToken)
    {
        if (parameter == null) throw new ArgumentNullException(nameof(parameter));

        if (parameter.RefKind != RefKind.None ||
            parameter.ContainingSymbol is not IMethodSymbol method)
            return false;

        foreach (var syntaxReference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = syntaxReference.GetSyntax(cancellationToken);
            var body = declaration switch
            {
                MethodDeclarationSyntax methodDeclaration => methodDeclaration.Body,
                LocalFunctionStatementSyntax localFunction => localFunction.Body,
                _ => null
            };
            if (body == null) continue;
            foreach (var statement in body.Statements)
            {
                if (statement is not IfStatementSyntax
                    {
                        Else: null,
                        Condition: { } condition,
                        Statement: { } guardedStatement
                    } ||
                    !CSharpSyntaxFacts.IsThrowOnlyStatement(guardedStatement))
                    break;

                if (IsNullGuardForParameter(condition, parameter.Name)) return true;
                if (!method.Parameters.Any(candidate =>
                        IsNullGuardForParameter(condition, candidate.Name)))
                    break;
            }
        }

        return false;
    }

    internal static bool TryGetNotNullWhenValue(IParameterSymbol parameter, out bool value)
    {
        return TryGetParameterBooleanAttributeValue(parameter, NotNullWhenAttributeName, out value);
    }

    internal static bool TryGetMaybeNullWhenValue(IParameterSymbol parameter, out bool value)
    {
        return TryGetParameterBooleanAttributeValue(parameter, MaybeNullWhenAttributeName, out value);
    }

    internal static bool TryGetDoesNotReturnIfValue(IParameterSymbol parameter, out bool value)
    {
        return TryGetParameterBooleanAttributeValue(parameter, DoesNotReturnIfAttributeName, out value);
    }

    internal static bool HasDoesNotReturn(IMethodSymbol method)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));

        return HasAttribute(method.GetAttributes(), DoesNotReturnAttributeName) ||
               (!SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition) &&
                HasAttribute(method.OriginalDefinition.GetAttributes(), DoesNotReturnAttributeName));
    }

    internal static NullableFlowFactState GetMethodReturnState(IMethodSymbol method)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));

        if (!SymbolicTypeFacts.IsReferenceLikeType(method.ReturnType)) return NullableFlowFactState.Unknown;

        var contractState = GetMethodReturnContractState(method);
        if (contractState != NullableFlowFactState.Unknown) return contractState;

        return FromAnnotation(method.ReturnNullableAnnotation);
    }

    internal static NullableFlowFactState GetMethodBodyReturnState(IMethodSymbol method)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));
        if (!method.IsAsync) return GetMethodReturnState(method);

        if (method.ReturnType is not INamedTypeSymbol
            {
                TypeArguments.Length: 1,
                TypeArgumentNullableAnnotations.Length: 1
            } taskLike ||
            !SymbolicTypeFacts.IsReferenceLikeType(taskLike.TypeArguments[0]))
            return NullableFlowFactState.Unknown;

        return FromAnnotation(taskLike.TypeArgumentNullableAnnotations[0]);
    }

    internal static NullableFlowFactState GetPropertyReadState(IPropertySymbol property)
    {
        if (property == null) throw new ArgumentNullException(nameof(property));

        if (!SymbolicTypeFacts.IsReferenceLikeType(property.Type)) return NullableFlowFactState.Unknown;

        var contractState = GetPropertyReadContractState(property);
        if (contractState != NullableFlowFactState.Unknown) return contractState;

        return FromAnnotation(property.NullableAnnotation);
    }

    internal static NullableFlowFactState GetFieldReadState(IFieldSymbol field)
    {
        if (field == null) throw new ArgumentNullException(nameof(field));

        if (!SymbolicTypeFacts.IsReferenceLikeType(field.Type)) return NullableFlowFactState.Unknown;

        var contractState = GetFieldReadContractState(field);
        if (contractState != NullableFlowFactState.Unknown) return contractState;

        return FromAnnotation(field.NullableAnnotation);
    }

    internal static ImmutableArray<string> GetMemberNotNullTargets(IMethodSymbol method)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));

        var targets = ImmutableArray.CreateBuilder<string>();
        AddMemberTargets(method.GetAttributes(), MemberNotNullAttributeName, null, targets);
        if (!SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition))
            AddMemberTargets(
                method.OriginalDefinition.GetAttributes(),
                MemberNotNullAttributeName,
                null,
                targets);

        return targets.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    internal static ImmutableArray<string> GetMemberNotNullWhenTargets(
        IMethodSymbol method,
        bool methodReturnValue)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));

        var targets = ImmutableArray.CreateBuilder<string>();
        AddMemberTargets(
            method.GetAttributes(),
            MemberNotNullWhenAttributeName,
            methodReturnValue,
            targets);
        if (!SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition))
            AddMemberTargets(
                method.OriginalDefinition.GetAttributes(),
                MemberNotNullWhenAttributeName,
                methodReturnValue,
                targets);

        return targets.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    internal static ImmutableArray<string> GetMemberNotNullWhenTargets(IMethodSymbol method)
    {
        return GetMemberNotNullWhenTargets(method, true)
            .Concat(GetMemberNotNullWhenTargets(method, false))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal static bool TryResolveInstanceMemberTarget(
        INamedTypeSymbol containingType,
        string target,
        out ISymbol member)
    {
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

    internal static bool TryGetMemberType(ISymbol member, out ITypeSymbol type)
    {
        switch (member)
        {
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

    internal static bool TryGetNotNullIfNotNullParameterName(
        IMethodSymbol method,
        out string parameterName)
    {
        if (TryGetNotNullIfNotNullParameterName(method.GetReturnTypeAttributes(), out parameterName)) return true;

        if (!SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition) &&
            TryGetNotNullIfNotNullParameterName(
                method.OriginalDefinition.GetReturnTypeAttributes(),
                out parameterName))
            return true;

        parameterName = string.Empty;
        return false;
    }

    internal static bool TryGetNotNullIfNotNullParameterName(
        IPropertySymbol property,
        out string parameterName)
    {
        if (TryGetNotNullIfNotNullParameterName(property.GetAttributes(), out parameterName) ||
            TryGetNotNullIfNotNullParameterName(
                property.GetMethod?.GetReturnTypeAttributes() ?? ImmutableArray<AttributeData>.Empty,
                out parameterName))
            return true;

        if (!SymbolEqualityComparer.Default.Equals(property, property.OriginalDefinition) &&
            (TryGetNotNullIfNotNullParameterName(
                 property.OriginalDefinition.GetAttributes(),
                 out parameterName) ||
             TryGetNotNullIfNotNullParameterName(
                 property.OriginalDefinition.GetMethod?.GetReturnTypeAttributes() ??
                 ImmutableArray<AttributeData>.Empty,
                 out parameterName)))
            return true;

        parameterName = string.Empty;
        return false;
    }

    private static bool TryGetExplicitExpressionState(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out NullableFlowFactState state)
    {
        var operation = semanticModel.GetOperation(expression, cancellationToken);
        while (operation is IConversionOperation conversion) operation = conversion.Operand;

        state = operation switch
        {
            IInvocationOperation invocation => GetMethodReturnState(invocation.TargetMethod),
            IPropertyReferenceOperation property => GetPropertyReadState(property.Property),
            IFieldReferenceOperation field => GetFieldReadState(field.Field),
            _ => NullableFlowFactState.Unknown
        };
        return state != NullableFlowFactState.Unknown;
    }

    private static NullableFlowFactState GetExactExpressionState(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        expression = CSharpSyntaxFacts.UnwrapParentheses(expression);
        var operation = semanticModel.GetOperation(expression, cancellationToken);
        while (operation is IConversionOperation conversion) operation = conversion.Operand;

        var contractState = operation switch
        {
            IInvocationOperation invocation => GetMethodReturnContractState(invocation.TargetMethod),
            IPropertyReferenceOperation property => GetPropertyReadContractState(property.Property),
            IFieldReferenceOperation field => GetFieldReadContractState(field.Field),
            IParameterReferenceOperation parameter when HasExplicitNotNullInputContract(parameter.Parameter) =>
                NullableFlowFactState.NotNull,
            IInstanceReferenceOperation => NullableFlowFactState.NotNull,
            _ => NullableFlowFactState.Unknown
        };
        if (contractState != NullableFlowFactState.Unknown) return contractState;

        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue.HasValue)
            return constantValue.Value == null
                ? NullableFlowFactState.MaybeNull
                : NullableFlowFactState.NotNull;

        if (expression is ConditionalExpressionSyntax conditionalExpression)
        {
            var whenTrue = GetExactExpressionState(
                conditionalExpression.WhenTrue,
                semanticModel,
                cancellationToken);
            var whenFalse = GetExactExpressionState(
                conditionalExpression.WhenFalse,
                semanticModel,
                cancellationToken);
            if (whenTrue == NullableFlowFactState.NotNull && whenFalse == NullableFlowFactState.NotNull)
                return NullableFlowFactState.NotNull;

            if (whenTrue == NullableFlowFactState.MaybeNull && whenFalse == NullableFlowFactState.MaybeNull)
                return NullableFlowFactState.MaybeNull;
        }

        if (expression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression))
        {
            var left = GetExactExpressionState(coalesceExpression.Left, semanticModel, cancellationToken);
            var right = GetExactExpressionState(coalesceExpression.Right, semanticModel, cancellationToken);
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

    private static NullableFlowFactState GetMethodReturnContractState(IMethodSymbol method)
    {
        var attributes = method.GetReturnTypeAttributes();
        var originalAttributes = SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition)
            ? ImmutableArray<AttributeData>.Empty
            : method.OriginalDefinition.GetReturnTypeAttributes();
        if (HasAttribute(attributes, MaybeNullAttributeName) ||
            HasAttribute(originalAttributes, MaybeNullAttributeName))
            return NullableFlowFactState.MaybeNull;

        return HasAttribute(attributes, NotNullAttributeName) ||
               HasAttribute(originalAttributes, NotNullAttributeName)
            ? NullableFlowFactState.NotNull
            : NullableFlowFactState.Unknown;
    }

    private static NullableFlowFactState GetPropertyReadContractState(IPropertySymbol property)
    {
        if (PropertyHasAttribute(property, MaybeNullAttributeName)) return NullableFlowFactState.MaybeNull;

        return PropertyHasAttribute(property, NotNullAttributeName)
            ? NullableFlowFactState.NotNull
            : NullableFlowFactState.Unknown;
    }

    private static NullableFlowFactState GetFieldReadContractState(IFieldSymbol field)
    {
        if (field is
            {
                IsStatic: true,
                Name: "Empty",
                Type.SpecialType: SpecialType.System_String,
                ContainingType.SpecialType: SpecialType.System_String
            })
            return NullableFlowFactState.NotNull;

        if (FieldHasAttribute(field, MaybeNullAttributeName)) return NullableFlowFactState.MaybeNull;

        return FieldHasAttribute(field, NotNullAttributeName)
            ? NullableFlowFactState.NotNull
            : NullableFlowFactState.Unknown;
    }

    private static NullableFlowFactState FromAnnotation(NullableAnnotation annotation)
    {
        return annotation switch
        {
            NullableAnnotation.NotAnnotated => NullableFlowFactState.NotNull,
            NullableAnnotation.Annotated => NullableFlowFactState.MaybeNull,
            _ => NullableFlowFactState.Unknown
        };
    }

    private static bool HasParameterAttribute(IParameterSymbol parameter, string attributeName)
    {
        if (HasAttribute(parameter.GetAttributes(), attributeName)) return true;

        if (!SymbolEqualityComparer.Default.Equals(parameter, parameter.OriginalDefinition) &&
            HasAttribute(parameter.OriginalDefinition.GetAttributes(), attributeName))
            return true;

        if (parameter.ContainingSymbol is IMethodSymbol
            {
                MethodKind: MethodKind.PropertySet,
                AssociatedSymbol: IPropertySymbol property
            } setter &&
            parameter.Ordinal == setter.Parameters.Length - 1 &&
            PropertyHasAttribute(property, attributeName))
            return true;

        return false;
    }

    private static bool IsNullGuardForParameter(ExpressionSyntax condition, string parameterName)
    {
        condition = CSharpSyntaxFacts.UnwrapParentheses(condition);
        if (condition is IsPatternExpressionSyntax
            {
                Expression: IdentifierNameSyntax identifier,
                Pattern: ConstantPatternSyntax
                {
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

    private static bool TryGetParameterBooleanAttributeValue(
        IParameterSymbol parameter,
        string attributeName,
        out bool value)
    {
        if (TryGetBooleanAttributeValue(parameter.GetAttributes(), attributeName, out value)) return true;

        if (!SymbolEqualityComparer.Default.Equals(parameter, parameter.OriginalDefinition) &&
            TryGetBooleanAttributeValue(parameter.OriginalDefinition.GetAttributes(), attributeName, out value))
            return true;

        value = false;
        return false;
    }

    private static bool PropertyHasAttribute(IPropertySymbol property, string attributeName)
    {
        if (HasAttribute(property.GetAttributes(), attributeName) ||
            HasAttribute(
                property.GetMethod?.GetReturnTypeAttributes() ?? ImmutableArray<AttributeData>.Empty,
                attributeName))
            return true;

        return !SymbolEqualityComparer.Default.Equals(property, property.OriginalDefinition) &&
               (HasAttribute(property.OriginalDefinition.GetAttributes(), attributeName) ||
                HasAttribute(
                    property.OriginalDefinition.GetMethod?.GetReturnTypeAttributes() ??
                    ImmutableArray<AttributeData>.Empty,
                    attributeName));
    }

    private static bool FieldHasAttribute(IFieldSymbol field, string attributeName)
    {
        return HasAttribute(field.GetAttributes(), attributeName) ||
               (!SymbolEqualityComparer.Default.Equals(field, field.OriginalDefinition) &&
                HasAttribute(field.OriginalDefinition.GetAttributes(), attributeName));
    }

    private static bool HasAttribute(ImmutableArray<AttributeData> attributes, string attributeName)
    {
        return attributes.Any(attribute =>
            string.Equals(
                SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                attributeName,
                StringComparison.Ordinal));
    }

    private static bool TryGetBooleanAttributeValue(
        ImmutableArray<AttributeData> attributes,
        string attributeName,
        out bool value)
    {
        foreach (var attribute in attributes)
        {
            if (!string.Equals(
                    SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                    attributeName,
                    StringComparison.Ordinal) ||
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
        ImmutableArray<AttributeData> attributes,
        string attributeName,
        bool? methodReturnValue,
        ICollection<string> targets)
    {
        foreach (var attribute in attributes)
        {
            if (!string.Equals(
                    SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                    attributeName,
                    StringComparison.Ordinal))
                continue;

            var startIndex = 0;
            if (methodReturnValue.HasValue)
            {
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

    private static void AddMemberTarget(TypedConstant argument, ICollection<string> targets)
    {
        if (argument.Kind == TypedConstantKind.Array)
        {
            foreach (var item in argument.Values) AddMemberTarget(item, targets);

            return;
        }

        if (argument.Value is string target && !string.IsNullOrWhiteSpace(target)) targets.Add(target);
    }

    private static string? NormalizeMemberTarget(string target)
    {
        target = target.Trim();
        if (target.StartsWith("this.", StringComparison.Ordinal)) target = target.Substring("this.".Length);

        return target.Length != 0 && target.IndexOf(".", StringComparison.Ordinal) < 0
            ? target
            : null;
    }

    private static bool TryGetNotNullIfNotNullParameterName(
        ImmutableArray<AttributeData> attributes,
        out string parameterName)
    {
        foreach (var attribute in attributes)
        {
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
