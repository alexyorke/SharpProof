using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractForMetadataSignatureTests
{
    [TestCase("ReadBounds")]
    [TestCase("ReadModified")]
    public void CompoundMetadataSignatureIdentityMustMatchExactly(
        string methodName)
    {
        var targetReference = MetadataReference.CreateFromImage(
            CreateMetadataTarget());
        var syntaxTree = CSharpSyntaxTree.ParseText(
            """
            using System.Collections.Generic;
            using SharpProof.Attributes;

            [ContractFor(typeof(MetadataTarget))]
            public static class MetadataTargetContracts
            {
                public static void ReadBounds(
                    MetadataTarget receiver,
                    int[,] value)
                {
                }

                public static void ReadModified(
                    MetadataTarget receiver,
                    List<int> value)
                {
                }
            }
            """,
            new CSharpParseOptions(
                LanguageVersion.CSharp12,
                preprocessorSymbols: ["SHARPPROOF_CONTRACTS"]));
        var compilation = CSharpCompilation.Create(
            "CompoundMetadataSignatureIdentity",
            [syntaxTree],
            TestMetadataReferences.WithSharpProof.Add(targetReference),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        ContractTestCompilation.AssertNoErrors(compilation);
        var target = compilation.GetTypeByMetadataName("MetadataTarget")!
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();

        var result = new ContractBinder(compilation, new IrFactory())
            .Bind(target);

        Assert.That(
            result.Failure,
            Is.EqualTo(ContractBindingFailure.CompanionSignatureMismatch));
    }

    private static ImmutableArray<byte> CreateMetadataTarget()
    {
        var metadata = new MetadataBuilder();
        var moduleName = metadata.GetOrAddString(
            "CompoundMetadataSignatureIdentity.dll");
        metadata.AddModule(
            0,
            moduleName,
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("CompoundMetadataSignatureIdentity"),
            new Version(1, 0, 0, 0),
            default,
            default,
            (AssemblyFlags)0,
            System.Reflection.AssemblyHashAlgorithm.None);

        var coreAssemblyName = typeof(object).Assembly.GetName();
        var coreAssembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString(coreAssemblyName.Name!),
            coreAssemblyName.Version!,
            default,
            metadata.GetOrAddBlob(coreAssemblyName.GetPublicKeyToken() ?? []),
            (AssemblyFlags)0,
            default);
        var objectType = metadata.AddTypeReference(
            coreAssembly,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        var listType = metadata.AddTypeReference(
            coreAssembly,
            metadata.GetOrAddString("System.Collections.Generic"),
            metadata.GetOrAddString("List`1"));
        var modifierType = MetadataTokens.TypeDefinitionHandle(2);
        var firstMethod = MetadataTokens.MethodDefinitionHandle(1);
        var firstField = MetadataTokens.FieldDefinitionHandle(1);

        var boundsSignature = new BlobBuilder();
        new BlobEncoder(boundsSignature)
            .MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount: 0,
                isInstanceMethod: true)
            .Parameters(
                1,
                static returnType => returnType.Void(),
                static parameters => parameters.AddParameter()
                    .Type()
                    .Array(
                        static element => element.Int32(),
                        static shape => shape.Shape(
                            2,
                            [4],
                            [1])));
        metadata.AddMethodDefinition(
            MethodAttributes.Public |
            MethodAttributes.Abstract |
            MethodAttributes.Virtual |
            MethodAttributes.HideBySig |
            MethodAttributes.NewSlot,
            MethodImplAttributes.Managed,
            metadata.GetOrAddString("ReadBounds"),
            metadata.GetOrAddBlob(boundsSignature),
            0,
            MetadataTokens.ParameterHandle(1));

        var modifiedSignature = new BlobBuilder();
        new BlobEncoder(modifiedSignature)
            .MethodSignature(
                SignatureCallingConvention.Default,
                genericParameterCount: 0,
                isInstanceMethod: true)
            .Parameters(
                1,
                static returnType => returnType.Void(),
                parameters =>
                {
                    var arguments = parameters.AddParameter()
                        .Type()
                        .GenericInstantiation(
                            listType,
                            genericArgumentCount: 1,
                            isValueType: false);
                    var argument = arguments.AddArgument();
                    _ = argument.CustomModifiers()
                        .AddModifier(modifierType, isOptional: true);
                    argument.Int32();
                });
        metadata.AddMethodDefinition(
            MethodAttributes.Public |
            MethodAttributes.Abstract |
            MethodAttributes.Virtual |
            MethodAttributes.HideBySig |
            MethodAttributes.NewSlot,
            MethodImplAttributes.Managed,
            metadata.GetOrAddString("ReadModified"),
            metadata.GetOrAddBlob(modifiedSignature),
            0,
            MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            firstField,
            firstMethod);
        metadata.AddTypeDefinition(
            TypeAttributes.Public |
            TypeAttributes.Sealed |
            TypeAttributes.BeforeFieldInit,
            default,
            metadata.GetOrAddString("MetadataModifier"),
            objectType,
            firstField,
            firstMethod);
        metadata.AddTypeDefinition(
            TypeAttributes.Public |
            TypeAttributes.Interface |
            TypeAttributes.Abstract,
            default,
            metadata.GetOrAddString("MetadataTarget"),
            default,
            firstField,
            firstMethod);

        var peImage = new BlobBuilder();
        var peBuilder = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        _ = peBuilder.Serialize(peImage);
        return peImage.ToImmutableArray();
    }

}
