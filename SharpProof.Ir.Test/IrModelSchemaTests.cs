using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrModelSchemaTests
{
    private const BindingFlags DeclaredInstanceMembers =
        BindingFlags.DeclaredOnly |
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic;

    private static readonly string[] ExpectedDeclarationNames = [
        "IrValueKind",
        "IrEvaluationStatus",
        "IrUnsupportedReason",
        "IrExceptionKind",
        "IrProgramExecutionStatus",
        "IrValue",
        "IrUnsupportedInfo",
        "IrExceptionInfo",
        "IrEvaluationResult",
        "IrProgramExecutionResult",
        "IrTermKind",
        "IrOpaquePurity",
        "IrTypeInfo",
        "IrVariableInfo",
        "IrMemberInfo",
        "IrOperationInfo",
        "IrTerm",
        "IrBooleanTerm",
        "IrIntegerTerm",
        "IrStringTerm",
        "IrNullTerm",
        "IrVariableTerm",
        "IrOpaqueTerm",
        "IrUnaryTerm",
        "IrBinaryTerm",
        "IrConditionalTerm",
        "IrCastTerm",
        "IrLengthTerm",
        "IrSequenceAccessTerm",
        "IrInstructionKind",
        "IrLocationKind",
        "IrHavocKind",
        "IrLocation",
        "IrMemberLocation",
        "IrSequenceLocation",
        "IrInstruction",
        "IrAssignInstruction",
        "IrLoadInstruction",
        "IrStoreInstruction",
        "IrCallInstruction",
        "IrAssumeInstruction",
        "IrAssertInstruction",
        "IrHavocInstruction",
        "IrBranchInstruction",
        "IrGotoInstruction",
        "IrReturnInstruction",
        "IrBasicBlock",
        "IrProgram"
    ];

    [Test]
    public void SchemaProjectionPreservesExactRuntimeShape()
    {
        using var document = ReadSchema();
        var root = document.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(
                root.GetProperty("namespace").GetString(),
                Is.EqualTo("SharpProof.Ir"));
        }

        var declarations = root
            .GetProperty("declarations")
            .EnumerateArray()
            .ToArray();
        Assert.That(
            declarations.Select(static declaration =>
                declaration.GetProperty("name").GetString()),
            Is.EqualTo(ExpectedDeclarationNames));

        foreach (var declaration in declarations)
        {
            var kind = declaration.GetProperty("kind").GetString();
            if (kind == "enum")
            {
                AssertEnumShape(declaration);
            }
            else
            {
                Assert.That(kind, Is.EqualTo("class"));
                AssertClassShape(declaration);
            }
        }

        var declaredOrdinaryMethods = declarations
            .Where(static declaration =>
                declaration.GetProperty("kind").GetString() == "class")
            .Select(declaration => ResolveNamedType(
                    declaration.GetProperty("name").GetString()!)
                .GetMethods(DeclaredInstanceMembers)
                .Where(static method => !method.IsSpecialName)
                .Select(static method =>
                    $"{method.DeclaringType!.Name}.{method.Name}"))
            .SelectMany(static methods => methods)
            .ToArray();
        Assert.That(
            declaredOrdinaryMethods,
            Is.EqualTo([
                "IrValue.Get",
                "IrProgramExecutionResult.GetCurrentValue",
                "IrProgram.GetBlock"]));
    }

    [Test]
    public void EnumNumericValuesRemainStable()
    {
        AssertEnumValues<IrValueKind>(
            ("Boolean", 0),
            ("Integer", 1),
            ("String", 2),
            ("Null", 3),
            ("Reference", 4),
            ("Sequence", 5));
        AssertEnumValues<IrEvaluationStatus>(
            ("Value", 0),
            ("Unsupported", 1),
            ("Exception", 2));
        AssertEnumValues<IrUnsupportedReason>(
            ("OpaqueTerm", 0),
            ("MissingVariable", 1),
            ("InvalidVariableValue", 2),
            ("UnsupportedCast", 3),
            ("UnsupportedOperation", 4));
        AssertEnumValues<IrExceptionKind>(
            ("DivideByZero", 0),
            ("Overflow", 1),
            ("NullReference", 2),
            ("IndexOutOfRange", 3),
            ("InvalidCast", 4));
        AssertEnumValues<IrProgramExecutionStatus>(
            ("Returned", 0),
            ("AssumptionViolated", 1),
            ("AssertionFailed", 2),
            ("Unsupported", 3),
            ("Exception", 4),
            ("StepLimit", 5));
        AssertEnumValues<IrTermKind>(
            ("Boolean", 0),
            ("Integer", 1),
            ("String", 2),
            ("Null", 3),
            ("Variable", 4),
            ("Opaque", 5),
            ("Unary", 6),
            ("Binary", 7),
            ("Conditional", 8),
            ("Cast", 9),
            ("Length", 10),
            ("SequenceAccess", 11));
        AssertEnumValues<IrOpaquePurity>(
            ("Pure", 0),
            ("Impure", 1));
        AssertEnumValues<IrInstructionKind>(
            ("Assign", 0),
            ("Load", 1),
            ("Store", 2),
            ("Call", 3),
            ("Assume", 4),
            ("Assert", 5),
            ("Havoc", 6),
            ("Branch", 7),
            ("Goto", 8),
            ("Return", 9));
        AssertEnumValues<IrLocationKind>(
            ("Member", 0),
            ("Sequence", 1));
        AssertEnumValues<IrHavocKind>(
            ("Variables", 0),
            ("Memory", 1),
            ("VariablesAndMemory", 2));
    }

    [Test]
    public void ComputedModelBehaviorRemainsHandwritten()
    {
        var root = TestRepository.FindRoot();
        var generated = File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Ir",
            "IrModel.generated.cs"));
        var handwritten = File.ReadAllText(Path.Combine(
            root,
            "SharpProof.Ir",
            "IrProgram.cs"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(generated, Does.Not.Contain("IsTerminal =>"));
            Assert.That(generated, Does.Not.Contain("Terminator =>"));
            Assert.That(generated, Does.Not.Contain("_blocksById"));
            Assert.That(generated, Does.Not.Contain("GetBlock("));
            Assert.That(handwritten, Does.Contain("IsTerminal =>"));
            Assert.That(handwritten, Does.Contain("Terminator =>"));
            Assert.That(handwritten, Does.Not.Contain("_blocksById"));
            Assert.That(handwritten, Does.Contain("GetBlock("));
        }

        var returnProgram = CreateTerminalProgram(static (builder, factory, entry, _) =>
            builder.Return(entry, factory.CreateOperation("return")));
        var gotoProgram = CreateTerminalProgram(static (builder, factory, entry, target) =>
            builder.Goto(entry, factory.CreateOperation("goto"), target));
        var branchProgram = CreateTerminalProgram(
            static (builder, factory, entry, target) =>
            builder.Branch(
                entry,
                factory.CreateOperation("branch"),
                factory.Boolean(true),
                target,
                target));

        foreach (var program in new[] {
                     returnProgram,
                     gotoProgram,
                     branchProgram
                 })
        {
            var entry = program.GetBlock(program.Entry);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entry.Terminator.IsTerminal, Is.True);
                Assert.That(
                    entry.Terminator,
                    Is.SameAs(entry.Instructions[^1]));
            }
        }

        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var assignment = builder.Assign(
            block,
            factory.CreateOperation("assign"),
            variable,
            factory.Integer(1));
        builder.Return(block, factory.CreateOperation("return"));
        Assert.That(assignment.IsTerminal, Is.False);
    }

    private static IrProgram CreateTerminalProgram(
        Action<IrProgramBuilder, IrFactory, IrBlockId, IrBlockId> addTerminator)
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var target = builder.CreateBlock("target");
        addTerminator(builder, factory, entry, target);
        builder.Return(target, factory.CreateOperation("return"));
        return builder.Build();
    }

    private static void AssertEnumShape(JsonElement declaration)
    {
        var name = declaration.GetProperty("name").GetString()!;
        var type = ResolveNamedType(name);
        Assert.That(type.IsEnum, Is.True, name);
        var expected = declaration
            .GetProperty("members")
            .EnumerateArray()
            .Select(static member => (
                Name: member.GetProperty("name").GetString()!,
                Value: member.GetProperty("value").GetInt32()))
            .ToArray();
        Assert.That(Enum.GetNames(type), Is.EqualTo(expected.Select(static row => row.Name)));
        Assert.That(
            Enum.GetValues(type)
                .Cast<object>()
                .Select(static value => Convert.ToInt32(
                    value,
                    CultureInfo.InvariantCulture)),
            Is.EqualTo(expected.Select(static row => row.Value)));
    }

    private static void AssertClassShape(JsonElement declaration)
    {
        var name = declaration.GetProperty("name").GetString()!;
        var type = ResolveNamedType(name);
        var modifier = declaration.GetProperty("modifier").GetString();
        var expectedBase = declaration.TryGetProperty("baseType", out var baseType)
            ? ResolveNamedType(baseType.GetString()!)
            : typeof(object);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(type.IsPublic, Is.True, name);
            Assert.That(type.IsClass, Is.True, name);
            Assert.That(type.IsAbstract, Is.EqualTo(modifier == "abstract"), name);
            Assert.That(type.IsSealed, Is.EqualTo(modifier == "sealed"), name);
            Assert.That(type.BaseType, Is.EqualTo(expectedBase), name);
        }

        var constructorSchema = declaration.GetProperty("constructor");
        var constructor = type
            .GetConstructors(DeclaredInstanceMembers)
            .Single();
        var expectedConstructorAccessibility = constructorSchema
            .GetProperty("accessibility")
            .GetString();
        var expectedParameters = constructorSchema
            .GetProperty("parameters")
            .EnumerateArray()
            .ToArray();
        var actualParameters = constructor.GetParameters();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ConstructorAccessibility(constructor),
                Is.EqualTo(expectedConstructorAccessibility),
                name);
            Assert.That(actualParameters, Has.Length.EqualTo(expectedParameters.Length), name);
        }
        for (var index = 0; index < expectedParameters.Length; index++)
        {
            var expected = expectedParameters[index];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    actualParameters[index].Name,
                    Is.EqualTo(expected.GetProperty("name").GetString()),
                    $"{name} constructor parameter {index}");
                Assert.That(
                    actualParameters[index].ParameterType,
                    Is.EqualTo(ResolveType(expected.GetProperty("type").GetString()!)),
                    $"{name} constructor parameter {index}");
            }
        }

        var expectedProperties = declaration
            .GetProperty("properties")
            .EnumerateArray()
            .ToDictionary(
                static property => property.GetProperty("name").GetString()!,
                StringComparer.Ordinal);
        var actualProperties = type
            .GetProperties(DeclaredInstanceMembers)
            .ToDictionary(static property => property.Name, StringComparer.Ordinal);
        Assert.That(
            actualProperties.Keys.OrderBy(static value => value, StringComparer.Ordinal),
            Is.EqualTo(expectedProperties.Keys.OrderBy(
                static value => value,
                StringComparer.Ordinal)),
            name);
        foreach (var expectedProperty in expectedProperties)
        {
            var property = actualProperties[expectedProperty.Key];
            var schema = expectedProperty.Value;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    property.PropertyType,
                    Is.EqualTo(ResolveType(schema.GetProperty("type").GetString()!)),
                    $"{name}.{property.Name}");
                Assert.That(
                    PropertyAccessibility(property),
                    Is.EqualTo(schema.GetProperty("accessibility").GetString()),
                    $"{name}.{property.Name}");
                Assert.That(property.SetMethod, Is.Null, $"{name}.{property.Name}");
            }
        }
    }

    private static string ConstructorAccessibility(ConstructorInfo constructor)
    {
        if (constructor.IsAssembly)
        {
            return "internal";
        }
        if (constructor.IsFamilyAndAssembly)
        {
            return "private protected";
        }
        return constructor.Attributes.ToString();
    }

    private static string PropertyAccessibility(PropertyInfo property)
    {
        var getter = property.GetMethod!;
        return getter.IsPublic ? "public" :
            getter.IsAssembly ? "internal" :
            getter.Attributes.ToString();
    }

    private static Type ResolveType(string source)
    {
        if (source.EndsWith('?'))
        {
            var underlying = ResolveType(source[..^1]);
            return underlying.IsValueType
                ? typeof(Nullable<>).MakeGenericType(underlying)
                : underlying;
        }
        const string immutablePrefix = "ImmutableArray<";
        if (source.StartsWith(immutablePrefix, StringComparison.Ordinal) &&
            source.EndsWith('>'))
        {
            var argument = source[immutablePrefix.Length..^1];
            return typeof(ImmutableArray<>).MakeGenericType(ResolveType(argument));
        }
        const string immutableDictionaryPrefix = "ImmutableDictionary<";
        if (source.StartsWith(immutableDictionaryPrefix, StringComparison.Ordinal) &&
            source.EndsWith('>'))
        {
            var arguments = source[immutableDictionaryPrefix.Length..^1]
                .Split(", ", StringSplitOptions.None);
            return typeof(ImmutableDictionary<,>).MakeGenericType(
                ResolveType(arguments[0]),
                ResolveType(arguments[1]));
        }
        return source switch
        {
            "bool" => typeof(bool),
            "int" => typeof(int),
            "long" => typeof(long),
            "object" => typeof(object),
            "string" => typeof(string),
            "IrIdentityId" => typeof(IrIdentityId),
            "IrId" => typeof(IrId),
            "IrVarId" => typeof(IrVarId),
            "IrTypeId" => typeof(IrTypeId),
            "IrMemberId" => typeof(IrMemberId),
            "IrStringId" => typeof(IrStringId),
            "OperationId" => typeof(OperationId),
            "IrBlockId" => typeof(IrBlockId),
            "IrInstructionId" => typeof(IrInstructionId),
            _ => ResolveNamedType(source)
        };
    }

    private static Type ResolveNamedType(string name)
    {
        return typeof(IrProgram).Assembly.GetType(
                "SharpProof.Ir." + name,
                throwOnError: true,
                ignoreCase: false)!;
    }

    private static void AssertEnumValues<T>(params (string Name, int Value)[] expected)
        where T : struct, Enum
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Enum.GetNames<T>(),
                Is.EqualTo(expected.Select(static row => row.Name)));
            Assert.That(
                Enum.GetValues<T>().Select(static value => Convert.ToInt32(
                    value,
                    CultureInfo.InvariantCulture)),
                Is.EqualTo(expected.Select(static row => row.Value)));
        }
    }

    private static JsonDocument ReadSchema()
    {
        return TestRepository.ReadSchema(
            "SharpProof.Ir",
            "IrModel.schema.json");
    }

}
