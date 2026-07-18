using System;
using System.Collections.Immutable;
using System.Linq;
using SharpProof.Analyzer;

namespace SharpProof
{

internal enum CodeFixHandlerFamily
{
    SimpleRemoval,
    AddPurity,
    AddPurityOrRemoveSynchronization,
    MisplacedRequires,
    InferredContract,
    NullForgivingRemoval
}

internal enum SimpleRemovalOperation
{
    MisplacedAttribute,
    DeclarationAndAccessors,
    DiagnosticContract
}

internal sealed record SimpleRemovalRegistration(
    string DiagnosticId,
    string Title,
    SimpleRemovalOperation Operation,
    string EquivalenceKey,
    params string[] AttributeTypeNames);

internal sealed record CodeFixHandlerRegistration(
    string DiagnosticId,
    CodeFixHandlerFamily Family,
    SimpleRemovalRegistration? SimpleRemoval = null);

internal static class CodeFixHandlerRegistry
{
    internal static readonly ImmutableArray<CodeFixHandlerRegistration> All = CreateRegistrations();

    internal static readonly ImmutableArray<string> FixableDiagnosticIds =
        All.Select(static registration => registration.DiagnosticId).ToImmutableArray();

    private static readonly ImmutableDictionary<string, CodeFixHandlerRegistration> RegistrationsById =
        All.ToImmutableDictionary(static registration => registration.DiagnosticId, StringComparer.Ordinal);

    internal static bool TryGet(string diagnosticId, out CodeFixHandlerRegistration registration) =>
        RegistrationsById.TryGetValue(diagnosticId, out registration!);

