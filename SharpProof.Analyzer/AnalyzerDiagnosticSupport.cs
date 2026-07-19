namespace SharpProof.Analyzer;

internal static class AnalyzerDiagnosticReporter
{
    internal static Action<Diagnostic> CreateBaselineReporter(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline) =>
        diagnostic => ReportIfNotSuppressed(context, baseline, diagnostic);

    internal static void ReportIfNotSuppressed(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        Diagnostic diagnostic)
    {
        ReportIfNotSuppressed(baseline, diagnostic, context.ReportDiagnostic);
    }

    internal static void ReportIfNotSuppressed(
        Microsoft.CodeAnalysis.Diagnostics.OperationAnalysisContext context,
        DiagnosticBaseline baseline,
        Diagnostic diagnostic)
    {
        ReportIfNotSuppressed(baseline, diagnostic, context.ReportDiagnostic);
    }

    internal static void ReportIfNotSuppressed(
        DiagnosticBaseline baseline,
        Diagnostic diagnostic,
        Action<Diagnostic> reportDiagnostic)
    {
        if (!baseline.IsSuppressed(diagnostic)) reportDiagnostic(diagnostic);
    }
}

internal static class ContractDiagnosticSupport
{
    internal enum EvidenceFamily
    {
        Requires,
        Ensures
    }

    internal static ImmutableDictionary<string, string?> CreateProofProperties(
        EvidenceFamily family,
        IMethodSymbol methodSymbol,
        string operationKind,
        string condition,
        string proofStatus,
        string failureReason,
        string evidenceKey,
        Location? location,
        string explainUnknownReason,
        SymbolicAnalysisTruncationInfo analysisTruncation,
        string? diagnosticUnknownReason = null,
        string? callee = null,
        SymbolicUnknownReasonInfo? structuredUnknownReason = null)
    {
        var properties = family switch
        {
            EvidenceFamily.Requires => ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.RequiresConditionProperty, condition)
                .Add(SharpProofDiagnostics.RequiresProofStatusProperty, proofStatus)
                .Add(SharpProofDiagnostics.RequiresFailureReasonProperty, failureReason)
                .Add(SharpProofDiagnostics.RequiresCalleeProperty, callee),
            EvidenceFamily.Ensures => ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.EnsuresConditionProperty, condition)
                .Add(SharpProofDiagnostics.EnsuresProofStatusProperty, proofStatus)
                .Add(SharpProofDiagnostics.EnsuresFailureReasonProperty, failureReason),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
        };

        if (diagnosticUnknownReason != null)
            properties = properties.Add(
                family == EvidenceFamily.Requires
                    ? SharpProofDiagnostics.RequiresUnknownReasonProperty
                    : SharpProofDiagnostics.EnsuresUnknownReasonProperty,
                diagnosticUnknownReason);

        if (structuredUnknownReason?.IsUnknown == true)
            properties = UnknownReasonDiagnosticProperties.Add(properties, structuredUnknownReason);
        properties = AnalysisTruncationDiagnosticProperties.Add(properties, analysisTruncation);
        var syntaxTree = methodSymbol.Locations.FirstOrDefault(candidate => candidate.SourceTree != null)?.SourceTree;
        return AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            methodSymbol,
            syntaxTree,
            operationKind,
            condition,
            evidenceKey,
            location,
            condition,
            proofStatus,
            explainUnknownReason,
            condition);
    }

    internal static string FormatUnknownReason(
        SymbolicConditionProofResult proof,
        string contractAttributeName)
    {
        if (proof.Proof.UnknownReason != SymbolicUnknownReason.None &&
            proof.Proof.UnknownReason != SymbolicUnknownReason.Unknown)
            return proof.Proof.UnknownReason.ToString();

        return proof.Reason switch
        {
            "condition_parse_failure" => "condition parse failure",
            "condition_binding_failure" => "condition binding failure",
            "condition_not_supported" => "condition is not supported by the current bounded proof engine",
            "smt_required" => "SMT is required for [" + contractAttributeName + "] verification",
            _ when string.IsNullOrWhiteSpace(proof.Reason) => "unknown",
            _ => proof.Reason.Replace('_', ' ')
        };
    }

    internal static string FormatLocationKey(Location? location)
    {
        return location == null
            ? "none"
            : location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture) +
              ":" +
              location.SourceSpan.End.ToString(CultureInfo.InvariantCulture);
    }
}

internal static class AnalyzerDiagnosticProperties
{
    internal static ImmutableDictionary<string, string?> AddBaselineAndExplain(
        ImmutableDictionary<string, string?> properties,
        ISymbol? symbol,
        SyntaxTree? syntaxTree,
        string operationKind,
        string? baselineContractText,
        string evidenceKey,
        Location? location,
        string explainContractText,
        string proofStatus,
        string? unknownReason = null,
        string? impliedConditionText = null)
    {
        if (symbol != null && syntaxTree != null)
            properties = BaselineDiagnosticProperties.Add(
                properties,
                symbol,
                syntaxTree,
                operationKind,
                baselineContractText,
                evidenceKey);
        return ExplainDiagnosticProperties.Add(
            properties,
            location,
            explainContractText,
            proofStatus,
            unknownReason,
            impliedConditionText);
    }
}

internal static class DiagnosticEvidenceKey
{
    internal static string ForSpanLength(
        string kind,
        int spanStart,
        int spanLength,
        params string?[] segments)
    {
        return Build(kind, spanStart, spanLength, segments);
    }

    internal static string ForSpanEnd(
        string kind,
        int spanStart,
        int spanEnd,
        params string?[] segments)
    {
        return Build(kind, spanStart, spanEnd, segments);
    }

    private static string Build(
        string kind,
        int spanStart,
        int spanValue,
        IEnumerable<string?> segments)
    {
        return kind +
               "@" +
               spanStart.ToString(CultureInfo.InvariantCulture) +
               ":" +
               spanValue.ToString(CultureInfo.InvariantCulture) +
               "|" +
               string.Join("|", segments.Select(static segment => segment ?? string.Empty));
    }
}
