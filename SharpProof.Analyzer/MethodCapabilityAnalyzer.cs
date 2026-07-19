namespace SharpProof.Analyzer;

internal static class MethodCapabilityAnalyzer
{
    internal static void AnalyzeSymbolForCapabilities(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var methodSymbol = context.MethodSymbol;

        var report = AnalyzerDiagnosticReporter.CreateBaselineReporter(context, baseline);

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
            report(diagnostic);

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
            report(queryFailure);
            return;
        }

        var result = outcome.Value!;

        foreach (var site in result.Sites)
        {
            if (site.IsUnknown)
            {
                var unknownSiteDiagnostic = CreateUnknownDiagnostic(methodSymbol, site, context.Node.SyntaxTree);
                report(unknownSiteDiagnostic);

                continue;
            }

            var disallowedCapabilities =
                SymbolicCapabilityFacts.NormalizeMask(SymbolicCapabilityFacts.GetMask(site)) & ~allowedCapabilities;
            if (disallowedCapabilities == SymbolicCapabilityFacts.NoneMask) continue;

            var diagnostic =
                CreateViolationDiagnostic(methodSymbol, site, disallowedCapabilities, context.Node.SyntaxTree);
            report(diagnostic);
        }

        if (result.Sites.Count == 0 && result.UnknownReasons.Count > 0)
        {
            var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node);
            var reason = result.UnknownReasons[0].ToString();
            var unknownReasonInfo = result.UnknownReasonDetails.FirstOrDefault() ??
                                     SymbolicUnknownReasonTaxonomy.ForCapability(result.UnknownReasons[0]);
            var diagnostic = CreateMethodBodyUnknownDiagnostic(methodSymbol, context.Node.SyntaxTree, location,
                reason, unknownReasonInfo, "CapabilityUnknown", "unknown:" + reason);
            report(diagnostic);
        }
    }

    private static Diagnostic CreateQueryFailureDiagnostic(
        IMethodSymbol methodSymbol,
        SharpProofError error,
        SyntaxTree syntaxTree)
    {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, CancellationToken.None);
        var unknownReasonInfo = SymbolicUnknownReasonTaxonomy.ForCapabilityFailure(
            error.Code + ": " + error.Message);
        return CreateMethodBodyUnknownDiagnostic(methodSymbol, syntaxTree, location, error.Code, unknownReasonInfo,
            "CapabilityQueryFailure", "query-failure:" + error.Code);
    }

    private static Diagnostic CreateMethodBodyUnknownDiagnostic(
        IMethodSymbol methodSymbol, SyntaxTree syntaxTree, Location location, string reason,
        SymbolicUnknownReasonInfo unknownReasonInfo, string operationKind, string evidenceKey)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.CapabilityUnknownReasonProperty, reason);
        properties = UnknownReasonDiagnosticProperties.Add(properties, unknownReasonInfo);
        properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            methodSymbol,
            syntaxTree,
            operationKind,
            null,
            evidenceKey,
            location,
            "[AllowedCapabilities]",
            "unknown",
            unknownReasonInfo.Code);
        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("CapabilityUnknownRule"),
            location,
            null,
            properties,
            new object[] { "<method body>", methodSymbol.Name, reason });
    }

    private static Diagnostic CreateViolationDiagnostic(
        IMethodSymbol methodSymbol,
        SymbolicCapabilitySite site,
        int disallowedCapabilities,
        SyntaxTree syntaxTree)
    {
        var formattedCapabilities = SymbolicCapabilityFacts.FormatMask(disallowedCapabilities);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("sharpproof.capability.flags", formattedCapabilities);
        properties = AddCapabilitySiteDiagnosticProperties(
            properties,
            methodSymbol,
            site,
            syntaxTree,
            formattedCapabilities,
            "violated",
            null,
            out var location);

        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("CapabilityViolationRule"),
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
            .Add(DiagnosticPropertyNames.CapabilityUnknownReasonProperty, site.UnknownReason.ToString());
        properties = UnknownReasonDiagnosticProperties.Add(properties, site.UnknownReasonInfo);
        properties = AddCapabilitySiteDiagnosticProperties(
            properties,
            methodSymbol,
            site,
            syntaxTree,
            site.UnknownReason.ToString(),
            "unknown",
            site.UnknownReasonInfo.Code,
            out var location);

        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("CapabilityUnknownRule"),
            location,
            null,
            properties, site.OperationText, methodSymbol.Name, site.UnknownReason.ToString());
    }

    private static ImmutableDictionary<string, string?> AddCapabilitySiteDiagnosticProperties(
        ImmutableDictionary<string, string?> properties,
        IMethodSymbol methodSymbol,
        SymbolicCapabilitySite site,
        SyntaxTree syntaxTree,
        string evidenceOutcome,
        string analysisOutcome,
        string? unknownReason,
        out Location location)
    {
        properties = properties.Add(
            "sharpproof.capability.operation_kind",
            site.OperationKind);
        location = Location.Create(
            syntaxTree,
            new TextSpan(site.SourceSpanStart, site.SourceSpanLength));

        if (!string.IsNullOrWhiteSpace(site.SymbolDisplayName))
            properties = properties.Add(
                "sharpproof.capability.symbol",
                site.SymbolDisplayName);

        return AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            methodSymbol,
            syntaxTree,
            site.OperationKind,
            null,
            CreateCapabilityEvidenceKey(site, evidenceOutcome),
            location,
            "[AllowedCapabilities]",
            analysisOutcome,
            unknownReason);
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
        out int capabilities,
        out InvalidContractArgument? invalidContract)
    {
        capabilities = SymbolicCapabilityFacts.NoneMask;
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
                rawValue > int.MaxValue ||
                ((int)rawValue & ~SymbolicCapabilityFacts.AllKnownMask) != 0)
            {
                invalidContract = new InvalidContractArgument(
                    rawValue.ToString(CultureInfo.InvariantCulture),
                    "unknown SharpProofCapability bits are set",
                    location);
                return true;
            }

            var declaredCapabilities = SymbolicCapabilityFacts.ExpandAllowedMask((int)rawValue);
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
