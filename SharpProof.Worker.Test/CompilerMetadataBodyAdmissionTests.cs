using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Testing;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerMetadataBodyAdmissionTests
{
    [TestCase("", true)]
    [TestCase("static Dependency() { }", true)]
    [TestCase("private static int State; static Dependency() { State = 1; }", false)]
    [TestCase("[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)] static Dependency() { }", false)]
    [TestCase("[System.Runtime.CompilerServices.ModuleInitializer] public static void Initialize() { }", false)]
    public void OrdinaryInitializationPreservesMetadataBodyAdmission(string initialization, bool expectedAdmitted)
    {
        CheckAdmission(initialization, "return value;", expectedAdmitted);
    }

    [TestCase(512, true)]
    [TestCase(1100, false)]
    public void OrdinaryInstructionCountsRespectTheExistingDecodeLimit(int increments, bool expectedAdmitted)
    {
        var statements = string.Concat(Enumerable.Repeat("value += 1;\n", increments));
        CheckAdmission("", statements + "return value;", expectedAdmitted);
    }

    private static void CheckAdmission(string initialization, string statements, bool expectedAdmitted)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofMetadataAdmission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dependency = CreateCompilation(
                $$"""
                using SharpProof.Attributes;
                public static class Dependency {
                    {{initialization}}
                    public static int Identity([Positive] int value) {
                        {{statements}}
                    }
                }
                """).WithAssemblyName("MetadataAdmissionDependency");
            dependency = dependency.WithOptions(dependency.Options.WithOptimizationLevel(OptimizationLevel.Debug));
            var path = Path.Combine(directory, "dependency.dll");
            File.WriteAllBytes(path, EmitImage(dependency));
            var compilation = CreateCompilation(
                "internal static class Subject { internal static int Identity(int value) => Dependency.Identity(value); }",
                MetadataReference.CreateFromFile(path));
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
            var method = (IMethodSymbol)compilation.GetTypeByMetadataName("Dependency")!
                .GetMembers("Identity").Single();
            var captured = CompilerCompilationCapture.Capture(compilation, directory, "net8.0", [], CancellationToken.None);
            var provider = new CompilerTotalIlBodyProvider(compilation, captured.References);
            var body = provider.Resolve(method, CancellationToken.None);
            if (!expectedAdmitted)
            {
                Assert.That(body, Is.Null);
                return;
            }
            Assert.That(body, Is.Not.Null);
            var admitted = body!;
            var module = captured.References.SelectMany(reference => reference.Modules)
                .Single(candidate => Path.GetFullPath(candidate.Path) == Path.GetFullPath(path));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(admitted.Method, Is.SameAs(method));
                Assert.That(admitted.ImageSha256, Is.EqualTo(module.Sha256));
                Assert.That(admitted.ModuleMvid, Is.EqualTo(module.Mvid));
                Assert.That(admitted.ParameterAttributes, Has.Length.EqualTo(1));
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

    private static CSharpCompilation CreateCompilation(string source, params MetadataReference[] additionalReferences)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), "input.cs");
        return CSharpCompilation.Create("AnalyzerFixture", [tree],
            TestMetadataReferences.WithSharpProof.AddRange(additionalReferences),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static byte[] EmitImage(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        }
        return stream.ToArray();
    }
}
