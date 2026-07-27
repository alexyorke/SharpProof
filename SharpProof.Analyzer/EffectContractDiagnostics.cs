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
        if (!enforcePure &&
            !zeroAllocations &&
            !doesNotThrow &&
            capabilityAttributes.IsDefaultOrEmpty &&
            exceptionAttributes.IsDefaultOrEmpty)
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
        var isUnknown = IsUnknown(summary);
        var unknownReason = FormatUnknown(summary);
        var hasUnknown =
            !capabilityAttributes.IsDefaultOrEmpty && !capabilities.IsValid ||
            !exceptionAttributes.IsDefaultOrEmpty && !exceptions.IsValid;

        if (enforcePure && (isUnknown || !IsObservablePure(summary))) {
            hasUnknown = true;
            reportDiagnostic(
                Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.PurityNotVerifiedRule,
                    location,
                    method.Name));
        }

        if (zeroAllocations) {
            if (isUnknown ||
                summary.Allocation != EffectAllocationKind.None) {
                hasUnknown = true;
                reportDiagnostic(
                    Diagnostic.Create(
                        GeneratedDiagnosticDescriptors.ZeroAllocationsNotVerifiedRule,
                        location,
                        method.Name,
                        isUnknown
                            ? unknownReason
                            : "may-effect summary includes allocation: " +
                              summary.Allocation));
            }
        }

        if (!capabilityAttributes.IsDefaultOrEmpty && capabilities.IsValid) {
            if (isUnknown || summary.Capabilities.IsUnknown) {
                hasUnknown = true;
                reportDiagnostic(
                    Diagnostic.Create(
                        GeneratedDiagnosticDescriptors.CapabilityUnknownRule,
                        location,
                        "method summary",
                        method.Name,
                        unknownReason));
            }
            else {
                var actual = result.Projection.Capabilities;
                var disallowed = actual & ~capabilities.Value;
                if (disallowed != EffectContractCapabilityKind.None) {
                    hasUnknown = true;
                    reportDiagnostic(
                        Diagnostic.Create(
                            GeneratedDiagnosticDescriptors.CapabilityUnknownRule,
                            location,
                            "method summary",
                            method.Name,
                            "may-effect summary includes disallowed " +
                            "capabilities: " + disallowed));
                }
            }
        }

        if (doesNotThrow ||
            !exceptionAttributes.IsDefaultOrEmpty && exceptions.IsValid) {
            var contractName = doesNotThrow
                ? "[DoesNotThrow]"
                : "[AllowedExceptions]";
            if (isUnknown || summary.Throws.IncludesUnknown) {
                hasUnknown = true;
                reportDiagnostic(
                    Diagnostic.Create(
                        GeneratedDiagnosticDescriptors.ExceptionContractNotVerifiedRule,
                        location,
                        method.Name,
                        contractName,
                        unknownReason));
            }
            else {
                var disallowed = doesNotThrow
                    ? summary.Throws.Types
                    : [.. summary.Throws.Types
                        .Where(type => !IsAllowed(type, exceptions.Types))];
                if (!disallowed.IsDefaultOrEmpty) {
                    hasUnknown = true;
                    reportDiagnostic(
                        Diagnostic.Create(
                            GeneratedDiagnosticDescriptors.ExceptionContractNotVerifiedRule,
                            location,
                            method.Name,
                            contractName,
                            "may-effect summary includes disallowed " +
                            "exceptions: " + string.Join(
                                ", ",
                                disallowed.Select(static type => type.MetadataName))));
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
                !TryGetInt64(attribute.ConstructorArguments[0], out var raw) ||
                raw < 0 ||
                ((EffectContractCapabilityKind)raw &
                 ~EffectContractMetadata.AllCapabilities) != 0) {
                if (session.TryMarkAttributeValidated(attribute))
                    reportDiagnostic(
                        InvalidContractArgumentDiagnostics.Create(
                            "[AllowedCapabilities]",
                            "<invalid>",
                            "expected a defined SharpProofCapability flags value",
                            GetLocation(attribute, fallbackLocation)));
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
                    IsDerivedFrom(type, exceptionType))) {
                types.AddRange(values.Select(static argument =>
                    (INamedTypeSymbol)argument.Value!));
                continue;
            }
            ReportInvalidExceptions(
                attribute,
                fallbackLocation,
                session,
                reportDiagnostic);
            isValid = false;
        }
        return isValid ? (types.ToImmutable(), true) : ([], false);
    }

    private static void ReportInvalidExceptions(
        AttributeData attribute,
        Location fallbackLocation,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic) {
        if (session.TryMarkAttributeValidated(attribute))
            reportDiagnostic(
                InvalidContractArgumentDiagnostics.Create(
                    "[AllowedExceptions]",
                    "<invalid>",
                    "expected only System.Exception-derived types",
                    GetLocation(attribute, fallbackLocation)));
    }

    private static bool IsObservablePure(EffectSummary summary) {
        if (IsUnknown(summary) || !summary.Capabilities.IsEmpty)
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

    private static bool IsUnknown(EffectSummary summary) {
        const EffectUncertainty semanticUncertainty =
            EffectUncertainty.Dispatch |
            EffectUncertainty.UnsupportedOperation |
            EffectUncertainty.UnmodeledCall |
            EffectUncertainty.Recursion |
            EffectUncertainty.InvalidContract;
        return summary.IsBottom ||
               summary.Completeness != EffectCompleteness.Complete ||
               summary.Reads.IsUnknown ||
               summary.Writes.IsUnknown ||
               summary.Allocation == EffectAllocationKind.Unknown ||
               summary.Capabilities.IsUnknown ||
               summary.Throws.IncludesUnknown ||
               (summary.Uncertainty & semanticUncertainty) != 0;
    }

    private static string FormatUnknown(EffectSummary summary) {
        if (summary.Uncertainty != EffectUncertainty.None)
            return summary.Uncertainty.ToString();
        if (summary.Completeness != EffectCompleteness.Complete)
            return "incomplete summary";
        return "unknown effect facet";
    }

    private static bool IsAllowed(
        INamedTypeSymbol thrown,
        ImmutableArray<INamedTypeSymbol> allowed) =>
        allowed.Any(candidate => IsDerivedFrom(thrown, candidate));

    private static bool IsDerivedFrom(
        INamedTypeSymbol type,
        INamedTypeSymbol expectedBase) {
        for (var current = type; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    expectedBase.OriginalDefinition))
                return true;
        return false;
    }

    private static bool TryGetInt64(TypedConstant argument, out long value) {
        if (argument.Value is
            sbyte or byte or short or ushort or int or uint or long) {
            value = Convert.ToInt64(
                argument.Value,
                CultureInfo.InvariantCulture);
            return true;
        }
        value = 0;
        return false;
    }

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
}
