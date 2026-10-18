// Specification packs are audited package data. They are never discovered
// from the consumer's filesystem and are inactive unless explicitly selected.
namespace SharpProof.CompilerArtifact;

internal sealed class CompilerSpecificationPackProvider
{
    private readonly IrFactory _factory;
    private readonly ImmutableDictionary<string, CompilerSpecificationMethodEvidence> _methods;
    private ImmutableDictionary<IMethodSymbol, CompilerSpecificationMethodEvidence?> _resolved =
        ImmutableDictionary.Create<IMethodSymbol, CompilerSpecificationMethodEvidence?>(SymbolEqualityComparer.Default);

    internal CompilerSpecificationPackProvider(
        IrFactory factory,
        IEnumerable<string>? enabledPacks)
        : this(factory, ResolveConfiguration(enabledPacks))
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
    }

    internal CompilerSpecificationPackProvider(
        IrFactory factory,
        CompilerSpecificationPackConfiguration authority)
    {
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        authority = ArgumentNullGuard.NotNull(authority, nameof(authority));
        var catalog = RelationalSpecPackCatalogData.Catalog;
        var selectedPacks = authority.SpecificationPackIds;
        var catalogVersion = authority.SpecificationPackCatalogVersion;
        var catalogSha256 = authority.SpecificationPackCatalogSha256;
        if (!CompilerSpecificationPackSelection.IsValid(
                selectedPacks,
                catalogVersion,
                catalogSha256) ||
            catalogVersion != catalog.Version ||
            catalogSha256 != catalog.EvidenceSha256)
        {
            throw new InvalidOperationException(
                "The SharpProof specification-pack authority is not current.");
        }

        var methods = ImmutableDictionary.Create<
            string,
            CompilerSpecificationMethodEvidence>(StringComparer.Ordinal);
        foreach (var packId in selectedPacks!)
        {
            var pack = catalog.Packs[packId];

            foreach (var method in pack.Methods)
            {
                var definition = new CompilerSpecificationMethodEvidence(
                    method.DocumentationCommentId, method.Assemblies, method.ParameterTypes,
                    method.ResultType, method.Result, catalog.EvidenceSha256, pack.Id + "@" + pack.Version);
                methods = methods.AddUnique(method.DocumentationCommentId, definition);
            }
        }

        _methods = methods;
    }

    internal static CompilerSpecificationPackConfiguration ResolveConfiguration(
        IEnumerable<string>? enabledPacks)
    {
        var catalog = RelationalSpecPackCatalogData.Catalog;
        var selected = CanonicalizeSelection(enabledPacks, catalog);
        return new CompilerSpecificationPackConfiguration
        {
            SpecificationPackIds = selected,
            SpecificationPackCatalogVersion = catalog.Version,
            SpecificationPackCatalogSha256 = catalog.EvidenceSha256
        };
    }

    private static ImmutableArray<string> CanonicalizeSelection(
        IEnumerable<string>? enabledPacks,
        RelationalSpecPackCatalog catalog)
    {
        var values = ImmutableList<string>.Empty;
        var seen = ImmutableHashSet.Create<string>(StringComparer.Ordinal);
        foreach (var value in enabledPacks ?? [])
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                continue;
            }

            if (seen.Contains(normalized))
            {
                throw new InvalidOperationException(
                    "SharpProof specification-pack identifiers must be unique.");
            }

            seen = seen.Add(normalized);
            values = values.Add(normalized);
        }

        values = values.Sort(StringComparer.Ordinal);
        foreach (var packId in values)
        {
            if (!catalog.Packs.ContainsKey(packId))
            {
                throw new InvalidOperationException(
                    "Unknown SharpProof specification pack '" + packId + "'.");
            }
        }

        return values.ToImmutableArray();
    }

    internal TotalScalarCallModel? ResolveTotal(IMethodSymbol method)
    {
        if (_factory.Semantics != IrExecutionSemantics.Total || !TryResolve(method, out var definition))
        { return null; }
        return CompilerSpecificationMethodBinding.CreateTotal(_factory, method, definition);
    }

    private bool TryResolve(
        IMethodSymbol method,
        out CompilerSpecificationMethodEvidence definition)
    {
        method = SemanticClaimIdentity.NormalizeCandidate(ArgumentNullGuard.NotNull(method, nameof(method))).OriginalDefinition;
        if (_resolved.TryGetValue(method, out var cached))
        {
            definition = cached!;
            return cached != null;
        }

        var identity = method.GetDocumentationCommentId();
        var resolved = identity != null && _methods.TryGetValue(identity, out var candidate)
            ? CompilerSpecificationMethodBinding.Admit(method, candidate)
            : null;
        definition = resolved!;
        _resolved = _resolved.SetItem(method, resolved);
        return resolved != null;
    }
}
