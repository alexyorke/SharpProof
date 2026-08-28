using System.Collections.Immutable;
using System.Text.Json.Serialization;
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

public sealed record FuzzAbstention(
    int Case,
    int Seed,
    string Oracle,
    string Input,
    string Detail);

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
    int FiniteSmtSatisfiable,
    int FiniteSmtUnsatisfiable,
    int FiniteSmtAssumptions,
    int PartialSmtAgreements,
    int PartialSmtDefinedTrue,
    int PartialSmtDefinedFalse,
    int PartialSmtUndefined,
    FrontendFuzzCoverage FrontendCoverage,
    bool CoverageSatisfied,
    ImmutableArray<FuzzFailure> Failures)
{
    public ImmutableArray<FuzzAbstention> AbstentionEvidence
    {
        get;
        init;
    } = [];

    public bool Passed =>
        SchemaVersion == 6 &&
        Cases is > 0 and <= FuzzOptions.MaximumCases &&
        MaximumParallelism is >= 1 and <= 4 &&
        !Failures.IsDefault &&
        Failures.IsEmpty &&
        FrontendCoverage != null &&
        FrontendCoverage.HasValidCounts &&
        FrontendCoverage.HasValidExceptionCounts(Cases) &&
        FiniteSmtSatisfiable >= 0 &&
        FiniteSmtUnsatisfiable >= 0 &&
        FiniteSmtAssumptions >= 0 &&
        (long)FiniteSmtSatisfiable + FiniteSmtUnsatisfiable == Cases &&
        (Cases < FuzzOptions.DefaultCases ||
         FiniteSmtSatisfiable > 0 &&
         FiniteSmtUnsatisfiable > 0 &&
         FiniteSmtAssumptions > 0) &&
        PartialSmtDefinedTrue >= 0 &&
        PartialSmtDefinedFalse >= 0 &&
        PartialSmtUndefined >= 0 &&
        (long)PartialSmtDefinedTrue +
            PartialSmtDefinedFalse +
            PartialSmtUndefined ==
            (long)Cases * PartialTermSmtCaseGenerator.ScenarioCount &&
        (Cases < FuzzOptions.DefaultCases ||
         PartialSmtDefinedTrue + PartialSmtDefinedFalse > 0 &&
         PartialSmtUndefined > 0) &&
        CoverageSatisfied ==
            (Cases < FuzzOptions.DefaultCases ||
             FrontendCoverage.HasExpandedCategories) &&
        CoverageSatisfied &&
        !AbstentionEvidence.IsDefault &&
        (Abstentions != 0 || AbstentionEvidence.IsEmpty) &&
        Abstentions == 0 &&
        Agreements == Cases &&
        FrontendAgreements == Cases &&
        SmtAgreements == Cases &&
        PartialSmtAgreements == Cases;
}

public static class FuzzRunner
{
    private readonly record struct GeneratedEvaluation(bool Failed, object? Value)
    {
        internal static GeneratedEvaluation Failure => new(true, null);
    }

    private readonly record struct TotalFiniteDomainFormula(
        IrTerm Formula,
        FiniteDomainEnumerationResult Enumeration);

