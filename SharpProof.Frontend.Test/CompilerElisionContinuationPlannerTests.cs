using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CompilerElisionContinuationPlannerTests
{
    private const string Terminal = "flag ? throw new System.NullReferenceException() : throw new System.InvalidOperationException()";

    [Test]
    public void TerminalOwnerRecoveryRestoresOnlyItsActualBlock()
    {
        var subject = Create("{ Log(" + Terminal + "); return 7; }");
        var source = ElidedBranch(subject);
        Assert.That(subject.Planner.TryRecoverCompilerElidedOwner(source, [], out var recovered), Is.True);
        Assert.That(recovered.IsReachable, Is.False);
        Assert.That(subject.Planner.IsRestored(recovered), Is.True);
        Assert.That(subject.Planner.IsSpecificationConditional(source), Is.False);
        subject.Planner.PlanCompilerElidedTerminator(source);
        Assert.That(subject.Planner.TryGetPlan(source, out var planned), Is.True);
        Assert.That(planned, Is.SameAs(recovered));
        Assert.That(subject.Work.ConstructionLimitExceeded, Is.False);
    }

    [Test]
    public void OmittedRuntimeOperationRefusesOwnerRecovery()
    {
        var subject = Create("{ Runtime(); Log(" + Terminal + "); return 7; }");
        var source = ElidedBranch(subject);
        var runtime = subject.Graph.Blocks.Single(block => block.Operations.Any(operation =>
            operation.Syntax.ToString().Contains("Runtime()", StringComparison.Ordinal)));
        Assert.That(runtime.Operations, Is.Not.Empty);
        Assert.That(subject.Emission.IsElided(runtime.Operations[0]), Is.False);
        Assert.That(subject.Planner.TryRecoverCompilerElidedOwner(source, [runtime], out _), Is.False);
        Assert.That(subject.Work.ConstructionLimitExceeded, Is.False);
    }

    [Test]
    public void OmittedRuntimeBranchRefusesOwnerRecovery()
    {
        var subject = Create("{ if (other) { Runtime(); } Log(" + Terminal + "); return 7; }");
        var source = ElidedBranch(subject);
        var runtime = subject.Graph.Blocks.Single(block => block.BranchValue?.Syntax.ToString() == "other");
        Assert.That(runtime.Operations, Is.Empty);
        Assert.That(runtime.BranchValue, Is.Not.Null);
        Assert.That(subject.Emission.IsElided(runtime.BranchValue!), Is.False);
        Assert.That(subject.Planner.TryRecoverCompilerElidedOwner(source, [runtime], out _), Is.False);
        Assert.That(subject.Work.ConstructionLimitExceeded, Is.False);
    }

    [Test]
    public void ForeignAuthenticOwnerIsNotIndexedOrCached()
    {
        var subject = Create("{ Log(" + Terminal + "); return 7; }",
            "public static int Other(bool flag, bool other) { Log(" + Terminal + "); return 8; }");
        var syntax = subject.Compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Other");
        var model = subject.Compilation.GetSemanticModel(syntax.SyntaxTree);
        var graph = ControlFlowGraph.Create(syntax, model)!;
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var source = graph.Blocks.Single(block => block.ConditionKind != ControlFlowConditionKind.None &&
            block.BranchValue != null && subject.Emission.IsElided(block.BranchValue));
        Assert.That(subject.Graph.Blocks.Contains(source), Is.False);
        Assert.That(subject.Emission.OwningInvocation(source.BranchValue!), Is.Not.Null);
        Assert.That(subject.Planner.TryRecoverCompilerElidedOwner(source, [], out _), Is.False);
        Assert.That(subject.Planner.TryElidedSpecificationContinuation(source, out _, compilerMode: true), Is.False);
        subject.Planner.PlanCompilerElidedTerminator(ElidedBranch(subject));
        Assert.That(subject.Planner.TryGetPlan(ElidedBranch(subject), out _), Is.True);
    }

    [Test]
    public void UnreachableCompilerBranchCannotSelectLiveEmissionEdge()
    {
        var subject = Create("{ if (false) { if (other) { Runtime(); } Runtime(); } return 7; }");
        var source = subject.Graph.Blocks.Single(block => block.BranchValue?.Syntax.ToString() == "other");
        Assert.That(source.IsReachable, Is.False);
        Assert.That(source.ConditionKind, Is.Not.EqualTo(ControlFlowConditionKind.None));
        Assert.That(source.FallThroughSuccessor!.Destination, Is.Not.Null);
        Assert.That(source.ConditionalSuccessor!.Destination!.IsReachable, Is.False);
        var rejection = Assert.Throws<RoslynTotalProgramLowerer.RegionIncompleteException>(
            (System.Action)(() => CompilerElisionContinuationPlanner.ElidedEmissionSuccessor(source)));
        Assert.That(rejection!.Reason, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
        Assert.That(CompilerElisionContinuationPlanner.ElidedEmissionSuccessor(source, allowUnreachableCompilerElision: true),
            Is.SameAs(source.FallThroughSuccessor));
    }

    [Test]
    public void NonBranchContinuationRequestIsRejectedWithoutPopWork()
    {
        var subject = Create("{ return 7; }");
        var source = subject.Graph.Blocks.Single(block =>
            block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return);
        Assert.That(source.BranchValue, Is.InstanceOf<ILiteralOperation>());
        Assert.That(source.ConditionalSuccessor, Is.Null);
        Assert.That(subject.Planner.TryElidedSpecificationContinuation(source, out _, out var work), Is.False);
        Assert.That(work, Is.Zero);
        Assert.That(subject.Work.ConstructionLimitExceeded, Is.False);
    }

    [Test]
    public void SuccessfulSiblingScansDoNotAuthorizeConflictingCachedStops()
    {
        var subject = Create("{ if (other) { Log(flag && other); } else { Log(flag || other); } return 7; }");
        var children = subject.Graph.Blocks.Where(block => block.ConditionKind != ControlFlowConditionKind.None &&
            block.BranchValue != null && subject.Emission.IsElided(block.BranchValue)).ToArray();
        Assert.That(children, Has.Length.EqualTo(2));
        var targets = new HashSet<BasicBlock>();
        foreach (var child in children)
        {
            Assert.That(subject.Planner.TryElidedSpecificationContinuation(child, out var continuation, compilerMode: true), Is.True);
            targets.Add(continuation);
        }
        Assert.That(targets, Has.Count.EqualTo(2));
        var source = subject.Graph.Blocks.Single(block => block.ConditionKind != ControlFlowConditionKind.None &&
            block.BranchValue != null && !subject.Emission.IsElided(block.BranchValue));
        Assert.That(subject.Planner.TryElidedSpecificationContinuation(source, out _, out var work, compilerMode: true), Is.False);
        Assert.That(work, Is.EqualTo(2));
        var rejection = Assert.Throws<RoslynTotalProgramLowerer.RegionIncompleteException>(
            (System.Action)(() => subject.Planner.RequireCompilerContinuation(source)));
        Assert.That(rejection!.Reason, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
        foreach (var child in children)
        {
            Assert.That(subject.Planner.TryElidedSpecificationContinuation(child, out var continuation, compilerMode: true), Is.True);
            Assert.That(targets.Contains(continuation), Is.True);
        }
        Assert.That(subject.Work.ConstructionLimitExceeded, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WorkAccountUsesTheActualDefaultPool(bool sharedSession)
    {
        var subject = Create("{ return 7; }");
        var session = sharedSession
            ? new TotalSourceCallSession(subject.Compilation, null, null, CancellationToken.None) : null;
        var work = new TotalRegionWork(CancellationToken.None, session);
        if (session != null)
        {
            Assert.That(session.Spend(), Is.True);
        }
        var available = RoslynTotalProgramLowerer.MaximumRegionSteps - (sharedSession ? 1 : 0);
        for (var ordinal = 0; ordinal < available; ordinal++)
        {
            work.Spend();
        }
        Assert.That(work.ConstructionLimitExceeded, Is.False);
        Assert.Throws<RoslynTotalProgramLowerer.RegionIncompleteException>((System.Action)(() => work.Spend()));
        Assert.That(work.ConstructionLimitExceeded, Is.True);
        if (session != null)
        {
            Assert.That(session.ConstructionLimitExceeded, Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancellationPrecedesChargingTheActualWorkAccount(bool sharedSession)
    {
        var subject = Create("{ return 7; }");
        var session = sharedSession
            ? new TotalSourceCallSession(subject.Compilation, null, null, CancellationToken.None) : null;
        var work = new TotalRegionWork(new CancellationToken(canceled: true), session);
        Assert.Throws<OperationCanceledException>((System.Action)(() => work.Spend()));
        Assert.That(work.ConstructionLimitExceeded, Is.False);
        if (session != null)
        {
            for (var ordinal = 0; ordinal < RoslynTotalProgramLowerer.MaximumRegionSteps; ordinal++)
            {
                Assert.That(session.Spend(), Is.True);
            }
            Assert.That(session.Spend(), Is.False);
        }
    }

    private static BasicBlock ElidedBranch(Subject subject)
    {
        var source = subject.Graph.Blocks.Single(block => block.ConditionKind != ControlFlowConditionKind.None &&
            block.BranchValue != null && subject.Emission.IsElided(block.BranchValue));
        Assert.That(subject.Emission.OwningInvocation(source.BranchValue!), Is.Not.Null);
        return source;
    }

    private static Subject Create(string body, string additionalMember = "")
    {
        var source = "public static class Subject { public static int Target(bool flag, bool other) " + body +
            " [System.Diagnostics.Conditional(\"ABSENT\")] static void Log(bool argument) { } " +
            "static void Runtime() { } " + additionalMember + " }";
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12),
            path: "compiler-continuation-protocol.cs");
        var compilation = CSharpCompilation.Create("CompilerContinuationProtocol", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var target = (IMethodSymbol)model.GetDeclaredSymbol(syntax)!;
        var graph = ControlFlowGraph.Create(syntax, model)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(compilation));
        var emission = new CSharpInvocationEmissionPolicy(compilation);
        var work = new TotalRegionWork(CancellationToken.None);
        return new(compilation, graph, emission, work, new(graph, context, emission, work, compilation));
    }

    private sealed record Subject(CSharpCompilation Compilation, ControlFlowGraph Graph,
        CSharpInvocationEmissionPolicy Emission, TotalRegionWork Work, CompilerElisionContinuationPlanner Planner);
}
