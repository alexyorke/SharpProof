#pragma warning disable RS2001 // Disabled-by-default rules are preserved exactly; release tracking reports them as severity changes.
#pragma warning disable RS1037 // Compilation-end reporting policy is separate from descriptor boundary metadata.

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor PurityNotVerifiedRule = AnalyzerDiagnosticCatalog.Get(nameof(PurityNotVerifiedRule));
    public static readonly DiagnosticDescriptor MisplacedAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(MisplacedAttributeRule));
    public static readonly DiagnosticDescriptor MissingEnforcePureAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(MissingEnforcePureAttributeRule));
    public static readonly DiagnosticDescriptor ConflictingPurityAttributesRule = AnalyzerDiagnosticCatalog.Get(nameof(ConflictingPurityAttributesRule));
    public static readonly DiagnosticDescriptor AllowSynchronizationWithoutPurityAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(AllowSynchronizationWithoutPurityAttributeRule));
    public static readonly DiagnosticDescriptor MisplacedAllowSynchronizationAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(MisplacedAllowSynchronizationAttributeRule));
    public static readonly DiagnosticDescriptor RedundantAllowSynchronizationRule = AnalyzerDiagnosticCatalog.Get(nameof(RedundantAllowSynchronizationRule));
    public static readonly DiagnosticDescriptor PurityExplanationRule = AnalyzerDiagnosticCatalog.Get(nameof(PurityExplanationRule));
    public static readonly DiagnosticDescriptor ExceptionSummaryRule = AnalyzerDiagnosticCatalog.Get(nameof(ExceptionSummaryRule));
    public static readonly DiagnosticDescriptor UncaughtExceptionSiteRule = AnalyzerDiagnosticCatalog.Get(nameof(UncaughtExceptionSiteRule));
    public static readonly DiagnosticDescriptor BclFallbackGuessRule = AnalyzerDiagnosticCatalog.Get(nameof(BclFallbackGuessRule));
    public static readonly DiagnosticDescriptor AllocationInZeroAllocationMethodRule = AnalyzerDiagnosticCatalog.Get(nameof(AllocationInZeroAllocationMethodRule));
    public static readonly DiagnosticDescriptor MisplacedZeroAllocationsAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(MisplacedZeroAllocationsAttributeRule));
    public static readonly DiagnosticDescriptor CapabilityViolationRule = AnalyzerDiagnosticCatalog.Get(nameof(CapabilityViolationRule));
    public static readonly DiagnosticDescriptor CapabilityUnknownRule = AnalyzerDiagnosticCatalog.Get(nameof(CapabilityUnknownRule));
    public static readonly DiagnosticDescriptor MisplacedAllowedCapabilitiesAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(MisplacedAllowedCapabilitiesAttributeRule));
    public static readonly DiagnosticDescriptor EnsuresNotProvenRule = AnalyzerDiagnosticCatalog.Get(nameof(EnsuresNotProvenRule));
    public static readonly DiagnosticDescriptor EnsuresUnsupportedRule = AnalyzerDiagnosticCatalog.Get(nameof(EnsuresUnsupportedRule));
    public static readonly DiagnosticDescriptor MisplacedEnsuresAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(MisplacedEnsuresAttributeRule));
    public static readonly DiagnosticDescriptor ComplexityExceededRule = AnalyzerDiagnosticCatalog.Get(nameof(ComplexityExceededRule));
    public static readonly DiagnosticDescriptor ComplexityCouldNotBeVerifiedRule = AnalyzerDiagnosticCatalog.Get(nameof(ComplexityCouldNotBeVerifiedRule));
    public static readonly DiagnosticDescriptor MisplacedExpectedComplexityAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(MisplacedExpectedComplexityAttributeRule));
    public static readonly DiagnosticDescriptor InvalidContractArgumentRule = AnalyzerDiagnosticCatalog.Get(nameof(InvalidContractArgumentRule));
    public static readonly DiagnosticDescriptor InvalidAnalyzerConfigurationRule = AnalyzerDiagnosticCatalog.Get(nameof(InvalidAnalyzerConfigurationRule));
    public static readonly DiagnosticDescriptor UnrecognizedAttributeIdentityRule = AnalyzerDiagnosticCatalog.Get(nameof(UnrecognizedAttributeIdentityRule));
    public static readonly DiagnosticDescriptor RequiresNotProvenRule = AnalyzerDiagnosticCatalog.Get(nameof(RequiresNotProvenRule));
    public static readonly DiagnosticDescriptor RequiresUnsupportedRule = AnalyzerDiagnosticCatalog.Get(nameof(RequiresUnsupportedRule));
    public static readonly DiagnosticDescriptor MisplacedRequiresAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(MisplacedRequiresAttributeRule));
    public static readonly DiagnosticDescriptor ExceptionContractViolationRule = AnalyzerDiagnosticCatalog.Get(nameof(ExceptionContractViolationRule));
    public static readonly DiagnosticDescriptor MisplacedExceptionContractAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(MisplacedExceptionContractAttributeRule));
    public static readonly DiagnosticDescriptor InvalidAdditionalFileRule = AnalyzerDiagnosticCatalog.Get(nameof(InvalidAdditionalFileRule));
    public static readonly DiagnosticDescriptor UnknownRuntimeHazardRule = AnalyzerDiagnosticCatalog.Get(nameof(UnknownRuntimeHazardRule));
    public static readonly DiagnosticDescriptor SuggestZeroAllocationsRule = AnalyzerDiagnosticCatalog.Get(nameof(SuggestZeroAllocationsRule));
    public static readonly DiagnosticDescriptor SuggestAllowedCapabilitiesRule = AnalyzerDiagnosticCatalog.Get(nameof(SuggestAllowedCapabilitiesRule));
    public static readonly DiagnosticDescriptor SuggestExpectedComplexityRule = AnalyzerDiagnosticCatalog.Get(nameof(SuggestExpectedComplexityRule));
    public static readonly DiagnosticDescriptor SuggestExceptionContractRule = AnalyzerDiagnosticCatalog.Get(nameof(SuggestExceptionContractRule));
    public static readonly DiagnosticDescriptor SuggestEnsuresRule = AnalyzerDiagnosticCatalog.Get(nameof(SuggestEnsuresRule));
    public static readonly DiagnosticDescriptor SuggestRequiresRule = AnalyzerDiagnosticCatalog.Get(nameof(SuggestRequiresRule));
    public static readonly DiagnosticDescriptor TrustedBoundaryReviewRule = AnalyzerDiagnosticCatalog.Get(nameof(TrustedBoundaryReviewRule));
    public static readonly DiagnosticDescriptor NullableReturnContractViolationRule = AnalyzerDiagnosticCatalog.Get(nameof(NullableReturnContractViolationRule));
    public static readonly DiagnosticDescriptor NullableParameterPostconditionViolationRule = AnalyzerDiagnosticCatalog.Get(nameof(NullableParameterPostconditionViolationRule));
    public static readonly DiagnosticDescriptor NullableMemberContractViolationRule = AnalyzerDiagnosticCatalog.Get(nameof(NullableMemberContractViolationRule));
    public static readonly DiagnosticDescriptor UnsafeNullForgivingOperatorRule = AnalyzerDiagnosticCatalog.Get(nameof(UnsafeNullForgivingOperatorRule));
    public static readonly DiagnosticDescriptor UnnecessaryNullForgivingOperatorRule = AnalyzerDiagnosticCatalog.Get(nameof(UnnecessaryNullForgivingOperatorRule));
    public static readonly DiagnosticDescriptor SuggestNullableContractRule = AnalyzerDiagnosticCatalog.Get(nameof(SuggestNullableContractRule));
    public static readonly DiagnosticDescriptor NullableVerificationInconclusiveRule = AnalyzerDiagnosticCatalog.Get(nameof(NullableVerificationInconclusiveRule));
    public static readonly DiagnosticDescriptor AwaitNullConditionalRule = AnalyzerDiagnosticCatalog.Get(nameof(AwaitNullConditionalRule));
    public static readonly DiagnosticDescriptor TaskConvertedToStringRule = AnalyzerDiagnosticCatalog.Get(nameof(TaskConvertedToStringRule));
    public static readonly DiagnosticDescriptor TaskCompletionSourceContinuationsRule = AnalyzerDiagnosticCatalog.Get(nameof(TaskCompletionSourceContinuationsRule));
    public static readonly DiagnosticDescriptor AsyncVoidRule = AnalyzerDiagnosticCatalog.Get(nameof(AsyncVoidRule));
    public static readonly DiagnosticDescriptor BlockingAsyncRule = AnalyzerDiagnosticCatalog.Get(nameof(BlockingAsyncRule));
    public static readonly DiagnosticDescriptor NullTaskReturnRule = AnalyzerDiagnosticCatalog.Get(nameof(NullTaskReturnRule));
    public static readonly DiagnosticDescriptor TaskUsedAsDisposableRule = AnalyzerDiagnosticCatalog.Get(nameof(TaskUsedAsDisposableRule));
    public static readonly DiagnosticDescriptor AsyncValidationDeferredRule = AnalyzerDiagnosticCatalog.Get(nameof(AsyncValidationDeferredRule));
    public static readonly DiagnosticDescriptor CollectionMutationDuringEnumerationRule = AnalyzerDiagnosticCatalog.Get(nameof(CollectionMutationDuringEnumerationRule));
    public static readonly DiagnosticDescriptor CapturedLoopVariableRule = AnalyzerDiagnosticCatalog.Get(nameof(CapturedLoopVariableRule));
    public static readonly DiagnosticDescriptor MutableStructRule = AnalyzerDiagnosticCatalog.Get(nameof(MutableStructRule));
    public static readonly DiagnosticDescriptor OwnedDisposableFieldRule = AnalyzerDiagnosticCatalog.Get(nameof(OwnedDisposableFieldRule));
    public static readonly DiagnosticDescriptor HttpClientInLoopRule = AnalyzerDiagnosticCatalog.Get(nameof(HttpClientInLoopRule));
    public static readonly DiagnosticDescriptor UnsynchronizedSharedMutationRule = AnalyzerDiagnosticCatalog.Get(nameof(UnsynchronizedSharedMutationRule));
    public static readonly DiagnosticDescriptor ConcurrentCollectionEnumerationRule = AnalyzerDiagnosticCatalog.Get(nameof(ConcurrentCollectionEnumerationRule));
    public static readonly DiagnosticDescriptor BoxingInLoopRule = AnalyzerDiagnosticCatalog.Get(nameof(BoxingInLoopRule));
    public static readonly DiagnosticDescriptor MaybeNullResultDereferenceRule = AnalyzerDiagnosticCatalog.Get(nameof(MaybeNullResultDereferenceRule));
    public static readonly DiagnosticDescriptor PrematureQueryMaterializationRule = AnalyzerDiagnosticCatalog.Get(nameof(PrematureQueryMaterializationRule));
    public static readonly DiagnosticDescriptor DeferredQuerySideEffectRule = AnalyzerDiagnosticCatalog.Get(nameof(DeferredQuerySideEffectRule));
    public static readonly DiagnosticDescriptor QueryTranslationRiskRule = AnalyzerDiagnosticCatalog.Get(nameof(QueryTranslationRiskRule));
    public static readonly DiagnosticDescriptor SerializationCycleRiskRule = AnalyzerDiagnosticCatalog.Get(nameof(SerializationCycleRiskRule));
    public static readonly DiagnosticDescriptor SerializerAttributeMismatchRule = AnalyzerDiagnosticCatalog.Get(nameof(SerializerAttributeMismatchRule));
    public static readonly DiagnosticDescriptor IneffectiveRequiredAttributeRule = AnalyzerDiagnosticCatalog.Get(nameof(IneffectiveRequiredAttributeRule));
    public static readonly DiagnosticDescriptor UncheckedAllocationArithmeticRule = AnalyzerDiagnosticCatalog.Get(nameof(UncheckedAllocationArithmeticRule));
    public static readonly DiagnosticDescriptor SuppressionWithoutJustificationRule = AnalyzerDiagnosticCatalog.Get(nameof(SuppressionWithoutJustificationRule));
    public static readonly DiagnosticDescriptor NullableAnalysisDisabledRule = AnalyzerDiagnosticCatalog.Get(nameof(NullableAnalysisDisabledRule));
    public static readonly DiagnosticDescriptor IdenticalOperandsRule = AnalyzerDiagnosticCatalog.Get(nameof(IdenticalOperandsRule));
    public static readonly DiagnosticDescriptor ContainerOwnedServiceDisposedRule = AnalyzerDiagnosticCatalog.Get(nameof(ContainerOwnedServiceDisposedRule));
    public static readonly DiagnosticDescriptor UnconsumedDeferredQueryRule = AnalyzerDiagnosticCatalog.Get(nameof(UnconsumedDeferredQueryRule));
}

