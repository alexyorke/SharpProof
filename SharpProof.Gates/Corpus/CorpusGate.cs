using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.CompilerArtifact;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Corpus;

internal sealed record CorpusGateResult(
    bool Passed,
    int CaseCount,
    int BaseCaseCount,
    int OpenSourceMethodCount,
    int SupportedOpenSourceMethodCount,
    int OpenSourceFileCount,
    int SyntheticSeedCount,
    int VariantCount,
    int DiagnosticCount,
    int SupportedCaseCount,
    int IntentionallyUnsupportedCaseCount,
    int SupportedUnknownCount,
    int UnknownCount,
    int SilentUnknownCount,
    int TotalUnknownCount,
    double UnknownRate,
    double SilentUnknownRate,
    double TotalUnknownRate,
    int CacheReplayCount,
    int ConcurrentReplayCount,
    ImmutableArray<CorpusUnknownReasonCount> UnknownReasons,
    ImmutableArray<string> AllowedDegradations,
    ImmutableArray<string> Failures);

internal sealed record ContractCallCensus(int PublicOwners, int PotentialCalls, int BoundRequires,
    bool Complete, bool Unpublished, bool ManifestUnchanged, ImmutableArray<string> Failures);

internal readonly record struct ContractPhysicalSpan(string Document, int Start, int Length);
internal readonly record struct ContractCoverageKey(string OwnerId, string CalleeIdentity, int ClauseOrdinal,
    ContractPhysicalSpan Call, ContractPhysicalSpan Clause);
internal sealed record ContractCoverageMatch(int Expected, int Matched, ImmutableArray<string> Failures);

