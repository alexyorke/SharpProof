using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class TotalProgramSourceReplayTests
{
    [Test]
    public async Task OrdinaryIdentityUsesActualCompilerInterpreterAndNativeProofs()
    {
        const string member = "public static long Target0(long value) => value;";
        var reference = ObserveActualSource([member], ["Target0"]);
        Assert.That(reference.Values, Is.EqualTo(new object?[] { 7L }));
        Assert.That(reference.Classifications[0].Exact, Is.True);
        var replay = new TotalProgramSourceReplay(12345);
        await replay.CompareBatchAsync([new(member, [7L], false)], 0);

        Assert.That(replay.Result, Is.EqualTo(new TotalProgramSourceResult(
            1, 1, 1, 1, 0, ImmutableArray<FuzzFailure>.Empty)));
    }

    [Test]
    public async Task ExistingLoopPostconditionStrategyUsesItsActualCurrentParameter()
    {
        const string member = "public static long Target0(long value) { " +
            "for (int i = 0; i < 2; i++) value = unchecked(value + 1); return value; }";
        var reference = ObserveActualSource([member], ["Target0"]);
        Assert.That(reference.Values, Is.EqualTo(new object?[] { 9L }));
        Assert.That(reference.Classifications[0].Exact, Is.True);
        var replay = new TotalProgramSourceReplay(12346);
        await replay.CompareBatchAsync([new(member, [7L], true)], 0);

        Assert.That(replay.Result, Is.EqualTo(new TotalProgramSourceResult(
            1, 1, 1, 1, 0, ImmutableArray<FuzzFailure>.Empty)));
    }

    [Test]
    public async Task OrdinaryLockReturnsNormallyAndRetainsItsActualUnsupportedInterpreterExit()
    {
        var member = LockMember(0);
        var reference = ObserveActualSource([member], ["Target0"]);
        Assert.That(reference.Values, Is.EqualTo(new object?[] { 7L }));
        Assert.That(reference.Classifications[0].Exact, Is.True);
        Assert.That(reference.Classifications[0].Execution, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        var replay = new TotalProgramSourceReplay(12347);
        await replay.CompareBatchAsync([new(member, [7L], false)], 0);

        var result = replay.Result;
        Assert.That(result.Cases, Is.EqualTo(1));
        Assert.That(result.Agreements, Is.Zero);
        Assert.That(result.NativeProofs, Is.Zero);
        Assert.That(result.NativeRefutations, Is.Zero);
        Assert.That(result.ExceptionalExits, Is.Zero);
        Assert.That(result.Failures, Has.Length.EqualTo(1));
        var reproducer = member + "\n// inputs: [7]";
        Assert.That(result.Failures[0], Is.EqualTo(new FuzzFailure(0, 12347,
            "total-program", reproducer, reproducer,
            "Target0: compiled/original disagreement")));
    }

    [Test]
    public async Task ActualCatchVariableRetainsItsIncompleteProgramClassification()
    {
        const string member = "public static long Target0(long value) { " +
            "try { return 49L / value; } catch (System.Exception error) { return 0; } }";
        var reference = ObserveActualSource([member], ["Target0"]);
        Assert.That(reference.Values, Is.EqualTo(new object?[] { 7L }));
        Assert.That(reference.Classifications[0].Exact, Is.False);
        var replay = new TotalProgramSourceReplay(12351);
        await replay.CompareBatchAsync([new(member, [7L], false)], 0);
        var result = replay.Result;
        Assert.That(result.Cases, Is.EqualTo(1));
        Assert.That(result.Agreements, Is.Zero);
        Assert.That(result.NativeProofs, Is.Zero);
        Assert.That(result.NativeRefutations, Is.Zero);
        Assert.That(result.Failures, Has.Length.EqualTo(1));
        var reproducer = member + "\n// inputs: [7]";
        Assert.That(result.Failures[0], Is.EqualTo(new FuzzFailure(0, 12351,
            "total-program", reproducer, reproducer,
            "Target0: incomplete " + reference.Classifications[0].Abstention)));
    }

    [Test]
    public void ActualConstantOverflowReportsItsRealCompilerDiagnostic()
    {
        const string member = "public static long Target0(long value) => long.MaxValue + 1L;";
        var compilation = Compile([member]);
        var emission = GeneratedCompilationEmitter.Emit(compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.False);
        Assert.That(emission.Emit.Diagnostics.Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id),
            Does.Contain("CS0220"));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.Id),
            Does.Contain("CS0220"));
        var replay = new TotalProgramSourceReplay(12348);
        Func<Task> compare = () => replay.CompareBatchAsync([new(member, [7L], false)], 0);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(compare);
        Assert.That(exception!.Message, Does.Contain("CS0220"));
    }

    [Test]
    public void ActualExtraTargetFailsTheOwnedCaseUniverseBoundary()
    {
        const string member = "public static long Target0(long value) => value; " +
            "public static long Target1(long value) => value + 1L;";
        var reference = ObserveActualSource([member], ["Target0", "Target1"]);
        Assert.That(reference.Values, Is.EqualTo(new object?[] { 7L, 8L }));
        Assert.That(reference.Classifications, Has.Length.EqualTo(2));
        Assert.That(reference.Classifications.All(item => item.Exact), Is.True);
        var replay = new TotalProgramSourceReplay(12349);
        Func<Task> compare = () => replay.CompareBatchAsync([new(member, [7L], false)], 0);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(compare);
        Assert.That(exception!.Message,
            Is.EqualTo("Generated Total method count does not match its case universe."));
    }

    [Test]
    public async Task ActualUnsupportedCasesKeepTheExistingRetentionCapAcrossBatches()
    {
        var members = Enumerable.Range(0, 65).Select(LockMember).ToArray();
        var names = Enumerable.Range(0, 65).Select(index => "Target" + index).ToArray();
        var reference = ObserveActualSource(members, names);
        Assert.That(reference.Values, Has.Length.EqualTo(65));
        Assert.That(reference.Values, Is.All.EqualTo(7L));
        Assert.That(reference.Classifications.All(item => item.Exact &&
            item.Execution == IrProgramExecutionStatus.Unsupported), Is.True);
        var replay = new TotalProgramSourceReplay(12350);
        var cases = members.Select(member => new TotalProgramSourceCase(member, [7L], false)).ToArray();
        await replay.CompareBatchAsync(cases.Take(33).ToArray(), 0);
        await replay.CompareBatchAsync(cases.Skip(33).ToArray(), 33);

        var result = replay.Result;
        Assert.That(result.Cases, Is.EqualTo(65));
        Assert.That(result.Agreements, Is.Zero);
        Assert.That(result.NativeProofs, Is.Zero);
        Assert.That(result.NativeRefutations, Is.Zero);
        Assert.That(result.ExceptionalExits, Is.Zero);
        Assert.That(FuzzRunner.MaximumRetainedFailures, Is.EqualTo(64));
        Assert.That(result.Failures, Has.Length.EqualTo(64));
        for (var index = 0; index < result.Failures.Length; index++)
        {
            var reproducer = members[index] + "\n// inputs: [7]";
            Assert.That(result.Failures[index], Is.EqualTo(new FuzzFailure(index, 12350,
                "total-program", reproducer, reproducer,
                names[index] + ": compiled/original disagreement")));
        }
    }

    private static string LockMember(int index)
    {
        return "public static long Target" + index + "(long value) { lock (new object()) { return value; } }";
    }

    private static CSharpCompilation Compile(IReadOnlyList<string> members)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class TotalGenerated { " +
            string.Join("\n", members) + " }", new CSharpParseOptions(LanguageVersion.CSharp12),
            "total-source-replay-control.cs");
        return CSharpCompilation.Create("TotalSourceReplayControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
    }

    private static (object?[] Values, (bool Exact, string Abstention, IrProgramExecutionStatus? Execution)[] Classifications)
        ObserveActualSource(IReadOnlyList<string> members, string[] names)
    {
        var compilation = Compile(members);
        var emission = GeneratedCompilationEmitter.Emit(compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join("\n", emission.Emit.Diagnostics));
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText.StartsWith("Target", StringComparison.Ordinal))
            .ToDictionary(method => method.Identifier.ValueText);
        Assert.That(methods.Keys, Is.EquivalentTo(names));
        var context = new AssemblyLoadContext("TotalSourceReplayControl", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("TotalGenerated")!;
            var values = new object?[names.Length];
            var classifications = new (bool Exact, string Abstention, IrProgramExecutionStatus? Execution)[names.Length];
            for (var index = 0; index < names.Length; index++)
            {
                var name = names[index];
                values[index] = runtime.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, [7L]);
                var method = methods[name];
                var factory = new IrFactory(IrExecutionSemantics.Total);
                var declared = (IMethodSymbol)model.GetDeclaredSymbol(method)!;
                var loweringContext = new TotalLoweringContext(factory, declared);
                var lowering = new RoslynProgramLowerer(factory).LowerCandidate(
                    ControlFlowGraph.Create(method, model)!, loweringContext,
                    CancellationToken.None);
                IrProgramExecutionStatus? execution = null;
                if (lowering.IsExact)
                {
                    var entries = loweringContext.Parameters.ToDictionary(binding => binding.Entry,
                        binding => factory.CreateIntegerValue(factory.GetVariableInfo(binding.Entry).Type, 7L));
                    execution = new IrProgramInterpreter(factory).Execute(lowering.Program, entries).Status;
                }
                classifications[index] = (lowering.IsExact, lowering.Classification.Abstention.ToString(), execution);
            }
            return (values, classifications);
        }
        finally
        {
            context.Unload();
        }
    }
}
