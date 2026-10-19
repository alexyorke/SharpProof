using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class CSharpTypes
{
    public static HmType Erase(ITypeSymbol type)
    {
        if (type is null)
        {
            throw new global::System.ArgumentNullException(nameof(type));
        }

        return Convert(type, true, null);
    }

    public static HmType Rigid(ITypeSymbol type, Dictionary<ITypeParameterSymbol, HmType>? variables = null)
    {
        if (type is null)
        {
            throw new global::System.ArgumentNullException(nameof(type));
        }

        return Convert(type, false, variables);
    }

    private static HmType Convert(ITypeSymbol type, bool erase, Dictionary<ITypeParameterSymbol, HmType>? variables)
    {
        if (type is ITypeParameterSymbol parameter && variables is not null && variables.TryGetValue(parameter, out var variable))
        {
            return variable;
        }

        if (type is ITypeParameterSymbol)
        {
            return new(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (type is IArrayTypeSymbol array)
        {
            return new("[" + new string(',', array.Rank - 1) + "]", Convert(array.ElementType, erase, variables));
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

                    return Convert(e.Type, erase, variables);
                }).ToArray());
            }

            if (named.Name == "Func" && named.ContainingNamespace.ToDisplayString() == "System")
            {
                return new("Func", named.TypeArguments.Select(t =>
                {
                    if (t is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(t));
                    }

                    return Convert(t, erase, variables);
                }).ToArray());
            }

            if (named.IsAnonymousType || named.IsUnboundGenericType || named.IsRefLikeType)
            {
                throw new InferenceException($"Unsupported type: {type.ToDisplayString()}.");
            }

            if (named.IsGenericType)
            {
                if (named.ContainingType is not null)
                {
                    throw new InferenceException("Nested generic types are unsupported.");
                }

                var prefix = named.ContainingNamespace.IsGlobalNamespace ? "global::" : "global::" + named.ContainingNamespace.ToDisplayString() + ".";
                return new(prefix + Identifier(named.Name), named.TypeArguments.Select(t =>
                {
                    if (t is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(t));
                    }

                    return Convert(t, erase, variables);
                }).ToArray());
            }

            if (named.SpecialType == SpecialType.System_Void)
            {
                return new("void");
            }

            return erase ? HmType.Variable() : new(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
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

    public static string Render(HmType inferred, ITypeSymbol declared, Dictionary<HmType, string> variables)
    {
        if (variables is null)
        {
            throw new global::System.ArgumentNullException(nameof(variables));
        }

        inferred = inferred.Root;
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

                return Render(inferred.Arguments[i], field.Type, variables) + (field.IsImplicitlyDeclared ? "" : " " + Identifier(field.Name));
            })) + ")";
        }

        if (inferred.Name.StartsWith("[", StringComparison.Ordinal) && declared is IArrayTypeSymbol array)
        {
            return Render(inferred.Arguments[0], array.ElementType, variables) + inferred.Name;
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

                return Render(t, named.TypeArguments[i], variables);
            })) + ">";
        }

        return inferred.Format(variables);
    }
}
