using System.Reflection;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

internal static class SchemaModelTestHelpers
{
    private static readonly NullabilityInfoContext s_nullability = new();

    internal static void AssertJsonPropertyOrder(
        JsonDocument wire,
        IEnumerable<JsonElement> specifications,
        string context)
    {
        Assert.That(
            wire.RootElement.EnumerateObject().Select(static property =>
                property.Name),
            Is.EqualTo(specifications.Select(static specification =>
                specification.GetProperty("jsonName").GetString())),
            context);
    }

    internal static string SchemaType(
        PropertyInfo property,
        Func<Type, string?>? specialCase = null)
    {
        return SchemaType(
            property.PropertyType,
            s_nullability.Create(property),
            specialCase);
    }

    private static string SchemaType(
        Type type,
        NullabilityInfo? nullability,
        Func<Type, string?>? specialCase)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return SchemaType(underlying, null, specialCase) + "?";
        }

        var specialized = specialCase?.Invoke(type);
        if (specialized != null)
        {
            return specialized;
        }

        if (type.IsArray)
        {
            return SchemaType(type.GetElementType()!, nullability?.ElementType,
                specialCase) + "[]";
        }

        var name = type == typeof(string)
            ? "string"
            : type == typeof(bool)
                ? "bool"
                : type == typeof(int)
                    ? "int"
                    : type == typeof(uint)
                        ? "uint"
                        : type == typeof(long)
                            ? "long"
                            : type.IsGenericType
                                ? type.Name[..type.Name.IndexOf(
                                      '`',
                                      StringComparison.Ordinal)] + "<" +
                                  string.Join(
                                      ", ",
                                      type.GetGenericArguments()
                                          .Select((argument, index) =>
                                              SchemaType(
                                                  argument,
                                                  nullability?.GenericTypeArguments[
                                                      index],
                                                  specialCase))) +
                                  ">"
                                : type.Name;
        return !type.IsValueType &&
               nullability?.ReadState == NullabilityState.Nullable
            ? name + "?"
            : name;
    }
}
