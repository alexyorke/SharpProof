using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerOwnedMethodBodyGraphTests
{
    private static readonly ImmutableArray<string> ExpectedOwnerNames = ["Root", "Helper"];
    [TestCase("block")]
    [TestCase("expression")]
    [TestCase("partial-block")]
    [TestCase("partial-expression")]
    public async Task RootedCompilerBodyPreservesGraphAndExecution(string shape)
    {
        var declaration = shape switch
        {
            "block" => "public static int Root(int value) { return Helper(value); }",
            "expression" => "public static int Root(int value) => Helper(value);",
            "partial-block" => "public static partial int Root(int value); public static partial int Root(int value) { return Helper(value); }",
            "partial-expression" => "public static partial int Root(int value); public static partial int Root(int value) => Helper(value);",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var source = "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes; public static partial class Subject { " +
            "public static int Helper(int value) { Contract.Requires(value > 0); return value + 1; } " +
            declaration + " public static int Property => 2; static Subject() { } }";
        var compilation = TestCompilation.CreateExact("OwnedMethodBodyGraph", [("owned-body.cs", source)]);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join(Environment.NewLine, emission.Diagnostics));
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("OwnedMethodBodyGraph", isCollectible: true);
        try
        {
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Root")!;
            Assert.That(method.Invoke(null, [1]), Is.EqualTo(2));
            Assert.That(method.Invoke(null, [4]), Is.EqualTo(5));
        }
        finally { runtime.Unload(); }

        var tree = compilation.SyntaxTrees.Single();
        var syntax = await tree.GetRootAsync();
        var bodyDeclaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.Identifier.ValueText == "Root" && (node.Body != null || node.ExpressionBody != null)));
        var model = compilation.GetSemanticModel(tree);
        var operation = (IMethodBodyOperation)model.GetOperation(bodyDeclaration)!;
        Assert.That(operation.Parent, Is.Null);
        Assert.That(operation.Syntax, Is.SameAs(bodyDeclaration));
        var original = ControlFlowGraph.Create(bodyDeclaration, model)!;
        var typed = ControlFlowGraph.Create(operation);
        Assert.That(GraphDescription(typed), Is.EqualTo(GraphDescription(original)));

        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        Assert.That(inventory.Gaps, Is.Empty);
        Assert.That(inventory.Owners.Select(owner => owner is null ? throw new System.ArgumentNullException(nameof(owner)) : (owner.Method.Name)), Is.EquivalentTo(ExpectedOwnerNames));
        var owner = inventory.Owners.Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Method.Name == "Root"));
        Assert.That(owner.Declaration, Is.SameAs(bodyDeclaration));
        Assert.That(owner.SemanticModel.Compilation, Is.SameAs(compilation));
        Assert.That(owner.SemanticModel.SyntaxTree, Is.SameAs(tree));
        Assert.That(owner.Method.PartialImplementationPart, Is.Null);
        Assert.That(owner.Calls.Single().Target.Name, Is.EqualTo("Helper"));
        Assert.That(owner.Calls.Single().Origin, Is.InstanceOf<IInvocationOperation>());
        // Constructors/accessors are not owners in this method-only census.
        Assert.That(inventory.Owners.All(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Declaration is MethodDeclarationSyntax)), Is.True);
    }

    private static ImmutableArray<string> GraphDescription(ControlFlowGraph graph)
    {
        System.ArgumentNullException.ThrowIfNull(graph);
        return graph.Blocks.Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (string.Join("|",
            block.Ordinal, block.Kind, block.ConditionKind,
            block.FallThroughSuccessor?.Destination?.Ordinal,
            block.ConditionalSuccessor?.Destination?.Ordinal,
            string.Join(";", block.Operations.Select(operation =>
                operation is null ? throw new System.ArgumentNullException(nameof(operation)) : ($"{operation.Kind}:{operation.Syntax.SpanStart}:{operation.Syntax.Span.Length}"))),
            block.BranchValue == null ? string.Empty :
                $"{block.BranchValue.Kind}:{block.BranchValue.Syntax.SpanStart}:{block.BranchValue.Syntax.Span.Length}")))
            .ToImmutableArray();
    }
}
