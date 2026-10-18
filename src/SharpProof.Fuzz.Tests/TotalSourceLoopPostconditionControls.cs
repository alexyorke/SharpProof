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

public sealed class TotalSourceLoopPostconditionControls
{
    private const string Member = "public static long Target0(long value) { " +
        "while (value > 0) value = unchecked(value - 1); return value; }";

    [TestCase(0L, true)]
    [TestCase(7L, false)]
    public async Task ActualCountdownRetainsItsObservedPostconditionBoundary(long input, bool agreement)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class TotalGenerated { " + Member + " }",
            new CSharpParseOptions(LanguageVersion.CSharp12));
        var compilation = CSharpCompilation.Create("TotalSourceLoopBoundaryPremise", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        var emission = GeneratedCompilationEmitter.Emit(compilation, TestContext.CurrentContext.CancellationToken);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var syntax = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var symbol = model.GetDeclaredSymbol(syntax);
        Assert.That(symbol, Is.Not.Null);
        Assert.That(symbol!.IsStatic, Is.True);
        Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int64));
        Assert.That(symbol.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int64));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var loweringContext = new TotalLoweringContext(factory, symbol);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(
            ControlFlowGraph.Create(syntax, model)!, loweringContext, TestContext.CurrentContext.CancellationToken);
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var parameter = loweringContext.Parameters.Single();
        var entries = new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
        {
            [parameter.Entry] = factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, input)
        };
        var executed = new IrProgramInterpreter(factory).Execute(lowered.Program, entries);
        Assert.That(executed.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(executed.ConsumedApproximation, Is.False);
        Assert.That(executed.ReturnValue!.Integer, Is.Zero);
        var context = new AssemblyLoadContext("TotalSourceLoopBoundaryPremise", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("TotalGenerated");
            Assert.That(runtime, Is.Not.Null);
            var method = runtime!.GetMethod("Target0", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Assert.That(method!.Invoke(null, [input]), Is.EqualTo(0L));
        }
        finally
        {
            context.Unload();
        }
        var replay = new TotalProgramSourceReplay(12360);
        await replay.CompareBatchAsync([new(Member, [input], false)], 0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = replay.Result;
        await TestContext.Out.WriteLineAsync("input=" + input + "; agreements=" + result.Agreements +
            "; nativeProofs=" + result.NativeProofs + "; nativeRefutations=" + result.NativeRefutations +
            "; details=" + string.Join(" | ", result.Failures.Select(failure => failure is null ? throw new System.ArgumentNullException(nameof(failure)) : (failure.Detail))));
        Assert.That(result.Cases, Is.EqualTo(1));
        Assert.That(result.ExceptionalExits, Is.Zero);
        if (agreement)
        {
            Assert.That(result.Agreements, Is.EqualTo(1));
            Assert.That(result.NativeProofs, Is.EqualTo(1));
            Assert.That(result.NativeRefutations, Is.EqualTo(1));
            Assert.That(result.Failures, Is.Empty);
        }
        else
        {
            Assert.That(result.Agreements, Is.Zero);
            Assert.That(result.Failures, Has.Length.EqualTo(1));
            Assert.That(result.Failures[0].Detail, Does.StartWith("Target0: native agreement "));
            var reproducer = Member + "\n// inputs: [7]";
            Assert.That(result.Failures[0].Original, Is.EqualTo(reproducer));
            Assert.That(result.Failures[0].Minimized, Is.EqualTo(reproducer));
        }
    }
}
