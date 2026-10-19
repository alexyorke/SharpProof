using System.Collections.Immutable;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class FrontendFailureRetentionTests
{
    [Test]
    public void ActualSequenceMismatchIsRetainedAndMinimized()
    {
        var original = Case(GeneratedCSharpExpression.Binary(
            GeneratedExpressionKind.Equal,
            GeneratedCSharpExpression.Values(),
            Compound(GeneratedCSharpExpression.Values(), GeneratedCSharpExpression.Values())));
        var observed = FrontendDifferentialOracle.Compare(original, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(observed.Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        Assert.That(observed.Detail, Does.Contain("UnsupportedCast"));
        VerifyRetention(original, observed, 0);
    }

    [Test]
    public void EntireFrozenBatchRetainsEveryActualFailureWithoutChangingItsOutcome()
    {
        var cases = FrozenRecursiveCases();
        var results = FrontendDifferentialOracle.CompareBatch(cases, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(cases, Has.Count.EqualTo(269));
        Assert.That(results, Has.Length.EqualTo(cases.Count));
        var knownFailures = ImmutableHashSet.Create(StringComparer.Ordinal,
            "(values == (condition ? values : values))",
            "((condition ? values : values) == values)",
            "(values != (condition ? values : values))",
            "((condition ? values : values) != values)");
        var semanticFailures = 0;
        var agreements = 0;
        for (var index = 0; index < cases.Count; index++)
        {
            var observed = results[index];
            var expression = cases[index].Expression.Render();
            Assert.That(observed.Status, Is.EqualTo(knownFailures.Contains(expression)
                ? FuzzOracleStatus.Mismatch : FuzzOracleStatus.Agreement), expression);
            if (observed.Status == FuzzOracleStatus.Agreement)
            {
                agreements++;
            }
            else
            {
                Assert.That(observed.Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
                Assert.That(FuzzRunner.IsCompilationFailure(observed), Is.False);
                Assert.That(observed.Detail, Is.EqualTo(
                    "Compiled C# returned normally while the lowered IR reported unsupported UnsupportedCast."));
                Assert.That(observed.ExceptionKind, Is.Null);
                VerifyRetention(cases[index], observed, index);
                semanticFailures++;
            }
        }
        Assert.That(agreements, Is.EqualTo(265));
        Assert.That(semanticFailures, Is.EqualTo(4));
        var known = cases.FindIndex(generated => generated is null ? throw new System.ArgumentNullException(nameof(generated)) : (generated.Expression.Render() ==
            "(values == (condition ? values : values))"));
        Assert.That(known, Is.GreaterThanOrEqualTo(0));
        Assert.That(results[known].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        Assert.That(results[known].Detail, Does.Contain("UnsupportedCast"));
        var census = JsonSerializer.Serialize(new
        {
            cases = cases.Count,
            agreements,
            semanticFailures,
            sources = cases.Select(generated => generated is null ? throw new System.ArgumentNullException(nameof(generated)) : (generated.Source)).ToImmutableArray(),
            outcomes = results
        });
        var evidenceRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT")
            ?? Directory.GetCurrentDirectory();
        var evidenceDirectory = Path.Combine(evidenceRoot, "artifacts", "fuzz-retention");
        Directory.CreateDirectory(evidenceDirectory);
        File.WriteAllText(Path.Combine(evidenceDirectory, "whole-family.json"), census);
    }

    [Test]
    public void RealCompilationFailureRetainsOriginalAndAgreementIsNotAFailure()
    {
        var valid = Case(GeneratedCSharpExpression.Integer(0));
        var invalid = Case(GeneratedCSharpExpression.Binary(
            GeneratedExpressionKind.Add, GeneratedCSharpExpression.Integer(long.MaxValue),
            GeneratedCSharpExpression.Integer(1)));
        var observed = FrontendDifferentialOracle.CompareBatch([valid, invalid], cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(observed[0].Status, Is.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(observed[1].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        Assert.That(FuzzRunner.IsCompilationFailure(observed[1]), Is.True);
        Assert.That(observed[1].Detail, Does.Contain("CS0220"));
        Assert.Throws<ArgumentException>((Action)(() => FrontendFailureRetention.Retain(valid, observed[0], 0, 23063, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.That(Assert.Throws<ArgumentNullException>((Action)(() =>
            FrontendFailureRetention.Retain(null!, observed[1], 1, 23460, cancellationToken: TestContext.CurrentContext.CancellationToken)))!.ParamName,
            Is.EqualTo("original"));
        Assert.That(Assert.Throws<ArgumentNullException>((Action)(() =>
            FrontendFailureRetention.Retain(invalid, null!, 1, 23460, cancellationToken: TestContext.CurrentContext.CancellationToken)))!.ParamName,
            Is.EqualTo("originalResult"));
        VerifyRetention(invalid, observed[1], 1);
    }

    private static void VerifyRetention(GeneratedCSharpCase original,
        FrontendDifferentialResult observed, int index)
    {
        System.ArgumentNullException.ThrowIfNull(observed);
        System.ArgumentNullException.ThrowIfNull(original);
        var seed = unchecked(23063 + index * 397);
        var baseline = OriginalRetention(original, observed, index, seed);
        var candidate = FrontendFailureRetention.Retain(original, observed, index, seed, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(candidate.Failure, Is.EqualTo(baseline.Failure));
        Assert.That(candidate.MinimizedCase.Source, Is.EqualTo(baseline.MinimizedCase.Source));
        Assert.That(candidate.MinimizedResult, Is.EqualTo(baseline.MinimizedResult));
        Assert.That(candidate.OriginalCase, Is.SameAs(original));
        Assert.That(candidate.OriginalResult, Is.SameAs(observed));
        Assert.That(candidate.Failure.Original, Is.EqualTo(original.Source));
        Assert.That(candidate.Failure.Case, Is.EqualTo(index));
        Assert.That(candidate.Failure.Seed, Is.EqualTo(seed));
        Assert.That(candidate.Failure.Oracle, Is.EqualTo("frontend"));
        Assert.That(candidate.Failure.Minimized, Is.EqualTo(candidate.MinimizedCase.Source));
        Assert.That(candidate.Failure.Term, Is.EqualTo(candidate.MinimizedCase.Source));
        Assert.That(candidate.Failure.Detail, Is.EqualTo(candidate.MinimizedResult.Detail));
        Assert.That(candidate.MinimizedCase.Expression.Type, Is.EqualTo(original.Expression.Type));
        Assert.That(candidate.MinimizedCase.Expression.NodeCount,
            Is.LessThanOrEqualTo(original.Expression.NodeCount));
        Assert.That(candidate.MinimizedCase.Left, Is.EqualTo(original.Left));
        Assert.That(candidate.MinimizedCase.Right, Is.EqualTo(original.Right));
        Assert.That(candidate.MinimizedCase.Condition, Is.EqualTo(original.Condition));
        Assert.That(candidate.MinimizedCase.Text, Is.EqualTo(original.Text));
        Assert.That(candidate.MinimizedCase.Values, Is.SameAs(original.Values));
        Assert.That(candidate.MinimizedCase.Reference, Is.SameAs(original.Reference));
        var replay = FrontendDifferentialOracle.Compare(candidate.MinimizedCase, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(replay, Is.EqualTo(candidate.MinimizedResult));
        if (FuzzRunner.IsCompilationFailure(observed))
        {
            Assert.That(candidate.MinimizedCase, Is.SameAs(original));
            Assert.That(FuzzRunner.IsCompilationFailure(replay), Is.True);
        }
        else
        {
            Assert.That(FuzzRunner.IsSemanticMismatch(replay), Is.True);
            foreach (var smaller in CSharpStructuralShrinker.GetCandidates(candidate.MinimizedCase.Expression))
            {
                var result = FrontendDifferentialOracle.Compare(candidate.MinimizedCase with { Expression = smaller }, cancellationToken: TestContext.CurrentContext.CancellationToken);
                Assert.That(FuzzRunner.IsSemanticMismatch(result), Is.False, smaller.Render());
            }
        }
    }

    // Frozen pre-extraction frontend branch: its real oracle is the baseline.
    private static RetainedFrontendFailure OriginalRetention(GeneratedCSharpCase original,
        FrontendDifferentialResult observed, int index, int seed)
    {
        System.ArgumentNullException.ThrowIfNull(observed);
        System.ArgumentNullException.ThrowIfNull(original);
        var minimized = FuzzRunner.IsCompilationFailure(observed)
            ? original
            : CSharpStructuralShrinker.Minimize(original,
                candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (FuzzRunner.IsSemanticMismatch(FrontendDifferentialOracle.Compare(candidate, cancellationToken: TestContext.CurrentContext.CancellationToken))), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = FrontendDifferentialOracle.Compare(minimized, cancellationToken: TestContext.CurrentContext.CancellationToken);
        return new(original, minimized, observed, result,
            new(index, seed, "frontend", original.Source, minimized.Source, result.Detail));
    }

    private static GeneratedCSharpCase Case(GeneratedCSharpExpression expression)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        return new(expression, Left: 8, Right: 2, Condition: true)
        {
            Text = "proof",
            Values = [3, 5, 7],
            Reference = "proof"
        };
    }

    private static GeneratedCSharpExpression Compound(
        GeneratedCSharpExpression whenTrue, GeneratedCSharpExpression whenFalse)
    {
        System.ArgumentNullException.ThrowIfNull(whenFalse);
        System.ArgumentNullException.ThrowIfNull(whenTrue);
        return GeneratedCSharpExpression.Conditional(
            GeneratedCSharpExpression.Condition(), whenTrue, whenFalse);
    }
    private static List<GeneratedCSharpCase> FrozenRecursiveCases()
    {
        var integer = Compound(GeneratedCSharpExpression.Left(), GeneratedCSharpExpression.Right());
        var boolean = Compound(GeneratedCSharpExpression.Condition(), GeneratedCSharpExpression.Boolean(false));
        var text = Compound(GeneratedCSharpExpression.Text(), GeneratedCSharpExpression.String("!"));
        var sequence = Compound(GeneratedCSharpExpression.Values(), GeneratedCSharpExpression.Values());
        var reference = Compound(GeneratedCSharpExpression.Reference(), GeneratedCSharpExpression.NullReference());
        var parents = new List<GeneratedCSharpExpression>
        {
            GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Not, boolean),
            GeneratedCSharpExpression.Unary(GeneratedExpressionKind.Negate, integer),
            GeneratedCSharpExpression.Conditional(boolean, integer, integer),
            GeneratedCSharpExpression.Length(text),
            GeneratedCSharpExpression.Length(sequence),
            GeneratedCSharpExpression.ArrayIndex(sequence, integer),
            GeneratedCSharpExpression.CastToString(reference),
            GeneratedCSharpExpression.Binary(GeneratedExpressionKind.StringConcat, text, text),
            GeneratedCSharpExpression.Binary(GeneratedExpressionKind.AndAlso, boolean, boolean),
            GeneratedCSharpExpression.Binary(GeneratedExpressionKind.OrElse, boolean, boolean)
        };
        foreach (var kind in new[]
        {
            GeneratedExpressionKind.Add, GeneratedExpressionKind.Subtract,
            GeneratedExpressionKind.Multiply, GeneratedExpressionKind.Divide,
            GeneratedExpressionKind.Remainder, GeneratedExpressionKind.LessThan,
            GeneratedExpressionKind.LessThanOrEqual, GeneratedExpressionKind.GreaterThan,
            GeneratedExpressionKind.GreaterThanOrEqual
        })
        {
            parents.Add(GeneratedCSharpExpression.Binary(kind, integer, integer));
        }
        foreach (var kind in new[] { GeneratedExpressionKind.Equal, GeneratedExpressionKind.NotEqual })
        {
            foreach (var operand in new[] { integer, text, sequence, reference })
            {
                parents.Add(GeneratedCSharpExpression.Binary(kind, operand, operand));
            }
        }

        var cases = new List<GeneratedCSharpCase>();
        foreach (var parent in parents)
        {
            var candidates = CSharpStructuralShrinker.GetCandidates(parent);
            var childSources = parent.Children.Select(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (child.Render())).ToImmutableHashSet(StringComparer.Ordinal);
            Assert.That(candidates.Any(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Kind == parent.Kind &&
                !childSources.Contains(candidate.Render()))), Is.True, parent.Render());
            foreach (var candidate in candidates)
            {
                Assert.That(candidate.Type, Is.EqualTo(parent.Type));
                Assert.That(candidate.NodeCount, Is.LessThan(parent.NodeCount));
                cases.Add(new(candidate, Left: 8, Right: 2, Condition: true)
                {
                    Text = "proof",
                    Values = [3, 5, 7],
                    Reference = "proof"
                });
            }
        }
        Assert.That(parents.Select(parent => parent is null ? throw new System.ArgumentNullException(nameof(parent)) : (parent.Kind)).Distinct().Count(), Is.EqualTo(20));
        return cases;
    }
}
