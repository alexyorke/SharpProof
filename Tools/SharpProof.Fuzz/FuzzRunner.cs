global using FuzzOracleStatus = SharpProof.Testing.DifferentialStatus;

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpProof;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Fuzz;

public sealed record FuzzFailure(
    int Case,
    int Seed,
    string Oracle,
    string Original,
    string Minimized,
    string Detail)
{
    public string Term => Minimized;
}

internal readonly record struct FuzzFailureKey(int Case, string Oracle);
internal readonly record struct FuzzCaseClassification(
    bool HasMismatch,
    bool HasAbstention);

public sealed record FrontendFuzzCoverage(
    int TextParameters,
    int StringLiterals,
    int NullStrings,
    int StringConcatenations,
    int StringLengths,
    int StringCasts,
    int ArrayLengths,
    int ArrayIndexes,
    int DivideByZeroExceptions,
    int OverflowExceptions,
    int NullReferenceExceptions,
    int IndexOutOfRangeExceptions,
    int InvalidCastExceptions)
{
    [JsonIgnore]
    public bool HasValidCounts =>
        TextParameters >= 0 &&
        StringLiterals >= 0 &&
        NullStrings >= 0 &&
        StringConcatenations >= 0 &&
        StringLengths >= 0 &&
        StringCasts >= 0 &&
        ArrayLengths >= 0 &&
        ArrayIndexes >= 0 &&
        DivideByZeroExceptions >= 0 &&
        OverflowExceptions >= 0 &&
        NullReferenceExceptions >= 0 &&
        IndexOutOfRangeExceptions >= 0 &&
        InvalidCastExceptions >= 0;

    [JsonIgnore]
    public bool HasExpandedCategories =>
        TextParameters > 0 &&
        StringLiterals > 0 &&
        NullStrings > 0 &&
        StringConcatenations > 0 &&
        StringLengths > 0 &&
        StringCasts > 0 &&
        ArrayLengths > 0 &&
        ArrayIndexes > 0 &&
        DivideByZeroExceptions > 0 &&
        OverflowExceptions > 0 &&
        NullReferenceExceptions > 0 &&
        IndexOutOfRangeExceptions > 0 &&
        InvalidCastExceptions > 0;

    public bool HasValidExceptionCounts(int cases)
    {
        return cases >= 0 &&
            (long)DivideByZeroExceptions +
            OverflowExceptions +
            NullReferenceExceptions +
            IndexOutOfRangeExceptions +
            InvalidCastExceptions <= cases;
    }
}

public sealed record FuzzSummary(
    int SchemaVersion,
    int Cases,
    int Seed,
    int MaximumParallelism,
    int Agreements,
    int Abstentions,
    int FrontendAgreements,
    int SmtAgreements,
    int PartialSmtAgreements,
    FrontendFuzzCoverage FrontendCoverage,
    TotalProgramFuzzCoverage TotalProgramCoverage,
    MetadataProgramFuzzCoverage MetadataProgramCoverage,
    bool CoverageSatisfied,
    ImmutableArray<FuzzFailure> Failures)
{
    public bool Passed =>
        SchemaVersion == 7 &&
        Cases > 0 &&
        MaximumParallelism is >= 1 and <= 4 &&
        !Failures.IsDefault &&
        Failures.IsEmpty &&
        FrontendCoverage != null &&
        FrontendCoverage.HasValidCounts &&
        FrontendCoverage.HasValidExceptionCounts(Cases) &&
        TotalProgramCoverage != null &&
        TotalProgramCoverage.HasValidCounts &&
        TotalProgramCoverage.Cases == Cases &&
        TotalProgramCoverage.Agreements == Cases &&
        MetadataProgramCoverage != null && MetadataProgramCoverage.HasValidCounts &&
        MetadataProgramCoverage.Cases == Cases && MetadataProgramCoverage.Agreements == Cases &&
        CoverageSatisfied &&
        (Cases < FuzzOptions.DefaultCases ||
         (FrontendCoverage.HasExpandedCategories && TotalProgramCoverage.HasExpandedCategories && MetadataProgramCoverage.HasExpandedCategories)) &&
        Abstentions == 0 &&
        Agreements == Cases &&
        FrontendAgreements == Cases &&
        SmtAgreements == Cases &&
        PartialSmtAgreements == Cases;
}

