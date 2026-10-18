using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class BulkFrontendIlModuleScopeBoundaryTests
{
    private const int ParameterCount = 128;
    private static readonly ImmutableArray<bool> Inputs = [false, true];
    private static readonly ImmutableArray<string> ExpectedCodes = ["Ldarg", "Ret"];
    private static readonly ImmutableArray<bool> ExpectedPreconditions = [true];

    [TestCase(4096)]
    [TestCase(4097)]
    public async Task FixedSavedModuleScopePreservesMetadataEvidenceBoundary(int moduleScopeLength)
    {
        using var directory = new TempDirectory("sharpproof-persisted-scope-");
        var runtime = new AssemblyLoadContext("persisted-scope-" + Guid.NewGuid().ToString("N"),
            isCollectible: true);
        try
        {
            const string assemblySimpleName = "IdentityControl";
            const string typeName = "Library";
            const string methodName = "Target";
            var moduleScopeName = new string('S', moduleScopeLength);
            var assemblyName = new AssemblyName(assemblySimpleName) { Version = new Version(0, 0, 0, 0) };
            var builder = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
            var module = builder.DefineDynamicModule(moduleScopeName);
            var type = module.DefineType(typeName,
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            var parameterTypes = Enumerable.Repeat(typeof(bool), ParameterCount).ToArray();
            parameterTypes[0] = typeof(int);
            var methodBuilder = type.DefineMethod(methodName,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                typeof(bool), parameterTypes);
            var positiveConstructor = typeof(PositiveAttribute).GetConstructor(Type.EmptyTypes)!;
            for (var ordinal = 0; ordinal < ParameterCount; ordinal++)
            {
                var parameter = methodBuilder.DefineParameter(ordinal + 1, ParameterAttributes.None,
                    "value" + ordinal.ToString(CultureInfo.InvariantCulture));
                if (ordinal == 0)
                {
                    parameter.SetCustomAttribute(new CustomAttributeBuilder(positiveConstructor, Array.Empty<object>()));
                }
            }
            var il = methodBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ret);
            type.CreateType();
            using var emitted = new MemoryStream();
            builder.Save(emitted);
            var image = emitted.ToArray();
            var imagePath = Path.Combine(directory.FullName, "implementation.dll");
            await File.WriteAllBytesAsync(imagePath, image, TestContext.CurrentContext.CancellationToken);

            using var runtimeImage = new MemoryStream(image, writable: false);
            var runtimeAssembly = runtime.LoadFromStream(runtimeImage);
            var runtimeMethod = runtimeAssembly.GetType(typeName)!.GetMethod(methodName)!;
            foreach (var input in Inputs)
            {
                var arguments = Enumerable.Repeat<object>(false, ParameterCount).ToArray();
                arguments[0] = 1;
                arguments[1] = input;
                Assert.That(runtimeMethod.Invoke(null, arguments), Is.EqualTo(input),
                    "The saved implementation must load and run before any admission or fallback assertion.");
            }

            var source = "public static class Subject { public static bool Target(bool value) => " +
                typeName + "." + methodName + "(1, value" +
                string.Concat(Enumerable.Repeat(", false", ParameterCount - 2)) + "); }";
            var imageReference = MetadataReference.CreateFromFile(imagePath);
            var compilation = CSharpCompilation.Create("PersistedIdentitySubject_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12),
                    Path.Combine(directory.FullName, "Subject.cs"))],
                TestMetadataReferences.WithSharpProof.Add(imageReference),
                TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
            TestCompilation.AssertNoErrors(compilation);
            using var rootImage = new MemoryStream();
            var rootEmission = compilation.Emit(rootImage);
            Assert.That(rootEmission.Success, Is.True, string.Join("\n", rootEmission.Diagnostics));
            rootImage.Position = 0;
            var rootMethod = runtime.LoadFromStream(rootImage).GetType("Subject")!.GetMethod("Target")!;
            foreach (var input in Inputs)
            {
                Assert.That(rootMethod.Invoke(null, [input]), Is.EqualTo(input));
            }

            using var savedImage = new MemoryStream(image, writable: false);
            using var pe = new PEReader(savedImage);
            var reader = pe.GetMetadataReader();
            var savedScopeName = reader.GetString(reader.GetModuleDefinition().Name);
            Assert.That(savedScopeName.Length, Is.EqualTo(moduleScopeLength));
            Assert.That(savedScopeName, Is.EqualTo(moduleScopeName));
            Assert.That(runtimeAssembly.ManifestModule.ScopeName, Is.EqualTo(savedScopeName));

            var metadataMethod = compilation.GetTypeByMetadataName(typeName)!.GetMembers(methodName)
                .OfType<IMethodSymbol>().Single();
            var metadataAssembly = compilation.GetAssemblyOrModuleSymbol(imageReference);
            Assert.That(SymbolEqualityComparer.Default.Equals(metadataMethod.ContainingAssembly, metadataAssembly), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(metadataMethod.ContainingAssembly, compilation.Assembly), Is.False);
            Assert.That(metadataMethod.DeclaringSyntaxReferences, Is.Empty);
            Assert.That(metadataMethod.MetadataToken, Is.EqualTo(runtimeMethod.MetadataToken));
            Assert.That(metadataMethod.ContainingModule.Name, Is.EqualTo(savedScopeName));
            Assert.That(metadataMethod.Parameters, Has.Length.EqualTo(ParameterCount));
            Assert.That(metadataMethod.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
            Assert.That(metadataMethod.Parameters[0].Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            var positive = compilation.GetTypeByMetadataName(typeof(PositiveAttribute).FullName!)!;
            Assert.That(SymbolEqualityComparer.Default.Equals(
                metadataMethod.Parameters[0].GetAttributes().Single().AttributeClass, positive), Is.True);
            Assert.That(metadataMethod.Parameters.Skip(1).All(parameter =>
                parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.Type.SpecialType == SpecialType.System_Boolean && parameter.GetAttributes().IsEmpty)), Is.True);

            var references = CompilerCompilationCapture.CaptureReferences(compilation.References,
                CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
            var captured = references.SelectMany(reference => reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.Modules)).Single(reference =>
                reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (Path.GetFullPath(reference.Path) == Path.GetFullPath(imagePath)));
            Assert.That(captured.SizeBytes, Is.EqualTo(image.LongLength));
            Assert.That(captured.Sha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(image))));
            Assert.That(captured.Mvid, Is.EqualTo(runtimeAssembly.ManifestModule.ModuleVersionId.ToString("D")));
            Assert.That(captured.Name, Is.EqualTo(metadataMethod.ContainingModule.Name));
            Assert.That(captured.Name, Is.EqualTo(savedScopeName));
            var provider = new CompilerTotalIlBodyProvider(compilation, references);
            var decoded = provider.Resolve(metadataMethod, TestContext.CurrentContext.CancellationToken);
            Assert.That(decoded, Is.Not.Null);
            var decodedBody = decoded!;
            Assert.That(SymbolEqualityComparer.Default.Equals(decodedBody.Method, metadataMethod), Is.True);
            Assert.That(decodedBody.ImageSha256, Is.EqualTo(captured.Sha256));
            Assert.That(decodedBody.ModuleMvid, Is.EqualTo(captured.Mvid));
            Assert.That(decodedBody.Module, Is.EqualTo(captured.Name));
            Assert.That(decodedBody.Instructions.Select(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Code)).ToArray(), Is.EqualTo(ExpectedCodes));
            Assert.That(decodedBody.Instructions[0].Operand, Is.EqualTo(1));
            Assert.That(decodedBody.ParameterAttributes, Has.Length.EqualTo(1));
            Assert.That(decodedBody.ParameterAttributes.Single().Kind, Is.EqualTo("Positive"));
            Assert.That(decodedBody.ParameterAttributes.Single().ParameterOrdinal, Is.Zero);
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
            Assert.That(TotalIlStack.TryValidate(decodedBody, validationSession.Spend, out _), Is.True);
            Assert.That(validationSession.ConstructionLimitExceeded, Is.False);

            var identity = CompilerIdentityBridge.CreateSymbolDisplay(metadataMethod);
            var identityBytes = Encoding.UTF8.GetByteCount(identity);
            Assert.That(identity.Length, Is.EqualTo(2022));
            Assert.That(identityBytes, Is.EqualTo(2022));
            Assert.That(identityBytes, Is.LessThanOrEqualTo(4096));
            TestContext.WriteLine($"moduleScopeLength={moduleScopeLength}; actualScopeLength={savedScopeName.Length}; calleeIdentityBytes={identityBytes}");
            var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
                CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken), references,
                CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken,
                enableMetadataRequires: true);
            if (moduleScopeLength == 4097)
            {
                Assert.That(batch.Callers, Is.Empty);
                Assert.That(batch.Gaps, Has.Length.EqualTo(1));
                Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("UnsupportedBody"));
                return;
            }

            Assert.That(batch.Gaps, Is.Empty);
            Assert.That(batch.Callers, Has.Length.EqualTo(1));
            var body = batch.Callers.Single().Body;
            Assert.That(body.Clauses, Is.Empty);
            Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
            var precondition = body.CallPreconditions.Single();
            Assert.That(precondition.CalleeIdentity, Is.EqualTo(identity));
            Assert.That(precondition.MetadataClause, Is.Not.Null);
            var metadataClause = precondition.MetadataClause!;
            Assert.That(metadataClause.ImageSha256, Is.EqualTo(captured.Sha256));
            Assert.That(metadataClause.ModuleMvid, Is.EqualTo(captured.Mvid));
            Assert.That(metadataClause.ModuleName, Is.EqualTo(savedScopeName));
            var factory = body.Program.Factory;
            var entry = body.Parameters.Single().Entry;
            foreach (var input in Inputs)
            {
                var observations = new List<bool>();
                var markers = body.CallPreconditions.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Instruction)).ToHashSet();
                var options = new IrProgramReplayOptions(static _ => null)
                {
                    AssignmentObserver = (instruction, assigned, _) =>
                    {
                        System.ArgumentNullException.ThrowIfNull(assigned);
                        System.ArgumentNullException.ThrowIfNull(instruction);
                        if (markers.Contains(instruction.Id))
                        { observations.Add(assigned.Boolean); }
                    }
                };
                var replay = new IrProgramInterpreter(factory).Execute(body.Program,
                    global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(entry, factory.CreateBooleanValue(input)), 10000, options);
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ReturnValue!.Boolean, Is.EqualTo(input));
                Assert.That(observations, Is.EqualTo(ExpectedPreconditions));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ApproximationVariables, Is.Empty);
            }
            var artifact = CompilerTotalCallableArtifactCodec.Encode(body)!;
            var detached = JsonSerializer.Deserialize<CompilerTotalCallableArtifact>(JsonSerializer.Serialize(artifact))!;
            var decodedShadow = CompilerDecodedShadowBody.Decode(body.CallableId, detached, TestContext.CurrentContext.CancellationToken, references);
            ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
            var candidate = PassiveCallableArtifactAdapter.EnrollShadow(decodedShadow);
            Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True, reason.ToString());
            using var solver = new PassiveCallableSolver(plan!);
            var checkedCall = await solver.VerifyCallPreconditionAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(checkedCall.Outcome, Is.InstanceOf<ProvenOutcome>(), checkedCall.Reason.ToString());
        }
        finally
        {
            runtime.Unload();
        }
    }
}
