using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ProtocolModelSchemaTests
{
    private static readonly Assembly s_protocolAssembly =
        typeof(WorkerVerifyRequest).Assembly;
    private static readonly Type s_protocolMetadata = s_protocolAssembly.GetType(
        "SharpProof.Worker.Protocol.WorkerProtocolMetadata",
        throwOnError: true)!;
    private static readonly string[] s_manifestIdentityCollections = [
        "Callables",
        "Claims"
    ];

    [Test]
    public void SchemaPinsTheReleasedWireVersions()
    {
        using var schema = ReadSchema();
        var versions = schema.RootElement.GetProperty("versionMembers");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ResolveMemberValue(versions.GetProperty("protocol").GetString()!),
                Is.EqualTo("11"));
            Assert.That(
                ResolveMemberValue(versions.GetProperty("manifest").GetString()!),
                Is.EqualTo(4));
            Assert.That(
                ResolveMemberValue(versions.GetProperty("cache").GetString()!),
                Is.EqualTo(13));
            Assert.That(WorkerProtocolVersions.Current, Is.EqualTo("11"));
            Assert.That(WorkerManifestVersions.Current, Is.EqualTo(4));
            Assert.That(WorkerCacheVersions.Current, Is.EqualTo(13));
        }
    }

    [Test]
    public void GeneratedDeclarationsMatchSchemaExactly()
    {
        using var schema = ReadSchema();
        foreach (var declaration in schema.RootElement
                     .GetProperty("declarations")
                     .EnumerateArray())
        {
            var name = declaration.GetProperty("name").GetString()!;
            var type = s_protocolAssembly.GetType(
                "SharpProof.Worker.Protocol." + name,
                throwOnError: true)!;
            switch (declaration.GetProperty("kind").GetString())
            {
                case "staticClass":
                    Assert.That(type.IsAbstract && type.IsSealed, Is.True, name);
                    SchemaModelTestHelpers.AssertConstants(
                        type,
                        declaration,
                        BindingFlags.Public,
                        optional: true);
                    break;
                case "enum":
                    SchemaModelTestHelpers.AssertEnum(
                        type,
                        declaration,
                        validateWireNames: true);
                    break;
                case "class":
                    Assert.That(type.IsSealed, Is.True, name);
                    SchemaModelTestHelpers.AssertConstants(
                        type,
                        declaration,
                        BindingFlags.Public,
                        optional: true);
                    AssertProperties(type, declaration);
                    break;
                default:
                    Assert.Fail($"Unknown declaration kind for {name}.");
                    break;
            }
        }
    }

    [Test]
    public void GeneratedRuntimeMetadataMatchesSchemaExactly()
    {
        using var schema = ReadSchema();
        AssertDefinedEnums(schema.RootElement);
        AssertManifestNames(schema.RootElement);
        AssertValidationPlans(schema.RootElement);
    }

    [Test]
    public void GeneratedManifestIdentityCatalogIsCompleteAndOrdered()
    {
        var rootFields = WorkerManifestIdentityCatalog.RootFields;
        var collections = WorkerManifestIdentityCatalog.Collections;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootFields, Has.Length.EqualTo(1));
            Assert.That(collections, Has.Length.EqualTo(2));
            Assert.That(
                (rootFields[0].Label, rootFields[0].Property,
                    rootFields[0].Kind, rootFields[0].DefaultMember),
                Is.EqualTo((
                    "manifest.schemaVersion", "SchemaVersion",
                    WorkerManifestIdentityFieldKind.Int, (string?)null)));
            Assert.That(
                collections.Select(static collection => collection.Property).ToArray(),
                Is.EqualTo(s_manifestIdentityCollections));
        }

        foreach (var collection in collections)
        {
            Assert.That(collection.LengthLabel, Does.StartWith("manifest."));
            Assert.That(collection.EntryLabel, Does.Not.EndWith("."));
            Assert.That(collection.Order, Is.Not.Empty);
            Assert.That(collection.Fields, Is.Not.Empty);
            foreach (var order in collection.Order)
            {
                Assert.That(order.Property, Is.Not.Empty);
                Assert.That(order.Kind, Is.Not.Empty);
            }

            foreach (var field in collection.Fields)
            {
                Assert.That(field.Label, Is.Not.Empty);
                Assert.That(field.Property, Is.Not.Empty);
                Assert.That(
                    Enum.IsDefined(field.Kind),
                    Is.True);
                _ = field.DefaultMember;
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                WorkerProtocolMetadata.GetAssumptionOrder(
                    WorkerAssumptionKind.Unspecified),
                Is.EqualTo(0));
            Assert.That(
                WorkerProtocolMetadata.GetAssumptionOrder(
                    WorkerAssumptionKind.Precondition),
                Is.EqualTo(1));
            Assert.That(
                WorkerProtocolMetadata.GetAssumptionOrder(
                    WorkerAssumptionKind.UserAssume),
                Is.EqualTo(2));
            Assert.That(
                WorkerProtocolMetadata.GetAssumptionOrder(
                    WorkerAssumptionKind.TrustedBoundary),
                Is.EqualTo(3));
            Assert.That(
                WorkerProtocolMetadata.GetAssumptionOrder(
                    WorkerAssumptionKind.ApiSpecification),
                Is.EqualTo(4));
            Assert.That(
                WorkerProtocolMetadata.GetAssumptionOrder(
                    WorkerAssumptionKind.SourceDomain),
                Is.EqualTo(5));
            Assert.That(
                WorkerProtocolMetadata.GetAssumptionOrder(
                    WorkerAssumptionKind.NormalCompletion),
                Is.EqualTo(6));
        }

        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            WorkerProtocolMetadata.GetAssumptionOrder(
                (WorkerAssumptionKind)999)));
    }

    [Test]
    public void GeneratedValidationTablesMatchEveryDeclaredState()
    {
        using var schema = ReadSchema();
        foreach (var table in schema.RootElement
                     .GetProperty("validationTables")
                     .EnumerateArray())
        {
            var name = table.GetProperty("name").GetString()!;
            var method = s_protocolMetadata.GetMethod(
                "Matches" + name,
                BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var arguments in ValidationTableArguments(
                         table.GetProperty("parameters")))
            {
                Assert.That(
                    method.Invoke(null, arguments),
                    Is.EqualTo(TableContains(table, arguments)),
                    $"{name}({string.Join(", ", arguments)})");
            }
        }
    }

    private static void AssertDefinedEnums(JsonElement schema)
    {
        var defined = schema.GetProperty("definedEnums")
            .EnumerateArray()
            .Select(static value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var definition = s_protocolMetadata.GetMethod(
            "IsKnown",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var declaration in schema.GetProperty("declarations")
                     .EnumerateArray()
                     .Where(static value =>
                         value.GetProperty("kind").GetString() == "enum"))
        {
            var name = declaration.GetProperty("name").GetString()!;
            var type = ProtocolType(name);
            var typedDefinition = definition.MakeGenericMethod(type);
            foreach (var member in declaration.GetProperty("members").EnumerateArray())
            {
                var value = Enum.Parse(type, member.GetProperty("name").GetString()!);
                Assert.That(
                    typedDefinition.Invoke(null, [value]),
                    Is.EqualTo(defined.Contains(name)),
                    $"{name}.{value}");
            }
            var unknown = Enum.ToObject(
                type,
                Enum.GetUnderlyingType(type) == typeof(long)
                    ? long.MaxValue
                    : int.MaxValue);
            Assert.That(typedDefinition.Invoke(null, [unknown]), Is.EqualTo(false), name);
        }
    }

    private static void AssertManifestNames(JsonElement schema)
    {
        var included = schema.GetProperty("manifestNameEnums")
            .EnumerateArray()
            .Select(static value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var getName = s_protocolMetadata.GetMethod(
            "GetManifestName",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var declaration in schema.GetProperty("declarations")
                     .EnumerateArray()
                     .Where(static value =>
                         value.GetProperty("kind").GetString() == "enum"))
        {
            var name = declaration.GetProperty("name").GetString()!;
            var type = ProtocolType(name);
            foreach (var member in declaration.GetProperty("members").EnumerateArray())
            {
                var memberName = member.GetProperty("name").GetString()!;
                var value = Enum.Parse(type, memberName);
                Assert.That(
                    getName.Invoke(null, [value]),
                    Is.EqualTo(included.Contains(name) ? memberName : null),
                    $"{name}.{memberName}");
            }
        }
    }

    private static void AssertValidationPlans(JsonElement schema)
    {
        foreach (var plan in schema.GetProperty("validationPlans").EnumerateArray())
        {
            var name = plan.GetProperty("name").GetString()!;
            if (plan.TryGetProperty("mode", out var mode) &&
                mode.GetString() == "predicate")
            {
                Assert.That(
                    s_protocolMetadata.GetMethod(
                        "Is" + name + "Valid",
                        BindingFlags.NonPublic | BindingFlags.Static),
                    Is.Not.Null,
                    name);
                continue;
            }
            var rules = (Array)s_protocolMetadata.GetField(
                name + "Rules",
                BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var actual = rules.Cast<object>().Select(rule =>
                (string)rule.GetType().GetField(
                    "Code",
                    BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(rule)!);
            var expected = plan.GetProperty("rules").EnumerateArray()
                .Select(static rule => rule.GetProperty("code").GetString());
            Assert.That(actual, Is.EqualTo(expected), name);
        }
    }

    private static IEnumerable<object?[]> ValidationTableArguments(JsonElement parameters)
    {
        IEnumerable<object?[]> result = [[]];
        foreach (var parameter in parameters.EnumerateArray())
        {
            var typeName = parameter.GetProperty("type").GetString()!;
            object?[] values = typeName == "bool"
                ? [false, true]
                : [.. Enum.GetValues(ProtocolType(typeName)).Cast<object?>()];
            result = result.SelectMany(
                _ => values,
                static (prefix, value) => prefix.Append(value).ToArray());
        }
        return result;
    }

    private static bool TableContains(JsonElement table, object?[] arguments)
    {
        return table.GetProperty("rows").EnumerateArray().Any(row =>
            row.EnumerateArray().Select((value, index) =>
                    value.ValueKind == JsonValueKind.String && value.GetString() == "*" ||
                    value.ValueKind == JsonValueKind.String &&
                    value.GetString() == arguments[index]!.ToString() ||
                    value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                    value.GetBoolean() == (bool)arguments[index]!)
                .All(static value => value));
    }

    private static Type ProtocolType(string name)
    {
        return s_protocolAssembly.GetType(
            "SharpProof.Worker.Protocol." + name,
            throwOnError: true)!;
    }

    private static void AssertProperties(
        Type type,
        JsonElement declaration)
    {
        var specifications = declaration.GetProperty("properties")
            .EnumerateArray()
            .ToArray();
        var properties = type.GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly)
            .OrderBy(static property => property.MetadataToken)
            .ToArray();
        Assert.That(
            properties.Select(static property => property.Name),
            Is.EqualTo(specifications.Select(static specification =>
                specification.GetProperty("name").GetString())),
            type.Name);
        var instance = CreateInstance(type);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(
            instance,
            type,
            WorkerProtocolJson.Options));
        SchemaModelTestHelpers.AssertJsonPropertyOrder(
            wire,
            specifications,
            type.Name);

        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            var specification = specifications[index];
            Assert.That(
                specification.GetProperty("order").GetInt32(),
                Is.EqualTo(index),
                $"{type.Name}.{property.Name}");
            Assert.That(
                SchemaModelTestHelpers.SchemaType(property),
                Is.EqualTo(specification.GetProperty("type").GetString()),
                $"{type.Name}.{property.Name}");
            var expectsSetter = specification.GetProperty("set").GetBoolean();
            Assert.That(
                property.SetMethod?.IsPublic == true,
                Is.EqualTo(expectsSetter),
                $"{type.Name}.{property.Name}");
            var initial = property.GetValue(instance);
            AssertDefault(type, property, initial, specification);
            if (!expectsSetter)
            {
                continue;
            }

            var replacement = SchemaModelTestHelpers.ReplacementValue(
                property.PropertyType,
                initial,
                CreateInstance,
                includeUnsignedInteger: true);
            property.SetValue(instance, replacement);
            Assert.That(
                property.GetValue(instance),
                Is.EqualTo(replacement),
                $"{type.Name}.{property.Name}");
        }
    }

    private static void AssertDefault(
        Type declaringType,
        PropertyInfo property,
        object? actual,
        JsonElement specification)
    {
        var defaultValue = specification.GetProperty("default");
        var kind = defaultValue.GetProperty("kind").GetString();
        switch (kind)
        {
            case "implicit":
                Assert.That(
                    actual,
                    Is.EqualTo(property.PropertyType.IsValueType
                        ? Activator.CreateInstance(property.PropertyType)
                        : null),
                    $"{declaringType.Name}.{property.Name}");
                break;
            case "stringEmpty":
                Assert.That(actual, Is.EqualTo(string.Empty), property.Name);
                break;
            case "new":
                Assert.That(actual, Is.TypeOf(property.PropertyType), property.Name);
                break;
            case "emptyArray":
                Assert.That(actual, Is.InstanceOf<Array>(), property.Name);
                Assert.That(((Array)actual!).Length, Is.Zero, property.Name);
                break;
            case "true":
                Assert.That(actual, Is.EqualTo(true), property.Name);
                break;
            case "member":
                var expected = ResolveMemberValue(
                    defaultValue.GetProperty("value").GetString()!,
                    declaringType);
                if (property.PropertyType.IsEnum)
                {
                    expected = Enum.ToObject(property.PropertyType, expected!);
                }

                Assert.That(actual, Is.EqualTo(expected), property.Name);
                break;
            case "constructorAssigned":
                Assert.That(actual, Is.Not.Null, property.Name);
                Assert.That(
                    ((ImmutableArray<WorkerProtocolError>)actual!).IsEmpty,
                    Is.True,
                    property.Name);
                break;
            case "computed":
                Assert.That(actual, Is.EqualTo(true), property.Name);
                break;
            default:
                Assert.Fail(
                    $"Unknown default kind '{kind}' for " +
                    $"{declaringType.Name}.{property.Name}.");
                break;
        }
    }

    private static object CreateInstance(Type type)
    {
        if (type != typeof(WorkerProtocolValidationResult))
        {
            return Activator.CreateInstance(type)!;
        }

        return type.GetConstructors(
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Single()
            .Invoke([Array.Empty<WorkerProtocolError>()]);
    }

    private static object? ResolveMemberValue(
        string member,
        Type? declaringType = null)
    {
        var separator = member.LastIndexOf('.');
        var type = separator < 0
            ? declaringType!
            : s_protocolAssembly.GetType(
                "SharpProof.Worker.Protocol." + member[..separator],
                throwOnError: true)!;
        var name = separator < 0 ? member : member[(separator + 1)..];
        return type.GetField(
                name,
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)!
            .GetRawConstantValue();
    }

    private static JsonDocument ReadSchema()
    {
        return TestRepository.ReadSchema(
            "SharpProof.Worker.Protocol",
            "ProtocolModel.schema.json");
    }

}