public static class FuzzRunner
{
    private const int FrontendCompilationBatchSize = 256;
    private const int PullRequestCoverageBudget = FuzzOptions.DefaultCases;
    internal const int MaximumRetainedFailures = 64;

    internal static async Task<int> RunCliAsync(
        string[] arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        const string usage =
            "Usage: SharpProof.Fuzz [--cases N] [--seed N] [--max-parallelism 1..4]";
        FuzzOptions options;
        try
        {
            options = FuzzOptions.Parse(arguments);
        }
        catch (FuzzUsageException exception)
        {
            if (!string.IsNullOrEmpty(exception.Message))
            {
                error.WriteLine(exception.Message);
            }

            error.WriteLine(usage);
            return string.IsNullOrEmpty(exception.Message) ? 0 : 2;
        }

        try
        {
            var summary = await RunAsync(options, cancellationToken);
            output.WriteLine(JsonSerializer.Serialize(
                summary,
                SharpProofJsonDefaults.Indented));
            return summary.Passed ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            error.WriteLine("SharpProof fuzz run cancelled.");
            return 130;
        }
    }

    public static async Task<FuzzSummary> RunAsync(
        FuzzOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if (options.Cases <= 0 || options.Cases > FuzzOptions.MaximumCases)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Cases,
                "The fuzz case count must be between 1 and " +
                FuzzOptions.MaximumCases + ".");
        }
        if (options.MaximumParallelism is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaximumParallelism,
                "Maximum parallelism must be between 1 and 4.");
        }

        var frontendCases = new GeneratedCSharpCase[options.Cases];
        for (var index = 0; index < frontendCases.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var caseSeed = CreateCaseSeed(options.Seed, index);
            frontendCases[index] = new SmallCSharpCaseGenerator(
                unchecked(caseSeed ^ 0x35A1D7)).Next(
                    maximumDepth: 4);
        }
        var frontendResults =
            new FrontendDifferentialResult[options.Cases];
        var frontendStatuses = new FuzzOracleStatus[options.Cases];
        var smtStatuses = new FuzzOracleStatus[options.Cases];
        var partialStatuses = new FuzzOracleStatus[options.Cases];
        var nativeInputs = new FuzzSourceCaseExecutor.NativeInput[options.Cases];
        for (var offset = 0;
             offset < frontendCases.Length;
             offset += FrontendCompilationBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(
                FrontendCompilationBatchSize,
                frontendCases.Length - offset);
            var batchResults = FrontendDifferentialOracle.CompareBatch(
                new ArraySegment<GeneratedCSharpCase>(
                    frontendCases,
                    offset,
                    count),
                cancellationToken);
            batchResults.CopyTo(frontendResults, offset);
        }
        var frontendCoverage = CreateFrontendCoverage(
            frontendCases,
            frontendResults);
        var coverageSatisfied =
            options.Cases < PullRequestCoverageBudget ||
            HasRequiredFrontendCoverage(frontendCoverage);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaximumParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, options.Cases),
            parallelOptions,
            async (index, token) =>
            {
                token.ThrowIfCancellationRequested();
                var caseSeed = CreateCaseSeed(options.Seed, index);

                nativeInputs[index] = FuzzSourceCaseExecutor.NativeInput.Generated(caseSeed);
                var comparison = nativeInputs[index].CreateComparison();
                var smt = await comparison.CompareFiniteAsync(token).ConfigureAwait(false);
                smtStatuses[index] = smt.Status;
                var partial = await comparison.ComparePartialAsync(token).ConfigureAwait(false);
                partialStatuses[index] = partial.Status;
            });

        var (agreements, abstentions, frontendAgreements, smtAgreements, partialSmtAgreements, failures) =
            await CompleteCaseStageAsync(options, frontendCases, frontendResults,
                frontendStatuses, smtStatuses, partialStatuses, nativeInputs, cancellationToken).ConfigureAwait(false);

        var totalPrograms = await TotalProgramDifferentialOracle.RunAsync(options.Cases, options.Seed, cancellationToken);
        failures.AddRange(totalPrograms.Failures.Take(Math.Max(0, MaximumRetainedFailures - failures.Count)));
        coverageSatisfied &= options.Cases < PullRequestCoverageBudget || totalPrograms.Coverage.HasExpandedCategories;
        var metadataPrograms = await MetadataProgramDifferentialOracle.RunAsync(options.Cases, options.Seed, cancellationToken: cancellationToken);
        failures.AddRange(metadataPrograms.Failures.Take(Math.Max(0, MaximumRetainedFailures - failures.Count)));
        coverageSatisfied &= options.Cases < PullRequestCoverageBudget || metadataPrograms.Coverage.HasExpandedCategories;
        return new FuzzSummary(
            SchemaVersion: 7,
            options.Cases,
            options.Seed,
            options.MaximumParallelism,
            agreements,
            abstentions,
            frontendAgreements,
            smtAgreements,
            partialSmtAgreements,
            frontendCoverage,
            totalPrograms.Coverage,
            metadataPrograms.Coverage,
            coverageSatisfied,
            [.. failures]);
    }

    internal static async Task<FuzzSourceCampaignResult> ReplaySourceCasesAsync(
        IReadOnlyList<FuzzSourceCampaignCase> sourceCases,
        int seed = FuzzOptions.DefaultSeed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceCases);
        var sources = sourceCases.Select(source => new FuzzSourceCampaignCase(
            source.Frontend, source.FiniteMembers, source.FiniteArguments.ToArray(),
            source.PartialMembers, source.PartialScenarios.Select(arguments =>
                (IReadOnlyList<object?>)arguments.ToArray()).ToArray())).ToArray();
        var options = new FuzzOptions(sources.Length, seed, FuzzOptions.DefaultMaximumParallelism);
        var nativeInputs = sources.Select(FuzzSourceCaseExecutor.NativeInput.Source).ToArray();
        var frontendCases = sources.Select(source => source.Frontend).ToArray();
        var frontendResults = new FrontendDifferentialResult[sources.Length];
        var finiteResults = new FiniteDomainDifferentialResult[sources.Length];
        var partialResults = new PartialTermSmtDifferentialResult[sources.Length];
        var frontendStatuses = new FuzzOracleStatus[sources.Length];
        var smtStatuses = new FuzzOracleStatus[sources.Length];
        var partialStatuses = new FuzzOracleStatus[sources.Length];
        for (var offset = 0; offset < frontendCases.Length; offset += FrontendCompilationBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(FrontendCompilationBatchSize, frontendCases.Length - offset);
            var results = FrontendDifferentialOracle.CompareBatch(
                new ArraySegment<GeneratedCSharpCase>(frontendCases, offset, count), cancellationToken);
            results.CopyTo(frontendResults, offset);
        }
        await Parallel.ForEachAsync(Enumerable.Range(0, sources.Length),
            new ParallelOptions { MaxDegreeOfParallelism = options.MaximumParallelism, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                token.ThrowIfCancellationRequested();
                var comparison = nativeInputs[index].CreateComparison();
                var finite = await comparison.CompareFiniteAsync(token).ConfigureAwait(false);
                finiteResults[index] = finite;
                smtStatuses[index] = finite.Status;
                var partial = await comparison.ComparePartialAsync(token).ConfigureAwait(false);
                partialResults[index] = partial;
                partialStatuses[index] = partial.Status;
            }).ConfigureAwait(false);
        var completed = await CompleteCaseStageAsync(options, frontendCases, frontendResults,
            frontendStatuses, smtStatuses, partialStatuses, nativeInputs, cancellationToken).ConfigureAwait(false);
        return new FuzzSourceCampaignResult(sources.Length, seed, completed.Agreements, completed.Abstentions,
            completed.FrontendAgreements, completed.SmtAgreements, completed.PartialSmtAgreements,
            [.. completed.Failures], [.. frontendResults], [.. finiteResults], [.. partialResults]);
    }

    private static async Task<(int Agreements, int Abstentions, int FrontendAgreements,
        int SmtAgreements, int PartialSmtAgreements, List<FuzzFailure> Failures)> CompleteCaseStageAsync(
        FuzzOptions options, GeneratedCSharpCase[] frontendCases, FrontendDifferentialResult[] frontendResults,
        FuzzOracleStatus[] frontendStatuses, FuzzOracleStatus[] smtStatuses, FuzzOracleStatus[] partialStatuses,
        FuzzSourceCaseExecutor.NativeInput[] nativeInputs, CancellationToken cancellationToken)
    {
        var agreements = 0;
        var abstentions = 0;
        var frontendAgreements = 0;
        var smtAgreements = 0;
        var partialSmtAgreements = 0;
        for (var index = 0; index < options.Cases; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frontendStatus = frontendResults[index].Status;
            var smtStatus = smtStatuses[index];
            var partialStatus = partialStatuses[index];
            frontendStatuses[index] = frontendStatus;
            if (frontendStatus == FuzzOracleStatus.Agreement)
            {
                frontendAgreements++;
            }
            if (smtStatus == FuzzOracleStatus.Agreement)
            {
                smtAgreements++;
            }
            if (partialStatus == FuzzOracleStatus.Agreement)
            {
                partialSmtAgreements++;
            }

            var classification = ClassifyCase(
                frontendStatus,
                smtStatus,
                partialStatus);
            if (!classification.HasMismatch &&
                !classification.HasAbstention)
            {
                agreements++;
            }
            else if (!classification.HasMismatch)
            {
                abstentions++;
            }
        }

        var failureKeys = SelectFailureKeys(
            frontendStatuses,
            smtStatuses,
            partialStatuses);
        var failures = new List<FuzzFailure>(failureKeys.Length);
        foreach (var failureKey in failureKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = failureKey.Case;
            var caseSeed = CreateCaseSeed(options.Seed, index);
            switch (failureKey.Oracle)
            {
                case "frontend":
                    failures.Add(FrontendFailureRetention.Retain(
                        frontendCases[index],
                        frontendResults[index],
                        index,
                        caseSeed,
                        cancellationToken).Failure);
                    break;
                case "finite-domain-smt":
                    failures.Add(await nativeInputs[index].RetainFiniteAsync(
                        index, caseSeed, cancellationToken).ConfigureAwait(false));
                    break;
                case "partial-term-smt":
                    failures.Add(await nativeInputs[index].RetainPartialAsync(
                        index, caseSeed, cancellationToken).ConfigureAwait(false));
                    break;
            }
        }

        return (agreements, abstentions, frontendAgreements, smtAgreements, partialSmtAgreements, failures);
    }

    internal static ImmutableArray<FuzzFailureKey> SelectFailureKeys(
        IReadOnlyList<FuzzOracleStatus> frontend,
        IReadOnlyList<FuzzOracleStatus> smt,
        IReadOnlyList<FuzzOracleStatus> partial)
    {
        if (frontend.Count != smt.Count || smt.Count != partial.Count)
        {
            throw new ArgumentException(
                "Fuzz oracle status collections must have equal lengths.");
        }

        var keys = ImmutableArray.CreateBuilder<FuzzFailureKey>(
            MaximumRetainedFailures);
        for (var index = 0; index < frontend.Count; index++)
        {
            Add(index, "finite-domain-smt", smt[index] != FuzzOracleStatus.Agreement);
            Add(index, "frontend", frontend[index] != FuzzOracleStatus.Agreement);
            Add(index, "partial-term-smt", partial[index] != FuzzOracleStatus.Agreement);
            if (keys.Count >= MaximumRetainedFailures)
            {
                break;
            }
        }

        keys.Capacity = keys.Count;
        return keys.MoveToImmutable();

        void Add(int index, string oracle, bool failed)
        {
            if (failed && keys.Count < MaximumRetainedFailures)
            {
                keys.Add(new FuzzFailureKey(index, oracle));
            }
        }
    }

    internal static FuzzCaseClassification ClassifyCase(
        FuzzOracleStatus frontendStatus,
        FuzzOracleStatus smtStatus,
        FuzzOracleStatus partialStatus)
    {
        var hasMismatch =
            frontendStatus == FuzzOracleStatus.Mismatch ||
            smtStatus == FuzzOracleStatus.Mismatch ||
            partialStatus == FuzzOracleStatus.Mismatch;
        var hasAbstention =
            frontendStatus == FuzzOracleStatus.Abstained ||
            smtStatus == FuzzOracleStatus.Abstained ||
            partialStatus == FuzzOracleStatus.Abstained;
        return new FuzzCaseClassification(hasMismatch, hasAbstention);
    }

    internal static bool IsCompilationFailure(FrontendDifferentialResult result)
    {
        return result.Status == FuzzOracleStatus.Mismatch &&
            result.Detail.StartsWith(
                "Generated C# did not compile:",
                StringComparison.Ordinal);
    }

    internal static bool IsSemanticMismatch(FrontendDifferentialResult result)
    {
        return result.Status == FuzzOracleStatus.Mismatch &&
            !IsCompilationFailure(result);
    }

    private static FrontendFuzzCoverage CreateFrontendCoverage(
        IReadOnlyList<GeneratedCSharpCase> cases,
        IReadOnlyList<FrontendDifferentialResult> results)
    {
        var textParameters = 0;
        var stringLiterals = 0;
        var nullStrings = 0;
        var stringConcatenations = 0;
        var stringLengths = 0;
        var stringCasts = 0;
        var arrayLengths = 0;
        var arrayIndexes = 0;
        foreach (var generated in cases)
        {
            Count(generated.Expression);
        }

        var divideByZero = 0;
        var overflow = 0;
        var nullReference = 0;
        var indexOutOfRange = 0;
        var invalidCast = 0;
        foreach (var result in results)
        {
            switch (result.ExceptionKind)
            {
                case IrExceptionKind.DivideByZero:
                    divideByZero++;
                    break;
                case IrExceptionKind.Overflow:
                    overflow++;
                    break;
                case IrExceptionKind.NullReference:
                    nullReference++;
                    break;
                case IrExceptionKind.IndexOutOfRange:
                    indexOutOfRange++;
                    break;
                case IrExceptionKind.InvalidCast:
                    invalidCast++;
                    break;
            }
        }
        return new FrontendFuzzCoverage(
            textParameters,
            stringLiterals,
            nullStrings,
            stringConcatenations,
            stringLengths,
            stringCasts,
            arrayLengths,
            arrayIndexes,
            divideByZero,
            overflow,
            nullReference,
            indexOutOfRange,
            invalidCast);

        void Count(GeneratedCSharpExpression expression)
        {
            switch (expression.Kind)
            {
                case GeneratedExpressionKind.TextParameter:
                    textParameters++;
                    break;
                case GeneratedExpressionKind.StringLiteral:
                    stringLiterals++;
                    break;
                case GeneratedExpressionKind.NullString:
                    nullStrings++;
                    break;
                case GeneratedExpressionKind.StringConcat:
                    stringConcatenations++;
                    break;
                case GeneratedExpressionKind.Length
                    when expression.Children[0].Type ==
                         GeneratedExpressionType.String:
                    stringLengths++;
                    break;
                case GeneratedExpressionKind.Length:
                    arrayLengths++;
                    break;
                case GeneratedExpressionKind.CastToString:
                    stringCasts++;
                    break;
                case GeneratedExpressionKind.ArrayIndex:
                    arrayIndexes++;
                    break;
            }
            foreach (var child in expression.Children)
            {
                Count(child);
            }
        }
    }

    private static bool HasRequiredFrontendCoverage(
        FrontendFuzzCoverage coverage)
    {
        return coverage.HasExpandedCategories;
    }

    private static int CreateCaseSeed(int seed, int index)
    {
        return unchecked(seed + index * 397);
    }

}
