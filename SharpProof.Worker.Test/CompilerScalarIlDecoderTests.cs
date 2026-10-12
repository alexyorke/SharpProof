using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerScalarIlDecoderTests
{
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
            var compilation = TestCompilation.Create("ScalarIlCaller",
                "internal static class Caller { internal static int Read(int seed) => ScalarBody.Read(seed); }")
                .AddReferences(reference);
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

            var decoded = CompilerScalarIlDecoder.Decode(body, reader, assembly, method.ContainingModule.Name, CancellationToken.None);
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
            Assert.Throws<OperationCanceledException>(() => CompilerScalarIlDecoder.Decode(
                body, reader, assembly, method.ContainingModule.Name, cancellation.Token));

            var captured = CompilerCompilationCapture.Capture(compilation, directory, "net8.0", [], CancellationToken.None);
            var provider = new CompilerTotalIlBodyProvider(compilation, captured.References);
            var admitted = provider.Resolve(method, CancellationToken.None);
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
                Assert.That(provider.Resolve(method, CancellationToken.None), Is.SameAs(admitted));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
