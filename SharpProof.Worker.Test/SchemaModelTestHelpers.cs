using System.Globalization;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

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

    internal static void AssertConstants(
        Type type,
        JsonElement declaration,
        BindingFlags visibility,
        bool optional = false)
    {
        if (!declaration.TryGetProperty("constants", out var constants))
        {
            if (optional)
            {
                return;
            }

            Assert.Fail($"{type.Name} does not declare constants.");
        }

        var specifications = constants.EnumerateArray().ToArray();
        var fields = type.GetFields(
                visibility |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Where(static field => field.IsLiteral)
            .OrderBy(static field => field.MetadataToken)
            .ToArray();
        Assert.That(
            fields.Select(static field => field.Name),
            Is.EqualTo(specifications.Select(static specification =>
                specification.GetProperty("name").GetString())),
            type.Name);
        for (var index = 0; index < fields.Length; index++)
        {
            var expected = specifications[index].GetProperty("value");
            var actual = fields[index].GetRawConstantValue();
            if (expected.ValueKind == JsonValueKind.String)
            {
                Assert.That(actual, Is.EqualTo(expected.GetString()), fields[index].Name);
            }
            else
            {
                Assert.That(
                    Convert.ToInt64(actual, CultureInfo.InvariantCulture),
                    Is.EqualTo(expected.GetInt64()),
                    fields[index].Name);
            }
        }
    }

    internal static void AssertEnum(
        Type type,
        JsonElement declaration,
        bool validateWireNames = false)
    {
        Assert.That(type.IsEnum, Is.True, type.Name);
        if (declaration.TryGetProperty("underlyingType", out var underlying))
        {
            Assert.That(
                Enum.GetUnderlyingType(type),
                Is.EqualTo(underlying.GetString() == "long"
                    ? typeof(long)
                    : typeof(int)),
                type.Name);
        }

        if (declaration.TryGetProperty("flags", out var flags))
        {
            Assert.That(
                type.IsDefined(typeof(FlagsAttribute), inherit: false),
                Is.EqualTo(flags.GetBoolean()),
                type.Name);
        }

        var members = declaration.GetProperty("members")
            .EnumerateArray()
            .ToArray();
        Assert.That(
            Enum.GetNames(type),
            Is.EqualTo(members.Select(static member =>
                member.GetProperty("name").GetString())),
            type.Name);
        Assert.That(
            Enum.GetValues(type).Cast<object>().Select(static value =>
                Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            Is.EqualTo(members.Select(static member =>
                member.GetProperty("value").GetInt64())),
            type.Name);
        if (!validateWireNames)
        {
            return;
        }

        foreach (var member in members)
        {
            var name = member.GetProperty("name").GetString()!;
            var value = Enum.Parse(type, name);
            Assert.That(
                JsonSerializer.Serialize(
                    value,
                    type,
                    WorkerProtocolJson.Options),
                Is.EqualTo(JsonSerializer.Serialize(name)),
                $"{type.Name}.{name}");
        }
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

    internal static object? ReplacementValue(
        Type type,
        object? current,
        Func<Type, object> createObject,
        bool includeUnsignedInteger = false)
    {
        if (type == typeof(string))
        {
            return "changed";
        }

        if (type == typeof(bool))
        {
            return !(bool)current!;
        }

        if (type == typeof(int))
        {
            return 17;
        }

        if (includeUnsignedInteger && type == typeof(uint))
        {
            return 17U;
        }

        if (type == typeof(long))
        {
            return 17L;
        }

        if (Nullable.GetUnderlyingType(type) is { } nullable)
        {
            return nullable == typeof(long)
                ? 17L
                : Activator.CreateInstance(nullable);
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).GetValue(Enum.GetValues(type).Length - 1);
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var value = elementType == typeof(string)
                ? "item"
                : elementType.IsEnum
                    ? Enum.GetValues(elementType).GetValue(0)
                    : elementType.IsValueType
                        ? Activator.CreateInstance(elementType)
                        : createObject(elementType);
            var result = Array.CreateInstance(elementType, 1);
            result.SetValue(value, 0);
            return result;
        }

        return createObject(type);
    }
}
