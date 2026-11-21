using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// `[]` targeting List<T>, HashSet<T> or Dictionary<TKey, TValue> runs the
// parameterless constructor and adds nothing, so it is analyzed as `new T()`.
[TestFixture]
public sealed class EmptyCollectionExpressionTests
{
    private const string Types = "using System.Collections.Generic;\nusing SharpProof.Attributes;\n";

    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 1); List<int> xs = []; xs.Add(3); return xs.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 0); HashSet<int> xs = []; return xs.Count; }")]
    [TestCase("public static bool M() { Contract.Ensures(!Contract.Result<bool>()); Dictionary<int, int> xs = []; return xs.ContainsKey(1); }")]
    public async Task EmptyCollectionsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // Each `[]` agrees with the `new T()` it stands for, including where the
    // constructor's default comparer setup keeps a claim open.
    [TestCase("[EnforcePure] public static int M() { HashSet<string> xs = []; return xs.Count; }")]
    [TestCase("[EnforcePure] public static int M() { Dictionary<string, int> xs = []; return xs.Count; }")]
    [TestCase("[ZeroAllocations] public static int M() { List<int> xs = []; return xs.Count; }")]
    [TestCase("[DoesNotThrow] public static int M() { HashSet<int> xs = []; xs.Add(1); return xs.Count; }")]
    [TestCase("[EnforcePure] public static int M() { return new Holder().Count(); }")]
    [TestCase("[EnforcePure] public static int M() { List<int> xs = []; xs.Add(1); return xs.Count; }")]
    [TestCase("[DoesNotThrow] public static int M() { List<int> xs = []; return xs[0]; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 1); List<int> xs = []; return xs.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 0); return new Holder().Count(); }")]
    public async Task EmptyCollectionsMatchConstruction(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var collection = await ResultAsync(member);
        var construction = await ResultAsync(member.Replace("HashSet<string> xs = []", "HashSet<string> xs = new HashSet<string>()", StringComparison.Ordinal)
            .Replace("Dictionary<string, int> xs = []", "Dictionary<string, int> xs = new Dictionary<string, int>()", StringComparison.Ordinal)
            .Replace("List<int> xs = []", "List<int> xs = new List<int>()", StringComparison.Ordinal)
            .Replace("HashSet<int> xs = []", "HashSet<int> xs = new HashSet<int>()", StringComparison.Ordinal), constructed: true);
        Assert.That(collection.Outcome, Is.EqualTo(construction.Outcome), collection.Reason + " / " + construction.Reason);
        Assert.That(collection.Reason, Is.EqualTo(construction.Reason));
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member, bool constructed = false)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        // A class whose field initializers are empty collections.
        var holder = constructed
            ? "public sealed class Holder { private readonly List<int> _items = new List<int>(); private readonly Dictionary<int, int> _map = new Dictionary<int, int>(); public int Count() { return _items.Count + _map.Count; } }\n"
            : "public sealed class Holder { private readonly List<int> _items = []; private readonly Dictionary<int, int> _map = []; public int Count() { return _items.Count + _map.Count; } }\n";
        using var project = new ShadowTestProject(Types + holder + "public static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
