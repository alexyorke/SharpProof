using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.HmInference;

internal static partial class CSharpTypes
{
    internal const string Pointer = "@pointer";
    internal const string NullableParameter = "@nullable-parameter";
    internal const string NullableReference = "@nullable-reference";
    internal const string NullableValue = "global::System.Nullable";

    internal static string FunctionPointerName(IMethodSymbol signature)
    {
        if (signature is null)
        { throw new ArgumentNullException(nameof(signature)); }
        return "@function:" + signature.CallingConvention + ":" + signature.RefKind + ":"
            + string.Join(",", signature.Parameters.Select(parameter =>
            { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return parameter.RefKind.ToString(); }))
            + ":" + string.Join(",", signature.UnmanagedCallingConventionTypes.Select(type =>
            { if (type is null) { throw new ArgumentNullException(nameof(type)); } return ConstructorIdentity(type); }).OrderBy(identity =>
            { if (identity is null) { throw new ArgumentNullException(nameof(identity)); } return identity; }, StringComparer.Ordinal));
    }

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
        if (!shape.IsVariable && shape.Name is not (NullableReference or NullableParameter)
            && !(type is ITypeParameterSymbol mappedParameter && variables is not null && variables.ContainsKey(mappedParameter)))
        { shape.CSharpSymbol ??= type; }
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
                ? mapped : Convert(type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), false, null));
        }
        if (type is ITypeParameterSymbol parameter && variables is not null && variables.TryGetValue(parameter, out var variable))
        {
            return variable;
        }

        if (type is ITypeParameterSymbol)
        {
            return new(Display(type));
        }

        if (type is IFunctionPointerTypeSymbol functionPointer)
        {
            var signature = functionPointer.Signature;
            var components = signature.Parameters.Select(parameter =>
            { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } return Convert(parameter.Type, erase, variables, allowRefLike); }).ToList();
            components.Add(Convert(signature.ReturnType, erase, variables, allowRefLike));
            return new(FunctionPointerName(signature), components.ToArray());
        }
        if (type is IPointerTypeSymbol pointer)
        {
            // Managed pointees are a warning-bearing C# extension. Retain their
            // existing domain rather than adding an invalid unmanaged constraint.
            return new(Pointer, Convert(pointer.PointedAtType,
                erase && pointer.PointedAtType.IsUnmanagedType, variables, allowRefLike));
        }
        if (type is IArrayTypeSymbol array)
        {
            return new("[" + new string(',', array.Rank - 1) + "]", Convert(array.ElementType, erase, variables, allowRefLike));
        }

        if (type is IDynamicTypeSymbol)
        { return new("dynamic"); }
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

            if (named.IsAnonymousType)
            {
                return new(AnonymousName(named), named.GetMembers().OfType<IPropertySymbol>().Select(property =>
                { if (property is null) { throw new ArgumentNullException(nameof(property)); } return Convert(property.Type, erase, variables, allowRefLike); }).ToArray());
            }
            if (named.IsUnboundGenericType)
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

    public static string Render(HmType inferred, ITypeSymbol declared, Dictionary<HmType, string> variables, bool nullableAnnotations, SemanticModel? model, int position, bool preserveDeclaredAnnotations, CancellationToken cancellationToken)
    {
        if (variables is null)
        {
            throw new global::System.ArgumentNullException(nameof(variables));
        }

        cancellationToken.ThrowIfCancellationRequested();
        inferred = inferred.Root;
        if (inferred.CSharpSymbol is INamedTypeSymbol { IsAnonymousType: true })
        { throw new InferenceException("An anonymous type cannot appear in the suggested signature."); }
        // A narrowed result can have a different constructor than its declaration.
        if (inferred.CSharpSymbol is { } inferredSymbol && ConstructorIdentity(inferredSymbol) != ConstructorIdentity(declared))
        { declared = inferredSymbol; }
        var annotate = nullableAnnotations || preserveDeclaredAnnotations && declared.NullableAnnotation == NullableAnnotation.Annotated
            && (declared.IsReferenceType || declared is ITypeParameterSymbol);
        if (declared is ITypeParameterSymbol { NullableAnnotation: NullableAnnotation.Annotated }
            && inferred.Name == Display(declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated)))
        { return Display(annotate ? declared : declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated)); }
        if (inferred.Name == NullableParameter)
        {
            var inner = inferred.Arguments[0].Root;
            var rendered = Render(inner, declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated), variables, nullableAnnotations, model, position, preserveDeclaredAnnotations, cancellationToken);
            return !annotate || inner.IsValueType == true || inner.Name is NullableParameter or NullableReference or NullableValue ? rendered : rendered + "?";
        }
        if (inferred.Name == NullableReference)
        {
            var inner = inferred.Arguments[0].Root;
            var rendered = Render(inner, declared.WithNullableAnnotation(NullableAnnotation.NotAnnotated), variables, nullableAnnotations, model, position, preserveDeclaredAnnotations, cancellationToken);
            return !annotate || inner.Name is NullableReference or NullableParameter ? rendered : rendered + "?";
        }
        if (inferred.Name == NullableValue && declared is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            return Render(inferred.Arguments[0], nullable.TypeArguments[0], variables, nullableAnnotations, model, position, preserveDeclaredAnnotations, cancellationToken) + "?";
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

                return Render(inferred.Arguments[i], field.Type, variables, nullableAnnotations, model, position, preserveDeclaredAnnotations, cancellationToken) + (field.IsImplicitlyDeclared ? "" : " " + Identifier(field.Name));
            })) + ")";
        }

        if (declared is IFunctionPointerTypeSymbol functionPointer && inferred.Name == FunctionPointerName(functionPointer.Signature))
        {
            var syntax = (FunctionPointerTypeSyntax)SyntaxFactory.ParseTypeName(Display(declared));
            var parameters = syntax.ParameterList.Parameters;
            var rendered = new List<string>();
            for (var i = 0; i < parameters.Count; i++)
            {
                var original = i == functionPointer.Signature.Parameters.Length ? functionPointer.Signature.ReturnType : functionPointer.Signature.Parameters[i].Type;
                var modifiers = string.Join(" ", parameters[i].Modifiers.Select(token =>
                { if (token.RawKind == 0) { throw new ArgumentException("Expected a function pointer modifier.", nameof(token)); } return token.ValueText; }));
                rendered.Add((modifiers.Length == 0 ? "" : modifiers + " ")
                    + Render(inferred.Arguments[i], original, variables, nullableAnnotations, model, position, preserveDeclaredAnnotations, cancellationToken));
            }
            return "delegate*" + (syntax.CallingConvention is null ? "" : " " + syntax.CallingConvention.NormalizeWhitespace().ToFullString())
                + "<" + string.Join(", ", rendered) + ">";
        }
        if (inferred.Name == Pointer && declared is IPointerTypeSymbol pointer)
        {
            return Render(inferred.Arguments[0], pointer.PointedAtType, variables,
                nullableAnnotations, model, position, preserveDeclaredAnnotations, cancellationToken) + "*";
        }
        if (inferred.Name.StartsWith("[", StringComparison.Ordinal) && declared is IArrayTypeSymbol array)
        {
            return Render(inferred.Arguments[0], array.ElementType, variables, nullableAnnotations, model, position, preserveDeclaredAnnotations, cancellationToken) + inferred.Name;
        }

        if (inferred.NestedTypeName is not null && declared is INamedTypeSymbol { ContainingType: not null } nested)
        {
            var own = OwnArguments(inferred);
            var spelling = SyntaxFactory.ParseTypeName(BoundSpelling(nested, model, position, false, cancellationToken));
            var result = spelling is SimpleNameSyntax ? inferred.NestedTypeName
                : Render(inferred.Arguments[0], nested.ContainingType, variables, nullableAnnotations, model, position, preserveDeclaredAnnotations, cancellationToken) + "." + inferred.NestedTypeName;
            return own.Length == 0 ? result : result + "<" + string.Join(", ", own.Select((argument, i) =>
            { if (argument is null) { throw new ArgumentNullException(nameof(argument)); } if (i < 0) { throw new ArgumentOutOfRangeException(nameof(i)); } return Render(argument, nested.TypeArguments[i], variables, nullableAnnotations, model, position, preserveDeclaredAnnotations, cancellationToken); })) + ">";
        }

        if (inferred.Arguments.Length > 0 && declared is INamedTypeSymbol named && named.TypeArguments.Length == inferred.Arguments.Length)
        {
            var spelling = BoundSpelling(named, model, position, true, cancellationToken);
            var argumentStart = spelling.IndexOf('<');
            if (named.ContainingType is null && argumentStart < 0)
            { throw new InferenceException("A closed type alias cannot express the inferred generic arguments."); }
            var name = named.ContainingType is null ? spelling.Substring(0, argumentStart)
                : inferred.Name == "Func" ? "global::System.Func" : inferred.Name;
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

                return Render(t, named.TypeArguments[i], variables, nullableAnnotations, model, position, preserveDeclaredAnnotations, cancellationToken);
            })) + ">";
        }

        if (inferred.CSharpSymbol is INamedTypeSymbol { Arity: 0 } nominal && inferred.Arguments.Length == 0)
        { return BoundSpelling(nominal, model, position, false, cancellationToken); }
        return inferred.Format(variables, nullableAnnotations);
    }
}
