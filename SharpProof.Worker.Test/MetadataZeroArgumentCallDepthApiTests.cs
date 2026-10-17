using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class MetadataZeroArgumentCallDepthApiTests
{
    private static readonly string[] LeafInstructions = ["Ret"];
    private static readonly string[] CallingInstructions = ["Call", "Ret"];

    [TestCase(8, true)]
    [TestCase(256, false)]
    public void DistinctEmittedMethodsRespectTheDefaultCallDepth(int methodCount, bool exact)
    {
        var names = Enumerable.Range(0, methodCount).Select(index => index == 0 ? "Target" : "Step" + index).ToArray();
        var methods = names.Select((name, index) => ((index < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(index)) : (name is null ? throw new System.ArgumentNullException(nameof(name)) : ("public static void " + name + "() { " +
            (index + 1 == methodCount ? "" : names[index + 1] + "();") + " }"))));
        var library = "public static class Library { " + string.Join(" ", methods) + " }";
        const string source = "public static class Subject { public static int Target() { Library.Target(); return 0; } }";
        using var subject = new MetadataTestSubject(library, source);
        var owner = subject.Compilation.GetTypeByMetadataName("Library")!;
        var symbols = names.Select(name => name is null ? throw new System.ArgumentNullException(nameof(name)) : (owner.GetMembers(name).OfType<IMethodSymbol>().Single())).ToArray();
        Assert.That(symbols, Has.Length.EqualTo(methodCount));
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
        var census = new CompilerTotalIlBodyProvider(subject.Compilation, references);
        for (var index = 0; index < symbols.Length; index++)
        {
            var method = symbols[index];
            Assert.That(method.IsStatic && method.ReturnsVoid, Is.True);
            Assert.That(method.Parameters, Is.Empty);
            Assert.That(method.DeclaredAccessibility, Is.EqualTo(Accessibility.Public));
            Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingModule, symbols[0].ContainingModule), Is.True);
            var body = census.Resolve(method, CancellationToken.None);
            Assert.That(body, Is.Not.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(body!.Method, method), Is.True);
            Assert.That(body.ParameterAttributes, Is.Empty);
            Assert.That(body.Locals, Is.Empty);
            Assert.That(body.ModuleMvid, Is.Not.Empty);
            Assert.That(body.ImageSha256, Has.Length.EqualTo(64));
            Assert.That(body.Instructions.Select(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Code)),
                Is.EqualTo(index + 1 == methodCount ? LeafInstructions : CallingInstructions));
            if (index + 1 < methodCount)
            {
                var called = body.Instructions[0].Method;
                var expected = symbols[index + 1];
                Assert.That(called, Is.Not.Null);
                Assert.That(called!.MetadataToken, Is.EqualTo(expected.MetadataToken));
                Assert.That(called.GetDocumentationCommentId(), Is.EqualTo(expected.GetDocumentationCommentId()));
                Assert.That(called.ContainingAssembly.Identity, Is.EqualTo(expected.ContainingAssembly.Identity));
                Assert.That(called.ContainingModule.Name, Is.EqualTo(expected.ContainingModule.Name));
            }
        }
        Assert.That(subject.Method.Invoke(null, null), Is.Null);
        Assert.That(subject.RootMethod.Invoke(null, null), Is.EqualTo(0));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var graph = ControlFlowGraph.Create(declaration, model)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(context.HasScalarSignature, Is.True);
        var provider = new CompilerTotalIlBodyProvider(subject.Compilation, references);
        var lowered = TotalBodyLowering.Lower(subject.Compilation, graph, context, CancellationToken.None,
            resolveIl: provider.Resolve);
        Assert.That(lowered.IsExact, Is.EqualTo(exact));
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        if (!exact)
        {
            Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            return;
        }
        Assert.That(lowered.Classification.Abstention, Is.EqualTo(FrontendAbstention.None));
        var replay = new IrProgramInterpreter(factory).Execute(lowered.Program);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(BigInteger.Zero));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }
}
