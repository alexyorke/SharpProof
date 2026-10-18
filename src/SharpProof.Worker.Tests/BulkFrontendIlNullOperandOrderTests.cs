using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class BulkFrontendIlNullOperandOrderTests
{
    private static readonly ImmutableArray<string> ExpectedLeft = ["Ldnull", "Ldarg", "Ceq", "Ret"];
    private static readonly ImmutableArray<string> ExpectedRight = ["Ldarg", "Ldnull", "Ceq", "Ret"];

    [TestCase(true)]
    [TestCase(false)]
    public void FixedPersistedNullComparisonMatchesClrAndWholeIr(bool nullOnLeft)
    {
        using var directory = new TempDirectory("sharpproof-persisted-null-");
        var runtime = new AssemblyLoadContext("persisted-null-" + Guid.NewGuid().ToString("N"),
            isCollectible: true);
        try
        {
            var assemblyName = new AssemblyName("PersistedNull_" + Guid.NewGuid().ToString("N"));
            var builder = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
            var module = builder.DefineDynamicModule(assemblyName.Name!);
            var type = module.DefineType("Library",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var methodBuilder = type.DefineMethod("IsNull",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                typeof(bool), [typeof(string)]);
            var il = methodBuilder.GetILGenerator();
            il.Emit(nullOnLeft ? OpCodes.Ldnull : OpCodes.Ldarg_0);
            il.Emit(nullOnLeft ? OpCodes.Ldarg_0 : OpCodes.Ldnull);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Ret);
            type.CreateType();
            using var emitted = new MemoryStream();
            builder.Save(emitted);
            var image = emitted.ToArray();
            var imagePath = Path.Combine(directory.FullName, "implementation.dll");
            File.WriteAllBytes(imagePath, image);

            using var runtimeImage = new MemoryStream(image, writable: false);
            var runtimeAssembly = runtime.LoadFromStream(runtimeImage);
            var runtimeMethod = runtimeAssembly.GetType("Library")!.GetMethod("IsNull")!;
            Assert.That(runtimeMethod.ReturnType, Is.EqualTo(typeof(bool)));
            Assert.That(runtimeMethod.GetParameters().Single().ParameterType, Is.EqualTo(typeof(string)));
            string?[] inputs = [null, "x"];
            foreach (var input in inputs)
            {
                Assert.That(runtimeMethod.Invoke(null, [input]), Is.EqualTo(input == null));
            }

            const string source = """
                public static class Subject {
                    public static bool Target(string? value) => Library.IsNull(value);
                }
                """;
            var imageReference = MetadataReference.CreateFromFile(imagePath);
            var compilation = CSharpCompilation.Create("PersistedNullSubject_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12),
                    Path.Combine(directory.FullName, "Subject.cs"))],
                TestMetadataReferences.WithSharpProof.Add(imageReference),
                TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary, NullableContextOptions.Enable));
            TestCompilation.AssertNoErrors(compilation);
            var library = compilation.GetTypeByMetadataName("Library")!;
            var metadataMethod = library.GetMembers("IsNull").OfType<IMethodSymbol>().Single();
            var metadataAssembly = compilation.GetAssemblyOrModuleSymbol(imageReference);
            Assert.That(SymbolEqualityComparer.Default.Equals(metadataMethod.ContainingAssembly, metadataAssembly), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(metadataMethod.ContainingAssembly, compilation.Assembly), Is.False);
            Assert.That(metadataMethod.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(metadataMethod.MetadataToken, Is.EqualTo(runtimeMethod.MetadataToken));

            var references = CompilerCompilationCapture.CaptureReferences(compilation.References,
                CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
            var captured = references.SelectMany(reference => reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.Modules)).Single(reference =>
                reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (Path.GetFullPath(reference.Path) == Path.GetFullPath(imagePath)));
            Assert.That(captured.SizeBytes, Is.EqualTo(image.LongLength));
            Assert.That(captured.Sha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(image))));
            Assert.That(captured.Mvid, Is.EqualTo(runtimeAssembly.ManifestModule.ModuleVersionId.ToString("D")));
            Assert.That(captured.Name, Is.EqualTo(metadataMethod.ContainingModule.Name));

            var provider = new CompilerTotalIlBodyProvider(compilation, references);
            var decoded = provider.Resolve(metadataMethod, TestContext.CurrentContext.CancellationToken);
            Assert.That(decoded, Is.Not.Null, "The saved CLR-emitted implementation must pass native file admission.");
            var body = decoded!;
            Assert.That(SymbolEqualityComparer.Default.Equals(body.Method, metadataMethod), Is.True);
            Assert.That(body.ImageSha256, Is.EqualTo(captured.Sha256));
            Assert.That(body.ModuleMvid, Is.EqualTo(captured.Mvid));
            Assert.That(body.Module, Is.EqualTo(captured.Name));
            Assert.That(body.Instructions.Select(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Code)).ToArray(),
                Is.EqualTo(nullOnLeft ? ExpectedLeft : ExpectedRight));
            Assert.That(body.Instructions.Single(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Code == "Ldarg")).Operand, Is.Zero);
            bool PrepareSourceCallee(TotalLoweringContext frame)
            {
                System.ArgumentNullException.ThrowIfNull(frame);
                Assert.That(frame.Target.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
                var sourceDeclaration = frame.Target.DeclaringSyntaxReferences.Single().GetSyntax();
                Assert.That(sourceDeclaration, Is.InstanceOf<MethodDeclarationSyntax>());
                Assert.That(compilation.ContainsSyntaxTree(sourceDeclaration.SyntaxTree), Is.True);
                var sourceModel = compilation.GetSemanticModel(sourceDeclaration.SyntaxTree);
                Assert.That(SymbolEqualityComparer.Default.Equals(
                    sourceModel.GetDeclaredSymbol((MethodDeclarationSyntax)sourceDeclaration), frame.Target), Is.True);
                var sourceGraph = ControlFlowGraph.Create(sourceDeclaration, sourceModel)!;
                Assert.That(sourceGraph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
                Assert.That(frame.OwnsBody(sourceGraph.OriginalOperation), Is.True);
                var prepared = TotalBodyLowering.Lower(compilation, sourceGraph, frame, TestContext.CurrentContext.CancellationToken,
                    resolveIl: provider.Resolve);
                Assert.That(frame.Compilation, Is.SameAs(compilation));
                return prepared.IsExact;
            }
            var validationSession = new TotalSourceCallSession(compilation, PrepareSourceCallee,
                provider.Resolve, TestContext.CurrentContext.CancellationToken);
            Assert.That(TotalIlStack.TryValidate(body, validationSession.Spend, out _), Is.True);
            Assert.That(validationSession.ConstructionLimitExceeded, Is.False);

            using var rootImage = new MemoryStream();
            var rootEmission = compilation.Emit(rootImage);
            Assert.That(rootEmission.Success, Is.True, string.Join("\n", rootEmission.Diagnostics));
            rootImage.Position = 0;
            var rootMethod = runtime.LoadFromStream(rootImage).GetType("Subject")!.GetMethod("Target")!;
            foreach (var input in inputs)
            {
                Assert.That(rootMethod.Invoke(null, [input]), Is.EqualTo(input == null));
            }

            var tree = compilation.SyntaxTrees.Single();
            var model = compilation.GetSemanticModel(tree);
            var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
            var target = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
            var graph = ControlFlowGraph.Create(declaration, model)!;
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, target);
            Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
            Assert.That(context.HasScalarSignature, Is.True);
            var loweringProvider = new CompilerTotalIlBodyProvider(compilation, references);
            var lowered = TotalBodyLowering.Lower(compilation, graph, context, TestContext.CurrentContext.CancellationToken,
                resolveIl: loweringProvider.Resolve);
            Assert.That(lowered.IsExact, Is.True);
            Assert.That(lowered.ConstructionLimitExceeded, Is.False);
            Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
            var entry = context.Parameters.Single().Entry;
            var entryType = factory.GetVariableInfo(entry).Type;
            foreach (var input in inputs)
            {
                var expected = input == null;
                var value = input == null ? factory.CreateNullValue(entryType) : factory.CreateStringValue(input);
                var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                    global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(entry, value));
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ReturnValue!.Boolean, Is.EqualTo(expected));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ApproximationVariables, Is.Empty);
            }
        }
        finally
        {
            runtime.Unload();
        }
    }
}
