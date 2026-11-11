using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer;
using SharpProof.Compiler.Semantics;
using SharpProof.Ir;

namespace SharpProof.Gates.Corpus;

internal static class OpenSourceCorpusRunner
{
    private const string AnnotationKind = "SharpProofOssCorpusMethod";

    // Each source compiles and verifies as its own assembly. Observations
    // follow the document's method order.
    internal static async Task<ImmutableArray<CorpusObservation>> ObserveAsync(
        OpenSourceCorpusDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var bySource = ImmutableDictionary.CreateBuilder<string, CorpusObservation>(StringComparer.Ordinal);
        foreach (var source in SplitBySource(document))
        {
            foreach (var observation in await ObserveSourceAsync(source, document, cancellationToken).ConfigureAwait(false))
            {
                bySource.Add(observation.CaseId, observation);
            }
        }

        return [.. document.Methods.Select(method => bySource[$"{method.Id}.baseline"])];
    }

    // One document per source with selected methods: its files and methods,
    // in document order. Sources without a selected method are not compiled.
    internal static ImmutableArray<OpenSourceCorpusDocument> SplitBySource(
        OpenSourceCorpusDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sourceIds = document.Methods.Select(static method => method.SourceId)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        if (sourceIds.Length <= 1 &&
            document.Files.All(file => sourceIds.Contains(file.SourceId, StringComparer.Ordinal)))
        {
            return [document];
        }

        return [.. sourceIds.Select(id => document with
        {
            Sources = [.. (document.Sources.IsDefault ? [] : document.Sources)
                .Where(source => source is not null && string.Equals(source.Id, id, StringComparison.Ordinal))],
            Files = [.. document.Files.Where(file => string.Equals(file.SourceId, id, StringComparison.Ordinal))],
            Methods = [.. document.Methods.Where(method => string.Equals(method.SourceId, id, StringComparison.Ordinal))]
        })];
    }

    private static async Task<ImmutableArray<CorpusObservation>> ObserveSourceAsync(
        OpenSourceCorpusDocument document,
        OpenSourceCorpusDocument loaded,
        CancellationToken cancellationToken)
    {
        var prepared = Prepare(document, "global::SharpProof.Attributes.EnforcePure", cancellationToken, loaded: loaded);
        var compilation = prepared.Compilation;
        var targetMap = prepared.Targets;
        var diagnosticsByMethod = ImmutableDictionaryCapture.Create(
            targetMap.Values.Select(static target => target.Method.Id),
            static id => id,
            static _ => ImmutableArray.CreateBuilder<string>(),
            StringComparer.Ordinal);
        var factory = new RecordingSessionFactory(targetMap);
        var diagnostics = await AnalyzerGateHost.AnalyzeAsync(
                compilation,
                new SharpProofAnalyzer(factory),
                "effects",
                concurrentAnalysis: true,
                cancellationToken)
            .ConfigureAwait(false);
        var outcomes = factory.GetOutcomes();
        var nativeRows = (await NativeCorpusVerifier.VerifyAsync(compilation, cancellationToken).ConfigureAwait(false))
            .SelectMany(verdict => FindCallableTargets(verdict.Method, targetMap)
                .Select(target => target is null ? throw new System.ArgumentNullException(nameof(target)) : ((target.Method.Id, verdict.Outcome))));
        var nativeOutcomes = ImmutableDictionaryCapture.Create(nativeRows,
            static item => item.Id, static item => item.Outcome, StringComparer.Ordinal);
        var observations = ImmutableArray.CreateBuilder<CorpusObservation>(
            document.Methods.Length);
        var diagnosticAssignments = ImmutableArray.CreateBuilder<int>(diagnostics.Length);
        for (var index = 0; index < diagnostics.Length; index++)
        { diagnosticAssignments.Add(0); }
        var groupedTargets = targetMap.Values
            .SelectMany(target => DiagnosticTargets(compilation, target, cancellationToken))
            .GroupBy(
                static target => target.Tree,
                (IEqualityComparer<SyntaxTree>)ReferenceEqualityComparer.Instance);
        var targetsByTree = ImmutableDictionaryCapture.Create(groupedTargets,
            static group => group.Key,
            static group => group.ToImmutableArray(),
            (IEqualityComparer<SyntaxTree>)ReferenceEqualityComparer.Instance);
        for (var index = 0; index < diagnostics.Length; index++)
        {
            var diagnostic = diagnostics[index];
            if (diagnostic.Location.SourceTree is not { } tree ||
                !targetsByTree.TryGetValue(tree, out var treeTargets))
            {
                continue;
            }

            foreach (var target in treeTargets)
            {
                if (!target.Span.Contains(diagnostic.Location.SourceSpan))
                {
                    continue;
                }

                diagnosticAssignments[index]++;
                diagnosticsByMethod[target.Method.Id].Add(
                    CorpusGate.CanonicalizeDiagnostic(
                        diagnostic,
                        compilation.Options));
            }
        }
        foreach (var method in document.Methods)
        {
            if (!outcomes.TryGetValue(method.Id, out var semanticOutcome))
            {
                throw new InvalidDataException(
                    $"Analyzer did not record an outcome for OSS method {method.Id}.");
            }

            var methodDiagnostics = diagnosticsByMethod[method.Id].ToImmutable();
            var canonicalDiagnostics = methodDiagnostics
                .OrderBy(static diagnostic => diagnostic, StringComparer.Ordinal)
                .ToImmutableArray();
            observations.Add(
                new CorpusObservation(
                    $"{method.Id}.baseline",
                    CorpusGate.ToVerdict(
                        semanticOutcome,
                        nativeOutcomes.TryGetValue(method.Id, out var nativeOutcome) ? nativeOutcome : null,
                        canonicalDiagnostics.IsDefaultOrEmpty),
                    semanticOutcome,
                    canonicalDiagnostics));
        }
        RequireCompleteDiagnosticAssignment(
            diagnostics,
            diagnosticAssignments.ToImmutable());
        return observations.ToImmutable();
    }