internal static class CorpusGate
{
    internal static ContractCallCensus CensusContractCalls(CSharpCompilation compilation)
    {
        var baseline = new ClaimManifestBuilder(compilation).Build();
        var shadow = new ClaimManifestBuilder(compilation).Build(includePotentialCallShadow: true);
        var inventory = shadow.PotentialCalls!;
        var owners = inventory.Owners.Where(static owner =>
            owner.Method.DeclaredAccessibility == Accessibility.Public).ToArray();
        var calls = owners.SelectMany(static owner => owner.Calls).ToArray();
        var failures = ImmutableArray.CreateBuilder<string>();
        failures.AddRange(inventory.Gaps.Select(static gap => gap.Reason));
        var boundRequires = 0;
        foreach (var call in calls)
        {
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, call.Target);
            var binding = new ContractBinder(compilation, factory).BindTotalRequires(context);
            if (!binding.IsSuccess)
            { failures.Add(binding.Failure.ToString()); continue; }
            boundRequires += binding.Clauses.Count(static clause => clause.Kind == BoundContractKind.Requires);
        }
        return new(owners.Length, calls.Length, boundRequires,
            owners.All(static owner => owner.DiscoveryComplete),
            owners.All(owner => !baseline.Manifest.Callables.Any(entry => entry.CallableId == owner.CallableId)),
            JsonSerializer.Serialize(baseline.Manifest) == JsonSerializer.Serialize(shadow.Manifest), failures.ToImmutable());
    }

    internal static ContractCoverageMatch MatchNativeContractCalls(CSharpCompilation compilation,
        IEnumerable<CompilerTotalCallablePreparation> bodies)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        var snapshots = CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None);
        var documents = compilation.SyntaxTrees.Select((tree, index) => (Tree: tree, Path: snapshots[index].Path))
            .ToDictionary(static row => row.Tree, static row => row.Path);
        var expected = new HashSet<ContractCoverageKey>();
        var inventory = new ClaimManifestBuilder(compilation).BuildPotentialCallShadow();
        failures.AddRange(inventory.Gaps.Select(static gap => gap.Reason));
        foreach (var owner in inventory.Owners.Where(static owner => owner.Method.DeclaredAccessibility == Accessibility.Public))
        {
            if (!owner.DiscoveryComplete)
            { failures.Add("DiscoveryIncomplete:" + owner.CallableId); }
            foreach (var call in owner.Calls)
            {
                if (call.OriginKind != PotentialRequiresCallOrigin.Operation || call.Target.DeclaringSyntaxReferences.Length != 1)
                { failures.Add("UnsupportedClauseOwner:" + owner.CallableId); continue; }
                var factory = new IrFactory(IrExecutionSemantics.Total);
                var context = new TotalLoweringContext(factory, call.Target);
                var binding = new ContractBinder(compilation, factory).BindTotalRequires(context);
                if (!binding.IsSuccess)
                { failures.Add("RequiresBindingFailed:" + binding.Failure); continue; }
                var ordinal = 0;
                foreach (var clause in binding.Clauses.Where(static clause => clause.Kind == BoundContractKind.Requires))
                {
                    var span = factory.GetOperationInfo(clause.SourceOperation).SourceSpan;
                    var syntax = clause.SourceSyntax;
                    if (syntax == null || !documents.TryGetValue(syntax.SyntaxTree, out var document))
                    { failures.Add("ForeignClauseTree:" + owner.CallableId); ordinal++; continue; }
                    var tree = syntax.SyntaxTree;
                    var originalPath = string.IsNullOrEmpty(tree.FilePath) ? "source" : tree.FilePath;
                    if (span == null || span.Length == 0 || span.Document != originalPath ||
                        span.Start != syntax.Span.Start || span.Length != syntax.Span.Length)
                    { failures.Add("ClauseSourceSpanMismatch:" + owner.CallableId); ordinal++; continue; }
                    var key = new ContractCoverageKey(owner.CallableId, CompilerIdentityBridge.CreateSymbolDisplay(call.Target),
                        ordinal++, new(documents[call.Syntax.SyntaxTree], call.Syntax.SpanStart, call.Syntax.Span.Length),
                        new(document, span.Start, span.Length));
                    if (!expected.Add(key))
                    { failures.Add("DuplicateSourceObligation:" + key); }
                }
            }
        }
        var seen = new HashSet<ContractCoverageKey>();
        var matched = new HashSet<ContractCoverageKey>();
        foreach (var body in bodies)
        {
            var instructions = body.Program.Blocks.SelectMany(static block => block.Instructions)
                .ToDictionary(static instruction => instruction.Id);
            foreach (var obligation in body.CallPreconditions)
            {
                if (!instructions.TryGetValue(obligation.Instruction, out var instruction) || instruction is not IrAssignInstruction marker)
                { failures.Add("NativeMarkerMissing:" + body.CallableId); continue; }
                var call = body.Program.Factory.GetOperationInfo(marker.Operation).SourceSpan;
                var clause = body.Program.Factory.GetOperationInfo(obligation.ClauseSite).SourceSpan;
                if (call == null || clause == null || call.Length == 0 || clause.Length == 0)
                { failures.Add("NativeSourceSpanMissing:" + body.CallableId); continue; }
                var key = new ContractCoverageKey(body.CallableId, obligation.CalleeIdentity, obligation.ClauseOrdinal,
                    new(call.Document, call.Start, call.Length), new(clause.Document, clause.Start, clause.Length));
                if (!seen.Add(key))
                { failures.Add("DuplicateNativeObligation:" + key); }
                else if (!expected.Contains(key))
                { failures.Add("UnexpectedNativeObligation:" + key); }
                else
                { matched.Add(key); }
            }
        }
        foreach (var missing in expected.Except(matched))
        { failures.Add("MissingNativeObligation:" + missing); }
        return new(expected.Count, matched.Count, failures.ToImmutable());
    }

    public static async Task<CorpusGateResult> RunAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var wallTime = System.Diagnostics.Stopwatch.StartNew();
        var openSourceDocument = OpenSourceCorpusCatalog.Load(repositoryRoot);
        var cases = CorpusCatalog.CreateCases(openSourceDocument);
        var openSourceCases = cases
            .Where(static item => item.Origin == CorpusOrigin.OpenSource)
            .ToImmutableArray();
        var corpusDirectory =
            OpenSourceCorpusCatalog.GetCorpusDirectory(repositoryRoot);
        var snapshotPath = Path.Combine(
            corpusDirectory,
            "expected.canonical.snapshot");
        var allowancePath = Path.Combine(
            corpusDirectory,
            "proven-to-unknown.json");
        var unknownReasonRatchetPath = Path.Combine(
            corpusDirectory,
            "unknown-reason-ratchet.json");
        var expected = LoadSnapshot(snapshotPath);
        var allowances = LoadAllowances(allowancePath);
        var unknownReasonRatchet = LoadUnknownReasonRatchet(
            unknownReasonRatchetPath);
        var observations = await ObserveAllAsync(
                cases,
                openSourceDocument,
                cancellationToken)
            .ConfigureAwait(false);

        var failures = ImmutableArray.CreateBuilder<string>();
        var usedAllowances = new HashSet<string>(StringComparer.Ordinal);
        var allowedDegradations = ImmutableArray.CreateBuilder<string>();
        if (openSourceCases.Length is
            < OpenSourceCorpusCatalog.MinimumMethodCount or
            > OpenSourceCorpusCatalog.MaximumMethodCount)
        {
            failures.Add(
                $"Corpus has {openSourceCases.Length} OSS base methods; " +
                $"{OpenSourceCorpusCatalog.MinimumMethodCount}-" +
                $"{OpenSourceCorpusCatalog.MaximumMethodCount} are required.");
        }

        var casesByIdBuilder = ImmutableDictionary.CreateBuilder<
            string, CorpusCase>(StringComparer.Ordinal);
        var duplicateCaseIds = false;
        var supportedCaseCount = 0;
        var supportedOpenSourceMethodCount = 0;
        var intentionallyUnsupportedCaseCount = 0;
        foreach (var item in cases)
        {
            if (!casesByIdBuilder.TryAdd(item.Id, item))
            {
                duplicateCaseIds = true;
            }
            if (item.Support == CorpusSupport.Supported)
            {
                supportedCaseCount++;
                if (item.Origin == CorpusOrigin.OpenSource)
                {
                    supportedOpenSourceMethodCount++;
                }
            }
            else if (item.Support == CorpusSupport.IntentionallyUnsupported)
            {
                intentionallyUnsupportedCaseCount++;
            }
        }
        if (duplicateCaseIds)
        {
            failures.Add("Corpus case IDs are not unique.");
        }

        var unclassifiedCases = cases
            .Where(static item =>
                item.Support is not (
                    CorpusSupport.Supported or
                    CorpusSupport.IntentionallyUnsupported))
            .Select(static item => item.Id)
            .ToArray();
        if (unclassifiedCases.Length != 0)
        {
            failures.Add(
                "Corpus cases require an explicit support classification: " +
                string.Join(", ", unclassifiedCases));
        }

        if (expected.Count != cases.Length)
        {
            failures.Add(
                $"Snapshot has {expected.Count} cases but generator produced {cases.Length}.");
        }

        foreach (var item in cases)
        {
            if (expected.TryGetValue(item.Id, out var baseline) &&
                baseline.Verdict != item.SemanticExpectation)
            {
                failures.Add(
                    $"Snapshot verdict for {item.Id} is {baseline.Verdict}, " +
                    $"but its reviewed semantic expectation is " +
                    $"{item.SemanticExpectation}. Do not bless a precision " +
                    $"regression by rewriting the snapshot.");
            }
        }

        foreach (var observation in observations)
        {
            if (!expected.TryGetValue(observation.CaseId, out var baseline))
            {
                failures.Add($"Missing snapshot entry: {observation.CaseId}.");
                continue;
            }
            if (Matches(baseline, observation))
            {
                continue;
            }

            if (baseline.Verdict == CorpusVerdict.Proven &&
                observation.Verdict == CorpusVerdict.Unknown &&
                allowances.TryGetValue(observation.CaseId, out var allowance))
            {
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
        {
            failures.Add($"Stale snapshot entry: {caseId}.");
        }

        foreach (var allowance in allowances.Values)
        {
            if (!usedAllowances.Contains(allowance.CaseId))
            {
                failures.Add(
                    $"Stale or unnecessary Proven->Unknown allowance: {allowance.CaseId}.");
            }
        }

        failures.AddRange(
            ValidateMetamorphicConsistency(cases, observations));
        var cacheFailures = await VerifyCacheReplayAsync(
                cases,
                observations,
                cancellationToken)
            .ConfigureAwait(false);
        failures.AddRange(cacheFailures);
        var concurrencyFailures = await VerifyConcurrentReplayAsync(
                cases,
                observations,
                cancellationToken)
            .ConfigureAwait(false);
        failures.AddRange(concurrencyFailures);

        var casesById = casesByIdBuilder.ToImmutable();
        var unknownCount = 0;
        var silentUnknownCount = 0;
        var supportedUnknownCount = 0;
        var diagnosticCount = 0;
        var unknownReasonCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            var corpusCase = casesById[observation.CaseId];
            diagnosticCount = checked(
                diagnosticCount + observation.Diagnostics.Length);
            if (observation.Verdict == CorpusVerdict.Unknown)
            {
                unknownCount++;
            }
            else if (observation.Verdict == CorpusVerdict.SilentUnknown)
            {
                silentUnknownCount++;
            }

            if (observation.Verdict is
                    CorpusVerdict.Unknown or CorpusVerdict.SilentUnknown)
            {
                if (corpusCase.Support == CorpusSupport.Supported)
                {
                    supportedUnknownCount++;
                }

                var reason = GetUnknownReason(observation);
                unknownReasonCounts[reason] =
                    unknownReasonCounts.TryGetValue(reason, out var count)
                        ? checked(count + 1)
                        : 1;
            }
        }
        var totalUnknownCount = unknownCount + silentUnknownCount;
        failures.AddRange(
            ValidateSupportedUnknownCount(supportedUnknownCount));

        ImmutableArray<CorpusUnknownReasonCount> unknownReasons = [..
            unknownReasonCounts
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .Select(static entry => new CorpusUnknownReasonCount(
                    entry.Key,
                    entry.Value))];
        ValidateUnknownReasonRatchet(
            unknownReasonRatchet,
            unknownReasons,
            totalUnknownCount,
            supportedCaseCount,
            supportedOpenSourceMethodCount,
            failures);
        var observationCount = observations.Length;
        if (wallTime.Elapsed > TimeSpan.FromMinutes(5))
        {
            failures.Add($"Corpus wall time {wallTime.Elapsed.TotalSeconds:F1}s exceeds the 300s budget.");
        }
        return new CorpusGateResult(
            failures.Count == 0,
            cases.Length,
            CorpusCatalog.Seeds.Length + openSourceCases.Length,
            openSourceCases.Length,
            supportedOpenSourceMethodCount,
            OpenSourceCorpusCatalog.GetSourceFileCount(openSourceDocument),
            CorpusCatalog.Seeds.Length,
            CorpusCatalog.Variants.Length,
            diagnosticCount,
            supportedCaseCount,
            intentionallyUnsupportedCaseCount,
            supportedUnknownCount,
            unknownCount,
            silentUnknownCount,
            totalUnknownCount,
            observationCount == 0
                ? 0
                : unknownCount / (double)observationCount,
            observationCount == 0
                ? 0
                : silentUnknownCount / (double)observationCount,
            observationCount == 0
                ? 0
                : totalUnknownCount / (double)observationCount,
            CorpusCatalog.Seeds.Length,
            CorpusCatalog.Variants.Length,
            unknownReasons,
            allowedDegradations.ToImmutable(),
            failures.ToImmutable());
    }

    internal static ImmutableArray<string> ValidateSupportedOutcomes(
        ImmutableArray<CorpusCase> cases,
        ImmutableArray<(string CaseId, CorpusVerdict Verdict)> observations)
    {
        return ValidateSupportedUnknownCount(
            CountSupportedUnknown(cases, observations));
    }

    private static ImmutableArray<string> ValidateSupportedUnknownCount(
        int count)
    {
        return count == 0
            ? []
            : [
                $"{count} supported corpus cases produced " +
                "Unknown; supported cases must have an accountable Proven " +
                "or Refuted result."
            ];
    }

    private static int CountSupportedUnknown(
        ImmutableArray<CorpusCase> cases,
        ImmutableArray<(string CaseId, CorpusVerdict Verdict)> observations)
    {
        var casesById = cases.ToImmutableDictionary(
            static item => item.Id,
            StringComparer.Ordinal);
        return observations.Count(observation =>
            casesById.TryGetValue(observation.CaseId, out var item) &&
            item.Support == CorpusSupport.Supported &&
            observation.Verdict is
                CorpusVerdict.Unknown or CorpusVerdict.SilentUnknown);
    }

    internal static ImmutableArray<string> ValidateMetamorphicConsistency(
        ImmutableArray<CorpusCase> cases,
        ImmutableArray<CorpusObservation> observations)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        var observationsById = observations.ToImmutableDictionary(
            static observation => observation.CaseId,
            StringComparer.Ordinal);
        foreach (var seed in cases
                     .Where(static item =>
                         item.Origin == CorpusOrigin.SyntheticMetamorphic)
                     .GroupBy(static item => item.SeedId, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            var baselineCase = seed.Single(static item =>
                item.Variant == CorpusVariant.Baseline);
            var baseline = observationsById[baselineCase.Id];
            var baselineDiagnosticClasses = GetDiagnosticClasses(baseline);
            foreach (var item in seed
                         .Where(static item =>
                             item.Variant != CorpusVariant.Baseline)
                         .OrderBy(static item => item.Variant))
            {
                var observation = observationsById[item.Id];
                if (observation.SemanticOutcome != baseline.SemanticOutcome)
                {
                    failures.Add(
                        $"Metamorphic variant {item.Id} changed semantic " +
                        $"outcome from {baseline.SemanticOutcome} to " +
                        $"{observation.SemanticOutcome} relative to " +
                        $"{baseline.CaseId}.");
                }

                var diagnosticClasses = GetDiagnosticClasses(observation);
                if (!baselineDiagnosticClasses.SequenceEqual(
                        diagnosticClasses,
                        StringComparer.Ordinal))
                {
                    failures.Add(
                        $"Metamorphic variant {item.Id} changed diagnostic " +
                        $"classes from [{string.Join(", ", baselineDiagnosticClasses)}] " +
                        $"to [{string.Join(", ", diagnosticClasses)}] " +
                        $"relative to {baseline.CaseId}.");
                }
            }
        }
        return failures.ToImmutable();
    }

    private static ImmutableArray<string> GetDiagnosticClasses(
        CorpusObservation observation)
    {
        return [.. observation.Diagnostics.Select(static diagnostic =>
        {
            var diagnosticSpan = diagnostic.AsSpan();
            var idSeparator = diagnosticSpan.IndexOf('@');
            var locationSeparator = idSeparator + 1 +
                diagnosticSpan[(idSeparator + 1)..].IndexOf('@');
            return diagnostic[..locationSeparator];
        })];
    }

    private static async Task<string> RenderActualSnapshotAsync(
        OpenSourceCorpusDocument openSourceDocument,
        CancellationToken cancellationToken)
    {
        var observations = await ObserveAllAsync(
                CorpusCatalog.CreateSyntheticCases(),
                openSourceDocument,
                cancellationToken)
            .ConfigureAwait(false);
        return CorpusSnapshotFormat.Render(
            observations
                .Select(static observation => observation.ToCanonicalLine())
                .OrderBy(static line => line, StringComparer.Ordinal));
    }

    public static async Task WriteActualSnapshotAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var import = await OpenSourceCorpusImporter.PrepareIfRequestedAsync(
                repositoryRoot,
                cancellationToken)
            .ConfigureAwait(false);
        var document = import?.Document ??
            OpenSourceCorpusCatalog.Load(repositoryRoot);
        var corpusDirectory =
            OpenSourceCorpusCatalog.GetCorpusDirectory(repositoryRoot);
        var snapshotPath = Path.Combine(
            corpusDirectory,
            "expected.canonical.snapshot");
        var snapshot = await RenderActualSnapshotAsync(
                document,
                cancellationToken)
            .ConfigureAwait(false);
        var updates = import?.Updates.ToList() ?? [];
        updates.Add(new CorpusFileUpdate(snapshotPath, snapshot));
        await CorpusFileTransaction.WriteAllAsync(
                corpusDirectory,
                updates,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<CorpusObservation> ObserveCaseAsync(
        CorpusCase item,
        CancellationToken cancellationToken)
    {
        var analysis = await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(
                item.Source,
                item.Mode,
                cancellationToken)
            .ConfigureAwait(false);
        return Observe(item, analysis, await NativeOutcomeAsync(item, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<CorpusObservation> ReplayCaseAsync(
        CorpusCase item,
        WorkerClaimOutcome? nativeOutcome,
        CancellationToken cancellationToken)
    {
        var analysis = await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(
                item.Source,
                item.Mode,
                cancellationToken)
            .ConfigureAwait(false);
        return Observe(item, analysis, nativeOutcome);
    }

    // The worker's verdict on the case's public callable, or null when it
    // declares no claim the worker decides.
    private static async Task<WorkerClaimOutcome?> NativeOutcomeAsync(CorpusCase item, CancellationToken cancellationToken)
    {
        var verdicts = (await NativeCorpusVerifier.VerifyAsync(AnalyzerGateHost.CreateCompilation(item.Source), cancellationToken)
            .ConfigureAwait(false)).Where(static verdict => verdict.Method.DeclaredAccessibility == Accessibility.Public).ToArray();
        return verdicts.Length switch
        {
            0 => null,
            1 => verdicts[0].Outcome,
            _ => throw new InvalidOperationException($"Corpus case {item.Id} has {verdicts.Length} public verified callables.")
        };
    }

    private static async Task<ImmutableArray<CorpusObservation>> ObserveAllAsync(
        ImmutableArray<CorpusCase> cases,
        OpenSourceCorpusDocument openSourceDocument,
        CancellationToken cancellationToken)
    {
        var observations = ImmutableArray.CreateBuilder<CorpusObservation>();
        foreach (var item in cases.Where(static item =>
                     item.Origin == CorpusOrigin.SyntheticMetamorphic))
        {
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
        return observations.ToImmutable();
    }

    private static CorpusObservation Observe(
        CorpusCase item,
        AnalyzerGateAnalysis analysis,
        WorkerClaimOutcome? nativeOutcome)
    {
        var targets = analysis.SemanticOutcomes
            .Where(static outcome =>
                outcome.Accessibility == Accessibility.Public)
            .ToImmutableArray();
        if (targets.Length != 1)
        {
            throw new InvalidOperationException(
                $"Corpus case {item.Id} produced {targets.Length} public-method " +
                "semantic outcomes; exactly one is required.");
        }

        var diagnostics = analysis.Diagnostics
            .Select(diagnostic => CanonicalizeDiagnostic(
                diagnostic,
                analysis.CompilationOptions))
            .OrderBy(static diagnostic => diagnostic, StringComparer.Ordinal)
            .ToImmutableArray();
        var semanticOutcome = targets[0].Outcome;
        var verdict = ToVerdict(
            semanticOutcome,
            nativeOutcome,
            diagnostics.IsDefaultOrEmpty);
        return new CorpusObservation(
            item.Id,
            verdict,
            semanticOutcome,
            diagnostics)
        { NativeOutcome = nativeOutcome };
    }

    private static bool Matches(
        CorpusObservation expected,
        CorpusObservation actual)
    {
        return expected.Verdict == actual.Verdict &&
        expected.SemanticOutcome == actual.SemanticOutcome &&
        expected.Diagnostics.SequenceEqual(
            actual.Diagnostics,
            StringComparer.Ordinal);
    }

    private static async Task<ImmutableArray<string>> VerifyCacheReplayAsync(
        ImmutableArray<CorpusCase> cases,
        ImmutableArray<CorpusObservation> firstPass,
        CancellationToken cancellationToken)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        var byId = firstPass.ToImmutableDictionary(
            static observation => observation.CaseId,
            StringComparer.Ordinal);
        foreach (var item in cases.Where(static item =>
                     item.Origin == CorpusOrigin.SyntheticMetamorphic))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = AnalyzerGateHost.CreateCompilation(
                item.Source,
                $"CacheReplay_{item.SeedId}");
            var nativeOutcome = byId[item.Id].NativeOutcome;
            var first = Observe(
                item,
                await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(
                        compilation,
                        item.Mode,
                        concurrentAnalysis: true,
                        cancellationToken)
                    .ConfigureAwait(false),
                nativeOutcome);
            var second = Observe(
                item,
                await AnalyzerGateHost.AnalyzeWithSemanticOutcomesAsync(
                        compilation,
                        item.Mode,
                        concurrentAnalysis: true,
                        cancellationToken)
                    .ConfigureAwait(false),
                nativeOutcome);
            if (!Matches(byId[item.Id], first) ||
                !Matches(byId[item.Id], second))
            {
                failures.Add($"Cache replay changed {item.Id}.");
            }
        }
        return failures.ToImmutable();
    }

    private static async Task<ImmutableArray<string>> VerifyConcurrentReplayAsync(
        ImmutableArray<CorpusCase> cases,
        ImmutableArray<CorpusObservation> firstPass,
        CancellationToken cancellationToken)
    {
        var selected = cases.Where(static item =>
            item.Origin == CorpusOrigin.SyntheticMetamorphic);
        var expected = firstPass.ToImmutableDictionary(
            static observation => observation.CaseId,
            StringComparer.Ordinal);
        var observations = await Task.WhenAll(
            selected.Select(item => ReplayCaseAsync(item, expected[item.Id].NativeOutcome, cancellationToken)))
            .ConfigureAwait(false);
        return [.. observations
            .Where(observation => !Matches(expected[observation.CaseId], observation))
            .OrderBy(static observation => observation.CaseId, StringComparer.Ordinal)
            .Select(static observation =>
                $"Concurrent replay changed {observation.CaseId}.")];
    }

    private static ImmutableDictionary<string, CorpusObservation> LoadSnapshot(
        string path)
    {
        var result = ImmutableDictionary.CreateBuilder<
            string,
            CorpusObservation>(StringComparer.Ordinal);
        foreach (var expectation in CorpusSnapshotFormat.ReadObservations(path))
        {
            if (!result.TryAdd(
                    expectation.CaseId,
                    expectation))
            {
                throw new InvalidDataException(
                    $"Duplicate corpus snapshot case: {expectation.CaseId}");
            }
        }
        return result.ToImmutable();
    }

    // The worker decides every claim it verifies; the analyzer decides only
    // call-site preconditions and trusted boundaries.
    internal static CorpusVerdict ToVerdict(
        AnalyzerSemanticOutcome semanticOutcome,
        WorkerClaimOutcome? nativeOutcome,
        bool hasNoDiagnostics)
    {
        return (semanticOutcome, nativeOutcome) switch
        {
            (AnalyzerSemanticOutcome.Refuted, _) or (_, WorkerClaimOutcome.Refuted) => CorpusVerdict.Refuted,
            (_, WorkerClaimOutcome.Proven) or (AnalyzerSemanticOutcome.Proven, null) => CorpusVerdict.Proven,
            _ when hasNoDiagnostics => CorpusVerdict.SilentUnknown,
            _ => CorpusVerdict.Unknown
        };
    }

    internal static string CanonicalizeDiagnostic(
        Diagnostic diagnostic,
        CompilationOptions compilationOptions)
    {
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
        CompilationOptions compilationOptions)
    {
        if (!compilationOptions.SpecificDiagnosticOptions.TryGetValue(
                diagnostic.Id,
                out var action) ||
            action == ReportDiagnostic.Default)
        {
            return diagnostic.Severity;
        }

        return action switch
        {
            ReportDiagnostic.Error => DiagnosticSeverity.Error,
            ReportDiagnostic.Warn => DiagnosticSeverity.Warning,
            ReportDiagnostic.Info => DiagnosticSeverity.Info,
            ReportDiagnostic.Hidden => DiagnosticSeverity.Hidden,
            ReportDiagnostic.Suppress => throw new InvalidOperationException(
                $"Suppressed diagnostic {diagnostic.Id} was reported."),
            _ => diagnostic.Severity
        };
    }

    private static string NormalizeLocation(Location location)
    {
        if (location == Location.None || !location.IsInSource)
        {
            return "none";
        }

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

    private static string GetUnknownReason(CorpusObservation observation)
    {
        if (observation.Diagnostics.IsDefaultOrEmpty)
        {
            return observation.Verdict == CorpusVerdict.SilentUnknown
                ? "silent-unclassified"
                : "unknown-unclassified";
        }

        return string.Join(
            "+",
            observation.Diagnostics
                .Select(static diagnostic =>
                {
                    var separator = diagnostic.IndexOf(
                        '@',
                        StringComparison.Ordinal);
                    return separator < 0
                        ? diagnostic
                        : diagnostic[..separator];
                })
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static diagnostic => diagnostic, StringComparer.Ordinal));
    }

    internal static void ValidateUnknownReasonRatchet(
        CorpusUnknownReasonRatchet ratchet,
        ImmutableArray<CorpusUnknownReasonCount> actual,
        int totalUnknownCount,
        int supportedCaseCount,
        int supportedOpenSourceMethodCount,
        ImmutableArray<string>.Builder failures)
    {
        if (supportedCaseCount < ratchet.MinimumSupportedCases)
        {
            failures.Add(
                "Corpus support regressed from the ratcheted minimum of " +
                $"{ratchet.MinimumSupportedCases} cases to " +
                $"{supportedCaseCount}.");
        }

        if (supportedOpenSourceMethodCount <
            ratchet.MinimumSupportedOpenSourceMethods)
        {
            failures.Add(
                "Supported OSS corpus coverage regressed from the ratcheted " +
                $"minimum of {ratchet.MinimumSupportedOpenSourceMethods} " +
                $"methods to {supportedOpenSourceMethodCount}.");
        }

        if (totalUnknownCount > ratchet.MaximumTotalUnknown)
        {
            failures.Add(
                $"Corpus Unknown count regressed from the ratcheted maximum " +
                $"{ratchet.MaximumTotalUnknown} to {totalUnknownCount}.");
        }

        foreach (var item in actual)
        {
            if (!ratchet.MaximumByReason.TryGetValue(
                    item.Reason,
                    out var maximum))
            {
                failures.Add(
                    $"Corpus produced a new unreviewed Unknown reason " +
                    $"'{item.Reason}' ({item.Count} cases).");
                continue;
            }
            if (item.Count > maximum)
            {
                failures.Add(
                    $"Corpus Unknown reason '{item.Reason}' regressed from " +
                    $"the ratcheted maximum {maximum} to {item.Count}.");
            }
        }

        var observedReasons = actual
            .Select(static item => item.Reason)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var reason in ratchet.MaximumByReason.Keys
                     .Where(reason => !observedReasons.Contains(reason))
                     .OrderBy(static reason => reason, StringComparer.Ordinal))
        {
            failures.Add(
                $"Corpus Unknown reason '{reason}' is no longer observed; " +
                "remove its stale ratchet ceiling.");
        }
    }

    private static CorpusUnknownReasonRatchet LoadUnknownReasonRatchet(
        string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2)
        {
            throw new InvalidDataException(
                "Unsupported corpus Unknown-reason ratchet schema.");
        }

        var minimumSupportedCases =
            root.GetProperty("minimumSupportedCases").GetInt32();
        var minimumSupportedOpenSourceMethods =
            root.GetProperty("minimumSupportedOpenSourceMethods").GetInt32();
        var maximumTotalUnknown =
            root.GetProperty("maximumTotalUnknown").GetInt32();
        if (minimumSupportedCases < 0 ||
            minimumSupportedOpenSourceMethods < 0 ||
            maximumTotalUnknown < 0)
        {
            throw new InvalidDataException(
                "Corpus support minima and Unknown maxima cannot be negative.");
        }

        var maximumByReason =
            ImmutableDictionary.CreateBuilder<string, int>(
                StringComparer.Ordinal);
        foreach (var property in root.GetProperty("maximumByReason")
                     .EnumerateObject())
        {
            var maximum = property.Value.GetInt32();
            if (string.IsNullOrWhiteSpace(property.Name) || maximum < 0)
            {
                throw new InvalidDataException(
                    "Corpus Unknown-reason maxima need a name and " +
                    "a non-negative count.");
            }

            if (!maximumByReason.TryAdd(property.Name, maximum))
            {
                throw new InvalidDataException(
                    $"Duplicate corpus Unknown reason: {property.Name}.");
            }
        }
        return new CorpusUnknownReasonRatchet(
            minimumSupportedCases,
            minimumSupportedOpenSourceMethods,
            maximumTotalUnknown,
            maximumByReason.ToImmutable());
    }

    private static ImmutableDictionary<string, ProvenToUnknownAllowance>
        LoadAllowances(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw new InvalidDataException(
                "Unsupported Proven->Unknown allowance schema.");
        }

        var result = ImmutableDictionary.CreateBuilder<
            string,
            ProvenToUnknownAllowance>(StringComparer.Ordinal);
        foreach (var element in document.RootElement
                     .GetProperty("allowances")
                     .EnumerateArray())
        {
            var caseId = element.GetProperty("caseId").GetString();
            var reason = element.GetProperty("reason").GetString();
            if (string.IsNullOrWhiteSpace(caseId) ||
                string.IsNullOrWhiteSpace(reason))
            {
                throw new InvalidDataException(
                    "Every Proven->Unknown allowance needs a caseId and explanation.");
            }

            if (!result.TryAdd(
                    caseId,
                    new ProvenToUnknownAllowance(caseId, reason)))
            {
                throw new InvalidDataException(
                    $"Duplicate Proven->Unknown allowance: {caseId}");
            }
        }
        return result.ToImmutable();
    }
}