internal static class AnalyzerDiagnosticCatalog
{
    private const string ResourceName = "SharpProof.Analyzer.DiagnosticCatalog.json";

    private static readonly ImmutableDictionary<string, DiagnosticDescriptor> DescriptorsByField = Load();

    internal static readonly ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics = DescriptorsByField
        .Values
        .OrderBy(static descriptor => int.Parse(descriptor.Id.Substring(2), CultureInfo.InvariantCulture))
        .ToImmutableArray();

    internal static DiagnosticDescriptor Get(string fieldName) => DescriptorsByField[fieldName];

    private static ImmutableDictionary<string, DiagnosticDescriptor> Load()
    {
        using var stream = typeof(AnalyzerDiagnosticCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded diagnostic catalog '{ResourceName}'.");
        var definitions = JsonSerializer.Deserialize<DiagnosticDefinition[]>(stream)
            ?? throw new InvalidOperationException("The embedded diagnostic catalog is empty.");
        var descriptors = ImmutableDictionary.CreateBuilder<string, DiagnosticDescriptor>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            if (string.IsNullOrWhiteSpace(definition.FieldName) || string.IsNullOrWhiteSpace(definition.Id))
            {
                throw new InvalidOperationException("Every diagnostic catalog entry requires a field name and ID.");
            }

            if (!Enum.TryParse<DiagnosticSeverity>(definition.DefaultSeverity, out var severity))
            {
                throw new InvalidOperationException($"Diagnostic '{definition.Id}' has invalid severity '{definition.DefaultSeverity}'.");
            }

            if (!ids.Add(definition.Id) || descriptors.ContainsKey(definition.FieldName))
            {
                throw new InvalidOperationException($"Duplicate diagnostic catalog entry '{definition.FieldName}'/'{definition.Id}'.");
            }

            descriptors.Add(definition.FieldName, new DiagnosticDescriptor(
                definition.Id,
                definition.Title,
                definition.MessageFormat,
                definition.Category,
                severity,
                definition.IsEnabledByDefault,
                definition.Description,
                definition.HelpLinkUri,
                definition.CustomTags ?? Array.Empty<string>()));
        }

        return descriptors.ToImmutable();
    }

    private sealed class DiagnosticDefinition
    {
        public string FieldName { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string MessageFormat { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string DefaultSeverity { get; set; } = string.Empty;
        public bool IsEnabledByDefault { get; set; }
        public string Description { get; set; } = string.Empty;
        public string HelpLinkUri { get; set; } = string.Empty;
        public string[]? CustomTags { get; set; }
    }
}

#pragma warning restore RS1037
#pragma warning restore RS2001
