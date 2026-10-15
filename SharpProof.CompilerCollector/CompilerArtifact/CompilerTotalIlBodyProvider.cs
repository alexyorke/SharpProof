using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Implementation evidence is read only by the build-time collector.
#pragma warning disable RS1035
namespace SharpProof.CompilerArtifact;

internal sealed class CompilerTotalIlBodyProvider(CSharpCompilation compilation, CompilerReferenceSnapshot[]? references)
{
    private readonly CompilerMetadataResolution.MetadataResolutionContext _resolution = new(ArgumentNullGuard.NotNull(compilation, nameof(compilation)));
    private readonly Dictionary<string, byte[]> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TotalIlBody?> _bodies = new(StringComparer.Ordinal);
    private CompilerReferenceSnapshot[]? _capturedReferences = references;

    internal TotalIlBody? Resolve(IMethodSymbol method, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CompilerScalarIlDecoder.Candidate(method) || SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly))
        { return null; }
        var key = method.ContainingAssembly.Identity + "/" + method.ContainingModule.Name + "/" + method.MetadataToken;
        if (_bodies.TryGetValue(key, out var cached))
        { return cached; }
        TotalIlBody? result;
        try
        { result = Read(method, cancellationToken); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
            BadImageFormatException or InvalidOperationException or OverflowException)
        { result = null; }
        _bodies.Add(key, result);
        return result;
    }

    internal static void FillExactly(Stream stream, byte[] destination, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read != destination.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = stream.Read(destination, read, Math.Min(81920, destination.Length - read));
            if (count == 0)
            { throw new EndOfStreamException(); }
            read += count;
        }
    }

    private static bool NoInitialization(PEReader pe, MetadataReader reader, TypeDefinitionHandle type)
    {
        var remaining = 128;
        for (var current = type; !current.IsNil; current = reader.GetTypeDefinition(current).GetDeclaringType())
        {
            if (--remaining < 0)
            { return false; }
            foreach (var methodHandle in reader.GetTypeDefinition(current).GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != ".cctor")
                { continue; }
                if (!CompilerMetadataResolution.HasManagedIlBody(method) || method.RelativeVirtualAddress == 0 ||
                    (method.ImplAttributes & MethodImplAttributes.Synchronized) != 0 || method.GetDeclarativeSecurityAttributes().Count != 0)
                { return false; }
                var body = pe.GetMethodBody(method.RelativeVirtualAddress);
                var code = body.GetILBytes() ?? Array.Empty<byte>();
                if (!body.ExceptionRegions.IsEmpty || !body.LocalSignature.IsNil || code.Length is 0 or > 4096 ||
                    code[code.Length - 1] != 0x2a || code.Take(code.Length - 1).Any(opcode => opcode != 0x00))
                { return false; }
            }
        }
        return true;
    }

    private TotalIlBody? Read(IMethodSymbol method, CancellationToken cancellationToken)
    {
        if (!_resolution.TryFindReference(method.ContainingAssembly.Identity, method.ContainingModule.Name, cancellationToken,
                out var reference, out var backing, out var path) || _resolution.Resolve(reference) is not IAssemblySymbol assembly)
        { return null; }
        _capturedReferences ??= CompilerCompilationCapture.CaptureReferences(compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, cancellationToken);
        var authority = _capturedReferences.SelectMany(item => item.Modules).FirstOrDefault(module =>
            module.Name == method.ContainingModule.Name && Path.GetFullPath(module.Path) == Path.GetFullPath(path));
        if (authority == null || authority.SizeBytes <= 0 || authority.SizeBytes > CompilerReferenceLimits.MaximumModuleBytes)
        { return null; }
        if (!_images.TryGetValue(path, out var bytes))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length != authority.SizeBytes)
            { return null; }
            bytes = new byte[(int)stream.Length];
            FillExactly(stream, bytes, cancellationToken);
            using var buffer = new MemoryStream(bytes, writable: false);
            if (CompilerCompilationCapture.Hash(buffer, cancellationToken) != authority.Sha256)
            { return null; }
            _images.Add(path, bytes);
        }
        using var immutableImage = new MemoryStream(bytes, writable: false);
        using var pe = new PEReader(immutableImage);
        if (!pe.HasMetadata || pe.PEHeaders.CorHeader is not { } corHeader ||
            (corHeader.Flags & CorFlags.ILOnly) == 0 ||
            (corHeader.Flags & (CorFlags.Requires32Bit | CorFlags.NativeEntryPoint)) != 0 ||
            pe.PEHeaders.CoffHeader.Machine is not (Machine.I386 or Machine.Amd64) ||
            pe.PEHeaders.PEHeader is not { } peHeader ||
            peHeader.Magic != (pe.PEHeaders.CoffHeader.Machine == Machine.I386 ? PEMagic.PE32 : PEMagic.PE32Plus))
        { return null; }
        var reader = pe.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var typeDefinition = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(typeDefinition.Name) == "<Module>" && typeDefinition.GetMethods().Any(handle =>
                    reader.GetString(reader.GetMethodDefinition(handle).Name) == ".cctor"))
            { return null; }
        }
        if (!CompilerCompilationCapture.MetadataEquals(backing.GetMetadataReader(), reader) ||
            reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString("D") != authority.Mvid ||
            !CompilerMetadataResolution.TryGetMethodDefinition(reader, method.MetadataToken, out var handle, out var definition) ||
            !CompilerMetadataResolution.HasManagedIlBody(definition) ||
            (definition.ImplAttributes & MethodImplAttributes.Synchronized) != 0 || definition.RelativeVirtualAddress == 0 ||
            CompilerScalarIlDecoder.ResolveMethod(reader, assembly, handle, method.ContainingModule.Name) is not { } resolved ||
            !SameSignature(method, resolved) || !NoInitialization(pe, reader, definition.GetDeclaringType()))
        { return null; }
        if (!TryCaptureClosedAttributes(compilation, reader, method, definition, cancellationToken, out var parameterAttributes))
        { return null; }
        var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
        if (body.Size is <= 0 or > 65536 || body.MaxStack is < 0 or > 128 || !body.ExceptionRegions.IsEmpty)
        { return null; }
        var locals = ImmutableArray<SpecialType>.Empty;
        if (!body.LocalSignature.IsNil)
        {
            var signature = reader.GetStandaloneSignature(body.LocalSignature);
            var header = reader.GetBlobReader(signature.Signature);
            if (header.ReadSignatureHeader().Kind != SignatureKind.LocalVariables || header.ReadCompressedInteger() > 128)
            { return null; }
            locals = signature.DecodeLocalSignature(new CompilerScalarSignatureProvider(), genericContext: null);
            if (locals.Any(type => !TotalIlStack.Supported(type)))
            { return null; }
        }
        var instructions = CompilerScalarIlDecoder.Decode(body, reader, assembly, method.ContainingModule.Name, cancellationToken);
        return instructions.IsDefault ? null : new(method, authority.Sha256, authority.Name, locals,
            body.LocalVariablesInitialized, body.MaxStack, instructions, parameterAttributes, authority.Mvid);
    }

    private static bool SameSignature(IMethodSymbol expected, IMethodSymbol actual)
    {
        return CompilerScalarIlDecoder.Candidate(actual) && expected.MetadataToken == actual.MetadataToken &&
            expected.MetadataName == actual.MetadataName && SameType(expected.ReturnType, actual.ReturnType) &&
            expected.Parameters.Length == actual.Parameters.Length &&
            expected.Parameters.Select((parameter, ordinal) => SameType(parameter.Type, actual.Parameters[ordinal].Type)).All(same => same);
    }

    // The all-members metadata compilation has distinct Roslyn symbol instances.
    // This route admits only intrinsic SpecialTypes, never arbitrary classes or
    // arrays; compare their bound assembly identities as well as the intrinsic.
    private static bool SameType(ITypeSymbol expected, ITypeSymbol actual)
    {
        return expected.SpecialType == actual.SpecialType && expected.SpecialType != SpecialType.None &&
            expected.ContainingAssembly.Identity.Equals(actual.ContainingAssembly.Identity);
    }

    private static bool TryCaptureClosedAttributes(CSharpCompilation compilation, MetadataReader reader,
        IMethodSymbol method, MethodDefinition definition, CancellationToken cancellationToken,
        out ImmutableArray<TotalIlClosedAttribute> attributes)
    {
        attributes = [];
        var selections = ContractSelectionInventory.ForCompilation(compilation);
        var result = ImmutableArray.CreateBuilder<TotalIlClosedAttribute>();
        var sequences = new HashSet<int>();
        var remainingRows = 1024;
        foreach (var parameterHandle in definition.GetParameters())
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
                    if (assemblyReference.IsNil || !TryReadAssemblyIdentity(reader, assemblyReference,
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
                    candidate.Parameters.Length == (inRange ? 2 : 0) &&
                    candidate.Parameters.All(static parameter => parameter.RefKind == RefKind.None &&
                        parameter.Type.SpecialType == SpecialType.System_Int64));
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
        return binding.IsSuccess && TryPairMetadataRequires(binding.Clauses, result.ToImmutable(),
            cancellationToken, out attributes);
    }

    internal static bool TryPairMetadataRequires(ImmutableArray<BoundTotalMetadataRequires> clauses,
        ImmutableArray<TotalIlClosedAttribute> evidence, CancellationToken cancellationToken,
        out ImmutableArray<TotalIlClosedAttribute> paired)
    {
        paired = [];
        if (clauses.IsDefault || evidence.IsDefault || clauses.Length != evidence.Length || clauses.Length > 1024)
        { return false; }
        var consumed = new HashSet<int>();
        var result = ImmutableArray.CreateBuilder<TotalIlClosedAttribute>();
        foreach (var clause in clauses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributeIdentity = CompilerIdentityBridge.CreateSymbolDisplay(clause.Attribute.AttributeClass);
            var constructorIdentity = CompilerIdentityBridge.CreateSymbolDisplay(clause.Attribute.AttributeConstructor);
            var kind = clause.Validation.Kind.ToString();
            var matches = evidence.Where(row => row.ParameterOrdinal == clause.ParameterOrdinal &&
                row.AttributeIdentity == attributeIdentity && row.ConstructorIdentity == constructorIdentity &&
                row.Kind == kind && row.Minimum == clause.Validation.Minimum && row.Maximum == clause.Validation.Maximum)
                .Take(2).ToArray();
            if (matches.Length != 1 || !consumed.Add(matches[0].AttributeToken))
            { return false; }
            result.Add(matches[0].WithClauseOrdinal(result.Count));
        }
        paired = result.ToImmutable();
        return true;
    }

    internal static bool TryReadAssemblyIdentity(MetadataReader reader, AssemblyReferenceHandle handle,
        AssemblyIdentity expected, out AssemblyIdentity identity)
    {
        identity = null!;
        var assembly = reader.GetAssemblyReference(handle);
        const AssemblyFlags contentTypeMask = (AssemblyFlags)0x0e00;
        const AssemblyFlags allowed = AssemblyFlags.PublicKey | AssemblyFlags.Retargetable | contentTypeMask;
        var hasPublicKey = (assembly.Flags & AssemblyFlags.PublicKey) != 0;
        var isRetargetable = (assembly.Flags & AssemblyFlags.Retargetable) != 0;
        var contentType = (AssemblyContentType)(((int)assembly.Flags & (int)contentTypeMask) >> 9);
        if ((assembly.Flags & ~allowed) != 0 || isRetargetable != expected.IsRetargetable ||
            contentType != expected.ContentType)
        { return false; }
        var expectedKey = hasPublicKey ? expected.PublicKey : expected.PublicKeyToken;
        var key = reader.GetBlobReader(assembly.PublicKeyOrToken);
        // Reject oversized/unexpected keys before allocating a byte array.
        if (key.Length != expectedKey.Length || hasPublicKey && expectedKey.IsEmpty)
        { return false; }
        var bytes = key.ReadBytes(key.Length);
        if (!bytes.SequenceEqual(expectedKey))
        { return false; }
        identity = new(reader.GetString(assembly.Name), assembly.Version,
            assembly.Culture.IsNil ? null : reader.GetString(assembly.Culture),
            ImmutableArray.CreateRange(bytes), hasPublicKey, isRetargetable, contentType);
        return true;
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

// Decodes scalar IL representation; provider admission remains unchanged.
internal static class CompilerScalarIlDecoder
{
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => opcode.Value);
    internal static bool Candidate(IMethodSymbol method)
    {
        return method is
        {
            MethodKind: MethodKind.Ordinary, IsStatic: true, IsAsync: false, IsExtern: false, IsVirtual: false,
            Arity: 0, ReturnsByRef: false, ReturnsByRefReadonly: false, ReducedFrom: null
        } &&
            !method.IsVararg && !method.HasUnsupportedMetadata && !method.ContainingType.IsGenericType && method.Parameters.Length <= 128 &&
            method.Parameters.All(parameter => parameter.RefKind == RefKind.None && !parameter.IsParams && TotalIlStack.Supported(parameter.Type.SpecialType)) &&
            (method.ReturnsVoid || TotalIlStack.Supported(method.ReturnType.SpecialType)) &&
            !CompilerMetadataResolution.IsReferenceAssembly(method.ContainingAssembly) &&
            !method.GetAttributes().Any(attribute => attribute.AttributeClass is { MetadataName: "UnmanagedCallersOnlyAttribute" } type &&
                CompilerMetadataResolution.HasNamespace(type.ContainingNamespace, "System", "Runtime", "InteropServices"));
    }

    internal static ImmutableArray<TotalIlInstruction> Decode(MethodBodyBlock body, MetadataReader metadata,
        IAssemblySymbol assembly, string module, CancellationToken cancellationToken)
    {
        var decoded = ImmutableArray.CreateBuilder<TotalIlInstruction>();
        var reader = body.GetILReader();
        while (reader.RemainingBytes != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (decoded.Count >= RoslynTotalProgramLowerer.MaximumRegionSteps)
            { return default; }
            var offset = reader.Offset;
            var first = reader.ReadByte();
            var raw = first == 0xfe ? (short)(0xfe00 | reader.ReadByte()) : (short)first;
            if (!Opcodes.TryGetValue(raw, out var opcode))
            { return default; }
            long operand;
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    operand = 0;
                    break;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineBrTarget:
                    operand = reader.ReadSByte();
                    break;
                case OperandType.ShortInlineVar:
                    operand = reader.ReadByte();
                    break;
                case OperandType.InlineVar:
                    operand = reader.ReadUInt16();
                    break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineMethod:
                    operand = reader.ReadInt32();
                    break;
                case OperandType.InlineI8:
                    operand = reader.ReadInt64();
                    break;
                default:
                    return default;
            }
            var name = ((ILOpCode)(ushort)raw).ToString();
            var target = opcode.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget
                ? checked(reader.Offset + (int)operand) : -1;
            if (opcode.OperandType is OperandType.ShortInlineBrTarget or OperandType.InlineBrTarget &&
                (target < 0 || target >= reader.Length))
            { return default; }
            IMethodSymbol? called = null;
            if (name == "Call")
            {
                var entity = MetadataTokens.Handle((int)operand);
                if (entity.Kind != HandleKind.MethodDefinition ||
                    ResolveMethod(metadata, assembly, (MethodDefinitionHandle)entity, module) is not { } dependency || !Candidate(dependency))
                { return default; }
                called = dependency;
            }
            if (name.StartsWith("Ldarg_", StringComparison.Ordinal) || name.StartsWith("Ldloc_", StringComparison.Ordinal) ||
                name.StartsWith("Stloc_", StringComparison.Ordinal))
            {
                var split = name.LastIndexOf('_');
                if (name.Substring(split + 1) != "s")
                { operand = int.Parse(name.Substring(split + 1), CultureInfo.InvariantCulture); }
                name = name.Substring(0, split);
            }
            else if (name.StartsWith("Ldc_i4_", StringComparison.Ordinal))
            {
                var suffix = name.Substring(7);
                if (suffix != "s")
                { operand = suffix == "m1" ? -1 : int.Parse(suffix, CultureInfo.InvariantCulture); }
                name = "Ldc_i4";
            }
            else if (name.EndsWith("_s", StringComparison.Ordinal))
            { name = name.Substring(0, name.Length - 2); }
            decoded.Add(new(offset, name, operand, target, called));
        }
        var offsets = decoded.Select((instruction, index) => (instruction.Offset, index)).ToDictionary(item => item.Offset, item => item.index);
        for (var index = 0; index < decoded.Count; index++)
        {
            var instruction = decoded[index];
            if (instruction.Target < 0)
            { continue; }
            if (!offsets.TryGetValue(instruction.Target, out var target))
            { return default; }
            decoded[index] = new(instruction.Offset, instruction.Code, instruction.Operand, target, instruction.Method);
        }
        return decoded.ToImmutable();
    }

    internal static IMethodSymbol? ResolveMethod(MetadataReader reader, IAssemblySymbol assembly, MethodDefinitionHandle handle, string module)
    {
        var definition = reader.GetMethodDefinition(handle);
        var type = assembly.GetTypeByMetadataName(TypeName(reader, definition.GetDeclaringType()));
        return type?.GetMembers(reader.GetString(definition.Name)).OfType<IMethodSymbol>()
            .SingleOrDefault(method => method.MetadataToken == MetadataTokens.GetToken(handle) && method.ContainingModule.Name == module);
    }

    private static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var parent = definition.GetDeclaringType();
        if (!parent.IsNil)
        { return TypeName(reader, parent) + "+" + name; }
        var space = reader.GetString(definition.Namespace);
        return space.Length == 0 ? name : space + "." + name;
    }

}
