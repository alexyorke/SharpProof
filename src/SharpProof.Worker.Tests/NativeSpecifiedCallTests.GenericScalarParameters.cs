using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCaseSource(nameof(GenericArrayReturnCases))]
    public async Task GenericScalarParameterIdentityPreservesTheValue(string element, bool local, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static T Identity<T>(T value) => value;";
        await CheckInterfaceArrayContract(element + " value", "Contract.Ensures(Contract.Result<" + element + ">() " +
            (correct ? "==" : "!=") + " value); return Identity<" + element + ">(value); " + (local ? helper : ""),
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element, local ? "" : helper);
    }

    [TestCaseSource(nameof(GenericArrayReturnCases))]
    public async Task GenericScalarParameterAssignmentsUseCurrentValues(string element, bool local, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        const string helper = "static T Replace<T>(T first, T second) { first = second; return first; }";
        await CheckInterfaceArrayContract(element + " value, " + element + " replacement",
            "Contract.Ensures(Contract.Result<" + element + ">() == " + (correct ? "replacement" : "value") + "); " +
            "return Replace<" + element + ">(value, replacement); " + (local ? helper : ""),
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element, local ? "" : helper);
    }

    [Test]
    public async Task GenericScalarParametersRetainReadAndBoxingEffects()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int value",
            "return Identity<int>(value); static T Identity<T>(T input) => input;", true, true, true);
        await CheckInterfaceArrayEffects("[ZeroAllocations]", "int value",
            "return Box<int>(value); static object Box<T>(T input) => input;", null, false, null, "object");
    }
    [TestCase("System.Action change = () => first = second; change(); return first;")]
    [TestCase("Change(ref first, second); return first; static void Change(ref T value, T replacement) { value = replacement; }")]
    public async Task GenericScalarEscapingWritesCannotPreserveAStaleValue(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target(int value, int replacement) { " +
            "Contract.Requires(value != replacement); Contract.Ensures(Contract.Result<int>() == value); return Replace<int>(value, replacement); " +
            "static T Replace<T>(T first, T second) { " + body + " } } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }
}
