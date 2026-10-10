using System.Runtime.Loader;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// With one non-constant operand, `value + ""` (or `"" + value`, `value + null`)
// is emitted as `value ?? ""`: it keeps the operand's reference, unlike
// String.Concat, which canonicalizes two empty operands to the "" literal.
[TestFixture]
public sealed class SingleOperandConcatenationIdentityTests
{
    private const string Guard = "if (value == null || value.Length != 0) { return true; } ";

    [TestCase("object result = value + \"\"; object empty = \"\"; return result == empty;")]
    [TestCase("object result = \"\" + value; object empty = \"\"; return result == empty;")]
    [TestCase("object result = value + null; object empty = \"\"; return result == empty;")]
    [TestCase("string text = value + \"\"; object result = text; object empty = \"\"; return result == empty || other == null;")]
    public async Task DistinctEmptyOperandIsNotTheLiteral(string body)
    {
        var (runtime, claim) = await ObserveAndVerifyAsync(Guard + body, DistinctEmptyString(), "");
        Assert.That(runtime, Is.False, "The CLR keeps the distinct empty operand.");
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
            "A single-operand concatenation keeps its operand, so it need not be the literal.");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    [TestCase("object result = value + \"\"; object original = value; return value == null || result == original;", true)]
    [TestCase("object result = value + \"\"; object original = value; return value == null || result == original;", false)]
    [TestCase("object result = \"\" + value; object original = value; return value == null || result == original;", false)]
    public async Task SingleOperandKeepsItsReference(string body, bool distinctEmpty)
    {
        var (runtime, claim) = await ObserveAndVerifyAsync(body, distinctEmpty ? DistinctEmptyString() : "x", "");
        Assert.That(runtime, Is.True);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [TestCase("object result = value + \"\"; object empty = \"\"; return value != null || result == empty;")]
    [TestCase("string text = value + \"\"; return text != null && text.Length == (value == null ? 0 : value.Length);")]
    public async Task NullOperandStillBecomesTheLiteral(string body)
    {
        var (runtime, claim) = await ObserveAndVerifyAsync(body, null, "");
        Assert.That(runtime, Is.True);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task NonNullOperandIsNeverAFreshString()
    {
        var (runtime, claim) = await ObserveAndVerifyAsync(
            "object result = value + \"\"; object original = value; return result != original;", "x", "");
        Assert.That(runtime, Is.False);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    [Test]
    public async Task TwoDistinctEmptyOperandsConcatenateToTheLiteral()
    {
        var (runtime, claim) = await ObserveAndVerifyAsync(Guard +
            "if (other == null || other.Length != 0) { return true; } object result = value + other; object empty = \"\"; return result == empty;",
            DistinctEmptyString(), DistinctEmptyString());
        Assert.That(runtime, Is.True);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

#pragma warning disable CS0618 // string.Copy is the documented way to obtain a distinct string instance.
    private static string DistinctEmptyString()
    {
        var copy = string.Copy("");
        Assert.That(copy, Has.Length.EqualTo(0));
        Assert.That(ReferenceEquals(copy, ""), Is.False);
        return copy;
    }
#pragma warning restore CS0618

    private static async Task<(bool Runtime, WorkerClaimResult Claim)> ObserveAndVerifyAsync(string body, string? value, string other)
    {
        var source = """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Target(string value, string other) {
                    Contract.Ensures(Contract.Result<bool>());
                    __BODY__
                }
            }
            """.Replace("__BODY__", body, StringComparison.Ordinal) + "\n";
        var compilation = TestCompilation.Create("SingleOperandConcatenationIdentity", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("SingleOperandConcatenationIdentity", isCollectible: true);
        bool runtime;
        try
        {
            var method = context.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            runtime = (bool)method.Invoke(null, [value, other])!;
        }
        finally { context.Unload(); }

        // The proof artifact and the CLR oracle use this same compilation.
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(artifact.Callables.Single().Total, Is.Not.Null, "Expected admitted compiler-captured total body.");
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var claim = response.ClaimResults.Single();
        TestContext.WriteLine($"CLR={runtime}; native={claim.Outcome}; reason={claim.Reason}; vacuity={claim.Vacuity}");
        Assert.That(claim.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        return (runtime, claim);
    }
}
