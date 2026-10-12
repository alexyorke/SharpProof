using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerDisjointSourceBoundaryCoverageTests
{
    [TestCase(45, 990, true)]
    [TestCase(46, 1024, true)]
    [TestCase(128, 1024, true)]
    [TestCase(129, 0, true)]
    [TestCase(45, 990, false)]
    [TestCase(46, 1024, false)]
    [TestCase(128, 1024, false)]
    [TestCase(129, 0, false)]
    public async Task RealClosedSiblingTypesRetainOnlyCertifiedInputPairs(
        int parameterCount, int expectedPairCount, bool matchingClaim)
    {
        var types = string.Concat(Enumerable.Range(0, parameterCount).Select(index => $"public sealed class A{index} {{ }}\n"));
        var parameters = string.Join(",", Enumerable.Range(0, parameterCount).Select(index => $"A{index} p{index}"));
        var expectedResult = matchingClaim ? 1 : 2;
        var source = $$"""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            {{types}}
            public static class Subject {
                public static int Root({{parameters}}) {
                    Contract.Ensures(Contract.Result<int>() == {{expectedResult}});
                    return 1;
                }
            }
            """;
        var compilation = TestCompilation.Create("DisjointSourceBoundary", ("disjoint-inputs.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));
        var declaration = (await compilation.SyntaxTrees.Single().GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration)!;
        Assert.That(method.Parameters, Has.Length.EqualTo(parameterCount));
        foreach (var parameter in method.Parameters)
        {
            var type = (INamedTypeSymbol)parameter.Type;
            Assert.That(type.IsSealed, Is.True);
            Assert.That(type.Arity, Is.Zero);
            Assert.That(type.BaseType!.SpecialType, Is.EqualTo(SpecialType.System_Object));
            Assert.That(type.BaseType.BaseType, Is.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly), Is.True);
        }
        var possiblePairs = (from left in Enumerable.Range(0, parameterCount)
                             from right in Enumerable.Range(left + 1, parameterCount - left - 1)
                             select new CompilerDisjointInputPair(left, right)).ToArray();
        foreach (var pair in possiblePairs)
        { Assert.That(compilation.ClassifyConversion(method.Parameters[pair.Left].Type, method.Parameters[pair.Right].Type).Exists, Is.False); }
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("DisjointSourceBoundary", isCollectible: true);
        try
        {
            var assembly = runtime.LoadFromStream(image);
            var root = assembly.GetType("Subject")!.GetMethod("Root")!;
            Assert.That(root.GetParameters(), Has.Length.EqualTo(parameterCount));
            Assert.That(root.Invoke(null, new object?[parameterCount]), Is.EqualTo(1));
        }
        finally { runtime.Unload(); }

        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var claim = artifact.Manifest.Claims.Single();
        var callable = artifact.Callables.Single();
        Assert.That(callable.CallableId, Is.EqualTo(claim.CallableId));
        Assert.That(callable.FailureReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(callable.Total, Is.Not.Null);
        Assert.That(callable.Total!.IsBodyAbstraction, Is.False);
        Assert.That(callable.Total.Parameters, Has.Length.EqualTo(parameterCount));
        Assert.That(callable.Total.DisjointInputs.Select(pair => (pair.Left, pair.Right)),
            Is.EqualTo(possiblePairs.Take(expectedPairCount).Select(pair => (pair.Left, pair.Right))));
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var total = preparations.Single().Total!;
        var factory = total.Program.Factory;
        var inputs = total.Parameters.ToDictionary(parameter => parameter.Entry,
            parameter => factory.CreateNullValue(factory.GetVariableInfo(parameter.Entry).Type));
        var execution = new IrProgramInterpreter(factory).Execute(total.Program, inputs);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(1)));
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().ClaimId, Is.EqualTo(claim.ClaimId));
        Assert.That(response.ClaimResults.Single().Outcome,
            Is.EqualTo(matchingClaim ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted));
    }
}
