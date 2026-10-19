using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase(17, WorkerClaimOutcome.Proven)]
    [TestCase(99, WorkerClaimOutcome.Refuted)]
    public async Task DictionaryCallerProofDoesNotVerifyOrImportHelperEnsures(int expected, WorkerClaimOutcome outcome)
    {
        // The helper's deliberately false Ensures must neither be trusted by
        // the caller nor be mistaken for a specialized postcondition proof.
        await CheckDictionaryContractTarget("using SharpProof.Attributes; public static class C { public static int Target() { " +
            "Contract.Ensures(Contract.Result<int>() == " + expected + "); var data = new System.Collections.Generic.Dictionary<int, int>(); " +
            "data.Add(1, 17); return Read(data); } " +
            "static int Read(System.Collections.Generic.Dictionary<int, int> data) { Contract.Requires(data != null && data.ContainsKey(1)); " +
            "Contract.Ensures(Contract.Result<int>() == 99); return data[1]; } }", outcome);
    }
}
