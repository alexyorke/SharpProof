using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SearchLib.Purity;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public class SymbolicProofPipelineTests
{
    [Test]
    public void ConditionTruth_AttributesUnknownToTheDecisiveBranchStage()
    {
        var session = new SequencedProofSearchSession(
            "smt_timeout",
            "smt_method_budget_exceeded");
        using var service = new SmtAnalysisService(
            SmtAnalysisOptions.Default,
            () => session);
        var value = new SymbolicVariableTerm("pipeline_text", SmtValueKind.String);
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(
                SymbolicStringPredicateKind.Contains,
                value,
                new SymbolicStringConstantTerm("needle")),
            SyntaxFactory.ParseExpression("pipeline_text.Contains(\"needle\")"),
            "test.pipeline"));

        var result = new SymbolicProofService(service)
            .ClassifyConditionTruth(new SymbolicState(), condition);

        Assert.That(session.ClassificationCount, Is.EqualTo(2));
        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(result.Info.Reason, Is.EqualTo("smt_method_budget_exceeded"));
        Assert.That(result.Info.UnknownReason, Is.EqualTo(SymbolicUnknownReason.MethodBudgetExceeded));
        Assert.That(result.Info.Stage, Is.EqualTo(SymbolicProofStage.Budgeting));
        Assert.That(result.Info.Support, Is.EqualTo(SymbolicProofSupport.Exact));
    }

    [Test]
    public void FormulaCompatibilityProof_IsMarkedApproximate()
    {
        using var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = new SymbolicProofService(service)
            .ClassifyFormulaReachability(Array.Empty<SmtFormula>());

        Assert.That(result.Info.Status, Is.EqualTo(SymbolicProofStatus.Reachable));
        Assert.That(result.Info.Support, Is.EqualTo(SymbolicProofSupport.Approximate));
        Assert.That(result.Info.Stage, Is.EqualTo(SymbolicProofStage.ResultMapping));
    }

    [Test]
    public void ProofServiceCache_EvictsAtPerServiceLimitWithoutChangingResults()
    {
        using var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var proofService = new SymbolicProofService(service);
        var firstState = CreateUnsupportedState(0);
        var first = proofService.ClassifyReachability(firstState);
        SymbolicState? lastState = null;

        for (var index = 1; index < 1100; index++)
        {
            lastState = CreateUnsupportedState(index);
            proofService.ClassifyReachability(lastState);
        }

        var cachedLast = proofService.ClassifyReachability(lastState!);
        var recomputedFirst = proofService.ClassifyReachability(firstState);

        Assert.That(cachedLast.Info.CacheHit, Is.True);
        Assert.That(cachedLast.Info.Budget?.Cache, Is.Not.Null);
        Assert.That(cachedLast.Info.Budget!.Cache!.Entries, Is.LessThanOrEqualTo(2048));
        Assert.That(cachedLast.Info.Budget.Cache.Evictions, Is.GreaterThan(0));
        Assert.That(recomputedFirst.Info.CacheHit, Is.False);
        Assert.That(recomputedFirst.Info.Status, Is.EqualTo(first.Info.Status));
        Assert.That(recomputedFirst.Info.Reason, Is.EqualTo(first.Info.Reason));
    }

    [Test]
    public void StructuralPathCache_IsBoundedPerMethod()
    {
        var statements = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 520).Select(static index => $"value += {index};"));
        var tree = CSharpSyntaxTree.ParseText(
            "public class C { public void M() { int value = 0; " + statements + " } }");
        var compilation = CSharpCompilation.Create(
            "structural_path_cache",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var model = compilation.GetSemanticModel(tree);
        var sites = tree.GetRoot().DescendantNodes().OfType<ExpressionStatementSyntax>().ToArray();

        foreach (var site in sites)
            SymbolicReachabilityService.CollectPathConditionsAt(site, model, CancellationToken.None, false);

        SymbolicReachabilityService.CollectPathConditionsAt(sites[^1], model, CancellationToken.None, false);
        var cache = SymbolicReachabilityService.GetStructuralPathCacheInfo(sites[^1], model);

        Assert.That(sites, Has.Length.EqualTo(520));
        Assert.That(cache.Entries, Is.EqualTo(512));
        Assert.That(cache.Evictions, Is.EqualTo(8));
        Assert.That(cache.Misses, Is.EqualTo(520));
        Assert.That(cache.Hits, Is.EqualTo(1));
    }

    private static SymbolicState CreateUnsupportedState(int index)
    {
        var name = "proof_cache_resource_" + index;
        var fact = SymbolicFact.Exact(
            new SymbolicFreshnessAtom(new SymbolicVariableTerm(name, SmtValueKind.Reference)),
            SyntaxFactory.ParseExpression(name),
            "test.cache");
        return new SymbolicState(new[] { fact });
    }

    private sealed class SequencedProofSearchSession : ISmtProofSearchSession
    {
        private readonly Queue<string> _reasons;

        internal SequencedProofSearchSession(params string[] reasons)
        {
            _reasons = new Queue<string>(reasons);
        }

        internal int ClassificationCount { get; private set; }

        public long ConsumedResourceCount => 0;

        public PurityProofResult Classify(PurityProofQuery query, TimeSpan timeout)
        {
            ClassificationCount++;
            var reason = _reasons.Dequeue();
            return new PurityProofResult(
                PurityProofOutcome.Unknown,
                new ProofCheckInfo(true, Feasibility.Unknown),
                new ProofCheckInfo(false, Feasibility.Unknown),
                reason);
        }

        public void Dispose()
        {
        }
    }
}
