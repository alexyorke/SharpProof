using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class Pr275NestedCSharpStructuralCandidateTests
{
    [TestCaseSource(nameof(NestedExpressions))]
    public void ActualNestedCandidatesRetainCompiledCSharpAndIrAgreement(
        string name, GeneratedCSharpExpression expression)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(name);
        var candidates = CSharpStructuralShrinker.GetCandidates(expression);
        Assert.That(candidates, Is.Not.Empty, name);
        Assert.That(candidates.All(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Type == expression.Type)), Is.True, name);
        Assert.That(candidates.All(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.NodeCount < expression.NodeCount)), Is.True, name);
        Assert.That(candidates.Any(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Kind == expression.Kind)), Is.True, name);
        Assert.That(candidates.Select(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Render())).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(candidates.Length), name);
        var cases = candidates.Prepend(expression).Select(candidate =>
            candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (new GeneratedCSharpCase(candidate, 1, 2, true)
            {
                Text = "proof",
                Values = [2L, 3L],
                Reference = "proof"
            })).ToImmutableArray();
        var results = FrontendDifferentialOracle.CompareBatch(cases, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(results, Has.Length.EqualTo(cases.Length));
        for (var index = 0; index < results.Length; index++)
        {
            Assert.That(results[index].Status, Is.EqualTo(FuzzOracleStatus.Agreement),
                name + ": " + cases[index].Expression.Render() + " -- " + results[index].Detail);
        }
    }

    private static IEnumerable<TestCaseData> NestedExpressions()
    {
        var left = GeneratedCSharpExpression.Left();
        var right = GeneratedCSharpExpression.Right();
        var condition = GeneratedCSharpExpression.Condition();
        var integer = GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Add, left,
            GeneratedCSharpExpression.Integer(1));
        var other = GeneratedCSharpExpression.Binary(GeneratedExpressionKind.Multiply, right,
            GeneratedCSharpExpression.Integer(2));
        var boolean = GeneratedCSharpExpression.Binary(GeneratedExpressionKind.AndAlso, condition,
            GeneratedCSharpExpression.Boolean(true));
        var otherBoolean = GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Not, condition);
        var text = GeneratedCSharpExpression.Binary(GeneratedExpressionKind.StringConcat,
            GeneratedCSharpExpression.Text(), GeneratedCSharpExpression.String("!"));
        var otherText = GeneratedCSharpExpression.Conditional(condition, GeneratedCSharpExpression.Text(),
            GeneratedCSharpExpression.String("other"));
        var sequence = GeneratedCSharpExpression.Conditional(boolean, GeneratedCSharpExpression.Values(),
            GeneratedCSharpExpression.Values());
        var reference = GeneratedCSharpExpression.Conditional(boolean, GeneratedCSharpExpression.Reference(),
            GeneratedCSharpExpression.NullReference());
        yield return Case("NestedNot", GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Not, otherBoolean));
        yield return Case("NestedNegate", GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Negate,
            GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Negate, left)));
        yield return Case("NestedLength", GeneratedCSharpExpression.Length(text));
        yield return Case("NestedArrayIndex", GeneratedCSharpExpression.ArrayIndex(sequence, integer));
        yield return Case("NestedCast", GeneratedCSharpExpression.CastToString(reference));
        yield return Case("NestedIntegerConditional", GeneratedCSharpExpression.Conditional(boolean, integer, other));
        yield return Case("NestedStringConditional", GeneratedCSharpExpression.Conditional(boolean, text, otherText));
        foreach (var kind in ImmutableArray.Create(
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
            GeneratedExpressionKind.GreaterThanOrEqual))
        {
            yield return Case("Nested" + kind, GeneratedCSharpExpression.Binary(kind, integer, other));
        }
        yield return Case("NestedAndAlso", GeneratedCSharpExpression.Binary(GeneratedExpressionKind.AndAlso,
            boolean, otherBoolean));
        yield return Case("NestedOrElse", GeneratedCSharpExpression.Binary(GeneratedExpressionKind.OrElse,
            boolean, otherBoolean));
        yield return Case("NestedStringConcat", GeneratedCSharpExpression.Binary(GeneratedExpressionKind.StringConcat,
            text, otherText));
    }

    private static TestCaseData Case(string name, GeneratedCSharpExpression expression)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(name);
        return new TestCaseData(name, expression).SetName(name);
    }
}
