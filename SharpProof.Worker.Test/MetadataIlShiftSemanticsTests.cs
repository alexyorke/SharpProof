using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class MetadataIlShiftSemanticsTests
{
    [Test]
    public async Task LogicalRightShiftFromMetadataProvesMatchingClaim()
    {
        using var subject = new MetadataTestSubject(
            "public static class Library { public static int Target(int value) => value >>> 1; }",
            """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == (value >>> 1));
                    return Library.Target(value);
                }
            }
            """);
        Assert.That(subject.Invoke(-1), Is.EqualTo(int.MaxValue));
        Assert.That(subject.InvokeRoot(-1), Is.EqualTo(int.MaxValue));

        using var project = new ShadowTestProject(subject.CreateArtifact(), cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        TestContext.WriteLine($"Metadata shift claim: {claim.Outcome}, {claim.Reason}");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [TestCase("int", "value << 3", -1, -8)]
    [TestCase("int", "value << value", 33, 66)]
    [TestCase("long", "value << -1", 1L, long.MinValue)]
    [TestCase("int", "value >> 1", -3, -2)]
    [TestCase("long", "value >> 65", long.MinValue, -4611686018427387904L)]
    [TestCase("uint", "value >> 1", uint.MaxValue, 2147483647U)]
    [TestCase("ulong", "value >> 65", ulong.MaxValue, 9223372036854775807UL)]
    public async Task NearbyMetadataShiftsProveMatchingClaims(string resultType, string expression, object input, object expected)
    {
        var library = $"public static class Library {{ public static {resultType} Target({resultType} value) => {expression}; }}";
        var source = $$"""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static {{resultType}} Target({{resultType}} value) {
                    Contract.Ensures(Contract.Result<{{resultType}}>() == ({{expression}}));
                    return Library.Target(value);
                }
            }
            """;
        using var subject = new MetadataTestSubject(library, source);
        Assert.That(subject.Invoke(input), Is.EqualTo(expected));
        Assert.That(subject.InvokeRoot(input), Is.EqualTo(expected));
        using var project = new ShadowTestProject(subject.CreateArtifact(), cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task FalseMetadataShiftClaimIsRefuted()
    {
        using var subject = new MetadataTestSubject(
            "public static class Library { public static int Target(int value) => value >>> 1; }",
            """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return Library.Target(value);
                }
            }
            """);
        Assert.That(subject.InvokeRoot(-1), Is.Not.EqualTo(-1));
        using var project = new ShadowTestProject(subject.CreateArtifact(), cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

}
