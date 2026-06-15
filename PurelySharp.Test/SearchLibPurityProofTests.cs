using NUnit.Framework;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public class SearchLibPurityProofTests
    {
        [Test]
        public void PurityProof_FalseImpurityCondition_IsProvablyPure()
        {
            using var search = new PurityProofSearch();

            var result = search.Classify(
                new SmtBooleanConstant(false),
                TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("impurity_unreachable"));
        }

        [Test]
        public void PurityProof_TrueImpurityCondition_IsProvablyImpure()
        {
            using var search = new PurityProofSearch();

            var result = search.Classify(
                new SmtBooleanConstant(true),
                TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
            Assert.That(result.Reason, Is.EqualTo("impurity_reachable"));
        }

        [Test]
        public void PurityProof_ContradictoryPath_IsProvablyPure()
        {
            using var search = new PurityProofSearch();
            var x = new SmtVariable("x", SmtValueKind.Int);

            var result = search.Classify(
                new SmtFormula[]
                {
                    new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, x, new SmtIntegerConstant(0)),
                    new SmtBinaryFormula(SmtBinaryOperator.LessThan, x, new SmtIntegerConstant(0)),
                },
                new SmtBooleanConstant(true),
                TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("path_unsatisfiable"));
        }

        [Test]
        public void PurityProof_ReachableImpurityGuard_IsProvablyImpure()
        {
            using var search = new PurityProofSearch();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

            var result = search.Classify(
                new[] { xIsZero },
                xIsZero,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyImpure));
            Assert.That(result.Reason, Is.EqualTo("impurity_reachable"));
        }
    }
}
