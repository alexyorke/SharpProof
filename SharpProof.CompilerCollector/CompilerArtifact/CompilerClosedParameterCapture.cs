using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace SharpProof.CompilerArtifact;

// Captures and pairs closed-parameter evidence from supplied metadata rows.
// Body and metadata ownership admission remain the caller's responsibility.
internal static class CompilerClosedParameterCapture
{
    internal static bool TryCapture(CSharpCompilation compilation, MetadataReader reader,
        IMethodSymbol method, IEnumerable<ParameterHandle> parameters, CancellationToken cancellationToken,
        out ImmutableArray<TotalIlClosedAttribute> attributes)
    {
        if (parameters is null)
        {
            throw new System.ArgumentNullException(nameof(parameters));
        }
        attributes = [];
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var result = ImmutableArray.CreateBuilder<TotalIlClosedAttribute>();
        var sequences = new HashSet<int>();
        var remainingRows = 1024;
        foreach (var parameterHandle in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--remainingRows < 0)
            { return false; }
            var parameter = reader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber == 0)
            { continue; }
            if (parameter.SequenceNumber > method.Parameters.Length || !sequences.Add(parameter.SequenceNumber))
            { return false; }
            foreach (var attributeHandle in parameter.GetCustomAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (--remainingRows < 0)
                { return false; }
                var attribute = reader.GetCustomAttribute(attributeHandle);
                EntityHandle typeHandle;
                StringHandle constructorName;
                BlobHandle constructorSignature;
                if (attribute.Constructor.Kind == HandleKind.MemberReference)
                {
                    var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                    typeHandle = constructor.Parent;
                    constructorName = constructor.Name;
                    constructorSignature = constructor.Signature;
                }
                else if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
                {
                    var constructor = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                    typeHandle = constructor.GetDeclaringType();
                    constructorName = constructor.Name;
                    constructorSignature = constructor.Signature;
                }
                else
                { continue; }

                string typeNamespace;
                string typeName;
                AssemblyIdentity? identity;
                var assemblyReference = default(AssemblyReferenceHandle);
                if (typeHandle.Kind == HandleKind.TypeReference)
                {
                    var type = reader.GetTypeReference((TypeReferenceHandle)typeHandle);
                    typeNamespace = reader.GetString(type.Namespace);
                    typeName = reader.GetString(type.Name);
                    assemblyReference = type.ResolutionScope.Kind == HandleKind.AssemblyReference
                        ? (AssemblyReferenceHandle)type.ResolutionScope : default;
                    identity = null;
                }
                else if (typeHandle.Kind == HandleKind.TypeDefinition)
                {
                    var type = reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle);
                    typeNamespace = reader.GetString(type.Namespace);
                    typeName = reader.GetString(type.Name);
                    identity = method.ContainingAssembly.Identity;
                }
                else
                { continue; }

                var (recognized, expected) = (typeNamespace, typeName) switch
                {
                    ("SharpProof.Attributes", "PositiveAttribute") => (true, selections.Positive),
                    ("SharpProof.Attributes", "NotNullAttribute") => (true, selections.NotNull),
                    ("SharpProof.Attributes", "InRangeAttribute") => (true, selections.InRange),
                    _ => (false, (INamedTypeSymbol?)null)
                };
                if (!recognized)
                { continue; }
                if (typeHandle.Kind == HandleKind.TypeReference && expected != null)
                {
                    if (assemblyReference.IsNil || !CompilerTotalIlBodyProvider.TryReadAssemblyIdentity(reader, assemblyReference,
                        expected.ContainingAssembly.Identity, out var resolvedIdentity))
                    { return false; }
                    identity = resolvedIdentity;
                }
                if (expected == null || identity == null || !identity.Equals(expected.ContainingAssembly.Identity) ||
                    reader.GetString(constructorName) != ".ctor")
                { return false; }
                var inRange = typeName == "InRangeAttribute";
                var signature = reader.GetBlobReader(constructorSignature);
                if (signature.Length != (inRange ? 5 : 3) || signature.ReadByte() != 0x20 ||
                    signature.ReadByte() != (inRange ? 2 : 0) || signature.ReadByte() != 1 ||
                    inRange && (signature.ReadByte() != 0x0a || signature.ReadByte() != 0x0a) ||
                    signature.RemainingBytes != 0)
                { return false; }
                var expectedConstructor = expected.InstanceConstructors.SingleOrDefault(candidate =>
                    candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.Parameters.Length == (inRange ? 2 : 0) &&
                    candidate.Parameters.All(static parameter => parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.RefKind == RefKind.None &&
                        parameter.Type.SpecialType == SpecialType.System_Int64))));
                if (expectedConstructor == null ||
                    attribute.Constructor.Kind == HandleKind.MethodDefinition &&
                    (expectedConstructor.MetadataToken != MetadataTokens.GetToken(attribute.Constructor) ||
                        expectedConstructor.ContainingModule.Name != method.ContainingModule.Name))
                { return false; }
                if (!TryReadClosedBlob(reader, attribute.Value, inRange, out var minimum, out var maximum))
                { return false; }
                result.Add(new(method.MetadataToken, parameter.SequenceNumber - 1, parameter.SequenceNumber,
                    MetadataTokens.GetToken(parameterHandle), MetadataTokens.GetToken(attributeHandle),
                    MetadataTokens.GetToken(attribute.Constructor), CompilerIdentityBridge.CreateSymbolDisplay(expected),
                    CompilerIdentityBridge.CreateSymbolDisplay(expectedConstructor),
                    inRange ? "InRange" : typeName == "PositiveAttribute" ? "Positive" : "NotNull",
                    ImmutableArray.CreateRange(reader.GetBlobBytes(constructorSignature)),
                    ImmutableArray.CreateRange(reader.GetBlobBytes(attribute.Value)), minimum, maximum));
            }
        }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var binding = new ContractBinder(compilation, factory).BindTotalMetadataRequires(new(factory, method), cancellationToken);
        if (binding.Failure == ContractBindingFailure.ContractApiUnavailable && result.Count == 0)
        { return true; }
        return binding.IsSuccess && CompilerTotalIlBodyProvider.TryPairMetadataRequires(binding.Clauses, result.ToImmutable(),
            cancellationToken, out attributes);
    }

    private static bool TryReadClosedBlob(MetadataReader reader, BlobHandle handle, bool inRange,
        out long minimum, out long maximum)
    {
        minimum = 0;
        maximum = 0;
        var blob = reader.GetBlobReader(handle);
        if (blob.Length != (inRange ? 20 : 4) || blob.ReadUInt16() != 1)
        { return false; }
        if (inRange)
        {
            minimum = blob.ReadInt64();
            maximum = blob.ReadInt64();
            if (minimum > maximum)
            { return false; }
        }
        return blob.ReadUInt16() == 0 && blob.RemainingBytes == 0;
    }
}
