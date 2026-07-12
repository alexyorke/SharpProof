using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine;

internal enum PurityPolicyDecision
{
    Unknown,
    Pure,
    Impure
}

internal sealed record PurityPolicyCandidate(
    PurityPolicyDecision Decision,
    string Source,
    int Priority,
    string Category,
    string CatalogSource);

internal sealed record PurityPolicyResolution(
    PurityPolicyDecision Decision,
    PurityPolicyCandidate? Winner,
    ImmutableArray<PurityPolicyCandidate> Candidates)
{
    internal ImmutableArray<PurityPolicyCandidate> OverriddenCandidates =>
        Winner == null
            ? ImmutableArray<PurityPolicyCandidate>.Empty
            : Candidates.Remove(Winner);
}

internal static class PurityPolicyResolver
{
    internal static PurityPolicyResolution Resolve(
        IMethodSymbol method,
        Compilation compilation,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        return ResolveCore(method, compilation, attributePolicy, null);
    }

    internal static PurityPolicyResolution ResolveInvocation(
        IMethodSymbol method,
        IInvocationOperation invocation,
        Compilation compilation,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        if (invocation == null) throw new ArgumentNullException(nameof(invocation));
        return ResolveCore(method, compilation, attributePolicy, invocation);
    }

    private static PurityPolicyResolution ResolveCore(
        IMethodSymbol method,
        Compilation compilation,
        SharpProofAttributeIdentityPolicy attributePolicy,
        IInvocationOperation? invocation)
    {
        if (method == null) throw new ArgumentNullException(nameof(method));
        if (compilation == null) throw new ArgumentNullException(nameof(compilation));
        if (attributePolicy == null) throw new ArgumentNullException(nameof(attributePolicy));

        method = method.OriginalDefinition;
        var candidates = ImmutableArray.CreateBuilder<PurityPolicyCandidate>();

        AddDirectAttributeCandidates(method, attributePolicy, candidates);
        AddAssemblyAttributeCandidates(method, attributePolicy, candidates);

        if (PurityAnalysisEngine.IsInConfiguredImpureNamespaceOrType(method) &&
            !PurityAnalysisEngine.IsConfiguredKnownPureMember(method))
            candidates.Add(Impure(
                "configured_impure_namespace_or_type",
                30,
                "catalog_hit",
                "known_impure_namespace_or_type"));

        if (ImpurityCatalog.TryGetConfiguredKnownImpureMember(method, out _))
            candidates.Add(Impure("configured_impure_member", 30, "impure_callee", "config_known_impure"));

        if (ImpurityCatalog.TryGetConfiguredKnownPureMember(method, out _))
            candidates.Add(Pure("configured_pure_member", 30, "configured_pure", "config_known_pure"));

        if (invocation != null &&
            PurityAnalysisEngine.TryGetSemanticKnownImpureCatalogSource(invocation, out var semanticCatalogSource))
            candidates.Add(Impure(
                "invocation_semantic_rule",
                24,
                "catalog_hit",
                semanticCatalogSource));

        var metadata = PurityAnalysisEngine.GetTrustedMethodPurityMetadata(method, compilation);
        if (metadata.HasTrustedGeneratedPurity)
            candidates.Add(metadata.GeneratedPurity.IsPure
                ? Pure("generated_summary", 40, "generated_pure", "generated_purity_summary")
                : Impure(
                    "generated_summary",
                    25,
                    metadata.GeneratedPurity.PrimaryCategory,
                    "generated_purity_summary"));

        if (metadata.KnownImpureMemberSource != null &&
            !metadata.HasConfiguredKnownImpureMember)
            candidates.Add(Impure(
                "built_in_impure_catalog",
                ShouldPreferSemanticImpurityEvidence(metadata.KnownImpureMemberSource) ? 24 : 50,
                PurityAnalysisEngine.GetKnownImpureCatalogHitCategory(method, true),
                metadata.KnownImpureMemberSource));

        if (Rules.AwaitableRuntimeMemberClassifier.IsKnownPureAwaitInfrastructureMethod(method))
            candidates.Add(Pure(
                "built_in_await_infrastructure",
                45,
                "known_pure",
                "await_infrastructure"));

        if (method.DeclaringSyntaxReferences.Length == 0 &&
            PurityAnalysisEngine.IsInImpureNamespaceOrType(method) &&
            !PurityAnalysisEngine.IsConfiguredKnownPureMember(method) &&
            !PurityAnalysisEngine.IsKnownPureBCLMember(method, compilation) &&
            !Rules.AwaitableRuntimeMemberClassifier.IsKnownPureAwaitInfrastructureMethod(method))
            candidates.Add(Impure(
                "built_in_impure_namespace_or_type",
                50,
                PurityAnalysisEngine.GetKnownImpureCatalogHitCategory(method, true),
                "known_impure_namespace_or_type"));

        if (!metadata.HasTrustedGeneratedPurity &&
            PurityAnalysisEngine.IsKnownPureBCLMember(method, compilation))
            candidates.Add(Pure("built_in_pure_catalog", 50, "known_pure", "built_in_purity_catalog"));

        var ordered = candidates
            .OrderBy(static candidate => candidate.Priority)
            .ThenBy(static candidate => candidate.Decision == PurityPolicyDecision.Impure ? 0 : 1)
            .ThenBy(static candidate => candidate.Source, StringComparer.Ordinal)
            .ToImmutableArray();
        var winner = ordered.FirstOrDefault();
        return new PurityPolicyResolution(
            winner?.Decision ?? PurityPolicyDecision.Unknown,
            winner,
            ordered);
    }

