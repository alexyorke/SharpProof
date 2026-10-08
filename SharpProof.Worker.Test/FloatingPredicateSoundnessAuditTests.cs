using System.Runtime.Loader;
using System.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class FloatingPredicateSoundnessAuditTests
{
    private const string SourceTemplate = """
        #undef SHARPPROOF_CONTRACTS
        using SharpProof.Attributes;
        public static class Subject {
            public static bool Target(__VALUE_TYPE__ value) {
                Contract.Ensures(Contract.Result<bool>());
                var copy = value;
                return value == copy;
            }
        }
        """ + "\n";

    [TestCase("double")]
    [TestCase("float")]
    public async Task NaNEqualityCannotProveFalsePostcondition(string valueType)
    {
        object witness = valueType == "double" ? (object)double.NaN : float.NaN;
        var claim = await ObserveAndVerifyAsync(valueType, witness, expectedRuntime: false);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
            "The emitted CLR returns false for NaN, so the true-result contract cannot be Proven.");
    }

    [Test]
    public async Task SupportedReferenceCopyEqualityRemainsProven()
    {
        var claim = await ObserveAndVerifyAsync("object", new object(), expectedRuntime: true);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    private static async Task<WorkerClaimResult> ObserveAndVerifyAsync(string valueType, object witness, bool expectedRuntime)
    {
        var source = SourceTemplate.Replace("__VALUE_TYPE__", valueType, StringComparison.Ordinal);
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ??
            throw new AssertionException("Canonical container repository root is missing.");
        var evidence = Path.Combine(repository, "artifacts", "correctness", "floating-predicates-audit");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, valueType + ".observed.cs"), source);
        var compilation = TestCompilation.Create("FloatingPredicateSoundness_" + valueType, ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, valueType + ".oracle.dll"), imageBytes);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("FloatingPredicateSoundness_" + valueType, isCollectible: true);
        string runtimeObservation;
        try
        {
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            var actual = (bool)method.Invoke(null, [witness])!;
            var argument = valueType == "object" ? "same non-null object reference" : "NaN";
            runtimeObservation = $"type={valueType}; argument={argument}; CLR={actual}; " +
                $"IL={Convert.ToHexString(method.GetMethodBody()!.GetILAsByteArray()!)}; " +
                $"sourceSHA256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(source))}; " +
                $"PE_SHA256={WorkerProtocolJson.ComputeSha256(imageBytes)}";
            TestContext.WriteLine(runtimeObservation);
            await File.WriteAllTextAsync(Path.Combine(evidence, valueType + ".clr.txt"), runtimeObservation + "\n");
            Assert.That(actual, Is.EqualTo(expectedRuntime), runtimeObservation);
        }
        finally { runtime.Unload(); }

        // The proof artifact and emitted CLR oracle use this same compilation.
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(artifact.Callables.Single().Total, Is.Not.Null, "Expected admitted compiler-captured total body.");
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        await File.WriteAllTextAsync(Path.Combine(evidence, valueType + ".compiler-artifact.json"),
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidence, valueType + ".worker-response.json"),
            WorkerProtocolJson.SerializeResponse(response));
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var claim = response.ClaimResults.Single();
        var nativeObservation = runtimeObservation + $"; native={claim.Outcome}; reason={claim.Reason}; vacuity={claim.Vacuity}";
        TestContext.WriteLine(nativeObservation);
        await File.WriteAllTextAsync(Path.Combine(evidence, valueType + ".observation.txt"), nativeObservation + "\n");
        Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        return claim;
    }
}
