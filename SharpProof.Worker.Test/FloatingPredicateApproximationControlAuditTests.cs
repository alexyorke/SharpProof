using System.Runtime.Loader;
using System.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class FloatingPredicateApproximationControlAuditTests
{
    [TestCase("double", false)]
    [TestCase("double", true)]
    [TestCase("float", false)]
    [TestCase("float", true)]
    public async Task IndependentResultTautologyRemainsProven(string valueType, bool inequality)
    {
        var comparison = inequality ? "!=" : "==";
        var source = CreateSource(valueType + " value", "",
            "var copy = value; var ignored = value " + comparison + " copy; return true;");
        object witness = valueType == "double" ? (object)double.NaN : float.NaN;
        await ObserveAndVerifyAsync(valueType + "-independent-" + (inequality ? "neq" : "eq"), source, [witness]);
    }

    [TestCase("double", false)]
    [TestCase("double", true)]
    [TestCase("float", false)]
    [TestCase("float", true)]
    public async Task UnreachableFloatingComparisonRemainsProven(string valueType, bool inequality)
    {
        var comparison = inequality ? "!=" : "==";
        var source = CreateSource(valueType + " value, bool compare", "!compare",
            "var copy = value; if (compare) return value " + comparison + " copy; return true;");
        object witness = valueType == "double" ? (object)double.NaN : float.NaN;
        await ObserveAndVerifyAsync(valueType + "-unreachable-" + (inequality ? "neq" : "eq"), source, [witness, false]);
    }

    [Test]
    public async Task SupportedReferenceCopyEqualityRemainsProven()
    {
        var source = CreateSource("object value", "", "var copy = value; return value == copy;");
        await ObserveAndVerifyAsync("object-copy-eq", source, [new object()]);
    }

    private static string CreateSource(string parameters, string requires, string body)
    {
        return "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes;\npublic static class Subject {\n" +
            "    public static bool Target(" + parameters + ") {\n" +
            (requires.Length == 0 ? "" : "        Contract.Requires(" + requires + ");\n") +
            "        Contract.Ensures(Contract.Result<bool>());\n" +
            "        " + body + "\n    }\n}\n";
    }

    private static async Task ObserveAndVerifyAsync(string scenario, string source, object?[] witnesses)
    {
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ??
            throw new AssertionException("Canonical container repository root is missing.");
        var evidence = Path.Combine(repository, "artifacts", "correctness", "floating-predicates-audit", "approximation-controls");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".observed.cs"), source);
        var compilation = TestCompilation.Create("FloatingPredicateApproximationControl_" + scenario, ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, scenario + ".oracle.dll"), imageBytes);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("FloatingPredicateApproximationControl_" + scenario, isCollectible: true);
        string runtimeObservation;
        try
        {
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            var actual = (bool)method.Invoke(null, witnesses)!;
            runtimeObservation = $"scenario={scenario}; CLR={actual}; " +
                $"IL={Convert.ToHexString(method.GetMethodBody()!.GetILAsByteArray()!)}; " +
                $"sourceSHA256={WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(source))}; " +
                $"PE_SHA256={WorkerProtocolJson.ComputeSha256(imageBytes)}";
            TestContext.WriteLine(runtimeObservation);
            await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".clr.txt"), runtimeObservation + "\n");
            Assert.That(actual, Is.True, runtimeObservation);
        }
        finally { runtime.Unload(); }

        // The emitted CLR oracle and captured contract use this same compilation.
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".compiler-artifact.json"),
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        var hasTotal = artifact.Callables.Single().Total != null;
        Assert.That(hasTotal, Is.True, "The control requires an admitted compiler-captured total body.");
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".worker-response.json"), WorkerProtocolJson.SerializeResponse(response));
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var claim = response.ClaimResults.Single();
        var observation = runtimeObservation +
            $"; capturedTotal={hasTotal}; native={claim.Outcome}; reason={claim.Reason}; vacuity={claim.Vacuity}";
        TestContext.WriteLine(observation);
        await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".observation.txt"), observation + "\n");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), observation);
        Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None), observation);
    }
}
