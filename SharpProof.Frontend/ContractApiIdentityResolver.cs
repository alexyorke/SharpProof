using System.Runtime.CompilerServices;
using SharpProof.Roslyn;

namespace SharpProof.Frontend;

internal sealed class ContractApiIdentityResolver
{
    private const string AttributesAssemblyName =
        ContractApiMetadata.AttributesNamespace;
    private static readonly ContractApiPayloadPolicy PayloadPolicy =
        ContractApiPayloadPolicy.FromRuntimeOwner(typeof(ContractApiIdentityResolver).Assembly);
    private static readonly ConditionalWeakTable<
        Compilation, ContractApiIdentityResolver> Cache = new();
    private static readonly ImmutableHashSet<string> AttributeMetadataNames =
        ImmutableHashSet.CreateRange(
            StringComparer.Ordinal,
            ContractApiMetadata.AttributeMetadataNames);
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _attribute;
    private readonly INamedTypeSymbol? _conditionalAttribute;
    private ImmutableDictionary<IAssemblySymbol, bool> _compilationReferenceCache =
        ImmutableDictionary.Create<IAssemblySymbol, bool>(SymbolEqualityComparer.Default);
    private ImmutableDictionary<string, AttributeResolution> _attributes =
        ImmutableDictionary.Create<string, AttributeResolution>(StringComparer.Ordinal);
    private ImmutableDictionary<INamedTypeSymbol, string> _knownAttributeMetadataNames =
        ImmutableDictionary.Create<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
    private readonly ContractApiMetadataNameMatcher _metadataNames = new();

    private ContractApiIdentityResolver(Compilation compilation)
    {
        _compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        _attribute = compilation.GetTypeByMetadataName(
            ContractApiMetadata.Attribute);
        _conditionalAttribute = compilation.GetTypeByMetadataName(
            ContractApiMetadata.ConditionalAttribute);
        var candidate = compilation.GetTypeByMetadataName(
            ContractApiMetadata.Contract);
        var shape = IsTrustedReferenceType(
                candidate,
                ContractApiMetadata.Contract) &&
            HasTrustedAttributesPayload(
                candidate!.ContainingAssembly)
                ? new ContractApiShapeValidator(_conditionalAttribute, _metadataNames).TryResolve(candidate!)
                : null;
        Contract = shape is not null
                ? candidate
                : null;
        Result = shape?.Result;
        Old = shape?.Old;
    }

    internal INamedTypeSymbol? Contract
    {
        get;
    }

    internal IMethodSymbol? Result
    {
        get;
    }

    internal IMethodSymbol? Old
    {
        get;
    }

    internal bool IsResolvedContractAssembly(IAssemblySymbol assembly)
    {
        assembly = RepositoryParameterGuard.NotNull(assembly, nameof(assembly));
        return assembly != null &&
            Contract is { } contract &&
            SymbolEqualityComparer.Default.Equals(
                contract.ContainingAssembly,
                assembly);
    }

