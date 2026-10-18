using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class BulkCollectorWireConversionBoundaryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void SdkClauseMappingsRejectUndefinedConversionInputs(bool evidence)
    {
        var compilation = TestCompilation.Create("WireConversionBoundary", """
            using SharpProof.Attributes;
            public static class Subject
            {
                public static int Read(int value)
                {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return value;
                }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Read").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        var binding = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(binding.IsSuccess, Is.True);
        Assert.That(binding.Clauses, Has.Length.EqualTo(2));
        var requires = binding.Clauses.Single(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Requires));
        var ensures = binding.Clauses.Single(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == BoundContractKind.Ensures));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CompilerLoweringWireMappings.ToCompiler(requires.Kind), Is.EqualTo(CompilerContractKind.Requires));
            Assert.That(CompilerLoweringWireMappings.ToCompiler(ensures.Kind), Is.EqualTo(CompilerContractKind.Ensures));
            Assert.That(requires.Evidence, Is.EqualTo(BoundContractEvidence.CompilerBoundInvocation));
            Assert.That(CompilerLoweringWireMappings.ToWorkerEvidence(requires.Evidence), Is.EqualTo(WorkerClaimEvidence.DirectClause));
            Assert.That(CompilerLoweringWireMappings.ToWorkerEvidence(ensures.Evidence), Is.EqualTo(WorkerClaimEvidence.DirectClause));
        }

        Execute(compilation, contractCallsEnabled: true);
        var tree = compilation.SyntaxTrees.Single();
        var originalOptions = (CSharpParseOptions)tree.Options;
        Assert.That(originalOptions.PreprocessorSymbolNames,
            Does.Contain(SharpProof.Attributes.Contract.ConditionalSymbol));
        var runtimeOptions = originalOptions.WithPreprocessorSymbols(originalOptions.PreprocessorSymbolNames
            .Where(symbol => symbol is null ? throw new System.ArgumentNullException(nameof(symbol)) : (symbol != SharpProof.Attributes.Contract.ConditionalSymbol)));
        var runtimeTree = tree.WithRootAndOptions(tree.GetRoot(), runtimeOptions);
        Assert.That(runtimeTree.ToString(), Is.EqualTo(tree.ToString()));
        Execute(compilation.ReplaceSyntaxTree(tree, runtimeTree), contractCallsEnabled: false);

        // Undefined input exists only at this conversion API boundary. It is
        // never written into a binding, IR object, manifest or proof result.
        var error = evidence
            ? Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
                CompilerLoweringWireMappings.ToWorkerEvidence((BoundContractEvidence)int.MaxValue)))
            : Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
                CompilerLoweringWireMappings.ToCompiler((BoundContractKind)int.MaxValue)));
        Assert.That(error!.ParamName, Is.EqualTo("value"));
    }

    private static void Execute(CSharpCompilation compilation, bool contractCallsEnabled)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("WireConversionBoundary", isCollectible: true);
        try
        {
            image.Position = 0;
            var read = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Read")!;
            if (contractCallsEnabled)
            {
                var error = Assert.Throws<System.Reflection.TargetInvocationException>((Action)(() => read.Invoke(null, [7])));
                Assert.That(error!.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(error.InnerException!.Message,
                    Is.EqualTo("Contract.Result<T>() is valid only inside Contract.Ensures(...)."));
            }
            else
            { Assert.That(read.Invoke(null, [7]), Is.EqualTo(7)); }
        }
        finally { runtime.Unload(); }
    }
}
