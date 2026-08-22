using System.Collections.Concurrent;
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
        CoverageSatisfied ==
            (Cases < FuzzOptions.DefaultCases ||
             FrontendCoverage.HasExpandedCategories) &&
        CoverageSatisfied &&
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

    public static async Task<FuzzSummary> RunAsync(
        FuzzOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        if (options.Cases <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Cases,
                "The fuzz case count must be positive.");
        }
        if (options.MaximumParallelism is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaximumParallelism,
                "Maximum parallelism must be between 1 and 4.");
        }

        var failures = new ConcurrentQueue<FuzzFailure>();
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
                if (partial.Status == FuzzOracleStatus.Agreement)
                {
                    Interlocked.Increment(ref partialSmtAgreements);
                }

                if (frontend.Status == FuzzOracleStatus.Mismatch)
                {
                    var minimized = CSharpStructuralShrinker.Minimize(
                        frontendCase,
                        candidate =>
                            frontendOracle.Compare(candidate, token).Status ==
                            FuzzOracleStatus.Mismatch,
                        token);
                    var minimizedResult = frontendOracle.Compare(
                        minimized,
                        token);
                    failures.Enqueue(
                        new FuzzFailure(
                            index,
                            caseSeed,
                            "frontend",
                            frontendCase.Source,
                            minimized.Source,
                            minimizedResult.Detail));
                }
                if (smt.Status == FuzzOracleStatus.Mismatch)
                {
                    var minimized = await IrStructuralShrinker.MinimizeAsync(
                            factory,
                            formula,
                            async (candidate, cancellation) =>
                                (await smtOracle.CompareAsync(
                                        factory,
                                        candidate,
                                        cancellation)
                                    .ConfigureAwait(false)).Status ==
                                FuzzOracleStatus.Mismatch,
                            token)
                        .ConfigureAwait(false);
                    var minimizedResult = await smtOracle.CompareAsync(
                            factory,
                            minimized,
                            token)
                        .ConfigureAwait(false);
                    var printer = new IrPrinter(factory);
                    failures.Enqueue(
                        new FuzzFailure(
                            index,
                            caseSeed,
                            "finite-domain-smt",
                            printer.Print(formula),
                            printer.Print(minimized),
                            minimizedResult.Detail));
                }
                if (partial.Status != FuzzOracleStatus.Agreement)
                {
                    var printer = new IrPrinter(factory);
                    failures.Enqueue(
                        new FuzzFailure(
                            index,
                            caseSeed,
                            "partial-term-smt",
                            printer.Print(partialCase.Formula),
                            printer.Print(partialCase.Formula),
                            partial.Detail));
                }

                var hasMismatch =
                    frontend.Status == FuzzOracleStatus.Mismatch ||
                    smt.Status == FuzzOracleStatus.Mismatch ||
                    partial.Status != FuzzOracleStatus.Agreement;
                var hasAbstention =
                    frontend.Status == FuzzOracleStatus.Abstained ||
                    smt.Status == FuzzOracleStatus.Abstained ||
                    partial.Status == FuzzOracleStatus.Abstained;
                if (!hasMismatch && !hasAbstention)
                {
                    Interlocked.Increment(ref agreements);
                }
                else if (!hasMismatch)
                {
                    Interlocked.Increment(ref abstentions);
                }
            });

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
            [.. failures
                .OrderBy(static failure => failure.Case)
                .ThenBy(static failure => failure.Oracle, StringComparer.Ordinal)]);
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
