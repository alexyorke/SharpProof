using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeForkJoinProjectionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void IndependentlyChecksEveryProjectedChildAfterCodecRoundTrip(bool arrays)
    {
        var projection = Lower(arrays ? "for(int i=0;i<count;i++){var value=new int[2];}" : "for(int i=0;i<count;i++){}");
        foreach (var child in projection.Children)
        {
            var cancellation = TestContext.CurrentContext.CancellationToken;
            var owner = CompilerTotalCallableArtifactCodec.Decode(
                CompilerTotalCallableArtifactCodec.Encode(child.Owner, cancellation),
                new WorkerCallableManifestEntry { CallableId = child.Owner.CallableId }, [], cancellation)!;
            var program = ResourceProgramCodec.Decode(ResourceProgramCodec.Encode(child.Cost, cancellation), cancellation);
            var check = NativeResourceProgramVerifier.Verify(program, owner.CallableId, owner, cancellation);
            Assert.That(check.Verified, Is.True, check.UnknownReason);
            Assert.That(check.Cost!.LoopEntries.ToDisplayString(), Is.EqualTo("max(0, count)"));
            Assert.That(check.Cost.Arrays.ToDisplayString(), Is.EqualTo(arrays ? "max(0, count)" : "0"));
            Assert.That(check.Cost.Elements.ToDisplayString(), Is.EqualTo(arrays ? "(max(0, count) * 2)" : "0"));
        }
    }

    [Test]
    public void ChildCostsWithoutTheirInputBindingsHaveNoNativeCost()
    {
        var cost = Lower("for(int i=0;i<count;i++){}").Children[0].Cost;
        var unrelated = Lower("").Children[0].Owner with { Parameters = [] };
        var check = NativeResourceProgramVerifier.Verify(cost, unrelated.CallableId, unrelated, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Verified, Is.False);
        Assert.That(check.Cost, Is.Null);
        Assert.That(check.UnknownReason, Is.Not.Null.And.Not.Empty);
    }

    private static CompilerForkJoinProjection Lower(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using System.Threading.Tasks; public static class C {public static Task Root(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) {" + body + "}}";
        var compilation = TestCompilation.Create("ForkJoinProjection", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Root");
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        var result = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.Null);
        return result.Projection!;
    }
}
