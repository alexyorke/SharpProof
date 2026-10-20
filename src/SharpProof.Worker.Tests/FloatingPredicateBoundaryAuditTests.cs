using System.Collections.Immutable;
using System.Runtime.Loader;
using System.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class FloatingPredicateBoundaryAuditTests
{
    [TestCase("double")]
    [TestCase("float")]
    public async Task NaNInequalityCannotProveFalsePostcondition(string valueType)
    {
        System.ArgumentNullException.ThrowIfNull(valueType);
        object witness = valueType == "double" ? (object)double.NaN : float.NaN;
        var source = CreateSource("(" + valueType + " value)", "", "!Contract.Result<bool>()",
            "var copy = value; return value != copy;");
        var claim = await ObserveAndVerifyAsync(valueType + "-nan-neq", source, [witness]);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
            "The emitted CLR returns true for NaN, so the false-result contract cannot be Proven.");
    }

    [TestCase("object", false)]
    [TestCase("object", true)]
    [TestCase("int[]", false)]
    [TestCase("int[]", true)]
    public async Task SupportedAliasEqualityRemainsProven(string valueType, bool isNull)
    {
        System.ArgumentNullException.ThrowIfNull(valueType);
        object? witness = isNull ? null : valueType == "object" ? new object() : new int[1];
        var source = CreateSource("(" + valueType + "? value)", "", "Contract.Result<bool>()",
            "var copy = value; return value == copy;");
        var scenario = (valueType == "object" ? "object" : "array") + "-alias-" + (isNull ? "null" : "nonnull");
        var claim = await ObserveAndVerifyAsync(scenario, source, [witness]);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase("object", false)]
    [TestCase("object", true)]
    [TestCase("int[]", false)]
    [TestCase("int[]", true)]
    public async Task SupportedNullComparisonsRemainProven(string valueType, bool isNull)
    {
        System.ArgumentNullException.ThrowIfNull(valueType);
        object? witness = isNull ? null : valueType == "object" ? new object() : new int[1];
        var predicate = isNull ? "value == null" : "value != null";
        var body = isNull ? "return null == value;" : "return value != null;";
        var source = CreateSource("(" + valueType + "? value)", predicate, "Contract.Result<bool>()", body);
        var scenario = (valueType == "object" ? "object" : "array") + (isNull ? "-null-left-eq" : "-null-right-neq");
        var claim = await ObserveAndVerifyAsync(scenario, source, [witness]);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SupportedStringContentComparisonsRemainProven(bool inequality)
    {
        var left = new string('a', 1);
        var right = new string(inequality ? 'b' : 'a', 1);
        Assert.That(ReferenceEquals(left, right), Is.False, "The CLR witnesses must have distinct references.");
        var predicate = inequality ? "left != right" : "left == right";
        var source = CreateSource("(string left, string right)", predicate, "Contract.Result<bool>()",
            "return " + predicate + ";");
        var claim = await ObserveAndVerifyAsync(inequality ? "string-content-neq" : "string-content-eq", source, [left, right]);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReferenceConstrainedGenericIdentityIsProvenAndWrongClaimIsRefuted(bool wrongClaim)
    {
        var source = CreateSource("<T>(T value) where T : class", "", wrongClaim ? "!Contract.Result<bool>()" : "Contract.Result<bool>()",
            "var copy = value; return value == copy;");
        var claim = await ObserveAndVerifyAsync(wrongClaim ? "generic-class-eq-wrong-claim" : "generic-class-eq", source, [new object()]);
        Assert.That(claim.Outcome, Is.EqualTo(wrongClaim ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven));
    }

    private static string CreateSource(string signature, string requires, string ensures, string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(ensures);
        System.ArgumentNullException.ThrowIfNull(requires);
        System.ArgumentNullException.ThrowIfNull(signature);
        return "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes;\npublic static class Subject {\n" +
            "    public static bool Target" + signature + " {\n" +
            (requires.Length == 0 ? "" : "        Contract.Requires(" + requires + ");\n") +
            "        Contract.Ensures(" + ensures + ");\n" +
            "        " + body + "\n    }\n}\n";
    }

    private static async Task<WorkerClaimResult> ObserveAndVerifyAsync(string scenario, string source, ImmutableArray<object?> witnesses)
    {
        if (witnesses.IsDefault)
        { throw new ArgumentNullException(nameof(witnesses)); }
        System.ArgumentNullException.ThrowIfNull(scenario);
        System.ArgumentNullException.ThrowIfNull(source);
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ??
            throw new AssertionException("Canonical container repository root is missing.");
        var evidence = Path.Combine(repository, "artifacts", "correctness", "floating-predicates-audit", "nearby-boundaries");
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".observed.cs"), source);
        var compilation = TestCompilation.CreateExact("FloatingPredicateBoundary_" + scenario, [("Subject.cs", source)]);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        var imageBytes = ImmutableArray.Create<byte>(image.GetBuffer().AsSpan(0, checked((int)image.Length)));
        await File.WriteAllBytesAsync(Path.Combine(evidence, scenario + ".oracle.dll"), imageBytes.AsMemory());
        image.Position = 0;
        var runtime = new AssemblyLoadContext("FloatingPredicateBoundary_" + scenario, isCollectible: true);
        string runtimeObservation;
        try
        {
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            if (method.IsGenericMethodDefinition)
            { method = method.MakeGenericMethod(typeof(object)); }
            var actual = (bool)method.Invoke(null, witnesses.ToArray())!;
            runtimeObservation = $"scenario={scenario}; CLR={actual}; " +
                $"IL={Convert.ToHexString(method.GetMethodBody()!.GetILAsByteArray()!)}; " +
                $"sourceSHA256={WorkerProtocolJson.ComputeSha256((Encoding.UTF8.GetBytes(source)).ToImmutableArray())}; " +
                $"PE_SHA256={WorkerProtocolJson.ComputeSha256(imageBytes)}";
            TestContext.WriteLine(runtimeObservation);
            await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".clr.txt"), runtimeObservation + "\n");
            Assert.That(actual, Is.True, runtimeObservation);
        }
        finally { runtime.Unload(); }

        // Emit and contract capture share this exact compilation and source.
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        var hasTotal = artifact.Callables.Single().Total != null;
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        artifact = project.Artifact;
        await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".compiler-artifact.json"),
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken));
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".worker-response.json"), WorkerProtocolJson.SerializeResponse(response));
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var claim = response.ClaimResults.Single();
        var nativeObservation = runtimeObservation +
            $"; capturedTotal={hasTotal}; native={claim.Outcome}; reason={claim.Reason}; vacuity={claim.Vacuity}";
        TestContext.WriteLine(nativeObservation);
        await File.WriteAllTextAsync(Path.Combine(evidence, scenario + ".observation.txt"), nativeObservation + "\n");
        Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        return claim;
    }
}
