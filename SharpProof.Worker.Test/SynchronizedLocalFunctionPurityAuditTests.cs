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
public sealed class SynchronizedLocalFunctionPurityAuditTests
{
    private static readonly int[] s_inputValues = [-7, 0, 42];
    private const string SubjectTemplate = """
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;

        public static class Subject
        {
            public static int Outer(int value)
            {
                [EnforcePure]
                [MethodImpl(__METHOD_IMPL_OPTIONS__)]
                static int Local(int argument) => argument;
                return Local(value);
            }
        }
        """ + "\n";

    [TestCase(false)]
    [TestCase(true)]
    public async Task SelectedLocalSynchronizationMatchesNativePurity(bool synchronized)
    {
        var options = synchronized ? "MethodImplOptions.NoInlining | MethodImplOptions.Synchronized"
            : "MethodImplOptions.NoInlining";
        var source = SubjectTemplate.Replace("__METHOD_IMPL_OPTIONS__", options, StringComparison.Ordinal);
        var sourceHash = Hash(Encoding.UTF8.GetBytes(source));
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var caseName = synchronized ? "synchronized" : "no-inlining-control";
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "synchronized-selected-local-purity-audit",
            caseName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "subject-source.cs"), source);

        var compilation = TestCompilation.Create("SynchronizedLocalFunctionPurity_" + caseName, ("Subject.cs", source));
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
        var loadContext = new AssemblyLoadContext("SynchronizedLocalFunctionPurity_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        RuntimeObservation runtimeObservation;
        try
        {
            var subject = loadContext.LoadFromStream(image).GetType("Subject")!;
            var outer = subject.GetMethod("Outer", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
            var outerIl = outer.GetMethodBody()!.GetILAsByteArray()!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(outerIl, Has.Length.EqualTo(7));
                Assert.That(outerIl[0], Is.EqualTo(0x02));
                Assert.That(outerIl[1], Is.EqualTo(0x28));
                Assert.That(outerIl[6], Is.EqualTo(0x2a));
            }
            var callToken = BitConverter.ToInt32(outerIl, 2);
            var method = (MethodInfo)outer.Module.ResolveMethod(callToken)!;
            var flags = method.GetMethodImplementationFlags();
            var execute = outer.CreateDelegate<Func<int, int>>();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(method.DeclaringType, Is.EqualTo(subject));
                Assert.That(method.Module.ModuleVersionId, Is.EqualTo(outer.Module.ModuleVersionId));
                Assert.That(method.MetadataToken, Is.EqualTo(callToken));
                Assert.That(method.Name.StartsWith("<Outer>g__Local|", StringComparison.Ordinal), Is.True);
            }
            await Save(evidenceDirectory, "generated-local-binding.json", new
            {
                SourceSha256 = sourceHash,
                EmittedPeSha256 = Hash(imageBytes),
                OuterName = outer.Name,
                OuterMetadataToken = outer.MetadataToken,
                OuterIlHex = Convert.ToHexStringLower(outerIl),
                CallMetadataToken = callToken,
                GeneratedLocalName = method.Name,
                GeneratedLocalMetadataToken = method.MetadataToken,
                ModuleVersionId = method.Module.ModuleVersionId.ToString("D"),
                ExactCallTokenResolution = method.DeclaringType == subject && method.MetadataToken == callToken,
                RawLocalImplementationFlags = (int)flags,
                LocalIlHex = Convert.ToHexStringLower(method.GetMethodBody()!.GetILAsByteArray()!)
            });
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
                TypedInvocation = "Func<int,int> bound to public Outer proxy calling generated Local",
                Inputs = s_inputValues,
                Observation = runtimeObservation,
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
                Assert.That(runtimeObservation.IsStatic, Is.True);
                Assert.That(runtimeObservation.ReturnsInt32, Is.True);
                Assert.That(runtimeObservation.ParameterTypes, Has.Length.EqualTo(1));
                Assert.That(runtimeObservation.ParameterTypes.Single(), Is.EqualTo(typeof(int).FullName));
                Assert.That(runtimeObservation.FieldCount, Is.Zero);
                Assert.That(runtimeObservation.HasTypeInitializer, Is.False);
                Assert.That(runtimeObservation.ReturnValues, Is.EqualTo(s_inputValues));
                Assert.That(runtimeObservation.IlHex, Is.EqualTo("022a"));
            }
        }
        finally
        {
            loadContext.Unload();
        }

        var tree = compilation.SyntaxTrees.Single();
        var syntaxRoot = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var localSyntax = syntaxRoot.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var localSymbol = (IMethodSymbol)model.GetDeclaredSymbol(localSyntax)!;
        var outerSyntax = syntaxRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var outerSymbol = (IMethodSymbol)model.GetDeclaredSymbol(outerSyntax)!;
        var discovery = new ClaimManifestBuilder(compilation).Build();
        Assert.That(discovery.Targets, Has.Count.EqualTo(1));
        var selectedTarget = discovery.Targets.Values.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(localSymbol.MethodKind, Is.EqualTo(MethodKind.LocalFunction));
            Assert.That(localSymbol.Name, Is.EqualTo("Local"));
            Assert.That(localSymbol.IsStatic, Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(selectedTarget.Method, localSymbol), Is.True);
            Assert.That(SymbolEqualityComparer.Default.Equals(selectedTarget.Method, outerSymbol), Is.False);
            Assert.That(selectedTarget.Declaration, Is.TypeOf<LocalFunctionStatementSyntax>());
            Assert.That(selectedTarget.Declaration!.Span, Is.EqualTo(localSyntax.Span));
            Assert.That(selectedTarget.EffectClaims, Has.Length.EqualTo(1));
            Assert.That(selectedTarget.EffectClaims.Single().HasValidConstraint, Is.True);
        }
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
            SelectedSourceMethod = localSymbol.ToDisplayString(),
            SelectedMethodKind = localSymbol.MethodKind.ToString(),
            SelectedSourceSpanStart = localSyntax.SpanStart,
            SelectedSourceSpanLength = localSyntax.Span.Length,
            OuterProxySourceMethod = outerSymbol.ToDisplayString(),
            SelectedSourceIsLocal = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, localSymbol),
            SelectedSourceIsOuter = SymbolEqualityComparer.Default.Equals(selectedTarget.Method, outerSymbol),
            LegacyCallableFailureReason = artifact.Callables.Single().FailureReason.ToString(),
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
                claim.CallableId,
                Kind = claim.Kind.ToString(),
                ContractKind = claim.EffectContractKind.ToString()
            }).ToArray(),
            Claims = response.ClaimResults.Select(claim => new
            {
                claim.ClaimId,
                CallableId = artifact.Manifest.Claims.Single(manifestClaim => manifestClaim.ClaimId == claim.ClaimId).CallableId,
                Outcome = claim.Outcome.ToString(),
                Reason = claim.Reason.ToString(),
                EffectCertainty = claim.EffectCertainty.ToString(),
                Vacuity = claim.Vacuity.ToString()
            }).ToArray(),
            FrozenExpectation = synchronized ? "The selected synchronized local function cannot be Proven EnforcePure."
                : "The selected NoInlining-only local function remains Proven EnforcePure with complete native may-effect evidence."
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
                    "CLR synchronization is excluded by the local function EnforcePure claim, even when its written body only returns its input.");
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
        bool Synchronized, bool IsStatic, bool ReturnsInt32, string?[] ParameterTypes, int FieldCount,
        bool HasTypeInitializer, int[] ReturnValues, string IlHex, string ModuleVersionId);
}
