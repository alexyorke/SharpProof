using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerScalarIlDecoderTests
{
    [Test]
    public void ActualSourceCompilationReferenceRemainsOutsideMetadataBodyAdmission()
    {
        var dependency = TestCompilation.Create("SourceBodyDependency",
            "public static class SourceBody { public static int Read(int value) => value + 1; }");
        TestCompilation.AssertNoErrors(dependency);
        using var dependencyImage = new MemoryStream();
        var dependencyEmit = dependency.Emit(dependencyImage);
        Assert.That(dependencyEmit.Success, Is.True, string.Join(Environment.NewLine, dependencyEmit.Diagnostics));
        Assert.That(dependencyImage.Length, Is.GreaterThan(0));

        var reference = dependency.ToMetadataReference();
        var compilation = CSharpCompilation.Create("SourceBodyConsumer",
            [CSharpSyntaxTree.ParseText(
                "internal static class Caller { internal static int Read(int value) => SourceBody.Read(value); }",
                (CSharpParseOptions)dependency.SyntaxTrees.Single().Options, "source-reference.cs")],
            TestMetadataReferences.WithSharpProof.Add(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: false));
        TestCompilation.AssertNoErrors(compilation);
        using var consumerImage = new MemoryStream();
        var consumerEmit = compilation.Emit(consumerImage);
        Assert.That(consumerEmit.Success, Is.True, string.Join(Environment.NewLine, consumerEmit.Diagnostics));
        Assert.That(consumerImage.Length, Is.GreaterThan(0));
        Assert.That(reference, Is.InstanceOf<CompilationReference>());
        var tree = compilation.SyntaxTrees.Single();
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(invocation);
        Assert.That(operation, Is.InstanceOf<IInvocationOperation>());
        var method = ((IInvocationOperation)operation!).TargetMethod;
        var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, assembly), Is.True);
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.False);
        Assert.That(method.IsStatic, Is.True);
        Assert.That(method.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(method.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(CompilerScalarIlDecoder.Candidate(method), Is.True);
        Assert.That(new CompilerTotalIlBodyProvider(compilation, null).Resolve(method, TestContext.CurrentContext.CancellationToken), Is.Null);

        var trees = CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(trees, Has.Length.EqualTo(compilation.SyntaxTrees.Length));
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            trees, null, CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("UnsupportedSourceReference"));
    }

    [TestCase(127, 128, true)]
    [TestCase(299, 300, false)]
    public void ActualEmittedLongLocalOperandsDoNotBroadenProviderAdmission(
        int declaredLocals, int expectedPeLocals, bool expectedProviderAdmission)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofScalarIlDecoder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var declarations = string.Concat(Enumerable.Range(0, declaredLocals).Select(index => $"int value{index}=seed;\n"));
            var sum = string.Join("+", Enumerable.Range(0, declaredLocals).Select(index => $"value{index}"));
            var dependency = TestCompilation.Create("ScalarIlDependency",
                $"public static class ScalarBody {{ public static int Read(int seed) {{ {declarations} return {sum}; }} }}");
            dependency = dependency.WithOptions(dependency.Options.WithOptimizationLevel(OptimizationLevel.Debug));
            using var imageStream = new MemoryStream();
            var emitted = dependency.Emit(imageStream);
            Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
            var image = imageStream.ToArray();
            var path = Path.Combine(directory, "dependency.dll");
            File.WriteAllBytes(path, image);
            var reference = MetadataReference.CreateFromFile(path);
            var compilation = CSharpCompilation.Create("ScalarIlCaller_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(
                    "internal static class Caller { internal static int Read(int seed) => ScalarBody.Read(seed); }",
                    (CSharpParseOptions)dependency.SyntaxTrees.Single().Options, string.Empty)],
                TestMetadataReferences.WithSharpProof.Add(reference),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: false));
            TestCompilation.AssertNoErrors(compilation);
            var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
            var method = (IMethodSymbol)assembly.GetTypeByMetadataName("ScalarBody")!.GetMembers("Read").Single();
            Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, assembly), Is.True);
            using var pe = new PEReader(new MemoryStream(image, writable: false));
            var reader = pe.GetMetadataReader();
            var definition = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MetadataToken));
            var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
            Assert.That(body.LocalSignature.IsNil, Is.False);
            var localSignature = reader.GetStandaloneSignature(body.LocalSignature);
            var signatureReader = reader.GetBlobReader(localSignature.Signature);
            Assert.That(signatureReader.ReadSignatureHeader().Kind, Is.EqualTo(SignatureKind.LocalVariables));
            Assert.That(signatureReader.ReadCompressedInteger(), Is.EqualTo(expectedPeLocals));
            var locals = localSignature.DecodeLocalSignature(new CompilerScalarSignatureProvider(), genericContext: null);
            Assert.That(locals, Has.Length.EqualTo(expectedPeLocals));
            Assert.That(locals.All(type => type == SpecialType.System_Int32), Is.True);

            var decoded = CompilerScalarIlDecoder.Decode(body, reader, assembly, method.ContainingModule.Name, TestContext.CurrentContext.CancellationToken);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(decoded.IsDefault, Is.False);
                Assert.That(decoded, Is.Not.Empty);
                Assert.That(decoded.Length, Is.LessThanOrEqualTo(4096));
                Assert.That(decoded.Any(instruction => instruction.Code == "Ldloc" && instruction.Operand >= 256), Is.EqualTo(!expectedProviderAdmission));
                Assert.That(decoded.Any(instruction => instruction.Code == "Stloc" && instruction.Operand >= 256), Is.EqualTo(!expectedProviderAdmission));
            }
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>((Action)(() => CompilerScalarIlDecoder.Decode(
                body, reader, assembly, method.ContainingModule.Name, cancellation.Token)));

            var captured = CompilerCompilationCapture.Capture(compilation, directory, "net8.0", [], TestContext.CurrentContext.CancellationToken);
            var provider = new CompilerTotalIlBodyProvider(compilation, captured.References);
            var admitted = provider.Resolve(method, TestContext.CurrentContext.CancellationToken);
            if (!expectedProviderAdmission)
            {
                Assert.That(admitted, Is.Null);
                return;
            }
            Assert.That(admitted, Is.Not.Null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(admitted!.Method, Is.SameAs(method));
                Assert.That(admitted.Locals, Has.Length.EqualTo(expectedPeLocals));
                Assert.That(admitted.Instructions, Is.Not.Empty);
                Assert.That(admitted.Instructions.Length, Is.LessThanOrEqualTo(4096));
                Assert.That(provider.Resolve(method, TestContext.CurrentContext.CancellationToken), Is.SameAs(admitted));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
