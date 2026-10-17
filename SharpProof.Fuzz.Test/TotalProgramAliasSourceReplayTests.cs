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

public sealed class TotalProgramAliasSourceReplayTests
{
    [TestCase(2, true)]
    [TestCase(2, false)]
    [TestCase(3, true)]
    [TestCase(3, false)]
    public async Task ActualObjectAliasInputsPreserveCompilerInterpreterAndNativeAgreement(
        int parameterCount, bool sharedIdentity)
    {
        var member = parameterCount == 2
            ? "public static bool Target0(object x, object y) { return x == y; }"
            : "public static bool Target0(object x, object y, object z) { return x == y && y == z; }";
        var shared = new object();
        object?[] arguments = [.. Enumerable.Range(0, parameterCount)
            .Select(_ => sharedIdentity ? shared : new object())];
        Assert.That(ReferenceEquals(arguments[0], arguments[1]), Is.EqualTo(sharedIdentity));
        if (parameterCount == 3)
        {
            Assert.That(ReferenceEquals(arguments[1], arguments[2]), Is.EqualTo(sharedIdentity));
        }
        AssertActualCompilerAndInterpreter(member, arguments, sharedIdentity);

        var replay = new TotalProgramSourceReplay(12350 + parameterCount);
        await replay.CompareBatchAsync([new(member, arguments, false)], 0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = replay.Result;
        Assert.That(result.Failures, Is.Empty,
            string.Join("\n", result.Failures.Select(failure => failure is null ? throw new System.ArgumentNullException(nameof(failure)) : (failure.Detail))) +
            $"\nActual replay counters: agreements={result.Agreements}, " +
            $"native proofs={result.NativeProofs}, native refutations={result.NativeRefutations}.");
        Assert.That(result.Cases, Is.EqualTo(1));
        Assert.That(result.Agreements, Is.EqualTo(1));
        Assert.That(result.NativeProofs, Is.EqualTo(1));
        Assert.That(result.NativeRefutations, Is.EqualTo(1));
        Assert.That(result.ExceptionalExits, Is.Zero);
    }

    private static void AssertActualCompilerAndInterpreter(string member,
        object?[] arguments, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(arguments, nameof(arguments));
        System.ArgumentNullException.ThrowIfNull(member);
        var tree = CSharpSyntaxTree.ParseText("public static class TotalGenerated { " + member + " }",
            new CSharpParseOptions(LanguageVersion.CSharp12), "total-object-alias-control.cs");
        var compilation = CSharpCompilation.Create("TotalObjectAliasControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        var emission = GeneratedCompilationEmitter.Emit(compilation, TestContext.CurrentContext.CancellationToken);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True, string.Join("\n", emission.Emit.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var declared = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        Assert.That(declared.Parameters, Has.Length.EqualTo(arguments.Length));
        Assert.That(declared.Parameters.All(parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.Type.SpecialType == SpecialType.System_Object)), Is.True);
        Assert.That(declared.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
        var runtime = new AssemblyLoadContext("TotalObjectAliasControl", isCollectible: true);
        try
        {
            image.Position = 0;
            var actualType = runtime.LoadFromStream(image).GetType("TotalGenerated")!;
            var actualValue = actualType.GetMethod("Target0", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, arguments);
            Assert.That(actualValue, Is.EqualTo(expected));
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, declared);
            var lowering = new RoslynProgramLowerer(factory).LowerCandidate(
                ControlFlowGraph.Create(syntax, model)!, context, TestContext.CurrentContext.CancellationToken);
            Assert.That(factory.Semantics, Is.EqualTo(IrExecutionSemantics.Total));
            Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
            Assert.That(lowering.Abstentions, Is.Empty);
            Assert.That(lowering.Program.Factory, Is.SameAs(factory));
            Assert.That(lowering.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                .OfType<IrCallInstruction>(), Is.Empty);
            Assert.That(lowering.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                .OfType<IrHavocInstruction>(), Is.Empty);
            var entries = context.Parameters.ToDictionary(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry),
                binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (factory.CreateReferenceValue(factory.GetVariableInfo(binding.Entry).Type,
                    arguments[binding.Parameter.Ordinal]!)));
            var interpreted = new IrProgramInterpreter(factory).Execute(lowering.Program, entries);
            Assert.That(interpreted.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(interpreted.ConsumedApproximation, Is.False);
            Assert.That(interpreted.ReturnValue!.Kind, Is.EqualTo(IrValueKind.Boolean));
            Assert.That(interpreted.ReturnValue.Boolean, Is.EqualTo(expected));
        }
        finally
        {
            runtime.Unload();
        }
    }
}
