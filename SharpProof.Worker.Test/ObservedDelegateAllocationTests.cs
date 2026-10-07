using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Frontend.Host;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ObservedDelegateAllocationTests
{
    [TestCase("captured-write")]
    [TestCase("captured-read")]
    [TestCase("ref")]
    [TestCase("assignment-value")]
    [TestCase("field-store")]
    [TestCase("argument")]
    public async Task ObservedConstructionStillAllocates(string shape)
    {
        var body = shape switch
        {
            "captured-write" => "System.Action held = new System.Action(Sink); Writer = () => { held = new System.Action(Sink); }; return null;",
            "captured-read" => "System.Action held = new System.Action(Sink); Writer = () => held(); return null;",
            "ref" => "System.Action held = new System.Action(Sink); Replace(ref held); return null;",
            "assignment-value" => "System.Action held; return held = new System.Action(Sink);",
            "field-store" => "Writer = new System.Action(Sink); return null;",
            "argument" => "Consume(new System.Action(Sink)); return null;",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var source = "using SharpProof.Attributes; public static class Subject { " +
            "public static System.Action Writer; static void Sink() {} " +
            "static void Replace(ref System.Action held) { held = null; } static void Consume(System.Action held) {} " +
            "[ZeroAllocations, System.Runtime.CompilerServices.MethodImpl(" +
            "System.Runtime.CompilerServices.MethodImplOptions.NoInlining | " +
            "System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] " +
            "public static object Target(int value) { " + body + " } }";
        var compilation = TestCompilation.Create("ObservedDelegateOracle", ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("ObservedDelegateOracle", isCollectible: true);
        try
        {
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            var target = method.CreateDelegate<Func<int, object?>>();
            for (var repeat = 0; repeat < 3; repeat++)
            { _ = target(1); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            object? result = null;
            for (var repeat = 0; repeat < 32; repeat++)
            { result = target(1); }
            var measured = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(result, shape == "assignment-value" ? Is.InstanceOf<System.Action>() : Is.Null);
            Assert.That(measured, Is.GreaterThan(0));
            await TestContext.Out.WriteLineAsync($"shape={shape}; bytes32={measured}; IL={Convert.ToHexString(method.GetMethodBody()!.GetILAsByteArray()!)}");
        }
        finally { runtime.Unload(); }
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var preparation = preparations.Single();
        // Closures and mutable ref calls do not enroll a complete native body.
        // Exercise the same expression lowerer directly to ensure their observed
        // delegate initializers retain allocation when surrounded by those uses.
        var tree = compilation.SyntaxTrees.Single();
        var model = CompilationModelProvider.GetSemanticModel(compilation, tree);
        var targetSyntax = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var creation = model.GetOperation(targetSyntax)!.Descendants().OfType<IDelegateCreationOperation>()
            .First(CSharpOperationSemantics.IsExplicitDelegateCreation);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, (IMethodSymbol)model.GetDeclaredSymbol(targetSyntax)!)
        { Compilation = compilation };
        var builder = new IrProgramBuilder(factory);
        var lowered = new RoslynTotalExpressionLowerer(context, builder).LowerBodyValue(creation, builder.CreateBlock());
        Assert.That(lowered.Classification.IsExact, Is.True);
        builder.Return(lowered.Continuation, factory.CreateOperation("return"), lowered.Value);
        Assert.That(builder.Build().Blocks.SelectMany(block => block.Instructions)
            .OfType<IrAllocationInstruction>(), Has.Exactly(1).Items,
            "Keep the observed delegate's allocation in the lowered program.");
        var native = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(native.Outcome, Is.Not.TypeOf<ProvenOutcome>(), native.Reason.ToString());
        if (shape is "assignment-value" or "field-store" or "argument")
        { Assert.That(native.Outcome, Is.TypeOf<RefutedOutcome>(), native.Reason.ToString()); }
    }
}
