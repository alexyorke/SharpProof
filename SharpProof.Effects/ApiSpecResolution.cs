using System.Runtime.CompilerServices;
using System.Globalization;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using SharpProof.Effects;
using SharpProof.Ir;
namespace SharpProof.Specs;
public enum ApiSpecResolutionFailureKind
{
    MissingContainingType,
    AmbiguousContainingType,
    UnapprovedContainingAssembly,
    UnapprovedReferenceFamily,
    MissingMember,
    AmbiguousMember,
    DuplicateResolvedSymbol
}
public enum ApiSpecLookupStatus
{
    Resolved,
    Unknown
}
public enum ApiSpecLookupFailureKind
{
    UnspecifiedMember
}
public sealed class ResolvedApiSpecTable(ImmutableDictionary<ISymbol, ResolvedApiSpec> specs,
    ImmutableArray<ApiSpecResolutionFailure> failures)
{
    private readonly ImmutableDictionary<ISymbol, ResolvedApiSpec> _specs = specs;
    private readonly ImmutableArray<ResolvedApiSpec> _orderedSpecs =
        [.. specs.Values.OrderBy(static spec => spec.Template.Id.Value)];
    public ImmutableArray<ResolvedApiSpec> Specs => _orderedSpecs;
    public ImmutableArray<ApiSpecResolutionFailure> Failures { get; } = failures;
    public bool IsComplete => Failures.IsDefaultOrEmpty;
    public bool TryGet(
        ISymbol symbol,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ResolvedApiSpec? spec)
    {
        symbol = ArgumentNullGuard.NotNull(symbol, nameof(symbol));

        var normalized = NormalizeSymbol(symbol);
        spec = null;
        return normalized != null && _specs.TryGetValue(normalized, out spec);
    }
    public bool IsPureAndAllocationFree(IMethodSymbol method)
    {
        return TryGet(method, out var spec) &&
        spec.Template.Facets.Effects.Effects == SpecEffect.None &&
        spec.Template.Facets.Allocation.Behavior == SpecAllocationBehavior.None;
    }

    public bool IsSideEffectFree(IMethodSymbol method)
    {
        return TryGet(method, out var spec) &&
            spec.Template.Facets.Effects.Effects == SpecEffect.None;
    }

    internal bool IsNonThrowingAndTerminating(IMethodSymbol method)
    {
        return TryGet(method, out var spec) &&
            spec.Template.Facets.Throws.Behavior ==
                SpecThrowBehavior.DoesNotThrow &&
            spec.Template.Facets.Termination?.Behavior ==
                SpecTerminationBehavior.Terminates;
    }

    public ApiSpecLookupResult Lookup(ISymbol symbol)
    {
        symbol = ArgumentNullGuard.NotNull(symbol, nameof(symbol));

        if (TryGet(symbol, out var spec))
        {
            return new ApiSpecLookupResult(ApiSpecLookupStatus.Resolved, spec, null);
        }

        var identifier = symbol.GetDocumentationCommentId() ?? symbol.MetadataName;
        return new ApiSpecLookupResult(ApiSpecLookupStatus.Unknown, null, new ApiSpecLookupFailure(
            ApiSpecLookupFailureKind.UnspecifiedMember,
            identifier,
            "No resolved API spec exists for this original definition."));
    }
    internal static ISymbol? NormalizeSymbol(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => (method.ReducedFrom ?? method).OriginalDefinition,
            IPropertySymbol property => property.GetMethod?.OriginalDefinition,
            _ => symbol.OriginalDefinition
        };
    }
}
public sealed class ApiSpecResolver(ApiSpecTable table)
{
    private static readonly (string Marker, ApiSpecReferenceFamily Family)[] ReferenceFamilyMarkers =
        EffectContractMappingCatalog.ReferenceFamilyMarkers;
    private readonly ConditionalWeakTable<Compilation, ResolvedApiSpecTable> _cache = new();
    private readonly ApiSpecTable _table =
        ArgumentNullGuard.NotNull(table, nameof(table));
    public ResolvedApiSpecTable Resolve(Compilation compilation)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));

        return _cache.GetValue(compilation, Build);
    }
    private ResolvedApiSpecTable Build(Compilation compilation)
    {
        var failures = ImmutableArray.CreateBuilder<ApiSpecResolutionFailure>();
        var resolved = new List<(ApiSpecTemplate Template, ISymbol Symbol)>();
        foreach (var template in _table.Templates)
        {
            var candidate = ResolveTemplate(compilation, template);
            if (candidate.Failure == null)
            {
                resolved.Add((template, candidate.Symbol!));
            }
            else
            {
                failures.Add(candidate.Failure);
            }
        }
        var specs = ImmutableDictionary.CreateBuilder<ISymbol, ResolvedApiSpec>(SymbolEqualityComparer.Default);
        foreach (var group in resolved.GroupBy(
                     static candidate => candidate.Symbol, SymbolEqualityComparer.Default))
        {
            using var candidates = group.GetEnumerator();
            if (!candidates.MoveNext())
            {
                continue;
            }

            var first = candidates.Current;
            if (!candidates.MoveNext())
            {
                specs.Add(first.Symbol,
                    new ResolvedApiSpec(first.Template, first.Symbol));
                continue;
            }

            failures.Add(Failure(first.Template,
                ApiSpecResolutionFailureKind.DuplicateResolvedSymbol,
                "Multiple spec rows resolved to the same original symbol."));
            do
            {
                failures.Add(Failure(candidates.Current.Template,
                    ApiSpecResolutionFailureKind.DuplicateResolvedSymbol,
                    "Multiple spec rows resolved to the same original symbol."));
            }
            while (candidates.MoveNext());
        }
        return new ResolvedApiSpecTable(specs.ToImmutable(), failures.ToImmutable());
    }
    private static (ISymbol? Symbol, ApiSpecResolutionFailure? Failure) ResolveTemplate(
        Compilation compilation, ApiSpecTemplate template)
    {
        var target = template.Target;
        var containingType = compilation.GetTypeByMetadataName(target.ContainingTypeMetadataName);
        if (containingType == null)
        {
            var alternatives = compilation.GetTypesByMetadataName(target.ContainingTypeMetadataName);
            return alternatives.Length > 1
                ? Unresolved(
                    template,
                    ApiSpecResolutionFailureKind.AmbiguousContainingType,
                    "Multiple referenced assemblies define the containing metadata type.")
                : Unresolved(
                    template,
                    ApiSpecResolutionFailureKind.MissingContainingType,
                    "The containing metadata type is unavailable in this compilation.");
        }
        var assemblyMatch = MatchAssembly(
            compilation,
            containingType.ContainingAssembly,
            target);
        if (!assemblyMatch.IdentityApproved)
        {
            return Unresolved(
                template,
                ApiSpecResolutionFailureKind.UnapprovedContainingAssembly,
                "The containing type is defined by unapproved assembly identity '" +
                containingType.ContainingAssembly.Identity + "'.");
        }

        if (!assemblyMatch.FamilyApproved)
        {
            return Unresolved(
                template,
                ApiSpecResolutionFailureKind.UnapprovedReferenceFamily,
                "The containing type is loaded from unapproved reference family '" +
                assemblyMatch.ReferenceFamily + "' at '" +
                assemblyMatch.ReferencePath + "'.");
        }

        var normalized = DocumentationCommentId.GetSymbolsForDeclarationId(
                target.DocumentationCommentId, compilation)
            .Where(candidate => SymbolEqualityComparer.Default.Equals(
                    candidate.ContainingType?.OriginalDefinition,
                    containingType.OriginalDefinition) &&
                MatchesTarget(candidate, target))
            .Select(ResolvedApiSpecTable.NormalizeSymbol)
            .Where(static symbol => symbol != null)
            .Select(static symbol => symbol!)
            .ToImmutableHashSet<ISymbol>(SymbolEqualityComparer.Default);
        return normalized.Count switch
        {
            0 => Unresolved(
                template,
                ApiSpecResolutionFailureKind.MissingMember,
                "The documentation identifier did not resolve to the declared member shape."),
            1 => (normalized.Single(), null),
            _ => Unresolved(
                template,
                ApiSpecResolutionFailureKind.AmbiguousMember,
                "The documentation identifier resolved to multiple original definitions.")
        };
    }
    private static bool MatchesTarget(ISymbol symbol, ApiSpecTarget target)
    {
        return target.MemberKind switch
        {
            SpecTargetMemberKind.Constructor => symbol is IMethodSymbol
            {
                MethodKind: MethodKind.Constructor
            } constructor &&
                constructor.IsStatic == target.IsStatic &&
                string.Equals(constructor.MetadataName, target.MemberName, StringComparison.Ordinal) &&
                constructor.Arity == target.GenericArity &&
                constructor.Parameters.Length == target.ParameterTypes.Length,
            SpecTargetMemberKind.Method => symbol is IMethodSymbol
            {
                MethodKind: MethodKind.Ordinary
            } method &&
                method.IsStatic == target.IsStatic &&
                string.Equals(method.Name, target.MemberName, StringComparison.Ordinal) &&
                method.Arity == target.GenericArity &&
                method.Parameters.Length == target.ParameterTypes.Length,
            SpecTargetMemberKind.PropertyGet => symbol is IPropertySymbol property &&
                property.GetMethod != null &&
                property.IsStatic == target.IsStatic &&
                string.Equals(property.Name, target.MemberName, StringComparison.Ordinal) &&
                property.GetMethod.Arity == target.GenericArity &&
                property.Parameters.Length == target.ParameterTypes.Length,
            _ => false
        };
    }

    private static (bool IdentityApproved, bool FamilyApproved,
        ApiSpecReferenceFamily ReferenceFamily, string ReferencePath) MatchAssembly(
        Compilation compilation, IAssemblySymbol assembly, ApiSpecTarget target)
    {
        var identity = assembly.Identity;
        var token = HashEncoding.ToLowerHex(identity.PublicKeyToken);
        bool IdentityMatches(ApiSpecAssemblyIdentity approved)
        {
            return string.Equals(approved.Name, identity.Name, StringComparison.Ordinal) &&
            string.Equals(approved.PublicKeyToken, token, StringComparison.OrdinalIgnoreCase);
        }

        var identityMatches = target.ApprovedAssemblies
            .Where(IdentityMatches)
            .ToArray();
        if (identityMatches.Length == 0)
        {
            return (false, false, ApiSpecReferenceFamily.Unspecified, string.Empty);
        }

        var reference = compilation.GetMetadataReference(assembly) as PortableExecutableReference;
        var path = reference?.FilePath ?? string.Empty;
        var family = ClassifyReferenceFamily(
            compilation,
            identity.Name,
            assembly,
            reference,
            path);
        return (
            true,
            identityMatches.Any(approved =>
                (approved.ReferenceFamily == ApiSpecReferenceFamily.Unspecified ||
                 approved.ReferenceFamily == family)),
            family,
            path);
    }
    private static ApiSpecReferenceFamily ClassifyReferenceFamily(
        Compilation compilation,
        string assemblyName,
        IAssemblySymbol assembly,
        PortableExecutableReference? reference,
        string path)
    {
        var normalized = path.Replace('\\', '/');
        foreach (var (marker, family) in ReferenceFamilyMarkers)
        {
            if (normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0 &&
                HasExpectedReferenceMetadata(reference, family))
            {
                return family;
            }
        }

        return assemblyName == "SharpProof.Attributes" &&
            string.Equals(Path.GetFileName(path), "SharpProof.Attributes.dll",
                StringComparison.OrdinalIgnoreCase) &&
            ContractApiIdentityResolver.ForCompilation(compilation)
                .IsResolvedContractAssembly(assembly)
            ? ApiSpecReferenceFamily.SharpProofPackage
            : ApiSpecReferenceFamily.Unspecified;
    }
    internal static bool HasExpectedReferenceMetadata(
        PortableExecutableReference? reference, ApiSpecReferenceFamily family)
    {
        if (reference?.GetMetadata() is not AssemblyMetadata metadata)
        {
            return false;
        }

        var reader = metadata.GetModules()[0].GetMetadataReader();
        var isReferenceAssembly = reader.GetAssemblyDefinition().GetCustomAttributes().Any(handle =>
            IsAttribute(reader, reader.GetCustomAttribute(handle),
                FrameworkTypeMetadataNames.ReferenceAssemblyAttribute));
        return family switch
        {
            ApiSpecReferenceFamily.MicrosoftNetCoreReferencePack or
            ApiSpecReferenceFamily.NetStandardReferencePack or
            ApiSpecReferenceFamily.NetFrameworkReferenceAssemblies => isReferenceAssembly,
            ApiSpecReferenceFamily.MicrosoftNetCoreRuntime => !isReferenceAssembly,
            _ => false
        };
    }
    private static bool IsAttribute(
        MetadataReader reader, CustomAttribute attribute, string metadataName)
    {
        var type = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference(
                (MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition(
                (MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default
        };
        return type.Kind switch
        {
            HandleKind.TypeReference => Matches(reader,
                reader.GetTypeReference((TypeReferenceHandle)type).Namespace,
                reader.GetTypeReference((TypeReferenceHandle)type).Name),
            HandleKind.TypeDefinition => Matches(reader,
                reader.GetTypeDefinition((TypeDefinitionHandle)type).Namespace,
                reader.GetTypeDefinition((TypeDefinitionHandle)type).Name),
            _ => false
        };
        bool Matches(MetadataReader metadata, StringHandle typeNamespace, StringHandle typeName)
        {
            return string.Equals(metadata.GetString(typeNamespace) + "." + metadata.GetString(typeName),
                metadataName, StringComparison.Ordinal);
        }
    }
    private static ApiSpecResolutionFailure Failure(
        ApiSpecTemplate template, ApiSpecResolutionFailureKind kind, string detail)
    {
        return new(template.Id, template.Target.WitnessIdentifier, kind, detail);
    }

    private static (ISymbol?, ApiSpecResolutionFailure) Unresolved(
            ApiSpecTemplate template, ApiSpecResolutionFailureKind kind, string detail)
    {
        return (null, Failure(template, kind, detail));
    }
}
