global using FuzzOracleStatus = SharpProof.Testing.DifferentialStatus;

using System.Collections.Immutable;
using SharpProof.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using SharpProof;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Fuzz;

internal sealed record FuzzFailure(
    int Case,
    int Seed,
    string Oracle,
    string Original,
    string Minimized,
    string Detail)
{
    public string Term => Minimized;
    public string Oracle { get; init; } = Oracle ?? throw new System.ArgumentNullException(nameof(Oracle));
    public string Original { get; init; } = Original ?? throw new System.ArgumentNullException(nameof(Original));
    public string Minimized { get; init; } = Minimized ?? throw new System.ArgumentNullException(nameof(Minimized));
    public string Detail { get; init; } = Detail ?? throw new System.ArgumentNullException(nameof(Detail));
}

internal readonly record struct FuzzFailureKey(int Case, string Oracle)
{
    public string Oracle { get; init; } = Oracle ?? throw new System.ArgumentNullException(nameof(Oracle));
}
internal readonly record struct FuzzCaseClassification(
    bool HasMismatch,
    bool HasAbstention);

internal sealed record FrontendFuzzCoverage(
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
        IndexOutOfRangeExceptions > 0;

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

internal sealed record FuzzSummary(
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
    public int SchemaVersion { get; init; } = SchemaVersion;
    public int Cases { get; init; } = Cases;
    public int Seed { get; init; } = Seed;
    public int MaximumParallelism { get; init; } = MaximumParallelism;
    public int Agreements { get; init; } = Agreements;
    public int Abstentions { get; init; } = Abstentions;
    public int FrontendAgreements { get; init; } = FrontendAgreements;
    public int SmtAgreements { get; init; } = SmtAgreements;
    public int PartialSmtAgreements { get; init; } = PartialSmtAgreements;
    public FrontendFuzzCoverage FrontendCoverage { get; init; } = FrontendCoverage ?? throw new System.ArgumentNullException(nameof(FrontendCoverage));
    public TotalProgramFuzzCoverage TotalProgramCoverage { get; init; } = TotalProgramCoverage ?? throw new System.ArgumentNullException(nameof(TotalProgramCoverage));
    public MetadataProgramFuzzCoverage MetadataProgramCoverage { get; init; } = MetadataProgramCoverage ?? throw new System.ArgumentNullException(nameof(MetadataProgramCoverage));
    public bool CoverageSatisfied { get; init; } = CoverageSatisfied;
    public ImmutableArray<FuzzFailure> Failures { get; init; } = Failures.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Failures)) : Failures;

    public bool Passed =>
        SchemaVersion == 7 &&
        Cases > 0 &&
        MaximumParallelism is >= 1 and <= FuzzOptions.MaximumAllowedParallelism &&
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

internal static class FuzzRunner
{
    private const int FrontendCompilationBatchSize = 256;
    private const int PullRequestCoverageBudget = FuzzOptions.DefaultCases;
    internal const int MaximumRetainedFailures = 64;

    internal static async Task<int> RunCliAsync(
        ImmutableArray<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.ThrowIfMissing(arguments.IsDefault, nameof(arguments));
        error = RepositoryParameterGuard.NotNull(error, nameof(error));
        output = RepositoryParameterGuard.NotNull(output, nameof(output));
        var log = new RepositoryLogger(output, error);
        var usage =
            "Usage: SharpProof.Fuzz [--cases N] [--seed N] [--max-parallelism 1.." + FuzzOptions.MaximumAllowedParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
        FuzzOptions options;
        try
        {
            options = FuzzOptions.Parse(arguments);
        }
        catch (FuzzUsageException exception)
        {
            if (!string.IsNullOrEmpty(exception.Message))
            {
                log.Error(exception.Message);
            }

            log.Error(usage);
            return string.IsNullOrEmpty(exception.Message) ? 0 : 2;
        }

        try
        {
            var summary = await RunAsync(options, cancellationToken);
            log.Info(JsonSerializer.Serialize(
                summary,
                SharpProofJsonDefaults.Indented));
            return summary.Passed ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            log.Error("SharpProof fuzz run cancelled.");
            return 130;
        }
    }

