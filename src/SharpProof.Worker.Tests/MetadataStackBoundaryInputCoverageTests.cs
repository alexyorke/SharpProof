using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class MetadataStackBoundaryInputCoverageTests
{
    // These are intentionally invalid method bodies in ordinary serialized PE
    // inputs. The compiler metadata reader, decoder and CLR consume the same
    // bytes; no trusted IL body or compiler artifact is constructed by this test.
    private static IEnumerable<TestCaseData> Bodies()
    {
        yield return new TestCaseData("argument-index", false);
        yield return new TestCaseData("local-index", false);
        yield return new TestCaseData("empty-dup", false);
        yield return new TestCaseData("merge-depth", false);
        yield return new TestCaseData("merge-type", false);
        yield return new TestCaseData("shared-merge-depth", false);
        yield return new TestCaseData("shared-merge-type", false);
        yield return new TestCaseData("reference-conversion", false);
        yield return new TestCaseData("shift-left-wide-count", false);
        yield return new TestCaseData("shift-right-wide-count", false);
        yield return new TestCaseData("shift-right-unsigned-wide-count", false);
        yield return new TestCaseData("fallthrough-past-body", false);
        yield return new TestCaseData("identity-control", true);
        yield return new TestCaseData("widening-control", true);
    }

    [TestCaseSource(nameof(Bodies))]
    public void ActualPeStackAdmissionAgreesWithRuntime(string name, bool admitted)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        using var directory = new TempDirectory("sharpproof-stack-input-");
        var image = CreateImage(Code(name));
        var path = Path.Combine(directory.FullName, "StackBoundary.dll");
        File.WriteAllBytes(path, image.AsSpan());
        var compilation = CSharpCompilation.Create("StackConsumer",
            [CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int value) => Library.Target(value); }")],
            TestMetadataReferences.WithSharpProof.Add(MetadataReference.CreateFromFile(path)),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var provider = new CompilerTotalIlBodyProvider(compilation, null);
        var session = new TotalSourceCallSession(compilation, null, provider.Resolve, TestContext.CurrentContext.CancellationToken);
        var body = session.PrepareIl(method);
        Assert.That(body, Is.Not.Null, name + " must reach the real decoded stack boundary.");
        Assert.That(body!.ImageSha256.ToUpperInvariant(),
            Is.EqualTo(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.AsSpan()))));
        Assert.That(TotalIlStack.TryValidate(body, session.Spend, out var shapes), Is.EqualTo(admitted));
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        if (name == "widening-control")
        {
            var shared = shapes[^2]!.Value.Single();
            Assert.That(shared.NumericWidth, Is.EqualTo(32));
            Assert.That(shared.IsBooleanCompatibleI4, Is.False);
        }
        var runtime = new AssemblyLoadContext("StackBoundaryOracle", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image.ToArray(), writable: false);
            var target = runtime.LoadFromStream(stream).GetType("Library")!.GetMethod("Target")!;
            if (admitted)
            {
                Assert.That(target.Invoke(null, [17]), Is.EqualTo(name == "widening-control" ? 0 : 17));
                Assert.That(target.Invoke(null, [0]), Is.EqualTo(0));
            }
            else
            {
                var error = Assert.Throws<TargetInvocationException>(new Action(() => target.Invoke(null, [17])));
                Assert.That(error!.InnerException, Is.TypeOf<InvalidProgramException>(), name);
            }
        }
        finally
        { runtime.Unload(); }
    }

    private static ImmutableArray<byte> Code(string name)
    {
        return name switch
        {
            "argument-index" => [0x0e, 0xff, 0x2a],
            "local-index" => [0x11, 0xff, 0x2a],
            "empty-dup" => [0x25, 0x2a],
            "merge-depth" => [0x02, 0x2d, 3, 0x16, 0x2b, 1, 0x00, 0x2a],
            "merge-type" => [0x02, 0x2d, 3, 0x16, 0x2b, 1, 0x14, 0x26, 0x26, 0x2a],
            // The extra nop keeps the shared return pending until both
            // predecessor stacks arrive, before return-type validation.
            "shared-merge-depth" => [0x02, 0x2d, 3, 0x16, 0x2b, 2, 0x00, 0x00, 0x2a],
            "shared-merge-type" => [0x02, 0x2d, 3, 0x16, 0x2b, 2, 0x14, 0x00, 0x26, 0x26, 0x2a],
            // The CLR permits these unverifiable reference operations. A later
            // underflow makes runtime rejection definite while the frontend
            // must still close the earlier reference-conversion boundary.
            "reference-conversion" => [0x14, 0x69, 0x26, 0x26, 0x2a],
            // The CLR accepts these I8 counts. A later underflow makes runtime
            // rejection definite while the frontend still rejects the earlier
            // unsupported count shape. Original admitted bytes are kept in bb2.
            "shift-left-wide-count" => [0x02, 0x21, 1, 0, 0, 0, 0, 0, 0, 0, 0x62, 0x26, 0x26, 0x2a],
            "shift-right-wide-count" => [0x02, 0x21, 1, 0, 0, 0, 0, 0, 0, 0, 0x63, 0x26, 0x26, 0x2a],
            "shift-right-unsigned-wide-count" => [0x02, 0x21, 1, 0, 0, 0, 0, 0, 0, 0, 0x64, 0x26, 0x26, 0x2a],
            "fallthrough-past-body" => [0x02],
            "identity-control" => [0x02, 0x2a],
            // The first queued predecessor supplies a boolean-compatible zero.
            // The delayed int argument widens that stored shape and requeues
            // the shared nop before the return is validated again.
            "widening-control" => [0x02, 0x2d, 4, 0x02, 0x00, 0x2b, 1, 0x16, 0x00, 0x2a],
            _ => throw new ArgumentException("Unknown PE stack input.", nameof(name))
        };
    }

    private static ImmutableArray<byte> CreateImage(ImmutableArray<byte> code)
    {
        if (code.IsDefault)
        { throw new ArgumentNullException(nameof(code)); }
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("StackBoundary.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("StackBoundary"), new Version(1, 0, 0, 0), default, default,
            (AssemblyFlags)0, System.Reflection.AssemblyHashAlgorithm.None);
        var coreName = typeof(object).Assembly.GetName();
        var core = metadata.AddAssemblyReference(metadata.GetOrAddString(coreName.Name!), coreName.Version!, default,
            metadata.GetOrAddBlob(ImmutableArray.Create<byte>((coreName.GetPublicKeyToken() ?? []).AsSpan())), (AssemblyFlags)0, default);
        var objectType = metadata.AddTypeReference(core, metadata.GetOrAddString("System"), metadata.GetOrAddString("Object"));
        var parameter = metadata.AddParameter(ParameterAttributes.None, metadata.GetOrAddString("value"), 1);
        metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            MethodImplAttributes.Managed, metadata.GetOrAddString("Target"), metadata.GetOrAddBlob(ImmutableArray.Create<byte>(0, 1, 8, 8)), 4, parameter);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract, default,
            metadata.GetOrAddString("Library"), objectType, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var il = new BlobBuilder();
        il.WriteBytes(ImmutableArray.Create<byte>(0, 0, 0, 0));
        il.WriteByte(checked((byte)((code.Length << 2) | 2)));
        il.WriteBytes(code);
        var image = new BlobBuilder();
        new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll), new MetadataRootBuilder(metadata),
            il, flags: CorFlags.ILOnly).Serialize(image);
        using var stream = new MemoryStream();
        image.WriteContentTo(stream);
        return ImmutableArray.Create<byte>(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
}
