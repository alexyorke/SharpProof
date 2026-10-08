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
public sealed class SynchronizedBodiedGetterCallerPurityAuditTests
{
    private static readonly int[] s_inputValues = [-7, 0, 42];
    private const string SubjectTemplate = """
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;

        public sealed class Subject
        {
            private readonly int _value;

            public Subject(int value) { _value = value; }

            public int Value
            {
                [MethodImpl(__METHOD_IMPL_OPTIONS__)]
                get { return _value; }
            }

            [EnforcePure]
            public static int Target(Subject receiver) => receiver.Value;
        }
        """ + "\n";

    [TestCase(false)]
    [TestCase(true)]
    public async Task BodiedGetterSynchronizationMatchesCallerPurity(bool synchronized)
    {
        var options = synchronized ? "MethodImplOptions.NoInlining | MethodImplOptions.Synchronized"
            : "MethodImplOptions.NoInlining";
        var source = SubjectTemplate.Replace("__METHOD_IMPL_OPTIONS__", options, StringComparison.Ordinal);
        var sourceHash = Hash(Encoding.UTF8.GetBytes(source));
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var caseName = synchronized ? "synchronized" : "no-inlining-control";
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "synchronized-bodied-getter-caller-purity-audit",
            caseName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject-source.cs"), source);

        var compilation = TestCompilation.Create("SynchronizedBodiedGetterCallerPurity_" + caseName, ("Subject.cs", source));
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
        var loadContext = new AssemblyLoadContext("SynchronizedBodiedGetterCallerPurity_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        RuntimeObservation runtimeObservation;
        try
        {
            var subject = loadContext.LoadFromStream(image).GetType("Subject")!;
            var target = subject.GetMethod("Target", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
            var getter = subject.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!.GetMethod!;
            var field = subject.GetField("_value", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            var constructor = subject.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single();
            var targetIl = target.GetMethodBody()!.GetILAsByteArray()!;
            var getterIl = getter.GetMethodBody()!.GetILAsByteArray()!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(targetIl, Has.Length.EqualTo(7));
                Assert.That(targetIl[0], Is.EqualTo(0x02));
                Assert.That(targetIl[1], Is.EqualTo(0x6f));
                Assert.That(targetIl[6], Is.EqualTo(0x2a));
                Assert.That(getterIl, Has.Length.EqualTo(7));
                Assert.That(getterIl[0], Is.EqualTo(0x02));
                Assert.That(getterIl[1], Is.EqualTo(0x7b));
                Assert.That(getterIl[6], Is.EqualTo(0x2a));
                Assert.That(constructor.GetParameters(), Has.Length.EqualTo(1));
                Assert.That(constructor.GetParameters().Single().ParameterType, Is.EqualTo(typeof(int)));
            }
            var callToken = BitConverter.ToInt32(targetIl, 2);
            var fieldToken = BitConverter.ToInt32(getterIl, 2);
            var calledGetter = (MethodInfo)target.Module.ResolveMethod(callToken)!;
            var returnedField = getter.Module.ResolveField(fieldToken)!;
            var flags = getter.GetMethodImplementationFlags();
            var receivers = s_inputValues.Select(input => constructor.Invoke([input])).ToArray();
            var executions = receivers.Select(receiver => target.CreateDelegate<Func<int>>(receiver)).ToArray();
            var closedReceiversMatch = executions.Select((execute, index) => ReferenceEquals(execute.Target, receivers[index])).ToArray();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(calledGetter.DeclaringType, Is.EqualTo(subject));
                Assert.That(calledGetter.MetadataToken, Is.EqualTo(getter.MetadataToken));
                Assert.That(calledGetter.Module.ModuleVersionId, Is.EqualTo(getter.Module.ModuleVersionId));
                Assert.That(calledGetter.Name, Is.EqualTo("get_Value"));
                Assert.That(returnedField.MetadataToken, Is.EqualTo(field.MetadataToken));
                Assert.That(returnedField.DeclaringType, Is.EqualTo(subject));
                Assert.That(target.GetParameters(), Has.Length.EqualTo(1));
                Assert.That(target.GetParameters().Single().ParameterType, Is.EqualTo(subject));
                Assert.That(target.ReturnType, Is.EqualTo(typeof(int)));
                Assert.That(target.GetMethodImplementationFlags(), Is.EqualTo((MethodImplAttributes)0));
                Assert.That(closedReceiversMatch.All(static same => same), Is.True);
            }
            await Save(evidenceDirectory, "getter-call-binding.json", new
            {
                SourceSha256 = sourceHash,
                EmittedPeSha256 = Hash(imageBytes),
                TargetName = target.Name,
                TargetMetadataToken = target.MetadataToken,
                TargetIlHex = Convert.ToHexStringLower(targetIl),
                TargetImplementationFlags = (int)target.GetMethodImplementationFlags(),
                CallOpcode = "callvirt",
                CallMetadataToken = callToken,
                GetterName = getter.Name,
                GetterMetadataToken = getter.MetadataToken,
                GetterIlHex = Convert.ToHexStringLower(getterIl),
                FieldMetadataToken = field.MetadataToken,
                GetterFieldMetadataToken = fieldToken,
                FieldName = field.Name,
                FieldReadonly = field.IsInitOnly,
                ModuleVersionId = subject.Module.ModuleVersionId.ToString("D"),
                ExactGetterCallTokenResolution = calledGetter.MetadataToken == getter.MetadataToken && calledGetter.DeclaringType == subject,
                ExactGetterFieldTokenResolution = returnedField.MetadataToken == field.MetadataToken && returnedField.DeclaringType == subject,
                RawGetterImplementationFlags = (int)flags,
                ClosedDelegateReceiverIdentity = closedReceiversMatch,
                ReceiverType = subject.FullName
            });
            runtimeObservation = new(
                (int)flags,
                flags.ToString(),
                (flags & MethodImplAttributes.NoInlining) != 0,
                (flags & MethodImplAttributes.Synchronized) != 0,
                getter.IsStatic,
                getter.ReturnType == typeof(int),
                getter.GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray(),
                subject.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Length,
                field.IsInitOnly,
                field.IsStatic,
                field.FieldType == typeof(int),
                subject.TypeInitializer != null,
                executions.Select(static execute => execute()).ToArray(),
                Convert.ToHexStringLower(getterIl),
                subject.Module.ModuleVersionId.ToString("D"));
            await Save(evidenceDirectory, "runtime-observations.json", new
            {
                SourceSha256 = sourceHash,
                EmittedPeSha256 = Hash(imageBytes),
                Case = caseName,
                ExpectedGetterSynchronized = synchronized,
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                TypedInvocation = "Closed Func<int> bound to each emitted Subject receiver as static Target first argument",
                Inputs = s_inputValues,
                Observation = runtimeObservation,
                ClosedDelegateReceiverIdentity = closedReceiversMatch,
                SynchronizationBasis = "Reflected instance getter Synchronized flag and CLR runtime source semantics; no contention or timing observation.",
                ClrSource = "https://github.com/dotnet/runtime/blob/v9.0.0/src/coreclr/jit/flowgraph.cpp#L1572-L1588",
                RuntimeLimit = "Runtime identity is recorded. The reference CLR source is v9.0.0; no JIT machine-code disassembly is performed."
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
                Assert.That(runtimeObservation.RawImplementationFlags, Is.EqualTo(synchronized ? 40 : 8));
                Assert.That(runtimeObservation.NoInlining, Is.True);
                Assert.That(runtimeObservation.Synchronized, Is.EqualTo(synchronized));
                Assert.That(runtimeObservation.IsStatic, Is.False);
                Assert.That(runtimeObservation.ReturnsInt32, Is.True);
                Assert.That(runtimeObservation.ParameterTypes, Is.Empty);
                Assert.That(runtimeObservation.FieldCount, Is.EqualTo(1));
                Assert.That(runtimeObservation.FieldReadonly, Is.True);
                Assert.That(runtimeObservation.FieldStatic, Is.False);
                Assert.That(runtimeObservation.FieldInt32, Is.True);
                Assert.That(runtimeObservation.HasTypeInitializer, Is.False);
                Assert.That(runtimeObservation.ReturnValues, Is.EqualTo(s_inputValues));
            }
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
        var getterSymbol = propertySymbol.GetMethod!;
        var getterSyntax = propertySyntax.AccessorList!.Accessors.Single();
        var propertyAccess = (IPropertyReferenceOperation)model.GetOperation(targetSyntax.ExpressionBody!.Expression)!;
        var receiverParameter = (propertyAccess.Instance as IParameterReferenceOperation)?.Parameter;
        var getterFieldRead = (IFieldReferenceOperation)((IReturnOperation)model.GetOperation(getterSyntax.Body!.Statements.Single())!).ReturnedValue!;
        var discovery = new ClaimManifestBuilder(compilation).Build();
        Assert.That(discovery.Targets, Has.Count.EqualTo(1));
        var selectedTarget = discovery.Targets.Values.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(targetSymbol.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(targetSymbol.Name, Is.EqualTo("Target"));
            Assert.That(targetSymbol.IsStatic, Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(selectedTarget.Method, targetSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(selectedTarget.Method, getterSymbol), Is.False);
            Assert.That(SymbolEqualityComparer.Default.Equals(propertyAccess.Property.GetMethod, getterSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(receiverParameter, targetSymbol.Parameters.Single()), Is.True);
            Assert.That(getterFieldRead.Field.Name, Is.EqualTo("_value"));
            Assert.That(getterFieldRead.Field.IsReadOnly, Is.True);
            Assert.That(getterSymbol.GetAttributes(), Has.Length.EqualTo(1));
            Assert.That(getterSymbol.GetAttributes().Single().AttributeClass!.ToDisplayString(),
                Is.EqualTo("System.Runtime.CompilerServices.MethodImplAttribute"));
            Assert.That(selectedTarget.Declaration!.Span, Is.EqualTo(targetSyntax.Span));
            Assert.That(selectedTarget.EffectClaims, Has.Length.EqualTo(1));
            Assert.That(selectedTarget.EffectClaims.Single().HasValidConstraint, Is.True);
        }
        await Save(evidenceDirectory, "source-call-binding.json", new
        {
            SourceSha256 = sourceHash,
            SelectedCallableId = selectedTarget.Entry.CallableId,
            TargetSourceMethod = targetSymbol.ToDisplayString(),
            GetterSourceMethod = getterSymbol.ToDisplayString(),
            ReceiverParameter = receiverParameter!.ToDisplayString(),
            TargetSpanStart = targetSyntax.SpanStart,
            TargetSpanLength = targetSyntax.Span.Length,
            GetterSpanStart = getterSyntax.SpanStart,
            GetterSpanLength = getterSyntax.Span.Length,
            PropertyAccessKind = propertyAccess.Kind.ToString(),
            BoundGetterMatchesProperty = SymbolEqualityComparer.Default.Equals(propertyAccess.Property.GetMethod, getterSymbol),
            BoundReceiverMatchesTargetParameter = SymbolEqualityComparer.Default.Equals(receiverParameter, targetSymbol.Parameters.Single()),
            GetterReturnedField = getterFieldRead.Field.ToDisplayString(),
            GetterFieldReadonly = getterFieldRead.Field.IsReadOnly,
            GetterSourceAttributes = getterSymbol.GetAttributes().Select(attribute => attribute.AttributeClass!.ToDisplayString()).ToArray(),
            SelectedSourceIsTarget = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, targetSymbol),
            SelectedSourceIsGetter = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, getterSymbol)
        });
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var manifestBytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        await File.WriteAllBytesAsync(Path.Combine(evidenceDirectory, "compiler-manifest.json"), manifestBytes);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-request.json"),
            WorkerProtocolJson.SerializeRequest(project.Request) + "\n");
        Assert.That(Hash(manifestBytes), Is.EqualTo(project.Request.CompilerManifest.Sha256));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "worker-response.json"),
            WorkerProtocolJson.SerializeResponse(response) + "\n");
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
            CalledGetterSourceMethod = getterSymbol.ToDisplayString(),
            SelectedSourceIsTarget = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, targetSymbol),
            SelectedSourceIsGetter = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, getterSymbol),
            LegacyCallableFailureReason = artifact.Callables.Single().FailureReason.ToString(),
            ReflectedGetterImplementationFlags = runtimeObservation.ImplementationFlags,
            ReflectedGetterSynchronized = runtimeObservation.Synchronized,
            CacheEnabled = project.Request.Cache.Enabled,
            Budgets = project.Request.Budgets,
            RunStatus = response.RunStatus.ToString(),
            FailureReason = response.FailureReason.ToString(),
            ProtocolValid = validation.IsValid,
            ProtocolErrors = validation.Errors,
            response.Errors,
            ManifestClaims = artifact.Manifest.Claims.Select(claim => new
            {
                claim.ClaimId,
                Kind = claim.Kind.ToString(),
                ContractKind = claim.EffectContractKind.ToString()
            }).ToArray(),
            Claims = response.ClaimResults.Select(claim => new
            {
                claim.ClaimId,
                Outcome = claim.Outcome.ToString(),
                Reason = claim.Reason.ToString(),
                EffectCertainty = claim.EffectCertainty.ToString(),
                Vacuity = claim.Vacuity.ToString()
            }).ToArray(),
            FrozenExpectation = synchronized ? "The caller of the synchronized bodied getter cannot be Proven EnforcePure."
                : "The caller of the NoInlining-only bodied getter remains Proven EnforcePure with complete native may-effect evidence."
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
            var claim = response.ClaimResults.Single();
            Assert.That(claim.ClaimId, Is.EqualTo(artifact.Manifest.Claims.Single().ClaimId));
            Assert.That(response.CallableResults.Single().CallableId, Is.EqualTo(selectedTarget.Entry.CallableId));
            Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            if (synchronized)
            {
                Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                    "CLR synchronization in the bodied getter is excluded by the Target caller EnforcePure claim, even when the getter only returns a readonly field.");
            }
            else
            {
                Assert.That(artifact.Callables.Single().Total, Is.Not.Null);
                Assert.That(response.CallableResults, Has.Length.EqualTo(1));
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

    private sealed record RuntimeObservation(int RawImplementationFlags, string ImplementationFlags, bool NoInlining,
        bool Synchronized, bool IsStatic, bool ReturnsInt32, string?[] ParameterTypes, int FieldCount, bool FieldReadonly, bool FieldStatic, bool FieldInt32,
        bool HasTypeInitializer, int[] ReturnValues, string IlHex, string ModuleVersionId);
}
