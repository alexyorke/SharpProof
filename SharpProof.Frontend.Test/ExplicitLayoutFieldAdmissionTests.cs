using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
[NonParallelizable]
public sealed class ExplicitLayoutFieldAdmissionTests
{
    private static readonly byte[] Int32FieldSignature = [0x06, 0x08];
    private static readonly ImmutableArray<string> BclAliases = ["global", "Bcl"];

    private const string AutoSource = """
        #undef SHARPPROOF_CONTRACTS
        [global::System.Runtime.InteropServices.StructLayoutAttribute(global::System.Runtime.InteropServices.LayoutKind.Auto)]
        public sealed class Cell
        {
            public int Value;
            public int Other;
        }
        """ + "\n";

    private const string SequentialSource = """
        #undef SHARPPROOF_CONTRACTS
        [global::System.Runtime.InteropServices.StructLayoutAttribute(global::System.Runtime.InteropServices.LayoutKind.Sequential)]
        public sealed class Cell
        {
            public int Value;
            public int Other;
        }
        """ + "\n";

    private const string ExplicitSource = """
        #undef SHARPPROOF_CONTRACTS
        [global::System.Runtime.InteropServices.StructLayoutAttribute(global::System.Runtime.InteropServices.LayoutKind.Explicit)]
        public sealed class Cell
        {
            [global::System.Runtime.InteropServices.FieldOffsetAttribute(0)]
            public int Value;
            [global::System.Runtime.InteropServices.FieldOffsetAttribute(0)]
            public int Other;
        }
        """ + "\n";

    private const string OtherSource = """
        #undef SHARPPROOF_CONTRACTS
        namespace Other
        {
            public sealed class StructLayoutAttribute : global::System.Attribute
            {
                public StructLayoutAttribute(global::System.Runtime.InteropServices.LayoutKind kind)
                {
                }
            }
        }

        [Other.StructLayoutAttribute(global::System.Runtime.InteropServices.LayoutKind.Explicit)]
        public sealed class Cell
        {
            public int Value;
            public int Other;
        }
        """ + "\n";

    private const string NestedSource = """
        #undef SHARPPROOF_CONTRACTS
        extern alias Bcl;
        namespace System.Runtime
        {
            public static class InteropServices
            {
                public sealed class StructLayoutAttribute : Bcl::System.Attribute
                {
                    public StructLayoutAttribute(Bcl::System.Runtime.InteropServices.LayoutKind kind)
                    {
                    }
                }
            }
        }

        [global::System.Runtime.InteropServices.StructLayoutAttribute(Bcl::System.Runtime.InteropServices.LayoutKind.Explicit)]
        public sealed class Cell
        {
            [Bcl::System.Runtime.InteropServices.FieldOffsetAttribute(0)]
            public int Value;
            [Bcl::System.Runtime.InteropServices.FieldOffsetAttribute(0)]
            public int Other;
        }
        """ + "\n";

    private const string GenericSource = """
        #undef SHARPPROOF_CONTRACTS
        public sealed class GenericCell<T>
        {
            public int Value;
            public int Other;
        }
        """ + "\n";

