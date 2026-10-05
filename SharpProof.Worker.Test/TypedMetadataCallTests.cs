using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Frontend;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class TypedMetadataCallTests
{
    [TestCase("int", "int", "value & int.MaxValue", -1, int.MaxValue)]
    [TestCase("uint", "uint", "value & 0x80000000U", uint.MaxValue, 0x80000000U)]
    [TestCase("long", "long", "value & long.MaxValue", -1L, long.MaxValue)]
    [TestCase("ulong", "ulong", "value & 0x8000000000000000UL", ulong.MaxValue, 0x8000000000000000UL)]
    [TestCase("bool", "bool", "value & !value", true, false)]
    [TestCase("sbyte", "sbyte", "unchecked((sbyte)(value + 1))", 127, -128)]
    [TestCase("byte", "byte", "checked((byte)(value + 1))", 255, typeof(OverflowException))]
    [TestCase("int", "byte", "value * value", 255, 65025)]
    [TestCase("ushort", "ushort", "checked((ushort)(value * 2))", 32768, typeof(OverflowException))]
    [TestCase("uint", "uint", "unchecked(value + 1U)", uint.MaxValue, 0U)]
    [TestCase("ulong", "ulong", "unchecked(value + 1UL)", ulong.MaxValue, 0UL)]
    [TestCase("ulong", "int", "unchecked((ulong)value)", -1, ulong.MaxValue)]
    [TestCase("ulong", "uint", "value", uint.MaxValue, 4294967295UL)]
    [TestCase("long", "ulong", "unchecked((long)value)", ulong.MaxValue, -1L)]
    [TestCase("long", "long", "unchecked(-value)", long.MinValue, long.MinValue)]
    [TestCase("int", "int", "value / -1", int.MinValue, typeof(OverflowException))]
    [TestCase("int", "int", "value % -1", int.MinValue, typeof(OverflowException))]
    [TestCase("uint", "uint", "value / 2U", uint.MaxValue, 2147483647U)]
    [TestCase("bool", "bool", "!value", true, false)]
    [TestCase("int", "int", "checked(value + 1)", int.MaxValue, typeof(OverflowException))]
    [TestCase("int", "int", "checked(value - 1)", int.MinValue, typeof(OverflowException))]
    [TestCase("uint", "uint", "checked(value - 1U)", 0U, typeof(OverflowException))]
    [TestCase("long", "long", "checked(value * -1L)", long.MinValue, typeof(OverflowException))]
    [TestCase("long", "long", "checked(value * -2L)", 2L, -4L)]
    [TestCase("long", "long", "checked(value * -2L)", -2L, 4L)]
    [TestCase("ulong", "ulong", "checked(value * 2UL)", ulong.MaxValue, typeof(OverflowException))]
    [TestCase("ulong", "ulong", "checked(value * 0UL)", ulong.MaxValue, 0UL)]
    [TestCase("short", "int", "checked((short)value)", 32767, 32767)]
    [TestCase("short", "int", "checked((short)value)", 32768, typeof(OverflowException))]
    [TestCase("sbyte", "uint", "checked((sbyte)value)", uint.MaxValue, typeof(OverflowException))]
    [TestCase("byte", "long", "unchecked((byte)value)", -1L, 255)]
    [TestCase("uint", "long", "unchecked((uint)value)", -1L, uint.MaxValue)]
    [TestCase("long", "uint", "value", uint.MaxValue, 4294967295L)]
    [TestCase("long", "ulong", "checked((long)value)", ulong.MaxValue, typeof(OverflowException))]
    [TestCase("ulong", "long", "checked((ulong)value)", -1L, typeof(OverflowException))]
    [TestCase("bool", "uint", "value > 2147483647U", uint.MaxValue, true)]
    [TestCase("int", "int", "value > 0 ? value + 2 : value - 2", -3, -5)]
    [TestCase("uint", "uint", "value > 2147483647U ? value - 1U : value + 1U", uint.MaxValue, uint.MaxValue - 1U)]
    public void CompiledMetadataScalarMatchesOwnedOriginal(string resultType, string parameterType,
        string expression, object input, object expected)
    {
        using var subject = new MetadataTestSubject(
            $"public static class Library {{ public static {resultType} Target({parameterType} value) {{ return {expression}; }} }}",
            $$"""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject { public static {{resultType}} Target({{parameterType}} value) {
                Contract.Ensures(true); return Library.Target(value);
            } }
            """);
        var runtime = subject.Invoke(input);
        if (expected is Type exceptionType)
        { Assert.That(runtime, Is.TypeOf(exceptionType)); }
        else
        { Assert.That(runtime, Is.EqualTo(expected)); }
        var artifact = subject.CreateArtifact();
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var total = preparations.Single(callable => callable.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal)).Total!;
        Assert.That(total, Is.Not.Null);
        var factory = total.Program.Factory;
        var entry = total.Parameters.Single().Entry;
        var type = factory.GetVariableInfo(entry).Type;
        var value = input is bool boolean ? factory.CreateBooleanValue(boolean) : input is ulong unsigned
            ? factory.CreateIntegerValue(type, unsigned) : factory.CreateIntegerValue(type, Convert.ToInt64(input, CultureInfo.InvariantCulture));
        var execution = new IrProgramInterpreter(factory).Execute(total.Program, new Dictionary<IrVarId, IrValue> { [entry] = value });
        if (expected is Type)
        { Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow)); }
        else if (expected is bool expectedBoolean)
        { Assert.That(execution.ReturnValue!.Boolean, Is.EqualTo(expectedBoolean)); }
        else
        { Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(expected is ulong number ? new System.Numerics.BigInteger(number) : new System.Numerics.BigInteger(Convert.ToInt64(expected, CultureInfo.InvariantCulture)))); }
        TestContext.Out.WriteLine($"typed IL baseline: result={resultType} parameter={parameterType} bytes={subject.Method.GetMethodBody()!.GetILAsByteArray()!.Length} runtime={runtime}");
    }

    [Test]
    public void CompiledMetadataDependencyHasExactOwnedOriginal()
    {
        using var subject = new MetadataTestSubject(
            "public static class Library { public static int Target(int value) { return Again(value); } private static int Again(int value) { return checked(value + 1); } }",
            """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int value) {
                Contract.Ensures(Contract.Result<int>() == 1); return Forward(value);
            } private static int Forward(int value) { return Library.Target(value); } }
            """);
        Assert.That(subject.Invoke(0), Is.EqualTo(1));
        Assert.That(subject.InvokeRoot(0), Is.EqualTo(1));
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(subject.CreateArtifact()), out var preparations);
        var total = preparations.Single(callable => callable.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal)).Total!;
        Assert.That(total, Is.Not.Null);
        var input = total.Parameters.Single().Entry;
        var execution = new IrProgramInterpreter(total.Program.Factory).Execute(total.Program,
            new Dictionary<IrVarId, IrValue> { [input] = total.Program.Factory.CreateIntegerValue(total.Program.Factory.GetVariableInfo(input).Type, 0) });
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Integer, Is.EqualTo(1));
    }

    [Test]
    public void ModuleInitializerRemainsAbstract()
    {
        using var subject = new MetadataTestSubject("""
            public static class Boot {
                public static int State;
                [System.Runtime.CompilerServices.ModuleInitializer] public static void Initialize() { State = 5; }
            }
            public static class Library { public static int Target(int value) { return value; } }
            """, """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(true); return Library.Target(x); } }
            """);
        Assert.That(subject.Invoke(3), Is.EqualTo(3));
        Assert.That(subject.Compilation.GetTypeByMetadataName("Library")!.StaticConstructors, Is.Empty);
        Assert.That(CompilerTotalCallableArtifactTests.IsAbstractOrOpaque(subject.CreateArtifact().Callables.Single().Total!), Is.True);
    }

    [TestCase(-1)]
    [TestCase(2)]
    public void InvalidBranchTargetClosesBeforeFrameAllocation(int target)
    {
        var compilation = TestCompilation.Create("Shape", "public static class Subject { public static void Target() { } }");
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var body = new TotalIlBody(method, new string('0', 64), "shape", [], true, 0,
            [new(0, "Br", 0, target), new(1, "Ret", 0)]);
        Assert.That(TotalIlStack.TryValidate(body, _ => true, out _), Is.False);
    }

    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(256, false)]
    public void NoncanonicalBooleanReturnRemainsClosed(int literal, bool admitted)
    {
        var compilation = TestCompilation.Create("Shape", "public static class Subject { public static bool Target() { return true; } }");
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var body = new TotalIlBody(method, new string('0', 64), "shape", [], true, 1,
            [new(0, "Ldc_i4", literal), new(1, "Ret", 0)]);
        Assert.That(TotalIlStack.TryValidate(body, _ => true, out _), Is.EqualTo(admitted));
    }

    [TestCase("stack")]
    [TestCase("branch")]
    [TestCase("opcode")]
    [TestCase("locals")]
    public void MalformedImplementationBodyCannotSupplyConcreteEvidence(string mutation)
    {
        var library = mutation == "locals"
            ? "public static class Library { public static int Target(int value) { int result = 0; for (int i = 0; i < value; i++) result += i; return result; } }"
            : "public static class Library { public static int Target(int value) { return value; } }";
        using var subject = new MetadataTestSubject(library, """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(true); return Library.Target(x); } }
            """);
        subject.MutateImage((bytes, header, il) =>
        {
            switch (mutation)
            {
                case "stack":
                    bytes[il] = 0x26;
                    break; // pop with an empty entry stack
                case "branch":
                    bytes[il] = 0x2b;
                    bytes[il + 1] = 0xfc;
                    break; // negative short target
                case "opcode":
                    bytes[il] = 0x27;
                    break; // unsupported jmp, no operand bytes
                case "locals":
                    Assert.That(bytes[header] & 3, Is.EqualTo(3));
                    bytes[header + 8] = 0xff;
                    bytes[header + 9] = 0xff;
                    bytes[header + 10] = 0;
                    bytes[header + 11] = 0x11;
                    break;
            }
        });
        Assert.That(CompilerTotalCallableArtifactTests.IsAbstractOrOpaque(subject.CreateArtifact().Callables.Single().Total!), Is.True);
    }

    [Test]
    public void CapturedImageMismatchClosesAndOwnedDecodedBytesRemainImmutable()
    {
        using var subject = new MetadataTestSubject("public static class Library { public static int Target(int value) { return value; } }", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(true); return Library.Target(x); } }
            """);
        var captured = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var provider = new CompilerTotalIlBodyProvider(subject.Compilation, captured);
        var owned = provider.Resolve(method, CancellationToken.None)!;
        Assert.That(owned, Is.Not.Null);
        Assert.That(owned.ImageSha256, Is.EqualTo(WorkerProtocolJson.ComputeSha256(subject.Image)));
        subject.MutateImage((bytes, _, il) => bytes[il] = 0x26);
        Assert.That(provider.Resolve(method, CancellationToken.None), Is.SameAs(owned));
        Assert.That(owned.Instructions[0].Code, Is.EqualTo("Ldarg"));
        Assert.That(new CompilerTotalIlBodyProvider(subject.Compilation, captured).Resolve(method, CancellationToken.None), Is.Null);
    }

    [TestCase("static Library() { }")]
    [TestCase("public static int State = Initialize(); private static int Initialize() { return 1; }")]
    [TestCase("")]
    public void TypeInitializationAndDependencyAdmissionIsExplicit(string initialization)
    {
        var body = string.IsNullOrEmpty(initialization) ? "return System.Math.Abs(value);" : "return value;";
        using var subject = new MetadataTestSubject($"public static class Library {{ {initialization} public static int Target(int value) {{ {body} }} }}", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(true); return Library.Target(x); } }
            """);
        Assert.That(CompilerTotalCallableArtifactTests.IsAbstractOrOpaque(subject.CreateArtifact().Callables.Single().Total!),
            Is.EqualTo(initialization != "static Library() { }"));
    }

    [Test]
    public void MetadataExceptionRegionsRemainAbstract()
    {
        using var subject = new MetadataTestSubject("""
            public static class Library { public static int Target(int value) {
                try { return 10 / value; } catch (System.DivideByZeroException) { return 1; }
            } }
            """, """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(true); return Library.Target(x); } }
            """);
        Assert.That(subject.Invoke(0), Is.EqualTo(1));
        Assert.That(CompilerTotalCallableArtifactTests.IsAbstractOrOpaque(subject.CreateArtifact().Callables.Single().Total!), Is.True);
    }

    [Test]
    public void TypedIlShapeConstructionIsBoundedAndCancellationIsPreserved()
    {
        var compilation = TestCompilation.Create("Shape", "public static class Subject { public static void Target() { } }");
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var oversized = new TotalIlBody(method, new string('0', 64), "shape", [], true, 0,
            [.. Enumerable.Repeat(new TotalIlInstruction(0, "Nop", 0), RoslynTotalProgramLowerer.MaximumRegionSteps + 1)]);
        Assert.That(TotalIlStack.TryValidate(oversized, _ => true, out var shapes), Is.False);
        Assert.That(shapes, Is.Empty);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var bounded = new TotalIlBody(method, new string('0', 64), "shape", [], true, 0, [new(0, "Ret", 0)]);
        Assert.Throws<OperationCanceledException>((Action)(() =>
        {
            TotalIlStack.TryValidate(bounded, _ =>
        { cancellation.Token.ThrowIfCancellationRequested(); return true; }, out _);
        }));
        Assert.That(TotalIlStack.TryValidate(bounded, _ => false, out _), Is.False);
    }

    [Test]
    public void UnreachableInstructionsAreNotSilentlyAdmitted()
    {
        var compilation = TestCompilation.Create("Shape", "public static class Subject { public static void Target() { } }");
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var body = new TotalIlBody(method, new string('0', 64), "shape", [], true, 0,
            [new(0, "Ret", 0), new(1, "Starg", 999)]);
        Assert.That(TotalIlStack.TryValidate(body, _ => true, out _), Is.False);
    }

    [TestCase("public static int Target(int value, __arglist) { return value; }")]
    [TestCase("[System.Runtime.InteropServices.UnmanagedCallersOnly] public static int Target(int value) { return value; }")]
    [TestCase("[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)] public static int Target(int value) { return value; }")]
    public void UnsupportedCallingConventionsAndSynchronizationStayClosed(string declaration)
    {
        using var subject = new MetadataTestSubject("public static class Library { " + declaration + " }", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(true); return x; } }
            """);
        var captured = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        Assert.That(new CompilerTotalIlBodyProvider(subject.Compilation, captured).Resolve(method, CancellationToken.None), Is.Null);
        Assert.That(subject.CreateArtifact().Callables.Single().Total, Is.Not.Null, "An independent scalar root stays admitted.");
    }

    [TestCase(Platform.AnyCpu)]
    [TestCase(Platform.X64)]
    public void CanonicalManagedImagePlatformsAreAdmitted(Platform platform)
    {
        using var subject = new MetadataTestSubject("public static class Library { public static int Target(int value) { return value; } }", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(true); return Library.Target(x); } }
            """, platform);
        Assert.That(subject.InvokeRoot(3), Is.EqualTo(3));
        Assert.That(subject.CreateArtifact().Callables.Single().Total, Is.Not.Null);
    }

    [TestCase("32-bit")]
    [TestCase("native")]
    [TestCase("arm64")]
    [TestCase("amd64-pe32")]
    public void IncompatibleImplementationImageCannotSupplyConcreteEvidence(string mutation)
    {
        using var subject = new MetadataTestSubject("public static class Library { public static int Target(int value) { return value; } }", """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(true); return Library.Target(x); } }
            """);
        subject.MutateImage((bytes, _, _) =>
        {
            using var pe = new PEReader(new MemoryStream(bytes, writable: false));
            if (mutation is "arm64" or "amd64-pe32")
            {
                var offset = pe.PEHeaders.CoffHeaderStartOffset;
                bytes[offset] = 0x64;
                bytes[offset + 1] = mutation == "arm64" ? (byte)0xaa : (byte)0x86;
            }
            else
            {
                var offset = pe.PEHeaders.CorHeaderStartOffset + 16;
                bytes[offset] = mutation == "32-bit" ? (byte)(bytes[offset] | 2) : (byte)(bytes[offset] & ~1);
            }
        });
        Assert.That(CompilerTotalCallableArtifactTests.IsAbstractOrOpaque(subject.CreateArtifact().Callables.Single().Total!), Is.True);
    }

    [Test]
    public void SourceAndMetadataFramesShareOneConstructionLimit()
    {
        var steps = string.Concat(Enumerable.Repeat("value++;", 50));
        var library = $"public static class Library {{ public static int Target(int value) {{ {steps} return value; }} }}";
        using var single = new MetadataTestSubject(library, """
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int x) { Contract.Ensures(true); return Library.Target(x); } }
            """);
        Assert.That(single.CreateArtifact().Callables.Single().Total, Is.Not.Null, "The IL frame fits on its own.");
        var sourceSteps = string.Concat(Enumerable.Repeat("value++;", 1000));
        using var sourceOnly = new MetadataTestSubject(library, $$"""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int value) { Contract.Ensures(true); {{sourceSteps}} return value; } }
            """);
        using var combined = new MetadataTestSubject(library, $$"""
            using SharpProof.Attributes;
            public static class Subject { public static int Target(int value) { Contract.Ensures(true); {{sourceSteps}} return Library.Target(value); } }
            """);
        Assert.That(sourceOnly.CreateArtifact().Callables.Single().Total, Is.Not.Null, "The source frame fits on its own.");
        Assert.That(combined.CreateArtifact().Callables.Single().Total, Is.Null, "The combined expansion exceeds the shared unchanged limit.");
    }
}

