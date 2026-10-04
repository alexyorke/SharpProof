using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// `string == string` compares content; reference identity stays separate.
[TestFixture]
public sealed class WorkerVcStringEqualityTests
{
    private const string Source =
        """
        #nullable enable
        using SharpProof.Attributes;
        public static class Subject {
            public static string? Identity(string? value) {
                Contract.Ensures(Contract.Result<string?>() == value);
                return value;
            }
            public static string? NotTheLiteral(string? value) {
                Contract.Ensures(Contract.Result<string?>() == "abc");
                return value;
            }
            public static string LengthFromContent(string value) {
                Contract.Requires(value == "abc");
                Contract.Ensures(Contract.Result<string>().Length == 3);
                return value;
            }
            public static bool DistinctLiterals(string value) {
                Contract.Requires(value == "abc");
                Contract.Ensures(Contract.Result<bool>());
                return value != "abd";
            }
            public static string ContentIsNotIdentity(string value) {
                Contract.Requires(value == "abc");
                Contract.Ensures((object)Contract.Result<string>() == (object)"abc");
                return value;
            }
            public static bool AppendEmpty(string? value) {
                Contract.Ensures(Contract.Result<bool>());
                return value + "" == value;
            }
            public static bool Prefixed(string name) {
                Contract.Requires(name != null);
                Contract.Ensures(Contract.Result<bool>());
                return "hi " + name != "hi";
            }
            public static bool PrefixedLength(string name) {
                Contract.Requires(name == "ab");
                Contract.Ensures(Contract.Result<bool>());
                return "hi " + name == "hi ab";
            }
        }
        """;

    private static WorkerVerifyResponse? s_response;

    [OneTimeSetUp]
    public async Task Verify()
    {
        using var project = WorkerTests.TestProject.Create(Source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        s_response = await worker.VerifyAsync(request);
        Assert.That(s_response.Errors, Is.Empty);
    }

    [TestCase("Identity", WorkerClaimOutcome.Proven)]
    [TestCase("NotTheLiteral", WorkerClaimOutcome.Refuted)]
    [TestCase("LengthFromContent", WorkerClaimOutcome.Proven)]
    [TestCase("DistinctLiterals", WorkerClaimOutcome.Proven)]
    [TestCase("AppendEmpty", WorkerClaimOutcome.Refuted)]
    [TestCase("Prefixed", WorkerClaimOutcome.Proven)]
    [TestCase("PrefixedLength", WorkerClaimOutcome.Proven)]
    public void ContentEqualityIsDecided(string method, WorkerClaimOutcome expected)
    {
        var claim = Claim(method);
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    // A string equal in content to a literal need not be the literal's object.
    [Test]
    public void ContentEqualityDoesNotProveReferenceIdentity()
    {
        Assert.That(Claim("ContentIsNotIdentity").Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    private static WorkerClaimResult Claim(string method)
    {
        var response = s_response!;
        var callable = response.Manifest.Callables.Single(callable =>
            callable.CallableId.Contains("." + method + "(", StringComparison.Ordinal));
        return response.ClaimResults.Single(result => callable.ClaimIds.Contains(result.ClaimId));
    }
}
