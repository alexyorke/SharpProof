using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

#pragma warning disable RS1035 // Captured implementation evidence requires reading the final PE image.
namespace SharpProof.CompilerArtifact;

internal static class CompilerMetadataResolution
{
    internal sealed class MetadataResolutionContext(CSharpCompilation compilation)
    {
        private sealed class ReferenceModule(
            PortableExecutableReference reference,
            ModuleMetadata module,
            string path)
        {
            internal PortableExecutableReference Reference { get; } = reference;
            internal ModuleMetadata Module { get; } = module;
            internal string Path { get; } = path;
        }

        private readonly CSharpCompilation _compilation =
            ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        private readonly Dictionary<PortableExecutableReference, ISymbol?> _symbols =
            new(ReferenceComparer<PortableExecutableReference>.Instance);
        private CSharpCompilation? _metadataCompilation;
        private Dictionary<
            (AssemblyIdentity Identity, string ModuleName),
            ReferenceModule[]>? _referenceModules;

        internal ISymbol? Resolve(PortableExecutableReference reference)
        {
            if (_symbols.TryGetValue(reference, out var symbol))
            {
                return symbol;
            }

            _metadataCompilation ??= _compilation.WithOptions(
                _compilation.Options.WithMetadataImportOptions(
                    MetadataImportOptions.All));
            symbol = _metadataCompilation.GetAssemblyOrModuleSymbol(reference);
            _symbols.Add(reference, symbol);
            return symbol;
        }

        internal bool TryFindReference(
            AssemblyIdentity assemblyIdentity,
            string moduleName,
            CancellationToken cancellationToken,
            out PortableExecutableReference reference,
            out ModuleMetadata module,
            out string modulePath)
        {
            var matches = GetReferenceModules(
                assemblyIdentity,
                moduleName,
                cancellationToken);
            if (matches.Length != 1)
            {
                reference = null!;
                module = null!;
                modulePath = string.Empty;
                return false;
            }

            reference = matches[0].Reference;
            module = matches[0].Module;
            modulePath = matches[0].Path;
            return true;
        }

        private ReferenceModule[] GetReferenceModules(
            AssemblyIdentity assemblyIdentity,
            string moduleName,
            CancellationToken cancellationToken)
        {
            _referenceModules ??= new Dictionary<
                (AssemblyIdentity Identity, string ModuleName),
                ReferenceModule[]>();
            var key = (assemblyIdentity, moduleName);
            if (_referenceModules.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var matches = new List<ReferenceModule>();
            foreach (var candidate in _compilation.References
                         .OfType<PortableExecutableReference>())
            {
                if (candidate.Properties.Kind != MetadataImageKind.Assembly ||
                    _compilation.GetAssemblyOrModuleSymbol(candidate)
                        is not IAssemblySymbol assembly ||
                    !assembly.Identity.Equals(assemblyIdentity) ||
                    candidate.FilePath == null ||
                    candidate.GetMetadata() is not AssemblyMetadata metadata)
                {
                    continue;
                }

                var modules = metadata.GetModules();
                for (var indexInAssembly = 0;
                     indexInAssembly < modules.Length;
                     indexInAssembly++)
                {
                    var module = modules[indexInAssembly];
                    var currentName = CompilerCompilationCapture.ReadModuleName(
                        module.GetMetadataReader());
                    if (!string.Equals(
                            currentName,
                            moduleName,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var path = indexInAssembly == 0
                        ? Path.GetFullPath(candidate.FilePath)
                        : CompilerCompilationCapture.ResolveSiblingModule(
                            candidate.FilePath,
                            currentName);
                    matches.Add(new ReferenceModule(
                        candidate,
                        module,
                        path));
                }
            }

            if (matches.Count > 1)
            {
                // Roslyn can expose several references for the same assembly
                // when aliases or duplicate paths are present.  Preserve the
                // fail-closed behavior for different images, but collapse
                // byte-identical images to one deterministic representative.
                var hashes = new Dictionary<string, ReferenceModule>(
                    StringComparer.Ordinal);
                foreach (var match in matches)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryHashReferenceModule(
                            match,
                            cancellationToken,
                            out var hash))
                    {
                        cached = matches.Take(2).ToArray();
                        _referenceModules.Add(key, cached);
                        return cached;
                    }

                    if (!hashes.ContainsKey(hash))
                    {
                        hashes.Add(hash, match);
                    }
                }

                if (hashes.Count == 1)
                {
                    cached = [matches
                        .OrderBy(static match => match.Path, StringComparer.Ordinal)
                        .First()];
                    _referenceModules.Add(key, cached);
                    return cached;
                }
            }

            cached = matches.Count > 1
                ? matches.Take(2).ToArray()
                : matches.ToArray();
            _referenceModules.Add(key, cached);
            return cached;
        }

        private static bool TryHashReferenceModule(
            ReferenceModule module,
            CancellationToken cancellationToken,
            out string hash)
        {
            try
            {
                using var stream = new FileStream(
                    module.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                hash = CompilerCompilationCapture.Hash(
                    stream,
                    cancellationToken);
                return true;
            }
            catch (Exception exception) when (exception is
                IOException or
                UnauthorizedAccessException or
                ArgumentException)
            {
                hash = string.Empty;
                return false;
            }
        }
    }

    internal static bool IsReferenceAssembly(IAssemblySymbol assembly)
    {
        return assembly.GetAttributes().Any(static attribute =>
            attribute.AttributeClass is
            {
                MetadataName: "ReferenceAssemblyAttribute",
                ContainingNamespace: { } containingNamespace
            } &&
            HasNamespace(
                containingNamespace,
                "System",
                "Runtime",
                "CompilerServices"));
    }

    internal static bool HasNamespace(
        INamespaceSymbol value,
        params string[] segments)
    {
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            if (value.IsGlobalNamespace ||
                value.Name != segments[index])
            {
                return false;
            }

            value = value.ContainingNamespace;
        }

        return value.IsGlobalNamespace;
    }

    internal static bool TryGetMethodDefinition(
        MetadataReader reader,
        int metadataToken,
        out MethodDefinitionHandle handle,
        out MethodDefinition definition)
    {
        var entity = MetadataTokens.Handle(metadataToken);
        if (entity.Kind != HandleKind.MethodDefinition)
        {
            handle = default;
            definition = default;
            return false;
        }

        handle = (MethodDefinitionHandle)entity;
        var rowNumber = MetadataTokens.GetRowNumber(handle);
        if (rowNumber <= 0 || rowNumber > reader.MethodDefinitions.Count)
        {
            definition = default;
            return false;
        }

        definition = reader.GetMethodDefinition(handle);
        return true;
    }

    internal static bool HasManagedIlBody(MethodDefinition definition)
    {
        return (definition.Attributes & MethodAttributes.Abstract) == 0 &&
            (definition.Attributes & MethodAttributes.PinvokeImpl) == 0 &&
            (definition.ImplAttributes & MethodImplAttributes.CodeTypeMask) ==
                MethodImplAttributes.IL &&
            (definition.ImplAttributes & MethodImplAttributes.ManagedMask) ==
                MethodImplAttributes.Managed;
    }

}
