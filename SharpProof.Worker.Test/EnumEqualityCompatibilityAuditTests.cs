using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EnumEqualityCompatibilityAuditTests
{
    private const string SourceTemplate = """
        #undef SHARPPROOF_CONTRACTS
        using SharpProof.Attributes;
        public enum Choice { First = 1, Second = 2 }
        public static class Subject {
            public static bool Target(Choice value) {
                Contract.Ensures(__ENSURES__);
                var copy = value;
                return value __OPERATOR__ copy;
            }
        }
        """ + "\n";

    [TestCase("==", true)]
    [TestCase("!=", false)]
    public async Task CopiedEnumEqualityRetainsExactNativeProof(string comparison, bool expectedRuntime)
    {
        var ensures = expectedRuntime ? "Contract.Result<bool>()" : "!Contract.Result<bool>()";
        var source = SourceTemplate.Replace("__ENSURES__", ensures, StringComparison.Ordinal)
            .Replace("__OPERATOR__", comparison, StringComparison.Ordinal);
        var caseName = expectedRuntime ? "equal" : "not-equal";
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ??
            throw new AssertionException("Canonical container repository root is missing.");
        var evidence = Path.Combine(repository, "artifacts", "correctness", "numeric-abstraction-policy-audit",
            "enum-compatibility");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, caseName + ".observed.cs"), source);
        var compilation = TestCompilation.Create("EnumEqualityCompatibility_" + caseName, ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        var warnings = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning).ToArray();
        Assert.That(warnings, Is.Empty, string.Join("\n", warnings.Select(diagnostic => diagnostic.ToString())));
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, caseName + ".oracle.dll"), imageBytes);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("EnumEqualityCompatibility_" + caseName, isCollectible: true);
        string runtimeObservation;
        try
        {
            var assembly = runtime.LoadFromStream(image);
            var method = assembly.GetType("Subject")!.GetMethod("Target")!;
            var witness = Enum.Parse(assembly.GetType("Choice")!, "Second");
            var actual = (bool)method.Invoke(null, [witness])!;
            runtimeObservation = $"operator={comparison}; argument=Choice.Second; CLR={actual}; " +
                $"IL={Convert.ToHexString(method.GetMethodBody()!.GetILAsByteArray()!)}; " +
                $"sourceSHA256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(source))}; " +
                $"PE_SHA256={WorkerProtocolJson.ComputeSha256(imageBytes)}";
            TestContext.WriteLine(runtimeObservation);
            await File.WriteAllTextAsync(Path.Combine(evidence, caseName + ".clr.txt"), runtimeObservation + "\n");
            Assert.That(actual, Is.EqualTo(expectedRuntime), runtimeObservation);
        }
        finally
        {
            runtime.Unload();
        }

        // The compiler artifact and CLR oracle use this same compilation.
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        var declaredClaim = artifact.Manifest.Claims.Single();
        Assert.That(declaredClaim.Kind, Is.EqualTo(WorkerClaimKind.Postcondition));
        var captured = artifact.Callables.Single(callable => callable.CallableId == declaredClaim.CallableId);
        Assert.That(captured.Total, Is.Not.Null, "Copied enum equality must retain captured Total.");
        await File.WriteAllTextAsync(Path.Combine(evidence, caseName + ".compiler-artifact.json"),
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        var preparation = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId == declaredClaim.CallableId);
        Assert.That(preparation.Total, Is.Not.Null);
        var total = preparation.Total!;
        Assert.That(total.IsBodyAbstraction, Is.False);
        var clause = total.Clauses.Single(candidate => candidate.Kind == CompilerContractKind.Ensures);
        Assert.That(clause.ClaimId, Is.EqualTo(declaredClaim.ClaimId));
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidence, caseName + ".worker-response.json"),
            WorkerProtocolJson.SerializeResponse(response));
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.ClaimId, Is.EqualTo(declaredClaim.ClaimId));
        var observation = runtimeObservation + $"; capturedTotal=True; native={claim.Outcome}; " +
            $"reason={claim.Reason}; vacuity={claim.Vacuity}";
        TestContext.WriteLine(observation);
        await File.WriteAllTextAsync(Path.Combine(evidence, caseName + ".observation.txt"), observation + "\n");
        Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), observation);
        Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.None));
    }
}
