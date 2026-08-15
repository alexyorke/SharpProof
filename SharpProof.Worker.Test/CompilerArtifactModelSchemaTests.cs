using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Effects;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;
using AssemblyIdentityComparer = Microsoft.CodeAnalysis.AssemblyIdentityComparer;
using DesktopAssemblyIdentityComparer =
    Microsoft.CodeAnalysis.DesktopAssemblyIdentityComparer;
using MetadataImportOptions = Microsoft.CodeAnalysis.MetadataImportOptions;
using NullableContextOptions = Microsoft.CodeAnalysis.NullableContextOptions;
using OptimizationLevel = Microsoft.CodeAnalysis.OptimizationLevel;
using OutputKind = Microsoft.CodeAnalysis.OutputKind;
using Platform = Microsoft.CodeAnalysis.Platform;
using ReportDiagnostic = Microsoft.CodeAnalysis.ReportDiagnostic;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerArtifactModelSchemaTests
{
    private static readonly Assembly s_artifactAssembly =
        typeof(CompilerManifestArtifact).Assembly;
    private static readonly NullabilityInfoContext s_nullability = new();
    private static readonly string[] s_assemblyComparerSources = [
        "AssemblyIdentityComparer.Default",
        "DesktopAssemblyIdentityComparer.Default"
    ];
    private static readonly string[] s_assemblyComparerTargets = [
        "Default",
        "Desktop"
    ];

    [Test]
    public void GeneratedDeclarationsMatchTheAuthoritativeSchema()
    {
        using var schema = ReadSchema();

        foreach (var declaration in schema.RootElement
                     .GetProperty("declarations")
                     .EnumerateArray())
        {
            var name = declaration.GetProperty("name").GetString()!;
            var type = s_artifactAssembly.GetType(
                "SharpProof.CompilerArtifact." + name,
                throwOnError: true)!;
            switch (declaration.GetProperty("kind").GetString())
            {
                case "staticClass":
                    Assert.That(type.IsAbstract && type.IsSealed, Is.True, name);
                    AssertConstants(type, declaration);
                    break;
                case "enum":
                    AssertEnum(type, declaration);
                    break;
                case "class":
                    Assert.That(type.IsSealed, Is.True, name);
                    AssertClass(type, declaration);
                    break;
                case "record":
                case "preparedBodyRecord":
                    Assert.That(type.IsSealed && !type.IsValueType, Is.True, name);
                    AssertRecord(type, declaration);
                    break;
                case "recordStruct":
                    Assert.That(type.IsValueType, Is.True, name);
                    AssertRecord(type, declaration);
                    break;
                default:
                    Assert.Fail($"Unknown declaration kind for {name}.");
                    break;
            }
        }
    }

    [Test]
    public void SchemaPinsEnvelopeWireCatalogsAndEffectEvidenceDomain()
    {
        using var schema = ReadSchema();
        var envelope = schema.RootElement.GetProperty("artifactEnvelope");
        var evidence = schema.RootElement.GetProperty("effectEvidence");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                envelope.GetProperty("schema").GetString(),
                Is.EqualTo(CompilerManifestArtifactVersions.Schema));
            Assert.That(
                envelope.GetProperty("version").GetInt32(),
                Is.EqualTo(CompilerManifestArtifactVersions.Current));
            Assert.That(
                evidence.GetProperty("domain").GetString(),
                Is.EqualTo("SharpProof.CompilerEffectClaimEvidence"));
            Assert.That(evidence.GetProperty("version").GetInt32(), Is.EqualTo(8));
            Assert.That(
                evidence.GetProperty("replay")
                    .GetProperty("pathKind").GetString(),
                Is.EqualTo(nameof(CompilerEffectReplayPathKind.Unconditional)));
            Assert.That(
                evidence.GetProperty("replay")
                    .GetProperty("maximumEvents").GetInt32(),
                Is.EqualTo(256));
            Assert.That(
                evidence.GetProperty("replay")
                    .GetProperty("supportedEventKinds")
                    .EnumerateArray()
                    .Select(static value => value.GetString()),
                Is.EqualTo(new[]
                {
                    nameof(CompilerEffectReplayEventKind.ManagedObjectAllocation),
                    nameof(CompilerEffectReplayEventKind.ManagedArrayAllocation)
                }));
        }

        var codec = typeof(PortableIrGraphCodec);
        foreach (var catalog in schema.RootElement
                     .GetProperty("wireEnumCatalogs")
                     .EnumerateArray())
        {
            var field = codec.GetField(
                catalog.GetProperty("field").GetString()!,
                BindingFlags.NonPublic |
                BindingFlags.Static)!;
            string?[] values = [
                .. ((Array)field.GetValue(null)!).Cast<object>()
                    .Select(static value => value.ToString())
            ];
            Assert.That(
                values,
                Is.EqualTo(catalog.GetProperty("members")
                    .EnumerateArray()
                    .Select(static member => member.GetString())),
                field.Name);
        }
        Assert.That(PortableIrGraphCodec.HasCompleteWireEnumCatalogs, Is.True);

        var slotCatalog = typeof(PortableIrSlotCatalog);
        foreach (var domain in schema.RootElement
                     .GetProperty("portableIrSlotMappings")
                     .EnumerateObject())
        {
            var field = slotCatalog.GetField(
                char.ToUpperInvariant(domain.Name[0]) + domain.Name[1..],
                BindingFlags.NonPublic |
                BindingFlags.Static)!;
            var actual = (PortableIrSlotMapping[])field.GetValue(null)!;
            var expected = domain.Value.EnumerateArray().ToArray();
            Assert.That(actual.Length, Is.EqualTo(expected.Length), domain.Name);
            for (var index = 0; index < actual.Length; index++)
            {
                Assert.That(actual[index].Kind,
                    Is.EqualTo(expected[index].GetProperty("kind").GetString()),
                    domain.Name + " kind");
                Assert.That(actual[index].Slots,
                    Is.EqualTo(expected[index].GetProperty("slots")
                        .EnumerateArray()
                        .Select(static value => value.GetString())),
                    domain.Name + " slots");
            }
        }
        Assert.That(PortableIrGraphCodec.HasCompleteSlotCatalogs, Is.True);

        foreach (var domain in schema.RootElement
                     .GetProperty("portableIrSlotDomains")
                     .EnumerateArray())
        {
            var key = domain.GetProperty("key").GetString()!;
            var field = slotCatalog.GetField(
                domain.GetProperty("name").GetString()!,
                BindingFlags.NonPublic |
                BindingFlags.Static)!;
            var enumType = typeof(IrTermKind).Assembly.GetType(
                "SharpProof.Ir." + domain.GetProperty("enum").GetString(),
                throwOnError: true)!;
            var expectedKinds = domain.GetProperty("kinds")
                .EnumerateArray()
                .Select(static value => value.GetString())
                .ToArray();
            var expectedSlots = domain.GetProperty("slots")
                .EnumerateArray()
                .Select(static value => value.GetString())
                .ToArray();
            var actual = (PortableIrSlotMapping[])field.GetValue(null)!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(enumType.IsEnum, Is.True, key);
                Assert.That(Enum.GetNames(enumType), Is.EqualTo(expectedKinds), key);
                Assert.That(actual, Is.Not.Empty, key);
                Assert.That(actual.Select(static mapping => mapping.Slots.Length),
                    Is.All.EqualTo(expectedSlots.Length), key);
            }
        }

        var encoder = typeof(PortableIrGraphCodec).GetNestedType(
            "Encoder",
            BindingFlags.NonPublic)!;
        foreach (var mapping in schema.RootElement
                     .GetProperty("portableIrMetadataRowMappings")
                     .EnumerateArray())
        {
            var methodName = mapping.GetProperty("method").GetString()!;
            var sourceType = mapping.GetProperty("sourceType").GetString() switch
            {
                "IrTypeId" => typeof(IrTypeId),
                "IrVarId" => typeof(IrVarId),
                "IrMemberId" => typeof(IrMemberId),
                "OperationId" => typeof(OperationId),
                var value => throw new AssertionException(
                    "Unknown metadata-row source type: " + value)
            };
            var rowType = s_artifactAssembly.GetType(
                "SharpProof.CompilerArtifact." +
                mapping.GetProperty("rowType").GetString(),
                throwOnError: true)!;
            var method = encoder.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(method.ReturnType, Is.EqualTo(rowType), methodName);
                Assert.That(
                    method.GetParameters().Select(static parameter => parameter.ParameterType),
                    Is.EqualTo([sourceType]),
                    methodName);
                Assert.That(
                    mapping.GetProperty("arguments").GetArrayLength(),
                    Is.EqualTo(rowType.GetConstructors().Single().GetParameters().Length),
                    methodName);
            }
        }
    }

    [Test]
    public void EffectEvidenceUnknownReasonsAreDerivedFromProtocolTuples()
    {
        using var protocol = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SharpProof.Worker.Protocol",
            "ProtocolModel.schema.json")));
        var table = protocol.RootElement.GetProperty("validationTables")
            .EnumerateArray()
            .Single(value => value.GetProperty("name").GetString() == "EffectCertainty");
        var expected = table.GetProperty("rows")
            .EnumerateArray()
            .Where(row => row[0].GetString() == "Unknown" && row[1].GetString() != "*")
            .Select(row => Enum.Parse<WorkerClaimReason>(row[1].GetString()!))
            .Distinct()
            .ToArray();
        var catalogType = s_artifactAssembly.GetType(
            "SharpProof.CompilerArtifact.CompilerEffectEvidenceCatalog",
            throwOnError: true)!;
        var actual = ((Array)catalogType.GetField(
                "UnknownReasons",
                BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
            .Cast<WorkerClaimReason>()
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void CollectorWireCatalogIsUniqueAndSourceComplete()
    {
        using var schema = ReadSchema();
        JsonElement[] mappings = [
            .. schema.RootElement.GetProperty("collectorWireMappings")
                .EnumerateArray()
        ];
        var expectedTypes = new Dictionary<string, (Type Source, Type Target)>(
            StringComparer.Ordinal)
        {
            [nameof(OutputKind)] =
                (typeof(OutputKind), typeof(CompilerOutputKind)),
            [nameof(OptimizationLevel)] =
                (typeof(OptimizationLevel), typeof(CompilerOptimizationLevel)),
            [nameof(Platform)] =
                (typeof(Platform), typeof(CompilerPlatform)),
            [nameof(NullableContextOptions)] =
                (typeof(NullableContextOptions), typeof(CompilerNullableContext)),
            [nameof(MetadataImportOptions)] =
                (typeof(MetadataImportOptions), typeof(CompilerMetadataImportOptions)),
            [nameof(ReportDiagnostic)] =
                (typeof(ReportDiagnostic), typeof(CompilerReportDiagnostic)),
            [nameof(AssemblyIdentityComparer)] =
                (typeof(AssemblyIdentityComparer),
                    typeof(CompilerAssemblyIdentityComparer)),
            [nameof(EffectEvaluationContractKind)] =
                (typeof(EffectEvaluationContractKind),
                    typeof(WorkerEffectContractKind)),
            [nameof(EffectEvaluationOutcome)] =
                (typeof(EffectEvaluationOutcome), typeof(WorkerClaimOutcome)),
            [nameof(EffectEvaluationReason)] =
                (typeof(EffectEvaluationReason), typeof(WorkerClaimReason)),
            [nameof(EffectEvaluationCertainty)] =
                (typeof(EffectEvaluationCertainty),
                    typeof(WorkerEffectEvidenceCertainty)),
            [nameof(EffectContractKind)] =
                (typeof(EffectContractKind), typeof(WorkerEffectSet)),
            [nameof(EffectContractCapabilityKind)] =
                (typeof(EffectContractCapabilityKind),
                    typeof(WorkerEffectCapabilitySet)),
            [nameof(BoundContractKind)] =
                (typeof(BoundContractKind), typeof(CompilerContractKind)),
            [nameof(BoundContractEvidence)] =
                (typeof(BoundContractEvidence), typeof(CompilerContractEvidence)),
            [nameof(BoundContractVariableRole)] =
                (typeof(BoundContractVariableRole), typeof(CompilerVariableRole)),
            ["BoundContractEvidenceWorker"] =
                (typeof(BoundContractEvidence), typeof(WorkerClaimEvidence)),
            [nameof(ContractBindingFailure)] =
                (typeof(ContractBindingFailure), typeof(WorkerClaimReason))
        };
        string[] expectedNames = [
            nameof(OutputKind),
            nameof(OptimizationLevel),
            nameof(Platform),
            nameof(NullableContextOptions),
            nameof(MetadataImportOptions),
            nameof(ReportDiagnostic),
            nameof(AssemblyIdentityComparer),
            nameof(EffectEvaluationContractKind),
            nameof(EffectEvaluationOutcome),
            nameof(EffectEvaluationReason),
            nameof(EffectEvaluationCertainty),
            nameof(EffectContractKind),
            nameof(EffectContractCapabilityKind),
            nameof(BoundContractKind),
            nameof(BoundContractEvidence),
            nameof(BoundContractVariableRole),
            "BoundContractEvidenceWorker",
            nameof(ContractBindingFailure)
        ];
        Assert.That(
            mappings.Select(static mapping =>
                mapping.GetProperty("name").GetString()),
            Is.EqualTo(expectedNames));
        Assert.That(
            mappings.Select(static mapping =>
                mapping.GetProperty("owner").GetString() + "." +
                mapping.GetProperty("method").GetString() + "(" +
                mapping.GetProperty("sourceType").GetString() + ")")
                .Distinct(StringComparer.Ordinal)
                .Count(),
            Is.EqualTo(mappings.Length));

        foreach (var mapping in mappings)
        {
            var name = mapping.GetProperty("name").GetString()!;
            var types = expectedTypes[name];
            var isOption = Array.IndexOf(expectedNames, name) < 7;
            var isEvaluation =
                Array.IndexOf(expectedNames, name) is >= 7 and < 11;
            var isLowering =
                Array.IndexOf(expectedNames, name) is >= 13 and < 18;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    mapping.GetProperty("sourceType").GetString(),
                    Is.EqualTo(types.Source.Name),
                    name + " source type");
                Assert.That(
                    mapping.GetProperty("targetType").GetString(),
                    Is.EqualTo(types.Target.Name),
                    name + " target type");
                Assert.That(
                    mapping.GetProperty("owner").GetString(),
                    Is.EqualTo(isOption
                        ? "CompilerOptionWireMappings"
                        : isEvaluation
                            ? "CompilerEffectEvaluationWireMappings"
                            : isLowering
                                ? "CompilerLoweringWireMappings"
                                : "ClaimManifestBuilder"),
                    name + " owner");
                Assert.That(
                    mapping.GetProperty("method").GetString(),
                    Is.EqualTo(isOption
                        ? "Map"
                        : isEvaluation
                            ? "ToWorker"
                            : isLowering
                                ? name == nameof(BoundContractKind) ||
                                    name == nameof(BoundContractEvidence) ||
                                    name == nameof(BoundContractVariableRole)
                                    ? "ToCompiler"
                                    : name == "BoundContractEvidenceWorker"
                                        ? "ToWorkerEvidence"
                                        : "ToWorkerFailure"
                            : name == nameof(EffectContractKind)
                                ? "ToWorkerEffects"
                                : "ToWorkerCapabilities"),
                    name + " method");
                Assert.That(
                    mapping.GetProperty("kind").GetString(),
                    Is.EqualTo(name == nameof(AssemblyIdentityComparer)
                        ? "referenceIdentity"
                        : isOption || isEvaluation || isLowering
                            ? "enum"
                            : "flags"),
                    name + " kind");
                Assert.That(
                    mapping.GetProperty("unknownException").GetString(),
                    Is.EqualTo(isOption
                        ? "InvalidOperationException"
                        : "ArgumentOutOfRangeException"),
                    name + " unknown exception");
            }
            string[] sources = [
                .. mapping.GetProperty("rows").EnumerateArray()
                    .Select(static row =>
                        row.GetProperty("source").GetString()!)
            ];
            string[] targets = [
                .. mapping.GetProperty("rows").EnumerateArray()
                    .Select(static row =>
                        row.GetProperty("target").GetString()!)
            ];
            Assert.That(
                sources.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(sources.Length),
                name + " sources");
            if (!mapping.TryGetProperty("allowTargetAliases", out var aliases) ||
                !aliases.GetBoolean())
            {
                Assert.That(
                    targets.Distinct(StringComparer.Ordinal).Count(),
                    Is.EqualTo(targets.Length),
                    name + " targets");
            }

            if (name == nameof(AssemblyIdentityComparer))
            {
                Assert.That(
                    sources,
                    Is.EqualTo(s_assemblyComparerSources));
                Assert.That(
                    targets,
                    Is.EqualTo(s_assemblyComparerTargets));
                continue;
            }

            Assert.That(
                sources,
                Is.EquivalentTo(Enum.GetNames(types.Source)),
                name + " source completeness");
            Assert.That(
                targets.All(target =>
                    Enum.GetNames(types.Target).Contains(
                        target,
                        StringComparer.Ordinal)),
                Is.True,
                name + " target names");
        }
    }

    private static void AssertConstants(Type type, JsonElement declaration)
    {
        JsonElement[] specifications = [
            .. declaration.GetProperty("constants").EnumerateArray()
        ];
        FieldInfo[] fields = [
            .. type.GetFields(
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
                .Where(static field => field.IsLiteral)
                .OrderBy(static field => field.MetadataToken)
        ];
        Assert.That(
            fields.Select(static field => field.Name),
            Is.EqualTo(specifications.Select(static specification =>
                specification.GetProperty("name").GetString())),
            type.Name);
        for (var index = 0; index < fields.Length; index++)
        {
            var expected = specifications[index].GetProperty("value");
            Assert.That(
                fields[index].GetRawConstantValue(),
                Is.EqualTo(expected.ValueKind == JsonValueKind.String
                    ? expected.GetString()
                    : expected.GetInt32()),
                fields[index].Name);
        }
    }

    private static void AssertEnum(Type type, JsonElement declaration)
    {
        JsonElement[] members = [
            .. declaration.GetProperty("members").EnumerateArray()
        ];
        Assert.That(type.IsEnum, Is.True, type.Name);
        Assert.That(
            Enum.GetNames(type),
            Is.EqualTo(members.Select(static member =>
                member.GetProperty("name").GetString())),
            type.Name);
        Assert.That(
            Enum.GetValues(type).Cast<object>().Select(static value =>
                Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            Is.EqualTo(members.Select(static member =>
                member.GetProperty("value").GetInt32())),
            type.Name);
    }

    private static void AssertRecord(Type type, JsonElement declaration)
    {
        JsonElement[] parameters = [
            .. declaration.GetProperty("parameters").EnumerateArray()
        ];
        JsonElement[] members = declaration.TryGetProperty(
            "members",
            out var memberRows)
            ? [.. memberRows.EnumerateArray()]
            : [];
        string?[] expectedNames = [
            .. parameters
                .Select(static parameter =>
                    parameter.GetProperty("name").GetString())
                .Concat(members.Select(static member =>
                    member.GetProperty("name").GetString()))
        ];
        PropertyInfo[] properties = [
            .. type.GetProperties(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly)
                .Where(property => expectedNames.Contains(
                    property.Name,
                    StringComparer.Ordinal))
                .OrderBy(static property => property.MetadataToken)
        ];
        Assert.That(
            properties.Select(static property => property.Name),
            Is.EqualTo(expectedNames),
            type.Name);
        for (var index = 0; index < parameters.Length; index++)
        {
            Assert.That(
                SchemaType(properties[index]),
                Is.EqualTo(parameters[index].GetProperty("type").GetString()),
                $"{type.Name}.{properties[index].Name}");
        }

    }

    private static void AssertClass(Type type, JsonElement declaration)
    {
        JsonElement[] specifications = [
            .. declaration.GetProperty("properties").EnumerateArray()
        ];
        PropertyInfo[] properties = [
            .. type.GetProperties(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly)
                .OrderBy(static property => property.MetadataToken)
        ];
        Assert.That(
            properties.Select(static property => property.Name),
            Is.EqualTo(specifications.Select(static specification =>
                specification.GetProperty("name").GetString())),
            type.Name);
        for (var index = 0; index < properties.Length; index++)
        {
            var property = properties[index];
            var specification = specifications[index];
            Assert.That(
                SchemaType(property),
                Is.EqualTo(specification.GetProperty("type").GetString()),
                $"{type.Name}.{property.Name}");
            Assert.That(
                property.GetMethod!.IsPublic,
                Is.EqualTo(
                    specification.GetProperty("accessibility").GetString() ==
                    "public"),
                $"{type.Name}.{property.Name}");
            AssertSetter(property, specification);
        }

        if (specifications.Any(static specification =>
                !specification.TryGetProperty("jsonName", out _)))
        {
            return;
        }

        var instance = CreateDefaultInstance(type);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(
            instance,
            type,
            WorkerProtocolJson.Options));
        Assert.That(
            wire.RootElement.EnumerateObject().Select(static property =>
                property.Name),
            Is.EqualTo(specifications.Select(static specification =>
                specification.GetProperty("jsonName").GetString())),
            type.Name);
        for (var index = 0; index < properties.Length; index++)
        {
            AssertDefault(
                type,
                properties[index],
                properties[index].GetValue(instance),
                specifications[index],
                declaration);
            var property = properties[index];
            if (property.SetMethod == null)
            {
                continue;
            }

            var replacement = ReplacementValue(
                property.PropertyType,
                property.GetValue(instance));
            property.SetValue(instance, replacement);
            Assert.That(
                property.GetValue(instance),
                Is.EqualTo(replacement),
                $"{type.Name}.{property.Name}");
        }
    }

    private static void AssertSetter(
        PropertyInfo property,
        JsonElement specification)
    {
        var expected = specification.GetProperty("set").GetString();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                property.SetMethod == null,
                Is.EqualTo(expected == "none"),
                property.Name);
            if (expected != "none")
            {
                Assert.That(
                    property.SetMethod!.IsPublic,
                    Is.EqualTo(
                        specification.GetProperty("accessibility").GetString() ==
                        "public"),
                    property.Name);
            }
        }
    }

    private static void AssertDefault(
        Type declaringType,
        PropertyInfo property,
        object? actual,
        JsonElement specification,
        JsonElement declaration)
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
                    property.Name);
                break;
            case "parameter":
                Assert.That(
                    actual,
                    Is.EqualTo(ParameterDefault(
                        property.PropertyType,
                        defaultValue.GetProperty("name").GetString()!,
                        declaration)),
                    property.Name);
                break;
            case "stringEmpty":
            case "parameterOrStringEmpty":
                Assert.That(actual, Is.EqualTo(string.Empty), property.Name);
                break;
            case "new":
                Assert.That(actual, Is.TypeOf(property.PropertyType), property.Name);
                break;
            case "emptyArray":
            case "parameterOrEmptyArray":
                Assert.That(actual, Is.InstanceOf<Array>(), property.Name);
                Assert.That(((Array)actual!).Length, Is.Zero, property.Name);
                break;
            case "literal":
                Assert.That(
                    Convert.ToInt64(actual, CultureInfo.InvariantCulture),
                    Is.EqualTo(long.Parse(
                        defaultValue.GetProperty("value").GetString()!,
                        CultureInfo.InvariantCulture)),
                    property.Name);
                break;
            case "member":
                Assert.That(
                    actual,
                    Is.EqualTo(ResolveMember(
                        defaultValue.GetProperty("value").GetString()!)),
                    property.Name);
                break;
            default:
                Assert.Fail(
                    $"Unknown default kind '{kind}' for " +
                    $"{declaringType.Name}.{property.Name}.");
                break;
        }
    }

    private static object? ParameterDefault(
        Type type,
        string name,
        JsonElement declaration)
    {
        var parameter = declaration.GetProperty("constructor")
            .EnumerateArray()
            .Single(candidate =>
                candidate.GetProperty("name").GetString() == name);
        var value = parameter.GetProperty("default").GetString()!;
        return value switch
        {
            "default" => type.IsValueType ? Activator.CreateInstance(type) : null,
            "null" => null,
            "false" => false,
            "true" => true,
            _ when type == typeof(int) => int.Parse(
                value,
                CultureInfo.InvariantCulture),
            _ when type == typeof(long) => long.Parse(
                value,
                CultureInfo.InvariantCulture),
            _ => throw new AssertionException(
                $"Unsupported constructor default '{value}'.")
        };
    }

    private static object? ReplacementValue(Type type, object? current)
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
                : elementType.IsValueType
                    ? Activator.CreateInstance(elementType)
                    : CreateObject(elementType);
            var result = Array.CreateInstance(elementType, 1);
            result.SetValue(value, 0);
            return result;
        }
        return CreateObject(type);
    }

    private static object CreateObject(Type type)
    {
        var constructor = type.GetConstructors(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance)
            .OrderBy(static candidate => candidate.GetParameters().Length)
            .First();
        var parameters = constructor.GetParameters();
        if (parameters.All(static parameter => parameter.HasDefaultValue))
        {
            return constructor.Invoke([
                .. parameters.Select(static parameter => parameter.DefaultValue)
            ]);
        }

        return constructor.Invoke([
            .. parameters.Select(parameter => parameter.ParameterType.IsValueType
                ? Activator.CreateInstance(parameter.ParameterType)
                : null)
        ]);
    }

    private static object CreateDefaultInstance(Type type)
    {
        var constructor = type.GetConstructors(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance)
            .Single();
        return constructor.Invoke([
            .. constructor.GetParameters()
                .Select(static parameter => parameter.HasDefaultValue
                    ? parameter.DefaultValue
                    : throw new AssertionException(
                        $"{parameter.Member.DeclaringType!.Name} does not have " +
                        "a default wire constructor."))
        ]);
    }

    private static object? ResolveMember(string member)
    {
        var separator = member.LastIndexOf('.');
        var typeName = member[..separator];
        var memberName = member[(separator + 1)..];
        var type = s_artifactAssembly.GetType(
                       "SharpProof.CompilerArtifact." + typeName) ??
                   typeof(WorkerProtocolJson).Assembly.GetType(
                       "SharpProof.Worker.Protocol." + typeName,
                       throwOnError: true)!;
        return type.GetField(
                memberName,
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)!
            .GetRawConstantValue();
    }

    private static string SchemaType(PropertyInfo property)
    {
        return SchemaType(property.PropertyType, s_nullability.Create(property));
    }

    private static string SchemaType(Type type, NullabilityInfo? nullability)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return SchemaType(underlying, null) + "?";
        }

        if (type.IsArray)
        {
            return SchemaType(type.GetElementType()!, nullability?.ElementType) + "[]";
        }

        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(ScopedIrId<>))
        {
            return IrIdentifierSchemaName(type.GetGenericArguments()[0]);
        }

        var name = type == typeof(string)
            ? "string"
            : type == typeof(bool)
                ? "bool"
                : type == typeof(int)
                    ? "int"
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
                                                  index]))) +
                              ">"
                            : type.Name;
        return !type.IsValueType &&
               nullability?.ReadState == NullabilityState.Nullable
            ? name + "?"
            : name;
    }

    private static string IrIdentifierSchemaName(Type tag)
    {
        return tag.Name switch
        {
            nameof(IrIdentityTag) => "IrIdentityId",
            nameof(IrTermTag) => "IrId",
            nameof(IrVariableTag) => "IrVarId",
            nameof(IrTypeTag) => "IrTypeId",
            nameof(IrMemberTag) => "IrMemberId",
            nameof(IrStringTag) => "IrStringId",
            nameof(IrOperationTag) => "OperationId",
            nameof(IrBlockTag) => "IrBlockId",
            nameof(IrInstructionTag) => "IrInstructionId",
            _ => throw new InvalidOperationException(
                $"Unknown IR identifier tag '{tag.FullName}'.")
        };
    }

    private static JsonDocument ReadSchema()
    {
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SharpProof.CompilerArtifact",
            "CompilerArtifactModel.schema.json")));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
}
