using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275OwnedSpecTermDepthAdmissionTests
{
    [TestCase(255)]
    [TestCase(256)]
    public void PublicBooleanDeclarationDepthKeepsOwnedTermAdmissionBounded(int unaryCount)
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Target() { Contract.Ensures(Contract.Result<bool>() == true); return true; }
            }
            """;
        var compilation = TestCompilation.Create("OwnedSpecTermDepthAdmission", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("OwnedSpecTermDepthAdmission", isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            Action invoke = () => method.Invoke(null, []);
            var exception = Assert.Throws<TargetInvocationException>(invoke);
            Assert.That(exception!.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.InnerException!.Message,
                Is.EqualTo("Contract.Result<T>() is valid only inside Contract.Ensures(...)."));
        }
        finally
        {
            runtime.Unload();
        }
        // The analysis image intentionally emits conditional contract calls. Replay
        // the identical source with only that runtime contract symbol omitted.
        var tree = compilation.SyntaxTrees.Single();
        var analysisOptions = (CSharpParseOptions)tree.Options;
        Assert.That(analysisOptions.PreprocessorSymbolNames, Does.Contain(Contract.ConditionalSymbol));
        var runtimeOptions = analysisOptions.WithPreprocessorSymbols(
            analysisOptions.PreprocessorSymbolNames.Where(symbol => symbol != Contract.ConditionalSymbol));
        Assert.That(runtimeOptions.PreprocessorSymbolNames, Does.Not.Contain(Contract.ConditionalSymbol));
        var runtimeCompilation = compilation.RemoveAllSyntaxTrees()
            .AddSyntaxTrees(tree.WithRootAndOptions(tree.GetRoot(), runtimeOptions));
        Assert.That(runtimeCompilation.SyntaxTrees.Single().ToString(), Is.EqualTo(tree.ToString()));
        TestCompilation.AssertNoErrors(runtimeCompilation);
        using var runtimeImage = new MemoryStream();
        var runtimeEmitted = runtimeCompilation.Emit(runtimeImage);
        Assert.That(runtimeEmitted.Success, Is.True, string.Join("\n", runtimeEmitted.Diagnostics));
        RuntimeAssemblyTestHost.WithRuntimeAssembly("OwnedSpecTermDepthRuntimeControl", runtimeImage, assembly =>
            Assert.That(assembly.GetType("Subject")!.GetMethod("Target")!.Invoke(null, []), Is.True));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.Parameters, Is.Empty);
        var factory = preparation.Total.Program.Factory;
        SpecTermDeclaration declaration = new SpecBooleanDeclaration(true);
        for (var ordinal = 0; ordinal < unaryCount; ordinal++)
        {
            declaration = new SpecUnaryDeclaration(IrUnaryOperator.Not, declaration, IrTypeKind.Boolean);
        }
        // Raw public declaration admission, with no metadata target/spec-pack identity or proof enrollment.
        if (unaryCount == 256)
        {
            Action reject = () => CompilerTotalSpecTermInstantiator.Instantiate(factory, declaration, [], factory.IntegerType, 0);
            var exception = Assert.Throws<ArgumentException>(reject);
            Assert.That(exception!.Message, Is.EqualTo("A specification-pack term is too deep."));
        }
        else
        {
            var term = CompilerTotalSpecTermInstantiator.Instantiate(factory, declaration, [], factory.IntegerType, 0);
            Assert.That(term.Type, Is.EqualTo(factory.BooleanType));
            var value = new IrInterpreter(factory).Evaluate(term, new Dictionary<ScopedIrId<IrVariableTag>, IrValue>());
            Assert.That(value.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(value.Value!.Boolean, Is.False);
        }
        var leaf = CompilerTotalSpecTermInstantiator.Instantiate(factory, new SpecBooleanDeclaration(true), [], factory.IntegerType, 0);
        var control = new IrInterpreter(factory).Evaluate(leaf, new Dictionary<ScopedIrId<IrVariableTag>, IrValue>());
        Assert.That(control.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(control.Value!.Boolean, Is.True);
    }
}
