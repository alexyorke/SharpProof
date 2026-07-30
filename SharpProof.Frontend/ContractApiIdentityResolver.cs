using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Cryptography;

namespace SharpProof.Frontend;

internal sealed class ContractApiIdentityResolver
{
    private const string AttributesAssemblyName = "SharpProof.Attributes";
    private static readonly ImmutableArray<byte>
        AttributesAssemblyPayloadSha256 =
            ReadExpectedPayloadSha256();
    private static readonly Version AttributesAssemblyVersion =
        typeof(ContractApiIdentityResolver).Assembly.GetName().Version ??
        throw new InvalidOperationException(
            "SharpProof.Frontend has no assembly version.");
    private static readonly ImmutableArray<byte> AttributesAssemblyPublicKey =
        [.. typeof(ContractApiIdentityResolver).Assembly
            .GetName()
            .GetPublicKey() ?? []];
    private static readonly ConditionalWeakTable<
        Compilation, ContractApiIdentityResolver> Cache = new();
    private static readonly ImmutableHashSet<string> AttributeMetadataNames =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            ContractApiMetadata.ContractFor,
            ContractApiMetadata.EnforcePure,
            ContractApiMetadata.ZeroAllocations,
            ContractApiMetadata.AllowedCapabilities,
            ContractApiMetadata.DoesNotThrow,
            ContractApiMetadata.AllowedExceptions,
            ContractApiMetadata.EffectContract,
            ContractApiMetadata.NotNull,
            ContractApiMetadata.Positive,
            ContractApiMetadata.InRange,
            ContractApiMetadata.Suppress,
            ContractApiMetadata.Trusted);
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _attribute;
    private readonly INamedTypeSymbol? _conditionalAttribute;
    private readonly ConcurrentDictionary<string, AttributeResolution> _attributes =
        new(StringComparer.Ordinal);

    private ContractApiIdentityResolver(Compilation compilation)
    {
        _compilation = compilation ??
            throw new ArgumentNullException(nameof(compilation));
        _attribute = compilation.GetTypeByMetadataName(
            ContractApiMetadata.Attribute);
        _conditionalAttribute = compilation.GetTypeByMetadataName(
            ContractApiMetadata.ConditionalAttribute);
        var candidate = compilation.GetTypeByMetadataName(
            ContractApiMetadata.Contract);
        Contract = IsTrustedReferenceType(
                candidate,
                ContractApiMetadata.Contract) &&
            HasTrustedAttributesPayload(
                candidate!.ContainingAssembly) &&
            HasValidContractShape(candidate!)
                ? candidate
                : null;
    }

    internal INamedTypeSymbol? Contract
    {
        get;
    }

    internal bool IsResolvedContractAssembly(IAssemblySymbol assembly)
    {
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
            compilation ?? throw new ArgumentNullException(nameof(compilation)),
            static value => new(value));
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

        return _attributes.GetOrAdd(
            metadataName,
            ResolveAttributeCore).Symbol;
    }

    internal bool TryGetRejectedAttributeMetadataName(
        AttributeData attribute,
        out string metadataName)
    {
        var type = attribute.AttributeClass?.OriginalDefinition;
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
        var definition = method.OriginalDefinition;
        return definition.Name is
            ContractApiMetadata.RequiresMethodName or
            ContractApiMetadata.EnsuresMethodName or
            ContractApiMetadata.AssumeMethodName &&
            HasMetadataName(
                definition.ContainingType,
                ContractApiMetadata.Contract) &&
            (Contract == null ||
             !SymbolEqualityComparer.Default.Equals(
                 definition.ContainingType,
                 Contract));
    }

    private AttributeResolution ResolveAttributeCore(string metadataName)
    {
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
            !HasMetadataName(candidate, metadataName) ||
            candidate.Locations.Any(static location => location.IsInSource) ||
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
            identity.Version == AttributesAssemblyVersion &&
            string.IsNullOrEmpty(identity.CultureName) &&
            identity.PublicKey.SequenceEqual(
                AttributesAssemblyPublicKey);
    }

    private bool IsCompilationReference(
        IAssemblySymbol assembly)
    {
        return _compilation.References.Any(reference =>
            reference is CompilationReference &&
            _compilation.GetAssemblyOrModuleSymbol(reference) is
                IAssemblySymbol referenced &&
            SymbolEqualityComparer.Default.Equals(
                assembly,
                referenced));
    }

    private bool HasTrustedAttributesPayload(
        IAssemblySymbol assembly)
    {
        if (AttributesAssemblyPayloadSha256.IsDefaultOrEmpty)
        {
            return false;
        }

        var matches = _compilation.References
            .Where(reference =>
                SymbolEqualityComparer.Default.Equals(
                    assembly,
                    _compilation.GetAssemblyOrModuleSymbol(
                        reference)))
            .ToImmutableArray();
        if (matches.Length != 1 ||
            matches[0] is not
                PortableExecutableReference matched ||
            string.IsNullOrEmpty(matched.FilePath))
        {
            return false;
        }

        return matched.FilePath is { } path &&
            HasExpectedPayloadHash(path);
    }

    private static bool HasExpectedPayloadHash(string path)
    {
        try
        {
            using var stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var algorithm = SHA256.Create();
            return algorithm.ComputeHash(stream).SequenceEqual(
                AttributesAssemblyPayloadSha256);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static ImmutableArray<byte>
        ReadExpectedPayloadSha256()
    {
        var values = typeof(ContractApiIdentityResolver)
            .Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(static attribute => string.Equals(
                attribute.Key,
                ContractApiMetadata
                    .AttributesPayloadSha256MetadataKey,
                StringComparison.Ordinal))
            .Select(static attribute => attribute.Value)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        if (values.Length != 1 ||
            values[0] is not
            {
                Length: 64
            } value)
        {
            return [];
        }

        var result = ImmutableArray.CreateBuilder<byte>(32);
        for (var index = 0; index < value.Length; index += 2)
        {
            if (!byte.TryParse(
                    value.Substring(index, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return [];
            }

            result.Add(parsed);
        }

        return result.MoveToImmutable();
    }

    private bool IsAttribute(INamedTypeSymbol candidate)
    {
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

    private bool HasValidContractShape(INamedTypeSymbol contract)
    {
        return contract is
        {
            TypeKind: TypeKind.Class,
            IsStatic: true,
            Arity: 0
        } &&
        HasConditionalSymbol(contract) &&
        HasSingleClause(contract, ContractApiMetadata.RequiresMethodName) &&
        HasSingleClause(contract, ContractApiMetadata.EnsuresMethodName) &&
        HasSingleClause(contract, ContractApiMetadata.AssumeMethodName) &&
        HasSingleResult(contract) &&
        HasSingleOld(contract);
    }

    private bool HasSingleClause(
        INamedTypeSymbol contract,
        string name)
    {
        var members = contract.GetMembers(name).OfType<IMethodSymbol>()
            .ToImmutableArray();
        return members.Length == 1 &&
            members[0] is
            {
                MethodKind: MethodKind.Ordinary,
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: true,
                Arity: 0,
                ReturnsVoid: true
            } method &&
            method.Parameters.Length == 1 &&
            method.Parameters[0] is
            {
                RefKind: RefKind.None,
                ScopedKind: ScopedKind.None,
                IsParams: false,
                IsOptional: false,
                Type.SpecialType: SpecialType.System_Boolean
            } &&
            HasOnlyElidingConditionalAttribute(method);
    }

    private bool HasOnlyElidingConditionalAttribute(IMethodSymbol method)
    {
        if (_conditionalAttribute == null)
        {
            return false;
        }

        var attributes = method.GetAttributes()
            .Where(attribute => HasMetadataName(
                attribute.AttributeClass,
                ContractApiMetadata.ConditionalAttribute))
            .ToImmutableArray();
        return attributes.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(
                attributes[0].AttributeClass?.OriginalDefinition,
                _conditionalAttribute.OriginalDefinition) &&
            attributes[0].ConstructorArguments.Length == 1 &&
            attributes[0].ConstructorArguments[0] is
            {
                Kind: TypedConstantKind.Primitive,
                Value: ContractApiMetadata.ConditionalSymbol
            };
    }

    private static bool HasConditionalSymbol(INamedTypeSymbol contract)
    {
        var members = contract.GetMembers("ConditionalSymbol");
        return members.Length == 1 &&
            members[0] is IFieldSymbol
            {
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: true,
                IsConst: true,
                Type.SpecialType: SpecialType.System_String,
                ConstantValue: ContractApiMetadata.ConditionalSymbol
            };
    }

    private static bool HasSingleResult(INamedTypeSymbol contract)
    {
        var members = contract
            .GetMembers(ContractApiMetadata.ResultMethodName)
            .OfType<IMethodSymbol>()
            .ToImmutableArray();
        return members.Length == 1 &&
            members[0] is
            {
                MethodKind: MethodKind.Ordinary,
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: true,
                Arity: 1,
                Parameters.Length: 0,
                ReturnsByRef: false,
                ReturnsByRefReadonly: false
            } method &&
            HasUnconstrainedTypeParameter(method.TypeParameters[0]) &&
            SymbolEqualityComparer.Default.Equals(
                method.ReturnType,
                method.TypeParameters[0]);
    }

    private static bool HasSingleOld(INamedTypeSymbol contract)
    {
        var members = contract
            .GetMembers(ContractApiMetadata.OldMethodName)
            .OfType<IMethodSymbol>()
            .ToImmutableArray();
        return members.Length == 1 &&
            members[0] is
            {
                MethodKind: MethodKind.Ordinary,
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: true,
                Arity: 1,
                ReturnsByRef: false,
                ReturnsByRefReadonly: false
            } method &&
            method.Parameters.Length == 1 &&
            method.Parameters[0] is
            {
                RefKind: RefKind.None,
                ScopedKind: ScopedKind.None,
                IsParams: false,
                IsOptional: false
            } parameter &&
            HasUnconstrainedTypeParameter(method.TypeParameters[0]) &&
            SymbolEqualityComparer.Default.Equals(
                method.ReturnType,
                method.TypeParameters[0]) &&
            SymbolEqualityComparer.Default.Equals(
                parameter.Type,
                method.TypeParameters[0]);
    }

    private static bool HasUnconstrainedTypeParameter(
        ITypeParameterSymbol parameter)
    {
        return !parameter.HasConstructorConstraint &&
            !parameter.HasReferenceTypeConstraint &&
            !parameter.HasValueTypeConstraint &&
            !parameter.HasNotNullConstraint &&
            !parameter.HasUnmanagedTypeConstraint &&
            parameter.ConstraintTypes.IsDefaultOrEmpty;
    }

    private static bool InheritsFrom(
        INamedTypeSymbol candidate,
        INamedTypeSymbol expectedBase)
    {
        for (var current = candidate.BaseType;
             current != null;
             current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    expectedBase.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetKnownAttributeMetadataName(
        INamedTypeSymbol type,
        out string metadataName)
    {
        foreach (var candidate in AttributeMetadataNames)
        {
            if (HasMetadataName(type, candidate))
            {
                metadataName = candidate;
                return true;
            }
        }

        metadataName = string.Empty;
        return false;
    }

    private static bool HasMetadataName(
        INamedTypeSymbol? type,
        string metadataName)
    {
        if (type == null || type.ContainingType != null)
        {
            return false;
        }

        var separator = metadataName.LastIndexOf('.');
        return separator > 0 &&
            string.Equals(
                type.MetadataName,
                metadataName.Substring(separator + 1),
                StringComparison.Ordinal) &&
            NamespaceMatches(
                type.ContainingNamespace,
                metadataName.Substring(0, separator));
    }

    private static bool NamespaceMatches(
        INamespaceSymbol @namespace,
        string expected)
    {
        var segments = expected.Split('.');
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            if (@namespace.IsGlobalNamespace ||
                !string.Equals(
                    @namespace.Name,
                    segments[index],
                    StringComparison.Ordinal))
            {
                return false;
            }

            @namespace = @namespace.ContainingNamespace;
        }

        return @namespace.IsGlobalNamespace;
    }

    private sealed class AttributeResolution(INamedTypeSymbol? symbol)
    {
        internal INamedTypeSymbol? Symbol { get; } = symbol;
    }
}