    private static ImmutableArray<CodeFixHandlerRegistration> CreateRegistrations()
    {
        var builder = ImmutableArray.CreateBuilder<CodeFixHandlerRegistration>();
        AddSimple(builder, SharpProofDiagnostics.PurityNotVerifiedId,
            "Remove [EnforcePure] and [Pure] attributes", SimpleRemovalOperation.DeclarationAndAccessors,
            "RemoveAttributesMatchingAsyncSP0002", "EnforcePureAttribute", "PureAttribute");
        AddSimple(builder, SharpProofDiagnostics.MisplacedAttributeId,
            "Remove misplaced purity attribute", SimpleRemovalOperation.MisplacedAttribute,
            "RemoveMisplacedAttributeAsync");
        AddSimple(builder, SharpProofDiagnostics.ConflictingPurityAttributesId,
            "Remove conflicting purity boundary attributes", SimpleRemovalOperation.DeclarationAndAccessors,
            "RemoveAttributesMatchingAsyncSP0005", "PureAttribute", "PureExternalAttribute", "ImpureAttribute");
        AddSimple(builder, SharpProofDiagnostics.MisplacedAllowSynchronizationAttributeId,
            "Remove misplaced [AllowSynchronization] attribute", SimpleRemovalOperation.MisplacedAttribute,
            "RemoveMisplacedAttributeAsyncSP0007");
        AddSimple(builder, SharpProofDiagnostics.RedundantAllowSynchronizationId,
            "Remove [AllowSynchronization] attribute", SimpleRemovalOperation.DeclarationAndAccessors,
            "RemoveAttributesMatchingAsyncSP0008", "AllowSynchronizationAttribute");
        AddSimple(builder, SharpProofDiagnostics.AllocationInZeroAllocationMethodId,
            "Remove [ZeroAllocations] attribute", SimpleRemovalOperation.DiagnosticContract,
            "RemoveContractAttributeAsyncSP0013", "ZeroAllocationsAttribute");
        AddSimple(builder, SharpProofDiagnostics.MisplacedZeroAllocationsAttributeId,
            "Remove misplaced [ZeroAllocations] attribute", SimpleRemovalOperation.MisplacedAttribute,
            "RemoveMisplacedAttributeAsyncSP0014");
        AddSimple(builder, SharpProofDiagnostics.CapabilityViolationId,
            "Remove [AllowedCapabilities] attribute", SimpleRemovalOperation.DiagnosticContract,
            "RemoveContractAttributeAsyncSP0015", "AllowedCapabilitiesAttribute");
        AddSimple(builder, SharpProofDiagnostics.CapabilityUnknownId,
            "Remove [AllowedCapabilities] attribute", SimpleRemovalOperation.DiagnosticContract,
            "RemoveContractAttributeAsyncSP0016", "AllowedCapabilitiesAttribute");
        AddSimple(builder, SharpProofDiagnostics.MisplacedAllowedCapabilitiesAttributeId,
            "Remove misplaced [AllowedCapabilities] attribute", SimpleRemovalOperation.MisplacedAttribute,
            "RemoveMisplacedAttributeAsyncSP0017");
        AddSimple(builder, SharpProofDiagnostics.EnsuresNotProvenId,
            "Remove [Ensures] attribute", SimpleRemovalOperation.DiagnosticContract,
            "RemoveContractAttributeAsyncSP0018", "EnsuresAttribute");
        AddSimple(builder, SharpProofDiagnostics.EnsuresUnsupportedId,
            "Remove [Ensures] attribute", SimpleRemovalOperation.DiagnosticContract,
            "RemoveContractAttributeAsyncSP0019", "EnsuresAttribute");
        AddSimple(builder, SharpProofDiagnostics.MisplacedEnsuresAttributeId,
            "Remove misplaced [Ensures] attribute", SimpleRemovalOperation.MisplacedAttribute,
            "RemoveMisplacedAttributeAsyncSP0020");
        AddSimple(builder, SharpProofDiagnostics.ComplexityExceededId,
            "Remove [ExpectedComplexity] attribute", SimpleRemovalOperation.DiagnosticContract,
            "RemoveContractAttributeAsyncSP0021", "ExpectedComplexityAttribute");
        AddSimple(builder, SharpProofDiagnostics.ComplexityCouldNotBeVerifiedId,
            "Remove [ExpectedComplexity] attribute", SimpleRemovalOperation.DiagnosticContract,
            "RemoveContractAttributeAsyncSP0022", "ExpectedComplexityAttribute");
        AddSimple(builder, SharpProofDiagnostics.MisplacedExpectedComplexityAttributeId,
            "Remove misplaced [ExpectedComplexity] attribute", SimpleRemovalOperation.MisplacedAttribute,
            "RemoveMisplacedAttributeAsyncSP0023");

        Add(builder, CodeFixHandlerFamily.AddPurity, SharpProofDiagnostics.MissingEnforcePureAttributeId);
        Add(builder, CodeFixHandlerFamily.AddPurityOrRemoveSynchronization,
            SharpProofDiagnostics.AllowSynchronizationWithoutPurityAttributeId);
        Add(builder, CodeFixHandlerFamily.MisplacedRequires, SharpProofDiagnostics.MisplacedRequiresAttributeId);
        Add(builder, CodeFixHandlerFamily.InferredContract,
            SharpProofDiagnostics.SuggestZeroAllocationsId,
            SharpProofDiagnostics.SuggestAllowedCapabilitiesId,
            SharpProofDiagnostics.SuggestExpectedComplexityId,
            SharpProofDiagnostics.SuggestExceptionContractId,
            SharpProofDiagnostics.SuggestEnsuresId,
            SharpProofDiagnostics.SuggestRequiresId,
            SharpProofDiagnostics.SuggestNullableContractId);
        Add(builder, CodeFixHandlerFamily.NullForgivingRemoval,
            SharpProofDiagnostics.UnnecessaryNullForgivingOperatorId);
        return builder.ToImmutable();
    }

    private static void AddSimple(
        ImmutableArray<CodeFixHandlerRegistration>.Builder builder,
        string diagnosticId,
        string title,
        SimpleRemovalOperation operation,
        string equivalenceKey,
        params string[] attributeTypeNames)
    {
        var removal = new SimpleRemovalRegistration(
            diagnosticId, title, operation, equivalenceKey, attributeTypeNames);
        builder.Add(new CodeFixHandlerRegistration(diagnosticId, CodeFixHandlerFamily.SimpleRemoval, removal));
    }

    private static void Add(
        ImmutableArray<CodeFixHandlerRegistration>.Builder builder,
        CodeFixHandlerFamily family,
        params string[] diagnosticIds)
    {
        foreach (var diagnosticId in diagnosticIds)
            builder.Add(new CodeFixHandlerRegistration(diagnosticId, family));
    }
}
}