    [TestCase("source-auto", true, true)]
    [TestCase("source-sequential", true, true)]
    [TestCase("source-explicit", false, false)]
    [TestCase("source-other-attribute", true, true)]
    [TestCase("source-nested-attribute", false, false)]
    [TestCase("metadata-auto", true, false)]
    [TestCase("metadata-sequential", true, false)]
    [TestCase("metadata-explicit", false, false)]
    [TestCase("metadata-constructed-auto", true, false)]
    public async Task FieldAdmissionUsesDeclaringOwnerLayout(
        string scenario,
        bool expectedRead,
        bool expectedWrite)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var source = Source(scenario);
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "explicit-layout-field-admission-audit",
            scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "producer-source.cs"), source);
        var references = scenario == "source-nested-attribute"
            ? TestMetadataReferences.Platform.Select(static reference => reference.WithAliases(BclAliases)).ToImmutableArray()
            : TestMetadataReferences.Platform;
        var producer = CreateCompilation("LayoutAdmission_" + scenario.Replace('-', '_'), source, references);
        using var image = new MemoryStream();
        var emission = producer.Emit(image);
        await Save(evidenceDirectory, "emission.json", new
        {
            SourceSha256 = Hash(Encoding.UTF8.GetBytes(source)),
            Optimization = producer.Options.OptimizationLevel.ToString(),
            CompilationDiagnostics = producer.GetDiagnostics().Select(static diagnostic => diagnostic.ToString()).ToArray(),
            emission.Success,
            EmitDiagnostics = emission.Diagnostics.Select(static diagnostic => diagnostic.ToString()).ToArray()
        });
        AssertNoErrors(producer);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "emitted-producer.dll"), imageBytes);
        var constructed = scenario == "metadata-constructed-auto";
        var typeName = constructed ? "GenericCell\u00601" : "Cell";
        var sourceOwner = producer.GetTypeByMetadataName(typeName);
        Assert.That(sourceOwner, Is.Not.Null);
        await VerifySourceBinding(evidenceDirectory, scenario, producer, sourceOwner!);
        using var peStream = new MemoryStream(imageBytes, writable: false);
        using var pe = new PEReader(peStream);
        var reader = pe.GetMetadataReader();
        var typeHandle = reader.TypeDefinitions.Single(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == typeName);
        var typeDefinition = reader.GetTypeDefinition(typeHandle);
        var valueHandle = typeDefinition.GetFields().Single(handle => reader.GetString(reader.GetFieldDefinition(handle).Name) == "Value");
        var otherHandle = typeDefinition.GetFields().Single(handle => reader.GetString(reader.GetFieldDefinition(handle).Name) == "Other");
        var valueDefinition = reader.GetFieldDefinition(valueHandle);
        var otherDefinition = reader.GetFieldDefinition(otherHandle);
        var expectedLayout = scenario.Contains("explicit", StringComparison.Ordinal) || scenario == "source-nested-attribute"
            ? TypeAttributes.ExplicitLayout
            : scenario.Contains("sequential", StringComparison.Ordinal) ? TypeAttributes.SequentialLayout : TypeAttributes.AutoLayout;
        var expectedOffset = expectedLayout == TypeAttributes.ExplicitLayout ? 0 : -1;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(typeDefinition.Attributes & TypeAttributes.LayoutMask, Is.EqualTo(expectedLayout));
            Assert.That(typeDefinition.GetFields().Count, Is.EqualTo(2));
            Assert.That(reader.GetBlobBytes(valueDefinition.Signature), Is.EqualTo(Int32FieldSignature));
            Assert.That(reader.GetBlobBytes(otherDefinition.Signature), Is.EqualTo(Int32FieldSignature));
            Assert.That(valueDefinition.GetOffset(), Is.EqualTo(expectedOffset));
            Assert.That(otherDefinition.GetOffset(), Is.EqualTo(expectedOffset));
            Assert.That(valueDefinition.Attributes & (FieldAttributes.Static | FieldAttributes.InitOnly), Is.EqualTo((FieldAttributes)0));
            Assert.That(otherDefinition.Attributes & (FieldAttributes.Static | FieldAttributes.InitOnly), Is.EqualTo((FieldAttributes)0));
        }

        await Save(evidenceDirectory, "pe-layout-binding.json", new
        {
            SourceSha256 = Hash(Encoding.UTF8.GetBytes(source)),
            EmittedPeSha256 = Hash(imageBytes),
            ModuleMvid = reader.GetGuid(reader.GetModuleDefinition().Mvid),
            TypeToken = MetadataTokens.GetToken(typeHandle),
            ValueToken = MetadataTokens.GetToken(valueHandle),
            OtherToken = MetadataTokens.GetToken(otherHandle),
            LayoutMask = (typeDefinition.Attributes & TypeAttributes.LayoutMask).ToString(),
            PackingSize = typeDefinition.GetLayout().PackingSize,
            Size = typeDefinition.GetLayout().Size,
            ValueOffset = valueDefinition.GetOffset(),
            OtherOffset = otherDefinition.GetOffset(),
            Int32FieldSignatures = true,
            MutableInstanceFields = true
        });
        var imported = scenario.StartsWith("metadata-", StringComparison.Ordinal);
        var selectedCompilation = producer;
        var selectedOwner = sourceOwner!;
        if (imported)
        {
            selectedCompilation = CreateCompilation("LayoutAdmissionConsumer_" + scenario.Replace('-', '_'),
                "internal static class Consumer { }\n",
                TestMetadataReferences.Platform.Add(MetadataReference.CreateFromImage(imageBytes)));
            AssertNoErrors(selectedCompilation);
            selectedOwner = selectedCompilation.GetTypeByMetadataName(typeName)!;
            Assert.That(selectedOwner, Is.Not.Null);
            if (constructed)
            {
                selectedOwner = selectedOwner.Construct(selectedCompilation.GetSpecialType(SpecialType.System_Int32));
                Assert.That(SymbolEqualityComparer.Default.Equals(selectedOwner, selectedOwner.OriginalDefinition), Is.False);
                Assert.That(selectedOwner.TypeArguments.Single().SpecialType, Is.EqualTo(SpecialType.System_Int32));
            }

            var originalOwner = selectedOwner.OriginalDefinition;
            var ownMetadata = originalOwner.ContainingModule.GetMetadata();
            Assert.That(ownMetadata, Is.Not.Null);
            var importedReader = ownMetadata!.GetMetadataReader();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SymbolEqualityComparer.Default.Equals(originalOwner.ContainingAssembly, selectedCompilation.Assembly), Is.False);
                Assert.That(originalOwner.MetadataToken, Is.EqualTo(MetadataTokens.GetToken(typeHandle)));
                Assert.That(importedReader.GetGuid(importedReader.GetModuleDefinition().Mvid),
                    Is.EqualTo(reader.GetGuid(reader.GetModuleDefinition().Mvid)));
                Assert.That(importedReader.GetTypeDefinition((TypeDefinitionHandle)MetadataTokens.Handle(originalOwner.MetadataToken)).Attributes &
                    TypeAttributes.LayoutMask, Is.EqualTo(expectedLayout));
            }
        }
        else
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SymbolEqualityComparer.Default.Equals(selectedOwner.ContainingAssembly, producer.Assembly), Is.True);
                Assert.That(selectedOwner.MetadataToken, Is.Zero);
                Assert.That(selectedOwner.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
            }
        }

        var field = selectedOwner.GetMembers("Value").OfType<IFieldSymbol>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(field.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(field.IsStatic, Is.False);
            Assert.That(field.IsReadOnly, Is.False);
            Assert.That(SymbolEqualityComparer.Default.Equals(field.ContainingType, selectedOwner), Is.True);
            Assert.That(field.OriginalDefinition.MetadataToken, Is.EqualTo(imported ? MetadataTokens.GetToken(valueHandle) : 0));
        }

        var actualRead = CSharpOperationSemantics.IsSupportedFieldRead(field);
        var actualWrite = CSharpOperationSemantics.IsSupportedFieldWrite(field, selectedCompilation.Assembly);
        await Save(evidenceDirectory, "admission-observation.json", new
        {
            Case = scenario,
            SourceSha256 = Hash(Encoding.UTF8.GetBytes(source)),
            EmittedPeSha256 = Hash(imageBytes),
            Imported = imported,
            Constructed = constructed,
            OwnerMetadataToken = selectedOwner.MetadataToken,
            OriginalOwnerMetadataToken = selectedOwner.OriginalDefinition.MetadataToken,
            OriginalFieldMetadataToken = field.OriginalDefinition.MetadataToken,
            OwnerModule = selectedOwner.OriginalDefinition.ContainingModule.Name,
            OwnerIsSourceAssembly = SymbolEqualityComparer.Default.Equals(selectedOwner.ContainingAssembly, selectedCompilation.Assembly),
            WriteAssemblyIsConsumerForImported = imported,
            ActualRead = actualRead,
            ActualWrite = actualWrite,
            ExpectedRead = expectedRead,
            ExpectedWrite = expectedWrite,
            AssertionPolicy = "Exact field-admission boundary; no native verdict or CLR alias claim is inferred by this fixture."
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(actualRead, Is.EqualTo(expectedRead));
            Assert.That(actualWrite, Is.EqualTo(expectedWrite));
        }
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        ImmutableArray<MetadataReference> references)
    {
        return CSharpCompilation.Create(assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), "Subject.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static void AssertNoErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(errors, Is.Empty, string.Join("\n", errors.Select(static diagnostic => diagnostic.ToString())));
    }

    private static async Task VerifySourceBinding(
        string evidenceDirectory,
        string scenario,
        CSharpCompilation producer,
        INamedTypeSymbol sourceOwner)
    {
        var attributes = sourceOwner.GetAttributes();
        var constructed = scenario == "metadata-constructed-auto";
        if (constructed)
        {
            Assert.That(attributes, Is.Empty);
            await Save(evidenceDirectory, "source-attribute-binding.json", new { AttributeCount = 0, SourceOwner = sourceOwner.Name });
            return;
        }

        Assert.That(attributes, Has.Length.EqualTo(1));
        var attribute = attributes.Single();
        var attributeType = attribute.AttributeClass!;
        var constructor = attribute.AttributeConstructor!;
        var parameter = constructor.Parameters.Single();
        var layoutKind = producer.GetTypeByMetadataName("System.Runtime.InteropServices.LayoutKind")!;
        var argument = attribute.ConstructorArguments.Single();
        var isOther = scenario == "source-other-attribute";
        var isNested = scenario == "source-nested-attribute";
        var expectedKind = scenario.Contains("auto", StringComparison.Ordinal) ? 3
            : scenario.Contains("sequential", StringComparison.Ordinal) ? 0 : 2;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attributeType.Name, Is.EqualTo("StructLayoutAttribute"));
            Assert.That(SymbolEqualityComparer.Default.Equals(constructor.ContainingType, attributeType), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(parameter.Type, layoutKind), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(attributeType.BaseType, producer.GetTypeByMetadataName("System.Attribute")), Is.True);
            Assert.That(parameter.RefKind, Is.EqualTo(RefKind.None));
            Assert.That(layoutKind.TypeKind, Is.EqualTo(TypeKind.Enum));
            Assert.That(layoutKind.EnumUnderlyingType!.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(argument.Kind, Is.EqualTo(TypedConstantKind.Enum));
            Assert.That(SymbolEqualityComparer.Default.Equals(argument.Type, layoutKind), Is.True);
            Assert.That(argument.Value, Is.EqualTo(expectedKind));
            Assert.That(SymbolEqualityComparer.Default.Equals(attributeType.ContainingAssembly, producer.Assembly), Is.EqualTo(isOther || isNested));
        }

        if (isOther)
        {
            Assert.That(attributeType.ContainingNamespace.Name, Is.EqualTo("Other"));
            Assert.That(attributeType.ContainingNamespace.ContainingNamespace.IsGlobalNamespace, Is.True);
        }
        else if (isNested)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(attributeType.ContainingType!.Name, Is.EqualTo("InteropServices"));
                Assert.That(attributeType.ContainingNamespace.Name, Is.EqualTo("Runtime"));
                Assert.That(attributeType.ContainingNamespace.ContainingNamespace.Name, Is.EqualTo("System"));
                Assert.That(attributeType.ContainingNamespace.ContainingNamespace.ContainingNamespace.IsGlobalNamespace, Is.True);
            }
        }
        else
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(attributeType.ContainingType, Is.Null);
                Assert.That(attributeType.ContainingNamespace.Name, Is.EqualTo("InteropServices"));
                Assert.That(attributeType.ContainingNamespace.ContainingNamespace.Name, Is.EqualTo("Runtime"));
                Assert.That(attributeType.ContainingNamespace.ContainingNamespace.ContainingNamespace.Name, Is.EqualTo("System"));
                Assert.That(attributeType.ContainingNamespace.ContainingNamespace.ContainingNamespace.ContainingNamespace.IsGlobalNamespace, Is.True);
            }
        }

        await Save(evidenceDirectory, "source-attribute-binding.json", new
        {
            SourceOwner = sourceOwner.Name,
            AttributeName = attributeType.Name,
            AttributeContainingType = attributeType.ContainingType?.Name,
            AttributeNamespace = attributeType.ContainingNamespace.Name,
            AttributeAssembly = attributeType.ContainingAssembly.Identity.Name,
            AttributeIsSourceDefined = isOther || isNested,
            ConstructorParameters = constructor.Parameters.Length,
            ParameterMetadataName = layoutKind.MetadataName,
            ParameterAssembly = layoutKind.ContainingAssembly.Identity.Name,
            ParameterEnumUnderlyingType = layoutKind.EnumUnderlyingType!.SpecialType.ToString(),
            ArgumentKind = argument.Kind.ToString(),
            ArgumentValue = (int)argument.Value!,
            CompilerRecognizedNestedQualifier = isNested,
            UnrelatedQualifier = isOther
        });
    }

    private static string Source(string scenario)
    {
        return scenario switch
        {
            "source-auto" or "metadata-auto" => AutoSource,
            "source-sequential" or "metadata-sequential" => SequentialSource,
            "source-explicit" or "metadata-explicit" => ExplicitSource,
            "source-other-attribute" => OtherSource,
            "source-nested-attribute" => NestedSource,
            "metadata-constructed-auto" => GenericSource,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value));
    }
}
