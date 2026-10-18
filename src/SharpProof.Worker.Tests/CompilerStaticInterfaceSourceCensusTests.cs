using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerStaticInterfaceSourceCensusTests
{
    [TestCase("class")]
    [TestCase("struct")]
    public async Task ExplicitStaticImplementationPreservesCensusAndNativeEffect(string ownerKind)
    {
        System.ArgumentNullException.ThrowIfNull(ownerKind);
        var source = $$"""
            using SharpProof.Attributes;
            public interface IStaticRoot { static abstract int Root(int value); }
            public {{ownerKind}} Subject : IStaticRoot
            {
                [DoesNotThrow] static int IStaticRoot.Root(int value) => value + 1;
            }
            public static class Runner
            {
                public static int Run(int value) => Invoke<Subject>(value);
                private static int Invoke<T>(int value) where T : IStaticRoot => T.Root(value);
            }
            """;
        var compilation = TestCompilation.Create("ExplicitStaticInterface", ("static-interface.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join(Environment.NewLine, emission.Diagnostics));
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ExplicitStaticInterface", isCollectible: true);
        try
        {
            var run = runtime.LoadFromStream(image).GetType("Runner")!.GetMethod("Run")!;
            Assert.That(run.Invoke(null, [1]), Is.EqualTo(2));
            Assert.That(run.Invoke(null, [4]), Is.EqualTo(5));
        }
        finally { runtime.Unload(); }

        var tree = compilation.SyntaxTrees.Single();
        var syntax = await tree.GetRootAsync();
        var declaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.ExplicitInterfaceSpecifier != null));
        var model = compilation.GetSemanticModel(tree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        Assert.That(method.IsStatic, Is.True);
        Assert.That(method.MethodKind, Is.EqualTo(MethodKind.ExplicitInterfaceImplementation));
        Assert.That(method.ExplicitInterfaceImplementations, Has.Length.EqualTo(1));
        Assert.That(method.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(method.ContainingType.TypeKind, Is.EqualTo(ownerKind == "class" ? TypeKind.Class : TypeKind.Struct));
        var operation = (IMethodBodyOperation)model.GetOperation(declaration)!;
        Assert.That(operation.Parent, Is.Null);
        Assert.That(operation.Syntax, Is.SameAs(declaration));

        var builder = new ClaimManifestBuilder(compilation);
        var inventory = builder.BuildPotentialCallShadow();
        var gap = inventory.Gaps.Single(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Start == declaration.SpanStart));
        Assert.That(gap.Reason, Is.EqualTo("UnsupportedOwner"));
        Assert.That(gap.TreeOrdinal, Is.Zero);
        Assert.That(gap.Length, Is.EqualTo(declaration.Span.Length));
        Assert.That(inventory.Owners.Any(owner => owner is null ? throw new System.ArgumentNullException(nameof(owner)) : (SymbolEqualityComparer.Default.Equals(owner.Method, method))), Is.False);
        Assert.That(inventory.Owners.Single().Method.Name, Is.EqualTo("Run"));
        var discovery = builder.Build();
        var target = discovery.Targets.Single().Value;
        Assert.That(SymbolEqualityComparer.Default.Equals(target.Method, method), Is.True);
        Assert.That(target.IsVerifierSupported, Is.True);

        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        Assert.That(artifact.CompilerDiagnostics, Is.Empty);
        var claim = artifact.Manifest.Claims.Single();
        Assert.That(claim.EffectContractKind, Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
        var callable = artifact.Callables.Single();
        Assert.That(callable.CallableId, Is.EqualTo(claim.CallableId));
        Assert.That(callable.FailureReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(callable.Total, Is.Not.Null);
        Assert.That(callable.Total!.IsBodyAbstraction, Is.False);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        var result = response.ClaimResults.Single();
        Assert.That(result.ClaimId, Is.EqualTo(claim.ClaimId));
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
    }
}
