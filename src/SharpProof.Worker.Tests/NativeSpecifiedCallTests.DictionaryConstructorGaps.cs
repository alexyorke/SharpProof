using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(0); return data.Count;", 0)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, int>(17); var alias = data; alias.Add(17, 23); data[17] = 29; return alias.Count * 100 + data[17];", 129)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<int, decimal>(3); return data.Count + (data.ContainsKey(17) ? 1 : 0);", 0)]
    [TestCase("var data = new System.Collections.Generic.Dictionary<string, int>(3); return data.Count;", 0)]
    [TestCase("var previous = new System.Collections.Generic.Dictionary<int, int>(); previous[17] = 0; var data = new System.Collections.Generic.Dictionary<int, int>(capacity: previous[17] = 3); return previous[17] + data.Count;", 3)]
    public async Task DictionaryCapacityConstructionPreservesEmptyStateAndOrderedArguments(string body, int result)
    {
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + result + "); " + body, WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == " + (result + 1) + "); " + body, WorkerClaimOutcome.Unknown);
    }

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public async Task DictionaryCapacityConstructionCannotReturnNormallyForNegativeCapacity(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => { _ = new Dictionary<int, int>(capacity); }));
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); " +
            "var data = new System.Collections.Generic.Dictionary<int, int>(" + capacity + "); return 1;", WorkerClaimOutcome.Proven);
    }
    [Test]
    public async Task DictionaryCapacityConstructionRetainsUncertainEffects()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int capacity",
            "var data = new System.Collections.Generic.Dictionary<int, int>(capacity); return data.Count;", false, false, false);
    }
    [TestCase("", "return data.Count;", 0, WorkerClaimOutcome.Proven)]
    [TestCase("where T : System.IComparable<T>", "return data.Count;", 0, WorkerClaimOutcome.Proven)]
    [TestCase("where T : System.IComparable<T>", "return data.ContainsKey(17) ? 1 : 0;", 0, WorkerClaimOutcome.Proven)]
    [TestCase("where T : class", "return data.Count;", 0, WorkerClaimOutcome.Proven)]
    [TestCase("", "data = new System.Collections.Generic.Dictionary<int, T>(17); return data.Count;", 0, WorkerClaimOutcome.Proven)]
    [TestCase("where T : struct", "data = new System.Collections.Generic.Dictionary<int, T>(0); return data.ContainsKey(17) ? 1 : 0;", 0, WorkerClaimOutcome.Proven)]
    [TestCase("", "return data.Count;", 1, WorkerClaimOutcome.Refuted)]
    public async Task DictionaryConstructorEmptyStateDoesNotRequireValueSemantics(string constraints, string body, int result, WorkerClaimOutcome expected)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(body);
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { " +
            "public static int Target<T>() " + constraints + " { Contract.Ensures(Contract.Result<int>() == " + result + "); " +
            "var data = new System.Collections.Generic.Dictionary<int, T>(); " + body + " } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expected), response.ClaimResults.Single().Reason.ToString());
    }

    [Test]
    public async Task DictionaryConstructorOpaqueInitializerCannotRetainEmptyState()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); " +
            "var data = new System.Collections.Generic.Dictionary<int, decimal> { { 17, 23m } }; return data.Count;", WorkerClaimOutcome.Unknown);
    }

    [TestCase(1, WorkerClaimOutcome.Proven)]
    [TestCase(0, WorkerClaimOutcome.Refuted)]
    public async Task DictionaryOpaqueValueInsertionUpdatesCountWithoutInterpretingTheValue(int result, WorkerClaimOutcome expected)
    {
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { " +
            "public static int Target<T>(T value) where T : System.IComparable<T> { Contract.Ensures(Contract.Result<int>() == " + result + "); " +
            "var data = new System.Collections.Generic.Dictionary<int, T>(); data.Add(17, value); return data.Count; } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
        Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.None));
        if (expected == WorkerClaimOutcome.Refuted)
        { Assert.That(claim.Model, Is.Not.Empty); }
    }

    [Test]
    public void DictionaryConstructorDoesNotInvokeValueConstructor()
    {
        const string source = "public struct Value { public static int Calls; public Value() { Calls++; throw new System.InvalidOperationException(); } } " +
            "public static class C { public static int Target() { var data = new System.Collections.Generic.Dictionary<int, Value>(); " +
            "return data.Count + (data.ContainsKey(17) ? 100 : 0) + Value.Calls; } }";
        var compilation = CSharpCompilation.Create("DictionaryOpaqueConstructorOracle", [CSharpSyntaxTree.ParseText(source)], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(";", emitted.Diagnostics));
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("DictionaryOpaqueConstructorOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            var actual = runtime.LoadFromStream(image).GetType("C")!.GetMethod("Target")!.Invoke(null, null);
            Assert.That(actual, Is.EqualTo(0));
        }
        finally { runtime.Unload(); }
    }
}
