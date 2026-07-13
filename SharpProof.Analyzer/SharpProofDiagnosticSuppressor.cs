using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer.Configuration;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SharpProofDiagnosticSuppressor : DiagnosticSuppressor
{
    private const string ProofDocumentationUrl =
        "https://github.com/alexyorke/SharpProof/blob/main/docs/proven-diagnostic-suppression.md";

    private static readonly ImmutableArray<SuppressionSpec> SuppressionSpecs =
        ImmutableArray.Create(
            CreateSpec("SPS0001", "CS8602", "null-dereference", SymbolicRuntimeHazardKind.NullDereference),
            CreateSpec("SPS0002", "CS8670", "null-dereference", SymbolicRuntimeHazardKind.NullDereference),
            CreateSpec("SPS0003", "CS8605", "unbox-null", SymbolicRuntimeHazardKind.UnboxNull),
            CreateSpec(
                "SPS0004",
                "CS8629",
                "nullable-value",
                SymbolicRuntimeHazardKind.NullableValueWithoutValue),
            CreateSpec(
                "SPS0005",
                "CS8509",
                "switch no-match",
                SymbolicRuntimeHazardKind.SwitchExpressionNoMatch),
            CreateSpec(
                "SPS0006",
                "CS8524",
                "switch no-match",
                SymbolicRuntimeHazardKind.SwitchExpressionNoMatch),
            CreateSpec(
                "SPS0007",
                "CS8846",
                "switch no-match",
                SymbolicRuntimeHazardKind.SwitchExpressionNoMatch),
            CreateSpec("SPS0008", "S2259", "null-dereference", SymbolicRuntimeHazardKind.NullDereference),
            CreateSpec(
                "SPS0009",
                "S3655",
                "nullable-value",
                SymbolicRuntimeHazardKind.NullableValueWithoutValue),
            CreateSpec("SPS0010", "V3080", "null-dereference", SymbolicRuntimeHazardKind.NullDereference),
            CreateSpec("SPS0011", "V3095", "null-dereference", SymbolicRuntimeHazardKind.NullDereference),
            CreateSpec(
                "SPS0012",
                "V3106",
                "index-in-range",
                SymbolicRuntimeHazardKind.IndexOutOfRange,
                SymbolicRuntimeHazardKind.ArgumentOutOfRange),
            CreateSpec(
                "SPS0013",
                "V3218",
                "index-in-range",
                SymbolicRuntimeHazardKind.IndexOutOfRange,
                SymbolicRuntimeHazardKind.ArgumentOutOfRange),
            CreateSpec("SPS0014", "V3064", "non-zero divisor", SymbolicRuntimeHazardKind.DivideByZero),
            CreateSpec("SPS0015", "V3151", "non-zero divisor", SymbolicRuntimeHazardKind.DivideByZero),
            CreateSpec("SPS0016", "V3152", "non-zero divisor", SymbolicRuntimeHazardKind.DivideByZero),
            CreateSpec(
                "SPS0017",
                "CS8655",
                "switch no-match",
                SymbolicRuntimeHazardKind.SwitchExpressionNoMatch),
            CreateSpec(
                "SPS0018",
                "CS8847",
                "switch no-match",
                SymbolicRuntimeHazardKind.SwitchExpressionNoMatch));

    private static readonly ImmutableArray<SuppressionDescriptor> SuppressionDescriptors =
        SuppressionSpecs.Select(static spec => spec.Descriptor).ToImmutableArray();

    private static readonly ImmutableDictionary<string, SuppressionSpec> SpecsByDiagnosticId =
        SuppressionSpecs.ToImmutableDictionary(
            static spec => spec.Descriptor.SuppressedDiagnosticId,
            StringComparer.Ordinal);

    private static readonly ImmutableArray<SymbolicRuntimeHazardKind> SupportedHazardKinds =
        SuppressionSpecs
            .SelectMany(static spec => spec.HazardKinds)
            .Distinct()
            .ToImmutableArray();

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => SuppressionDescriptors;

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        if (context.ReportedDiagnostics.IsDefaultOrEmpty) return;

        var configuration = AnalyzerConfiguration.FromOptions(context.Options);
        var candidates = CollectCandidates(context, configuration.ProvenDiagnosticSuppressions);
        if (candidates.Count == 0) return;

        SmtNativeLibraryBootstrap.TryLoadFromAnalyzerLocatorPaths(
            context.Options.AdditionalFiles.Select(static file => file.Path));
        using var smtAnalysis = new SmtAnalysisService(configuration.SmtOptions);
        var hazardService = new SymbolicRuntimeHazardQueryService();
        var attributePolicy = SharpProofAttributeIdentityPolicy.Create(configuration.AttributeStubNamespaces);
        var hazardsByRoot = new Dictionary<QueryRootKey, IReadOnlyList<SymbolicRuntimeHazard>>();
        foreach (var candidate in candidates)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var key = new QueryRootKey(
                candidate.QueryRoot.SyntaxTree,
                candidate.QueryRoot.SpanStart,
                candidate.QueryRoot.Span.End);
            if (!hazardsByRoot.TryGetValue(key, out var hazards))
            {
                hazards = QueryHazards(
                    candidate.QueryRoot,
                    context.GetSemanticModel(candidate.QueryRoot.SyntaxTree),
                    hazardService,
                    smtAnalysis,
                    configuration.AnalysisLimits,
                    attributePolicy,
                    context.CancellationToken);
                hazardsByRoot.Add(key, hazards);
            }

            if (!hazards.Any(hazard =>
                    candidate.Spec.HazardKinds.Contains(hazard.Kind) &&
                    HasExactUnreachableProof(hazard) &&
                    HazardContainsDiagnostic(hazard, candidate.Diagnostic.Location.SourceSpan)))
                continue;

            context.ReportSuppression(Suppression.Create(candidate.Spec.Descriptor, candidate.Diagnostic));
        }
    }

    private static List<SuppressionCandidate> CollectCandidates(
        SuppressionAnalysisContext context,
        ProvenDiagnosticSuppressionOptions globalOptions)
    {
        var candidates = new List<SuppressionCandidate>();
        var optionsByTree = new Dictionary<SyntaxTree, ProvenDiagnosticSuppressionOptions>();
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (diagnostic.DefaultSeverity == DiagnosticSeverity.Error ||
                !SpecsByDiagnosticId.TryGetValue(diagnostic.Id, out var spec) ||
                diagnostic.Location.SourceTree is not { } syntaxTree)
                continue;

            if (!optionsByTree.TryGetValue(syntaxTree, out var options))
            {
                options = AnalyzerConfiguration.GetProvenDiagnosticSuppressionOptions(
                    context.Options,
                    syntaxTree,
                    globalOptions);
                optionsByTree.Add(syntaxTree, options);
            }

            if (!options.Includes(diagnostic.Id)) continue;

            var root = syntaxTree.GetRoot(context.CancellationToken);
            var sourceSpan = diagnostic.Location.SourceSpan;
            if (sourceSpan.Start < root.FullSpan.Start || sourceSpan.End > root.FullSpan.End) continue;

            var diagnosticNode = sourceSpan.IsEmpty
                ? root.FindToken(sourceSpan.Start).Parent
                : root.FindNode(sourceSpan, getInnermostNodeForTie: true);
            var queryRoot = diagnosticNode == null ? null : FindQueryRoot(diagnosticNode);
            if (queryRoot != null) candidates.Add(new SuppressionCandidate(diagnostic, spec, queryRoot));
        }

        return candidates;
    }

    private static IReadOnlyList<SymbolicRuntimeHazard> QueryHazards(
        SyntaxNode queryRoot,
        SemanticModel semanticModel,
        SymbolicRuntimeHazardQueryService hazardService,
        SmtAnalysisService smtAnalysis,
        SymbolicAnalysisLimits analysisLimits,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        try
        {
            using var limitScope = SymbolicAnalysisLimitContext.Push(analysisLimits, queryRoot);
            var options = new SymbolicRuntimeHazardQueryOptions(
                includeUnprovenCandidates: true,
                kinds: SupportedHazardKinds);
            var initialState = ExceptionFlowAnalyzer.CreateStableMethodEntryRequiresState(
                queryRoot,
                semanticModel,
                attributePolicy,
                cancellationToken);
            return (initialState == null
                    ? hazardService.QueryNodeRuntimeHazards(
                        queryRoot,
                        semanticModel,
                        smtAnalysis,
                        cancellationToken,
                        options,
                        includeNestedCallables: false)
                    : hazardService.QueryNodeRuntimeHazardsWithInitialState(
                        queryRoot,
                        semanticModel,
                        smtAnalysis,
                        initialState,
                        cancellationToken,
                        options,
                        includeNestedCallables: false))
                .Hazards;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Array.Empty<SymbolicRuntimeHazard>();
        }
    }

    private static SyntaxNode? FindQueryRoot(SyntaxNode node)
    {
        return CSharpSyntaxFacts.GetContainingExecutionRoot(
            node,
            ExecutionRootPolicy.Callable |
            ExecutionRootPolicy.ExpressionBodiedPropertyOrIndexer |
            ExecutionRootPolicy.Initializer |
            ExecutionRootPolicy.GlobalStatement);
    }

    private static bool HasExactUnreachableProof(SymbolicRuntimeHazard hazard)
    {
        return hazard.Status == SymbolicRuntimeHazardStatus.Unreachable &&
               hazard.Proof.Status == SymbolicProofStatus.Unreachable &&
               hazard.Proof.Backend != SymbolicProofBackend.None &&
               hazard.Proof.UnknownReason == SymbolicUnknownReason.None &&
               !hazard.AnalysisTruncation.IsTruncated;
    }

    private static bool HazardContainsDiagnostic(SymbolicRuntimeHazard hazard, TextSpan diagnosticSpan)
    {
        var hazardSpan = TextSpan.FromBounds(hazard.SpanStart, hazard.SpanEnd);
        return diagnosticSpan.IsEmpty
            ? hazardSpan.Contains(diagnosticSpan.Start)
            : hazardSpan.Start <= diagnosticSpan.Start && hazardSpan.End >= diagnosticSpan.End;
    }

    private static SuppressionSpec CreateSpec(
        string suppressionId,
        string diagnosticId,
        string proofKind,
        params SymbolicRuntimeHazardKind[] hazardKinds)
    {
        var justification =
            $"SharpProof proved the matching {proofKind} trigger unreachable with exact, non-truncated evidence. " +
            "Inspect the source location with SharpProof.SymbolicCli explain or " +
            "SharpProof.SymbolicCli --runtime-hazards. Proof policy: " +
            ProofDocumentationUrl;
        return new SuppressionSpec(
            new SuppressionDescriptor(suppressionId, diagnosticId, justification),
            hazardKinds.ToImmutableHashSet());
    }

    private readonly record struct SuppressionSpec(
        SuppressionDescriptor Descriptor,
        ImmutableHashSet<SymbolicRuntimeHazardKind> HazardKinds);

    private readonly record struct SuppressionCandidate(
        Diagnostic Diagnostic,
        SuppressionSpec Spec,
        SyntaxNode QueryRoot);

    private readonly record struct QueryRootKey(
        SyntaxTree SyntaxTree,
        int SpanStart,
        int SpanEnd);
}
