using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer;

namespace SharpProof.V2Gates.Corpus;

public sealed record CorpusGateResult(
    bool Passed,
    int CaseCount,
    int BaseCaseCount,
    int OpenSourceMethodCount,
    int OpenSourceFileCount,
    int SyntheticSeedCount,
    int VariantCount,
    int DiagnosticCount,
    int CacheReplayCount,
    int ConcurrentReplayCount,
    ImmutableArray<string> AllowedDegradations,
    ImmutableArray<string> Failures);

public static class CorpusGate {
    public static async Task<CorpusGateResult> RunAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default) {
        var openSourceDocument = OpenSourceCorpusCatalog.Load(repositoryRoot);
        var cases = CorpusCatalog.CreateCases(repositoryRoot);
        var openSourceCases = cases
            .Where(static item => item.Origin == CorpusOrigin.OpenSource)
            .ToImmutableArray();
        var snapshotPath = Path.Combine(
            repositoryRoot,
            "SharpProof.V2Gates",
            "Corpus",
            "expected.canonical.snapshot");
        var allowancePath = Path.Combine(
            repositoryRoot,
            "SharpProof.V2Gates",
            "Corpus",
            "proven-to-unknown.json");
        var expected = LoadSnapshot(snapshotPath);
        var allowances = LoadAllowances(allowancePath);
        var observations = ImmutableArray.CreateBuilder<CorpusObservation>();
        foreach (var item in cases.Where(static item =>
                     item.Origin == CorpusOrigin.SyntheticMetamorphic)) {
            cancellationToken.ThrowIfCancellationRequested();
            observations.Add(
                await ObserveCaseAsync(item, cancellationToken)
                    .ConfigureAwait(false));
        }
        observations.AddRange(
            await OpenSourceCorpusRunner.ObserveAsync(
                    openSourceDocument,
                    cancellationToken)
                .ConfigureAwait(false));

        var failures = ImmutableArray.CreateBuilder<string>();
        var usedAllowances = new HashSet<string>(StringComparer.Ordinal);
        var allowedDegradations = ImmutableArray.CreateBuilder<string>();
        if (openSourceCases.Length is
            < OpenSourceCorpusCatalog.MinimumMethodCount or
            > OpenSourceCorpusCatalog.MaximumMethodCount)
            failures.Add(
                $"Corpus has {openSourceCases.Length} OSS base methods; " +
                $"{OpenSourceCorpusCatalog.MinimumMethodCount}-" +
                $"{OpenSourceCorpusCatalog.MaximumMethodCount} are required.");
        if (cases.Select(static item => item.Id).Distinct(StringComparer.Ordinal)
                .Count() != cases.Length)
            failures.Add("Corpus case IDs are not unique.");
        if (expected.Count != cases.Length)
            failures.Add(
                $"Snapshot has {expected.Count} cases but generator produced {cases.Length}.");

        foreach (var item in cases) {
            if (expected.TryGetValue(item.Id, out var baseline) &&
                baseline.Verdict != item.SemanticExpectation)
                failures.Add(
                    $"Snapshot verdict for {item.Id} is {baseline.Verdict}, " +
                    $"but its reviewed semantic expectation is " +
                    $"{item.SemanticExpectation}. Do not bless a precision " +
                    $"regression by rewriting the snapshot.");
        }

        foreach (var observation in observations) {
            if (!expected.TryGetValue(observation.CaseId, out var baseline)) {
                failures.Add($"Missing snapshot entry: {observation.CaseId}.");
                continue;
            }
            if (Matches(baseline, observation))
                continue;
            if (baseline.Verdict == CorpusVerdict.Proven &&
                observation.Verdict == CorpusVerdict.Unknown &&
                allowances.TryGetValue(observation.CaseId, out var allowance)) {
                usedAllowances.Add(observation.CaseId);
                allowedDegradations.Add(
                    $"{observation.CaseId}: {allowance.Reason}");
                continue;
            }
            failures.Add(
                $"Snapshot mismatch for {observation.CaseId}: expected " +
                $"{baseline.ToCanonicalLine()}, actual {observation.ToCanonicalLine()}.");
        }

        foreach (var caseId in expected.Keys.Except(
                     cases.Select(static item => item.Id),
                     StringComparer.Ordinal))
            failures.Add($"Stale snapshot entry: {caseId}.");
        foreach (var allowance in allowances.Values)
            if (!usedAllowances.Contains(allowance.CaseId))
                failures.Add(
                    $"Stale or unnecessary Proven->Unknown allowance: {allowance.CaseId}.");

        var immutableObservations = observations.ToImmutable();
        var cacheFailures = await VerifyCacheReplayAsync(
                cases,
                immutableObservations,
                cancellationToken)
            .ConfigureAwait(false);
        failures.AddRange(cacheFailures);
        var concurrencyFailures = await VerifyConcurrentReplayAsync(
                cases,
                immutableObservations,
                cancellationToken)
            .ConfigureAwait(false);
        failures.AddRange(concurrencyFailures);

        return new CorpusGateResult(
            failures.Count == 0,
            cases.Length,
            CorpusCatalog.Seeds.Length + openSourceCases.Length,
            openSourceCases.Length,
            openSourceDocument.Methods
                .Select(static method => method.Path)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            CorpusCatalog.Seeds.Length,
            CorpusCatalog.Variants.Length,
            observations.Sum(static observation =>
                observation.Diagnostics.Length),
            CorpusCatalog.Seeds.Length,
            CorpusCatalog.Variants.Length,
            allowedDegradations.ToImmutable(),
            failures.ToImmutable());
    }

    public static async Task<string> RenderActualSnapshotAsync(
        string? repositoryRoot = null,
        CancellationToken cancellationToken = default) {
        repositoryRoot ??= RepositoryLayout.FindRoot();
        var openSourceDocument = OpenSourceCorpusCatalog.Load(repositoryRoot);
        var lines = new List<string> {
            "# SharpProof v2 analyzer corpus snapshot schema 3",
            "# case-id|verdict|semantic-outcome|sorted-diagnostics",
            "# diagnostic=id@effective-severity@normalized-location@base64-invariant-message"
        };
        foreach (var item in CorpusCatalog.CreateCases(repositoryRoot)
                     .Where(static item =>
                         item.Origin ==
                         CorpusOrigin.SyntheticMetamorphic)) {
            cancellationToken.ThrowIfCancellationRequested();
            lines.Add(
                (await ObserveCaseAsync(item, cancellationToken).ConfigureAwait(false))
                .ToCanonicalLine());
        }
        lines.AddRange(
            (await OpenSourceCorpusRunner.ObserveAsync(
                    openSourceDocument,
                    cancellationToken)
                .ConfigureAwait(false))
            .Select(static observation => observation.ToCanonicalLine()));
        return string.Join("\n", lines) + "\n";
    }

    public static async Task WriteActualSnapshotAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default) {
        await OpenSourceCorpusImporter.ImportIfRequestedAsync(
                repositoryRoot,
                cancellationToken)
            .ConfigureAwait(false);
        var snapshotPath = Path.Combine(
            repositoryRoot,
            "SharpProof.V2Gates",
            "Corpus",
            "expected.canonical.snapshot");
        var snapshot = await RenderActualSnapshotAsync(
                repositoryRoot,
                cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                snapshotPath,
                snapshot,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<CorpusObservation> ObserveCaseAsync(
        CorpusCase item,
        CancellationToken cancellationToken) {
        var analysis = await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(
                item.Source,
                item.Mode,
                cancellationToken)
            .ConfigureAwait(false);
        return Observe(item, analysis);
    }

    private static CorpusObservation Observe(
        CorpusCase item,
        AnalyzerGateAnalysis analysis) {
        var targets = analysis.SemanticOutcomes
            .Where(static outcome =>
                outcome.Accessibility == Accessibility.Public)
            .ToImmutableArray();
        if (targets.Length != 1)
            throw new InvalidOperationException(
                $"Corpus case {item.Id} produced {targets.Length} public-method " +
                "semantic outcomes; exactly one is required.");
        var diagnostics = analysis.Diagnostics
            .Select(diagnostic => CanonicalizeDiagnostic(
                diagnostic,
                analysis.CompilationOptions))
            .OrderBy(static diagnostic => diagnostic, StringComparer.Ordinal)
            .ToImmutableArray();
        var semanticOutcome = targets[0].Outcome;
        var verdict = ToVerdict(
            semanticOutcome,
            diagnostics.IsDefaultOrEmpty);
        return new CorpusObservation(
            item.Id,
            verdict,
            semanticOutcome,
            diagnostics);
    }

    private static bool Matches(
        SnapshotExpectation expected,
        CorpusObservation actual) =>
        expected.Verdict == actual.Verdict &&
        expected.SemanticOutcome == actual.SemanticOutcome &&
        expected.Diagnostics.SequenceEqual(
            actual.Diagnostics,
            StringComparer.Ordinal);

    private static bool Matches(
        CorpusObservation expected,
        CorpusObservation actual) =>
        expected.Verdict == actual.Verdict &&
        expected.SemanticOutcome == actual.SemanticOutcome &&
        expected.Diagnostics.SequenceEqual(
            actual.Diagnostics,
            StringComparer.Ordinal);

    private static async Task<ImmutableArray<string>> VerifyCacheReplayAsync(
        ImmutableArray<CorpusCase> cases,
        ImmutableArray<CorpusObservation> firstPass,
        CancellationToken cancellationToken) {
        var failures = ImmutableArray.CreateBuilder<string>();
        var byId = firstPass.ToImmutableDictionary(
            static observation => observation.CaseId,
            StringComparer.Ordinal);
        foreach (var item in cases.Where(static item =>
                     item.Origin == CorpusOrigin.SyntheticMetamorphic &&
                     item.Variant == CorpusVariant.Baseline)) {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = AnalyzerGateHost.CreateCompilation(
                item.Source,
                $"CacheReplay_{item.SeedId}");
            var first = Observe(
                item,
                await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(
                        compilation,
                        item.Mode,
                        concurrentAnalysis: true,
                        cancellationToken)
                    .ConfigureAwait(false));
            var second = Observe(
                item,
                await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(
                        compilation,
                        item.Mode,
                        concurrentAnalysis: true,
                        cancellationToken)
                    .ConfigureAwait(false));
            if (!Matches(byId[item.Id], first) ||
                !Matches(byId[item.Id], second))
                failures.Add($"Cache replay changed {item.Id}.");
        }
        return failures.ToImmutable();
    }

    private static async Task<ImmutableArray<string>> VerifyConcurrentReplayAsync(
        ImmutableArray<CorpusCase> cases,
        ImmutableArray<CorpusObservation> firstPass,
        CancellationToken cancellationToken) {
        var selected = CorpusCatalog.Variants
            .Select(variant => cases.First(item =>
                item.Origin == CorpusOrigin.SyntheticMetamorphic &&
                item.Variant == variant))
            .ToImmutableArray();
        var expected = firstPass.ToImmutableDictionary(
            static observation => observation.CaseId,
            StringComparer.Ordinal);
        var bag = new ConcurrentBag<CorpusObservation>();
        await Task.WhenAll(selected.Select(async item => {
            var observation = await ObserveCaseAsync(item, cancellationToken)
                .ConfigureAwait(false);
            bag.Add(observation);
        })).ConfigureAwait(false);
        return [.. bag
            .Where(observation => !Matches(expected[observation.CaseId], observation))
            .OrderBy(static observation => observation.CaseId, StringComparer.Ordinal)
            .Select(static observation =>
                $"Concurrent replay changed {observation.CaseId}.")];
    }

    private static ImmutableDictionary<string, SnapshotExpectation> LoadSnapshot(
        string path) {
        var result = ImmutableDictionary.CreateBuilder<
            string,
            SnapshotExpectation>(StringComparer.Ordinal);
        foreach (var rawLine in File.ReadAllLines(path)) {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                continue;
            var parts = line.Split('|');
            if (parts.Length != 4 ||
                !Enum.TryParse<CorpusVerdict>(
                    parts[1],
                    ignoreCase: false,
                    out var verdict) ||
                !Enum.TryParse<AnalyzerSemanticOutcome>(
                    parts[2],
                    ignoreCase: false,
                    out var semanticOutcome))
                throw new InvalidDataException(
                    $"Invalid corpus snapshot line: {rawLine}");
            ImmutableArray<string> diagnostics = parts[3].Length == 0
                ? []
                : [.. parts[3].Split(',')
                    .OrderBy(static diagnostic =>
                        diagnostic,
                        StringComparer.Ordinal)
                ];
            if (!result.TryAdd(
                    parts[0],
                    new SnapshotExpectation(
                        parts[0],
                        verdict,
                        semanticOutcome,
                        diagnostics)))
                throw new InvalidDataException(
                    $"Duplicate corpus snapshot case: {parts[0]}");
        }
        return result.ToImmutable();
    }

    internal static CorpusVerdict ToVerdict(
        AnalyzerSemanticOutcome semanticOutcome,
        bool hasNoDiagnostics) =>
        semanticOutcome switch {
            AnalyzerSemanticOutcome.Proven => CorpusVerdict.Proven,
            AnalyzerSemanticOutcome.Refuted => CorpusVerdict.Refuted,
            _ when hasNoDiagnostics => CorpusVerdict.SilentUnknown,
            _ => CorpusVerdict.Unknown
        };

    internal static string CanonicalizeDiagnostic(
        Diagnostic diagnostic,
        CompilationOptions compilationOptions) {
        var severity = GetEffectiveSeverity(diagnostic, compilationOptions);
        var location = NormalizeLocation(diagnostic.Location);
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var encodedMessage = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(message));
        return $"{diagnostic.Id}@{severity}@{location}@{encodedMessage}";
    }

    private static DiagnosticSeverity GetEffectiveSeverity(
        Diagnostic diagnostic,
        CompilationOptions compilationOptions) {
        if (!compilationOptions.SpecificDiagnosticOptions.TryGetValue(
                diagnostic.Id,
                out var action) ||
            action == ReportDiagnostic.Default)
            return diagnostic.Severity;
        return action switch {
            ReportDiagnostic.Error => DiagnosticSeverity.Error,
            ReportDiagnostic.Warn => DiagnosticSeverity.Warning,
            ReportDiagnostic.Info => DiagnosticSeverity.Info,
            ReportDiagnostic.Hidden => DiagnosticSeverity.Hidden,
            ReportDiagnostic.Suppress => throw new InvalidOperationException(
                $"Suppressed diagnostic {diagnostic.Id} was reported."),
            _ => diagnostic.Severity
        };
    }

    private static string NormalizeLocation(Location location) {
        if (location == Location.None || !location.IsInSource)
            return "none";
        var lineSpan = location.GetMappedLineSpan();
        var path = Path.GetFileName(lineSpan.Path)
            .Replace('\\', '/');
        var start = lineSpan.StartLinePosition;
        var end = lineSpan.EndLinePosition;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{path}:{start.Line + 1}:{start.Character + 1}-" +
            $"{end.Line + 1}:{end.Character + 1}");
    }

    private static ImmutableDictionary<string, ProvenToUnknownAllowance>
        LoadAllowances(string path) {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException(
                "Unsupported Proven->Unknown allowance schema.");
        var result = ImmutableDictionary.CreateBuilder<
            string,
            ProvenToUnknownAllowance>(StringComparer.Ordinal);
        foreach (var element in document.RootElement
                     .GetProperty("allowances")
                     .EnumerateArray()) {
            var caseId = element.GetProperty("caseId").GetString();
            var reason = element.GetProperty("reason").GetString();
            if (string.IsNullOrWhiteSpace(caseId) ||
                string.IsNullOrWhiteSpace(reason))
                throw new InvalidDataException(
                    "Every Proven->Unknown allowance needs a caseId and explanation.");
            if (!result.TryAdd(
                    caseId,
                    new ProvenToUnknownAllowance(caseId, reason)))
                throw new InvalidDataException(
                    $"Duplicate Proven->Unknown allowance: {caseId}");
        }
        return result.ToImmutable();
    }
}
