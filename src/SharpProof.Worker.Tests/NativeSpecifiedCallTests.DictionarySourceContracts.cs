using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("int", "17", "31", "Contract.Result<int>() == Contract.Old(data[key])", "ReadAndSet(data, 1, 31) * 100 + data[1]", 1731)]
    [TestCase("object", "first", "second", "object.ReferenceEquals(Contract.Result<T>(), Contract.Old(data[key]))", "object.ReferenceEquals(ReadAndSet(data, 1, second), first) && object.ReferenceEquals(data[1], second) ? 1 : 0", 1)]
    public async Task DictionarySourceContractsReadCallerEntryState(string valueType, string first, string second,
        string postcondition, string result, int expected)
    {
        ArgumentNullException.ThrowIfNull(valueType);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(postcondition);
        ArgumentNullException.ThrowIfNull(result);
        var helperType = valueType == "int" ? "int" : "T";
        var helperName = valueType == "int" ? "ReadAndSet" : "ReadAndSet<T>";
        var source = "using SharpProof.Attributes; public static class C { public static int Target() { Contract.Ensures(Contract.Result<int>() == " + expected + "); " +
            "var first = new object(); var second = new object(); var data = new System.Collections.Generic.Dictionary<int, " + valueType + ">(); data.Add(1, " + first + "); return " + result + "; } " +
            "static " + helperType + " " + helperName + "(System.Collections.Generic.Dictionary<int, " + helperType + "> data, int key, " + helperType + " item) { " +
            "Contract.Requires(data != null && data.Count == 1 && data.ContainsKey(key)); Contract.Ensures(" + postcondition + "); " +
            "var before = data[key]; data[key] = item; return before; } }";
        await CheckDictionaryContractTarget(source, WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task DictionaryCountContractSharesCallerStorageAfterSourceMutation()
    {
        await CheckDictionaryContractTarget("using SharpProof.Attributes; public static class C { public static int Target() { " +
            "Contract.Ensures(Contract.Result<int>() == 2); var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(1, 17); return Append(data); } " +
            "static int Append(System.Collections.Generic.Dictionary<int, int> data) { Contract.Requires(data != null && data.Count == 1); " +
            "Contract.Ensures(Contract.Result<int>() == Contract.Old(data.Count) + 1); data.Add(2, 31); return data.Count; } }", WorkerClaimOutcome.Proven);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DictionaryContractPreconditionsDoNotRestoreAnUnknownComparer(bool writerInsideHelper)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int Target(Mutator change) { Contract.Requires(change != null); " +
            "Contract.Ensures(Contract.Result<int>() == 23); var data = new System.Collections.Generic.Dictionary<int, int>(); data.Add(1, 17); " +
            (writerInsideHelper ? "" : "change.Change(data);") + " return ReadAndSet(data, change); } " +
            "static int ReadAndSet(System.Collections.Generic.Dictionary<int, int> data, Mutator change) { " +
            "Contract.Requires(data != null && data.ContainsKey(1)); Contract.Ensures(Contract.Result<int>() == 23); " +
            (writerInsideHelper ? "change.Change(data);" : "") + " data[1] = 23; data[3] = 99; return data[1]; } " +
            "public abstract class Mutator { public abstract void Change(System.Collections.Generic.Dictionary<int, int> data); } }";
        await CheckDictionaryContractTarget(source, WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task DictionarySourceContractsDoNotInventCustomComparerSemantics()
    {
        await CheckDictionaryContractTarget("using SharpProof.Attributes; public static class C { public static int Target(System.Collections.Generic.IEqualityComparer<int> comparer) { " +
            "Contract.Ensures(Contract.Result<int>() == 23); var data = new System.Collections.Generic.Dictionary<int, int>(comparer); return ReadAndSet(data); } " +
            "static int ReadAndSet(System.Collections.Generic.Dictionary<int, int> data) { Contract.Requires(data != null && data.Count == 0); " +
            "Contract.Ensures(Contract.Result<int>() == 23); data[1] = 23; data[3] = 99; return data[1]; } }", WorkerClaimOutcome.Unknown);
    }

    private static async Task CheckDictionaryContractTarget(string source, WorkerClaimOutcome expected)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var project = WorkerTests.TestProject.Create(source);
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        var declaration = response.Manifest.Claims.Single(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.CallableId.Contains(".Target", StringComparison.Ordinal));
        var target = response.ClaimResults.Single(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.ClaimId == declaration.ClaimId);
        Assert.That(target.Outcome, Is.EqualTo(expected), target.Reason.ToString());
        Assert.That(response.ClaimResults.Where(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.ClaimId != declaration.ClaimId)
            .All(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.Outcome == WorkerClaimOutcome.Unknown), Is.True);
    }
}
