using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class BoundaryMetadataCompilerCollectorCoverageTests
{
    [TestCase("nil-method")]
    [TestCase("static-field")]
    [TestCase("readonly-field")]
    [TestCase("empty-value-stack")]
    [TestCase("two-values")]
    [TestCase("non-field-signature")]
    public void BoundaryStoresRejectUnownedOrMalformedMetadata(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        using var image = new BoundaryImage(
            mutation == "static-field" ? FieldAttributes.Public | FieldAttributes.Static :
            mutation == "readonly-field" ? FieldAttributes.Public | FieldAttributes.InitOnly : FieldAttributes.Public,
            mutation == "non-field-signature" ? [0x07, 0x08] : [0x06, 0x08]);
        var instructions = new List<CompilerIlToken> { Token(0, "Ldarg_0") };
        if (mutation == "empty-value-stack")
        { instructions.Add(Token(1, "Stloc_0")); }
        else
        { instructions.Add(Token(1, "Ldc_i4_0")); }
        if (mutation == "two-values")
        { instructions.Add(Token(2, "Ldc_i4_1")); }
        instructions.Add(Token(instructions.Count, "Stfld", 0x04000001));

        var result = Invoke<bool>("OwnedBoundaryField", image.Metadata,
            mutation == "nil-method" ? default(MethodDefinitionHandle) : BoundaryImage.Method,
            instructions, instructions.Count - 1, new HashSet<int>(), TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.False, "A rejected field write must not become owned bookkeeping.");
    }

    [Test]
    public void BoundaryFieldLoadsAllowAnOwnedReceiverAcrossHarmlessNops()
    {
        using var image = new BoundaryImage();
        var instructions = new List<CompilerIlToken>
        {
            Token(0, "Ldarg_0"), Token(1, "Nop"), Token(2, "Ldfld", 0x04000001)
        };

        Assert.That(Invoke<bool>("OwnedBoundaryField", image.Metadata, BoundaryImage.Method,
            instructions, 2, new HashSet<int>(), TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(Invoke<bool>("OwnedBoundaryField", image.Metadata, BoundaryImage.Method,
            instructions, 2, new HashSet<int> { 1 }, TestContext.CurrentContext.CancellationToken), Is.False,
            "A branch into the receiver prefix must invalidate ownership.");
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void BoundaryFieldOwnershipRejectsAnIndexOutsideItsDecodedBody(int index)
    {
        using var image = new BoundaryImage();
        var instructions = new List<CompilerIlToken> { Token(0, "Ldfld", 0x04000001) };

        var error = Assert.Throws<TargetInvocationException>((Action)(() => Invoke<bool>("OwnedBoundaryField",
            image.Metadata, BoundaryImage.Method, instructions, index, new HashSet<int>(), TestContext.CurrentContext.CancellationToken)));

        Assert.That(error!.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(((ArgumentOutOfRangeException)error.InnerException!).ParamName, Is.EqualTo("index"));
    }

    [TestCase("wrong-target")]
    [TestCase("non-local-signature")]
    [TestCase("primitive-local")]
    [TestCase("invalid-coded-index")]
    public void BoundaryLocalInitializationRejectsAnUnprovedStorageType(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ImmutableArray<byte> signature = mutation switch
        {
            "non-local-signature" => [0x06, 0x01, 0x11, 0x08],
            "primitive-local" => [0x07, 0x01, 0x08],
            "invalid-coded-index" => [0x07, 0x01, 0x11, 0x07],
            _ => [0x07, 0x01, 0x11, 0x08]
        };
        using var image = new BoundaryImage(localSignature: signature);
        var instructions = new List<CompilerIlToken>
        {
            Token(0, "Ldloca_s"), Token(1, "Initobj", mutation == "wrong-target" ? 0x04000001 : 0x02000002)
        };

        Assert.That(Invoke<bool>("OwnedBoundaryLocalInitialization", image.Metadata, image.Body,
            instructions, 1, new HashSet<int>(), TestContext.CurrentContext.CancellationToken), Is.False,
            "An initobj token alone is not proof that the addressed local has that owned type.");
    }

    [Test]
    public void BoundaryLocalInitializationBindsATypeSpecificationAcrossNops()
    {
        // TypeDefOrRef encoded index 6 denotes TypeSpecification row 1.
        using var image = new BoundaryImage(localSignature: [0x07, 0x01, 0x11, 0x06]);
        var instructions = new List<CompilerIlToken>
        {
            Token(0, "Ldloca_s"), Token(1, "Nop"), Token(2, "Initobj", 0x1b000001)
        };

        Assert.That(Invoke<bool>("OwnedBoundaryLocalInitialization", image.Metadata, image.Body,
            instructions, 2, new HashSet<int>(), TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(Invoke<bool>("OwnedBoundaryLocalInitialization", image.Metadata, image.Body,
            instructions, 2, new HashSet<int> { 1 }, TestContext.CurrentContext.CancellationToken), Is.False);
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void BoundaryLocalInitializationRejectsAnIndexOutsideItsDecodedBody(int index)
    {
        using var image = new BoundaryImage();
        var instructions = new List<CompilerIlToken> { Token(0, "Initobj", 0x02000002) };

        var error = Assert.Throws<TargetInvocationException>((Action)(() => Invoke<bool>("OwnedBoundaryLocalInitialization",
            image.Metadata, image.Body, instructions, index, new HashSet<int>(), TestContext.CurrentContext.CancellationToken)));

        Assert.That(error!.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(((ArgumentOutOfRangeException)error.InnerException!).ParamName, Is.EqualTo("index"));
    }

    [Test]
    public void ExceptionBoundaryRejectsANilOwnedMethodBeforeReadingItsBody()
    {
        using var image = new BoundaryImage();

        var result = CompilerTaskCarrierCollector.ValidateExceptionBoundary(image.Reader, default,
            new AssemblyIdentity("System.Private.CoreLib"), 2, TestContext.CurrentContext.CancellationToken);

        Assert.That(result, Is.EqualTo("TaskRootExceptionBoundaryMethodUnavailable"));
    }

    [TestCase("Code")]
    [TestCase("Instructions")]
    public void ValidatedSourceBodyRejectsADefaultDecodedComponent(string missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        ImmutableArray<byte> code = missing == "Code" ? default : [0x2a];
        ImmutableArray<CompilerTaskPrimitiveIlInstruction> instructions = missing == "Instructions" ? default : [];

        var error = Assert.Throws<ArgumentException>((Action)(() =>
            new CompilerTaskCarrierCollector.ValidatedSourceBody(code, instructions)));

        Assert.That(error!.ParamName, Is.EqualTo(missing));
    }

    [Test]
    public void ValidatedSourceInventoryRejectsADefaultBodyCollection()
    {
        var module = new string('a', 64);
        var inventory = new CompilerTaskSourceBodyInventory("root", module, []);
        var shapes = new CompilerTaskCarrierShapes("root", module,
            typeof(CompilerTaskCarrierCollector).Assembly.FullName!, 0, [], null);
        var framework = new AssemblyIdentity("System.Private.CoreLib");

        var error = Assert.Throws<ArgumentException>((Action)(() =>
            new CompilerTaskCarrierCollector.ValidatedSourceBodies(inventory, shapes, framework.ToString(), framework, default)));

        Assert.That(error!.ParamName, Is.EqualTo("Bodies"));
    }

    private static CompilerIlToken Token(int offset, string code, long operand = 0)
    {
        ArgumentNullException.ThrowIfNull(code);
        return new(offset, code, operand, -1);
    }

    private static T Invoke<T>(string name, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);
        var method = typeof(CompilerTaskCarrierCollector).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, name);
        return (T)method!.Invoke(null, arguments)!;
    }

    private sealed class BoundaryImage : IDisposable
    {
        private readonly MemoryStream _stream;
        internal PEReader Reader { get; }
        internal MetadataReader Metadata => Reader.GetMetadataReader();
        internal static MethodDefinitionHandle Method => MetadataTokens.MethodDefinitionHandle(1);
        internal MethodBodyBlock Body => Reader.GetMethodBody(Metadata.GetMethodDefinition(Method).RelativeVirtualAddress);

        internal BoundaryImage(FieldAttributes fieldAttributes = FieldAttributes.Public,
            ImmutableArray<byte> fieldSignature = default, ImmutableArray<byte> localSignature = default)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("OwnedBoundary.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
            metadata.AddAssembly(metadata.GetOrAddString("OwnedBoundary"), new Version(1, 0, 0, 0), default, default,
                (AssemblyFlags)0, System.Reflection.AssemblyHashAlgorithm.None);
            var coreName = typeof(object).Assembly.GetName();
            var core = metadata.AddAssemblyReference(metadata.GetOrAddString(coreName.Name!), coreName.Version!, default,
                metadata.GetOrAddBlob(ImmutableArray.Create<byte>((coreName.GetPublicKeyToken() ?? []).AsSpan())), (AssemblyFlags)0, default);
            var objectType = metadata.AddTypeReference(core, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
            metadata.AddTypeSpecification(metadata.GetOrAddBlob(ImmutableArray.Create<byte>(0x11, 0x08)));
            metadata.AddFieldDefinition(fieldAttributes, metadata.GetOrAddString("Owned"),
                metadata.GetOrAddBlob(fieldSignature.IsDefault ? ImmutableArray.Create<byte>(0x06, 0x08) : fieldSignature));
            var locals = metadata.AddStandaloneSignature(metadata.GetOrAddBlob(
                localSignature.IsDefault ? ImmutableArray.Create<byte>(0x07, 0x01, 0x11, 0x08) : localSignature));
            metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.HideBySig, MethodImplAttributes.Managed,
                metadata.GetOrAddString("Probe"), metadata.GetOrAddBlob(ImmutableArray.Create<byte>(0x20, 0, 1)), 4,
                MetadataTokens.ParameterHandle(1));
            metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddTypeDefinition(TypeAttributes.Public, default, metadata.GetOrAddString("Boundary"), objectType,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var il = new BlobBuilder();
            il.WriteInt32(0);
            il.WriteUInt16(0x3013); // Fat, initialized-local, three-word method header.
            il.WriteUInt16(8);
            il.WriteInt32(1);
            il.WriteInt32(MetadataTokens.GetToken(locals));
            il.WriteByte(0x2a);
            var image = new BlobBuilder();
            new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll), new MetadataRootBuilder(metadata),
                il, flags: CorFlags.ILOnly).Serialize(image);
            _stream = new MemoryStream(image.ToArray(), false);
            Reader = new PEReader(_stream);
        }

        public void Dispose()
        {
            Reader.Dispose();
            _stream.Dispose();
        }
    }
}
