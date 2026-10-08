using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class SynchronizedInstanceMethodPurityAuditTests
{
    private static readonly int[] s_inputValues = [-7, 0, 42];
    private const string SubjectTemplate = """
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;

        public sealed class Subject
        {
            [EnforcePure]
            [MethodImpl(__METHOD_IMPL_OPTIONS__)]
            public int Target(int value) => value;
        }
        """ + "\n";

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClosedInstanceSynchronizationMatchesNativePurity(bool synchronized)
    {
        var options = synchronized ? "MethodImplOptions.NoInlining | MethodImplOptions.Synchronized"
            : "MethodImplOptions.NoInlining";
        var source = SubjectTemplate.Replace("__METHOD_IMPL_OPTIONS__", options, StringComparison.Ordinal);
        var sourceHash = Hash(Encoding.UTF8.GetBytes(source));
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var caseName = synchronized ? "synchronized" : "no-inlining-control";
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "synchronized-instance-method-own-purity-audit",
            caseName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject-source.cs"), source);

        var compilation = TestCompilation.Create("SynchronizedInstanceMethodPurity_" + caseName, ("Subject.cs", source));
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
        var loadContext = new AssemblyLoadContext("SynchronizedInstanceMethodPurity_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        RuntimeObservation runtimeObservation;
        ClosedDelegateObservation closedBinding;
        try
        {
            var subject = loadContext.LoadFromStream(image).GetType("Subject")!;
            var instance = Activator.CreateInstance(subject)!;
            var method = subject.GetMethod("Target", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            var flags = method.GetMethodImplementationFlags();
            var execute = method.CreateDelegate<Func<int, int>>(instance);
            closedBinding = new(method.MetadataToken, execute.Method.MetadataToken, method.Module.ModuleVersionId,
                execute.Method.Module.ModuleVersionId, ReferenceEquals(execute.Target, instance), execute.Target?.GetType() == subject,
                execute.Method.DeclaringType == subject, (int)execute.Method.GetMethodImplementationFlags(), method.Name, execute.Method.Name);
            runtimeObservation = new(
                (int)flags,
                flags.ToString(),
                (flags & MethodImplAttributes.NoInlining) != 0,
                (flags & MethodImplAttributes.Synchronized) != 0,
                method.IsStatic,
                method.ReturnType == typeof(int),
                method.GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray(),
                subject.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Length,
                subject.TypeInitializer != null,
                s_inputValues.Select(execute).ToArray(),
                Convert.ToHexStringLower(method.GetMethodBody()!.GetILAsByteArray()!),
                subject.Module.ModuleVersionId.ToString("D"));
            await Save(evidenceDirectory, "runtime-observations.json", new
            {
                SourceSha256 = sourceHash,
                EmittedPeSha256 = Hash(imageBytes),
                Case = caseName,
                ExpectedSynchronized = synchronized,
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                TypedInvocation = "Closed Func<int,int> bound to the same emitted Subject instance",
                Inputs = s_inputValues,
                Observation = runtimeObservation,
                ClosedDelegateBinding = closedBinding,
                SynchronizationBasis = "Reflected Synchronized flag and CLR runtime source semantics; no contention or timing observation.",
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
                Assert.That(runtimeObservation.ParameterTypes, Has.Length.EqualTo(1));
                Assert.That(runtimeObservation.ParameterTypes.Single(), Is.EqualTo(typeof(int).FullName));
                Assert.That(runtimeObservation.FieldCount, Is.Zero);
                Assert.That(runtimeObservation.HasTypeInitializer, Is.False);
                Assert.That(runtimeObservation.ReturnValues, Is.EqualTo(s_inputValues));
                Assert.That(runtimeObservation.IlHex, Is.EqualTo("032a"), "Instance int identity loads arg1; arg0 is this.");
                Assert.That(closedBinding.ReflectedMethodToken, Is.EqualTo(closedBinding.DelegateMethodToken));
                Assert.That(closedBinding.ReflectedModuleVersionId, Is.EqualTo(closedBinding.DelegateModuleVersionId));
                Assert.That(closedBinding.DelegateTargetIsEmittedInstance, Is.True);
                Assert.That(closedBinding.DelegateTargetTypeIsSubject, Is.True);
                Assert.That(closedBinding.DelegateDeclaringTypeIsSubject, Is.True);
                Assert.That(closedBinding.DelegateImplementationFlags, Is.EqualTo(runtimeObservation.RawImplementationFlags));
                Assert.That(closedBinding.ReflectedMethodName, Is.EqualTo("Target"));
                Assert.That(closedBinding.DelegateMethodName, Is.EqualTo("Target"));
            }
        }
        finally
        {
            loadContext.Unload();
        }

        var discovery = new ClaimManifestBuilder(compilation).Build();
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
            CapturedReceiver = artifact.Callables.Single().Total?.HasReceiver,
            ClosedDelegateBinding = closedBinding,
            ReflectedImplementationFlags = runtimeObservation.ImplementationFlags,
            ReflectedSynchronized = runtimeObservation.Synchronized,
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
            FrozenExpectation = synchronized ? "The synchronized instance source method cannot be Proven EnforcePure."
                : "The NoInlining-only control remains Proven EnforcePure with complete native may-effect evidence."
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
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Claims.Single().CallableId, Is.EqualTo("M:Subject.Target(System.Int32)~System.Int32"));
            Assert.That(artifact.Manifest.Callables.Single().CallableId, Is.EqualTo("M:Subject.Target(System.Int32)~System.Int32"));
            Assert.That(artifact.Manifest.Claims.Single().Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(artifact.Manifest.Claims.Single().EffectContractKind, Is.EqualTo(WorkerEffectContractKind.EnforcePure));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            var claim = response.ClaimResults.Single();
            Assert.That(claim.ClaimId, Is.EqualTo(artifact.Manifest.Claims.Single().ClaimId));
            Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            if (synchronized)
            {
                Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                    "CLR synchronization is excluded by EnforcePure, even when the written body only returns its input.");
            }
            else
            {
                Assert.That(artifact.Callables.Single().Total, Is.Not.Null);
                Assert.That(artifact.Callables.Single().Total!.HasReceiver, Is.True);
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

    private sealed record ClosedDelegateObservation(int ReflectedMethodToken, int DelegateMethodToken,
        Guid ReflectedModuleVersionId, Guid DelegateModuleVersionId, bool DelegateTargetIsEmittedInstance,
        bool DelegateTargetTypeIsSubject, bool DelegateDeclaringTypeIsSubject, int DelegateImplementationFlags,
        string ReflectedMethodName, string DelegateMethodName);
    private sealed record RuntimeObservation(int RawImplementationFlags, string ImplementationFlags, bool NoInlining,
        bool Synchronized, bool IsStatic, bool ReturnsInt32, string?[] ParameterTypes, int FieldCount,
        bool HasTypeInitializer, int[] ReturnValues, string IlHex, string ModuleVersionId);
}
