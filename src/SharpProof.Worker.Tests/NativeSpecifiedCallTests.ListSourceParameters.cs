using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase("var data = new System.Collections.Generic.List<int>() { 17, 23 }; return Read<int>(data, 0);", "static T Read<T>(System.Collections.Generic.IList<T> data, int index) => data[index];", 17)]
    [TestCase("var data = new System.Collections.Generic.List<int>() { 17, 23 }; Swap<int>(data, 0, 1); return data[0] * 100 + data[1];", "static void Swap<T>(System.Collections.Generic.IList<T> data, int first, int second) { if (data.Count < 2 || first == second) return; var item = data[first]; data[first] = data[second]; data[second] = item; }", 2317)]
    [TestCase("var data = new System.Collections.Generic.List<int>() { 17, 23 }; data.Swap(0, 1); return data[0] * 100 + data[1];", "public static void Swap<T>(this System.Collections.Generic.IList<T> data, int first, int second) { if (data.Count < 2 || first == second) return; var item = data[first]; data[first] = data[second]; data[second] = item; }", 2317)]
    [TestCase("var data = new System.Collections.Generic.List<int>(); return Size(data);", "static int Size(System.Collections.Generic.ICollection<int> data) => data.Count;", 0)]
    [TestCase("var data = new System.Collections.Generic.List<int>() { 17 }; return Append<int>(data, 23);", "static int Append<T>(System.Collections.Generic.ICollection<T> data, T value) { data.Add(value); return data.Count; }", 2)]
    [TestCase("var data = new System.Collections.Generic.List<int>() { 17 }; return Forward<int>(data);", "static T Forward<T>(System.Collections.Generic.IList<T> data) => Read<T>(data); static T Read<T>(System.Collections.Generic.IList<T> data) => data[0];", 17)]
    [TestCase("var data = new System.Collections.Generic.List<int>() { 17 }; return Read<int>(data);", "static T Read<T>(System.Collections.Generic.List<T> data) => data[0];", 17)]
    [TestCase("var data = new System.Collections.Generic.List<object>(); var item = new object(); Append<object>(data, item); return object.ReferenceEquals(data[0], item) ? data.Count : -1;", "static void Append<T>(System.Collections.Generic.ICollection<T> data, T item) { data.Add(item); }", 1)]
    public async Task ExactListSourceParametersShareCallerStorage(string body, string helper, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(helper);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body,
            WorkerClaimOutcome.Proven, members: helper);
    }

    [TestCase("object", "new object()", false)]
    [TestCase("string", "\"value\"", false)]
    [TestCase("int[]", "new int[1]", false)]
    [TestCase("object", "new object()", true)]
    [TestCase("string", "\"value\"", true)]
    [TestCase("int[]", "new int[1]", true)]
    public async Task ExactListGenericReferenceElementsKeepCallerIdentity(string element, string initializer, bool constrained)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(initializer);
        var body = "var item = " + initializer + "; var data = new System.Collections.Generic.List<" + element + ">() { item }; return object.ReferenceEquals(Read<" + element + ">(data), item) ? 1 : 0;";
        var helper = "static T Read<T>(System.Collections.Generic.IList<T> data) " + (constrained ? "where T : class " : "") + "=> data[0];";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " + body, WorkerClaimOutcome.Proven, members: helper);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); " + body, WorkerClaimOutcome.Unknown, members: helper);
    }

    [Test]
    public async Task ConcreteGenericReferenceHelperReadsTheExistingValue()
    {
        await CheckInterfaceArrayContract("System.Collections.Generic.List<object> data, object item",
            "Contract.Requires(data != null && data.Count == 1 && item != null && object.ReferenceEquals(data[0], item)); Contract.Ensures(Contract.Result<int>() == 1); return object.ReferenceEquals(Read<object>(data), item) ? 1 : 0;",
            WorkerClaimOutcome.Proven, members: "static T Read<T>(System.Collections.Generic.List<T> data) where T : class => data[0];");
    }

    [Test]
    public async Task ExactListSourceParametersRetainClassConstrainedCallerTypes()
    {
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target<T>(T item) where T : class { Contract.Ensures(Contract.Result<int>() == 1); var data = new System.Collections.Generic.List<T>() { item }; return object.ReferenceEquals(Read<T>(data), item) ? 1 : 0; } static T Read<T>(System.Collections.Generic.IList<T> data) => data[0]; }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), response.ClaimResults.Single().Reason.ToString());
    }

    [Test]
    public async Task ExactListSourceParameterContractsUseCallerEntryState()
    {
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { public static int Target() { Contract.Ensures(Contract.Result<int>() == 1731); var data = new System.Collections.Generic.List<int>() { 17 }; return ReadAndSet(data) * 100 + data[0]; } static int ReadAndSet(System.Collections.Generic.IList<int> data) { Contract.Requires(data != null && data.Count == 1); Contract.Ensures(Contract.Result<int>() == Contract.Old(data[0])); var before = data[0]; data[0] = 31; return before; } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        var declaration = response.Manifest.Claims.Single(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.CallableId.Contains(".Target", StringComparison.Ordinal));
        var target = response.ClaimResults.Single(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.ClaimId == declaration.ClaimId);
        Assert.That(target.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), target.Reason.ToString());
        var standalone = response.ClaimResults.Single(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.ClaimId != declaration.ClaimId);
        Assert.That(standalone.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TypedReferenceRebindingDoesNotPreserveListImplementation(bool helper)
    {
        const string replace = "__refvalue(__makeref(data), System.Collections.Generic.IList<int>) = other; return data.Count;";
        await CheckInterfaceArrayContract("System.Collections.Generic.IList<int> other",
            "Contract.Ensures(Contract.Result<int>() >= 0); System.Collections.Generic.IList<int> data = new System.Collections.Generic.List<int>(); " +
            (helper ? "return Replace(data, other);" : replace), WorkerClaimOutcome.Unknown,
            members: helper ? "static int Replace(System.Collections.Generic.IList<int> data, System.Collections.Generic.IList<int> other) { " + replace + " }" : "");
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
    public async Task ExactListSourceParametersKeepScalarLimits(string element, string low, string high)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(low);
        ArgumentNullException.ThrowIfNull(high);
        var body = "var data = new System.Collections.Generic.List<" + element + ">() { " + low + " }; return Store<" + element + ">(data, " + high + ") == " + high + " && data[0] == " + high + " ? 1 : 0;";
        const string helper = "static T Store<T>(System.Collections.Generic.IList<T> data, T item) { data[0] = item; return data[0]; }";
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " + body, WorkerClaimOutcome.Proven, members: helper);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); " + body, WorkerClaimOutcome.Unknown, members: helper);
    }

    [TestCase("System.Collections.Generic.IList<int> unknown", "var data = new System.Collections.Generic.List<int>() { 17 }; return Read<int>(unknown);", "static T Read<T>(System.Collections.Generic.IList<T> data) => data[0];")]
    [TestCase("System.Collections.Generic.IList<int> unknown", "var data = new System.Collections.Generic.List<int>() { 17 }; return Read<int>(data, unknown);", "static T Read<T>(System.Collections.Generic.IList<T> data, System.Collections.Generic.IList<T> other) { data = other; return data[0]; }")]
    [TestCase("System.Collections.Generic.List<int> incoming", "return Read<int>(incoming);", "static T Read<T>(System.Collections.Generic.IList<T> data) => data[0];")]
    [TestCase("System.Action callback", "var data = new System.Collections.Generic.List<int>() { 17 }; return Read<int>(data, callback);", "static T Read<T>(System.Collections.Generic.IList<T> data, System.Action callback) { callback(); return data[0]; }")]
    [TestCase("System.Collections.Generic.IList<int> unknown, bool choose", "var data = new System.Collections.Generic.List<int>() { 17 }; return Read<int>(data, unknown, choose);", "static T Read<T>(System.Collections.Generic.IList<T> data, System.Collections.Generic.IList<T> other, bool choose) { var alias = choose ? data : other; return alias[0]; }")]
    [TestCase("System.Collections.Generic.IList<int> unknown", "var data = new System.Collections.Generic.List<int>() { 17 }; return Read<int>(data, unknown);", "static T Read<T>(System.Collections.Generic.IList<T> data, System.Collections.Generic.IList<T> other) { System.Action change = () => data = other; change(); return data[0]; }")]
    [TestCase("", "var data = new System.Collections.Generic.List<int>() { 17 }; return Read<int>(data);", "static T Read<T>(System.Collections.Generic.IList<T> data) { Escape(ref data); return data[0]; } static void Escape<T>(ref System.Collections.Generic.IList<T> value) { }")]
    public async Task ListSourceParametersDoNotInventImplementationOrContents(string parameters, string body, string helper)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(helper);
        await CheckInterfaceArrayContract(parameters, "Contract.Ensures(Contract.Result<int>() == 17); " + body,
            WorkerClaimOutcome.Unknown, members: helper);
    }
}