    internal static void RequireCompleteDiagnosticAssignment(
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArray<int> assignmentCounts)
    {
        if (assignmentCounts.IsDefault)
        { throw new ArgumentNullException(nameof(assignmentCounts)); }
        if (assignmentCounts.Length != diagnostics.Length)
        {
            throw new ArgumentException(
                "Diagnostic assignment counts do not match the diagnostics.",
                nameof(assignmentCounts));
        }

        var invalid = diagnostics
            .Select((diagnostic, index) =>
                ((index < 0) ? throw new System.ArgumentException("Value must be initialized.", nameof(index)) : (diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : ((Diagnostic: diagnostic, Count: assignmentCounts[index])))))
            .Where(static item => item.Count != 1)
            .ToImmutableArray();
        if (invalid.Length == 0)
        {
            return;
        }

        throw new InvalidDataException(
            $"{invalid.Length} analyzer diagnostics were not assigned " +
            "to exactly one selected OSS method:" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                invalid.Take(CorpusExecutionDefaults.MaximumReportedDiagnostics).Select(static item =>
                    $"{item.Diagnostic.Id} [{item.Count}] " +
                    item.Diagnostic.Location)));
    }

    // A probe compilation for a single-source document.
    internal static CSharpCompilation PrepareExceptionProbe(OpenSourceCorpusDocument document, CancellationToken cancellationToken,
        bool allocations = false, bool purity = false, bool capabilities = false, bool summary = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sources = SplitBySource(document);
        if (sources.Length != 1)
        {
            throw new ArgumentException("A single probe compilation needs a single-source document; use PrepareExceptionProbes.", nameof(document));
        }

        return PrepareExceptionProbes(document, cancellationToken, allocations, purity, capabilities, summary)[0].Compilation;
    }

    // One probe compilation per source, with the corpus method IDs it declares.
    internal static ImmutableArray<(CSharpCompilation Compilation, ImmutableArray<string> MethodIds)> PrepareExceptionProbes(
        OpenSourceCorpusDocument document, CancellationToken cancellationToken,
        bool allocations = false, bool purity = false, bool capabilities = false, bool summary = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        var attribute = summary
            ? "global::SharpProof.Attributes.EffectContract(global::SharpProof.Attributes.SharpProofEffect.None)"
            : capabilities
            ? "global::SharpProof.Attributes.AllowedCapabilities(global::SharpProof.Attributes.SharpProofCapability.None)"
            : purity ? "global::SharpProof.Attributes.EnforcePure" : allocations
            ? "global::SharpProof.Attributes.ZeroAllocations" : "global::SharpProof.Attributes.DoesNotThrow";
        return [.. SplitBySource(document).Select(source => (
            Prepare(source, attribute, cancellationToken, includeExternalEffectsFixture: purity || capabilities || summary, loaded: document).Compilation,
            source.Methods.Select(static method => method.Id).ToImmutableArray()))];
    }

    internal static string? CorpusMethodId(SyntaxNode? declaration)
    { return declaration?.GetAnnotations(AnnotationKind).SingleOrDefault()?.Data; }

    private sealed record PreparedCorpus(CSharpCompilation Compilation, ImmutableDictionary<TargetKey, TargetInfo> Targets)
    {
        public CSharpCompilation Compilation { get; init; } = Compilation ?? throw new System.ArgumentNullException(nameof(Compilation));
        public ImmutableDictionary<TargetKey, TargetInfo> Targets { get; init; } = Targets ?? throw new System.ArgumentNullException(nameof(Targets));
    }

    private static PreparedCorpus Prepare(OpenSourceCorpusDocument document, string effectAttribute, CancellationToken cancellationToken,
        bool includeExternalEffectsFixture = true, OpenSourceCorpusDocument? loaded = null)
    {
        System.ArgumentNullException.ThrowIfNull(effectAttribute);
        // A per-source split reuses the parse caches of the catalog-loaded document.
        var parsedFiles = OpenSourceCorpusCatalog.GetParsedFiles(loaded ?? document);
        var declarationIndexes = OpenSourceCorpusCatalog.GetDeclarationIndexes(loaded ?? document);
        var sourceId = document.Files.Select(static file => file.SourceId)
            .Concat(document.Methods.Select(static method => method.SourceId))
            .Distinct(StringComparer.Ordinal)
            .SingleOrDefault() ?? string.Empty;
        var options = OpenSourceCorpusCatalog.GetCompilationOptions(document, sourceId);
        var pinned = !document.Sources.IsDefault &&
            document.Sources.Any(source => source is not null && string.Equals(source.Id, sourceId, StringComparison.Ordinal));
        var parseOptions = OpenSourceCorpusCatalog.GetParseOptions(options);
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>(
            document.Files.Length + 1);
        if (!options.GlobalUsings.IsDefaultOrEmpty)
        {
            trees.Add(CSharpSyntaxTree.ParseText(
                string.Concat(options.GlobalUsings.Select(static name => $"global using {name};\n")),
                parseOptions,
                "__SharpProofOssCorpusGlobalUsings.cs",
                Encoding.UTF8,
                cancellationToken));
        }

        var methodsByFile = document.Methods
            .GroupBy(
                static method => OpenSourceCorpusCatalog.GetSourceFileKey(
                    method.SourceId,
                    method.Path),
                StringComparer.Ordinal)
            .ToImmutableDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray(),
                StringComparer.Ordinal);
        var methodsById = document.Methods.ToImmutableDictionary(
            static method => method.Id,
            StringComparer.Ordinal);
        var targets = ImmutableDictionary.CreateBuilder<
            TargetKey,
            TargetInfo>();
        foreach (var file in document.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = OpenSourceCorpusCatalog.GetSourceFileKey(
                file.SourceId,
                file.Path);
            var root = parsedFiles is not null &&
                parsedFiles.TryGetValue(key, out var parsedRoot)
                ? parsedRoot
                : CSharpSyntaxTree.ParseText(
                        OpenSourceCorpusCatalog.NormalizeLineEndings(file.Content),
                        parseOptions,
                        file.Path,
                        Encoding.UTF8,
                        cancellationToken)
                    .GetCompilationUnitRoot(cancellationToken);
            if (methodsByFile.TryGetValue(key, out var methods))
            {
                var declarationIndex = declarationIndexes is not null &&
                    declarationIndexes.TryGetValue(key, out var cachedIndex)
                    ? cachedIndex
                    : OpenSourceCorpusCatalog.BuildDeclarationIndex(root);
                var selected = methods.ToImmutableDictionary(
                    method => OpenSourceCorpusCatalog.FindDeclaration(
                        declarationIndex,
                        method),
                    static method => method);
                root = root.ReplaceNodes(
                    selected.Keys,
                    (original, rewritten) => original is null ? throw new System.ArgumentNullException(nameof(original)) : rewritten is null ? throw new System.ArgumentNullException(nameof(rewritten)) : (Instrument(
                        rewritten,
                        selected[original].Id, effectAttribute)));
            }
            // Pinned sources get source-qualified paths, which keep diagnostics
            // from same-named files in different sources apart. Test fixtures
            // without a source entry keep their bare paths.
            var tree = CSharpSyntaxTree.Create(
                root,
                parseOptions,
                pinned ? $"{sourceId}/{file.Path}" : file.Path,
                Encoding.UTF8);
            trees.Add(tree);
            foreach (var declaration in tree.GetCompilationUnitRoot(
                         cancellationToken)
                     .GetAnnotatedNodes(AnnotationKind)
                     .OfType<MethodDeclarationSyntax>())
            {
                var annotation = declaration.GetAnnotations(AnnotationKind)
                    .Single();
                var id = annotation.Data ??
                    throw new InvalidDataException(
                        $"Instrumented method in {file.Path} has no corpus ID.");
                var method = methodsById[id];
                targets.Add(
                    new TargetKey(tree, declaration.SpanStart),
                    new TargetInfo(method, tree, declaration.FullSpan, declaration));
            }
        }
        if (targets.Count != document.Methods.Length)
        {
            throw new InvalidDataException(
                $"Instrumented {targets.Count} OSS methods, expected " +
                $"{document.Methods.Length}.");
        }

        var template = AnalyzerGateHost.CreateCompilation(
            string.Empty,
            sourceId.Length == 0 ? "SharpProofOssCorpus" : $"SharpProofOssCorpus.{sourceId}",
            includeExternalEffectsFixture: includeExternalEffectsFixture && effectAttribute is not ("global::SharpProof.Attributes.DoesNotThrow" or "global::SharpProof.Attributes.ZeroAllocations"));
        var compilation = template
            .WithOptions(template.Options
                .WithNullableContextOptions(options.Nullable ? NullableContextOptions.Enable : NullableContextOptions.Disable)
                .WithOverflowChecks(options.CheckOverflow))
            .RemoveSyntaxTrees(template.SyntaxTrees)
            .AddSyntaxTrees(trees.ToImmutable());
        AnalyzerGateHost.ThrowIfCompilationHasErrors(
            compilation,
            CorpusExecutionDefaults.MaximumReportedDiagnostics,
            errors => new InvalidDataException(
                $"The pinned OSS corpus source '{sourceId}' did not compile against the BCL:" +
                Environment.NewLine + errors),
            cancellationToken);

        return new(compilation, targets.ToImmutable());
    }

    private static MethodDeclarationSyntax Instrument(
        MethodDeclarationSyntax declaration,
        string id, string effectAttribute)
    {
        System.ArgumentNullException.ThrowIfNull(declaration);
        System.ArgumentNullException.ThrowIfNull(effectAttribute);
        System.ArgumentNullException.ThrowIfNull(id);
        return declaration
            .WithAttributeLists(
                declaration.AttributeLists.Insert(
                    0,
                    SyntaxFactory.AttributeList(
                        SyntaxFactory.SingletonSeparatedList(
                            Attribute(effectAttribute)))))
            .WithAdditionalAnnotations(
                new SyntaxAnnotation(AnnotationKind, id));
    }

    // "Name" or "Name(argument)".
    private static AttributeSyntax Attribute(string text)
    {
        System.ArgumentNullException.ThrowIfNull(text);
        var open = text.IndexOf('(', StringComparison.Ordinal);
        return open < 0 ? SyntaxFactory.Attribute(SyntaxFactory.ParseName(text))
            : SyntaxFactory.Attribute(SyntaxFactory.ParseName(text[..open]), SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.AttributeArgument(
                    SyntaxFactory.ParseName(text[(open + 1)..^1])))));
    }

    private readonly record struct TargetKey(
        SyntaxTree Tree,
        int SourceStart)
    {
        public SyntaxTree Tree { get; init; } = Tree ?? throw new System.ArgumentNullException(nameof(Tree));
    }

    private sealed record TargetInfo(
        OpenSourceCorpusMethod Method,
        SyntaxTree Tree,
        TextSpan Span /* SPINT001: all values valid - all constructed spans, including an empty default span, are valid */,
        MethodDeclarationSyntax Declaration)
    {
        public OpenSourceCorpusMethod Method { get; init; } = Method ?? throw new System.ArgumentNullException(nameof(Method));
        public SyntaxTree Tree { get; init; } = Tree ?? throw new System.ArgumentNullException(nameof(Tree));
        public MethodDeclarationSyntax Declaration { get; init; } = Declaration ?? throw new System.ArgumentNullException(nameof(Declaration));
    }

    private static ImmutableArray<TargetInfo> FindCallableTargets(
        IMethodSymbol method,
        ImmutableDictionary<TargetKey, TargetInfo> targets)
    {
        System.ArgumentNullException.ThrowIfNull(method);
        System.ArgumentNullException.ThrowIfNull(targets);
        method = method.PartialImplementationPart ?? method;
        var referenceBuilder = method.DeclaringSyntaxReferences.ToBuilder();
        if (method.PartialDefinitionPart is { } definition)
        {
            referenceBuilder.AddRange(definition.DeclaringSyntaxReferences);
        }
        var references = referenceBuilder.ToImmutable();
        return [.. references
            .Select(static reference => new TargetKey(reference.SyntaxTree, reference.Span.Start))
            .Where(targets.ContainsKey)
            .Select(key => ((key.Tree is null) ? throw new System.ArgumentException("Value must be initialized.", nameof(key)) : (targets[key])))
            .DistinctBy(static target => target.Method.Id, StringComparer.Ordinal)];
    }

    private static IEnumerable<TargetInfo> DiagnosticTargets(
        CSharpCompilation compilation,
        TargetInfo target,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(target);
        yield return target;
        var method = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, target.Tree)
            .GetDeclaredSymbol(target.Declaration, cancellationToken);
        if (method?.PartialImplementationPart is not { } implementation)
        {
            yield break;
        }
        foreach (var reference in implementation.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is MethodDeclarationSyntax declaration)
            {
                yield return new TargetInfo(target.Method, declaration.SyntaxTree, declaration.FullSpan, declaration);
            }
        }
    }

    private sealed class RecordingSessionFactory(
        ImmutableDictionary<TargetKey, TargetInfo> targets)
        : AnalyzerSessionFactoryBase<string>(StringComparer.Ordinal)
    {
        private readonly ImmutableDictionary<TargetKey, TargetInfo> _guarded_targets = targets ?? throw new System.ArgumentNullException(nameof(targets));
        internal ImmutableDictionary<string, AnalyzerSemanticOutcome>
            GetOutcomes()
        {
            return Outcomes.ToImmutableDictionary(StringComparer.Ordinal);
        }

        protected override void Record(
            IMethodSymbol method,
            AnalyzerSemanticOutcome outcome)
        {
            System.ArgumentNullException.ThrowIfNull(method);
            foreach (var target in FindCallableTargets(method, _guarded_targets))
            {
                RecordOutcome(target.Method.Id, outcome);
            }
        }
    }
}
