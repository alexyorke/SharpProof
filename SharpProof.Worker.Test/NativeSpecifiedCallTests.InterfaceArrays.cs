using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("ICollection")]
    [TestCase("IList")]
    public async Task ArrayBackedCollectionCountProvesLengthAndEffectClaims(string collection)
    {
        var expression = "((System.Collections.Generic." + collection + "<int>)values).Count";
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null); Contract.Ensures(Contract.Result<int>() == values.Length); return " + expression + ";",
            WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] values",
            "Contract.Requires(values != null); return " + expression + ";", true, true, true);
    }

    [TestCase("int", "int")]
    [TestCase("long", "long")]
    [TestCase("bool", "bool")]
    [TestCase("char", "char")]
    [TestCase("uint", "uint")]
    public async Task ArrayBackedListReadsProveContentsAndEffects(string element, string result)
    {
        var body = "Contract.Requires(values != null && index >= 0 && index < values.Length); " +
            "return ((System.Collections.Generic.IList<" + element + ">)values)[index];";
        await CheckInterfaceArrayContract(element + "[] values, int index", body.Replace("return ",
            "Contract.Ensures(Contract.Result<" + result + ">() == values[index]); return ", StringComparison.Ordinal),
            WorkerClaimOutcome.Proven, result);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]",
            element + "[] values, int index", body, true, true, true, result);
    }

    [TestCase("int")]
    [TestCase("long")]
    [TestCase("bool")]
    [TestCase("char")]
    [TestCase("uint")]
    public async Task ArrayBackedListStoreProvesStoredValueAndRetainsMutationEffects(string element)
    {
        var store = "((System.Collections.Generic.IList<" + element + ">)values)[index] = replacement;";
        var requires = "Contract.Requires(values != null && index >= 0 && index < values.Length); ";
        var parameters = element + "[] values, int index, " + element + " replacement";
        await CheckInterfaceArrayContract(parameters,
            requires + "Contract.Ensures(Contract.Result<" + element + ">() == replacement); " + store + " return values[index];",
            WorkerClaimOutcome.Proven, element);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", parameters,
            requires + store + " return values[index];", true, true, false, element);
    }

    [Test]
    public async Task ArrayBackedListReadAndStoreKeepEvaluationOrder()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length > 0); " +
            "Contract.Ensures(Contract.Result<int>() == unchecked(values[0] + 100)); " +
            "int index = 0; return unchecked(((System.Collections.Generic.IList<int>)values)[index++] + index * 100);",
            WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length > 0); Contract.Ensures(Contract.Result<int>() == 101); " +
            "int index = 0; ((System.Collections.Generic.IList<int>)values)[index++] = index; return values[0] * 100 + index;",
            WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task InterfaceArrayStoresDoNotRetroactivelyChangeEarlierReads()
    {
        await CheckInterfaceArrayContract("int[] values, int[] alias, int replacement",
            "Contract.Requires(values != null && values.Length > 0 && alias == values); " +
            "Contract.Ensures(Contract.Result<int>() == Contract.Old(values[0])); " +
            "int before = ((System.Collections.Generic.IList<int>)values)[0]; " +
            "((System.Collections.Generic.IList<int>)alias)[0] = replacement; return before;", WorkerClaimOutcome.Proven);
    }

    [TestCase("((System.Collections.Generic.ICollection<int>)values).Count")]
    [TestCase("((System.Collections.Generic.IList<int>)values)[-1]")]
    [TestCase("((System.Collections.Generic.IList<int>)values)[int.MaxValue]")]
    public async Task ArrayBackedInterfaceNullAndBoundsFaultsAreNotErased(string expression)
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow]", "int[] values", "return " + expression + ";", false, null, null);
    }

    [Test]
    public async Task ArrayBackedInterfaceRejectsAnIncorrectResultClaim()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null); Contract.Ensures(Contract.Result<int>() == -1); " +
            "return ((System.Collections.Generic.ICollection<int>)values).Count;", WorkerClaimOutcome.Refuted);
    }

    [TestCase("System.Collections.Generic.ICollection<int>", "values.Count")]
    [TestCase("System.Collections.Generic.IList<int>", "values[0]")]
    [TestCase("string[]", "((System.Collections.Generic.IList<object>)values)[0] == null ? 0 : 1")]
    public async Task UnknownDispatchAndCovariantArraysRemainConservative(string type, string expression)
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow]", type + " values",
            "Contract.Requires(values != null); return " + expression + ";", false, null, null);
    }


    [Test]
    public async Task InterfaceArrayStoreEvaluatesItsValueBeforeTheNullCheck()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values == null); Contract.Ensures(Contract.Result<int>() == 1); int evaluated = 0; " +
            "try { ((System.Collections.Generic.IList<int>)values)[0] = ++evaluated; } " +
            "catch (System.NullReferenceException) { return evaluated; } return evaluated;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task InterfaceArrayStoreUsesTheReceiverCapturedBeforeItsValue()
    {
        await CheckInterfaceArrayContract("int[] values, int[] other",
            "Contract.Requires(values != null && values.Length > 0 && other != null && other.Length > 0); " +
            "Contract.Ensures(Contract.Result<int>() == other[0]); var original = values; " +
            "((System.Collections.Generic.IList<int>)values)[0] = (values = other)[0]; return original[0];", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task InterfaceArrayReadRemainsUnknownAcrossAnOpaqueMutation()
    {
        await CheckInterfaceArrayContract("int[] values, System.Action<int[]> mutate",
            "Contract.Requires(values != null && values.Length > 0 && mutate != null); " +
            "Contract.Ensures(Contract.Result<int>() == values[0]); int before = ((System.Collections.Generic.IList<int>)values)[0]; " +
            "mutate(values); return before;", WorkerClaimOutcome.Unknown);
    }


    [Test]
    public async Task ArrayBackedInterfaceGettersWorkInContractClauses()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null); " +
            "Contract.Ensures(Contract.Result<int>() == ((System.Collections.Generic.ICollection<int>)values).Count); " +
            "return values.Length;", WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("int[] values, int index",
            "Contract.Requires(values != null && index >= 0 && index < values.Length); " +
            "Contract.Ensures(Contract.Result<int>() == ((System.Collections.Generic.IList<int>)values)[index]); " +
            "return values[index];", WorkerClaimOutcome.Proven);
    }


    [TestCase(true)]
    [TestCase(false)]
    public async Task InterfaceArrayFailureResourceEffectsRequireAnInvalidIndex(bool valid)
    {
        var condition = valid ? "index >= 0 && index < values.Length" : "index < 0";
        var preparation = Prepare("[ZeroAllocations, EnforcePure, AllowedCapabilities(SharpProofCapability.None)] " +
            "public static int Target(int[] values, int index) { Contract.Requires(values != null && " + condition + "); " +
            "return ((System.Collections.Generic.IList<int>)values)[index]; }");
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var capabilities = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        if (!valid)
        { Assert.That(purity.Outcome, Is.Not.TypeOf<RefutedOutcome>(), "A possible cache write must not fabricate a definite impurity."); }
        foreach (var result in new[] { allocations, purity, capabilities })
        { Assert.That(result.Outcome is ProvenOutcome, Is.EqualTo(valid), result.Reason.ToString()); }
    }

    [Test]
    public async Task KnownNonWritingCallsPreserveArrayContents()
    {
        await CheckInterfaceArrayContract("int[] values, string text",
            "Contract.Requires(values != null && values.Length > 0); Contract.Ensures(Contract.Result<int>() == 0); " +
            "int before = values[0]; _ = string.IsNullOrEmpty(text); return unchecked(values[0] - before);", WorkerClaimOutcome.Proven);
    }


    [Test]
    public async Task InterfaceArrayBoundsFailureDoesNotNarrowUnknownAmbientCapabilities()
    {
        var preparation = Prepare("[AllowedCapabilities(SharpProofCapability.IO | SharpProofCapability.Randomness | " +
            "SharpProofCapability.Reflection | SharpProofCapability.Synchronization | SharpProofCapability.NativeInterop)] " +
            "public static int Target(int[] values) { Contract.Requires(values != null); " +
            "return ((System.Collections.Generic.IList<int>)values)[-1]; }");
        var result = await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
    }

    private static async Task CheckInterfaceArrayContract(string parameters, string body, WorkerClaimOutcome expected, string result = "int")
    {
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static " +
            result + " Target(" + parameters + ") { " + body + " } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors.Select(error => error.Code + ": " + error.Message), Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expected), response.ClaimResults.Single().ToString());
    }

    private static async Task CheckInterfaceArrayEffects(string attributes, string parameters, string body,
        bool? noThrow, bool? noAllocation, bool? pure, string result = "int")
    {
        var preparation = Prepare(attributes + " public static " + result + " Target(" + parameters + ") { " + body + " }");
        if (noThrow.HasValue)
        {
            var verified = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(verified.Outcome is ProvenOutcome, Is.EqualTo(noThrow.Value), verified.Reason.ToString());
        }
        if (noAllocation.HasValue)
        {
            var verified = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(verified.Outcome is ProvenOutcome, Is.EqualTo(noAllocation.Value), verified.Reason.ToString());
        }
        if (pure.HasValue)
        {
            var verified = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(verified.Outcome is ProvenOutcome, Is.EqualTo(pure.Value), verified.Reason.ToString());
        }
    }
}
