global using FuzzOracleStatus = SharpProof.Testing.DifferentialStatus;

using System.Collections.Immutable;
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
    bool CoverageSatisfied,
    ImmutableArray<FuzzFailure> Failures)
{
    public bool Passed =>
        SchemaVersion == 4 &&
        Cases > 0 &&
        MaximumParallelism is >= 1 and <= 4 &&
        !Failures.IsDefault &&
        Failures.IsEmpty &&
        FrontendCoverage != null &&
        FrontendCoverage.HasValidCounts &&
        FrontendCoverage.HasValidExceptionCounts(Cases) &&
        CoverageSatisfied &&
        (Cases < FuzzOptions.DefaultCases ||
         FrontendCoverage.HasExpandedCategories) &&
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

        var agreements = 0;
        var abstentions = 0;
        var frontendAgreements = 0;
        var smtAgreements = 0;
        var partialSmtAgreements = 0;
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
        var frontendOracle = new FrontendDifferentialOracle();
        for (var offset = 0;
             offset < frontendCases.Length;
             offset += FrontendCompilationBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(
                FrontendCompilationBatchSize,
                frontendCases.Length - offset);
            var batchResults = frontendOracle.CompareBatch(
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
                var frontendCase = frontendCases[index];
                var frontend = frontendResults[index];
                frontendStatuses[index] = frontend.Status;
                if (frontend.Status == FuzzOracleStatus.Agreement)
                {
                    Interlocked.Increment(ref frontendAgreements);
                }

                var factory = new IrFactory();
                var formula = CreateTotalFiniteDomainFormula(
                    factory,
                    caseSeed,
                    token);
                var smtOracle = new FiniteDomainSmtDifferentialOracle();
                var smt = await smtOracle.CompareAsync(
                        factory,
                        formula,
                        token)
                    .ConfigureAwait(false);
                smtStatuses[index] = smt.Status;
                if (smt.Status == FuzzOracleStatus.Agreement)
                {
                    Interlocked.Increment(ref smtAgreements);
                }

                var partialCase = PartialTermSmtCaseGenerator.Create(
                    factory,
                    unchecked(caseSeed ^ 0x243F6A88));
                var partialOracle =
                    new PartialTermSmtDifferentialOracle();
                var partial = await partialOracle.CompareAsync(
                        factory,
                        partialCase,
                        token)
                    .ConfigureAwait(false);
                partialStatuses[index] = partial.Status;
                if (partial.Status == FuzzOracleStatus.Agreement)
                {
                    Interlocked.Increment(ref partialSmtAgreements);
                }

                var classification = ClassifyCase(
                    frontend.Status,
                    smt.Status,
                    partial.Status);
                if (!classification.HasMismatch &&
                    !classification.HasAbstention)
                {
                    Interlocked.Increment(ref agreements);
                }
                else if (!classification.HasMismatch)
                {
                    Interlocked.Increment(ref abstentions);
                }
            });

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
                    var frontendCase = frontendCases[index];
                    var minimizedFrontend = IsCompilationFailure(
                            frontendResults[index])
                        ? frontendCase
                        : CSharpStructuralShrinker.Minimize(
                            frontendCase,
                            candidate => IsSemanticMismatch(
                                frontendOracle.Compare(
                                    candidate,
                                    cancellationToken)),
                            cancellationToken);
                    var minimizedFrontendResult = frontendOracle.Compare(
                        minimizedFrontend,
                        cancellationToken);
                    failures.Add(new FuzzFailure(
                        index,
                        caseSeed,
                        failureKey.Oracle,
                        frontendCase.Source,
                        minimizedFrontend.Source,
                        minimizedFrontendResult.Detail));
                    break;
                case "finite-domain-smt":
                    var factory = new IrFactory();
                    var formula = CreateTotalFiniteDomainFormula(
                        factory,
                        caseSeed,
                        cancellationToken);
                    var smtOracle = new FiniteDomainSmtDifferentialOracle();
                    var minimizedFormula = await IrStructuralShrinker
                        .MinimizeAsync(
                            factory,
                            formula,
                            async (candidate, cancellation) =>
                                (await smtOracle.CompareAsync(
                                        factory,
                                        candidate,
                                        cancellation)
                                    .ConfigureAwait(false)).Status !=
                                FuzzOracleStatus.Agreement,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var minimizedSmtResult = await smtOracle.CompareAsync(
                            factory,
                            minimizedFormula,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var printer = new IrPrinter(factory);
                    failures.Add(new FuzzFailure(
                        index,
                        caseSeed,
                        failureKey.Oracle,
                        printer.Print(formula),
                        printer.Print(minimizedFormula),
                        minimizedSmtResult.Detail));
                    break;
                case "partial-term-smt":
                    var partialFactory = new IrFactory();
                    var partialCase = PartialTermSmtCaseGenerator.Create(
                        partialFactory,
                        unchecked(caseSeed ^ 0x243F6A88));
                    var partialResult = await new
                        PartialTermSmtDifferentialOracle().CompareAsync(
                            partialFactory,
                            partialCase,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var partialPrinter = new IrPrinter(partialFactory);
                    var printed = partialPrinter.Print(partialCase.Formula);
                    failures.Add(new FuzzFailure(
                        index,
                        caseSeed,
                        failureKey.Oracle,
                        printed,
                        printed,
                        partialResult.Detail));
                    break;
            }
        }

        return new FuzzSummary(
            SchemaVersion: 4,
            options.Cases,
            options.Seed,
            options.MaximumParallelism,
            agreements,
            abstentions,
            frontendAgreements,
            smtAgreements,
            partialSmtAgreements,
            frontendCoverage,
            coverageSatisfied,
            [.. failures]);
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

    private static IrTerm CreateTotalFiniteDomainFormula(
        IrFactory factory,
        int caseSeed,
        CancellationToken cancellationToken)
    {
        var generator = new WellSortedIrGenerator(
            factory,
            unchecked(caseSeed ^ 0x6C8E9CF5));
        for (var attempt = 0; attempt < 64; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var generated = generator.NextArithmeticOrBoolean(maximumDepth: 3);
            var formula = generated.Term.Type == factory.BooleanType
                ? generated.Term
                : factory.Binary(
                    IrBinaryOperator.Equal,
                    generated.Term,
                    factory.Integer(
                        FiniteDomainSmtDifferentialOracle.IntegerDomain[
                            PositiveModulo(
                                caseSeed,
                                FiniteDomainSmtDifferentialOracle
                                    .IntegerDomain
                                    .Length)]));
            if (FiniteDomainSmtDifferentialOracle
                .IsDefinedForAllAssignments(
                    factory,
                    formula,
                    cancellationToken))
            {
                return formula;
            }
        }
        return factory.Boolean((caseSeed & 1) == 0);
    }

    private static int CreateCaseSeed(int seed, int index)
    {
        return unchecked(seed + index * 397);
    }

    private static int PositiveModulo(int value, int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}
