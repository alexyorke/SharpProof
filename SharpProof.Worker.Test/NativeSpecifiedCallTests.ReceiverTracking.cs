using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeSpecifiedCallTests
{
    [TestCase("System.Collections.Generic.IList<int> alias = values; return alias[0];")]
    [TestCase("System.Collections.Generic.IList<int> alias = values; System.Collections.Generic.IList<int> second = alias; return second[0];")]
    [TestCase("System.Collections.Generic.IList<int> alias = values; values = new int[1]; return alias[0];")]
    [TestCase("System.Collections.Generic.IList<int> alias; if (choose) alias = values; else alias = other; return alias[0];")]
    [TestCase("System.Collections.Generic.IList<int> alias = choose ? values : other; return alias[0];")]
    [TestCase("System.Collections.Generic.IList<int> alias = values; alias = other; return alias[0];")]
    public async Task TrackedArrayAliasesPreserveConcreteIdentity(string body)
    {
        await CheckInterfaceArrayContract("int[] values, int[] other, bool choose",
            "Contract.Requires(values != null && other != null && values.Length > 0 && other.Length > 0 && values[0] == 17 && other[0] == 17); " +
            "Contract.Ensures(Contract.Result<int>() == 17); " + body, WorkerClaimOutcome.Proven);
    }

    [Test]
    public async Task TrackedArrayCountAndStoreUseTheSameReceiver()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length > 0); Contract.Ensures(Contract.Result<int>() == 37); " +
            "System.Collections.Generic.IList<int> list = values; System.Collections.Generic.ICollection<int> collection = list; " +
            "list[collection.Count - 1] = 37; return values[values.Length - 1];", WorkerClaimOutcome.Proven);
    }

    [TestCase("return Read(values); static int Read(System.Collections.Generic.IList<int> list) { System.Collections.Generic.IList<int> alias = list; return alias[0]; }")]
    [TestCase("return Read(values); static int Read(System.Collections.Generic.IList<int> list) => list[0];")]
    [TestCase("System.Collections.Generic.IList<int> alias = values; return Read(alias); static int Read(System.Collections.Generic.IList<int> list) => list[0];")]
    [TestCase("return Read(values); static int Read(System.Collections.Generic.IList<int> list) { return Next(list); } static int Next(System.Collections.Generic.IList<int> list) => list[0];")]
    [TestCase("return Read(values); static int Read(System.Collections.Generic.IList<int> list) { list[0] = 17; return list[0]; }")]
    public async Task ConcreteArrayArgumentsSpecializeSourceHelpers(string body)
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null && values.Length > 0 && values[0] == 17); " +
            "Contract.Ensures(Contract.Result<int>() == 17); " + body, WorkerClaimOutcome.Proven);
    }

    [TestCase("System.Collections.Generic.IList<int> alias = values; alias = choose ? values : unknown; return alias[0];")]
    [TestCase("System.Collections.Generic.IList<int> alias = values; (alias, choose) = (unknown, false); return alias[0];")]
    [TestCase("System.Collections.Generic.IList<int> alias = values; alias = unknown; return alias[0];")]
    [TestCase("System.Collections.Generic.IList<int> alias = choose ? values : unknown; return alias[0];")]
    [TestCase("System.Collections.Generic.IList<int> alias = values; Change(ref alias, unknown); return alias[0]; static void Change(ref System.Collections.Generic.IList<int> list, System.Collections.Generic.IList<int> other) { list = other; }")]
    [TestCase("System.Collections.Generic.IList<int> alias = values; System.Action change = () => alias = unknown; change(); return alias[0];")]
    [TestCase("return Read(values, unknown); static int Read(System.Collections.Generic.IList<int> list, System.Collections.Generic.IList<int> other) { System.Action change = () => list = other; change(); return list[0]; }")]
    [TestCase("return Read(values, unknown); static int Read(System.Collections.Generic.IList<int> list, System.Collections.Generic.IList<int> other) { (list, other) = (other, list); return list[0]; }")]
    [TestCase("return Read(values, unknown); static int Read(System.Collections.Generic.IList<int> list, System.Collections.Generic.IList<int> other) { list = other; return list[0]; }")]
    public async Task UncertainArrayReceiverProvenanceDoesNotProveAResult(string body)
    {
        await CheckInterfaceArrayContract("int[] values, System.Collections.Generic.IList<int> unknown, bool choose",
            "Contract.Requires(values != null && values.Length > 0 && values[0] == 17); " +
            "Contract.Ensures(Contract.Result<int>() == 17); " + body, WorkerClaimOutcome.Unknown);
    }


    [Test]
    public async Task TrackedCountRetainsEffectGuaranteesAndArrayCastFaults()
    {
        await CheckInterfaceArrayContract("int[] values",
            "Contract.Requires(values != null); Contract.Ensures(Contract.Result<int>() == values.Length); " +
            "return Count<int>(values);", WorkerClaimOutcome.Proven,
            members: "private static int Count<T>(System.Collections.Generic.ICollection<T> list) => list.Count;");
        await CheckInterfaceArrayEffects("[DoesNotThrow, ZeroAllocations, EnforcePure]", "int[] values",
            "Contract.Requires(values != null); System.Collections.Generic.IList<int> alias = values; return alias.Count;", true, true, true);
        await CheckInterfaceArrayEffects("[DoesNotThrow]", "object value",
            "return ((System.Collections.Generic.IList<int>)(int[])value).Count;", false, null, null);
    }
    [Test]
    public async Task CorpusSwapRetainsConcreteArrayReceiverAcrossItsGenericHelper()
    {
        var declaration = await CorpusSwapDeclaration();
        using var project = WorkerTests.TestProject.Create(
            "using System.Collections.Generic; public static class C { " +
            "public static int Target(int[] values, int first, int second, int read) { C.Swap<int>(values, first, second); return values[read]; } " +
            declaration + " }");
        project.UseNetCoreReferencePack();
        var compilation = project.CreateCompilation();
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var target = (await tree.GetRootAsync(TestContext.CurrentContext.CancellationToken)).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(syntax => syntax is null ? throw new System.ArgumentNullException(nameof(syntax)) : (syntax.Identifier.ValueText == "Target"));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(target)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(
            ControlFlowGraph.Create(target, model)!, context,
            frame =>
            {
                System.ArgumentNullException.ThrowIfNull(frame);
                var binding = new SharpProof.Contracts.ContractBinder(compilation, factory).BindTotalRequires(frame,
                    cancellationToken: TestContext.CurrentContext.CancellationToken);
                Assert.That(binding.IsSuccess, Is.True, binding.Failure.ToString());
                return binding.IsSuccess;
            }, null, TestContext.CurrentContext.CancellationToken, opaqueCalls: true, pinElementReads: true);
        Assert.That(lowered.IsExact, Is.True);
        Assert.That(lowered.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrCallInstruction>()
            .All(call => call is null ? throw new System.ArgumentNullException(nameof(call)) : (factory.GetOperationInfo(call.Operation).Description is { } description &&
                factory.GetString(description).Contains(":bounds", StringComparison.Ordinal))), Is.True,
            "The generic Swap body must inline; its collection calls cannot fall back to opaque dispatch.");
        using var emitted = new MemoryStream();
        var emission = compilation.Emit(emitted, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        emitted.Position = 0;
        var load = new System.Runtime.Loader.AssemblyLoadContext("corpus-array-receiver", isCollectible: true);
        try
        {
            var assembly = load.LoadFromStream(emitted);
            var runtime = assembly.GetType("C")!.GetMethod("Target")!.CreateDelegate<Func<int[]?, int, int, int, int>>();
            foreach (var values in new int[]?[] { null, [], [17], [11, 23], [-1, 0, 7] })
            {
                foreach (var first in new[] { -1, 0, 1, values?.Length ?? 0 })
                {
                    foreach (var second in new[] { -1, 0, 1, values?.Length ?? 0 })
                    {
                        for (var read = 0; read < Math.Max(1, values?.Length ?? 0); read++)
                        {
                            Exception? fault = null;
                            var expected = 0;
                            try
                            { expected = runtime(values?.ToArray(), first, second, read); }
                            catch (Exception exception) when (exception is NullReferenceException or ArgumentOutOfRangeException or IndexOutOfRangeException)
                            { fault = exception; }
                            var parameters = context.Parameters;
                            var type = factory.GetVariableInfo(parameters[0].Entry).Type;
                            var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                                new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
                                {
                                    [parameters[0].Entry] = values == null ? factory.CreateNullValue(type) :
                                        factory.CreateSequenceValue(type, values.Select(value => factory.CreateIntegerValue(value))),
                                    [parameters[1].Entry] = factory.CreateIntegerValue(first),
                                    [parameters[2].Entry] = factory.CreateIntegerValue(second),
                                    [parameters[3].Entry] = factory.CreateIntegerValue(read)
                                }, 100000, new IrProgramReplayOptions(_ => factory.CreateBooleanValue(false)));
                            Assert.That(replay.Status, Is.EqualTo(fault == null ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Exception));
                            if (fault == null)
                            {
                                Assert.That(replay.ConsumedApproximation, Is.False);
                                Assert.That(replay.ReturnValue!.Integer, Is.EqualTo(expected));
                            }
                            else
                            {
                                Assert.That(replay.Exception!.Kind, Is.EqualTo(fault is NullReferenceException ? IrExceptionKind.NullReference :
                                    fault is IndexOutOfRangeException ? IrExceptionKind.IndexOutOfRange : IrExceptionKind.ArgumentOutOfRange));
                            }
                        }
                    }
                }
            }
        }
        finally
        { load.Unload(); }
    }

    [TestCase("bool", true, false)]
    [TestCase("char", true, false)]
    [TestCase("sbyte", true, false)]
    [TestCase("byte", true, false)]
    [TestCase("short", true, false)]
    [TestCase("ushort", true, false)]
    [TestCase("int", true, false)]
    [TestCase("uint", true, false)]
    [TestCase("long", true, false)]
    [TestCase("ulong", true, false)]
    [TestCase("bool", false, false)]
    [TestCase("char", false, false)]
    [TestCase("sbyte", false, false)]
    [TestCase("byte", false, false)]
    [TestCase("short", false, false)]
    [TestCase("ushort", false, false)]
    [TestCase("int", false, false)]
    [TestCase("uint", false, false)]
    [TestCase("long", false, false)]
    [TestCase("ulong", false, false)]
    [TestCase("bool", true, true)]
    [TestCase("char", true, true)]
    [TestCase("sbyte", true, true)]
    [TestCase("byte", true, true)]
    [TestCase("short", true, true)]
    [TestCase("ushort", true, true)]
    [TestCase("int", true, true)]
    [TestCase("uint", true, true)]
    [TestCase("long", true, true)]
    [TestCase("ulong", true, true)]
    [TestCase("bool", false, true)]
    [TestCase("char", false, true)]
    [TestCase("sbyte", false, true)]
    [TestCase("byte", false, true)]
    [TestCase("short", false, true)]
    [TestCase("ushort", false, true)]
    [TestCase("int", false, true)]
    [TestCase("uint", false, true)]
    [TestCase("long", false, true)]
    [TestCase("ulong", false, true)]
    public async Task CorpusArraySwapProvesItsFullContentsPostcondition(string elementType, bool correct, bool local)
    {
        ArgumentNullException.ThrowIfNull(elementType);
        var declaration = (await CorpusSwapDeclaration()).Replace("IList<T>", "System.Collections.Generic.IList<T>", StringComparison.Ordinal);
        var expected = correct ? "(read == first ? Contract.Old(values[second]) : read == second ? Contract.Old(values[first]) : Contract.Old(values[read]))" :
            "Contract.Old(values[read])";
        var body = "Contract.Requires(values != null && first >= 0 && first < values.Length && second >= 0 && second < values.Length && read >= 0 && read < values.Length); " +
            "Contract.Ensures(Contract.Result<" + elementType + ">() == " + expected + "); " +
            "Swap<" + elementType + ">(values, first, second); return values[read];";
        if (local)
        { body += declaration.Replace("public static void Swap<T>(this ", "static void Swap<T>(", StringComparison.Ordinal); }
        await CheckInterfaceArrayContract(elementType + "[] values, int first, int second, int read", body,
            correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted, result: elementType, members: local ? "" : declaration);
    }
    private static async Task<string> CorpusSwapDeclaration()
    {
        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            TestRepository.FindRoot(), "SharpProof.Gates/Corpus/oss-methods.json"), TestContext.CurrentContext.CancellationToken));
        var method = document.RootElement.GetProperty("methods").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "OSS0002");
        var file = document.RootElement.GetProperty("files").EnumerateArray().Single(item =>
            item.GetProperty("sourceId").GetString() == method.GetProperty("sourceId").GetString() &&
            item.GetProperty("path").GetString() == method.GetProperty("path").GetString());
        var declaration = string.Join("\n", file.GetProperty("content").GetString()!.Split('\n')
            .Skip(method.GetProperty("startLine").GetInt32() - 1)
            .Take(method.GetProperty("endLine").GetInt32() - method.GetProperty("startLine").GetInt32() + 1));
        return declaration;
    }

}
