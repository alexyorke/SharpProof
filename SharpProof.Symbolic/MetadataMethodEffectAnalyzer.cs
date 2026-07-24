using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using SharpProof.Attributes;
namespace SharpProof.Symbolic;
internal sealed class MetadataMethodEffectAnalyzer(Compilation compilation) {
    private const int MaxDepth = 32;
    private const int MaxMethods = 256;
    private const int MaxInstructions = 100_000;
    private static readonly ImmutableDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(static field => field.FieldType == typeof(OpCode))
        .Select(static field => (OpCode)field.GetValue(null)!)
        .ToImmutableDictionary(static opcode => opcode.Value);
    private readonly ImmutableDictionary<AssemblyLookupKey, string> _referencePaths = BuildReferencePaths(compilation);
    private readonly ConcurrentDictionary<(Guid Mvid, int Token, string Context), Lazy<MethodEffects>> _cache = new();
    internal MethodEffects Analyze(IMethodSymbol method) {
        if (method.ContainingAssembly == null) return Unknown("metadata_assembly_unavailable");
        var reference = compilation.GetMetadataReference(method.ContainingAssembly) as PortableExecutableReference;
        var path = reference?.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Unknown("metadata_implementation_path_unavailable");
        if (!SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference!), method.ContainingAssembly))
            return Unknown("metadata_assembly_identity_mismatch");
        try {
            using var stream = File.OpenRead(path!);
            using var pe = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
            if (!pe.HasMetadata) return Unknown("metadata_image_unavailable");
            var reader = pe.GetMetadataReader();
            var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (!TryFindMethod(reader, method, out var handle)) return Unknown("metadata_method_unresolved");
            var key = (Mvid: mvid, Token: MetadataTokens.GetToken(handle), Context: method.ToDisplayString());
            return _cache.GetOrAdd(key, _ => new Lazy<MethodEffects>(
                () => AnalyzeBody(path!, handle),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or
                                          InvalidOperationException or ArgumentException) {
            return Unknown("malformed_or_unavailable_metadata");
        }
    }
    private MethodEffects AnalyzeBody(string path, MethodDefinitionHandle root) {
        var active = new HashSet<MethodLocation>();
        var analyzed = new HashSet<MethodLocation>();
        var effects = SharpProofEffect.None;
        var unknowns = ImmutableArray.CreateBuilder<SharpProofUnknownReason>();
        var exceptions = ImmutableArray.CreateBuilder<string>();
        var hasUnknownExceptionBoundary = false;
        var instructionCount = 0;
        void MarkUnknown(string reason, SharpProofEffect additional = SharpProofEffect.None, bool exceptionBoundary = false) {
            effects |= SharpProofEffect.Unknown | additional;
            hasUnknownExceptionBoundary |= exceptionBoundary;
            unknowns.Add(Reason(reason));
        }
        void Visit(MethodLocation location, int depth) {
            if (depth > MaxDepth || analyzed.Count >= MaxMethods) {
                MarkUnknown("metadata_budget_exhausted", SharpProofEffect.BudgetExhaustion);
                return;
            }
            if (analyzed.Contains(location)) return;
            if (!active.Add(location)) {
                MarkUnknown("metadata_recursive_cycle");
                return;
            }
            try {
                using var stream = File.OpenRead(location.Path);
                using var pe = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
                var reader = pe.GetMetadataReader();
                var definition = reader.GetMethodDefinition(location.Handle);
                if ((definition.Attributes & MethodAttributes.PinvokeImpl) != 0) {
                    MarkUnknown("metadata_native_exception_boundary", SharpProofEffect.UsesNativeCode, true);
                    return;
                }
                if (definition.RelativeVirtualAddress == 0) {
                    MarkUnknown("metadata_body_unavailable", exceptionBoundary: true);
                    return;
                }
                var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
                if (body.ExceptionRegions.Length != 0) {
                    MarkUnknown("metadata_exception_regions_unsupported", SharpProofEffect.UnsupportedOperation, true);
                }
                var bytes = body.GetILBytes() ?? [];
                for (var offset = 0; offset < bytes.Length;) {
                    if (++instructionCount > MaxInstructions) {
                        MarkUnknown("metadata_instruction_budget_exhausted", SharpProofEffect.BudgetExhaustion);
                        return;
                    }
                    if (!TryRead(bytes, ref offset, out var opcode, out var operand)) {
                        MarkUnknown("malformed_il", SharpProofEffect.UnsupportedOperation);
                        return;
                    }
                    if (opcode == OpCodes.Newobj || opcode == OpCodes.Newarr || opcode == OpCodes.Box) {
                        effects |= SharpProofEffect.Allocates;
                        if (opcode == OpCodes.Newobj) {
                            var constructor = MetadataTokens.Handle(operand);
                            if (TryResolveMethod(location.Path, reader, constructor, out var constructorLocation))
                                Visit(constructorLocation, depth + 1);
                            else {
                                MarkUnknown("metadata_constructor_unresolved", exceptionBoundary: true);
                            }
                        }
                    }
                    else if (opcode == OpCodes.Throw || opcode == OpCodes.Rethrow) {
                        effects |= SharpProofEffect.Throws;
                        if (!exceptions.Contains("System.Exception", StringComparer.Ordinal))
                            exceptions.Add("System.Exception");
                    }
                    else if (opcode == OpCodes.Stsfld)
                        effects |= SharpProofEffect.WritesStaticState;
                    else if (opcode == OpCodes.Ldsfld || opcode == OpCodes.Ldsflda)
                        effects |= SharpProofEffect.ReadsStaticState;
                    else if (opcode == OpCodes.Stfld)
                        effects |= SharpProofEffect.WritesReceiverState;
                    else if (opcode == OpCodes.Ldfld || opcode == OpCodes.Ldflda)
                        effects |= SharpProofEffect.ReadsReceiverState;
                    else if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt) {
                        effects |= SharpProofEffect.DirectCall;
                        if (opcode == OpCodes.Callvirt) {
                            MarkUnknown("metadata_virtual_dispatch_unresolved", SharpProofEffect.DispatchUncertainty, true);
                        }
                        var called = MetadataTokens.Handle(operand);
                        if (opcode == OpCodes.Call &&
                            TryResolveMethod(location.Path, reader, called, out var calledLocation))
                            Visit(calledLocation, depth + 1);
                        else if (opcode == OpCodes.Call ||
                                 !TryResolveMethod(location.Path, reader, called, out _)) {
                            MarkUnknown("metadata_external_call_unresolved", exceptionBoundary: true);
                        }
                    }
                    else if (IsIndirectOrElementWrite(opcode)) {
                        MarkUnknown("metadata_indirect_write_origin_unknown",
                            SharpProofEffect.WritesArgumentState | SharpProofEffect.UnsupportedOperation, true);
                    }
                    else if (MayThrowImplicitly(opcode)) {
                        MarkUnknown("metadata_implicit_exception", exceptionBoundary: true);
                    }
                    else if (!IsModeledNoEffectOpcode(opcode)) {
                        MarkUnknown("metadata_opcode_unsupported", SharpProofEffect.UnsupportedOperation, true);
                    }
                }
            }
            finally {
                active.Remove(location);
                analyzed.Add(location);
            }
        }
        Visit(new MethodLocation(path, root), 0);
        var exceptionFacts = ImmutableArray.CreateBuilder<MethodExceptionFact>();
        exceptionFacts.AddRange(exceptions.Select(static type =>
            MethodExceptionFact.Boundary(type, MethodExceptionSource.Metadata, "metadata_throw")));
        if (hasUnknownExceptionBoundary)
            exceptionFacts.Add(MethodExceptionFact.Boundary(
                "System.Exception",
                MethodExceptionSource.Metadata,
                "metadata_exception_boundary_unknown",
                SharpProofVerdict.Unknown));
        return new MethodEffects(
            effects,
            SharpProofCapability.None,
            exceptionFacts.ToImmutable(),
            [],
            [.. unknowns.Distinct()]);
    }
    private static bool IsIndirectOrElementWrite(OpCode opcode) => opcode == OpCodes.Stind_I ||
               opcode == OpCodes.Stind_I1 || opcode == OpCodes.Stind_I2 || opcode == OpCodes.Stind_I4 ||
               opcode == OpCodes.Stind_I8 || opcode == OpCodes.Stind_R4 || opcode == OpCodes.Stind_R8 ||
               opcode == OpCodes.Stind_Ref || opcode == OpCodes.Stobj || opcode == OpCodes.Cpblk ||
               opcode == OpCodes.Initblk || opcode.Name?.StartsWith("stelem", StringComparison.Ordinal) == true;
    private static bool MayThrowImplicitly(OpCode opcode) => opcode == OpCodes.Div || opcode == OpCodes.Div_Un ||
               opcode == OpCodes.Rem || opcode == OpCodes.Rem_Un || opcode == OpCodes.Castclass ||
               opcode == OpCodes.Unbox || opcode == OpCodes.Unbox_Any || opcode == OpCodes.Ldlen ||
               opcode == OpCodes.Ldelema || opcode.Name?.StartsWith("ldelem", StringComparison.Ordinal) == true ||
               opcode.Name?.IndexOf("ovf", StringComparison.Ordinal) >= 0 ||
               opcode.Name?.StartsWith("ldind", StringComparison.Ordinal) == true;
    private static bool IsModeledNoEffectOpcode(OpCode opcode) {
        var name = opcode.Name ?? string.Empty;
        return opcode == OpCodes.Nop || opcode == OpCodes.Ret || opcode == OpCodes.Pop || opcode == OpCodes.Dup ||
               opcode == OpCodes.Add || opcode == OpCodes.Sub || opcode == OpCodes.Mul || opcode == OpCodes.Neg ||
               opcode == OpCodes.Not || opcode == OpCodes.And || opcode == OpCodes.Or || opcode == OpCodes.Xor ||
               opcode == OpCodes.Shl || opcode == OpCodes.Shr || opcode == OpCodes.Shr_Un || opcode == OpCodes.Ceq ||
               opcode == OpCodes.Cgt || opcode == OpCodes.Cgt_Un || opcode == OpCodes.Clt || opcode == OpCodes.Clt_Un ||
               opcode == OpCodes.Ldnull || opcode == OpCodes.Ldstr || opcode == OpCodes.Ldtoken ||
               name.StartsWith("ldarg", StringComparison.Ordinal) || name.StartsWith("ldloc", StringComparison.Ordinal) ||
               name.StartsWith("stloc", StringComparison.Ordinal) || name.StartsWith("ldc", StringComparison.Ordinal) ||
               name.StartsWith("br", StringComparison.Ordinal) || name.StartsWith("leave", StringComparison.Ordinal) ||
               name.StartsWith("conv", StringComparison.Ordinal) || name.StartsWith("readonly", StringComparison.Ordinal) ||
               name.StartsWith("constrained", StringComparison.Ordinal) || name.StartsWith("tail", StringComparison.Ordinal) ||
               name.StartsWith("unaligned", StringComparison.Ordinal) || name.StartsWith("volatile", StringComparison.Ordinal);
    }
    private static bool TryFindMethod(MetadataReader reader, IMethodSymbol symbol, out MethodDefinitionHandle result) {
        var wantedKey = RoslynStructuralMethodIdentity.GetCanonicalKey(symbol);
        foreach (var typeHandle in reader.TypeDefinitions) {
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var methodHandle in type.GetMethods()) {
                if (!string.Equals(EcmaStructuralMethodIdentity.GetCanonicalKey(reader, methodHandle), wantedKey,
                    StringComparison.Ordinal)) continue;
                result = methodHandle;
                return true;
            }
        }
        result = default;
        return false;
    }
    private bool TryResolveMethod(
        string currentPath,
        MetadataReader reader,
        Handle candidate,
        out MethodLocation result) {
        if (candidate.Kind == HandleKind.MethodDefinition) {
            result = new MethodLocation(currentPath, (MethodDefinitionHandle)candidate);
            return true;
        }
        if (candidate.Kind == HandleKind.MethodSpecification) {
            var specification = reader.GetMethodSpecification((MethodSpecificationHandle)candidate);
            return TryResolveMethod(currentPath, reader, specification.Method, out result);
        }
        if (candidate.Kind != HandleKind.MemberReference) {
            result = default;
            return false;
        }
        var member = reader.GetMemberReference((MemberReferenceHandle)candidate);
        if (!TryResolveContainingType(
                currentPath,
                reader,
                member.Parent,
                out var declaringPath,
                out var declaringType)) {
            result = default;
            return false;
        }
        using var declaringStream = File.OpenRead(declaringPath);
        using var declaringPe = new PEReader(declaringStream, PEStreamOptions.PrefetchMetadata);
        var declaringReader = declaringPe.GetMetadataReader();
        var wantedName = reader.GetString(member.Name);
        var wantedSignature = member.DecodeMethodSignature(new StructuralTypeProvider(), null);
        foreach (var methodHandle in declaringReader.GetTypeDefinition(declaringType).GetMethods()) {
            var definition = declaringReader.GetMethodDefinition(methodHandle);
            if (!string.Equals(declaringReader.GetString(definition.Name), wantedName, StringComparison.Ordinal) ||
                !SignaturesMatch(wantedSignature, definition.DecodeSignature(new StructuralTypeProvider(), null)))
                continue;
            result = new MethodLocation(declaringPath, methodHandle);
            return true;
        }
        result = default;
        return false;
    }
    private bool TryResolveContainingType(
        string currentPath,
        MetadataReader reader,
        EntityHandle parent,
        out string declaringPath,
        out TypeDefinitionHandle declaringType) {
        if (parent.Kind == HandleKind.TypeDefinition) {
            declaringPath = currentPath;
            declaringType = (TypeDefinitionHandle)parent;
            return true;
        }
        if (parent.Kind != HandleKind.TypeReference) {
            declaringPath = string.Empty;
            declaringType = default;
            return false;
        }
        var referenceHandle = (TypeReferenceHandle)parent;
        var reference = reader.GetTypeReference(referenceHandle);
        if (!TryResolveAssemblyPath(reader, reference.ResolutionScope, currentPath, out declaringPath)) {
            declaringType = default;
            return false;
        }
        var wantedType = EcmaStructuralMethodIdentity.GetTypeReferenceMetadataName(reader, referenceHandle);
        using var stream = File.OpenRead(declaringPath);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
        var declaringReader = pe.GetMetadataReader();
        foreach (var typeHandle in declaringReader.TypeDefinitions) {
            if (!string.Equals(
                    EcmaStructuralMethodIdentity.GetTypeDefinitionMetadataName(declaringReader, typeHandle),
                    wantedType,
                    StringComparison.Ordinal))
                continue;
            declaringType = typeHandle;
            return true;
        }
        declaringType = default;
        return false;
    }
    private bool TryResolveAssemblyPath(
        MetadataReader reader,
        EntityHandle resolutionScope,
        string currentPath,
        out string path) {
        while (resolutionScope.Kind == HandleKind.TypeReference)
            resolutionScope = reader.GetTypeReference((TypeReferenceHandle)resolutionScope).ResolutionScope;
        if (resolutionScope.Kind is HandleKind.ModuleDefinition or HandleKind.ModuleReference) {
            path = currentPath;
            return true;
        }
        if (resolutionScope.Kind != HandleKind.AssemblyReference) {
            path = string.Empty;
            return false;
        }
        var reference = reader.GetAssemblyReference((AssemblyReferenceHandle)resolutionScope);
        return _referencePaths.TryGetValue(
            AssemblyLookupKey.Create(
                reader.GetString(reference.Name),
                reference.Version,
                reference.Culture.IsNil ? string.Empty : reader.GetString(reference.Culture),
                GetPublicKeyToken(
                    reader.GetBlobBytes(reference.PublicKeyOrToken),
                    (reference.Flags & AssemblyFlags.PublicKey) != 0)),
            out path!);
    }
    private static bool SignaturesMatch(
        MethodSignature<StructuralDecodedType> left,
        MethodSignature<StructuralDecodedType> right) {
        if (left.Header.RawValue != right.Header.RawValue ||
            left.GenericParameterCount != right.GenericParameterCount ||
            left.RequiredParameterCount != right.RequiredParameterCount ||
            left.ParameterTypes.Length != right.ParameterTypes.Length ||
            !TypesMatch(left.ReturnType, right.ReturnType))
            return false;
        for (var index = 0; index < left.ParameterTypes.Length; index++)
            if (!TypesMatch(left.ParameterTypes[index], right.ParameterTypes[index]))
                return false;
        return true;
    }
    private static bool TypesMatch(StructuralDecodedType left, StructuralDecodedType right) =>
        left.IsByRef == right.IsByRef && string.Equals(left.Key, right.Key, StringComparison.Ordinal);
    private static ImmutableDictionary<AssemblyLookupKey, string> BuildReferencePaths(Compilation compilation) {
        var builder = ImmutableDictionary.CreateBuilder<AssemblyLookupKey, string>();
        foreach (var reference in compilation.References.OfType<PortableExecutableReference>()) {
            if (string.IsNullOrWhiteSpace(reference.FilePath) ||
                !File.Exists(reference.FilePath) ||
                compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;
            var identity = assembly.Identity;
            builder[AssemblyLookupKey.Create(
                identity.Name,
                identity.Version,
                identity.CultureName,
                [.. identity.PublicKeyToken])] = reference.FilePath!;
        }
        return builder.ToImmutable();
    }
    private static byte[] GetPublicKeyToken(byte[] keyOrToken, bool isFullKey) {
        if (!isFullKey || keyOrToken.Length == 0) return keyOrToken;
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(keyOrToken);
        var token = new byte[8];
        for (var index = 0; index < token.Length; index++)
            token[index] = hash[hash.Length - 1 - index];
        return token;
    }
    private static bool TryRead(byte[] bytes, ref int offset, out OpCode opcode, out int operand) {
        opcode = default;
        operand = 0;
        if (offset >= bytes.Length) return false;
        short value = bytes[offset++] == 0xFE
            ? offset < bytes.Length ? (short)(0xFE00 | bytes[offset++]) : (short)-1
            : (short)bytes[offset - 1];
        if (!OpCodesByValue.TryGetValue(value, out opcode)) return false;
        var size = OperandSize(opcode.OperandType, bytes, offset);
        if (size < 0 || offset + size > bytes.Length) return false;
        if (size == 4) operand = BitConverter.ToInt32(bytes, offset);
        offset += size;
        return true;
    }
    private static int OperandSize(OperandType type, byte[] bytes, int offset) => type switch {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod or
            OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or
            OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch when offset + 4 <= bytes.Length => 4 + BitConverter.ToInt32(bytes, offset) * 4,
        _ => -1
    };
    private static MethodEffects Unknown(string code) => new(
        SharpProofEffect.Unknown,
        SharpProofCapability.None,
        [MethodExceptionFact.Boundary(
            "System.Exception",
            MethodExceptionSource.Metadata,
            code,
            SharpProofVerdict.Unknown)],
        [],
        [Reason(code)]);
    private static SharpProofUnknownReason Reason(string code) => new("SP-EFFECT-METADATA", "Effects", code, false, false);
    private readonly record struct MethodLocation(string Path, MethodDefinitionHandle Handle);
    private readonly record struct AssemblyLookupKey(string Name, Version Version, string Culture, string PublicKeyToken) {
        internal static AssemblyLookupKey Create(
            string name,
            Version version,
            string? culture,
            byte[] publicKeyToken) =>
            new(
                name.ToUpperInvariant(),
                version,
                (culture ?? string.Empty).ToUpperInvariant(),
                Convert.ToBase64String(publicKeyToken));
    }
}
