using System.Collections.Immutable;
using System.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ConditionalElisionDeadShadowContinuationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualShadowFactoryPreservesOmittedConditionalInsideConstantBranch(bool branchEnabled)
    {
        const string library = "using SharpProof.Attributes; public static class Library { " +
            "public static int Target([Positive] int value) => value + 1; }";
        var source = "public static class Subject { public static int Target(int value) { if (" +
            (branchEnabled ? "true" : "false") + ") { Log(value > 0 ? true : false); } return Library.Target(value); } " +
            "[System.Diagnostics.Conditional(\"ABSENT\")] static void Log(bool argument) { } }";
        using var subject = new MetadataTestSubject(library, source);
        Assert.That(subject.Method.Invoke(null, [17]), Is.EqualTo(18));
        Assert.That(subject.RootMethod.Invoke(null, [17]), Is.EqualTo(18));

        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var graph = ControlFlowGraph.Create(syntax, model)!;
        Assert.That(graph.OriginalOperation.Syntax.SyntaxTree, Is.SameAs(tree));
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var logSyntax = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (invocation.Expression.ToString() == "Log"));
        var invocation = (IInvocationOperation)model.GetOperation(logSyntax)!;
        Assert.That(invocation.TargetMethod.IsConditional, Is.True);
        var emission = new CSharpInvocationEmissionPolicy(subject.Compilation);
        Assert.That(emission.IsElided(invocation, requireOwningInvocation: true), Is.True);
        var conditional = graph.Blocks.Single(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.ConditionKind != ControlFlowConditionKind.None &&
            block.BranchValue != null && emission.IsElided(block.BranchValue)));
        Assert.That(conditional.BranchValue!.Syntax.ToString(), Is.EqualTo("value > 0"));
        Assert.That(conditional.IsReachable, Is.EqualTo(branchEnabled));
        Assert.That(conditional.FallThroughSuccessor!.Destination!.IsReachable, Is.EqualTo(branchEnabled));
        Assert.That(conditional.ConditionalSuccessor!.Destination!.IsReachable, Is.EqualTo(branchEnabled));
        Assert.That(conditional.FallThroughSuccessor.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Regular));
        Assert.That(conditional.ConditionalSuccessor.Semantics, Is.EqualTo(ControlFlowBranchSemantics.Regular));
        Assert.That(conditional.FallThroughSuccessor.FinallyRegions, Is.Empty);
        Assert.That(conditional.ConditionalSuccessor.FinallyRegions, Is.Empty);

        WriteOwnedGraph(graph, source, "dead-shadow-" + (branchEnabled ? "live" : "dead"), [invocation]);
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken);
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(subject.Compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(subject.Compilation, TestContext.CurrentContext.CancellationToken).ToImmutableArray(), references,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken, enableMetadataRequires: true);
        Assert.That(batch.Gaps, Is.Empty);
        Assert.That(batch.Callers, Has.Length.EqualTo(1));
        var body = batch.Callers.Single().Body;
        Assert.That(body.Clauses, Is.Empty);
        Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
        Assert.That(body.CallPreconditions.Single().MetadataClause, Is.Not.Null);
        var factory = body.Program.Factory;
        var entry = body.Parameters.Single().Entry;
        var replay = new IrProgramInterpreter(factory).Execute(body.Program,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(entry, factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, 17)));
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(18)));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ApproximationVariables, Is.Empty);
    }

    private static void WriteOwnedGraph(ControlFlowGraph graph, string source, string scenario,
        IEnumerable<IInvocationOperation> owningInvocations)
    {
        System.ArgumentNullException.ThrowIfNull(graph);
        System.ArgumentNullException.ThrowIfNull(owningInvocations);
        System.ArgumentNullException.ThrowIfNull(scenario);
        System.ArgumentNullException.ThrowIfNull(source);
        var regionsBuilder = ImmutableArray.CreateBuilder<ControlFlowRegion>();
        var pending = ImmutableStack<ControlFlowRegion>.Empty.Push(graph.Root);
        while (!pending.IsEmpty)
        {
            pending = pending.Pop(out var region);
            regionsBuilder.Add(region);
            foreach (var nested in region.NestedRegions)
            { pending = pending.Push(nested); }
        }
        var regions = regionsBuilder.ToImmutable();
        var regionIds = ImmutableDictionaryCapture.Create(regions.Select((region, id) => region is null ? throw new System.ArgumentNullException(nameof(region)) : ((region, id))), item => item.region, item => item.id);
        var evidence = new
        {
            Source = source,
            Scenario = scenario,
            OriginalOperation = new
            {
                Kind = graph.OriginalOperation.Kind.ToString(),
                Tree = graph.OriginalOperation.Syntax.SyntaxTree.FilePath,
                Start = graph.OriginalOperation.Syntax.SpanStart,
                Length = graph.OriginalOperation.Syntax.Span.Length,
                Syntax = graph.OriginalOperation.Syntax.ToString()
            },
            OwningInvocations = owningInvocations.Select(invocation => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (new
            {
                Kind = invocation.Kind.ToString(),
                Tree = invocation.Syntax.SyntaxTree.FilePath,
                Start = invocation.Syntax.SpanStart,
                Length = invocation.Syntax.Span.Length,
                Syntax = invocation.Syntax.ToString(),
                Target = invocation.TargetMethod.ToDisplayString()
            })).ToImmutableArray(),
            Regions = regions.Select(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (new
            {
                Id = regionIds[region],
                Parent = region.EnclosingRegion == null ? (int?)null : regionIds[region.EnclosingRegion],
                Kind = region.Kind.ToString(),
                First = region.FirstBlockOrdinal,
                Last = region.LastBlockOrdinal
            })).ToImmutableArray(),
            Blocks = graph.Blocks.Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (new
            {
                block.Ordinal,
                block.IsReachable,
                Kind = block.Kind.ToString(),
                Region = regionIds[block.EnclosingRegion],
                Condition = block.ConditionKind.ToString(),
                Operations = block.Operations.SelectMany(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.DescendantsAndSelf())).Select(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (new
                {
                    Kind = operation.Kind.ToString(),
                    operation.IsImplicit,
                    Tree = operation.Syntax.SyntaxTree.FilePath,
                    Start = operation.Syntax.SpanStart,
                    Length = operation.Syntax.Span.Length,
                    Syntax = operation.Syntax.ToString()
                })).ToImmutableArray(),
                BranchValue = block.BranchValue?.DescendantsAndSelf().Select(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (new
                {
                    Kind = operation.Kind.ToString(),
                    operation.IsImplicit,
                    Tree = operation.Syntax.SyntaxTree.FilePath,
                    Start = operation.Syntax.SpanStart,
                    Length = operation.Syntax.Span.Length,
                    Syntax = operation.Syntax.ToString()
                })).ToImmutableArray(),
                Fall = block.FallThroughSuccessor == null ? null : new
                {
                    Semantics = block.FallThroughSuccessor.Semantics.ToString(),
                    Destination = block.FallThroughSuccessor.Destination?.Ordinal,
                    Finally = block.FallThroughSuccessor.FinallyRegions.Select(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (regionIds[region])).ToImmutableArray()
                },
                Alternative = block.ConditionalSuccessor == null ? null : new
                {
                    Semantics = block.ConditionalSuccessor.Semantics.ToString(),
                    Destination = block.ConditionalSuccessor.Destination?.Ordinal,
                    Finally = block.ConditionalSuccessor.FinallyRegions.Select(region => region is null ? throw new System.ArgumentNullException(nameof(region)) : (regionIds[region])).ToImmutableArray()
                }
            })).ToImmutableArray()
        };
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ?? TestContext.CurrentContext.WorkDirectory;
        var directory = Path.Combine(repositoryRoot, "artifacts", "coverage", "semantic", "conditional-elision-cfg");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, scenario + "-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(evidence));
        TestContext.Out.WriteLine("Owned CFG evidence: " + path);
    }
}
