using NUnit.Framework;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Test
{
    [TestFixture]
    public class SearchLibZ3SmokeTests
    {
        [Test]
        public void SmtSolver_TrueAndFalseConjunction_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtBooleanConstant(true),
                    new SmtBooleanConstant(false),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_NonZeroGuardDoesNotImplyZero_IsSatisfiable()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xNotZero = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, x, new SmtIntegerConstant(0));
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

            var result = solver.Implies(
                new[] { xNotZero },
                xIsZero,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Satisfiable));
        }

        [Test]
        public void SmtSolver_ZeroGuardImpliesNotZero_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));
            var xNotZero = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, x, new SmtIntegerConstant(0));

            var result = solver.Implies(
                new[] { xIsZero },
                xNotZero,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void PurityProof_NonNullGuard_MakesNullDereferenceProvablyPure()
        {
            using var search = new PurityProofSearch();
            var s = new SmtVariable("s", SmtValueKind.Reference);
            var sIsNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, s, new SmtNullConstant());
            var sIsNotNull = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, s, new SmtNullConstant());

            var result = search.ClassifyNullDereference(
                new[] { sIsNotNull },
                sIsNull,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result.Outcome, Is.EqualTo(PurityProofOutcome.ProvablyPure));
            Assert.That(result.Reason, Is.EqualTo("null_dereference_unreachable"));
        }
    }
}
