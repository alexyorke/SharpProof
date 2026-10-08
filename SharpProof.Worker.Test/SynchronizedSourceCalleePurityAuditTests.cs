using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class SynchronizedSourceCalleePurityAuditTests
{
    private static readonly int[] s_inputValues = [-7, 0, 42];
    private const string SubjectTemplate = """
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;

        public static class Subject
        {
            [EnforcePure]
            public static int Target(int value) => Helper(value);

            [MethodImpl(__METHOD_IMPL_OPTIONS__)]
            private static int Helper(int value) => value;
        }
        """ + "\n";

    [TestCase(false)]
    [TestCase(true)]
    public async Task OrdinarySourceHelperSynchronizationMatchesCallerPurity(bool synchronized)
    {
        var options = synchronized ? "MethodImplOptions.NoInlining | MethodImplOptions.Synchronized"
            : "MethodImplOptions.NoInlining";
        var source = SubjectTemplate.Replace("__METHOD_IMPL_OPTIONS__", options, StringComparison.Ordinal);
        var sourceHash = Hash(Encoding.UTF8.GetBytes(source));
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var caseName = synchronized ? "synchronized" : "no-inlining-control";
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "synchronized-source-callee-purity-audit",
            caseName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject-source.cs"), source);

        var compilation = TestCompilation.Create("SynchronizedSourceCalleePurity_" + caseName, ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        var subjectSymbol = compilation.GetTypeByMetadataName("Subject")!;
        var targetSymbol = subjectSymbol.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var helperSymbol = subjectSymbol.GetMembers("Helper").OfType<IMethodSymbol>().Single();
        var helperSyntax = await helperSymbol.DeclaringSyntaxReferences.Single().GetSyntaxAsync();
        var expectedHelperFlags = MethodImplAttributes.NoInlining |
            (synchronized ? MethodImplAttributes.Synchronized : MethodImplAttributes.IL);
        await Save(evidenceDirectory, "source-binding.json", new
        {
            SourceSha256 = sourceHash,
            Target = targetSymbol.ToDisplayString(),
            Helper = helperSymbol.ToDisplayString(),
            HelperMethodKind = helperSymbol.MethodKind.ToString(),
            HelperImplementationFlags = (int)helperSymbol.MethodImplementationFlags,
            HelperSyntaxType = helperSyntax.GetType().Name,
            ExpectedHelperImplementationFlags = (int)expectedHelperFlags
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(helperSymbol.MethodKind, Is.EqualTo(MethodKind.Ordinary));
            Assert.That(helperSyntax, Is.InstanceOf<MethodDeclarationSyntax>());
            Assert.That(helperSymbol.MethodImplementationFlags, Is.EqualTo(expectedHelperFlags));
            Assert.That(targetSymbol.MethodImplementationFlags, Is.EqualTo(MethodImplAttributes.IL));
        }
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
        var loadContext = new AssemblyLoadContext("SynchronizedSourceCalleePurity_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        RuntimeObservation runtimeObservation;
        SourceCallObservation callObservation;
        try
        {
            var subject = loadContext.LoadFromStream(image).GetType("Subject")!;
            var target = subject.GetMethod("Target", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
            var method = subject.GetMethod("Helper", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
            var flags = method.GetMethodImplementationFlags();
            var execute = target.CreateDelegate<Func<int, int>>();
            var targetIl = target.GetMethodBody()!.GetILAsByteArray()!;
            Assert.That(targetIl, Has.Length.EqualTo(7), "Release Target must directly call the ordinary source helper.");
            Assert.That(targetIl[0], Is.EqualTo((byte)System.Reflection.Emit.OpCodes.Ldarg_0.Value));
            Assert.That(targetIl[1], Is.EqualTo((byte)System.Reflection.Emit.OpCodes.Call.Value));
            Assert.That(targetIl[6], Is.EqualTo((byte)System.Reflection.Emit.OpCodes.Ret.Value));
            var callToken = BitConverter.ToInt32(targetIl, 2);
            var calledMethod = subject.Module.ResolveMethod(callToken)!;
            callObservation = new(
                target.Name,
                target.MetadataToken,
                (int)target.GetMethodImplementationFlags(),
                target.IsStatic,
                target.ReturnType == typeof(int),
                target.GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray(),
                Convert.ToHexStringLower(targetIl),
                method.Name,
                method.MetadataToken,
                method.IsPrivate,
                callToken,
                calledMethod.Name,
                calledMethod.DeclaringType?.FullName,
                calledMethod.MetadataToken,
                calledMethod.Module.ModuleVersionId.ToString("D"));
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
                TypedInvocation = "Func<int,int>",
                Inputs = s_inputValues,
                Observation = runtimeObservation,
                CallBinding = callObservation,
                SynchronizationBasis = "Exact reflected Helper Synchronized flag, Target call-token binding, and CLR runtime source semantics; no contention or timing observation.",
                ClrSource = "https://github.com/dotnet/runtime/blob/v9.0.0/src/coreclr/jit/flowgraph.cpp#L1572-L1588",
                RuntimeLimit = "Runtime identity is recorded. The reference CLR source is v9.0.0; no JIT machine-code disassembly is performed."
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
                Assert.That(runtimeObservation.RawImplementationFlags, Is.EqualTo((int)expectedHelperFlags));
                Assert.That(runtimeObservation.NoInlining, Is.True);
                Assert.That(runtimeObservation.Synchronized, Is.EqualTo(synchronized));
                Assert.That(runtimeObservation.IlHex, Is.EqualTo("022a"), "Helper's written body only returns its int parameter.");
                Assert.That(callObservation.TargetImplementationFlags, Is.Zero);
                Assert.That(callObservation.TargetIsStatic, Is.True);
                Assert.That(callObservation.TargetReturnsInt32, Is.True);
                Assert.That(callObservation.TargetParameterTypes, Has.Length.EqualTo(1));
                Assert.That(callObservation.TargetParameterTypes.Single(), Is.EqualTo(typeof(int).FullName));
                Assert.That(callObservation.TargetName, Is.EqualTo("Target"));
                Assert.That(callObservation.HelperName, Is.EqualTo("Helper"));
                Assert.That(callObservation.HelperIsPrivate, Is.True);
                Assert.That(callObservation.CallMetadataToken, Is.EqualTo(callObservation.HelperMetadataToken));
                Assert.That(callObservation.ResolvedMetadataToken, Is.EqualTo(callObservation.HelperMetadataToken));
                Assert.That(callObservation.CalledMethodName, Is.EqualTo("Helper"));
                Assert.That(callObservation.CalledDeclaringType, Is.EqualTo("Subject"));
                Assert.That(callObservation.ResolvedModuleVersionId, Is.EqualTo(runtimeObservation.ModuleVersionId));
                Assert.That(runtimeObservation.IsStatic, Is.True);
                Assert.That(runtimeObservation.ReturnsInt32, Is.True);
                Assert.That(runtimeObservation.ParameterTypes, Has.Length.EqualTo(1));
                Assert.That(runtimeObservation.ParameterTypes.Single(), Is.EqualTo(typeof(int).FullName));
                Assert.That(runtimeObservation.FieldCount, Is.Zero);
                Assert.That(runtimeObservation.HasTypeInitializer, Is.False);
                Assert.That(runtimeObservation.ReturnValues, Is.EqualTo(s_inputValues));
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
            ReflectedHelperImplementationFlags = runtimeObservation.ImplementationFlags,
            ReflectedHelperSynchronized = runtimeObservation.Synchronized,
            SourceCallBinding = callObservation,
            TargetSymbol = targetSymbol.ToDisplayString(),
            HelperSymbol = helperSymbol.ToDisplayString(),
            HelperSourceImplementationFlags = (int)helperSymbol.MethodImplementationFlags,
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
            FrozenExpectation = synchronized ? "The caller of the synchronized ordinary source helper cannot be Proven EnforcePure."
                : "The caller of the NoInlining-only ordinary source helper remains Proven EnforcePure with complete native may-effect evidence."
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
            Assert.That(artifact.Manifest.Claims.Single().Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(artifact.Manifest.Claims.Single().EffectContractKind, Is.EqualTo(WorkerEffectContractKind.EnforcePure));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            var claim = response.ClaimResults.Single();
            Assert.That(claim.ClaimId, Is.EqualTo(artifact.Manifest.Claims.Single().ClaimId));
            Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            if (synchronized)
            {
                Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
                    "CLR helper synchronization is excluded by caller EnforcePure, even when the helper written body only returns its input.");
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

    private sealed record SourceCallObservation(string TargetName, int TargetMetadataToken, int TargetImplementationFlags,
        bool TargetIsStatic, bool TargetReturnsInt32, string?[] TargetParameterTypes, string TargetIlHex,
        string HelperName, int HelperMetadataToken, bool HelperIsPrivate, int CallMetadataToken,
        string CalledMethodName, string? CalledDeclaringType, int ResolvedMetadataToken, string ResolvedModuleVersionId);

    private sealed record RuntimeObservation(int RawImplementationFlags, string ImplementationFlags, bool NoInlining,
        bool Synchronized, bool IsStatic, bool ReturnsInt32, string?[] ParameterTypes, int FieldCount,
        bool HasTypeInitializer, int[] ReturnValues, string IlHex, string ModuleVersionId);
}
