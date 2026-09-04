using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpProof.Roslyn;

namespace SharpProof.Frontend;

internal sealed class ContractApiIdentityResolver
{
    private const string AttributesAssemblyName =
        ContractApiMetadata.AttributesNamespace;
    private const string AttributesAssemblyMvidMetadataKey =
        ContractApiMetadata.AttributesAssemblyMvidMetadataKey;
    private static readonly ImmutableArray<byte>
        AttributesAssemblyPayloadSha256 =
            ReadExpectedPayloadSha256();
    private static readonly Guid AttributesAssemblyModuleVersionId =
        ReadExpectedModuleVersionId();
    private static readonly Version AttributesAssemblyVersion =
        typeof(ContractApiIdentityResolver).Assembly.GetName().Version ??
        throw new InvalidOperationException(
            "SharpProof.Frontend has no assembly version.");
    private static readonly ConditionalWeakTable<
        Compilation, ContractApiIdentityResolver> Cache = new();
    private static readonly ImmutableHashSet<string> AttributeMetadataNames =
        ImmutableHashSet.CreateRange(
            StringComparer.Ordinal,
            ContractApiMetadata.AttributeMetadataNames);
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _attribute;
    private readonly INamedTypeSymbol? _conditionalAttribute;
    private readonly ConcurrentDictionary<IAssemblySymbol, bool>
        _compilationReferenceCache =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<string, AttributeResolution> _attributes =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MetadataNameParts>
        _metadataNames = new(StringComparer.Ordinal);