    private readonly record struct GeneratedCaseEvaluation(
        FuzzSourceCaseExecutor.NativeInput NativeInput,
        FuzzOracleStatus SmtStatus,
        FuzzOracleStatus PartialStatus)
    {
        public FuzzSourceCaseExecutor.NativeInput NativeInput { get; } =
            NativeInput ?? throw new ArgumentNullException(nameof(NativeInput));
    }

    private readonly record struct ReplayedCaseEvaluation(
        FiniteDomainDifferentialResult FiniteResult,
        PartialTermSmtDifferentialResult PartialResult)
    {
        public FiniteDomainDifferentialResult FiniteResult { get; } =
            FiniteResult ?? throw new ArgumentNullException(nameof(FiniteResult));
        public PartialTermSmtDifferentialResult PartialResult { get; } =
            PartialResult ?? throw new ArgumentNullException(nameof(PartialResult));
    }

    private static async Task<ImmutableArray<T>> RunIndexedAsync<T>(
        int count, int maximumParallelism,
        Func<int, CancellationToken, ValueTask<T>> execute, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execute);
        RepositoryParameterGuard.RequireInitialized(count < 0, nameof(count), "Value must be initialized.");
        RepositoryParameterGuard.RequireInitialized(maximumParallelism < 1, nameof(maximumParallelism), "Value must be initialized.");
        var ordered = ImmutableArray.CreateBuilder<T>(count);
        ordered.Count = count;
        var gate = new object();
        await Parallel.ForEachAsync(Enumerable.Range(0, count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maximumParallelism,
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                RepositoryParameterGuard.RequireInitialized(index < 0, nameof(index), "Value must be initialized.");
                var value = await execute(index, token).ConfigureAwait(false);
                lock (gate)
                { ordered[index] = value; }
            }).ConfigureAwait(false);
        return ordered.MoveToImmutable();
    }

    public static async Task<FuzzSummary> RunAsync(
        FuzzOptions options,
        CancellationToken cancellationToken)
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
        if (options.MaximumParallelism is < 1 or > FuzzOptions.MaximumAllowedParallelism)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaximumParallelism,
                "Maximum parallelism must be between 1 and " + FuzzOptions.MaximumAllowedParallelism.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
        }

        var caseBuffer = ImmutableArray.CreateBuilder<GeneratedCSharpCase>(options.Cases);
        for (var index = 0; index < options.Cases; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var caseSeed = CreateCaseSeed(options.Seed, index);
            caseBuffer.Add(new SmallCSharpCaseGenerator(
                unchecked(caseSeed ^ 0x35A1D7)).Next(
                    maximumDepth: 4));
        }
        var frontendCases = caseBuffer.ToImmutable();
        var frontendResultBuffer = ImmutableArray.CreateBuilder<FrontendDifferentialResult>(options.Cases);
        frontendResultBuffer.Count = options.Cases;
        for (var offset = 0;
             offset < frontendCases.Length;
             offset += FrontendCompilationBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(
                FrontendCompilationBatchSize,
                frontendCases.Length - offset);
            var batchResults = FrontendDifferentialOracle.CompareBatch(
                frontendCases.Slice(offset, count), cancellationToken);
            for (var index = 0; index < batchResults.Length; index++)
            { frontendResultBuffer[offset + index] = batchResults[index]; }
        }
        var frontendResults = frontendResultBuffer.ToImmutable();
        var frontendCoverage = CreateFrontendCoverage(
            frontendCases.AsSpan(),
            frontendResults.AsSpan());
        var coverageSatisfied =
            options.Cases < PullRequestCoverageBudget ||
            HasRequiredFrontendCoverage(frontendCoverage);
        var evaluations = await RunIndexedAsync<GeneratedCaseEvaluation>(
            options.Cases, options.MaximumParallelism,
            async (index, token) =>
            {
                RepositoryParameterGuard.RequireInitialized(index < 0, nameof(index), "Value must be initialized.");
                token.ThrowIfCancellationRequested();
                var caseSeed = CreateCaseSeed(options.Seed, index);
                var nativeInput = FuzzSourceCaseExecutor.NativeInput.Generated(caseSeed);
                var comparison = nativeInput.CreateComparison();
                var smt = await comparison.CompareFiniteAsync(token).ConfigureAwait(false);
                var partial = await comparison.ComparePartialAsync(token).ConfigureAwait(false);
                return new GeneratedCaseEvaluation(nativeInput, smt.Status, partial.Status);
            }, cancellationToken);
        var nativeInputs = evaluations.Select(static result => result.NativeInput).ToImmutableArray();
        var smtStatuses = evaluations.Select(static result => result.SmtStatus).ToImmutableArray();
        var partialStatuses = evaluations.Select(static result => result.PartialStatus).ToImmutableArray();

        var (agreements, abstentions, frontendAgreements, smtAgreements, partialSmtAgreements, retainedFailures) =
            await CompleteCaseStageAsync(options, frontendCases, frontendResults,
                smtStatuses, partialStatuses, nativeInputs, cancellationToken).ConfigureAwait(false);
        var failures = ImmutableArray.CreateBuilder<FuzzFailure>();
        failures.AddRange(retainedFailures);
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
            failures.ToImmutable());
    }

    internal static async Task<FuzzSourceCampaignResult> ReplaySourceCasesAsync(
        ImmutableArray<FuzzSourceCampaignCase> sourceCases,
        CancellationToken cancellationToken, int seed = FuzzOptions.DefaultSeed)
    {
        if (sourceCases.IsDefault)
        { throw new ArgumentNullException(nameof(sourceCases)); }
        var sources = sourceCases.Select(source => source is null ? throw new ArgumentNullException(nameof(source)) : new FuzzSourceCampaignCase(
            source.Frontend, source.FiniteMembers, source.FiniteArguments,
            source.PartialMembers, source.PartialScenarios)).ToImmutableArray();
        var options = new FuzzOptions(sources.Length, seed, FuzzOptions.DefaultMaximumParallelism);
        var nativeInputs = sources.Select(FuzzSourceCaseExecutor.NativeInput.Source).ToImmutableArray();
        var frontendCases = sources.Select(source => source is null ? throw new ArgumentNullException(nameof(source)) : source.Frontend).ToImmutableArray();
        var frontendResultBuffer = ImmutableArray.CreateBuilder<FrontendDifferentialResult>(sources.Length);
        frontendResultBuffer.Count = sources.Length;
        for (var offset = 0; offset < frontendCases.Length; offset += FrontendCompilationBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(FrontendCompilationBatchSize, frontendCases.Length - offset);
            var results = FrontendDifferentialOracle.CompareBatch(
                frontendCases.Slice(offset, count), cancellationToken);
            for (var index = 0; index < results.Length; index++)
            { frontendResultBuffer[offset + index] = results[index]; }
        }
        var frontendResults = frontendResultBuffer.ToImmutable();
        var evaluations = await RunIndexedAsync<ReplayedCaseEvaluation>(
            sources.Length, options.MaximumParallelism,
            async (index, token) =>
            {
                RepositoryParameterGuard.RequireInitialized(index < 0, nameof(index), "Value must be initialized.");
                token.ThrowIfCancellationRequested();
                var comparison = nativeInputs[index].CreateComparison();
                var finite = await comparison.CompareFiniteAsync(token).ConfigureAwait(false);
                var partial = await comparison.ComparePartialAsync(token).ConfigureAwait(false);
                return new ReplayedCaseEvaluation(finite, partial);
            }, cancellationToken).ConfigureAwait(false);
        var finiteResults = evaluations.Select(static result => result.FiniteResult).ToImmutableArray();
        var partialResults = evaluations.Select(static result => result.PartialResult).ToImmutableArray();
        var smtStatuses = finiteResults.Select(static result => result is null ? throw new ArgumentNullException(nameof(result)) : result.Status).ToImmutableArray();
        var partialStatuses = partialResults.Select(static result => result is null ? throw new ArgumentNullException(nameof(result)) : result.Status).ToImmutableArray();
        var completed = await CompleteCaseStageAsync(options, frontendCases, frontendResults,
            smtStatuses, partialStatuses, nativeInputs, cancellationToken).ConfigureAwait(false);
        return new FuzzSourceCampaignResult(sources.Length, seed, completed.Agreements, completed.Abstentions,
            completed.FrontendAgreements, completed.SmtAgreements, completed.PartialSmtAgreements,
            completed.Failures, frontendResults, finiteResults, partialResults);
    }

    private static async Task<(int Agreements, int Abstentions, int FrontendAgreements,
        int SmtAgreements, int PartialSmtAgreements, ImmutableArray<FuzzFailure> Failures)> CompleteCaseStageAsync(
        FuzzOptions options, ImmutableArray<GeneratedCSharpCase> frontendCases, ImmutableArray<FrontendDifferentialResult> frontendResults,
        ImmutableArray<FuzzOracleStatus> smtStatuses, ImmutableArray<FuzzOracleStatus> partialStatuses,
        ImmutableArray<FuzzSourceCaseExecutor.NativeInput> nativeInputs, CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.ThrowIfInvalid(frontendCases.IsDefault, nameof(frontendCases));
        RepositoryParameterGuard.ThrowIfInvalid(frontendResults.IsDefault, nameof(frontendResults));
        RepositoryParameterGuard.ThrowIfInvalid(nativeInputs.IsDefault, nameof(nativeInputs));
        RepositoryParameterGuard.ThrowIfInvalid(partialStatuses.IsDefault, nameof(partialStatuses));
        RepositoryParameterGuard.ThrowIfInvalid(smtStatuses.IsDefault, nameof(smtStatuses));
        options = RepositoryParameterGuard.NotNull(options, nameof(options));
        var agreements = 0;
        var abstentions = 0;
        var frontendAgreements = 0;
        var smtAgreements = 0;
        var partialSmtAgreements = 0;
        var frontendStatuses = ImmutableArray.CreateBuilder<FuzzOracleStatus>(options.Cases);
        for (var index = 0; index < options.Cases; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frontendStatus = frontendResults[index].Status;
            var smtStatus = smtStatuses[index];
            var partialStatus = partialStatuses[index];
            frontendStatuses.Add(frontendStatus);
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
            frontendStatuses.ToImmutable(),
            smtStatuses,
            partialStatuses);
        var failures = ImmutableArray.CreateBuilder<FuzzFailure>(failureKeys.Length);
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

        return (agreements, abstentions, frontendAgreements, smtAgreements, partialSmtAgreements, failures.ToImmutable());
    }

    internal static ImmutableArray<FuzzFailureKey> SelectFailureKeys(
        ImmutableArray<FuzzOracleStatus> frontend,
        ImmutableArray<FuzzOracleStatus> smt,
        ImmutableArray<FuzzOracleStatus> partial)
    {
        if (frontend.IsDefault)
        { throw new ArgumentNullException(nameof(frontend)); }
        if (smt.IsDefault)
        { throw new ArgumentNullException(nameof(smt)); }
        if (partial.IsDefault)
        { throw new ArgumentNullException(nameof(partial)); }
        if (frontend.Length != smt.Length || smt.Length != partial.Length)
        {
            throw new ArgumentException(
                "Fuzz oracle status collections must have equal lengths.");
        }

        var keys = ImmutableArray.CreateBuilder<FuzzFailureKey>(
            MaximumRetainedFailures);
        for (var index = 0; index < frontend.Length; index++)
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
            oracle = RepositoryParameterGuard.NotNull(oracle, nameof(oracle));
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
        result = RepositoryParameterGuard.NotNull(result, nameof(result));
        return result.Status == FuzzOracleStatus.Mismatch &&
            result.Detail.StartsWith(
                "Generated C# did not compile:",
                StringComparison.Ordinal);
    }

    internal static bool IsSemanticMismatch(FrontendDifferentialResult result)
    {
        result = RepositoryParameterGuard.NotNull(result, nameof(result));
        return result.Status == FuzzOracleStatus.Mismatch &&
            !IsCompilationFailure(result);
    }

    private static FrontendFuzzCoverage CreateFrontendCoverage(
        ReadOnlySpan<GeneratedCSharpCase> cases /* SPINT001: all values valid - default and empty spans represent zero generated cases */,
        ReadOnlySpan<FrontendDifferentialResult> results /* SPINT001: all values valid - default and empty spans represent zero oracle observations */)
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
            expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
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
        coverage = RepositoryParameterGuard.NotNull(coverage, nameof(coverage));
        return coverage.HasExpandedCategories;
    }

    private static int CreateCaseSeed(int seed, int index)
    {
        return unchecked(seed + index * 397);
    }

}
