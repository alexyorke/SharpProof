using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Reference-typed instance fields (objects, strings and arrays) are modeled
// like scalar ones, and a counterexample's objects carry their decoded entry
// values, referenced objects included.
[TestFixture]
public sealed class WorkerVcReferenceFieldTests
{
    private const string Source =
        """
        using SharpProof.Attributes;
        public sealed class Node {
            public int Value;
            public Node Next;
            public string Name;
            public int[] Items;
        }
        public static class Subject {
            public static int NextValue([NotNull] Node node) {
                Contract.Requires(node.Next != null && node.Next.Value > 0);
                Contract.Ensures(Contract.Result<int>() > 0);
                return node.Next.Value;
            }
            public static void Link([NotNull] Node left, [NotNull] Node right) {
                Contract.Ensures(left.Next == right);
                left.Next = right;
            }
            public static int ThroughLink([NotNull] Node left, [NotNull] Node right) {
                Contract.Ensures(Contract.Result<int>() == 5);
                left.Next = right;
                right.Value = 5;
                return left.Next.Value;
            }
            public static Node StoreNull([NotNull] Node node) {
                Contract.Ensures(Contract.Result<Node>() != null);
                node.Next = null;
                return node.Next;
            }
            public static void Rename([NotNull] Node node) {
                Contract.Ensures(node.Name == "x");
                node.Name = "x";
            }
            public static int ArrayField([NotNull] Node node) {
                Contract.Requires(node.Items != null && node.Items.Length > 0);
                Contract.Ensures(Contract.Result<int>() == 3);
                node.Items[0] = 3;
                return node.Items[0];
            }
            public static int UnknownNext([NotNull] Node node) {
                Contract.Requires(node.Next != null);
                Contract.Ensures(Contract.Result<int>() == 0);
                return node.Next.Value;
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
        Assert.That(s_response.Errors, Is.Empty, string.Join("; ", s_response.Errors.Select(error => error.Code + ": " + error.Message)));
    }

    [TestCase("NextValue", WorkerClaimOutcome.Proven)]
    [TestCase("Link", WorkerClaimOutcome.Proven)]
    [TestCase("ThroughLink", WorkerClaimOutcome.Proven)]
    [TestCase("StoreNull", WorkerClaimOutcome.Refuted)]
    [TestCase("Rename", WorkerClaimOutcome.Proven)]
    [TestCase("ArrayField", WorkerClaimOutcome.Proven)]
    [TestCase("UnknownNext", WorkerClaimOutcome.Refuted)]
    public void ReferenceFieldsAreModeled(string method, WorkerClaimOutcome expected)
    {
        var claim = Claim(method);
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    private static WorkerClaimResult Claim(string method)
    {
        var response = s_response!;
        var callable = response.Manifest.Callables.Single(callable =>
            callable.CallableId.Contains("." + method + "(", StringComparison.Ordinal));
        return response.ClaimResults.Single(result => callable.ClaimIds.Contains(result.ClaimId));
    }
}
