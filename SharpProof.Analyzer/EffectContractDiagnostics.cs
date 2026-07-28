namespace SharpProof.Analyzer;

internal static class EffectContractDiagnostics {
    internal static void ValidateArguments(IMethodSymbol method, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic) {
        var attributes = AnalyzerAttributeSymbols.GetCallableAttributes(method).ToImmutableArray();
        var location = method.Locations.FirstOrDefault() ?? Location.None;
        _ = DecodeCapabilities(
            SelectAttributes(attributes, session.Attributes.AllowedCapabilities),
            location, session, reportDiagnostic);
        _ = DecodeAllowedExceptions(
            SelectAttributes(attributes, session.Attributes.AllowedExceptions),
            session.Compilation, location, session, reportDiagnostic);
        if (!attributes.Any(attribute =>
                AnalyzerAttributeSymbols.Is(
                    attribute,
                    session.Attributes.EffectContract)))
            return;
        var contract = session.ResolveEffectContract(method);
        if (contract.Kind != EffectContractResolutionKind.Invalid ||
            contract.InvalidAttribute == null ||
            !session.TryMarkAttributeValidated(contract.InvalidAttribute))
            return;
        ReportInvalid(
            contract.InvalidAttribute,
            "[EffectContract]",
            contract.InvalidReason,
            location,
            reportDiagnostic);
    }

