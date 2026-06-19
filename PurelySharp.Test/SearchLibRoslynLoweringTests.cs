using NUnit.Framework;
using PurelySharp.Test.Smt;
using SearchLib.Purity;

namespace PurelySharp.Test
{
    [TestFixture]
    public class SearchLibRoslynLoweringTests
    {
        [Test]
        public void Lowering_ContradictoryImpureCallPath_ProvesPure()
        {
            var context = AnalyzerTestHost.CreateConditionImplicationContext("int x", "x > 0 && x < 0", "true");
            var evidence = new AnalyzerPurityEvidence(
                AnalyzerPurityHazardKind.ImpureCallReachability,
                new[] { context.PathCondition },
                context.Conclusion);

            var lowered = AnalyzerEvidenceToSearchLibLowering.TryLower(
                evidence,
                context.SemanticModel,
                CancellationToken.None,
                out var query);

            Assert.That(lowered, Is.True);
            using var search = new PurityProofSearch();
            var result = search.Classify(query!, TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void Lowering_NullReceiverGuard_ProvesNullDereference()
        {
            var context = AnalyzerTestHost.CreateConditionImplicationContext("string s", "s == null", "s == null");
            var evidence = new AnalyzerPurityEvidence(
                AnalyzerPurityHazardKind.NullDereference,
                new[] { context.PathCondition },
                context.Conclusion);

            var lowered = AnalyzerEvidenceToSearchLibLowering.TryLower(
                evidence,
                context.SemanticModel,
                CancellationToken.None,
                out var query);

            Assert.That(lowered, Is.True);
            using var search = new PurityProofSearch();
            var result = search.Classify(query!, TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
            Assert.That(result.Reason, Is.EqualTo("null_dereference_reachable"));
        }

        [Test]
        public void Lowering_NonZeroGuard_ProvesDivideByZeroUnreachable()
        {
            var context = AnalyzerTestHost.CreateConditionImplicationContext("int divisor", "divisor != 0", "divisor == 0");
            var evidence = new AnalyzerPurityEvidence(
                AnalyzerPurityHazardKind.DivideByZero,
                new[] { context.PathCondition },
                context.Conclusion);

            var lowered = AnalyzerEvidenceToSearchLibLowering.TryLower(
                evidence,
                context.SemanticModel,
                CancellationToken.None,
                out var query);

            Assert.That(lowered, Is.True);
            using var search = new PurityProofSearch();
            var result = search.Classify(query!, TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("divide_by_zero_unreachable"));
        }

        [Test]
        public void Lowering_AffineContradictoryImpureCallPath_ProvesPure()
        {
            var context = AnalyzerTestHost.CreateConditionImplicationContext("int x", "x + 1 <= 0 && x >= 0", "true");
            var evidence = new AnalyzerPurityEvidence(
                AnalyzerPurityHazardKind.ImpureCallReachability,
                new[] { context.PathCondition },
                context.Conclusion);

            var lowered = AnalyzerEvidenceToSearchLibLowering.TryLower(
                evidence,
                context.SemanticModel,
                CancellationToken.None,
                out var query);

            Assert.That(lowered, Is.True);
            using var search = new PurityProofSearch();
            var result = search.Classify(query!, TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void Lowering_AffineGuard_ProvesDivideByZeroUnreachable()
        {
            var context = AnalyzerTestHost.CreateConditionImplicationContext("int divisor", "divisor + 1 != 1", "divisor == 0");
            var evidence = new AnalyzerPurityEvidence(
                AnalyzerPurityHazardKind.DivideByZero,
                new[] { context.PathCondition },
                context.Conclusion);

            var lowered = AnalyzerEvidenceToSearchLibLowering.TryLower(
                evidence,
                context.SemanticModel,
                CancellationToken.None,
                out var query);

            Assert.That(lowered, Is.True);
            using var search = new PurityProofSearch();
            var result = search.Classify(query!, TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("divide_by_zero_unreachable"));
        }
    }
}