    internal static bool IsAuthoritativeDeclaration(PurityPolicyCandidate? candidate)
    {
        return candidate?.Source is
            "member_impure_attribute" or
            "member_pure_external_attribute" or
            "recognized_external_pure_attribute" or
            "assembly_impure_attribute" or
            "assembly_pure_external_attribute" or
            "configured_impure_namespace_or_type" or
            "configured_impure_member" or
            "configured_pure_member";
    }

    private static bool ShouldPreferSemanticImpurityEvidence(string source)
    {
        return source is
            "array_mutation_semantic_rule" or
            "assembly_load_context_semantic_rule" or
            "random_semantic_rule" or
            "string_builder_semantic_rule" or
            "threading_semantic_rule";
    }

    private static void AddDirectAttributeCandidates(
        IMethodSymbol method,
        SharpProofAttributeIdentityPolicy attributePolicy,
        ICollection<PurityPolicyCandidate> candidates)
    {
        if (attributePolicy.HasAttribute(method, "ImpureAttribute"))
            candidates.Add(Impure("member_impure_attribute", 10, "impure_boundary_attribute", "attribute"));

        if (attributePolicy.HasAttribute(method, "PureExternalAttribute"))
            candidates.Add(Pure("member_pure_external_attribute", 10, "pure_boundary_attribute", "attribute"));

        if (GetPolicyAttributes(method).Any(static attribute =>
                attribute.AttributeClass != null &&
                SharpProofAttributeIdentityPolicy.IsRecognizedExternalPureAttribute(attribute.AttributeClass)))
            candidates.Add(Pure(
                "recognized_external_pure_attribute",
                10,
                "pure_boundary_attribute",
                "recognized_external_attribute"));
    }

    private static void AddAssemblyAttributeCandidates(
        IMethodSymbol method,
        SharpProofAttributeIdentityPolicy attributePolicy,
        ICollection<PurityPolicyCandidate> candidates)
    {
        var attributes = method.ContainingAssembly?.GetAttributes() ?? ImmutableArray<AttributeData>.Empty;
        if (attributes.Any(attribute => attributePolicy.IsAccepted(attribute, "ImpureAttribute")))
            candidates.Add(Impure("assembly_impure_attribute", 20, "impure_boundary_attribute", "attribute"));

        if (attributes.Any(attribute => attributePolicy.IsAccepted(attribute, "PureExternalAttribute")))
            candidates.Add(Pure("assembly_pure_external_attribute", 20, "pure_boundary_attribute", "attribute"));
    }

    private static IEnumerable<AttributeData> GetPolicyAttributes(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes()) yield return attribute;

        if (method.MethodKind != MethodKind.PropertyGet || method.AssociatedSymbol is not IPropertySymbol property)
            yield break;

        foreach (var attribute in property.GetAttributes()) yield return attribute;
    }

    private static PurityPolicyCandidate Pure(string source, int priority, string category, string catalogSource)
    {
        return new PurityPolicyCandidate(PurityPolicyDecision.Pure, source, priority, category, catalogSource);
    }

    private static PurityPolicyCandidate Impure(string source, int priority, string category, string catalogSource)
    {
        return new PurityPolicyCandidate(PurityPolicyDecision.Impure, source, priority, category, catalogSource);
    }
}
