// Specification packs are audited package data. They are never discovered
// from the consumer's filesystem and are inactive unless explicitly selected.
namespace SharpProof.CompilerArtifact;

internal sealed class CompilerSpecificationPackProvider
{
    private readonly IrFactory _factory;
    private readonly ImmutableDictionary<string, RelationalSpecPackMethod> _methods;
    private readonly Dictionary<IMethodSymbol, RelationalSpecPackMethod?> _resolved =
        new(SymbolEqualityComparer.Default);

    internal CompilerSpecificationPackProvider(
        IrFactory factory,
        IEnumerable<string>? enabledPacks)
        : this(factory, ResolveConfiguration(enabledPacks))
    {
    }

    internal CompilerSpecificationPackProvider(
        IrFactory factory,
        CompilerSpecificationPackConfiguration authority)
    {
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        authority = ArgumentNullGuard.NotNull(authority, nameof(authority));
        var catalog = RelationalSpecPackCatalogData.Catalog;
        if (!CompilerSpecificationPackSelection.IsValid(
                authority.SpecificationPackIds,
                authority.SpecificationPackCatalogVersion,
                authority.SpecificationPackCatalogSha256) ||
            authority.SpecificationPackCatalogVersion != catalog.Version ||
            authority.SpecificationPackCatalogSha256 != catalog.EvidenceSha256)
        {
            throw new InvalidOperationException(
                "The SharpProof specification-pack authority is not current.");
        }

        var methods = ImmutableDictionary.CreateBuilder<
            string,
            RelationalSpecPackMethod>(StringComparer.Ordinal);
        foreach (var packId in authority.SpecificationPackIds)
        {
            if (!catalog.Packs.TryGetValue(packId, out var pack))
            {
                throw new InvalidOperationException(
                    "Unknown SharpProof specification pack '" + packId + "'.");
            }

            foreach (var method in pack.Methods)
            {
                var definition = method with
                {
                    EvidenceSha256 = catalog.EvidenceSha256,
                    EvidenceIdentity = pack.Id + "@" + pack.Version
                };
                try
                {
                    methods.Add(method.DocumentationCommentId, definition);
                }
                catch (ArgumentException exception)
                    when (exception is not ArgumentNullException)
                {
                    throw new InvalidOperationException(
                        "Enabled SharpProof specification packs overlap at '" +
                        method.DocumentationCommentId + "'.",
                        exception);
                }
            }
        }

        _methods = methods.ToImmutable();
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

    private static string[] CanonicalizeSelection(
        IEnumerable<string>? enabledPacks,
        RelationalSpecPackCatalog catalog)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in enabledPacks ?? [])
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0)
            {
                continue;
            }

            if (!seen.Add(normalized))
            {
                throw new InvalidOperationException(
                    "SharpProof specification-pack identifiers must be unique.");
            }

            values.Add(normalized);
        }

        values.Sort(StringComparer.Ordinal);
        foreach (var packId in values)
        {
            if (!catalog.Packs.ContainsKey(packId))
            {
                throw new InvalidOperationException(
                    "Unknown SharpProof specification pack '" + packId + "'.");
            }
        }

        return values.ToArray();
    }

    internal bool CanResolve(IMethodSymbol method)
    {
        return TryResolve(method, out _);
    }

    internal TotalScalarCallModel? ResolveTotal(IMethodSymbol method)
    {
        if (_factory.Semantics != IrExecutionSemantics.Total || !TryResolve(method, out var definition))
        { return null; }
        if (!CompilerSpecificationScalarDomain.TryMapSignature(_factory, method,
                out var parameters, out var resultType, out var integerType))
        { return null; }
        return new(parameters.Length, arguments =>
        {
            try
            {
                if (arguments.Length != parameters.Length || arguments.Where((argument, ordinal) => argument.Type != parameters[ordinal]).Any())
                { throw new ArgumentException("A specification-pack argument has an invalid type."); }
                var result = CompilerTotalSpecTermInstantiator.Instantiate(_factory, definition.Result, arguments, integerType, 0);
                if (result.Type != resultType)
                { throw new ArgumentException("A specification-pack result has an invalid type."); }
                return new TotalScalarRule(result, [], FrontendSubsetClassification.Exact);
            }
            catch (ArgumentException)
            {
                return new TotalScalarRule(_factory.Boolean(false), [],
                    FrontendSubsetClassification.Abstain(FrontendAbstention.UnsupportedInvocationShape));
            }
        });
    }

    private bool TryResolve(
        IMethodSymbol method,
        out RelationalSpecPackMethod definition)
    {
        method = SemanticClaimIdentity.NormalizeCandidate(method).OriginalDefinition;
        if (_resolved.TryGetValue(method, out var cached))
        {
            definition = cached!;
            return cached != null;
        }

        var identity = method.GetDocumentationCommentId();
        if (identity == null ||
            !_methods.TryGetValue(identity, out var resolved) ||
            resolved == null ||
            method.MethodKind != MethodKind.Ordinary ||
            !method.IsStatic ||
            method.TypeParameters.Length != 0 ||
            method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            method.Parameters.Length != resolved.ParameterTypes.Length ||
            !MatchesAssembly(method.ContainingAssembly, resolved.Assemblies) ||
            !CompilerSpecificationScalarDomain.MatchesType(method.ReturnType, resolved.ResultType))
        {
            definition = null!;
            _resolved[method] = null;
            return false;
        }

        for (var index = 0; index < method.Parameters.Length; index++)
        {
            if (!CompilerSpecificationScalarDomain.MatchesType(
                    method.Parameters[index].Type,
                    resolved.ParameterTypes[index]))
            {
                definition = null!;
                _resolved[method] = null;
                return false;
            }
        }

        definition = resolved;
        _resolved[method] = definition;
        return true;
    }

    private static bool MatchesAssembly(
        IAssemblySymbol assembly,
        ImmutableArray<ApiSpecAssemblyIdentity> approved)
    {
        var identity = assembly.Identity;
        var token = identity.PublicKeyToken.IsDefaultOrEmpty
            ? string.Empty
            : HashEncoding.ToLowerHex(identity.PublicKeyToken);
        return approved.Any(candidate =>
            candidate.Name == identity.Name &&
            candidate.PublicKeyToken == token);
    }
}
