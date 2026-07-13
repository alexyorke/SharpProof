using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Configuration;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static class AnalyzerDiagnosticReporter
{
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
    internal static ImmutableDictionary<string, string?> AddBaselineProperties(
        ImmutableDictionary<string, string?> properties,
        IMethodSymbol methodSymbol,
        string operationKind,
        string contractText,
        string evidenceKey)
    {
        var syntaxTree = methodSymbol.Locations.FirstOrDefault(location => location.SourceTree != null)?.SourceTree;
        return syntaxTree == null
            ? properties
            : BaselineDiagnosticProperties.Add(
                properties,
                methodSymbol,
                syntaxTree,
                operationKind,
                contractText,
                evidenceKey);
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
}