    internal static AnalyzerSemanticOutcome Analyze(
        IMethodSymbol method,
        SyntaxNode declaration,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken) {
        var attributes = AnalyzerAttributeSymbols.GetCallableAttributes(method).ToImmutableArray();
        var enforcePure = attributes.Any(attribute =>
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.EnforcePure));
        var zeroAllocations = attributes.Any(attribute =>
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.ZeroAllocations));
        var doesNotThrow = attributes.Any(attribute =>
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.DoesNotThrow));
        var capabilityAttributes =
            SelectAttributes(attributes, session.Attributes.AllowedCapabilities);
        var exceptionAttributes =
            SelectAttributes(attributes, session.Attributes.AllowedExceptions);
        var hasEffectContract = attributes.Any(attribute =>
            AnalyzerAttributeSymbols.Is(
                attribute,
                session.Attributes.EffectContract));
        var checksSourceEffectContract =
            hasEffectContract && !method.IsAbstract && !method.IsExtern;
        if (!enforcePure &&
            !zeroAllocations &&
            !doesNotThrow &&
            capabilityAttributes.IsDefaultOrEmpty &&
            exceptionAttributes.IsDefaultOrEmpty &&
            !checksSourceEffectContract)
            return AnalyzerSemanticOutcome.NotApplicable;

        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration);
        var capabilities = DecodeCapabilities(
            capabilityAttributes,
            location,
            session,
            reportDiagnostic);
        var exceptions = DecodeAllowedExceptions(
            exceptionAttributes,
            session.Compilation,
            location,
            session,
            reportDiagnostic);
        cancellationToken.ThrowIfCancellationRequested();
        var result = session.AnalyzeEffects(method, cancellationToken);
        var summary = result.Summary;
        var hasUnknown =
            !capabilityAttributes.IsDefaultOrEmpty && !capabilities.IsValid ||
            !exceptionAttributes.IsDefaultOrEmpty && !exceptions.IsValid;
        void Report(DiagnosticDescriptor rule, params object[] arguments) {
            hasUnknown = true;
            reportDiagnostic(Diagnostic.Create(rule, location, arguments));
        }

        if (checksSourceEffectContract) {
            var contract = session.ResolveEffectContract(method);
            if (contract.Kind == EffectContractResolutionKind.Invalid)
                hasUnknown = true;
            else if (contract.Kind == EffectContractResolutionKind.Incomplete ||
                     contract.Kind == EffectContractResolutionKind.Missing ||
                     !Covers(summary, contract.Summary))
                Report(
                    GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                    method.Name,
                    contract.Kind == EffectContractResolutionKind.Incomplete
                        ? "IncompleteEffectContract"
                        : "EffectContractDoesNotCoverBodySummary");
        }

        if (enforcePure &&
            (IsUnknown(summary, UnknownFacet.PurityUnknown) ||
             !IsObservablePure(summary))) {
            Report(
                GeneratedDiagnosticDescriptors.PurityNotVerifiedRule,
                method.Name);
        }

        if (zeroAllocations) {
            var allocationUnknown = IsUnknown(
                summary,
                UnknownFacet.AllocationUnknown);
            if (allocationUnknown ||
                summary.Allocation != EffectAllocationKind.None) {
                Report(
                    GeneratedDiagnosticDescriptors.ZeroAllocationsNotVerifiedRule,
                    method.Name,
                    allocationUnknown
                        ? FormatUnknown(
                            summary,
                            UnknownFacet.AllocationUnknown)
                        : "may-effect summary includes allocation: " +
                          summary.Allocation);
            }
        }

        if (!capabilityAttributes.IsDefaultOrEmpty && capabilities.IsValid) {
            if (IsUnknown(summary, UnknownFacet.CapabilitySetUnknown)) {
                Report(
                    GeneratedDiagnosticDescriptors.CapabilityUnknownRule,
                    "method summary",
                    method.Name,
                    FormatUnknown(
                        summary,
                        UnknownFacet.CapabilitySetUnknown));
            }
            else {
                var actual = result.Projection.Capabilities;
                var disallowed = actual & ~capabilities.Value;
                if (disallowed != EffectContractCapabilityKind.None) {
                    Report(
                        GeneratedDiagnosticDescriptors.CapabilityUnknownRule,
                        "method summary",
                        method.Name,
                        "may-effect summary includes disallowed " +
                        "capabilities: " + disallowed);
                }
            }
        }

        if (doesNotThrow ||
            !exceptionAttributes.IsDefaultOrEmpty && exceptions.IsValid) {
            var contractName = doesNotThrow
                ? "[DoesNotThrow]"
                : "[AllowedExceptions]";
            if (IsUnknown(summary, UnknownFacet.ExceptionSetUnknown)) {
                Report(
                    GeneratedDiagnosticDescriptors.ExceptionContractNotVerifiedRule,
                    method.Name,
                    contractName,
                    FormatUnknown(
                        summary,
                        UnknownFacet.ExceptionSetUnknown));
            }
            else {
                var disallowed = doesNotThrow
                    ? summary.Throws.Types
                    : [.. summary.Throws.Types
                        .Where(type => !IsAllowed(type, exceptions.Types))];
                if (!disallowed.IsDefaultOrEmpty) {
                    Report(
                        GeneratedDiagnosticDescriptors.ExceptionContractNotVerifiedRule,
                        method.Name,
                        contractName,
                        "may-effect summary includes disallowed " +
                        "exceptions: " + string.Join(
                            ", ",
                            disallowed.Select(static type => type.MetadataName)));
                }
            }
        }
        return hasUnknown
            ? AnalyzerSemanticOutcome.Unknown : AnalyzerSemanticOutcome.Proven;
    }

    private static (EffectContractCapabilityKind Value, bool IsValid) DecodeCapabilities(
        ImmutableArray<AttributeData> attributes,
        Location fallbackLocation,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic) {
        var value = EffectContractCapabilityKind.None;
        foreach (var attribute in attributes) {
            if (attribute.ConstructorArguments.Length != 1 ||
                !EffectContractMetadata.TryConvertInt64(
                    attribute.ConstructorArguments[0].Value,
                    out var raw) ||
                raw < 0 ||
                ((EffectContractCapabilityKind)raw &
                 ~EffectContractMetadata.AllCapabilities) != 0) {
                if (session.TryMarkAttributeValidated(attribute))
                    ReportInvalid(
                        attribute,
                        "[AllowedCapabilities]",
                        "expected a defined SharpProofCapability flags value",
                        fallbackLocation,
                        reportDiagnostic);
                return (EffectContractCapabilityKind.None, false);
            }
            value |= (EffectContractCapabilityKind)raw;
        }
        return (value, true);
    }

    private static (ImmutableArray<INamedTypeSymbol> Types, bool IsValid)
        DecodeAllowedExceptions(
        ImmutableArray<AttributeData> attributes,
        Compilation compilation,
        Location fallbackLocation,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic) {
        var exceptionType = compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
        var types = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var isValid = true;
        foreach (var attribute in attributes) {
            var arguments = attribute.ConstructorArguments;
            var values = arguments.Length == 1 &&
                         arguments[0].Kind == TypedConstantKind.Array
                ? arguments[0].Values : default;
            if (exceptionType != null &&
                !values.IsDefault &&
                values.All(argument =>
                    argument.Value is INamedTypeSymbol type &&
                    EffectTypeFacts.IsDerivedFrom(type, exceptionType))) {
                types.AddRange(values.Select(static argument =>
                    (INamedTypeSymbol)argument.Value!));
                continue;
            }
            if (session.TryMarkAttributeValidated(attribute))
                ReportInvalid(
                attribute,
                "[AllowedExceptions]",
                "expected only System.Exception-derived types",
                fallbackLocation,
                reportDiagnostic);
            isValid = false;
        }
        return isValid ? (types.ToImmutable(), true) : ([], false);
    }

    private static void ReportInvalid(
        AttributeData attribute,
        string contract,
        string reason,
        Location fallbackLocation,
        Action<Diagnostic> reportDiagnostic) =>
        reportDiagnostic(
            InvalidContractArgumentDiagnostics.Create(
                contract,
                "<invalid>",
                reason,
                GetLocation(attribute, fallbackLocation)));

    private static bool IsObservablePure(EffectSummary summary) {
        if (!summary.Capabilities.IsEmpty)
            return false;
        if (summary.Reads.Regions.Any(static region =>
                region.Kind is
                    EffectRegionKind.Ambient or
                    EffectRegionKind.Captured or
                    EffectRegionKind.Static))
            return false;
        return summary.Writes.Regions.All(static region =>
            region.Kind == EffectRegionKind.Fresh);
    }

    private static bool Covers(
        EffectSummary actual,
        EffectSummary declared) {
        var actualProjection = EffectSummaryProjector.Project(actual);
        var declaredProjection = EffectSummaryProjector.Project(declared);
        return actualProjection.IsComplete &&
               (actualProjection.Effects & ~declaredProjection.Effects) == 0 &&
               (actualProjection.Capabilities &
                ~declaredProjection.Capabilities) == 0 &&
               actual.Throws.IsSubsetOf(declared.Throws);
    }

    private static bool IsUnknown(
        EffectSummary summary,
        UnknownFacet facet) =>
        summary.IsBottom ||
        facet switch {
            UnknownFacet.PurityUnknown =>
                summary.Reads.IsUnknown ||
                summary.Writes.IsUnknown ||
                summary.Capabilities.IsUnknown,
            UnknownFacet.AllocationUnknown =>
                summary.Allocation == EffectAllocationKind.Unknown,
            UnknownFacet.CapabilitySetUnknown =>
                summary.Capabilities.IsUnknown,
            UnknownFacet.ExceptionSetUnknown =>
                summary.Throws.IncludesUnknown,
            _ => throw new ArgumentOutOfRangeException(nameof(facet))
        };

    private static string FormatUnknown(
        EffectSummary summary,
        UnknownFacet facet) =>
        facet + ": " +
        (summary.Uncertainty != EffectUncertainty.None
            ? summary.Uncertainty.ToString()
            : summary.Completeness != EffectCompleteness.Complete
                ? "IncompleteSummary"
                : "UnknownFacet");

    private static bool IsAllowed(
        INamedTypeSymbol thrown,
        ImmutableArray<INamedTypeSymbol> allowed) =>
        allowed.Any(candidate =>
            EffectTypeFacts.IsDerivedFrom(thrown, candidate));

    private static Location GetLocation(
        AttributeData attribute,
        Location fallback) =>
        attribute.ApplicationSyntaxReference?.SyntaxTree.GetLocation(
            attribute.ApplicationSyntaxReference.Span) ?? fallback;

    private static ImmutableArray<AttributeData> SelectAttributes(
        ImmutableArray<AttributeData> attributes,
        INamedTypeSymbol? expected) =>
        [.. attributes.Where(attribute =>
            AnalyzerAttributeSymbols.Is(attribute, expected))];

    private enum UnknownFacet {
        PurityUnknown,
        AllocationUnknown,
        CapabilitySetUnknown,
        ExceptionSetUnknown
    }
}
