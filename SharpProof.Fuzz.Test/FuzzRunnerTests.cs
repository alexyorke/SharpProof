using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Fuzz;
using SharpProof.Verify;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class FuzzRunnerTests
{
    [Test]
    public void FailureEvidenceRetentionUsesDeterministicBoundedKeys()
    {
        var statuses = Enumerable.Repeat(
                FuzzOracleStatus.Mismatch,
                FuzzRunner.MaximumRetainedFailures)
            .ToArray();
        var keys = FuzzRunner.SelectFailureKeys(
            statuses,
            statuses,
            statuses);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                keys.Length,
                Is.EqualTo(FuzzRunner.MaximumRetainedFailures));
            Assert.That(
                keys.Take(3),
                Is.EqualTo(new[]
                {
                    new FuzzFailureKey(0, "finite-domain-smt"),
                    new FuzzFailureKey(0, "frontend"),
                    new FuzzFailureKey(0, "partial-term-smt")
                }));
            Assert.That(
                keys[^1],
                Is.EqualTo(new FuzzFailureKey(
                    21,
                    "finite-domain-smt")));
        }
    }

    [Test]
    public void PartialAbstentionIsNotClassifiedAsMismatchEvidence()
    {
        var classification = FuzzRunner.ClassifyCase(
            FuzzOracleStatus.Agreement,
            FuzzOracleStatus.Agreement,
            FuzzOracleStatus.Abstained);
        var keys = FuzzRunner.SelectFailureKeys(
            new[] { FuzzOracleStatus.Agreement },
            new[] { FuzzOracleStatus.Agreement },
            new[] { FuzzOracleStatus.Abstained });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(classification.HasMismatch, Is.False);
            Assert.That(classification.HasAbstention, Is.True);
            Assert.That(keys, Is.Empty);
        }
    }

    [Test]
    public async Task FixedSeedIsDeterministicAndSound()
    {
        var options = new FuzzOptions(Cases: 24, Seed: 12345, MaximumParallelism: 4);

        var first = await FuzzRunner.RunAsync(options);
        var second = await FuzzRunner.RunAsync(options);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first.SchemaVersion, Is.EqualTo(4));
        Assert.That(first.Passed, Is.True);
        Assert.That(first.Agreements, Is.EqualTo(options.Cases));
        Assert.That(first.Abstentions, Is.Zero);
        Assert.That(first.FrontendAgreements, Is.EqualTo(options.Cases));
        Assert.That(first.SmtAgreements, Is.EqualTo(options.Cases));
        Assert.That(
            first.PartialSmtAgreements,
            Is.EqualTo(options.Cases));
        Assert.That(first.FrontendCoverage.HasExpandedCategories, Is.True);
        Assert.That(first.FrontendCoverage.DivideByZeroExceptions, Is.GreaterThan(0));
        Assert.That(first.FrontendCoverage.OverflowExceptions, Is.GreaterThan(0));
        Assert.That(first.FrontendCoverage.InvalidCastExceptions, Is.GreaterThan(0));
    }

    [Test]
    public async Task ParallelismDoesNotChangeDeterministicOutcomes()
    {
        var serial = await FuzzRunner.RunAsync(
            new FuzzOptions(Cases: 16, Seed: -9876, MaximumParallelism: 1));
        var parallel = await FuzzRunner.RunAsync(
            new FuzzOptions(Cases: 16, Seed: -9876, MaximumParallelism: 4));

        Assert.That(serial.Passed, Is.True);
        Assert.That(parallel.Passed, Is.True);
        Assert.That(parallel.Agreements, Is.EqualTo(serial.Agreements));
        Assert.That(parallel.Abstentions, Is.EqualTo(serial.Abstentions));
        Assert.That(
            parallel.FrontendAgreements,
            Is.EqualTo(serial.FrontendAgreements));
        Assert.That(parallel.SmtAgreements, Is.EqualTo(serial.SmtAgreements));
        Assert.That(
            parallel.PartialSmtAgreements,
            Is.EqualTo(serial.PartialSmtAgreements));
        Assert.That(parallel.Failures, Is.EqualTo(serial.Failures));
    }

    [Test]
    public void SupportedDomainAbstentionFailsTheCampaign()
    {
        var coverage = new FrontendFuzzCoverage(
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        var summary = new FuzzSummary(
            SchemaVersion: 4,
            Cases: 1,
            Seed: 7,
            MaximumParallelism: 1,
            Agreements: 0,
            Abstentions: 1,
            FrontendAgreements: 1,
            SmtAgreements: 1,
            PartialSmtAgreements: 1,
            FrontendCoverage: coverage,
            CoverageSatisfied: true,
            Failures: []);

        Assert.That(summary.Passed, Is.False);
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(1, 5)]
    public void InvalidSummaryOptionsDoNotPass(
        int cases,
        int maximumParallelism)
    {
        var coverage = new FrontendFuzzCoverage(
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        var summary = new FuzzSummary(
            SchemaVersion: 4,
            Cases: cases,
            Seed: 7,
            MaximumParallelism: maximumParallelism,
            Agreements: cases,
            Abstentions: 0,
            FrontendAgreements: cases,
            SmtAgreements: cases,
            PartialSmtAgreements: cases,
            FrontendCoverage: coverage,
            CoverageSatisfied: true,
            Failures: []);

        Assert.That(summary.Passed, Is.False);
    }

    [Test]
    public void MalformedSummaryEvidenceDoesNotPass()
    {
        var complete = new FrontendFuzzCoverage(
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        var empty = new FrontendFuzzCoverage(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var negative = empty with { TextParameters = -1 };
        var impossibleExceptions = empty with
        {
            DivideByZeroExceptions = 1,
            OverflowExceptions = 1
        };
        var valid = new FuzzSummary(
            SchemaVersion: 4,
            Cases: FuzzOptions.DefaultCases,
            Seed: 7,
            MaximumParallelism: 1,
            Agreements: FuzzOptions.DefaultCases,
            Abstentions: 0,
            FrontendAgreements: FuzzOptions.DefaultCases,
            SmtAgreements: FuzzOptions.DefaultCases,
            PartialSmtAgreements: FuzzOptions.DefaultCases,
            FrontendCoverage: complete,
            CoverageSatisfied: true,
            Failures: []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(valid.Passed, Is.True);
            Assert.That(
                (valid with { SchemaVersion = 999 }).Passed,
                Is.False);
            Assert.That(
                (valid with { Failures = default }).Passed,
                Is.False);
            Assert.That(
                (valid with { FrontendCoverage = null! }).Passed,
                Is.False);
            Assert.That(
                (valid with { FrontendCoverage = empty }).Passed,
                Is.False);
            Assert.That(
                (valid with
                {
                    Cases = 1,
                    Agreements = 1,
                    FrontendAgreements = 1,
                    SmtAgreements = 1,
                    PartialSmtAgreements = 1,
                    FrontendCoverage = negative
                }).Passed,
                Is.False);
            Assert.That(
                (valid with
                {
                    Cases = 1,
                    Agreements = 1,
                    FrontendAgreements = 1,
                    SmtAgreements = 1,
                    PartialSmtAgreements = 1,
                    FrontendCoverage = impossibleExceptions
                }).Passed,
                Is.False);
        }
    }

    [Test]
    public async Task CancellationPropagates()
    {
        var cancellation = new CancellationToken(canceled: true);
        try
        {
            await FuzzRunner.RunAsync(
                new FuzzOptions(Cases: 10, Seed: 1, MaximumParallelism: 1),
                cancellation);
            Assert.Fail("Expected cancellation to propagate.");
        }
        catch (OperationCanceledException)
        {
            Assert.Pass();
        }
    }

    [Test]
    public void GeneratedSourceFlowsThroughFrontendAndMatchesRuntime()
    {
        var first = new SmallCSharpCaseGenerator(seed: 741).Next(
            maximumDepth: 4);
        var second = new SmallCSharpCaseGenerator(seed: 741).Next(
            maximumDepth: 4);

        Assert.That(first.Source, Is.EqualTo(second.Source));
        Assert.That(first.Left, Is.EqualTo(second.Left));
        Assert.That(first.Right, Is.EqualTo(second.Right));
        Assert.That(first.Condition, Is.EqualTo(second.Condition));
        var comparison = new FrontendDifferentialOracle().Compare(first);
        Assert.That(
            comparison.Status,
            Is.EqualTo(FuzzOracleStatus.Agreement),
            comparison.Detail + Environment.NewLine + first.Source);
    }

    [Test]
    public void ExpandedFrontendShapesMatchRuntime()
    {
        var cases = new[] {
            new GeneratedCSharpCase(
                GeneratedCSharpExpression.Length(
                    GeneratedCSharpExpression.Text()),
                Left: 0,
                Right: 0,
                Condition: false) {
                Text = null
            },
            new GeneratedCSharpCase(
                GeneratedCSharpExpression.ArrayIndex(
                    GeneratedCSharpExpression.Values(),
                    GeneratedCSharpExpression.Left()),
                Left: 1,
                Right: 0,
                Condition: false) {
                Values = [7]
            },
            new GeneratedCSharpCase(
                GeneratedCSharpExpression.CastToString(
                    GeneratedCSharpExpression.NullReference()),
                Left: 0,
                Right: 0,
                Condition: false)
        };

        var results = new FrontendDifferentialOracle().CompareBatch(cases);

        Assert.That(
            results.Select(static result => result.Status),
            Is.All.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(
            results[0].ExceptionKind,
            Is.EqualTo(IrExceptionKind.NullReference));
        Assert.That(
            results[1].ExceptionKind,
            Is.EqualTo(IrExceptionKind.IndexOutOfRange));
        Assert.That(results[2].ExceptionKind, Is.Null);
    }

    [Test]
    public void FrontendBatchCompileFailureIsIsolatedToInvalidCase()
    {
        var valid = new GeneratedCSharpCase(
            GeneratedCSharpExpression.Integer(0),
            Left: 0,
            Right: 0,
            Condition: false);
        var invalid = new GeneratedCSharpCase(
            GeneratedCSharpExpression.Binary(
                GeneratedExpressionKind.Add,
                GeneratedCSharpExpression.Integer(long.MaxValue),
                GeneratedCSharpExpression.Integer(1)),
            Left: 0,
            Right: 0,
            Condition: false);

        var results = new FrontendDifferentialOracle()
            .CompareBatch([valid, invalid]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].Status, Is.EqualTo(FuzzOracleStatus.Agreement));
            Assert.That(results[1].Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
        }
    }

    [Test]
    public void ExpandedCoverageRequirementFailsClosed()
    {
        var empty = new FrontendFuzzCoverage(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.That(empty.HasExpandedCategories, Is.False);
    }

    [Test]
    public async Task FiniteDomainOracleChecksSatAndUnsatWithExplicitAssumptions()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var enabled = factory.CreateVariable("enabled", factory.BooleanType);
        var satisfiable = factory.Binary(
            IrBinaryOperator.AndAlso,
            factory.Binary(
                IrBinaryOperator.Equal,
                factory.Variable(value),
                factory.Integer(2)),
            factory.Variable(enabled));
        var unsatisfiable = factory.Binary(
            IrBinaryOperator.LessThan,
            factory.Variable(value),
            factory.Integer(-2));
        var oracle = new FiniteDomainSmtDifferentialOracle();

        var sat = await oracle.CompareAsync(factory, satisfiable);
        var unsat = await oracle.CompareAsync(factory, unsatisfiable);

        Assert.That(sat.Status, Is.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(
            sat.Expected,
            Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
        Assert.That(
            sat.Actual,
            Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
        Assert.That(sat.FiniteDomainAssumptions, Is.EqualTo(2));
        Assert.That(unsat.Status, Is.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(
            unsat.Expected,
            Is.EqualTo(FiniteDomainSatisfiability.Unsatisfiable));
        Assert.That(
            unsat.Actual,
            Is.EqualTo(FiniteDomainSatisfiability.Unsatisfiable));
        Assert.That(unsat.FiniteDomainAssumptions, Is.EqualTo(1));
    }

    [TestCase(0, 0, 1, 1)]
    [TestCase(7, 1, 0, 1)]
    public async Task PartialTermOracleChecksShortCircuitAndUndefinedArithmetic(
        int seed,
        int expectedTrue,
        int expectedFalse,
        int expectedUndefined)
    {
        var factory = new IrFactory();
        var generated = PartialTermSmtCaseGenerator.Create(factory, seed);

        var result = await new PartialTermSmtDifferentialOracle()
            .CompareAsync(factory, generated);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Status,
                Is.EqualTo(FuzzOracleStatus.Agreement),
                result.Detail);
            Assert.That(result.ScenarioCount, Is.EqualTo(2));
            Assert.That(result.DefinedTrueCount, Is.EqualTo(expectedTrue));
            Assert.That(result.DefinedFalseCount, Is.EqualTo(expectedFalse));
            Assert.That(result.UndefinedCount, Is.EqualTo(expectedUndefined));
        }
    }

    [Test]
    public async Task PartialTermOracleAbstainsOnGenericCounterexampleReplayFailure()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var goal = new Goal(
            factory,
            factory.Boolean(true),
            ProofDiagnosticKind.Postcondition,
            new SourceLocationId(0));
        var query = new VerificationQuery(factory, [], goal, [variable]);
        var outcome = await new ProofKernel(
                new StubBackend(BackendCheckResult.Satisfiable(new BackendModel([]))))
            .VerifyAsync(query);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
            Assert.That(
                ((UnknownOutcome)outcome).Reason,
                Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
            Assert.That(
                PartialTermSmtDifferentialOracle.Classify(outcome),
                Is.Null);
        }
    }

    private sealed class StubBackend(BackendCheckResult result) : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    [Test]
    public void CSharpShrinkerIsDeterministicAndPreservesMismatch()
    {
        var expression = GeneratedCSharpExpression.Binary(
            GeneratedExpressionKind.Add,
            GeneratedCSharpExpression.Conditional(
                GeneratedCSharpExpression.Condition(),
                GeneratedCSharpExpression.Left(),
                GeneratedCSharpExpression.Right()),
            GeneratedCSharpExpression.Integer(1));
        var generated = new GeneratedCSharpCase(
            expression,
            Left: 3,
            Right: 4,
            Condition: true);
        static bool Preserves(GeneratedCSharpCase candidate)
        {
            return candidate.Expression.Render().Contains(
                "left",
                StringComparison.Ordinal);
        }

        var first = CSharpStructuralShrinker.Minimize(generated, Preserves);
        var second = CSharpStructuralShrinker.Minimize(generated, Preserves);

        Assert.That(
            first.Expression.NodeCount,
            Is.LessThan(expression.NodeCount));
        Assert.That(Preserves(first), Is.True);
        Assert.That(first.Source, Is.EqualTo(second.Source));
    }

    [Test]
    public async Task IrShrinkerIsDeterministicAndPreservesMismatch()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var variableTerm = factory.Variable(variable);
        var formula = factory.Binary(
            IrBinaryOperator.AndAlso,
            factory.Binary(
                IrBinaryOperator.GreaterThan,
                factory.Binary(
                    IrBinaryOperator.Add,
                    variableTerm,
                    factory.Integer(1)),
                factory.Integer(0)),
            factory.Binary(
                IrBinaryOperator.LessThan,
                variableTerm,
                factory.Integer(2)));
        Task<bool> Preserves(IrTerm candidate, CancellationToken _)
        {
            return Task.FromResult(Contains(candidate, variable));
        }

        var first = await IrStructuralShrinker.MinimizeAsync(
            factory,
            formula,
            Preserves);
        var second = await IrStructuralShrinker.MinimizeAsync(
            factory,
            formula,
            Preserves);

        Assert.That(
            IrStructuralShrinker.StructuralSize(first),
            Is.LessThan(IrStructuralShrinker.StructuralSize(formula)));
        Assert.That(Contains(first, variable), Is.True);
        Assert.That(first, Is.SameAs(second));
    }

    [TestCase("--cases", "0")]
    [TestCase("--cases", "1000001")]
    [TestCase("--max-parallelism", "5")]
    [TestCase("--unknown", "1")]
    public void InvalidOptionsFailClosed(string option, string value)
    {
        try
        {
            FuzzOptions.Parse([option, value]);
            Assert.Fail("Expected invalid options to fail.");
        }
        catch (FuzzUsageException)
        {
            Assert.Pass();
        }
    }

    [TestCase(0, 1)]
    [TestCase(1000001, 1)]
    [TestCase(1, 0)]
    [TestCase(1, 5)]
    public void DirectRunnerRejectsInvalidOptions(
        int cases,
        int maximumParallelism)
    {
        Func<Task> run = () => FuzzRunner.RunAsync(new FuzzOptions(
            cases,
            Seed: 1,
            maximumParallelism));

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(run);
    }

    private static bool Contains(IrTerm term, IrVarId variable)
    {
        return term switch
        {
            IrVariableTerm item => item.Variable == variable,
            IrUnaryTerm unary => Contains(unary.Operand, variable),
            IrBinaryTerm binary =>
                Contains(binary.Left, variable) ||
                Contains(binary.Right, variable),
            IrConditionalTerm conditional =>
                Contains(conditional.Condition, variable) ||
                Contains(conditional.WhenTrue, variable) ||
                Contains(conditional.WhenFalse, variable),
            IrCastTerm cast => Contains(cast.Operand, variable),
            IrLengthTerm length => Contains(length.Value, variable),
            IrSequenceAccessTerm access =>
                Contains(access.Sequence, variable) ||
                Contains(access.Index, variable),
            IrOpaqueTerm opaque =>
                opaque.Receiver != null &&
                Contains(opaque.Receiver, variable) ||
                opaque.Arguments.Any(argument => Contains(argument, variable)),
            _ => false
        };
    }
}
