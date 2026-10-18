using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.Frontend;
using SharpProof.Ir;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("ICollection", false)]
    [TestCase("ICollection", true)]
    [TestCase("IList", false)]
    [TestCase("IList", true)]
    public async Task MatchingArrayTryCastCountPreservesLength(string collection, bool local)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var cast = "values as System.Collections.Generic." + collection + "<int>";
        var body = local ? "var list = " + cast + "; return list.Count;" : "return (" + cast + ").Count;";
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null); Contract.Ensures(Contract.Result<int>() == values.Length); " + body,
            WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] values",
            "Contract.Requires(values != null); " + body, true, true, true);
    }

    [TestCase("bool", true)]
    [TestCase("char", true)]
    [TestCase("sbyte", true)]
    [TestCase("byte", true)]
    [TestCase("short", true)]
    [TestCase("ushort", true)]
    [TestCase("int", true)]
    [TestCase("uint", true)]
    [TestCase("long", true)]
    [TestCase("ulong", true)]
    [TestCase("bool", false)]
    [TestCase("char", false)]
    [TestCase("sbyte", false)]
    [TestCase("byte", false)]
    [TestCase("short", false)]
    [TestCase("ushort", false)]
    [TestCase("int", false)]
    [TestCase("uint", false)]
    [TestCase("long", false)]
    [TestCase("ulong", false)]
    public async Task MatchingArrayTryCastStoresPreserveIdentity(string element, bool correct)
    {
        ArgumentNullException.ThrowIfNull(element);
        await CheckInterfaceArrayContract(element + "[] values, int index, " + element + " replacement",
            "Contract.Requires(values != null && index >= 0 && index < values.Length); " +
            "Contract.Ensures(Contract.Result<" + element + ">() == " + (correct ? "replacement" : "Contract.Old(values[index])") + "); " +
            "var list = values as System.Collections.Generic.IList<" + element + ">; list[index] = replacement; return values[index];",
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, element);
    }

    [TestCase("ICollection")]
    [TestCase("IList")]
    public async Task MatchingArrayTryCastsRetainNullReceiverFaults(string collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var body = "return (values as System.Collections.Generic." + collection + "<int>).Count;";
        await CheckInterfaceArrayEffects("[DoesNotThrow]", "int[] values", body, false, null, null);
        await CheckInterfaceArrayContract("int[] values", "Contract.Requires(values == null); Contract.Ensures(Contract.Result<int>() == 17); " +
            "try { " + body + " } catch (System.NullReferenceException) { return 17; }", WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ArrayTryCastReadsAndStoresAgreeWithClrAtNullAndBounds()
    {
        using var project = WorkerTests.TestProject.Create("public static class C { public static int Target(int[] values, int index, int replacement) { " +
            "var list = values as System.Collections.Generic.IList<int>; var collection = values as System.Collections.Generic.ICollection<int>; " +
            "list[index] = replacement; return list[index] + collection.Count; } }");
        project.UseNetCoreReferencePack();
        var compilation = project.CreateCompilation();
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var syntax = (await tree.GetRootAsync(TestContext.CurrentContext.CancellationToken)).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(ControlFlowGraph.Create(syntax, model)!, context,
            static frame => { ArgumentNullException.ThrowIfNull(frame); return true; }, null,
            TestContext.CurrentContext.CancellationToken, opaqueCalls: true, pinElementReads: true);
        Assert.That(lowered.IsExact, Is.True);
        using var emitted = new MemoryStream();
        var emission = compilation.Emit(emitted, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        emitted.Position = 0;
        var load = new System.Runtime.Loader.AssemblyLoadContext("array-try-casts", isCollectible: true);
        try
        {
            var runtime = load.LoadFromStream(emitted).GetType("C")!.GetMethod("Target")!.CreateDelegate<Func<int[]?, int, int, int>>();
            foreach (var values in new int[]?[] { null, [], [11], [17, 23] })
            {
                foreach (var index in new[] { -1, 0, 1, values?.Length ?? 0 })
                {
                    Exception? fault = null;
                    var expected = 0;
                    try
                    { expected = runtime(values?.ToArray(), index, 29); }
                    catch (Exception exception) when (exception is NullReferenceException or ArgumentOutOfRangeException)
                    { fault = exception; }
                    var parameters = context.Parameters;
                    var type = factory.GetVariableInfo(parameters[0].Entry).Type;
                    var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                        new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
                        {
                            [parameters[0].Entry] = values == null ? factory.CreateNullValue(type) :
                                factory.CreateSequenceValue(type, values.Select(value => factory.CreateIntegerValue(value))),
                            [parameters[1].Entry] = factory.CreateIntegerValue(index),
                            [parameters[2].Entry] = factory.CreateIntegerValue(29)
                        }, 100000, new IrProgramReplayOptions(_ => factory.CreateBooleanValue(false)));
                    Assert.That(replay.Status, Is.EqualTo(fault == null ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Exception));
                    if (fault == null)
                    {
                        Assert.That(replay.ConsumedApproximation, Is.False);
                        Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(expected));
                    }
                    else
                    { Assert.That(replay.Exception!.Kind, Is.EqualTo(fault is NullReferenceException ? IrExceptionKind.NullReference : IrExceptionKind.ArgumentOutOfRange)); }
                }
            }
        }
        finally
        { load.Unload(); }
    }
    [TestCase("var list = choose ? values as System.Collections.Generic.IList<int> : other as System.Collections.Generic.IList<int>; return list[0];", "choose ? 17 : 23")]
    [TestCase("var list = values as System.Collections.Generic.IList<int>; values = other; return list[0];", "17")]
    public async Task ArrayTryCastAliasesAndJoinsRetainConcreteReceivers(string body, string expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(expected);
        await CheckInterfaceArrayContract("int[] values, int[] other, bool choose",
            "Contract.Requires(values != null && other != null && values.Length > 0 && other.Length > 0 && values[0] == 17 && other[0] == 23); " +
            "Contract.Ensures(Contract.Result<int>() == (" + expected + ")); " + body, WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task ArrayTryCastArgumentsSpecializeGenericCollectionHelpers()
    {
        await CheckInterfaceArrayContract("int[] values", "Contract.Requires(values != null); " +
            "Contract.Ensures(Contract.Result<int>() == values.Length); " +
            "return Count<int>(values as System.Collections.Generic.ICollection<int>);", WorkerClaimOutcome.Proven,
            members: "private static int Count<T>(System.Collections.Generic.ICollection<T> list) => list.Count;");
    }

    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase(true, false)]
    public async Task GenericLocalCollectionBranchesPreserveConcreteArrayReceivers(bool tryCast, bool correct)
    {
        var argument = tryCast ? "values as System.Collections.Generic.IList<int>" : "values";
        var body = "Contract.Requires(values != null && values.Length > 0); " +
            "Contract.Ensures(Contract.Result<int>() == " + (correct ? "17" : "0") + "); return Read<int>(" + argument + "); " +
            "static int Read<T>(System.Collections.Generic.IList<T> list) => list.Count == 0 ? 0 : 17;";
        await CheckInterfaceArrayContract("int[] values", body, correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted);
    }
    [Test]
    public async Task ArrayTryCastNullCheckPreservesTheArrayIdentity()
    {
        const string body = "var list = values as System.Collections.Generic.IList<int>; if (list == null) return -1; return list.Count;";
        await CheckInterfaceArrayContract("int[] values", "Contract.Ensures(Contract.Result<int>() == (values == null ? -1 : values.Length)); " + body,
            WorkerClaimOutcome.Proven);
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] values", body, true, true, true);
    }
    [Test]
    public async Task MatchingArrayTryCastStoreRetainsMutationEffects()
    {
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] values, int index, int replacement",
            "Contract.Requires(values != null && index >= 0 && index < values.Length); " +
            "var list = values as System.Collections.Generic.IList<int>; list[index] = replacement; return values[index];", true, true, false);
    }
    [TestCase("object values", "return (values as System.Collections.Generic.IList<int>).Count;")]
    [TestCase("int[] values, System.Collections.Generic.IList<int> unknown, bool choose", "var list = choose ? values as System.Collections.Generic.IList<int> : unknown; return list.Count;")]
    [TestCase("string[] values", "return (values as System.Collections.Generic.IList<string>).Count;")]
    [TestCase("int[] values", "return ((object)values as System.Collections.Generic.IList<uint>).Count;")]
    public async Task UnknownArrayTryCastRepresentationsStayConservative(string parameters, string body)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(body);
        await CheckInterfaceArrayContract(parameters, "Contract.Ensures(Contract.Result<int>() == 17); " + body,
            WorkerClaimOutcome.Unknown);
    }
}