    internal static ContractApiIdentityResolver ForCompilation(
        Compilation compilation)
    {
        return Cache.GetValue(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)),
            static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (new(value)));
    }

    internal INamedTypeSymbol? ResolveAttribute(string metadataName)
    {
        if (!AttributeMetadataNames.Contains(metadataName))
        {
            throw new ArgumentOutOfRangeException(
                nameof(metadataName),
                metadataName,
                "Unknown SharpProof contract attribute.");
        }

        return ImmutableInterlocked.GetOrAdd(ref _attributes,
            metadataName,
            ResolveAttributeCore).Symbol;
    }

    internal bool TryGetRejectedAttributeMetadataName(
        AttributeData attribute,
        out string metadataName)
    {
        var type = ArgumentNullGuard.NotNull(attribute, nameof(attribute))
            .AttributeClass?.OriginalDefinition;
        if (type == null ||
            !TryGetKnownAttributeMetadataName(type, out metadataName) ||
            SymbolEqualityComparer.Default.Equals(
                type,
                ResolveAttribute(metadataName)?.OriginalDefinition))
        {
            metadataName = string.Empty;
            return false;
        }

        return true;
    }

    internal bool IsRejectedClauseMethod(IMethodSymbol method)
    {
        var definition = ArgumentNullGuard.NotNull(method, nameof(method))
            .OriginalDefinition;
        return definition.Name is
            ContractApiMetadata.RequiresMethodName or
            ContractApiMetadata.EnsuresMethodName or
            ContractApiMetadata.AssumeMethodName &&
            _metadataNames.Matches(
                definition.ContainingType,
                ContractApiMetadata.Contract) &&
            (Contract == null ||
             !SymbolEqualityComparer.Default.Equals(
                 definition.ContainingType,
                 Contract));
    }

    private AttributeResolution ResolveAttributeCore(string metadataName)
    {
        metadataName = RepositoryParameterGuard.NotNull(metadataName, nameof(metadataName));
        var candidate = _compilation.GetTypeByMetadataName(metadataName);
        return new AttributeResolution(
            Contract is { } contract &&
            IsTrustedReferenceType(candidate, metadataName) &&
            SymbolEqualityComparer.Default.Equals(
                candidate!.ContainingAssembly,
                contract.ContainingAssembly) &&
            IsAttribute(candidate!)
                ? candidate
                : null);
    }

    private bool IsTrustedReferenceType(
        INamedTypeSymbol? candidate,
        string metadataName)
    {
        if (candidate == null ||
            !_metadataNames.Matches(candidate, metadataName) ||
            candidate.Locations.Any(static location => location is null ? throw new System.ArgumentNullException(nameof(location)) : (location.IsInSource)) ||
            IsCompilationReference(candidate.ContainingAssembly))
        {
            return false;
        }

        var identity = candidate.ContainingAssembly?.Identity;
        return identity != null &&
            string.Equals(
                identity.Name,
                AttributesAssemblyName,
                StringComparison.Ordinal) &&
            identity.Version == PayloadPolicy.ExpectedAssemblyVersion &&
            string.IsNullOrEmpty(identity.CultureName);
    }

    private bool IsCompilationReference(
        IAssemblySymbol assembly)
    {
        assembly = RepositoryParameterGuard.NotNull(assembly, nameof(assembly));
        return assembly is not null &&
            ImmutableInterlocked.GetOrAdd(ref _compilationReferenceCache,
            assembly,
            candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (_compilation.References.Any(reference =>
                reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference is CompilationReference &&
                _compilation.GetAssemblyOrModuleSymbol(reference) is
                    IAssemblySymbol referenced &&
                SymbolEqualityComparer.Default.Equals(
                    candidate,
                    referenced)))));
    }

    /// <summary>
    /// Set when the contract API assembly was located but could not be read, as
    /// opposed to being absent or not matching. Surfaced as SP0050 so the
    /// analyzer does not silently disable every contract.
    /// </summary>
    internal string? UnreadableContractApiReason
    {
        get;
        private set;
    }

    private bool HasTrustedAttributesPayload(IAssemblySymbol assembly)
    {
        assembly = RepositoryParameterGuard.NotNull(assembly, nameof(assembly));
        var trusted = PayloadPolicy.HasTrustedPayload(_compilation, assembly, out var unreadableReason);
        if (unreadableReason != null)
        {
            UnreadableContractApiReason = unreadableReason;
        }
        return trusted;
    }

    private bool IsAttribute(INamedTypeSymbol candidate)
    {
        candidate = RepositoryParameterGuard.NotNull(candidate, nameof(candidate));
        return candidate is
        {
            TypeKind: TypeKind.Class,
            IsSealed: true,
            IsAbstract: false,
            Arity: 0
        } &&
        _attribute != null &&
        InheritsFrom(candidate, _attribute);
    }

    private static bool InheritsFrom(
        INamedTypeSymbol candidate,
        INamedTypeSymbol expectedBase)
    {
        candidate = RepositoryParameterGuard.NotNull(candidate, nameof(candidate));
        expectedBase = RepositoryParameterGuard.NotNull(expectedBase, nameof(expectedBase));
        return RoslynSymbolFacts.IsOrDerivesFrom(
            candidate,
            expectedBase,
            includeSelf: false);
    }

    private bool TryGetKnownAttributeMetadataName(
        INamedTypeSymbol type,
        out string metadataName)
    {
        if (Volatile.Read(ref _knownAttributeMetadataNames).TryGetValue(type, out metadataName!))
        {
            return metadataName.Length != 0;
        }

        foreach (var candidate in AttributeMetadataNames)
        {
            if (_metadataNames.Matches(type, candidate))
            {
                metadataName = candidate;
                ImmutableInterlocked.TryAdd(ref _knownAttributeMetadataNames, type, metadataName);
                return true;
            }
        }

        metadataName = string.Empty;
        ImmutableInterlocked.TryAdd(ref _knownAttributeMetadataNames, type, metadataName);
        return false;
    }

    private sealed class AttributeResolution(INamedTypeSymbol? symbol)
    {
        internal INamedTypeSymbol? Symbol { get; } = symbol;
    }
}
