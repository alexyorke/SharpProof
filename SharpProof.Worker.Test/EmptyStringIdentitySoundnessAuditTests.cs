using System.Runtime.Loader;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A zero-length string is not necessarily the interned "" literal: the CLR can
// allocate distinct empty strings (string.Copy("") is one such source).
[TestFixture]
public sealed class EmptyStringIdentitySoundnessAuditTests
{
    private const string Guard = "if (value == null || value.Length != 0) { return true; } ";

    [TestCase("object first = value; object second = \"\"; return first == second;")]
    [TestCase("object first = value; object second = \"\"; return first == second || other == null;")]
    [TestCase("object first = value; object second = other; return other == null || other.Length != 0 || first == second;")]
    public async Task DistinctEmptyStringCannotProveLiteralIdentity(string body)
    {
        var (runtime, claim) = await ObserveAndVerifyAsync(Guard + body, DistinctEmptyString(), "");
        Assert.That(runtime, Is.False, "The CLR witness must expose a distinct empty string.");
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven),
            "A distinct empty string is not the literal, so the identity contract cannot be Proven.");
        // The witness replays as a distinct concrete empty string.
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    [TestCase("return value == \"\";")]
    [TestCase("object first = value; object second = value; return first == second;")]
    public async Task EmptyStringContentAndSelfIdentityRemainProven(string body)
    {
        var (runtime, claim) = await ObserveAndVerifyAsync(Guard + body, DistinctEmptyString(), "");
        Assert.That(runtime, Is.True);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task DifferentLengthIdentityRemainsRefuted()
    {
        var (runtime, claim) = await ObserveAndVerifyAsync(
            "object first = value; object second = \"\"; return first == second;", "x", "");
        Assert.That(runtime, Is.False);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
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

    private static async Task<(bool Runtime, WorkerClaimResult Claim)> ObserveAndVerifyAsync(string body, string value, string other)
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
        var compilation = TestCompilation.Create("EmptyStringIdentitySoundness", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("EmptyStringIdentitySoundness", isCollectible: true);
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
