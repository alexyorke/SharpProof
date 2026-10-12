using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class FrontendExpressionCoverageTests
{
    [Test]
    public void ExpressionFactoriesRejectMalformedTreesBeforeCompilation()
    {
        var integer = GeneratedCSharpExpression.Integer(1);
        var boolean = GeneratedCSharpExpression.Boolean(true);
        var text = GeneratedCSharpExpression.String("x");
        var values = GeneratedCSharpExpression.Values();

        using (Assert.EnterMultipleScope())
        {
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.String(null!), "value");
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.Length(null!), "value");
            AssertThrows<ArgumentException>(() => GeneratedCSharpExpression.Length(integer));
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.ArrayIndex(null!, integer), "sequence");
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.ArrayIndex(values, null!), "index");
            AssertThrows<ArgumentException>(() => GeneratedCSharpExpression.ArrayIndex(text, integer));
            AssertThrows<ArgumentException>(() => GeneratedCSharpExpression.ArrayIndex(values, boolean));
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.CastToString(null!), "value");
            AssertThrows<ArgumentException>(() => GeneratedCSharpExpression.CastToString(integer));
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Not, null!), "operand");
            AssertThrows<ArgumentOutOfRangeException>(
                () => GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Add, integer));
            AssertThrows<ArgumentException>(
                () => GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Not, integer));
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Add, null!, integer), "left");
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Add, integer, null!), "right");
            AssertThrows<ArgumentException>(() => GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Equal, integer, text));
            AssertThrows<ArgumentException>(() => GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Equal, boolean, boolean));
            AssertThrows<ArgumentException>(() => GeneratedCSharpExpression.Binary(GeneratedExpressionKind.StringConcat, text, integer));
            AssertThrows<ArgumentOutOfRangeException>(
                () => GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Not, integer, integer));
            AssertThrows<ArgumentException>(() => GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Add, integer, boolean));
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.Conditional(null!, integer, integer), "condition");
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.Conditional(boolean, null!, integer), "whenTrue");
            AssertParameter<ArgumentNullException>(
                () => GeneratedCSharpExpression.Conditional(boolean, integer, null!), "whenFalse");
            AssertThrows<ArgumentException>(() => GeneratedCSharpExpression.Conditional(integer, integer, integer));
            AssertThrows<ArgumentException>(() => GeneratedCSharpExpression.Conditional(boolean, integer, text));
        }
    }

    [Test]
    public void OracleAndShrinkerRejectMissingInputsAndPreserveEmptyBatch()
    {
        Assert.That(FrontendDifferentialOracle.CompareBatch([]), Is.Empty);
        Assert.That(FrontendDifferentialOracle.CompareSemanticEdges([]), Is.Empty);
        AssertParameter<ArgumentNullException>(
            () => FrontendDifferentialOracle.Compare(null!), "generated");
        AssertParameter<ArgumentNullException>(
            () => FrontendDifferentialOracle.CompareBatch(null!), "generatedCases");
        AssertParameter<ArgumentException>(
            () => FrontendDifferentialOracle.CompareBatch([null!]), "generatedCases");
        AssertParameter<ArgumentNullException>(
            () => FrontendDifferentialOracle.CompareSemanticEdges(null!), "cases");
        AssertParameter<ArgumentException>(
            () => FrontendDifferentialOracle.CompareSemanticEdges([null!]), "cases");
        AssertParameter<ArgumentOutOfRangeException>(
            () => new SmallCSharpCaseGenerator(1).Next(-1), "maximumDepth");
        AssertParameter<ArgumentNullException>(
            () => CSharpStructuralShrinker.GetCandidates(null!), "expression");
        AssertParameter<ArgumentNullException>(
            () => CSharpStructuralShrinker.Minimize(null!, _ => true), "generated");
        AssertParameter<ArgumentNullException>(
            () => CSharpStructuralShrinker.Minimize(
                Case(GeneratedCSharpExpression.Integer(1)), null!),
            "preservesMismatch");
    }

    [Test]
    public void SemanticEdgeOracleReportsInputAndClassificationErrorsPerCase()
    {
        var cases = new[]
        {
            new FrontendSemanticEdgeCase(
                "long", "long value", "value", [],
                FrontendSubsetDecision.Exact, FrontendAbstention.None),
            new FrontendSemanticEdgeCase(
                "long", "long value", "value", [7L],
                FrontendSubsetDecision.ClosedAbstention,
                FrontendAbstention.UnsupportedType),
            new FrontendSemanticEdgeCase(
                "long", "long value", "value", [7L],
                FrontendSubsetDecision.Exact, FrontendAbstention.None)
        };

        var results = FrontendDifferentialOracle.CompareSemanticEdges(cases);

        Assert.That(results, Has.Length.EqualTo(cases.Length));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
            Assert.That(results[0].Detail, Does.Contain("argument count"));
            Assert.That(results[1].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
            Assert.That(results[1].ActualDecision, Is.EqualTo(FrontendSubsetDecision.Exact));
            Assert.That(results[1].Detail, Does.Contain("lowering returned"));
            Assert.That(results[2].Status, Is.EqualTo(FuzzOracleStatus.Agreement), results[2].Detail);
        }
    }

    [Test]
    public void ConstantEvaluatorDistinguishesCompileTimeValuesFromRuntimeArithmetic()
    {
        var one = GeneratedCSharpExpression.Integer(1);
        var two = GeneratedCSharpExpression.Integer(2);
        var zero = GeneratedCSharpExpression.Integer(0);
        var cases = new (GeneratedCSharpExpression Expression, bool IsConstant, long Value)[]
        {
            (one, true, 1),
            (GeneratedCSharpExpression.Boolean(true), false, 0),
            (GeneratedCSharpExpression.Left(), false, 0),
            (GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Negate, two), true, -2),
            (GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Add, one, two), true, 3),
            (GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Subtract, one, two), true, -1),
            (GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Multiply, one, two), true, 2),
            (GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Divide, two, one), true, 2),
            (GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Remainder, two, one), true, 0),
            (GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Add,
                GeneratedCSharpExpression.Left(), one), false, 0),
            (GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Add,
                GeneratedCSharpExpression.Integer(long.MaxValue), one), false, 0),
            (GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Divide, one, zero), false, 0),
            (GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Negate,
                GeneratedCSharpExpression.Integer(long.MinValue)), false, 0),
            (GeneratedCSharpExpression.Conditional(
                GeneratedCSharpExpression.Unary(
                    GeneratedExpressionKind.Not,
                    GeneratedCSharpExpression.Boolean(false)),
                one,
                two), true, 1),
            (GeneratedCSharpExpression.Conditional(
                GeneratedCSharpExpression.Binary(
                    GeneratedExpressionKind.AndAlso,
                    GeneratedCSharpExpression.Boolean(false),
                    GeneratedCSharpExpression.Boolean(true)),
                one,
                two), true, 2),
            (GeneratedCSharpExpression.Conditional(
                GeneratedCSharpExpression.Binary(
                    GeneratedExpressionKind.OrElse,
                    GeneratedCSharpExpression.Boolean(false),
                    GeneratedCSharpExpression.Boolean(true)),
                one,
                two), true, 1)
        };

        foreach (var (expression, isConstant, expected) in cases)
        {
            var actual = expression.TryEvaluateIntegerConstant(out var value);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(actual, Is.EqualTo(isConstant), expression.Render());
                Assert.That(value, Is.EqualTo(expected), expression.Render());
            }
        }
    }

    [Test]
    public void EveryGeneratedExpressionFamilyAgreesWithCompiledCSharp()
    {
        var integer = GeneratedCSharpExpression.Left();
        var other = GeneratedCSharpExpression.Right();
        var boolean = GeneratedCSharpExpression.Condition();
        var text = GeneratedCSharpExpression.Text();
        var values = GeneratedCSharpExpression.Values();
        var reference = GeneratedCSharpExpression.Reference();
        var cases = new List<GeneratedCSharpCase>
        {
            Case(GeneratedCSharpExpression.Boolean(true)),
            Case(GeneratedCSharpExpression.Integer(7)),
            Case(integer),
            Case(other),
            Case(boolean),
            Case(text),
            Case(GeneratedCSharpExpression.String("sharp\nproof")),
            Case(GeneratedCSharpExpression.NullString()),
            Case(GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Not, boolean)),
            Case(GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Negate, integer)),
            Case(GeneratedCSharpExpression.Length(text)),
            Case(GeneratedCSharpExpression.Length(values)),
            Case(GeneratedCSharpExpression.ArrayIndex(values, GeneratedCSharpExpression.Integer(1))),
            Case(GeneratedCSharpExpression.CastToString(reference)),
            Case(GeneratedCSharpExpression.CastToString(GeneratedCSharpExpression.NullReference())),
            Case(GeneratedCSharpExpression.Conditional(boolean, integer, other)),
            Case(GeneratedCSharpExpression.Conditional(boolean, text, GeneratedCSharpExpression.NullString())),
            Case(GeneratedCSharpExpression.Binary(GeneratedExpressionKind.StringConcat, text, GeneratedCSharpExpression.String("!")))
        };

        foreach (var kind in new[]
        {
            GeneratedExpressionKind.Add,
            GeneratedExpressionKind.Subtract,
            GeneratedExpressionKind.Multiply,
            GeneratedExpressionKind.Divide,
            GeneratedExpressionKind.Remainder,
            GeneratedExpressionKind.LessThan,
            GeneratedExpressionKind.LessThanOrEqual,
            GeneratedExpressionKind.GreaterThan,
            GeneratedExpressionKind.GreaterThanOrEqual,
            GeneratedExpressionKind.Equal,
            GeneratedExpressionKind.NotEqual
        })
        {
            cases.Add(Case(GeneratedCSharpExpression.Binary(kind, integer, other)));
        }

        foreach (var kind in new[]
        {
            GeneratedExpressionKind.AndAlso,
            GeneratedExpressionKind.OrElse
        })
        {
            cases.Add(Case(GeneratedCSharpExpression.Binary(kind, boolean, GeneratedCSharpExpression.Boolean(false))));
        }

        cases.Add(Case(GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Equal, text, GeneratedCSharpExpression.String("proof"))));
        cases.Add(Case(GeneratedCSharpExpression.Binary(
            GeneratedExpressionKind.NotEqual,
            reference,
            GeneratedCSharpExpression.NullReference())));

        var results = FrontendDifferentialOracle.CompareBatch(cases);

        Assert.That(results, Has.Length.EqualTo(cases.Count));
        for (var index = 0; index < cases.Count; index++)
        {
            Assert.That(
                results[index].Status,
                Is.EqualTo(FuzzOracleStatus.Agreement),
                $"Case {index}: {cases[index].Expression.Render()} -- {results[index].Detail}");
        }
    }

    [Test]
    public void StructuralCandidatesStayWellTypedAndMatchCompiledCSharp()
    {
        var integer = GeneratedCSharpExpression.Left();
        var other = GeneratedCSharpExpression.Right();
        var boolean = GeneratedCSharpExpression.Condition();
        var text = GeneratedCSharpExpression.Text();
        var values = GeneratedCSharpExpression.Values();
        var parents = new List<GeneratedCSharpExpression>
        {
            GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Not, boolean),
            GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Negate, integer),
            GeneratedCSharpExpression.Conditional(boolean, integer, other),
            GeneratedCSharpExpression.Length(text),
            GeneratedCSharpExpression.ArrayIndex(values, integer),
            GeneratedCSharpExpression.CastToString(GeneratedCSharpExpression.Reference()),
            GeneratedCSharpExpression.Binary(GeneratedExpressionKind.StringConcat, text, GeneratedCSharpExpression.String("!"))
        };
        foreach (var kind in new[]
        {
            GeneratedExpressionKind.Add,
            GeneratedExpressionKind.Subtract,
            GeneratedExpressionKind.Multiply,
            GeneratedExpressionKind.Divide,
            GeneratedExpressionKind.Remainder,
            GeneratedExpressionKind.Equal,
            GeneratedExpressionKind.NotEqual,
            GeneratedExpressionKind.LessThan,
            GeneratedExpressionKind.LessThanOrEqual,
            GeneratedExpressionKind.GreaterThan,
            GeneratedExpressionKind.GreaterThanOrEqual
        })
        {
            parents.Add(GeneratedCSharpExpression.Binary(kind, integer, other));
        }
        parents.Add(GeneratedCSharpExpression.Binary(GeneratedExpressionKind.AndAlso, boolean, GeneratedCSharpExpression.Boolean(true)));
        parents.Add(GeneratedCSharpExpression.Binary(GeneratedExpressionKind.OrElse, boolean, GeneratedCSharpExpression.Boolean(false)));

        var candidates = parents.SelectMany(parent =>
            CSharpStructuralShrinker.GetCandidates(parent)
                .Where(candidate => candidate.NodeCount < parent.NodeCount)
                .Select(candidate => (Parent: parent, Candidate: candidate)))
            .ToArray();
        Assert.That(candidates, Is.Not.Empty);
        Assert.That(candidates.Select(item => item.Parent.Kind).Distinct().Count(), Is.EqualTo(parents.Count));
        Assert.That(candidates.All(item => item.Parent.Type == item.Candidate.Type), Is.True);

        var cases = candidates.Select(item => Case(item.Candidate)).ToArray();
        var results = FrontendDifferentialOracle.CompareBatch(cases);
        Assert.That(results, Has.Length.EqualTo(cases.Length));
        for (var index = 0; index < cases.Length; index++)
        {
            Assert.That(results[index].Status, Is.EqualTo(FuzzOracleStatus.Agreement),
                $"Candidate {index}: {cases[index].Expression.Render()} -- {results[index].Detail}");
        }
    }

    private static GeneratedCSharpCase Case(GeneratedCSharpExpression expression)
    {
        return new(expression, Left: 8, Right: 2, Condition: true)
        {
            Text = "proof",
            Values = [3, 5, 7],
            Reference = "proof"
        };
    }

    private static void AssertParameter<TException>(
        Action action,
        string name)
        where TException : ArgumentException
    {
        Assert.That(Assert.Throws<TException>(action)!.ParamName, Is.EqualTo(name));
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        Assert.Throws<TException>(action);
    }
}
