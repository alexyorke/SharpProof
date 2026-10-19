using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeSpecifiedCallTests
{
    [TestCase("")]
    [TestCase("where T : System.IComparable<T>")]
    [TestCase("where T : class")]
    public async Task GenericKeyDictionaryEmptyCountIsZero(string constraints)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        using var project = WorkerTests.TestProject.Create("using SharpProof.Attributes; public static class C { " +
            "public static int Target<T>() " + constraints + " { Contract.Ensures(Contract.Result<int>() == 0); " +
            "var data = new System.Collections.Generic.Dictionary<T, int>(); return data.Count; } }");
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), response.ClaimResults.Single().Reason.ToString());
    }

    [Test]
    public async Task GenericKeyDictionaryInitializationDoesNotPreserveUnrelatedHeapFacts()
    {
        await CheckInterfaceArrayContract("Box box", "Contract.Requires(box != null && box.Value == 1); Contract.Ensures(Contract.Result<int>() == 1); " +
            "try { var data = new System.Collections.Generic.Dictionary<string, int>(); } catch (System.Exception) { } return box.Value;", WorkerClaimOutcome.Unknown,
            members: "public sealed class Box { public int Value; }");
    }

    [Test]
    public async Task GenericKeyDictionaryWrongCountNeedsConcreteInitializationEvidence()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 1); " +
            "var data = new System.Collections.Generic.Dictionary<string, int>(); return data.Count;", WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task GenericKeyDictionaryOpaqueMutationCannotKeepZeroCount()
    {
        await CheckInterfaceArrayContract("string key", "Contract.Requires(key != null); Contract.Ensures(Contract.Result<int>() == 0); " +
            "var data = new System.Collections.Generic.Dictionary<string, int>(); data.Add(key, 17); return data.Count;", WorkerClaimOutcome.Unknown);
    }

    [Test]
    public async Task GenericKeyDictionaryStringEmptyCountIsZero()
    {
        await CheckInterfaceArrayContract("", "Contract.Ensures(Contract.Result<int>() == 0); " +
            "var data = new System.Collections.Generic.Dictionary<string, int>(); return data.Count;", WorkerClaimOutcome.Proven);
    }

    [Test]
    public void GenericKeyDictionaryConstructorDoesNotInvokeKeyCode()
    {
        const string source = "public sealed class Key { static Key() { throw new System.InvalidOperationException(); } " +
            "public Key() { throw new System.InvalidOperationException(); } public override bool Equals(object other) { throw new System.InvalidOperationException(); } " +
            "public override int GetHashCode() { throw new System.InvalidOperationException(); } } " +
            "public static class C { public static int Target() { return new System.Collections.Generic.Dictionary<Key, int>().Count; } }";
        var compilation = CSharpCompilation.Create("DictionaryKeyConstructorOracle", [CSharpSyntaxTree.ParseText(source)], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(";", emitted.Diagnostics));
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("DictionaryKeyConstructorOracle", isCollectible: true);
        try
        {
            image.Position = 0;
            Assert.That(runtime.LoadFromStream(image).GetType("C")!.GetMethod("Target")!.Invoke(null, null), Is.EqualTo(0));
        }
        finally { runtime.Unload(); }
    }
}