internal sealed class MetadataTestSubject : IDisposable
{
    private readonly TempDirectory _directory = new("sharpproof-typed-il-");
    private readonly AssemblyLoadContext _runtime = new("sharpproof-typed-il-oracle", isCollectible: true);
    internal CSharpCompilation Compilation { get; }
    internal MethodInfo Method { get; }
    internal string ImagePath { get; }
    internal byte[] Image { get; }
    internal MethodInfo RootMethod { get; }

    internal MetadataTestSubject(string librarySource, string source, Platform platform = Platform.AnyCpu)
    {
        try
        {
            var implementation = TestCompilation.Create("TypedIlLibrary", librarySource, includeSharpProofReference: false)
                .WithAssemblyName("TypedIlLibrary")
                .WithOptions(TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary).WithOptimizationLevel(OptimizationLevel.Release).WithDeterministic(true).WithPlatform(platform));
            using var stream = new MemoryStream();
            var emitted = implementation.Emit(stream);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
            Image = stream.ToArray();
            ImagePath = Path.Combine(_directory.FullName, "implementation.dll");
            File.WriteAllBytes(ImagePath, Image);
            using var runtimeImage = new MemoryStream(Image, writable: false);
            var assembly = _runtime.LoadFromStream(runtimeImage);
            Method = assembly.GetType("Library")!.GetMethod("Target")!;
            Compilation = CSharpCompilation.Create("TypedIlSubject_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12), Path.Combine(_directory.FullName, "Subject.cs"))],
                TestMetadataReferences.WithSharpProof.Add(MetadataReference.CreateFromFile(ImagePath)),
                TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary, NullableContextOptions.Enable));
            TestCompilation.AssertNoErrors(Compilation);
            using var root = new MemoryStream();
            var rootEmitted = Compilation.Emit(root);
            Assert.That(rootEmitted.Success, Is.True, string.Join("\n", rootEmitted.Diagnostics));
            root.Position = 0;
            RootMethod = _runtime.LoadFromStream(root).GetType("Subject")!.GetMethod("Target")!;
        }
        catch
        { Dispose(); throw; }
    }

    internal object? Invoke(object input)
    {
        var value = Convert.ChangeType(input, Method.GetParameters().Single().ParameterType, CultureInfo.InvariantCulture);
        try
        { return Method.Invoke(null, [value]); }
        catch (TargetInvocationException exception)
        { return exception.InnerException; }
    }

    internal CompilerManifestArtifact CreateArtifact()
    {
        return CompilerManifestArtifactProducer.Create(Compilation, _directory.FullName, "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(Compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
    }

    internal object? InvokeRoot(object input)
    {
        var value = Convert.ChangeType(input, RootMethod.GetParameters().Single().ParameterType, CultureInfo.InvariantCulture);
        try
        { return RootMethod.Invoke(null, [value]); }
        catch (TargetInvocationException exception) { return exception.InnerException; }
    }

    internal void MutateImage(Action<byte[], int, int> mutation)
    {
        var bytes = Image.ToArray();
        int header;
        using (var image = new PEReader(new MemoryStream(Image, writable: false)))
        {
            var definition = image.GetMetadataReader().GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(Method.MetadataToken & 0x00ffffff));
            var section = image.PEHeaders.SectionHeaders[image.PEHeaders.GetContainingSectionIndex(definition.RelativeVirtualAddress)];
            header = checked(definition.RelativeVirtualAddress - section.VirtualAddress + section.PointerToRawData);
        }
        var il = header + ((bytes[header] & 3) == 2 ? 1 : (bytes[header + 1] >> 4) * 4);
        mutation(bytes, header, il);
        File.WriteAllBytes(ImagePath, bytes);
    }

    public void Dispose()
    {
        _runtime.Unload();
        _directory.Dispose();
    }
}
