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

public sealed class TotalSourceIrreducibleControl
{
    private const string Member = "public static long Target0(long value) { " +
        "if (value > 3) goto A; goto B; " +
        "A: value = unchecked(value - 1); if (value > 0) goto B; return value; " +
        "B: value = unchecked(value - 1); if (value > 0) goto A; return value; }";

    [Test]
    public async Task ActualTwoEntryCycleRetainsItsNativeAdmissionRejection()
    {
        const long input = 7;
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
        Assert.That(result.Agreements, Is.Zero);
        Assert.That(result.NativeProofs, Is.Zero);
        Assert.That(result.NativeRefutations, Is.Zero);
        Assert.That(result.Failures, Has.Length.EqualTo(1));
        Assert.That(result.Failures[0].Detail, Is.EqualTo("Target0: native admission UnsupportedBody"));
        var reproducer = Member + "\n// inputs: [7]";
        Assert.That(result.Failures[0].Original, Is.EqualTo(reproducer));
        Assert.That(result.Failures[0].Minimized, Is.EqualTo(reproducer));
    }
}