    private ContractApiIdentityResolver(Compilation compilation)
    {
        _compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
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
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)),
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
            string.IsNullOrEmpty(identity.CultureName);
    }

    private bool IsCompilationReference(
        IAssemblySymbol assembly)
    {
        return _compilationReferenceCache.GetOrAdd(
            assembly,
            candidate => _compilation.References.Any(reference =>
                reference is CompilationReference &&
                _compilation.GetAssemblyOrModuleSymbol(reference) is
                    IAssemblySymbol referenced &&
                SymbolEqualityComparer.Default.Equals(
                    candidate,
                    referenced)));
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
        if (matches.IsDefaultOrEmpty ||
            matches.Any(static reference => reference is not PortableExecutableReference))
        {
            return false;
        }

        var trusted = true;
        string? unreadableReason = null;
        foreach (var match in matches.Cast<PortableExecutableReference>())
        {
            var current = match.FilePath is { Length: > 0 } path
                ? HasExpectedPayloadHash(path, match, out var currentReason)
                : HasExpectedModuleVersionId(match, out currentReason);
            trusted &= current;
            unreadableReason ??= currentReason;
        }

        if (unreadableReason != null)
        {
            UnreadableContractApiReason = unreadableReason;
        }

        return trusted;
    }

    /// <summary>
    /// Distinguishes a payload that does not match from a payload that could not
    /// be read at all. Both disable contract analysis, but only the second is an
    /// environment fault the user can act on, and reporting it is the difference
    /// between an explained failure and the analyzer silently doing nothing.
    /// </summary>
    private static bool HasExpectedPayloadHash(string path, PortableExecutableReference reference, out string? unreadableReason)
    {
        unreadableReason = null;
        try
        {
            using var stream = File.Open(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!reader.HasMetadata)
            {
                return false;
            }
            var metadataReader = reader.GetMetadataReader();
            if (metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid) !=
                GetBoundModuleVersionId(reference))
            {
                return false;
            }
            stream.Position = 0;
            using var algorithm = SHA256.Create();
            return algorithm.ComputeHash(stream).SequenceEqual(
                AttributesAssemblyPayloadSha256);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                IOException or
                NotSupportedException or
                UnauthorizedAccessException or
                SecurityException or
                CryptographicException)
        {
            unreadableReason = exception.Message;
            return false;
        }
    }

    private static Guid GetBoundModuleVersionId(PortableExecutableReference reference)
    {
        return reference.GetMetadata() is AssemblyMetadata metadata &&
            metadata.GetModules() is { Length: 1 } modules
                ? modules[0].GetModuleVersionId()
                : Guid.Empty;
    }

    private static bool HasExpectedModuleVersionId(
        PortableExecutableReference reference,
        out string? unreadableReason)
    {
        unreadableReason = null;
        try
        {
            if (AttributesAssemblyModuleVersionId == Guid.Empty ||
                reference.GetMetadata() is not AssemblyMetadata metadata)
            {
                return false;
            }

            var modules = metadata.GetModules();
            return modules.Length == 1 &&
                modules[0].GetModuleVersionId() ==
                    AttributesAssemblyModuleVersionId;
        }
        catch (Exception exception) when (
            exception is BadImageFormatException or
                IOException or
                InvalidOperationException or
                NotSupportedException)
        {
            unreadableReason = exception.Message;
            return false;
        }
    }

    private static ImmutableArray<byte>
        ReadExpectedPayloadSha256()
    {
        var values = ReadAssemblyMetadataValues(
            ContractApiMetadata.AttributesPayloadSha256MetadataKey);
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

    private static Guid ReadExpectedModuleVersionId()
    {
        var values = ReadAssemblyMetadataValues(
            AttributesAssemblyMvidMetadataKey);
        return values.Length == 1 &&
            Guid.TryParseExact(values[0], "D", out var result)
                ? result
                : Guid.Empty;
    }

    private static ImmutableArray<string> ReadAssemblyMetadataValues(
        string key)
    {
        return typeof(ContractApiIdentityResolver)
            .Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => string.Equals(
                attribute.Key,
                key,
                StringComparison.Ordinal))
            .Select(static attribute => attribute.Value)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
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
        HasSingleGenericIdentityMethod(
            contract,
            ContractApiMetadata.ResultMethodName,
            parameterCount: 0) &&
        HasSingleGenericIdentityMethod(
            contract,
            ContractApiMetadata.OldMethodName,
            parameterCount: 1);
    }

    private bool HasSingleClause(
        INamedTypeSymbol contract,
        string name)
    {
        return GetSingleMethod(contract, name) is
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

        AttributeData? attribute = null;
        var count = 0;
        foreach (var candidate in method.GetAttributes())
        {
            if (!HasMetadataName(
                    candidate.AttributeClass,
                    ContractApiMetadata.ConditionalAttribute))
            {
                continue;
            }

            count++;
            attribute ??= candidate;
        }

        return count == 1 &&
            SymbolEqualityComparer.Default.Equals(
                attribute!.AttributeClass?.OriginalDefinition,
                _conditionalAttribute.OriginalDefinition) &&
            attribute.ConstructorArguments.Length == 1 &&
            attribute.ConstructorArguments[0] is
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

    private static bool HasSingleGenericIdentityMethod(
        INamedTypeSymbol contract,
        string name,
        int parameterCount)
    {
        return GetSingleMethod(contract, name) is
        {
            MethodKind: MethodKind.Ordinary,
            DeclaredAccessibility: Accessibility.Public,
            IsStatic: true,
            Arity: 1,
            ReturnsByRef: false,
            ReturnsByRefReadonly: false
        } method &&
            method.Parameters.Length == parameterCount &&
            HasUnconstrainedTypeParameter(method.TypeParameters[0]) &&
            SymbolEqualityComparer.Default.Equals(
                method.ReturnType,
                method.TypeParameters[0]) &&
            (parameterCount == 0 || method.Parameters[0] is
            {
                RefKind: RefKind.None,
                ScopedKind: ScopedKind.None,
                IsParams: false,
                IsOptional: false
            } parameter &&
            SymbolEqualityComparer.Default.Equals(
                parameter.Type,
                method.TypeParameters[0]));
    }

    private static IMethodSymbol? GetSingleMethod(
        INamedTypeSymbol contract,
        string name)
    {
        IMethodSymbol? member = null;
        var count = 0;
        foreach (var candidate in contract.GetMembers(name))
        {
            if (candidate is not IMethodSymbol candidateMethod)
            {
                continue;
            }

            count++;
            if (count > 1)
            {
                return null;
            }
            member = candidateMethod;
        }
        return member;
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
        return RoslynSymbolFacts.IsOrDerivesFrom(
            candidate,
            expectedBase,
            includeSelf: false);
    }

    private bool TryGetKnownAttributeMetadataName(
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

    private bool HasMetadataName(
        INamedTypeSymbol? type,
        string metadataName)
    {
        if (type == null || type.ContainingType != null)
        {
            return false;
        }

        var parts = _metadataNames.GetOrAdd(
            metadataName,
            static value => new MetadataNameParts(value));
        return parts.IsValid &&
            string.Equals(
                type.MetadataName,
                parts.TypeName,
                StringComparison.Ordinal) &&
            NamespaceMatches(
                type.ContainingNamespace,
                parts.NamespaceSegments);
    }

    private static bool NamespaceMatches(
        INamespaceSymbol @namespace,
        string[] segments)
    {
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

    private sealed class MetadataNameParts
    {
        internal MetadataNameParts(string metadataName)
        {
            var separator = metadataName.LastIndexOf('.');
            if (separator <= 0)
            {
                IsValid = false;
                TypeName = string.Empty;
                NamespaceSegments = [];
                return;
            }

            IsValid = true;
            TypeName = metadataName.Substring(separator + 1);
            NamespaceSegments = metadataName
                .Substring(0, separator)
                .Split('.');
        }

        internal bool IsValid
        {
            get;
        }

        internal string TypeName
        {
            get;
        }

        internal string[] NamespaceSegments
        {
            get;
        }
    }

    private sealed class AttributeResolution(INamedTypeSymbol? symbol)
    {
        internal INamedTypeSymbol? Symbol { get; } = symbol;
    }
}
