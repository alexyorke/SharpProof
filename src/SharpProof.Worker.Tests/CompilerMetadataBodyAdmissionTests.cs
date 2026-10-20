using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerMetadataBodyAdmissionTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void CapturedTreeMismatchHasNamedArgumentError(bool entryOnly)
    {
        var compilation = CreateGuardCompilation();
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        var authority = CompilerSpecificationPackProvider.ResolveConfiguration([]);
        var error = Assert.Throws<ArgumentException>((Action)(() =>
        {
            if (entryOnly)
            {
                CompilerTotalCallableLowerer.PrepareEntry(compilation, target, [], TestContext.CurrentContext.CancellationToken);
            }
            else
            {
                CompilerTotalCallableLowerer.Prepare(compilation, target, [], null,
                    authority, TestContext.CurrentContext.CancellationToken);
            }
        }));
        Assert.That(error!.ParamName, Is.EqualTo("capturedTrees"));
        Assert.That(error.Message, Does.Contain("must match the compilation syntax trees"));
    }

    [Test]
    public void CapturedTreeControlsAdmitCompiledScalarCallable()
    {
        var compilation = CreateGuardCompilation();
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        var trees = CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken);
        var entry = CompilerTotalCallableLowerer.PrepareEntry(compilation, target, trees.ToImmutableArray(), TestContext.CurrentContext.CancellationToken);
        var body = CompilerTotalCallableLowerer.Prepare(compilation, target, trees.ToImmutableArray(), null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken);
        Assert.That(entry, Is.Not.Null);
        Assert.That(body, Is.Not.Null);
        var replay = new IrProgramInterpreter(body!.Program.Factory).Execute(body.Program);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(1));
    }

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

    [Test]
    public void ReturnAttributeDoesNotBecomeAnInputPrecondition()
    {
        CheckAdmission("", "return value;", true, "[return: Positive] ");
    }

    [Test]
    public void OrdinaryScalarDependencyWithoutTheContractApiHasNoInputPreconditions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofPlainMetadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dependency = CreatePlatformCompilation(
                "public static class Dependency { public static int Identity(int value) => value; }", ImmutableArray<MetadataReference>.Empty)
                .WithAssemblyName("PlainMetadataDependency");
            var path = Path.Combine(directory, "dependency.dll");
            File.WriteAllBytes(path, EmitImage(dependency).AsSpan());
            var compilation = CreatePlatformCompilation(
                "internal static class Subject { internal static int Identity(int value) => Dependency.Identity(value); }",
                [MetadataReference.CreateFromFile(path)]);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            Assert.That(compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract"), Is.Null);
            var method = (IMethodSymbol)compilation.GetTypeByMetadataName("Dependency")!.GetMembers("Identity").Single();
            var captured = CompilerCompilationCapture.Capture(compilation, directory, "net8.0", [], TestContext.CurrentContext.CancellationToken);
            var provider = new CompilerTotalIlBodyProvider(compilation, captured.References);
            var body = provider.Resolve(method, TestContext.CurrentContext.CancellationToken);
            Assert.That(body, Is.Not.Null);
            var admitted = body!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(admitted.Method, Is.SameAs(method));
                Assert.That(admitted.ParameterAttributes, Is.Empty);
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

    [TestCase(3, 1021, true)]
    [TestCase(3, 1022, false)]
    [TestCase(1, 1023, true)]
    [TestCase(1, 1024, false)]
    public void OrdinaryAnnotationRowsRespectTheClosedAttributeInventoryLimit(
        int parameters, int annotations, bool expectedAdmitted)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofAnnotationRows-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var tags = string.Concat(Enumerable.Repeat("[Tag]", annotations));
            var parameterSource = string.Join(",", Enumerable.Range(0, parameters)
                .Select(index => (index == 0 ? tags : "") + $" int value{index}"));
            var dependency = CreateCompilation(
                "[System.AttributeUsage(System.AttributeTargets.Parameter, AllowMultiple=true)] " +
                "public sealed class TagAttribute : System.Attribute { } " +
                $"public static class AnnotationSubject {{ public static int Identity({parameterSource}) => value0; }}", ImmutableArray<MetadataReference>.Empty)
                .WithAssemblyName("OrdinaryAnnotationDependency");
            var image = EmitImage(dependency);
            var path = Path.Combine(directory, "dependency.dll");
            File.WriteAllBytes(path, image.AsSpan());
            var arguments = string.Join(",", Enumerable.Range(0, parameters).Select(_ => "1"));
            var compilation = CreateCompilation(
                $"internal static class Caller {{ internal static int Invoke() => AnnotationSubject.Identity({arguments}); }}",
                [MetadataReference.CreateFromFile(path)]);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            var method = (IMethodSymbol)compilation.GetTypeByMetadataName("AnnotationSubject")!
                .GetMembers("Identity").Single();
            Assert.That(method.Parameters[0].GetAttributes(), Has.Length.EqualTo(annotations));
            Assert.That(method.Parameters[0].GetAttributes().All(attribute => attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeClass!.Name == "TagAttribute")), Is.True);
            using var pe = new PEReader(image);
            var reader = pe.GetMetadataReader();
            var definition = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MetadataToken));
            var rows = definition.GetParameters().Select(handle => ((handle.IsNil) ? throw new System.ArgumentException("Value must be initialized.", nameof(handle)) : (reader.GetParameter(handle)))).ToImmutableArray();
            var attributeRows = rows.Sum(row => ((row.Equals(default(Parameter))) ? throw new System.ArgumentException("Value must be initialized.", nameof(row)) : (row.GetCustomAttributes().Count)));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(rows.Select(row => ((row.Equals(default(Parameter))) ? throw new System.ArgumentException("Value must be initialized.", nameof(row)) : (row.SequenceNumber))), Is.EqualTo(Enumerable.Range(1, parameters)));
                Assert.That(rows, Has.Length.EqualTo(parameters));
                Assert.That(rows[0].GetCustomAttributes().Count, Is.EqualTo(annotations));
                Assert.That(attributeRows, Is.EqualTo(annotations));
                Assert.That(rows.Length + attributeRows, Is.EqualTo(expectedAdmitted ? 1024 : 1025));
            }
            var captured = CompilerCompilationCapture.Capture(compilation, directory, "net8.0", [], TestContext.CurrentContext.CancellationToken);
            var provider = new CompilerTotalIlBodyProvider(compilation, captured.References);
            var body = provider.Resolve(method, TestContext.CurrentContext.CancellationToken);
            if (!expectedAdmitted)
            {
                Assert.That(body, Is.Null);
                return;
            }
            Assert.That(body, Is.Not.Null);
            var admitted = body!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(admitted.Method, Is.SameAs(method));
                Assert.That(admitted.ParameterAttributes, Is.Empty);
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

    [TestCase(128, true)]
    [TestCase(129, false)]
    public void OrdinaryOwnerAncestryRespectsTheInitializationLimit(int owners, bool expectedAdmitted)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofOwnerAncestry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var names = Enumerable.Range(0, owners).Select(index => $"N{index}").ToImmutableArray();
            var source = string.Concat(names.Select(name => name is null ? throw new System.ArgumentNullException(nameof(name)) : ($"public class {name} {{\n"))) +
                "public static int Identity([SharpProof.Attributes.Positive] int value) { return value; }\n" +
                new string('}', owners);
            var dependency = CreateCompilation(source, ImmutableArray<MetadataReference>.Empty).WithAssemblyName("OwnerAncestryDependency");
            dependency = dependency.WithOptions(dependency.Options.WithOptimizationLevel(OptimizationLevel.Debug));
            var image = EmitImage(dependency);
            var path = Path.Combine(directory, "dependency.dll");
            File.WriteAllBytes(path, image.AsSpan());
            var compilation = CreateCompilation(
                $"internal static class Subject {{ internal static int Identity(int value) => {string.Join('.', names)}.Identity(value); }}",
                [MetadataReference.CreateFromFile(path)]);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            var method = (IMethodSymbol)compilation.GetTypeByMetadataName(string.Join('+', names))!
                .GetMembers("Identity").Single();
            Assert.That(method.ContainingType.IsGenericType, Is.False);
            using var pe = new PEReader(image);
            var reader = pe.GetMetadataReader();
            var definition = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(method.MetadataToken));
            var ancestry = 0;
            for (var current = definition.GetDeclaringType(); !current.IsNil;
                current = reader.GetTypeDefinition(current).GetDeclaringType())
            {
                ancestry++;
                Assert.That(reader.GetTypeDefinition(current).GetMethods().Any(handle =>
                    reader.GetString(reader.GetMethodDefinition(handle).Name) == ".cctor"), Is.False);
            }
            Assert.That(ancestry, Is.EqualTo(owners));
            var captured = CompilerCompilationCapture.Capture(compilation, directory, "net8.0", [], TestContext.CurrentContext.CancellationToken);
            var provider = new CompilerTotalIlBodyProvider(compilation, captured.References);
            var body = provider.Resolve(method, TestContext.CurrentContext.CancellationToken);
            if (!expectedAdmitted)
            {
                Assert.That(body, Is.Null);
                return;
            }
            Assert.That(body, Is.Not.Null);
            var admitted = body!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(admitted.Method, Is.SameAs(method));
                Assert.That(admitted.ParameterAttributes, Has.Length.EqualTo(1));
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

    private static void CheckAdmission(string initialization, string statements, bool expectedAdmitted,
        string returnAttributes = "")
    {
        System.ArgumentNullException.ThrowIfNull(initialization);
        System.ArgumentNullException.ThrowIfNull(returnAttributes);
        System.ArgumentNullException.ThrowIfNull(statements);
        var directory = Path.Combine(Path.GetTempPath(), "SharpProofMetadataAdmission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var dependency = CreateCompilation(
                $$"""
                using SharpProof.Attributes;
                public static class Dependency {
                    {{initialization}}
                    {{returnAttributes}}public static int Identity([Positive] int value) {
                        {{statements}}
                    }
                }
                """, ImmutableArray<MetadataReference>.Empty).WithAssemblyName("MetadataAdmissionDependency");
            dependency = dependency.WithOptions(dependency.Options.WithOptimizationLevel(OptimizationLevel.Debug));
            var path = Path.Combine(directory, "dependency.dll");
            File.WriteAllBytes(path, EmitImage(dependency).AsSpan());
            var compilation = CreateCompilation(
                "internal static class Subject { internal static int Identity(int value) => Dependency.Identity(value); }",
                [MetadataReference.CreateFromFile(path)]);
            Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
            var method = (IMethodSymbol)compilation.GetTypeByMetadataName("Dependency")!
                .GetMembers("Identity").Single();
            Assert.That(method.GetReturnTypeAttributes(), Has.Length.EqualTo(returnAttributes.Length == 0 ? 0 : 1));
            var captured = CompilerCompilationCapture.Capture(compilation, directory, "net8.0", [], TestContext.CurrentContext.CancellationToken);
            var provider = new CompilerTotalIlBodyProvider(compilation, captured.References);
            var body = provider.Resolve(method, TestContext.CurrentContext.CancellationToken);
            if (!expectedAdmitted)
            {
                Assert.That(body, Is.Null);
                return;
            }
            Assert.That(body, Is.Not.Null);
            var admitted = body!;
            var module = captured.References.SelectMany(reference => reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.Modules))
                .Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (Path.GetFullPath(candidate.Path) == Path.GetFullPath(path)));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(admitted.Method, Is.SameAs(method));
                Assert.That(admitted.ImageSha256, Is.EqualTo(module.Sha256));
                Assert.That(admitted.ModuleMvid, Is.EqualTo(module.Mvid));
                Assert.That(admitted.ParameterAttributes, Has.Length.EqualTo(1));
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

    private static CSharpCompilation CreateCompilation(string source, ImmutableArray<MetadataReference> additionalReferences)
    {
        if (additionalReferences.IsDefault)
        { throw new ArgumentNullException(nameof(additionalReferences)); }
        System.ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), "input.cs");
        return CSharpCompilation.Create("AnalyzerFixture", [tree],
            TestMetadataReferences.WithSharpProof.AddRange(additionalReferences),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static CSharpCompilation CreatePlatformCompilation(string source, ImmutableArray<MetadataReference> additionalReferences)
    {
        if (additionalReferences.IsDefault)
        { throw new ArgumentNullException(nameof(additionalReferences)); }
        System.ArgumentNullException.ThrowIfNull(source);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), "input.cs");
        return CSharpCompilation.Create("AnalyzerFixture", [tree],
            TestMetadataReferences.WithoutSharpProof.AddRange(additionalReferences),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static CSharpCompilation CreateGuardCompilation()
    {
        var compilation = TestCompilation.CreateExact("DefensiveGuardControl", [("Subject.cs", """
            using SharpProof.Attributes;
            public static class Subject
            {
                [ZeroAllocations]
                public static int Root() => 1;
            }
            """)]);
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }

    private static ImmutableArray<byte> EmitImage(CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine,
                result.Diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
        }
        return ImmutableArray.Create<byte>(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
}
