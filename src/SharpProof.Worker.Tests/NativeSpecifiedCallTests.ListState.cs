using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("var data = new System.Collections.Generic.List<int>(); return data.Count;", 0)]
    [TestCase("var data = new System.Collections.Generic.List<decimal>(); return data.Count;", 0)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(17); data.Add(23); return data.Count * 100 + data[0] + data[1];", 240)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); System.Collections.Generic.IList<int> alias = data; alias.Add(17); alias[0] = 23; return data.Count * 100 + data[0];", 123)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); System.Collections.Generic.ICollection<int> alias = data; alias.Add(17); return alias.Count;", 1)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(17); var alias = data; alias[0] = 23; return data[0];", 23)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(17); var other = new System.Collections.Generic.List<int>(); other.Add(29); return data[0] + other[0];", 46)]
    [TestCase("var data = new System.Collections.Generic.List<object>(); var item = new object(); data.Add(item); return object.ReferenceEquals(data[0], item) ? data.Count : -1;", 1)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(17); var alias = data; data[0] = (data = new System.Collections.Generic.List<int>()).Count; return alias[0];", 0)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(17); data.Add(29); int index = 0; data[index++] = (index = 0) + 31; return data[0] * 100 + data[1];", 3129)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); data.Add(17); data[0] = (data[0] = 23) + 1; return data[0];", 24)]
    [TestCase("var data = new System.Collections.Generic.List<int>() { 2, 3 }; return data.Count * 100 + data[0] + data[1];", 205)]
    public async Task FreshListStateProvesCountAndSharedContents(string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body, WorkerClaimOutcome.Proven);
    }

    [TestCase("System.Collections.Generic.IList<int> data", "return data.Count;", "Contract.Requires(data != null);", 0)]
    [TestCase("System.Action mutate", "var data = new System.Collections.Generic.List<int>(); data.Add(17); mutate(); return data[0];", "Contract.Requires(mutate != null);", 17)]
    [TestCase("System.Action<System.Collections.Generic.IList<int>> publish", "var data = new System.Collections.Generic.List<int>(); data.Add(17); System.Collections.Generic.IList<int> alias = data; publish(alias); return data[0];", "Contract.Requires(publish != null);", 17)]
    [TestCase("", "var data = new System.Collections.Generic.List<int>(); data.Add(17); data.Clear(); return data.Count;", "", 1)]
    public async Task UnprovenListStateDoesNotAcquireContentFacts(string parameters, string body, string requires, int expected)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(requires);
        await CheckInterfaceArrayContract(parameters, requires + "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body, WorkerClaimOutcome.Unknown);
    }
    [TestCase("sbyte", "sbyte.MinValue", "sbyte.MaxValue")]
    [TestCase("byte", "byte.MinValue", "byte.MaxValue")]
    [TestCase("short", "short.MinValue", "short.MaxValue")]
    [TestCase("ushort", "ushort.MinValue", "ushort.MaxValue")]
    [TestCase("char", "char.MinValue", "char.MaxValue")]
    [TestCase("int", "int.MinValue", "int.MaxValue")]
    [TestCase("uint", "uint.MinValue", "uint.MaxValue")]
    [TestCase("long", "long.MinValue", "long.MaxValue")]
    [TestCase("ulong", "ulong.MinValue", "ulong.MaxValue")]
    [TestCase("bool", "false", "true")]
    public async Task ListContentsKeepTheEntireScalarDomain(string element, string low, string high)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(high);
        var body = "var data = new System.Collections.Generic.List<" + element + ">(); data.Add(" + low + "); data.Add(" + high + "); " +
            "System.Collections.Generic.IList<" + element + "> alias = data; alias[0] = " + high + "; " +
            "return alias.Count == 2 && data[0] == " + high + " && alias[1] == " + high + " ? 1 : 0;";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " + body, WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); " + body, WorkerClaimOutcome.Unknown);
    }

    [TestCase(-1)]
    [TestCase(1)]
    [TestCase(int.MaxValue)]
    public async Task ListBoundsFaultsDoNotReturnAnOutOfRangeElement(int index)
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 71); " +
            "var data = new System.Collections.Generic.List<int>(); data.Add(17); " +
            "try { return data[" + index + "]; } catch (System.ArgumentOutOfRangeException) { return 71; }", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ListGrowthAndInitializationRetainEffectUncertainty()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "",
            "var data = new System.Collections.Generic.List<int>(); data.Add(17); return data.Count;", false, false, false);
    }

    [Test]
    public async Task AListSubclassDoesNotSupplyKnownInterfaceDispatch()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); " +
            "System.Collections.Generic.ICollection<int> data = new Strange(); return data.Count;", WorkerClaimOutcome.Unknown,
            members: "public sealed class Strange : System.Collections.Generic.List<int>, System.Collections.Generic.ICollection<int> { " +
                "int System.Collections.Generic.ICollection<int>.Count { get { return 23; } } }");
    }
    [TestCase("", "Contract.Ensures(Contract.Result<int>() == 0); var data = new System.Collections.Generic.List<T>(); return data.Count;")]
    [TestCase("where T : class", "Contract.Ensures(Contract.Result<int>() == 1); var data = new System.Collections.Generic.List<T>(); data.Add(item); System.Collections.Generic.IList<T> alias = data; return object.ReferenceEquals(alias[0], item) && data.Count == 1 ? 1 : 0;")]
    public async Task GenericListStateUsesTheDeclarationValueDomain(string constraints, string body)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(body);
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target<T>(T item) " + constraints + " { " + body + " } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), response.ClaimResults.Single().Reason.ToString());
    }
}
