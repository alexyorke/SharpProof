using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("decimal")]
    [TestCase("double")]
    [TestCase("float")]
    [TestCase("System.Guid")]
    [TestCase("System.DayOfWeek")]
    [TestCase("System.DateTime")]
    [TestCase("int?")]
    public async Task OpaqueListValuesRetainCountThroughSharedStorage(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var body = "var data = new System.Collections.Generic.List<" + element + ">(); data.Add(first); data.Add(second); Swap<" + element + ">(data); return data.Count;";
        const string helper = "static void Swap<T>(System.Collections.Generic.IList<T> data) { var item = data[0]; data[0] = data[1]; data[1] = item; }";
        await CheckInterfaceArrayContract(element + " first, " + element + " second", "Contract.Ensures(Contract.Result<int>() == 2); " + body,
            WorkerClaimOutcome.Proven, members: helper);
        await CheckInterfaceArrayContract(element + " first, " + element + " second", "Contract.Ensures(Contract.Result<int>() == 3); " + body,
            WorkerClaimOutcome.Unknown, members: helper);
    }

    [TestCase("")]
    [TestCase("where T : struct")]
    [TestCase("where T : class")]
    [TestCase("where T : System.IComparable<T>")]
    public async Task OpenGenericListValuesShareSourceHelperRepresentation(string constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target<T>(T first, T second) " + constraint + " { Contract.Ensures(Contract.Result<int>() == 2); var data = new System.Collections.Generic.List<T>() { first, second }; Swap<T>(data); return data.Count; } static void Swap<U>(System.Collections.Generic.IList<U> data) { var before = Read<U>(data, 0); data[0] = Read<U>(data, 1); data[1] = before; } static U Read<U>(System.Collections.Generic.IList<U> data, int index) => data[index]; }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), response.ClaimResults.Single().Reason.ToString());
    }

    [TestCase("decimal first, decimal second", "var data = new System.Collections.Generic.List<decimal>() { first, second }; return data[0] == data[1] ? 1 : 0;")]
    [TestCase("Cell cell", "var data = new System.Collections.Generic.List<Cell>() { cell }; return data[0].Value;")]
    public async Task OpaqueListStorageDoesNotInterpretOperatorsOrStructFields(string parameters, string body)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract(parameters, "Contract.Ensures(Contract.Result<int>() == 17); " + body,
            WorkerClaimOutcome.Unknown, members: "public struct Cell { public int Value; }");
    }
    [TestCase("decimal")]
    [TestCase("double")]
    [TestCase("System.Guid")]
    [TestCase("int?")]
    public async Task OpaqueListReadsRetainNoThrowAllocationAndPurityProofs(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "System.Collections.Generic.List<" + element + "> data",
            "Contract.Requires(data != null && data.Count > 0); return data[0];", noThrow: true, noAllocation: true, pure: true, result: element);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "System.Collections.Generic.List<" + element + "> data, " + element + " item",
            "Contract.Requires(data != null); data.Add(item); return data.Count;", noThrow: false, noAllocation: false, pure: false);
    }

    [TestCase("IList")]
    [TestCase("ICollection")]
    public async Task OpaqueListSourceHelpersDoNotInventAnInterfaceImplementation(string collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target<T>(System.Collections.Generic." + collection + "<T> data) { Contract.Requires(data != null); Contract.Ensures(Contract.Result<int>() >= 0); return Size<T>(data); } static int Size<U>(System.Collections.Generic." + collection + "<U> data) => data.Count; }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }
    [Test]
    public async Task OpaqueICollectionAddSharesTheConcreteListState()
    {
        await CheckInterfaceArrayContract("decimal first, decimal second",
            "Contract.Ensures(Contract.Result<int>() == 2); var data = new System.Collections.Generic.List<decimal>(); System.Collections.Generic.ICollection<decimal> alias = data; alias.Add(first); data.Add(second); return alias.Count;",
            WorkerClaimOutcome.Proven);
    }
    [TestCase("decimal")]
    [TestCase("double")]
    [TestCase("System.Guid")]
    [TestCase("int?")]
    public async Task BoxingAnOpaqueListElementDoesNotProveZeroAllocations(string element)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayEffects("[ZeroAllocations]", "System.Collections.Generic.List<" + element + "> data",
            "Contract.Requires(data != null && data.Count > 0); return data[0];", noThrow: null, noAllocation: false, pure: null, result: "object");
    }
}
