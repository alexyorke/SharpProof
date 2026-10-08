using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class SynchronizedAutoSetterCallerPurityAuditTests
{
    private static readonly int[] s_inputValues = [-7, 0, 42];
    private const string SubjectTemplate = """
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;

        public sealed class Cell
        {
            public int Value
            {
                get;
                [MethodImpl(__METHOD_IMPL_OPTIONS__)]
                set;
            }
        }

        public static class Subject
        {
            [EnforcePure]
            public static int Target(int value)
            {
                new Cell().Value = value;
                return value;
            }
        }
        """ + "\n";

    [TestCase(false)]
    [TestCase(true)]
    public async Task AutoSetterSynchronizationMatchesFreshCallerPurity(bool synchronized)
    {
        var options = synchronized ? "MethodImplOptions.NoInlining | MethodImplOptions.Synchronized"
            : "MethodImplOptions.NoInlining";
        var source = SubjectTemplate.Replace("__METHOD_IMPL_OPTIONS__", options, StringComparison.Ordinal);
        var sourceHash = Hash(Encoding.UTF8.GetBytes(source));
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var caseName = synchronized ? "synchronized" : "no-inlining-control";
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "synchronized-auto-setter-caller-purity-audit",
            caseName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject-source.cs"), source);
        var compilation = TestCompilation.Create("SynchronizedAutoSetterCallerPurity_" + caseName, ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        await Save(evidenceDirectory, "emission.json", new
        {
            SourceSha256 = sourceHash,
            Optimization = compilation.Options.OptimizationLevel.ToString(),
            emission.Success,
            Diagnostics = emission.Diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray()
        });
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "emitted-subject.dll"), imageBytes);
        image.Position = 0;
        var loadContext = new AssemblyLoadContext("SynchronizedAutoSetterCallerPurity_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        MethodImplAttributes setterFlags;
        try
        {
            var assembly = loadContext.LoadFromStream(image);
            var subject = assembly.GetType("Subject")!;
            var cell = assembly.GetType("Cell")!;
            var target = subject.GetMethod("Target", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
            var setter = cell.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.SetMethod!;
            var field = cell.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Single();
            var constructor = cell.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single();
            var targetIl = target.GetMethodBody()!.GetILAsByteArray()!;
            var setterIl = setter.GetMethodBody()!.GetILAsByteArray()!;
            var constructorIl = constructor.GetMethodBody()!.GetILAsByteArray()!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(targetIl, Has.Length.EqualTo(13));
                Assert.That(targetIl[0], Is.EqualTo(0x73));
                Assert.That(targetIl[5], Is.EqualTo(0x02));
                Assert.That(targetIl[6], Is.EqualTo(0x28));
                Assert.That(targetIl[11], Is.EqualTo(0x02));
                Assert.That(targetIl[12], Is.EqualTo(0x2a));
                Assert.That(setterIl, Has.Length.EqualTo(8));
                Assert.That(setterIl[0], Is.EqualTo(0x02));
                Assert.That(setterIl[1], Is.EqualTo(0x03));
                Assert.That(setterIl[2], Is.EqualTo(0x7d));
                Assert.That(setterIl[7], Is.EqualTo(0x2a));
                Assert.That(constructorIl, Has.Length.EqualTo(7));
                Assert.That(constructorIl[0], Is.EqualTo(0x02));
                Assert.That(constructorIl[1], Is.EqualTo(0x28));
                Assert.That(constructorIl[6], Is.EqualTo(0x2a));
            }
            var constructorToken = BitConverter.ToInt32(targetIl, 1);
            var setterToken = BitConverter.ToInt32(targetIl, 7);
            var fieldToken = BitConverter.ToInt32(setterIl, 3);
            var baseConstructorToken = BitConverter.ToInt32(constructorIl, 2);
            var calledConstructor = (ConstructorInfo)target.Module.ResolveMethod(constructorToken)!;
            var calledSetter = (MethodInfo)target.Module.ResolveMethod(setterToken)!;
            var storedField = setter.Module.ResolveField(fieldToken)!;
            var baseConstructor = (ConstructorInfo)constructor.Module.ResolveMethod(baseConstructorToken)!;
            setterFlags = setter.GetMethodImplementationFlags();
            var execute = target.CreateDelegate<Func<int, int>>();
            var returnValues = s_inputValues.Select(input => execute(input)).ToArray();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(calledConstructor.DeclaringType, Is.EqualTo(cell));
                Assert.That(calledConstructor.MetadataToken, Is.EqualTo(constructor.MetadataToken));
                Assert.That(calledSetter.DeclaringType, Is.EqualTo(cell));
                Assert.That(calledSetter.MetadataToken, Is.EqualTo(setter.MetadataToken));
                Assert.That(calledSetter.Module.ModuleVersionId, Is.EqualTo(setter.Module.ModuleVersionId));
                Assert.That(calledSetter.Name, Is.EqualTo("set_Value"));
                Assert.That(storedField.MetadataToken, Is.EqualTo(field.MetadataToken));
                Assert.That(storedField.DeclaringType, Is.EqualTo(cell));
                Assert.That(baseConstructor.DeclaringType, Is.EqualTo(typeof(object)));
                Assert.That(constructor.GetParameters(), Is.Empty);
                Assert.That(constructor.GetMethodImplementationFlags(), Is.EqualTo((MethodImplAttributes)0));
                Assert.That(target.GetParameters(), Has.Length.EqualTo(1));
                Assert.That(target.GetParameters().Single().ParameterType, Is.EqualTo(typeof(int)));
                Assert.That(target.ReturnType, Is.EqualTo(typeof(int)));
                Assert.That(target.GetMethodImplementationFlags(), Is.EqualTo((MethodImplAttributes)0));
                Assert.That(setter.GetParameters(), Has.Length.EqualTo(1));
                Assert.That(setter.GetParameters().Single().ParameterType, Is.EqualTo(typeof(int)));
                Assert.That(setter.ReturnType, Is.EqualTo(typeof(void)));
                Assert.That(setter.IsStatic, Is.False);
                Assert.That(setterFlags, Is.EqualTo((MethodImplAttributes)(synchronized ? 40 : 8)));
                Assert.That(field.Name, Is.EqualTo("<Value>k__BackingField"));
                Assert.That(field.IsInitOnly, Is.False);
                Assert.That(field.IsStatic, Is.False);
                Assert.That(field.FieldType, Is.EqualTo(typeof(int)));
                Assert.That(cell.TypeInitializer, Is.Null);
                Assert.That(returnValues, Is.EqualTo(s_inputValues));
                Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
            }
            await Save(evidenceDirectory, "setter-call-binding.json", new
            {
                SourceSha256 = sourceHash,
                EmittedPeSha256 = Hash(imageBytes),
                TargetMetadataToken = target.MetadataToken,
                TargetIlHex = Convert.ToHexStringLower(targetIl),
                ConstructorMetadataToken = constructor.MetadataToken,
                ConstructorCallToken = constructorToken,
                ConstructorIlHex = Convert.ToHexStringLower(constructorIl),
                BaseConstructorCallToken = baseConstructorToken,
                BaseConstructorDeclaringType = baseConstructor.DeclaringType!.FullName,
                SetterMetadataToken = setter.MetadataToken,
                SetterCallToken = setterToken,
                SetterIlHex = Convert.ToHexStringLower(setterIl),
                BackingFieldMetadataToken = field.MetadataToken,
                SetterStoreFieldToken = fieldToken,
                SetterImplementationFlags = (int)setterFlags,
                ExactConstructorCall = calledConstructor.MetadataToken == constructor.MetadataToken && calledConstructor.DeclaringType == cell,
                ExactSetterCall = calledSetter.MetadataToken == setter.MetadataToken && calledSetter.DeclaringType == cell,
                ExactSetterStore = storedField.MetadataToken == field.MetadataToken && storedField.DeclaringType == cell,
                ModuleVersionId = cell.Module.ModuleVersionId.ToString("D")
            });
            await Save(evidenceDirectory, "runtime-observations.json", new
            {
                SourceSha256 = sourceHash,
                EmittedPeSha256 = Hash(imageBytes),
                Case = caseName,
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                TypedInvocation = "Func<int, int> bound directly to emitted static Target; Target creates its own Cell receiver",
                Inputs = s_inputValues,
                ReturnValues = returnValues,
                RawSetterImplementationFlags = (int)setterFlags,
                SetterImplementationFlags = setterFlags.ToString(),
                ExpectedSetterSynchronized = synchronized,
                ReceiverFreshInsideTarget = true,
                CellFieldCount = cell.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Length,
                BackingFieldName = field.Name,
                BackingFieldReadonly = field.IsInitOnly,
                BackingFieldStatic = field.IsStatic,
                HasTypeInitializer = cell.TypeInitializer != null,
                SynchronizationBasis = "Reflected instance setter Synchronized flag and CLR runtime source semantics; no contention or timing observation.",
                ClrSource = "https://github.com/dotnet/runtime/blob/v9.0.0/src/coreclr/jit/flowgraph.cpp#L1572-L1588",
                RuntimeLimit = "Runtime identity is recorded. Reference CLR source is v9.0.0; no JIT machine-code disassembly is performed."
            });
        }
        finally
        {
            loadContext.Unload();
        }

        var tree = compilation.SyntaxTrees.Single();
        var syntaxRoot = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var targetSyntax = syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var targetSymbol = (IMethodSymbol)model.GetDeclaredSymbol(targetSyntax)!;
        var propertySyntax = syntaxRoot.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var propertySymbol = (IPropertySymbol)model.GetDeclaredSymbol(propertySyntax)!;
        var setterSymbol = propertySymbol.SetMethod!;
        var assignment = (ISimpleAssignmentOperation)((IExpressionStatementOperation)model.GetOperation(targetSyntax.Body!.Statements[0])!).Operation;
        var propertyReference = (IPropertyReferenceOperation)assignment.Target;
        var creation = (IObjectCreationOperation)propertyReference.Instance!;
        var value = (IParameterReferenceOperation)assignment.Value;
        var cellSymbol = propertySymbol.ContainingType;
        var discovery = new ClaimManifestBuilder(compilation).Build();
        Assert.That(discovery.Targets, Has.Count.EqualTo(1));
        var selectedTarget = discovery.Targets.Values.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(targetSymbol.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(targetSymbol.Name, Is.EqualTo("Target"));
            Assert.That(targetSymbol.IsStatic, Is.True);
            Assert.That(setterSymbol.MethodKind, Is.EqualTo(MethodKind.PropertySet));
            Assert.That((int)setterSymbol.MethodImplementationFlags, Is.EqualTo((int)setterFlags));
            Assert.That(SymbolEqualityComparer.Default.Equals(selectedTarget.Method, targetSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(selectedTarget.Method, setterSymbol), Is.False);
            Assert.That(SymbolEqualityComparer.Default.Equals(propertyReference.Property.SetMethod, setterSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(creation.Type, cellSymbol), Is.True);
            Assert.That(creation.Arguments, Is.Empty);
            Assert.That(creation.Initializer, Is.Null);
            Assert.That(creation.Constructor!.IsImplicitlyDeclared, Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(value.Parameter, targetSymbol.Parameters.Single()), Is.True);
            Assert.That(cellSymbol.IsSealed, Is.True);
            Assert.That(cellSymbol.StaticConstructors, Is.Empty);
            Assert.That(syntaxRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>(), Is.Empty);
            Assert.That(syntaxRoot.DescendantNodes().OfType<FieldDeclarationSyntax>(), Is.Empty);
            Assert.That(propertySyntax.Initializer, Is.Null);
            Assert.That(propertySyntax.AccessorList!.Accessors.All(accessor => accessor.Body == null && accessor.ExpressionBody == null), Is.True);
            Assert.That(setterSymbol.GetAttributes(), Has.Length.EqualTo(1));
            Assert.That(setterSymbol.GetAttributes().Single().AttributeClass!.ToDisplayString(), Is.EqualTo("System.Runtime.CompilerServices.MethodImplAttribute"));
            Assert.That(selectedTarget.Declaration!.Span, Is.EqualTo(targetSyntax.Span));
            Assert.That(selectedTarget.EffectClaims, Has.Length.EqualTo(1));
            Assert.That(selectedTarget.EffectClaims.Single().HasValidConstraint, Is.True);
        }
        await Save(evidenceDirectory, "source-call-binding.json", new
        {
            SourceSha256 = sourceHash,
            SelectedCallableId = selectedTarget.Entry.CallableId,
            TargetSourceMethod = targetSymbol.ToDisplayString(),
            SetterSourceMethod = setterSymbol.ToDisplayString(),
            SelectedSourceIsTarget = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, targetSymbol),
            SelectedSourceIsSetter = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, setterSymbol),
            SetterMethodKind = setterSymbol.MethodKind.ToString(),
            SetterSourceImplementationFlags = (int)setterSymbol.MethodImplementationFlags,
            TargetSpanStart = targetSyntax.SpanStart,
            TargetSpanLength = targetSyntax.Span.Length,
            AssignmentKind = assignment.Kind.ToString(),
            ReceiverOperationKind = creation.Kind.ToString(),
            ReceiverConstructorImplicit = creation.Constructor!.IsImplicitlyDeclared,
            SetterMatchesAssignment = SymbolEqualityComparer.Default.Equals(propertyReference.Property.SetMethod, setterSymbol),
            SourceInitializersAbsent = creation.Initializer == null && propertySyntax.Initializer == null,
            StaticConstructorsAbsent = cellSymbol.StaticConstructors.Length == 0
        });
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var manifestBytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "compiler-manifest.json"), manifestBytes);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-request.json"), WorkerProtocolJson.SerializeRequest(project.Request) + "\n");
        Assert.That(Hash(manifestBytes), Is.EqualTo(project.Request.CompilerManifest.Sha256));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-response.json"), WorkerProtocolJson.SerializeResponse(response) + "\n");
        var validation = WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(project.Request),
            Program.ComputeExpectedInputHash(project.Request, manifestBytes), artifact.Manifest, project.Request, Program.ExpectedVersions());
        await Save(evidenceDirectory, "native-observations.json", new
        {
            SourceSha256 = sourceHash,
            EmittedPeSha256 = Hash(imageBytes),
            Case = caseName,
            SameCompilationForClrAndNative = true,
            CapturedTotal = artifact.Callables.Single().Total != null,
            CapturedBodyAbstraction = artifact.Callables.Single().Total?.IsBodyAbstraction,
            SelectedCallableId = selectedTarget.Entry.CallableId,
            SelectedSourceMethod = targetSymbol.ToDisplayString(),
            CalledSetterSourceMethod = setterSymbol.ToDisplayString(),
            SelectedSourceIsTarget = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, targetSymbol),
            SelectedSourceIsSetter = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, setterSymbol),
            ReflectedSetterImplementationFlags = setterFlags.ToString(),
            ReflectedSetterSynchronized = (setterFlags & MethodImplAttributes.Synchronized) != 0,
            CacheEnabled = project.Request.Cache.Enabled,
            Budgets = project.Request.Budgets,
            RunStatus = response.RunStatus.ToString(),
            FailureReason = response.FailureReason.ToString(),
            ProtocolValid = validation.IsValid,
            ProtocolErrors = validation.Errors,
            response.Errors,
            ManifestClaims = artifact.Manifest.Claims.Select(claim => new { claim.ClaimId, Kind = claim.Kind.ToString(), ContractKind = claim.EffectContractKind.ToString() }).ToArray(),
            Claims = response.ClaimResults.Select(claim => new
            {
                claim.ClaimId,
                Outcome = claim.Outcome.ToString(),
                Reason = claim.Reason.ToString(),
                EffectCertainty = claim.EffectCertainty.ToString(),
                Vacuity = claim.Vacuity.ToString()
            }).ToArray(),
            FrozenExpectation = synchronized ? "The fresh-receiver caller of the synchronized auto setter cannot be Proven EnforcePure."
                : "The fresh-receiver caller of the NoInlining-only auto setter remains complete Proven EnforcePure."
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validation.IsValid, Is.True, JsonSerializer.Serialize(validation.Errors));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(project.Request.Cache.Enabled, Is.False);
            Assert.That(JsonSerializer.Serialize(project.Request.Budgets), Is.EqualTo(JsonSerializer.Serialize(new WorkerBudgets())));
            Assert.That(artifact.Callables, Has.Length.EqualTo(1));
            Assert.That(artifact.Callables.Single().CallableId, Is.EqualTo(selectedTarget.Entry.CallableId));
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Claims.Single().CallableId, Is.EqualTo(selectedTarget.Entry.CallableId));
            Assert.That(artifact.Manifest.Claims.Single().Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(artifact.Manifest.Claims.Single().EffectContractKind, Is.EqualTo(WorkerEffectContractKind.EnforcePure));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            Assert.That(response.CallableResults, Has.Length.EqualTo(1));
            var claim = response.ClaimResults.Single();
            Assert.That(claim.ClaimId, Is.EqualTo(artifact.Manifest.Claims.Single().ClaimId));
            Assert.That(response.CallableResults.Single().CallableId, Is.EqualTo(selectedTarget.Entry.CallableId));
            Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            if (synchronized)
            {
                Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                    "CLR synchronization in the setter remains excluded by EnforcePure even when the receiver and its field write are fresh.");
            }
            else
            {
                Assert.That(artifact.Callables.Single().Total, Is.Not.Null);
                Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
                Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
                Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.None));
                Assert.That(claim.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            }
        }
    }

    private static string Hash(byte[] value)
    {
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value) + "\n");
    }
}
