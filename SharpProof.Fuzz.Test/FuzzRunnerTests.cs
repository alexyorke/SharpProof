using NUnit.Framework;
using System.Text.Json;
using SharpProof.Ir;
using SharpProof.Fuzz;
using SharpProof.Testing;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class FuzzRunnerTests
{
    private static readonly string[] ExpectedAbstentionOracleOrder =
    [
        "frontend",
        "finite-domain-smt",
        "partial-term-smt"
    ];

    [Test]
    public void AbstentionEvidenceRetainsEachOracleBeforeApplyingTheCap()
    {
        var generatedCases = Enumerable.Range(0, 80)
            .Select(_ => new GeneratedCSharpCase(
                GeneratedCSharpExpression.Boolean(true),
                0,
                0,
                false))
            .ToArray();
        var frontendResults = Enumerable.Range(0, generatedCases.Length)
            .Select(index => new FrontendDifferentialResult(
                FuzzOracleStatus.Abstained,
                "frontend-detail-" + index))
            .ToArray();
        var finiteResults = Enumerable.Range(0, generatedCases.Length)
            .Select(index => new FiniteDomainDifferentialResult(
                FuzzOracleStatus.Abstained,
                null,
                null,
                0,
                "finite-detail-" + index))
            .ToArray();
        var partialResults = Enumerable.Range(0, generatedCases.Length)
            .Select(index => new PartialTermSmtDifferentialResult(
                FuzzOracleStatus.Abstained,
                PartialTermSmtCaseGenerator.ScenarioCount,
                0,
                0,
                0,
                "partial-detail-" + index))
            .ToArray();

        var evidence = FuzzRunner.SelectAbstentionEvidence(
            seed: 123,
            generatedCases,
            frontendResults,
            finiteResults,
            partialResults);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evidence.Length,
                Is.EqualTo(FuzzRunner.MaximumRetainedAbstentions));
            Assert.That(
                evidence.Take(3).Select(static item => item.Oracle),
                Is.EqualTo(ExpectedAbstentionOracleOrder));
            Assert.That(evidence[0].Detail, Is.EqualTo("frontend-detail-0"));
            Assert.That(evidence[0].Input, Does.Contain("Target"));
            Assert.That(
                evidence[1].Detail,
                Is.EqualTo("finite-detail-0"));
            Assert.That(
                evidence[2].Detail,
                Is.EqualTo("partial-detail-0"));

            var coverage = new FrontendFuzzCoverage(
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
            var summary = new FuzzSummary(
                SchemaVersion: 6,
                Cases: 1,
                Seed: 123,
                MaximumParallelism: 1,
                Agreements: 0,
                Abstentions: 1,
                FrontendAgreements: 1,
                SmtAgreements: 1,
                FiniteSmtSatisfiable: 1,
                FiniteSmtUnsatisfiable: 0,
                FiniteSmtAssumptions: 1,
                PartialSmtAgreements: 1,
                PartialSmtDefinedTrue: 1,
                PartialSmtDefinedFalse: 0,
                PartialSmtUndefined: 1,
                FrontendCoverage: coverage,
                CoverageSatisfied: true,
                Failures: [])
            {
                AbstentionEvidence = [evidence[0]]
            };
            using var document = JsonDocument.Parse(
                JsonSerializer.Serialize(summary));
            var serialized = document.RootElement
                .GetProperty("AbstentionEvidence");
            Assert.That(serialized.GetArrayLength(), Is.EqualTo(1));
            Assert.That(
                serialized[0].GetProperty("Oracle").GetString(),
                Is.EqualTo("frontend"));
            Assert.That(
                serialized[0].GetProperty("Detail").GetString(),
                Is.EqualTo("frontend-detail-0"));
        }
    }

    [Test]
    public void SerializedCoverageContainsOnlyStrictSchemaCounters()
    {
        var coverage = new FrontendFuzzCoverage(
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        var summary = new FuzzSummary(
            SchemaVersion: 6,
            Cases: 1,
            Seed: 7,
            MaximumParallelism: 1,
            Agreements: 1,
            Abstentions: 0,
            FrontendAgreements: 1,
            SmtAgreements: 1,
            FiniteSmtSatisfiable: 1,
            FiniteSmtUnsatisfiable: 0,
            FiniteSmtAssumptions: 1,
            PartialSmtAgreements: 1,
            PartialSmtDefinedTrue: 1,
            PartialSmtDefinedFalse: 0,
            PartialSmtUndefined: 1,
            FrontendCoverage: coverage,
            CoverageSatisfied: true,
            Failures: []);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary));
        var properties = document.RootElement
            .GetProperty("FrontendCoverage")
            .EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();

        Assert.That(properties, Does.Not.Contain("HasValidCounts"));
        Assert.That(properties, Does.Not.Contain("HasExpandedCategories"));
    }

    [Test]
    public void GeneratedModelRejectsSequenceEqualityUntilFrontendSupportsIt()
    {
        Assert.Throws<ArgumentException>((Action)(() =>
        {
            _ = GeneratedCSharpExpression.Binary(
                GeneratedExpressionKind.Equal,
                GeneratedCSharpExpression.Values(),
                GeneratedCSharpExpression.Values());
        }));
        Assert.Throws<ArgumentException>((Action)(() =>
        {
            _ = GeneratedCSharpExpression.Binary(
                GeneratedExpressionKind.NotEqual,
                GeneratedCSharpExpression.Values(),
                GeneratedCSharpExpression.Values());
        }));
    }

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
    public void FailureEvidenceReservesCapacityForEachOracle()
    {
        var finite = Enumerable.Repeat(
                FuzzOracleStatus.Mismatch,
                FuzzRunner.MaximumRetainedFailures)
            .Append(FuzzOracleStatus.Agreement)
            .ToArray();
        var frontend = Enumerable.Repeat(
                FuzzOracleStatus.Agreement,
                FuzzRunner.MaximumRetainedFailures)
            .Append(FuzzOracleStatus.Mismatch)
            .ToArray();
        var partial = Enumerable.Repeat(
                FuzzOracleStatus.Agreement,
                FuzzRunner.MaximumRetainedFailures + 1)
            .ToArray();

        var keys = FuzzRunner.SelectFailureKeys(frontend, finite, partial);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keys, Has.Length.EqualTo(FuzzRunner.MaximumRetainedFailures));
            Assert.That(keys, Does.Contain(new FuzzFailureKey(
                FuzzRunner.MaximumRetainedFailures,
                "frontend")));
            Assert.That(
                keys.Count(static key => key.Oracle == "finite-domain-smt"),
                Is.EqualTo(FuzzRunner.MaximumRetainedFailures - 1));
            Assert.That(
                keys.Count(static key => key.Oracle == "frontend"),
                Is.EqualTo(1));
            Assert.That(
                keys.Count(static key => key.Oracle == "partial-term-smt"),
                Is.Zero);
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
        Assert.That(first.SchemaVersion, Is.EqualTo(6));
        Assert.That(first.Passed, Is.True);
        Assert.That(first.Agreements, Is.EqualTo(options.Cases));
        Assert.That(first.Abstentions, Is.Zero);
        Assert.That(first.FrontendAgreements, Is.EqualTo(options.Cases));
        Assert.That(first.SmtAgreements, Is.EqualTo(options.Cases));
        Assert.That(
            first.FiniteSmtSatisfiable + first.FiniteSmtUnsatisfiable,
            Is.EqualTo(options.Cases));
        Assert.That(first.FiniteSmtSatisfiable, Is.GreaterThan(0));
        Assert.That(first.FiniteSmtUnsatisfiable, Is.GreaterThan(0));
        Assert.That(first.FiniteSmtAssumptions, Is.GreaterThan(0));
        Assert.That(
            first.PartialSmtAgreements,
            Is.EqualTo(options.Cases));
        Assert.That(
            first.PartialSmtDefinedTrue +
                first.PartialSmtDefinedFalse +
                first.PartialSmtUndefined,
            Is.EqualTo(options.Cases * PartialTermSmtCaseGenerator.ScenarioCount));
        Assert.That(first.PartialSmtDefinedTrue, Is.GreaterThan(0));
        Assert.That(first.PartialSmtUndefined, Is.GreaterThan(0));
        Assert.That(first.FrontendCoverage.HasValidCounts, Is.True);
        Assert.That(first.FrontendCoverage.StringCasts, Is.GreaterThan(0));
        Assert.That(first.FrontendCoverage.DivideByZeroExceptions, Is.GreaterThan(0));
        Assert.That(first.FrontendCoverage.OverflowExceptions, Is.GreaterThan(0));
    }

    [Test]
    public void CaseSeedHashDoesNotReplayShiftedSeedStreams()
    {
        const int firstSeed = 23063;
        const int secondSeed = 23460;
        const int cases = 1000;
        var first = Enumerable.Range(0, cases)
            .Select(index => FuzzRunner.CreateCaseSeed(firstSeed, index))
            .ToArray();
        var second = Enumerable.Range(0, cases)
            .Select(index => FuzzRunner.CreateCaseSeed(secondSeed, index))
            .ToArray();

        Assert.That(first.Distinct().Count(), Is.EqualTo(cases));
        Assert.That(second.Distinct().Count(), Is.EqualTo(cases));
        Assert.That(
            Enumerable.Range(0, cases - 1)
                .Any(index => second[index] == first[index + 1]),
            Is.False);
    }

    [Test]
    public void ReplayIndexSelectsTheOriginalCaseFromTheCampaignSeed()
    {
        var options = FuzzOptions.Parse([
            "--seed", "20260523",
            "--replay-index", "123"]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.Cases, Is.EqualTo(1));
            Assert.That(options.Seed, Is.EqualTo(20260523));
            Assert.That(options.ReplayCaseIndex, Is.EqualTo(123));
            Assert.That(
                FuzzRunner.ResolveCaseIndex(options, 0),
                Is.EqualTo(123));
            Assert.That(
                FuzzRunner.CreateCaseSeed(options.Seed, options.ReplayCaseIndex!.Value),
                Is.EqualTo(FuzzRunner.CreateCaseSeed(20260523, 123)));
        }
    }

    [Test]
    public void CaseSeedHashIsInjectiveWithinLargeCampaigns()
    {
        var seeds = new HashSet<int>();
        for (var index = 0; index < 1_000_000; index++)
        {
            Assert.That(
                seeds.Add(FuzzRunner.CreateCaseSeed(23063, index)),
                Is.True,
                $"case seed collision at index {index}");
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seeds.Count, Is.EqualTo(1_000_000));
            Assert.That(
                FuzzRunner.CreateCaseSeed(20260523, 5055),
                Is.Not.EqualTo(FuzzRunner.CreateCaseSeed(20260523, 6447)));
            Assert.That(
                FuzzRunner.CreateCaseSeed(20260605, 3773),
                Is.Not.EqualTo(FuzzRunner.CreateCaseSeed(20260605, 7592)));
        }
    }

    [Test]
    public void OppositeSignedSeedsProduceDifferentGeneratorStreams()
    {
        var positive = new DeterministicRandom(123456789);
        var negative = new DeterministicRandom(-123456789);
        var positiveStream = Enumerable.Range(0, 8)
            .Select(_ => positive.Next(int.MaxValue))
            .ToArray();
        var negativeStream = Enumerable.Range(0, 8)
            .Select(_ => negative.Next(int.MaxValue))
            .ToArray();

        Assert.That(positiveStream, Is.Not.EqualTo(negativeStream));
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
            parallel.FiniteSmtSatisfiable,
            Is.EqualTo(serial.FiniteSmtSatisfiable));
        Assert.That(
            parallel.FiniteSmtUnsatisfiable,
            Is.EqualTo(serial.FiniteSmtUnsatisfiable));
        Assert.That(
            parallel.FiniteSmtAssumptions,
            Is.EqualTo(serial.FiniteSmtAssumptions));
        Assert.That(
            parallel.PartialSmtAgreements,
            Is.EqualTo(serial.PartialSmtAgreements));
        Assert.That(
            parallel.PartialSmtDefinedTrue,
            Is.EqualTo(serial.PartialSmtDefinedTrue));
        Assert.That(
            parallel.PartialSmtDefinedFalse,
            Is.EqualTo(serial.PartialSmtDefinedFalse));
        Assert.That(
            parallel.PartialSmtUndefined,
            Is.EqualTo(serial.PartialSmtUndefined));
        Assert.That(parallel.Failures, Is.EqualTo(serial.Failures));
    }

    [Test]
    public void SupportedDomainAbstentionFailsTheCampaign()
    {
        var coverage = new FrontendFuzzCoverage(
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        var summary = new FuzzSummary(
            SchemaVersion: 6,
            Cases: 1,
            Seed: 7,
            MaximumParallelism: 1,
            Agreements: 0,
            Abstentions: 1,
            FrontendAgreements: 1,
            SmtAgreements: 1,
            FiniteSmtSatisfiable: 1,
            FiniteSmtUnsatisfiable: 0,
            FiniteSmtAssumptions: 1,
            PartialSmtAgreements: 1,
            PartialSmtDefinedTrue: 0,
            PartialSmtDefinedFalse: 0,
            PartialSmtUndefined: 0,
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
            SchemaVersion: 6,
            Cases: cases,
            Seed: 7,
            MaximumParallelism: maximumParallelism,
            Agreements: cases,
            Abstentions: 0,
            FrontendAgreements: cases,
            SmtAgreements: cases,
            FiniteSmtSatisfiable: cases,
            FiniteSmtUnsatisfiable: 0,
            FiniteSmtAssumptions: cases,
            PartialSmtAgreements: cases,
            PartialSmtDefinedTrue: 0,
            PartialSmtDefinedFalse: 0,
            PartialSmtUndefined: 0,
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
            SchemaVersion: 6,
            Cases: FuzzOptions.DefaultCases,
            Seed: 7,
            MaximumParallelism: 1,
            Agreements: FuzzOptions.DefaultCases,
            Abstentions: 0,
            FrontendAgreements: FuzzOptions.DefaultCases,
            SmtAgreements: FuzzOptions.DefaultCases,
            FiniteSmtSatisfiable: FuzzOptions.DefaultCases / 2,
            FiniteSmtUnsatisfiable: FuzzOptions.DefaultCases -
                FuzzOptions.DefaultCases / 2,
            FiniteSmtAssumptions: FuzzOptions.DefaultCases,
            PartialSmtAgreements: FuzzOptions.DefaultCases,
            PartialSmtDefinedTrue: FuzzOptions.DefaultCases,
            PartialSmtDefinedFalse: 0,
            PartialSmtUndefined: FuzzOptions.DefaultCases,
            FrontendCoverage: complete,
            CoverageSatisfied: true,
            Failures: []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(valid.Passed, Is.True);
            Assert.That(
                (valid with
                {
                    Cases = FuzzOptions.MaximumCases + 1,
                    Agreements = FuzzOptions.MaximumCases + 1,
                    FrontendAgreements = FuzzOptions.MaximumCases + 1,
                    SmtAgreements = FuzzOptions.MaximumCases + 1,
                    PartialSmtAgreements = FuzzOptions.MaximumCases + 1
                }).Passed,
                Is.False);
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
            Assert.That(
                (valid with { PartialSmtUndefined = 0 }).Passed,
                Is.False);
            Assert.That(
                (valid with { PartialSmtDefinedTrue = 0 }).Passed,
                Is.False);
            Assert.That(
                (valid with { FiniteSmtSatisfiable = 0 }).Passed,
                Is.False);
            Assert.That(
                (valid with { FiniteSmtUnsatisfiable = 0 }).Passed,
                Is.False);
            Assert.That(
                (valid with { FiniteSmtAssumptions = 0 }).Passed,
                Is.False);
            Assert.That(
                (valid with
                {
                    FiniteSmtSatisfiable = FuzzOptions.DefaultCases - 1,
                    FiniteSmtUnsatisfiable = FuzzOptions.DefaultCases
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
                Condition: false),
            new GeneratedCSharpCase(
                GeneratedCSharpExpression.Length(
                    GeneratedCSharpExpression.String("\uD83D\uDE00")),
                Left: 0,
                Right: 0,
                Condition: false),
            new GeneratedCSharpCase(
                GeneratedCSharpExpression.Binary(
                    GeneratedExpressionKind.StringConcat,
                    GeneratedCSharpExpression.String("\uD83D\uDE00"),
                    GeneratedCSharpExpression.String("!")),
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
        Assert.That(results[3].ExceptionKind, Is.Null);
        Assert.That(results[4].ExceptionKind, Is.Null);
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
    public void FrontendShrinkPredicateRejectsCompileFailures()
    {
        Assert.That(
            FuzzRunner.IsSemanticFrontendMismatch(
                new FrontendDifferentialResult(
                    FuzzOracleStatus.Mismatch,
                    "Generated C# did not compile: CS0020")),
            Is.False);
        Assert.That(
            FuzzRunner.IsSemanticFrontendMismatch(
                new FrontendDifferentialResult(
                    FuzzOracleStatus.Mismatch,
                    "Compiled C# and the lowered IR produced different values.")),
            Is.True);
    }

    [Test]
    public void ExpandedCoverageRequirementFailsClosed()
    {
        var empty = new FrontendFuzzCoverage(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        Assert.That(empty.HasExpandedCategories, Is.False);
    }

    [Test]
    public void FrontendCoverageCountsOnlyEvaluatedBranches()
    {
        var deadBranch = GeneratedCSharpExpression.Conditional(
            GeneratedCSharpExpression.Boolean(false),
            GeneratedCSharpExpression.Length(
                GeneratedCSharpExpression.String("dead")),
            GeneratedCSharpExpression.Integer(1));
        var liveBranch = GeneratedCSharpExpression.Conditional(
            GeneratedCSharpExpression.Boolean(true),
            GeneratedCSharpExpression.Length(
                GeneratedCSharpExpression.Text()),
            GeneratedCSharpExpression.Integer(1));
        var cases = new[]
        {
            new GeneratedCSharpCase(deadBranch, 0, 0, false),
            new GeneratedCSharpCase(liveBranch, 0, 0, false) { Text = "live" }
        };
        var results = Enumerable.Repeat(
            new FrontendDifferentialResult(FuzzOracleStatus.Agreement, ""),
            cases.Length).ToArray();

        var coverage = FuzzRunner.CreateFrontendCoverage(cases, results);

        Assert.That(coverage.StringLiterals, Is.Zero);
        Assert.That(coverage.StringLengths, Is.EqualTo(1));
        Assert.That(coverage.TextParameters, Is.EqualTo(1));
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

    [Test]
    public async Task UnsupportedFiniteDomainFormulaDoesNotInventAnExpectation()
    {
        var factory = new IrFactory();
        var text = factory.CreateVariable("text", factory.StringType);
        var formula = factory.Binary(
            IrBinaryOperator.Equal,
            factory.Variable(text),
            factory.String("value"));

        var result = await new FiniteDomainSmtDifferentialOracle()
            .CompareAsync(factory, formula);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(FuzzOracleStatus.Abstained));
            Assert.That(result.Expected, Is.Null);
            Assert.That(result.Actual, Is.Null);
            Assert.That(result.FiniteDomainAssumptions, Is.Zero);
            Assert.That(result.Detail, Does.Contain("outside"));
        }
    }

    [Test]
    public void FiniteDomainEnumerationVisitsEachAssignmentOnce()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var enabled = factory.CreateVariable("enabled", factory.BooleanType);
        var formula = factory.Binary(
            IrBinaryOperator.AndAlso,
            factory.Binary(
                IrBinaryOperator.Equal,
                factory.Variable(value),
                factory.Integer(0)),
            factory.Variable(enabled));

        var result = FiniteDomainSmtDifferentialOracle.EnumerateFiniteDomain(
            factory,
            formula);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AllDefined, Is.True);
            Assert.That(result.AnyTrue, Is.True);
            Assert.That(result.LeafEvaluations, Is.EqualTo(10));
        }
    }

    [Test]
    public async Task PartialTermOracleChecksShortCircuitAndUndefinedArithmetic()
    {
        var results = new List<PartialTermSmtDifferentialResult>();
        foreach (var seed in Enumerable.Range(0, 64))
        {
            var factory = new IrFactory();
            var generated = PartialTermSmtCaseGenerator.Create(factory, seed);
            results.Add(await new PartialTermSmtDifferentialOracle()
                .CompareAsync(factory, generated));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                results,
                Has.All.Matches<PartialTermSmtDifferentialResult>(
                    result => result.Status == FuzzOracleStatus.Agreement &&
                        result.ScenarioCount == PartialTermSmtCaseGenerator.ScenarioCount),
                string.Join(Environment.NewLine, results.Select(static result => result.Detail)));
            Assert.That(results.Sum(static result => result.DefinedTrueCount), Is.GreaterThan(0));
            Assert.That(results.Sum(static result => result.DefinedFalseCount), Is.GreaterThan(0));
            Assert.That(results.Sum(static result => result.UndefinedCount), Is.GreaterThan(0));
        }
    }

    [Test]
    public void PartialTermGeneratorUsesTheFullSeedWidth()
    {
        static string Signature(PartialTermSmtCase generated, IrFactory factory)
        {
            var printer = new IrPrinter(factory);
            return printer.Print(generated.Formula) + "|" + string.Join(
                ";",
                generated.Scenarios.Select(static scenario => string.Join(
                    ",",
                    scenario.OrderBy(static pair => pair.Key.Value).Select(static pair =>
                        pair.Key + "=" + (pair.Value.Kind switch
                        {
                            IrValueKind.Boolean => pair.Value.Boolean.ToString(),
                            IrValueKind.Integer => pair.Value.Integer.ToString(
                                System.Globalization.CultureInfo.InvariantCulture),
                            _ => pair.Value.Kind.ToString()
                        })))));
        }

        var signatures = Enumerable.Range(0, 1024)
            .Select(seed =>
            {
                var factory = new IrFactory();
                return Signature(
                    PartialTermSmtCaseGenerator.Create(factory, seed),
                    factory);
            })
            .Distinct(StringComparer.Ordinal)
            .Count();

        Assert.That(signatures, Is.GreaterThanOrEqualTo(128));
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
