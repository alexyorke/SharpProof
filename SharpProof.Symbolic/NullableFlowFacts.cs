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

        expression = UnwrapParentheses(expression);
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

    internal static bool IsDefinitelyNotNullReferenceValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return GetExpressionState(expression, semanticModel, cancellationToken) == NullableFlowFactState.NotNull;
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

        var attributes = method.GetReturnTypeAttributes();
        var originalAttributes = SymbolEqualityComparer.Default.Equals(method, method.OriginalDefinition)
            ? ImmutableArray<AttributeData>.Empty
            : method.OriginalDefinition.GetReturnTypeAttributes();
        if (HasAttribute(attributes, MaybeNullAttributeName) ||
            HasAttribute(originalAttributes, MaybeNullAttributeName))
            return NullableFlowFactState.MaybeNull;

        if (HasAttribute(attributes, NotNullAttributeName) ||
            HasAttribute(originalAttributes, NotNullAttributeName))
            return NullableFlowFactState.NotNull;

        return FromAnnotation(method.ReturnNullableAnnotation);
    }

    internal static NullableFlowFactState GetPropertyReadState(IPropertySymbol property)
    {
        if (property == null) throw new ArgumentNullException(nameof(property));

        if (!SymbolicTypeFacts.IsReferenceLikeType(property.Type)) return NullableFlowFactState.Unknown;

        if (PropertyHasAttribute(property, MaybeNullAttributeName)) return NullableFlowFactState.MaybeNull;

        if (PropertyHasAttribute(property, NotNullAttributeName)) return NullableFlowFactState.NotNull;

        return FromAnnotation(property.NullableAnnotation);
    }

    internal static NullableFlowFactState GetFieldReadState(IFieldSymbol field)
    {
        if (field == null) throw new ArgumentNullException(nameof(field));

        if (!SymbolicTypeFacts.IsReferenceLikeType(field.Type)) return NullableFlowFactState.Unknown;

        if (FieldHasAttribute(field, MaybeNullAttributeName)) return NullableFlowFactState.MaybeNull;

        if (FieldHasAttribute(field, NotNullAttributeName)) return NullableFlowFactState.NotNull;

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

        return target.Length != 0 && !target.Contains(".", StringComparison.Ordinal)
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

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;

        return expression;
    }
}
