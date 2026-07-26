namespace SharpProof.Analyzer;

internal static class EffectContractDiagnostics {
    private const SharpProofCapability AllCapabilities =
        SharpProofCapability.IO |
        SharpProofCapability.FileRead |
        SharpProofCapability.FileWrite |
        SharpProofCapability.Network |
        SharpProofCapability.Console |
        SharpProofCapability.Process |
        SharpProofCapability.Environment |
        SharpProofCapability.Registry |
        SharpProofCapability.Clock |
        SharpProofCapability.Randomness |
        SharpProofCapability.Reflection |
        SharpProofCapability.Synchronization |
        SharpProofCapability.NativeInterop;

    internal static AnalyzerSemanticOutcome Analyze(
        IMethodSymbol method,
        SyntaxNode declaration,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken) {
        var attributes = AnalyzerAttributeSymbols.GetCallableAttributes(method)
            .ToImmutableArray();
        var enforcePure = attributes.Any(attribute =>
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.EnforcePure));
        var zeroAllocations = attributes.Any(attribute =>
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.ZeroAllocations));
        var doesNotThrow = attributes.Any(attribute =>
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.DoesNotThrow));
        var capabilityAttributes = attributes.Where(attribute =>
            AnalyzerAttributeSymbols.Is(
                attribute,
                session.Attributes.AllowedCapabilities)).ToImmutableArray();
        var exceptionAttributes = attributes.Where(attribute =>
            AnalyzerAttributeSymbols.Is(
                attribute,
                session.Attributes.AllowedExceptions)).ToImmutableArray();
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
            reportDiagnostic);
        var exceptions = DecodeAllowedExceptions(
            exceptionAttributes,
            session.Compilation,
            location,
            reportDiagnostic);
        cancellationToken.ThrowIfCancellationRequested();
        var result = session.AnalyzeEffects(method, cancellationToken);
        var summary = result.Summary;
        var isUnknown = IsUnknown(summary);
        var unknownReason = FormatUnknown(summary);
        var hasUnknown =
            !capabilityAttributes.IsDefaultOrEmpty && !capabilities.IsValid ||
            !exceptionAttributes.IsDefaultOrEmpty && !exceptions.IsValid;

        if (enforcePure) {
            if (isUnknown || !IsObservablePure(summary))
                hasUnknown = true;
            if (isUnknown || !IsObservablePure(summary))
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
                if (disallowed != SharpProofCapability.None) {
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
            ? AnalyzerSemanticOutcome.Unknown
            : AnalyzerSemanticOutcome.Proven;
    }

    private static DecodedCapabilities DecodeCapabilities(
        ImmutableArray<AttributeData> attributes,
        Location fallbackLocation,
        Action<Diagnostic> reportDiagnostic) {
        var value = SharpProofCapability.None;
        foreach (var attribute in attributes) {
            if (attribute.ConstructorArguments.Length != 1 ||
                !TryGetInt64(attribute.ConstructorArguments[0], out var raw) ||
                raw < 0 ||
                ((SharpProofCapability)raw & ~AllCapabilities) != 0) {
                reportDiagnostic(
                    InvalidContractArgumentDiagnostics.Create(
                        "[AllowedCapabilities]",
                        "<invalid>",
                        "expected a defined SharpProofCapability flags value",
                        GetLocation(attribute, fallbackLocation)));
                return DecodedCapabilities.Invalid;
            }
            value |= (SharpProofCapability)raw;
        }
        return new DecodedCapabilities(value, true);
    }

    private static DecodedExceptions DecodeAllowedExceptions(
        ImmutableArray<AttributeData> attributes,
        Compilation compilation,
        Location fallbackLocation,
        Action<Diagnostic> reportDiagnostic) {
        var exceptionType = compilation.GetTypeByMetadataName("System.Exception");
        var types = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach (var attribute in attributes) {
            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Kind != TypedConstantKind.Array ||
                attribute.ConstructorArguments[0].Values.IsDefault ||
                exceptionType == null) {
                ReportInvalidExceptions(attribute, fallbackLocation, reportDiagnostic);
                return DecodedExceptions.Invalid;
            }
            foreach (var argument in attribute.ConstructorArguments[0].Values) {
                if (argument.Value is not INamedTypeSymbol type ||
                    !IsDerivedFrom(type, exceptionType)) {
                    ReportInvalidExceptions(attribute, fallbackLocation, reportDiagnostic);
                    return DecodedExceptions.Invalid;
                }
                types.Add(type);
            }
        }
        return new DecodedExceptions(types.ToImmutable(), true);
    }

    private static void ReportInvalidExceptions(
        AttributeData attribute,
        Location fallbackLocation,
        Action<Diagnostic> reportDiagnostic) =>
        reportDiagnostic(
            InvalidContractArgumentDiagnostics.Create(
                "[AllowedExceptions]",
                "<invalid>",
                "expected only System.Exception-derived types",
                GetLocation(attribute, fallbackLocation)));

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
        switch (argument.Value) {
            case sbyte number: value = number; return true;
            case byte number: value = number; return true;
            case short number: value = number; return true;
            case ushort number: value = number; return true;
            case int number: value = number; return true;
            case uint number: value = number; return true;
            case long number: value = number; return true;
            default: value = 0; return false;
        }
    }

    private static Location GetLocation(
        AttributeData attribute,
        Location fallback) =>
        attribute.ApplicationSyntaxReference?.SyntaxTree.GetLocation(
            attribute.ApplicationSyntaxReference.Span) ?? fallback;

    private readonly struct DecodedCapabilities {
        internal DecodedCapabilities(SharpProofCapability value, bool isValid) {
            Value = value;
            IsValid = isValid;
        }

        internal SharpProofCapability Value { get; }
        internal bool IsValid { get; }
        internal static DecodedCapabilities Invalid { get; } =
            new(SharpProofCapability.None, false);
    }

    private readonly struct DecodedExceptions {
        internal DecodedExceptions(
            ImmutableArray<INamedTypeSymbol> types,
            bool isValid) {
            Types = types;
            IsValid = isValid;
        }

        internal ImmutableArray<INamedTypeSymbol> Types { get; }
        internal bool IsValid { get; }
        internal static DecodedExceptions Invalid { get; } =
            new([], false);
    }
}
