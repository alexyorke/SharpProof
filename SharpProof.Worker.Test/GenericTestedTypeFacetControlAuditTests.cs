using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class GenericTestedTypeFacetControlAuditTests
{
    private const string ExpectedSourceHash = "71220A093C7634E3C88382F9E1CB4A6DDD5533E2AF9B1D93F06F7D5DE0827B73";
    private const string Source = """
        using System.Runtime.CompilerServices;
        using SharpProof.Attributes;
        
        public static class Subject
        {
            [EnforcePure, DoesNotThrow]
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static bool Target<T, U>(T value)
            {
                return value is U;
            }
        }
        """ + "\n";

    [Test]
    public async Task GenericTestedTypePreservesPurityAndDoesNotThrow()
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT");
        Assert.That(repositoryRoot, Is.Not.Null.And.Not.Empty);
        var evidenceDirectory = Path.Combine(repositoryRoot!, "artifacts", "correctness", "generic-type-values-audit",
            "facet-control");
        Directory.CreateDirectory(evidenceDirectory);
        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source)));
        Assert.That(sourceHash, Is.EqualTo(ExpectedSourceHash), "Frozen facet source changed.");
        var compilation = TestCompilation.Create("GenericTestedTypeFacetControl", ("Subject.cs", Source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var runtime = new AssemblyLoadContext("GenericTestedTypeFacetControl", isCollectible: true);
        CaseObservation[] cases;
        try
        {
            var generic = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            var objectTarget = generic.MakeGenericMethod(typeof(int), typeof(object)).CreateDelegate<Func<int, bool>>();
            var interfaceTarget = generic.MakeGenericMethod(typeof(int), typeof(IComparable)).CreateDelegate<Func<int, bool>>();
            var stringTarget = generic.MakeGenericMethod(typeof(int), typeof(string)).CreateDelegate<Func<int, bool>>();
            cases =
            [
                new("int/object", true, objectTarget(7)),
                new("int/IComparable", true, interfaceTarget(7)),
                new("int/string", false, stringTarget(7))
            ];
            var flags = generic.GetMethodImplementationFlags();
            await Save(evidenceDirectory, "facet-runtime.json", new
            {
                SourceSha256 = sourceHash,
                Runtime = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Optimization = compilation.Options.OptimizationLevel.ToString(),
                TargetNoInlining = (flags & MethodImplAttributes.NoInlining) != 0,
                TargetNoOptimization = (flags & MethodImplAttributes.NoOptimization) != 0,
                TypedDelegate = "Func<int,bool>",
                InputValue = 7,
                Cases = cases,
                IlHex = Convert.ToHexString(generic.GetMethodBody()!.GetILAsByteArray()!),
                AllocationCoverage = "No allocation measurement or ZeroAllocations claim in this focused facet control. Original immutable oracle observations provide allocation grounding."
            });
            using (Assert.EnterMultipleScope())
            {
                Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
                Assert.That((flags & MethodImplAttributes.NoInlining) != 0, Is.True);
                Assert.That((flags & MethodImplAttributes.NoOptimization) != 0, Is.False);
                foreach (var observed in cases)
                {
                    Assert.That(observed.Actual, Is.EqualTo(observed.Expected), observed.Name);
                }
            }
        }
        finally
        {
            runtime.Unload();
        }
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var manifest = discovery.Manifest;
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var expectedClaims = manifest.Claims.ToDictionary(claim => claim.ClaimId, StringComparer.Ordinal);
        var observedClaims = response.ClaimResults.Select(claim =>
        {
            var known = expectedClaims.TryGetValue(claim.ClaimId, out var entry);
            return new
            {
                claim.ClaimId,
                ContractKind = known ? entry!.EffectContractKind.ToString() : "UnknownClaimId",
                Outcome = claim.Outcome.ToString(),
                Reason = claim.Reason.ToString()
            };
        }).ToArray();
        await Save(evidenceDirectory, "facet-native.json", new
        {
            SourceSha256 = sourceHash,
            SameCompilationForClrAndNative = true,
            CacheEnabled = project.Request.Cache.Enabled,
            Budgets = project.Request.Budgets,
            Errors = response.Errors,
            ManifestClaims = manifest.Claims.Select(claim => new
            {
                claim.ClaimId,
                Kind = claim.Kind.ToString(),
                ContractKind = claim.EffectContractKind.ToString()
            }).ToArray(),
            Claims = observedClaims,
            Cases = cases,
            FrozenExpectation = "Both EnforcePure and DoesNotThrow remain Proven independently of potential allocation."
        });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(project.Request.Cache.Enabled, Is.False);
            Assert.That(manifest.Claims, Has.Length.EqualTo(2));
            Assert.That(manifest.Claims.All(claim => claim.Kind == WorkerClaimKind.Effect), Is.True);
            Assert.That(manifest.Claims.Select(claim => claim.EffectContractKind), Is.EquivalentTo(new[]
            {
                WorkerEffectContractKind.EnforcePure, WorkerEffectContractKind.DoesNotThrow
            }));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
            Assert.That(response.ClaimResults.Select(claim => claim.ClaimId), Is.EquivalentTo(manifest.Claims.Select(claim => claim.ClaimId)));
            foreach (var claim in response.ClaimResults)
            {
                Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
                    observedClaims.Single(observed => observed.ClaimId == claim.ClaimId).ContractKind + ": " + claim.Reason);
            }
        }
    }

    private static Task Save(string directory, string name, object value)
    {
        return File.WriteAllTextAsync(Path.Combine(directory, name), JsonSerializer.Serialize(value) + "\n");
    }

    private sealed record CaseObservation(string Name, bool Expected, bool Actual);
}
