using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

#pragma warning disable RS1035 // Captured implementation evidence requires reading the final PE image.
namespace SharpProof.CompilerArtifact;

internal static class CompilerMetadataResolution
{
    internal sealed class ReferenceModule(
        PortableExecutableReference reference,
        ModuleMetadata module,
        string path)
    {
        internal PortableExecutableReference Reference { get; } = (reference ?? throw new System.ArgumentNullException(nameof(reference)));
        internal ModuleMetadata Module { get; } = (module ?? throw new System.ArgumentNullException(nameof(module)));
        internal string Path { get; } = (path ?? throw new System.ArgumentNullException(nameof(path)));
    }

    internal sealed class MetadataResolutionContext(CSharpCompilation compilation)
    {
        private readonly CSharpCompilation _compilation =
            ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        private ImmutableDictionary<PortableExecutableReference, ISymbol?> _symbols =
            ImmutableDictionary.Create<PortableExecutableReference, ISymbol?>(ReferenceComparer<PortableExecutableReference>.Instance);
        private CSharpCompilation? _metadataCompilation;
        private ImmutableDictionary<
            (AssemblyIdentity Identity, string ModuleName),
            ImmutableArray<ReferenceModule>> _referenceModules = ImmutableDictionary<(AssemblyIdentity Identity, string ModuleName), ImmutableArray<ReferenceModule>>.Empty;

        internal ISymbol? Resolve(PortableExecutableReference reference)
        {
            reference = ArgumentNullGuard.NotNull(reference, nameof(reference));
            if (_symbols.TryGetValue(reference, out var symbol))
            {
                return symbol;
            }

            _metadataCompilation ??= _compilation.WithOptions(
                _compilation.Options.WithMetadataImportOptions(
                    MetadataImportOptions.All));
            symbol = _metadataCompilation.GetAssemblyOrModuleSymbol(reference);
            _symbols = _symbols.Add(reference, symbol);
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
            assemblyIdentity = ArgumentNullGuard.NotNull(assemblyIdentity, nameof(assemblyIdentity));
            moduleName = ArgumentNullGuard.NotNull(moduleName, nameof(moduleName));
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

        private ImmutableArray<ReferenceModule> GetReferenceModules(
            AssemblyIdentity assemblyIdentity,
            string moduleName,
            CancellationToken cancellationToken)
        {
            assemblyIdentity = RepositoryParameterGuard.NotNull(assemblyIdentity, nameof(assemblyIdentity));
            moduleName = RepositoryParameterGuard.NotNull(moduleName, nameof(moduleName));
            var key = (assemblyIdentity, moduleName);
            if (_referenceModules.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var matches = ImmutableList<ReferenceModule>.Empty;
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
                    matches = matches.Add(new ReferenceModule(
                        candidate,
                        module,
                        path));
                }
            }

            cached = SelectReferenceModules(matches, cancellationToken);
            _referenceModules = _referenceModules.Add(key, cached);
            return cached;
        }
    }

    internal static ImmutableArray<ReferenceModule> SelectReferenceModules(
        IReadOnlyList<ReferenceModule> matches,
        CancellationToken cancellationToken)
    {
        matches = RepositoryParameterGuard.NotNull(matches, nameof(matches));
        if (matches.Count > 1)
        {
            // Roslyn can expose several references for the same assembly
            // when aliases or duplicate paths are present.  Preserve the
            // fail-closed behavior for different images, but collapse
            // byte-identical images to one deterministic representative.
            var hashes = ImmutableDictionary.Create<string, ReferenceModule>(StringComparer.Ordinal);
            foreach (var match in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryHashReferenceModule(
                        match,
                        cancellationToken,
                        out var hash))
                {
                    return matches.Take(2).ToImmutableArray();
                }

                if (!hashes.ContainsKey(hash))
                {
                    hashes = hashes.Add(hash, match);
                }
            }

            if (hashes.Count == 1)
            {
                return [matches
                    .OrderBy(static match => match is null ? throw new System.ArgumentNullException(nameof(match)) : (match.Path), StringComparer.Ordinal)
                    .First()];
            }
        }

        return matches.Count > 1
            ? matches.Take(2).ToImmutableArray()
            : matches.ToImmutableArray();
    }

    private static bool TryHashReferenceModule(
        ReferenceModule module,
        CancellationToken cancellationToken,
        out string hash)
    {
        module = RepositoryParameterGuard.NotNull(module, nameof(module));
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

    internal static bool IsReferenceAssembly(IAssemblySymbol assembly)
    {
        assembly = RepositoryParameterGuard.NotNull(assembly, nameof(assembly));
        return assembly.GetAttributes().Any(static attribute =>
            attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeClass is
            {
                MetadataName: "ReferenceAssemblyAttribute",
                ContainingNamespace: { } containingNamespace
            } &&
            HasNamespace(
                containingNamespace,
                "System",
                "Runtime",
                "CompilerServices")));
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
        Handle entity;
        if ((metadataToken >> 24) != (int)TableIndex.MethodDef ||
            (entity = MetadataTokens.Handle(metadataToken)).Kind != HandleKind.MethodDefinition)
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
