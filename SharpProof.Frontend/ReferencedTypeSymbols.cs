using System.Reflection.Metadata;
namespace SharpProof.Frontend;
internal static class ReferencedTypeSymbols
{
    internal static IEnumerable<INamedTypeSymbol> GetAll(
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        return GetAll(compilation, null, cancellationToken);
    }
    internal static IEnumerable<INamedTypeSymbol> GetAll(
        Compilation compilation,
        INamedTypeSymbol? attributeType,
        CancellationToken cancellationToken = default)
    {
        foreach (var type in GetAll(
                     compilation.Assembly.GlobalNamespace,
                     cancellationToken))
        {
            yield return type;
        }
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attributeType != null &&
                !MayContainAttribute(
                    compilation,
                    assembly,
                    attributeType,
                    cancellationToken))
            {
                continue;
            }
            foreach (var type in GetAll(
                         assembly.GlobalNamespace,
                         cancellationToken))
            {
                yield return type;
            }
        }
    }
    private static bool MayContainAttribute(
        Compilation compilation,
        IAssemblySymbol assembly,
        INamedTypeSymbol attributeType,
        CancellationToken cancellationToken)
    {
        if (SymbolEqualityComparer.Default.Equals(
                assembly,
                attributeType.ContainingAssembly))
        {
            return true;
        }
        // A direct assembly reference is the common case and avoids opening
        // the metadata stream for the assembly at all. The metadata fallback
        // below also handles a forwarded attribute constructor.
        if (assembly.Modules.Any(module =>
                module.ReferencedAssemblySymbols.Any(reference =>
                    SymbolEqualityComparer.Default.Equals(
                        reference,
                        attributeType.ContainingAssembly))))
        {
            return true;
        }
        var reference = compilation.GetMetadataReference(assembly)
            as PortableExecutableReference;
        if (reference == null)
        {
            // Compilation references and unusual Roslyn symbol sources do not
            // expose PE metadata. Keep them in the candidate set so a new
            // symbol source cannot turn a valid companion into a miss.
            return true;
        }
        try
        {
            if (reference.GetMetadata() is not AssemblyMetadata metadata)
            {
                return true;
            }
            var namespaceName = GetMetadataNamespaceName(
                attributeType.ContainingNamespace);
            var metadataName = attributeType.MetadataName;
            foreach (var module in metadata.GetModules())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ModuleMayContainAttribute(
                        module.GetMetadataReader(),
                        namespaceName,
                        metadataName,
                        cancellationToken))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            BadImageFormatException or
            IOException or
            UnauthorizedAccessException)
        {
            // Metadata screening is an optimization only. If metadata cannot
            // be inspected, retain the old full-symbol behavior.
            return true;
        }
    }
    private static bool ModuleMayContainAttribute(
        MetadataReader reader,
        string namespaceName,
        string metadataName,
        CancellationToken cancellationToken)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var attributeHandle in type.GetCustomAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attribute = reader.GetCustomAttribute(attributeHandle);
                if (AttributeTypeMatches(
                        reader,
                        attribute.Constructor,
                        namespaceName,
                        metadataName))
                {
                    return true;
                }
            }
        }
        return false;
    }
    private static bool AttributeTypeMatches(
        MetadataReader reader,
        EntityHandle constructor,
        string namespaceName,
        string metadataName)
    {
        var type = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference(
                (MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition(
                (MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default
        };
        return type.Kind switch
        {
            HandleKind.TypeReference => TypeReferenceMatches(
                reader,
                (TypeReferenceHandle)type,
                namespaceName,
                metadataName),
            HandleKind.TypeDefinition => TypeDefinitionMatches(
                reader,
                (TypeDefinitionHandle)type,
                namespaceName,
                metadataName),
            _ => false
        };
    }
    private static bool TypeReferenceMatches(
        MetadataReader reader,
        TypeReferenceHandle handle,
        string namespaceName,
        string metadataName)
    {
        var type = reader.GetTypeReference(handle);
        return string.Equals(
                   reader.GetString(type.Namespace),
                   namespaceName,
                   StringComparison.Ordinal) &&
               string.Equals(
                   reader.GetString(type.Name),
                   metadataName,
                   StringComparison.Ordinal);
    }
    private static bool TypeDefinitionMatches(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        string namespaceName,
        string metadataName)
    {
        var type = reader.GetTypeDefinition(handle);
        return string.Equals(
                   reader.GetString(type.Namespace),
                   namespaceName,
                   StringComparison.Ordinal) &&
               string.Equals(
                   reader.GetString(type.Name),
                   metadataName,
                   StringComparison.Ordinal);
    }
    private static string GetMetadataNamespaceName(INamespaceSymbol @namespace)
    {
        var names = new Stack<string>();
        for (var current = @namespace;
             !current.IsGlobalNamespace;
             current = current.ContainingNamespace)
        {
            names.Push(current.MetadataName);
        }
        return string.Join(".", names);
    }
    private static IEnumerable<INamedTypeSymbol> GetAll(
        INamespaceOrTypeSymbol container,
        CancellationToken cancellationToken)
    {
        foreach (var type in container.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return type;
            foreach (var nested in GetAll(type, cancellationToken))
            {
                yield return nested;
            }
        }
        if (container is not INamespaceSymbol @namespace)
        {
            yield break;
        }
        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var type in GetAll(child, cancellationToken))
            {
                yield return type;
            }
        }
    }
}
