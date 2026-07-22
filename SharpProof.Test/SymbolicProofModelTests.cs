using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.ProofCore.Analysis;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
internal class SymbolicProofModelTests {
    [Test]
    public void ConditionTruth_PreservesTheDecisiveBranchFailureReason() {
        var session = new SequencedProofSearchSession("smt_timeout", "smt_method_budget_exceeded");
        using var service = new SmtAnalysisService(SmtAnalysisOptions.Default, () => session);
        var value = new SymbolicVariableTerm("pipeline_text", SmtValueKind.String);
        var condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicStringPredicateAtom(SymbolicStringPredicateKind.Contains, value, new SymbolicStringConstantTerm("needle")),
            SyntaxFactory.ParseExpression("pipeline_text.Contains(\"needle\")"),
            "test.pipeline"));

        var result = new SymbolicProofService(service)
            .ClassifyConditionTruth(new SymbolicState(), condition);

        Assert.That(session.ClassificationCount, Is.EqualTo(1));
        Assert.That(result.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        Assert.That(result.Reason, Is.EqualTo("ir_condition_true_branch_feasibility_unknown"));
        Assert.That(result.UnknownReason, Is.EqualTo(SymbolicUnknownReason.Timeout));
    }
    [Test]
    public void TypedReachabilityProof_ClassifiesEmptyStateAsReachable() {
        using var service = new SmtAnalysisService(SmtAnalysisOptions.Default);

        var result = new SymbolicProofService(service)
            .ClassifyReachability(new SymbolicState());

        Assert.That(result.Status, Is.EqualTo(SymbolicProofStatus.Reachable));
    }
    [Test]
    public void ProofServiceCache_EvictsAtPerServiceLimitWithoutChangingResults() {
        using var service = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var proofService = new SymbolicProofService(service);
        var firstState = CreateUnsupportedState(0);
        var first = proofService.ClassifyReachability(firstState);
        SymbolicState? lastState = null;

        for (var index = 1; index < 1100; index++) {
            lastState = CreateUnsupportedState(index);
            proofService.ClassifyReachability(lastState);
        }
        var cachedLast = proofService.ClassifyReachability(lastState!);
        var recomputedFirst = proofService.ClassifyReachability(firstState);

        Assert.That(cachedLast.CacheHit, Is.True);
        Assert.That(cachedLast.Budget?.Cache, Is.Not.Null);
        Assert.That(cachedLast.Budget!.Cache!.Entries, Is.LessThanOrEqualTo(2048));
        Assert.That(cachedLast.Budget.Cache.Evictions, Is.GreaterThan(0));
        Assert.That(recomputedFirst.CacheHit, Is.False);
        Assert.That(recomputedFirst.Status, Is.EqualTo(first.Status));
        Assert.That(recomputedFirst.Reason, Is.EqualTo(first.Reason));
    }
    [Test]
    public void StructuralPathCache_IsBoundedPerMethod() {
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

        var first = SymbolicReachabilityService.CollectPathStateAt(sites[0], model, CancellationToken.None);
        SymbolicState? last = null;
        foreach (var site in sites.Skip(1))
            last = SymbolicReachabilityService.CollectPathStateAt(site, model, CancellationToken.None);

        var cachedLast = SymbolicReachabilityService.CollectPathStateAt(sites[^1], model, CancellationToken.None);
        var recomputedFirst = SymbolicReachabilityService.CollectPathStateAt(sites[0], model, CancellationToken.None);

        Assert.That(sites, Has.Length.EqualTo(520));
        Assert.That(cachedLast, Is.SameAs(last));
        Assert.That(recomputedFirst, Is.Not.SameAs(first));
    }
    private static SymbolicState CreateUnsupportedState(int index) {
        var name = "proof_cache_resource_" + index;
        var source = SyntaxFactory.ParseExpression(name);
        var fact = new SymbolicFact(
            new SymbolicTruthAtom(new SymbolicVariableTerm(name, SmtValueKind.Bool)),
            true,
            SymbolicFactConfidence.Unsupported,
            "test.cache",
            source.Span,
            null,
            "test.cache.unsupported");
        return new SymbolicState(new[] { fact });
    }
    private sealed class SequencedProofSearchSession : IAnalysisProofSearchSession {
        private readonly Queue<string> _reasons;

        internal SequencedProofSearchSession(params string[] reasons) => _reasons = new Queue<string>(reasons);
        internal int ClassificationCount { get; private set; }

        public long ConsumedResourceCount => 0;

        public AnalysisProofResult Classify(AnalysisProofQuery query, TimeSpan timeout) {
            ClassificationCount++;
            var reason = _reasons.Dequeue();
            return new AnalysisProofResult(
                AnalysisProofOutcome.Unknown,
                new ProofCheckInfo(true, Feasibility.Unknown),
                new ProofCheckInfo(false, Feasibility.Unknown),
                reason);
        }
        public void Dispose() {
        }
    }
}
