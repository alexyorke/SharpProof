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
        public void SmtSolver_ZeroGuardImpliesZero_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xIsZero = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(0));

            var result = solver.Implies(
                new[] { xIsZero },
                xIsZero,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_AffineEqualityAndConflictingInequality_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xPlusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, x, new SmtIntegerConstant(1));
            var affineEquality = new SmtBinaryFormula(SmtBinaryOperator.Equal, xPlusOne, new SmtIntegerConstant(0));
            var xIsNonNegative = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, x, new SmtIntegerConstant(0));

            var result = solver.IsSatisfiable(
                new SmtFormula[] { affineEquality, xIsNonNegative },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_UnsupportedRegexWithoutConcreteInput_ReturnsUnknown()
        {
            using var solver = new SmtSolver();
            var text = new SmtVariable("text", SmtValueKind.String);

            var result = solver.IsSatisfiable(
                new SmtFormula[]
                {
                    new SmtRegexMatchFormula(text, "("),
                },
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unknown));
        }

        [Test]
        public void SmtSolver_AffineGuardImpliesExactValue_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var x = new SmtVariable("x", SmtValueKind.Int);
            var xMinusOne = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, x, new SmtIntegerConstant(1));
            var guard = new SmtBinaryFormula(SmtBinaryOperator.Equal, xMinusOne, new SmtIntegerConstant(0));
            var conclusion = new SmtBinaryFormula(SmtBinaryOperator.Equal, x, new SmtIntegerConstant(1));

            var result = solver.Implies(
                new[] { guard },
                conclusion,
                TimeSpan.FromMilliseconds(50));

            Assert.That(result, Is.EqualTo(Feasibility.Unsatisfiable));
        }

        [Test]
        public void SmtSolver_ReferenceNullAndNonNullConjunction_IsUnsatisfiable()
        {
            using var solver = new SmtSolver();
            var reference = new SmtVariable("reference", SmtValueKind.Reference);
            var isNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, reference, new SmtNullConstant());
            var isNotNull = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, reference, new SmtNullConstant());

            var result = solver.IsSatisfiable(
                new SmtFormula[] { isNull, isNotNull },
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