    private const int FrontendCompilationBatchSize = 256;
    private const int PullRequestCoverageBudget = FuzzOptions.DefaultCases;
    internal const int MaximumRetainedFailures = 64;
    internal const int MaximumRetainedAbstentions = 64;

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
        var finiteSmtSatisfiable = 0;
        var finiteSmtUnsatisfiable = 0;
        var finiteSmtAssumptions = 0;
        var partialSmtAgreements = 0;
        var partialSmtDefinedTrue = 0;
        var partialSmtDefinedFalse = 0;
        var partialSmtUndefined = 0;
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
        var finiteResults =
            new FiniteDomainDifferentialResult?[options.Cases];
        var partialResults =
            new PartialTermSmtDifferentialResult?[options.Cases];
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
                        formula.Formula,
                        formula.Enumeration,
                        token)
                    .ConfigureAwait(false);
                finiteResults[index] = smt;
                smtStatuses[index] = smt.Status;
                if (smt.Status == FuzzOracleStatus.Agreement)
                {
                    Interlocked.Increment(ref smtAgreements);
                }
                if (smt.Expected == FiniteDomainSatisfiability.Satisfiable)
                {
                    Interlocked.Increment(ref finiteSmtSatisfiable);
                }
                else if (smt.Expected == FiniteDomainSatisfiability.Unsatisfiable)
                {
                    Interlocked.Increment(ref finiteSmtUnsatisfiable);
                }
                Interlocked.Add(
                    ref finiteSmtAssumptions,
                    smt.FiniteDomainAssumptions);

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
                partialResults[index] = partial;
                partialStatuses[index] = partial.Status;
                if (partial.Status == FuzzOracleStatus.Agreement)
                {
                    Interlocked.Increment(ref partialSmtAgreements);
                }
                Interlocked.Add(
                    ref partialSmtDefinedTrue,
                    partial.DefinedTrueCount);
                Interlocked.Add(
                    ref partialSmtDefinedFalse,
                    partial.DefinedFalseCount);
                Interlocked.Add(
                    ref partialSmtUndefined,
                    partial.UndefinedCount);

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
                    var minimizedFrontend = CSharpStructuralShrinker.Minimize(
                        frontendCase,
                        candidate => IsSemanticFrontendMismatch(
                            frontendOracle.Compare(candidate, cancellationToken)),
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
                    var minimizedFiniteFormula = await IrStructuralShrinker
                        .MinimizeAsync(
                            factory,
                            formula.Formula,
                            async (candidate, cancellation) =>
                                (await smtOracle.CompareAsync(
                                        factory,
                                        candidate,
                                        cancellation)
                                    .ConfigureAwait(false)).Status ==
                                FuzzOracleStatus.Mismatch,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var minimizedSmtResult = await smtOracle.CompareAsync(
                            factory,
                            minimizedFiniteFormula,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var printer = new IrPrinter(factory);
                    failures.Add(new FuzzFailure(
                        index,
                        caseSeed,
                        failureKey.Oracle,
                        printer.Print(formula.Formula),
                        printer.Print(minimizedFiniteFormula),
                        minimizedSmtResult.Detail));
                    break;
                case "partial-term-smt":
                    var partialFactory = new IrFactory();
                    var partialCase = PartialTermSmtCaseGenerator.Create(
                        partialFactory,
                        unchecked(caseSeed ^ 0x243F6A88));
                    var partialPrinter = new IrPrinter(partialFactory);
                    var partialOracle = new PartialTermSmtDifferentialOracle();
                    var minimizedPartialFormula = await IrStructuralShrinker
                        .MinimizeAsync(
                            partialFactory,
                            partialCase.Formula,
                            async (candidate, cancellation) =>
                                (await partialOracle.CompareAsync(
                                        partialFactory,
                                        partialCase with { Formula = candidate },
                                        cancellation)
                                    .ConfigureAwait(false)).Status ==
                                FuzzOracleStatus.Mismatch,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var minimizedPartialResult = await partialOracle.CompareAsync(
                            partialFactory,
                            partialCase with { Formula = minimizedPartialFormula },
                            cancellationToken)
                        .ConfigureAwait(false);
                    failures.Add(new FuzzFailure(
                        index,
                        caseSeed,
                        failureKey.Oracle,
                        partialPrinter.Print(partialCase.Formula),
                        partialPrinter.Print(minimizedPartialFormula),
                        minimizedPartialResult.Detail));
                    break;
            }
        }

        var summary = new FuzzSummary(
            SchemaVersion: 6,
            options.Cases,
            options.Seed,
            options.MaximumParallelism,
            agreements,
            abstentions,
            frontendAgreements,
            smtAgreements,
            finiteSmtSatisfiable,
            finiteSmtUnsatisfiable,
            finiteSmtAssumptions,
            partialSmtAgreements,
            partialSmtDefinedTrue,
            partialSmtDefinedFalse,
            partialSmtUndefined,
            frontendCoverage,
            coverageSatisfied,
            [.. failures]);
        return summary with
        {
            AbstentionEvidence = SelectAbstentionEvidence(
                options.Seed,
                frontendCases,
                frontendResults,
                finiteResults,
                partialResults)
        };
    }

    internal static ImmutableArray<FuzzAbstention> SelectAbstentionEvidence(
        int seed,
        IReadOnlyList<GeneratedCSharpCase> frontendCases,
        IReadOnlyList<FrontendDifferentialResult> frontendResults,
        IReadOnlyList<FiniteDomainDifferentialResult?> finiteResults,
        IReadOnlyList<PartialTermSmtDifferentialResult?> partialResults)
    {
        if (frontendCases == null ||
            frontendResults == null ||
            finiteResults == null ||
            partialResults == null)
        {
            throw new ArgumentNullException(
                frontendCases == null ? nameof(frontendCases) :
                frontendResults == null ? nameof(frontendResults) :
                finiteResults == null ? nameof(finiteResults) :
                nameof(partialResults));
        }

        if (frontendCases.Count != frontendResults.Count ||
            frontendResults.Count != finiteResults.Count ||
            finiteResults.Count != partialResults.Count)
        {
            throw new ArgumentException(
                "Fuzz evidence collections must have equal lengths.");
        }

        var evidence = ImmutableArray.CreateBuilder<FuzzAbstention>(
            MaximumRetainedAbstentions);
        var retained = new HashSet<FuzzFailureKey>();

        // Reserve one entry for each oracle before applying the global cap.
        AddFirstFrontend();
        AddFirstFinite();
        AddFirstPartial();

        for (var index = 0; index < frontendResults.Count; index++)
        {
            AddFrontend(index);
            AddFinite(index);
            AddPartial(index);
            if (evidence.Count >= MaximumRetainedAbstentions)
            {
                break;
            }
        }

        evidence.Capacity = evidence.Count;
        return evidence.MoveToImmutable();

        void AddFirstFrontend()
        {
            for (var index = 0; index < frontendResults.Count; index++)
            {
                if (frontendResults[index].Status == FuzzOracleStatus.Abstained)
                {
                    Add(new FuzzAbstention(
                        index,
                        CreateCaseSeed(seed, index),
                        "frontend",
                        frontendCases[index].Source,
                        frontendResults[index].Detail));
                    return;
                }
            }
        }

        void AddFirstFinite()
        {
            for (var index = 0; index < finiteResults.Count; index++)
            {
                if (finiteResults[index]?.Status == FuzzOracleStatus.Abstained)
                {
                    AddFinite(index);
                    return;
                }
            }
        }

        void AddFirstPartial()
        {
            for (var index = 0; index < partialResults.Count; index++)
            {
                if (partialResults[index]?.Status == FuzzOracleStatus.Abstained)
                {
                    AddPartial(index);
                    return;
                }
            }
        }

        void AddFrontend(int index)
        {
            if (frontendResults[index].Status == FuzzOracleStatus.Abstained)
            {
                Add(new FuzzAbstention(
                    index,
                    CreateCaseSeed(seed, index),
                    "frontend",
                    frontendCases[index].Source,
                    frontendResults[index].Detail));
            }
        }

        void AddFinite(int index)
        {
            var result = finiteResults[index];
            if (result?.Status != FuzzOracleStatus.Abstained)
            {
                return;
            }

            var caseSeed = CreateCaseSeed(seed, index);
            var factory = new IrFactory();
            var formula = CreateTotalFiniteDomainFormula(
                factory,
                caseSeed,
                CancellationToken.None);
            Add(new FuzzAbstention(
                index,
                caseSeed,
                "finite-domain-smt",
                new IrPrinter(factory).Print(formula.Formula),
                result.Detail));
        }

        void AddPartial(int index)
        {
            var result = partialResults[index];
            if (result?.Status != FuzzOracleStatus.Abstained)
            {
                return;
            }

            var caseSeed = CreateCaseSeed(seed, index);
            var factory = new IrFactory();
            var generated = PartialTermSmtCaseGenerator.Create(
                factory,
                unchecked(caseSeed ^ 0x243F6A88));
            Add(new FuzzAbstention(
                index,
                caseSeed,
                "partial-term-smt",
                FormatPartialInput(factory, generated),
                result.Detail));
        }

        void Add(FuzzAbstention item)
        {
            if (evidence.Count >= MaximumRetainedAbstentions ||
                !retained.Add(new FuzzFailureKey(item.Case, item.Oracle)))
            {
                return;
            }

            evidence.Add(item);
        }
    }

    private static string FormatPartialInput(
        IrFactory factory,
        PartialTermSmtCase generated)
    {
        var printer = new IrPrinter(factory);
        var scenarios = string.Join(
            "; ",
            generated.Scenarios.Select((scenario, index) =>
                "scenario " + index.ToString() + ": " +
                string.Join(
                    ", ",
                    scenario
                        .OrderBy(static pair => pair.Key.Value)
                        .Select(static pair =>
                            pair.Key + "=" + FormatValue(pair.Value)))));
        return printer.Print(generated.Formula) +
            " scenarios=[" + scenarios + "]";

        static string FormatValue(IrValue value)
        {
            return value.Kind switch
            {
                IrValueKind.Boolean => value.Boolean ? "true" : "false",
                IrValueKind.Integer => value.Integer.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                IrValueKind.String => value.String,
                IrValueKind.Null => "null",
                _ => value.Kind.ToString()
            };
        }
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
            Add(index, "finite-domain-smt",
                smt[index] == FuzzOracleStatus.Mismatch);
            Add(index, "frontend",
                frontend[index] == FuzzOracleStatus.Mismatch);
            Add(index, "partial-term-smt",
                partial[index] == FuzzOracleStatus.Mismatch);
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

    internal static FrontendFuzzCoverage CreateFrontendCoverage(
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
            Evaluate(generated.Expression, generated);
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

        GeneratedEvaluation Evaluate(
            GeneratedCSharpExpression expression,
            GeneratedCSharpCase generated)
        {
            Count(expression);
            switch (expression.Kind)
            {
                case GeneratedExpressionKind.BooleanLiteral:
                    return new(false, expression.BooleanValue);
                case GeneratedExpressionKind.IntegerLiteral:
                    return new(false, expression.IntegerValue);
                case GeneratedExpressionKind.LeftParameter:
                    return new(false, generated.Left);
                case GeneratedExpressionKind.RightParameter:
                    return new(false, generated.Right);
                case GeneratedExpressionKind.ConditionParameter:
                    return new(false, generated.Condition);
                case GeneratedExpressionKind.TextParameter:
                    return new(false, generated.Text);
                case GeneratedExpressionKind.ValuesParameter:
                    return new(false, generated.Values);
                case GeneratedExpressionKind.ReferenceParameter:
                    return new(false, generated.Reference);
                case GeneratedExpressionKind.NullReference:
                case GeneratedExpressionKind.NullString:
                    return new(false, null);
                case GeneratedExpressionKind.StringLiteral:
                    return new(false, expression.StringValue);
                case GeneratedExpressionKind.Not:
                {
                    var operand = Evaluate(expression.Children[0], generated);
                    return operand.Failed || operand.Value is not bool value
                        ? GeneratedEvaluation.Failure
                        : new(false, !value);
                }
                case GeneratedExpressionKind.Negate:
                {
                    var operand = Evaluate(expression.Children[0], generated);
                    if (operand.Failed || operand.Value is not long value)
                    {
                        return GeneratedEvaluation.Failure;
                    }

                    try
                    {
                        return new(false, checked(-value));
                    }
                    catch (OverflowException)
                    {
                        return GeneratedEvaluation.Failure;
                    }
                }
                case GeneratedExpressionKind.Conditional:
                {
                    var condition = Evaluate(expression.Children[0], generated);
                    if (condition.Failed || condition.Value is not bool value)
                    {
                        return GeneratedEvaluation.Failure;
                    }

                    return Evaluate(
                        expression.Children[value ? 1 : 2],
                        generated);
                }
                case GeneratedExpressionKind.AndAlso:
                case GeneratedExpressionKind.OrElse:
                {
                    var left = Evaluate(expression.Children[0], generated);
                    if (left.Failed || left.Value is not bool leftValue)
                    {
                        return GeneratedEvaluation.Failure;
                    }

                    var shortCircuits = expression.Kind ==
                        GeneratedExpressionKind.AndAlso
                            ? !leftValue
                            : leftValue;
                    if (shortCircuits)
                    {
                        return new(false, leftValue);
                    }

                    var right = Evaluate(expression.Children[1], generated);
                    return right.Failed || right.Value is not bool rightValue
                        ? GeneratedEvaluation.Failure
                        : new(false, rightValue);
                }
                case GeneratedExpressionKind.StringConcat:
                {
                    var left = Evaluate(expression.Children[0], generated);
                    if (left.Failed)
                    {
                        return GeneratedEvaluation.Failure;
                    }

                    var right = Evaluate(expression.Children[1], generated);
                    return right.Failed
                        ? GeneratedEvaluation.Failure
                        : new(false,
                            (left.Value as string ?? string.Empty) +
                            (right.Value as string ?? string.Empty));
                }
                case GeneratedExpressionKind.Length:
                {
                    var operand = Evaluate(expression.Children[0], generated);
                    if (operand.Failed || operand.Value == null)
                    {
                        return GeneratedEvaluation.Failure;
                    }

                    return operand.Value switch
                    {
                        string text => new(false, (long)text.Length),
                        long[] values => new(false, (long)values.Length),
                        _ => GeneratedEvaluation.Failure
                    };
                }
                case GeneratedExpressionKind.ArrayIndex:
                {
                    var values = Evaluate(expression.Children[0], generated);
                    if (values.Failed || values.Value is not long[] array)
                    {
                        return GeneratedEvaluation.Failure;
                    }

                    var index = Evaluate(expression.Children[1], generated);
                    if (index.Failed || index.Value is not long offset ||
                        offset < 0 || offset >= array.Length)
                    {
                        return GeneratedEvaluation.Failure;
                    }

                    return new(false, array[(int)offset]);
                }
                case GeneratedExpressionKind.CastToString:
                {
                    var operand = Evaluate(expression.Children[0], generated);
                    return operand.Failed ||
                        operand.Value != null && operand.Value is not string
                        ? GeneratedEvaluation.Failure
                        : operand;
                }
                case GeneratedExpressionKind.Add:
                case GeneratedExpressionKind.Subtract:
                case GeneratedExpressionKind.Multiply:
                case GeneratedExpressionKind.Divide:
                case GeneratedExpressionKind.Remainder:
                case GeneratedExpressionKind.Equal:
                case GeneratedExpressionKind.NotEqual:
                case GeneratedExpressionKind.LessThan:
                case GeneratedExpressionKind.LessThanOrEqual:
                case GeneratedExpressionKind.GreaterThan:
                case GeneratedExpressionKind.GreaterThanOrEqual:
                {
                    var left = Evaluate(expression.Children[0], generated);
                    if (left.Failed)
                    {
                        return GeneratedEvaluation.Failure;
                    }

                    var right = Evaluate(expression.Children[1], generated);
                    if (right.Failed)
                    {
                        return GeneratedEvaluation.Failure;
                    }

                    if (expression.Kind is GeneratedExpressionKind.Equal or
                        GeneratedExpressionKind.NotEqual)
                    {
                        var equal = Equals(left.Value, right.Value);
                        return new(false,
                            expression.Kind == GeneratedExpressionKind.Equal
                                ? equal
                                : !equal);
                    }

                    if (left.Value is not long leftInteger ||
                        right.Value is not long rightInteger)
                    {
                        return GeneratedEvaluation.Failure;
                    }

                    try
                    {
                        return expression.Kind switch
                        {
                            GeneratedExpressionKind.Add =>
                                new(false, checked(leftInteger + rightInteger)),
                            GeneratedExpressionKind.Subtract =>
                                new(false, checked(leftInteger - rightInteger)),
                            GeneratedExpressionKind.Multiply =>
                                new(false, checked(leftInteger * rightInteger)),
                            GeneratedExpressionKind.Divide =>
                                new(false, checked(leftInteger / rightInteger)),
                            GeneratedExpressionKind.Remainder =>
                                new(false, checked(leftInteger % rightInteger)),
                            GeneratedExpressionKind.LessThan =>
                                new(false, leftInteger < rightInteger),
                            GeneratedExpressionKind.LessThanOrEqual =>
                                new(false, leftInteger <= rightInteger),
                            GeneratedExpressionKind.GreaterThan =>
                                new(false, leftInteger > rightInteger),
                            GeneratedExpressionKind.GreaterThanOrEqual =>
                                new(false, leftInteger >= rightInteger),
                            _ => GeneratedEvaluation.Failure
                        };
                    }
                    catch (ArithmeticException)
                    {
                        return GeneratedEvaluation.Failure;
                    }
                }
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(expression), expression.Kind, "Unknown expression kind.");
            }

            void Count(GeneratedCSharpExpression current)
            {
                switch (current.Kind)
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
                        when current.Children[0].Type ==
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
            }
        }
    }

    private static bool HasRequiredFrontendCoverage(
        FrontendFuzzCoverage coverage)
    {
        return coverage.HasExpandedCategories;
    }

    private static TotalFiniteDomainFormula CreateTotalFiniteDomainFormula(
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
            var generated = generator.NextArithmeticOrBooleanTerm(maximumDepth: 3);
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
            var enumeration = FiniteDomainSmtDifferentialOracle
                .EnumerateFiniteDomain(
                    factory,
                    formula,
                    cancellationToken);
            if (enumeration.AllDefined)
            {
                return new TotalFiniteDomainFormula(formula, enumeration);
            }
        }
        var fallback = factory.Boolean((caseSeed & 1) == 0);
        return new TotalFiniteDomainFormula(
            fallback,
            new FiniteDomainEnumerationResult(
                AllDefined: true,
                AnyTrue: (caseSeed & 1) == 0,
                LeafEvaluations: 1));
    }

    internal static int CreateCaseSeed(int seed, int index)
    {
        // Mix a 32-bit case index with a seed-derived key. Every operation is
        // invertible modulo 2^32 (odd multiplication, addition, or an
        // xor-shift), so distinct indices remain distinct for a campaign.
        var value = unchecked(
            (uint)index + (uint)seed * 0x9E3779B9u);
        value ^= value >> 16;
        value = unchecked(value * 0x7FEB352Du);
        value ^= value >> 15;
        value = unchecked(value * 0x846CA68Bu);
        value ^= value >> 16;
        return unchecked((int)value);
    }

    internal static bool IsSemanticFrontendMismatch(
        FrontendDifferentialResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Status == FuzzOracleStatus.Mismatch &&
            !result.Detail.StartsWith(
                "Generated C# did not compile:",
                StringComparison.Ordinal);
    }

    private static int PositiveModulo(int value, int divisor)
    {
        var remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}
