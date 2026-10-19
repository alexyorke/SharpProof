using System.Numerics;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkFrontendCallsElidedConstructorCaptureTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void ConstructorArgumentCapturesKeepTheirActualOmittedOwner(bool partial, bool enabled)
    {
        const string targetSource = "public static int Target(bool flag) { Log(flag ? throw new System.NullReferenceException() : " +
            "throw new System.InvalidOperationException(flag ? \"left\" : \"right\")); return 17; }";
        var declaration = partial
            ? "static partial void Log(bool argument);" + (enabled ? "static partial void Log(bool argument) { }" : "")
            : "[System.Diagnostics.Conditional(\"ABSENT\")] static void Log(bool argument) { }";
        var source = "public static partial class Subject { " + targetSource + declaration + " }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: enabled ? ["ABSENT"] : []), "constructor-capture.cs");
        var compilation = CSharpCompilation.Create("ConstructorCapture_" + partial + "_" + enabled,
            [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var graph = ControlFlowGraph.Create(syntax, model)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var runtime = new AssemblyLoadContext("ConstructorCapture_" + partial + "_" + enabled, isCollectible: true);
        try
        {
            image.Position = 0;
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            // Freeze actual compiled behavior before checking the proposed CFG shape.
            foreach (var flag in new[] { false, true })
            {
                if (enabled)
                {
                    var fault = Assert.Throws<TargetInvocationException>((Action)(() => method.Invoke(null, [flag])));
                    Assert.That(fault!.InnerException, Is.TypeOf(flag ? typeof(NullReferenceException) : typeof(InvalidOperationException)));
                    if (!flag)
                    { Assert.That(fault.InnerException!.Message, Is.EqualTo("right")); }
                }
                else
                { Assert.That(method.Invoke(null, [flag]), Is.EqualTo(17)); }
            }

            Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
            Assert.That(context.HasScalarSignature, Is.True);
            Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
            var ownerSyntax = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
            var owner = (IInvocationOperation)model.GetOperation(ownerSyntax)!;
            var emission = new CSharpInvocationEmissionPolicy(compilation);
            Assert.That(owner.TargetMethod.IsConditional, Is.EqualTo(!partial));
            if (partial)
            { Assert.That(owner.TargetMethod.PartialImplementationPart != null, Is.EqualTo(enabled)); }
            Assert.That(emission.IsElided(owner, requireOwningInvocation: true), Is.EqualTo(!enabled));
            Assert.That(owner.Syntax.SyntaxTree, Is.SameAs(tree));
            Assert.That(owner.Syntax.Span, Is.EqualTo(ownerSyntax.Span));
            var outer = (ConditionalExpressionSyntax)ownerSyntax.ArgumentList.Arguments.Single().Expression;
            var terminal = graph.Blocks.Where(block =>
                block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw)).ToArray();
            Assert.That(terminal.Length, Is.GreaterThanOrEqualTo(2));
            foreach (var block in terminal)
            {
                Assert.That(block.BranchValue, Is.Not.Null);
                AssertOwner(emission, block.BranchValue!, owner);
                Assert.That(emission.IsElided(block.BranchValue!), Is.EqualTo(!enabled));
            }

            var dispatch = graph.Blocks.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.ConditionKind != ControlFlowConditionKind.None &&
                block.BranchValue?.Syntax.SyntaxTree == tree && block.BranchValue.Syntax.Span == outer.Condition.Span));
            var falseEdge = dispatch.ConditionKind == ControlFlowConditionKind.WhenTrue
                ? dispatch.FallThroughSuccessor : dispatch.ConditionalSuccessor;
            Assert.That(falseEdge?.Destination, Is.Not.Null);
            var pending = new Stack<BasicBlock>();
            var visited = new HashSet<BasicBlock>();
            pending.Push(falseEdge!.Destination!);
            while (pending.Count != 0)
            {
                var block = pending.Pop();
                if (!visited.Add(block))
                { continue; }
                Assert.That(visited.Count, Is.LessThanOrEqualTo(graph.Blocks.Length));
                foreach (var edge in new[] { block.FallThroughSuccessor, block.ConditionalSuccessor })
                {
                    if (edge?.Destination is { } destination)
                    { pending.Push(destination); }
                }
            }
            var captures = visited.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations)).OfType<IFlowCaptureOperation>()
                .Where(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.Syntax.SyntaxTree == tree && outer.WhenFalse.Span.Contains(operation.Syntax.Span))).ToArray();
            Assert.That(captures, Is.Not.Empty, "The fixed false-arm constructor argument must produce compiler scan operations.");
            foreach (var capture in captures)
            {
                AssertOwner(emission, capture, owner);
                Assert.That(emission.IsElided(capture), Is.EqualTo(!enabled));
            }

            var lowerer = new RoslynProgramLowerer(factory);
            var lowering = enabled
                ? lowerer.LowerCandidate(graph, context, static _ => true, null, TestContext.CurrentContext.CancellationToken)
                : lowerer.LowerCandidate(graph, context);
            Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
            Assert.That(lowering.ConstructionLimitExceeded, Is.False);
            foreach (var flag in new[] { false, true })
            {
                var replay = new IrProgramInterpreter(factory).Execute(lowering.Program,
                    new Dictionary<IrVarId, IrValue> { [context.Parameters.Single().Entry] = factory.CreateBooleanValue(flag) },
                    maximumSteps: 10000, replayOptions: ExceptionConstructorNormalReplay.Options(factory));
                Assert.That(replay.Status, Is.EqualTo(enabled ? IrProgramExecutionStatus.Exception : IrProgramExecutionStatus.Returned));
                if (enabled)
                {
                    Assert.That(replay.Exception!.Kind, Is.EqualTo(IrExceptionKind.Explicit));
                    var span = factory.GetOperationInfo(replay.Instruction!.Operation).SourceSpan!;
                    Assert.That(source.Substring(span.Start, span.Length), Is.EqualTo(flag
                        ? "new System.NullReferenceException()"
                        : "new System.InvalidOperationException(flag ? \"left\" : \"right\")"));
                }
                else
                { Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(17))); }
                ExceptionConstructorNormalReplay.AssertApproximation(replay, enabled && flag);
            }
        }
        finally
        { runtime.Unload(); }
    }

    private static void AssertOwner(CSharpInvocationEmissionPolicy emission, IOperation operation, IInvocationOperation owner)
    {
        System.ArgumentNullException.ThrowIfNull(emission);
        System.ArgumentNullException.ThrowIfNull(operation);
        System.ArgumentNullException.ThrowIfNull(owner);
        var actual = emission.OwningInvocation(operation);
        Assert.That(actual, Is.Not.Null);
        Assert.That(actual!.Syntax.SyntaxTree, Is.SameAs(owner.Syntax.SyntaxTree));
        Assert.That(actual.Syntax.Span, Is.EqualTo(owner.Syntax.Span));
    }
}
