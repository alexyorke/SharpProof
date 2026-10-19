using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static partial class CSharpTypes
{
    internal const string NullableParameter = "@nullable-parameter";
    internal const string NullableReference = "@nullable-reference";
    internal const string NullableValue = "global::System.Nullable";

    public static string Display(ITypeSymbol type)
    {
        if (type is null)
        { throw new ArgumentNullException(nameof(type)); }
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
    }

    public static HmType Erase(ITypeSymbol type, bool allowRefLike = true)
    {
        if (type is null)
        {
            throw new global::System.ArgumentNullException(nameof(type));
        }

        return Convert(type, true, null, allowRefLike);
    }

    public static HmType Rigid(ITypeSymbol type, Dictionary<ITypeParameterSymbol, HmType>? variables = null)
    {
        if (type is null)
        {
            throw new global::System.ArgumentNullException(nameof(type));
        }

        return Convert(type, false, variables);
    }

    private static HmType Convert(ITypeSymbol type, bool erase, Dictionary<ITypeParameterSymbol, HmType>? variables, bool allowRefLike = true)
    {
        if (type is null)
        { throw new ArgumentNullException(nameof(type)); }
        var shape = ConvertCore(type, erase, variables, allowRefLike);
        // Erased variables range over both domains. Concrete constructors retain
        // their domain so an instantiated unconstrained T? never renders int?.
        if (!shape.Root.IsVariable)
        { shape.IsValueType ??= type.IsValueType ? true : type.IsReferenceType ? false : null; }
        return shape;
    }

    private static HmType ConvertCore(ITypeSymbol type, bool erase, Dictionary<ITypeParameterSymbol, HmType>? variables, bool allowRefLike)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated && type.IsReferenceType)
        {
            return new(NullableReference, Convert(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), erase, variables, allowRefLike));
        }
        if (type is ITypeParameterSymbol { NullableAnnotation: NullableAnnotation.Annotated } nullableParameter)
        {
            return new(NullableParameter, variables is not null && variables.TryGetValue(nullableParameter, out var mapped)
                ? mapped : new HmType(Display(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated))));
        }
        if (type is ITypeParameterSymbol parameter && variables is not null && variables.TryGetValue(parameter, out var variable))
        {
            return variable;
        }

        if (type is ITypeParameterSymbol)
        {
            return new(Display(type));
        }

        if (type is IArrayTypeSymbol array)
        {
            return new("[" + new string(',', array.Rank - 1) + "]", Convert(array.ElementType, erase, variables, allowRefLike));
        }

        if (type is INamedTypeSymbol named)
        {
            if (named.IsTupleType)
            {
                return new("Tuple", named.TupleElements.Select(e =>
                {
                    if (e is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(e));
                    }

                    return Convert(e.Type, erase, variables, allowRefLike);
                }).ToArray());
            }

            if (named.Name == "Func" && named.ContainingType is null && named.ContainingNamespace.ToDisplayString() == "System")
            {
                return new("Func", named.TypeArguments.Select(t =>
                {
                    if (t is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(t));
                    }

                    return Convert(t, erase, variables, allowRefLike);
                }).ToArray());
            }

            if (named.IsAnonymousType || named.IsUnboundGenericType)
            {
                throw new InferenceException($"Unsupported type: {type.ToDisplayString()}.");
            }

            if (named.IsGenericType)
            {
                if (named.ContainingType is not null)
                {
                    return NestedShape(named, erase, variables, allowRefLike);
                }

                var prefix = named.ContainingNamespace.IsGlobalNamespace ? "global::" : "global::" + named.ContainingNamespace.ToDisplayString() + ".";
                return new(prefix + Identifier(named.Name), named.TypeArguments.Select(t =>
                {
                    if (t is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(t));
                    }

                    return Convert(t, erase, variables, allowRefLike);
                }).ToArray());
            }

            if (named.SpecialType == SpecialType.System_Void)
            {
                return new("void");
            }

            if (named.IsRefLikeType && erase)
            { return allowRefLike ? new HmType("?") { NeedsRefLikePermission = true } : new HmType(Display(type)); }
            return erase ? HmType.Variable() : new(Display(type));
        }

        throw new InferenceException($"Unsupported type: {type.ToDisplayString()}.");
    }

    private static string Identifier(string name)
    {
        if (name is null)
        {
            throw new global::System.ArgumentNullException(nameof(name));
        }

        return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
    }

    public static string Render(HmType inferred, ITypeSymbol declared, Dictionary<HmType, string> variables, bool nullableAnnotations = true)
    {
        if (variables is null)
        {
            throw new global::System.ArgumentNullException(nameof(variables));
        }

        inferred = inferred.Root;
        var annotate = nullableAnnotations || declared.NullableAnnotation == NullableAnnotation.Annotated
            && (declared.IsReferenceType || declared is ITypeParameterSymbol);
        if (declared is ITypeParameterSymbol { NullableAnnotation: NullableAnnotation.Annotated }
            && inferred.Name == Display(declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated)))
        { return Display(annotate ? declared : declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated)); }
        if (inferred.Name == NullableParameter)
        {
            var inner = inferred.Arguments[0].Root;
            var rendered = Render(inner, declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated), variables, nullableAnnotations);
            return !annotate || inner.IsValueType == true || inner.Name is NullableParameter or NullableReference or NullableValue ? rendered : rendered + "?";
        }
        if (inferred.Name == NullableReference)
        {
            var inner = inferred.Arguments[0].Root;
            var rendered = Render(inner, declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated), variables, nullableAnnotations);
            return !annotate || inner.Name is NullableReference or NullableParameter ? rendered : rendered + "?";
        }
        if (inferred.Name == NullableValue && declared is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            return Render(inferred.Arguments[0], nullable.TypeArguments[0], variables, nullableAnnotations) + "?";
        }
        if (inferred.Name == "Tuple" && declared is INamedTypeSymbol { IsTupleType: true } tuple && tuple.TupleElements.Length == inferred.Arguments.Length)
        {
            return "(" + string.Join(", ", tuple.TupleElements.Select((field, i) =>
            {
                if (field is null)
                {
                    throw new global::System.ArgumentNullException(nameof(field));
                }

                if (i < 0)
                {
                    throw new global::System.ArgumentOutOfRangeException(nameof(i));
                }

                return Render(inferred.Arguments[i], field.Type, variables, nullableAnnotations) + (field.IsImplicitlyDeclared ? "" : " " + Identifier(field.Name));
            })) + ")";
        }

        if (inferred.Name.StartsWith("[", StringComparison.Ordinal) && declared is IArrayTypeSymbol array)
        {
            return Render(inferred.Arguments[0], array.ElementType, variables, nullableAnnotations) + inferred.Name;
        }

        if (inferred.NestedTypeName is not null && declared is INamedTypeSymbol { ContainingType: not null } nested)
        {
            var own = OwnArguments(inferred);
            var result = Render(inferred.Arguments[0], nested.ContainingType, variables, nullableAnnotations) + "." + inferred.NestedTypeName;
            return own.Length == 0 ? result : result + "<" + string.Join(", ", own.Select((argument, i) =>
            { if (argument is null) { throw new ArgumentNullException(nameof(argument)); } if (i < 0) { throw new ArgumentOutOfRangeException(nameof(i)); } return Render(argument, nested.TypeArguments[i], variables, nullableAnnotations); })) + ">";
        }

        if (inferred.Arguments.Length > 0 && declared is INamedTypeSymbol named && named.TypeArguments.Length == inferred.Arguments.Length)
        {
            var name = inferred.Name == "Func" ? "global::System.Func" : inferred.Name;
            return name + "<" + string.Join(", ", inferred.Arguments.Select((t, i) =>
            {
                if (t is null)
                {
                    throw new global::System.ArgumentNullException(nameof(t));
                }

                if (i < 0)
                {
                    throw new global::System.ArgumentOutOfRangeException(nameof(i));
                }

                return Render(t, named.TypeArguments[i], variables, nullableAnnotations);
            })) + ">";
        }

        return inferred.Format(variables, nullableAnnotations);
    }
}
