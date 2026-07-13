using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Analyzer.Configuration;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static class MethodCapabilityAnalyzer
{
    internal static void AnalyzeSymbolForCapabilities(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var methodSymbol = context.MethodSymbol;

        void Report(Diagnostic diagnostic)
        {
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(context, baseline, diagnostic);
        }

        if (!TryGetAllowedCapabilities(
                methodSymbol,
                attributePolicy,
                context.CancellationToken,
                out var allowedCapabilities,
                out var invalidContract))
            return;

        if (invalidContract != null)
        {
            var diagnostic = InvalidContractArgumentDiagnostics.Create(
                "[AllowedCapabilities]",
                invalidContract.Argument,
                invalidContract.Reason,
                invalidContract.Location ?? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                methodSymbol,
                context.Node.SyntaxTree);
            Report(diagnostic);

            return;
        }

        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;

        var outcome = context.State.GetCapabilityOutcome(context.CancellationToken);
        if (!outcome.IsSuccess)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var queryFailure = CreateQueryFailureDiagnostic(
                methodSymbol,
                outcome.Error!,
                context.Node.SyntaxTree);
            Report(queryFailure);
            return;
        }

        var result = outcome.Value!;

        foreach (var site in result.Sites)
        {
            if (site.IsUnknown)
            {
                var unknownSiteDiagnostic = CreateUnknownDiagnostic(methodSymbol, site, context.Node.SyntaxTree);
                Report(unknownSiteDiagnostic);

                continue;
            }

            var disallowedCapabilities =
                SymbolicCapabilityFacts.Normalize(site.Capabilities) & ~allowedCapabilities;
            if (disallowedCapabilities == SymbolicCapability.None) continue;

            var diagnostic =
                CreateViolationDiagnostic(methodSymbol, site, disallowedCapabilities, context.Node.SyntaxTree);
            Report(diagnostic);
        }

        if (result.Sites.Count == 0 && result.UnknownReasons.Count > 0)
        {
            var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.CapabilityUnknownReasonProperty, result.UnknownReasons[0].ToString());
            var unknownReasonInfo = result.UnknownReasonDetails.FirstOrDefault() ??
                                    SymbolicUnknownReasonTaxonomy.ForCapability(result.UnknownReasons[0]);
            properties = UnknownReasonDiagnosticProperties.Add(properties, unknownReasonInfo);
            properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
                properties,
                methodSymbol,
                context.Node.SyntaxTree,
                "CapabilityUnknown",
                null,
                "unknown:" + result.UnknownReasons[0],
                location,
                "[AllowedCapabilities]",
                "unknown",
                unknownReasonInfo.Code);
            var diagnostic = Diagnostic.Create(
                SharpProofDiagnostics.CapabilityUnknownRule,
                location,
                null,
                properties,
                new object[]
                {
                    "<method body>",
                    methodSymbol.Name,
                    result.UnknownReasons[0].ToString()
                });
            Report(diagnostic);
        }
    }

    private static Diagnostic CreateQueryFailureDiagnostic(
        IMethodSymbol methodSymbol,
        SymbolicError error,
        SyntaxTree syntaxTree)
    {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, CancellationToken.None);
        var unknownReasonInfo = SymbolicUnknownReasonTaxonomy.ForCapabilityFailure(
            error.Code + ": " + error.Message);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.CapabilityUnknownReasonProperty, error.Code);
        properties = UnknownReasonDiagnosticProperties.Add(properties, unknownReasonInfo);
        properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            methodSymbol,
            syntaxTree,
            "CapabilityQueryFailure",
            null,
            "query-failure:" + error.Code,
            location,
            "[AllowedCapabilities]",
            "unknown",
            unknownReasonInfo.Code);
        return Diagnostic.Create(
            SharpProofDiagnostics.CapabilityUnknownRule,
            location,
            null,
            properties,
            new object[] { "<method body>", methodSymbol.Name, error.Code });
    }

    private static Diagnostic CreateViolationDiagnostic(
        IMethodSymbol methodSymbol,
        SymbolicCapabilitySite site,
        SymbolicCapability disallowedCapabilities,
        SyntaxTree syntaxTree)
    {
        var formattedCapabilities = SymbolicCapabilityFacts.Format(disallowedCapabilities);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.CapabilityProperty, formattedCapabilities)
            .Add(SharpProofDiagnostics.CapabilityOperationKindProperty, site.OperationKind);
        var location = Location.Create(
            syntaxTree,
            new TextSpan(site.SourceSpanStart, site.SourceSpanLength));

        if (!string.IsNullOrWhiteSpace(site.SymbolDisplayName))
            properties = properties.Add(SharpProofDiagnostics.CapabilitySymbolProperty, site.SymbolDisplayName);

        properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            methodSymbol,
            syntaxTree,
            site.OperationKind,
            null,
            CreateCapabilityEvidenceKey(site, formattedCapabilities),
            location,
            "[AllowedCapabilities]",
            "violated");

        return Diagnostic.Create(
            SharpProofDiagnostics.CapabilityViolationRule,
            location,
            null,
            properties, site.OperationText, methodSymbol.Name, formattedCapabilities);
    }

    private static Diagnostic CreateUnknownDiagnostic(
        IMethodSymbol methodSymbol,
        SymbolicCapabilitySite site,
        SyntaxTree syntaxTree)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.CapabilityUnknownReasonProperty, site.UnknownReason.ToString())
            .Add(SharpProofDiagnostics.CapabilityOperationKindProperty, site.OperationKind);
        properties = UnknownReasonDiagnosticProperties.Add(properties, site.UnknownReasonInfo);
        var location = Location.Create(
            syntaxTree,
            new TextSpan(site.SourceSpanStart, site.SourceSpanLength));

        if (!string.IsNullOrWhiteSpace(site.SymbolDisplayName))
            properties = properties.Add(SharpProofDiagnostics.CapabilitySymbolProperty, site.SymbolDisplayName);

        properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            methodSymbol,
            syntaxTree,
            site.OperationKind,
            null,
            CreateCapabilityEvidenceKey(site, site.UnknownReason.ToString()),
            location,
            "[AllowedCapabilities]",
            "unknown",
            site.UnknownReasonInfo.Code);

        return Diagnostic.Create(
            SharpProofDiagnostics.CapabilityUnknownRule,
            location,
            null,
            properties, site.OperationText, methodSymbol.Name, site.UnknownReason.ToString());
    }

    private static string CreateCapabilityEvidenceKey(
        SymbolicCapabilitySite site,
        string outcome)
    {
        return DiagnosticEvidenceKey.ForSpanLength(
            site.OperationKind,
            site.SourceSpanStart,
            site.SourceSpanLength,
            outcome,
            site.SymbolDisplayName);
    }

    private static bool TryGetAllowedCapabilities(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken,
        out SymbolicCapability capabilities,
        out InvalidContractArgument? invalidContract)
    {
        capabilities = SymbolicCapability.None;
        invalidContract = null;
        var found = false;

        foreach (var source in MethodContractHierarchy.EnumerateSources(methodSymbol, cancellationToken))
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(
                     source,
                     "AllowedCapabilitiesAttribute"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
            var argumentText = AnalyzerSyntaxHelpers.GetFirstAttributeArgumentText(attribute, cancellationToken);
            if (!TryGetCapabilityArgumentValue(attribute, out var rawValue))
            {
                invalidContract = new InvalidContractArgument(
                    argumentText,
                    "expected a SharpProofCapability enum value",
                    location);
                return true;
            }

            if (rawValue < 0 ||
                ((SymbolicCapability)rawValue & ~SymbolicCapabilityFacts.AllKnown) != 0)
            {
                invalidContract = new InvalidContractArgument(
                    rawValue.ToString(CultureInfo.InvariantCulture),
                    "unknown SharpProofCapability bits are set",
                    location);
                return true;
            }

            var declaredCapabilities = SymbolicCapabilityFacts.ExpandAllowed((SymbolicCapability)rawValue);
            capabilities = found ? capabilities & declaredCapabilities : declaredCapabilities;
            found = true;
        }

        return found;
    }

    private static bool TryGetCapabilityArgumentValue(AttributeData attribute, out long value)
    {
        value = 0;
        if (attribute.ConstructorArguments.Length != 1) return false;

        switch (attribute.ConstructorArguments[0].Value)
        {
            case int intValue:
                value = intValue;
                return true;
            case uint uintValue:
                value = uintValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            default:
                return false;
        }
    }

    private sealed record InvalidContractArgument(
        string Argument,
        string Reason,
        Location? Location);
}
