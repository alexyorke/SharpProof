using Microsoft.CodeAnalysis;

namespace SharpProof.HmInference;

internal static partial class CSharpTypes
{
    private static HmType NestedShape(INamedTypeSymbol type, bool erase, Dictionary<ITypeParameterSymbol, HmType>? variables, bool allowRefLike)
    {
        if (type is null)
        { throw new ArgumentNullException(nameof(type)); }
        var owner = type.ContainingType ?? throw new InferenceException("Missing nested type owner.");
        var enclosing = Convert(owner, erase && owner.IsGenericType, variables, allowRefLike);
        var arguments = new List<HmType> { enclosing };
        foreach (var argument in type.TypeArguments)
        { arguments.Add(Convert(argument, erase, variables, allowRefLike)); }
        return new HmType(Display(type.OriginalDefinition), arguments.ToArray()) { NestedTypeName = Identifier(type.Name) };
    }

    internal static bool MapNamedArguments(INamedTypeSymbol type, HmType shape, Dictionary<ITypeParameterSymbol, HmType> mapping)
    {
        if (type is null)
        { throw new ArgumentNullException(nameof(type)); }
        if (shape is null)
        { throw new ArgumentNullException(nameof(shape)); }
        if (mapping is null)
        { throw new ArgumentNullException(nameof(mapping)); }
        shape = shape.Root;
        while (shape.Name is NullableReference or NullableParameter)
        { shape = shape.Arguments[0].Root; }
        var offset = 0;
        if (shape.NestedTypeName is not null)
        {
            if (type.ContainingType is null || shape.NestedTypeName != Identifier(type.Name) || shape.Arguments.Length == 0
                || !MapNamedArguments(type.ContainingType, shape.Arguments[0], mapping))
            { return false; }
            offset = 1;
        }
        else if (type.ContainingType is { IsGenericType: true })
        { return false; }
        if (shape.Arguments.Length - offset != type.TypeArguments.Length)
        { return false; }
        for (var i = 0; i < type.TypeArguments.Length; i++)
        { mapping[type.OriginalDefinition.TypeParameters[i]] = shape.Arguments[i + offset]; }
        return true;
    }

    internal static HmType[] OwnArguments(HmType type)
    {
        if (type is null)
        { throw new ArgumentNullException(nameof(type)); }
        type = type.Root;
        return type.Arguments.Skip(type.NestedTypeName is null ? 0 : 1).ToArray();
    }

    internal static HmType ReplaceOwnArguments(HmType template, params HmType[] arguments)
    {
        if (template is null)
        { throw new ArgumentNullException(nameof(template)); }
        if (arguments is null)
        { throw new ArgumentNullException(nameof(arguments)); }
        template = template.Root;
        return new HmType(template.Name, template.NestedTypeName is null ? arguments : new[] { template.Arguments[0] }.Concat(arguments).ToArray())
        { NestedTypeName = template.NestedTypeName, IsValueType = template.IsValueType };
    }
}
