using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase("int?", "int[]", false)]
    [TestCase("int?", "int[]", true)]
    [TestCase("int", "int", false)]
    [TestCase("int", "int", true)]
    [TestCase("long", "bool", false)]
    [TestCase("long", "bool", true)]
    [TestCase("string", "object", false)]
    [TestCase("string", "object", true)]
    [TestCase("System.DateTime", "System.Guid", false)]
    [TestCase("System.DateTime", "System.Guid", true)]
    [TestCase("System.Collections.Generic.List<int>", "int[]", false)]
    [TestCase("System.Collections.Generic.List<int>", "int[]", true)]
    public async Task KeyValuePairGettersCopyTheirValueWithoutExecutingCallbacks(string key, string value, bool readValue)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]",
            "System.Collections.Generic.KeyValuePair<" + key + ", " + value + "> pair",
            "return pair." + (readValue ? "Value" : "Key") + ";", true, true, true, result: readValue ? value : key);
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task KeyValuePairGetterPreservesReceiverEvaluationFaults(bool readValue)
    {
        var preparation = Prepare("[DoesNotThrow] public static int Target(System.Collections.Generic.KeyValuePair<int, int>[] pairs, int index) { return pairs[index]." + (readValue ? "Value" : "Key") + "; }");
        var verified = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(verified.Outcome, Is.TypeOf<RefutedOutcome>(), verified.Reason.ToString());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task KeyValuePairGetterPreservesAmbientArrayMutation(bool readValue)
    {
        var preparation = Prepare("[EnforcePure] public static int Target(System.Collections.Generic.KeyValuePair<int, int> pair, int[] data) { " +
            "SharpProof.Attributes.Contract.Requires(data != null && data.Length > 0); data[0] = 7; return pair." + (readValue ? "Value" : "Key") + "; }");
        var verified = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(verified.Outcome, Is.TypeOf<RefutedOutcome>(), verified.Reason.ToString());
    }
}
