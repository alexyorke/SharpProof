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
        var active = new HashSet<MethodAnalysisKey>();
        var analyzed = new Dictionary<MethodAnalysisKey, MetadataValueOrigin>();
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
        MetadataValueOrigin Visit(
            MethodLocation location,
            int depth,
            MetadataCallContext? suppliedContext = null) {
            if (depth > MaxDepth || analyzed.Count >= MaxMethods) {
                MarkUnknown("metadata_budget_exhausted", SharpProofEffect.BudgetExhaustion);
                return MetadataValueOrigin.Unknown;
            }
            using var stream = File.OpenRead(location.Path);
            using var pe = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
            var reader = pe.GetMetadataReader();
            var definition = reader.GetMethodDefinition(location.Handle);
            var isStatic = (definition.Attributes & MethodAttributes.Static) != 0;
            var definitionSignature = definition.DecodeSignature(new StructuralTypeProvider(), null);
            var context = suppliedContext ?? MetadataCallContext.Root(
                isStatic,
                definitionSignature.ParameterTypes.Length);
            var returnsVoid = IsVoid(definitionSignature.ReturnType);
            var analysisKey = new MethodAnalysisKey(location, context.Key);
            if (analyzed.TryGetValue(analysisKey, out var cachedReturn)) return cachedReturn;
            if (!active.Add(analysisKey)) {
                MarkUnknown("metadata_recursive_cycle");
                return MetadataValueOrigin.Unknown;
            }
            var methodReturn = MetadataValueOrigin.Unknown;
            MetadataValueOrigin? observedReturn = null;
            try {
                if ((definition.Attributes & MethodAttributes.PinvokeImpl) != 0) {
                    MarkUnknown("metadata_native_exception_boundary", SharpProofEffect.UsesNativeCode, true);
                    return methodReturn;
                }
                if (definition.RelativeVirtualAddress == 0) {
                    if (IsRuntimeDelegateConstructor(reader, location.Handle))
                        return MetadataValueOrigin.Scalar;
                    MarkUnknown("metadata_body_unavailable", exceptionBoundary: true);
                    return methodReturn;
                }
                var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
                if (body.ExceptionRegions.Length != 0) {
                    MarkUnknown("metadata_exception_regions_unsupported", SharpProofEffect.UnsupportedOperation, true);
                }
                var provenance = new MetadataProvenanceState(
                    isStatic,
                    context);
                var bytes = body.GetILBytes() ?? [];
                for (var offset = 0; offset < bytes.Length;) {
                    if (++instructionCount > MaxInstructions) {
                        MarkUnknown("metadata_instruction_budget_exhausted", SharpProofEffect.BudgetExhaustion);
                        return methodReturn;
                    }
                    if (!TryRead(bytes, ref offset, out var opcode, out var operand)) {
                        MarkUnknown("malformed_il", SharpProofEffect.UnsupportedOperation);
                        return methodReturn;
                    }
                    MetadataCallContext? invocationContext = null;
                    var invocationReturnsValue = false;
                    MetadataValueOrigin? accessOrigin;
                    if ((opcode == OpCodes.Call ||
                         opcode == OpCodes.Callvirt ||
                         opcode == OpCodes.Newobj) &&
                        TryDecodeMethodSignature(
                            reader,
                            MetadataTokens.Handle(operand),
                            out var invocationSignature)) {
                        invocationContext = provenance.ObserveInvocation(
                            invocationSignature.ParameterTypes.Length,
                            opcode != OpCodes.Newobj && invocationSignature.Header.IsInstance,
                            opcode == OpCodes.Newobj);
                        invocationReturnsValue = opcode != OpCodes.Newobj &&
                                                 !IsVoid(invocationSignature.ReturnType);
                        accessOrigin = null;
                    }
                    else if (opcode == OpCodes.Ret) {
                        if (!returnsVoid)
                            observedReturn = MergeOrigins(observedReturn, provenance.ObserveReturn());
                        else
                            provenance.ObserveReturn();
                        accessOrigin = null;
                    }
                    else {
                        accessOrigin = provenance.Observe(opcode, operand);
                    }
                    if (opcode == OpCodes.Newobj || opcode == OpCodes.Newarr || opcode == OpCodes.Box) {
                        effects |= SharpProofEffect.Allocates;
                        if (opcode == OpCodes.Newobj) {
                            var constructor = MetadataTokens.Handle(operand);
                            if (TryResolveMethod(location.Path, reader, constructor, out var constructorLocation))
                                _ = Visit(constructorLocation, depth + 1, invocationContext);
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
                    else if (opcode == OpCodes.Stfld) {
                        if (accessOrigin is MetadataValueOrigin.Fresh)
                            effects |= SharpProofEffect.WritesFreshOwnedState;
                        else if (accessOrigin is MetadataValueOrigin.Argument)
                            effects |= SharpProofEffect.WritesArgumentState;
                        else if (accessOrigin is MetadataValueOrigin.Receiver)
                            effects |= SharpProofEffect.WritesReceiverState;
                        else if (accessOrigin is not MetadataValueOrigin.Local)
                            MarkUnknown(
                                "metadata_field_write_origin_unknown",
                                SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesReceiverState,
                                true);
                        if (!IsInternalStorage(accessOrigin)) hasUnknownExceptionBoundary = true;
                    }
                    else if (opcode == OpCodes.Ldfld || opcode == OpCodes.Ldflda) {
                        if (accessOrigin is MetadataValueOrigin.Argument)
                            effects |= SharpProofEffect.ReadsArgumentState;
                        else if (accessOrigin is MetadataValueOrigin.Receiver)
                            effects |= SharpProofEffect.ReadsReceiverState;
                        else if (accessOrigin is MetadataValueOrigin.Static)
                            effects |= SharpProofEffect.ReadsStaticState;
                        else if (!IsInternalStorage(accessOrigin))
                            MarkUnknown(
                                "metadata_field_read_origin_unknown",
                                SharpProofEffect.ReadsArgumentState | SharpProofEffect.ReadsReceiverState,
                                true);
                        if (!IsInternalStorage(accessOrigin)) hasUnknownExceptionBoundary = true;
                    }
                    else if (IsElementRead(opcode)) {
                        if (accessOrigin is MetadataValueOrigin.Argument)
                            effects |= SharpProofEffect.ReadsArgumentState;
                        else if (accessOrigin is MetadataValueOrigin.Receiver)
                            effects |= SharpProofEffect.ReadsReceiverState;
                        else if (accessOrigin is MetadataValueOrigin.Static)
                            effects |= SharpProofEffect.ReadsStaticState;
                        else if (!IsInternalStorage(accessOrigin))
                            MarkUnknown(
                                "metadata_element_read_origin_unknown",
                                SharpProofEffect.ReadsArgumentState | SharpProofEffect.ReadsReceiverState,
                                true);
                        hasUnknownExceptionBoundary = true;
                    }
                    else if (IsIndirectRead(opcode)) {
                        if (accessOrigin is MetadataValueOrigin.Argument)
                            effects |= SharpProofEffect.ReadsArgumentState;
                        else if (accessOrigin is MetadataValueOrigin.Receiver)
                            effects |= SharpProofEffect.ReadsReceiverState;
                        else if (accessOrigin is MetadataValueOrigin.Static)
                            effects |= SharpProofEffect.ReadsStaticState;
                        else if (!IsInternalStorage(accessOrigin))
                            MarkUnknown(
                                "metadata_indirect_read_origin_unknown",
                                SharpProofEffect.ReadsArgumentState | SharpProofEffect.ReadsReceiverState,
                                true);
                        if (!IsInternalStorage(accessOrigin)) hasUnknownExceptionBoundary = true;
                    }
                    else if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt) {
                        effects |= SharpProofEffect.DirectCall;
                        if (opcode == OpCodes.Callvirt) {
                            MarkUnknown("metadata_virtual_dispatch_unresolved", SharpProofEffect.DispatchUncertainty, true);
                        }
                        var called = MetadataTokens.Handle(operand);
                        if (opcode == OpCodes.Call &&
                            TryResolveMethod(location.Path, reader, called, out var calledLocation)) {
                            var calledReturn = Visit(calledLocation, depth + 1, invocationContext);
                            if (invocationReturnsValue) provenance.PushInvocationReturn(calledReturn);
                        }
                        else if (opcode == OpCodes.Call ||
                                 !TryResolveMethod(location.Path, reader, called, out _)) {
                            if (invocationReturnsValue)
                                provenance.PushInvocationReturn(MetadataValueOrigin.Unknown);
                            MarkUnknown("metadata_external_call_unresolved", exceptionBoundary: true);
                        }
                        else if (invocationReturnsValue)
                            provenance.PushInvocationReturn(MetadataValueOrigin.Unknown);
                    }
                    else if (IsIndirectOrElementWrite(opcode)) {
                        if (accessOrigin is MetadataValueOrigin.Fresh)
                            effects |= SharpProofEffect.WritesFreshOwnedState;
                        else if (accessOrigin is MetadataValueOrigin.Argument)
                            effects |= SharpProofEffect.WritesArgumentState;
                        else if (accessOrigin is MetadataValueOrigin.Receiver)
                            effects |= SharpProofEffect.WritesReceiverState;
                        else if (accessOrigin is MetadataValueOrigin.Static)
                            effects |= SharpProofEffect.WritesStaticState;
                        else if (accessOrigin is not MetadataValueOrigin.Local)
                            MarkUnknown("metadata_indirect_write_origin_unknown",
                                SharpProofEffect.WritesArgumentState | SharpProofEffect.UnsupportedOperation, true);
                        if (IsElementWrite(opcode) ||
                            (IsIndirectWrite(opcode) && !IsInternalStorage(accessOrigin)))
                            hasUnknownExceptionBoundary = true;
                    }
                    else if (IsArithmeticExceptionOnly(opcode)) {
                        hasUnknownExceptionBoundary = true;
                    }
                    else if (opcode == OpCodes.Castclass) {
                        hasUnknownExceptionBoundary = true;
                    }
                    else if (opcode == OpCodes.Unbox || opcode == OpCodes.Unbox_Any) {
                        hasUnknownExceptionBoundary = true;
                    }
                    else if (opcode == OpCodes.Ldvirtftn) {
                        hasUnknownExceptionBoundary = true;
                    }
                    else if (MayThrowImplicitly(opcode)) {
                        MarkUnknown("metadata_implicit_exception", exceptionBoundary: true);
                    }
                    else if (!IsModeledNoEffectOpcode(opcode)) {
                        MarkUnknown("metadata_opcode_unsupported", SharpProofEffect.UnsupportedOperation, true);
                    }
                }
                methodReturn = returnsVoid
                    ? MetadataValueOrigin.Scalar
                    : observedReturn ?? MetadataValueOrigin.Unknown;
                return methodReturn;
            }
            finally {
                active.Remove(analysisKey);
                analyzed[analysisKey] = methodReturn;
            }
        }
        _ = Visit(new MethodLocation(path, root), 0);
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
               opcode == OpCodes.Stind_Ref || opcode == OpCodes.Stobj || opcode == OpCodes.Initobj ||
               opcode == OpCodes.Cpblk ||
               opcode == OpCodes.Initblk || opcode.Name?.StartsWith("stelem", StringComparison.Ordinal) == true;
    private static bool IsElementWrite(OpCode opcode) =>
        opcode.Name?.StartsWith("stelem", StringComparison.Ordinal) == true;
    private static bool IsIndirectWrite(OpCode opcode) =>
        opcode == OpCodes.Stind_I ||
        opcode == OpCodes.Stind_I1 ||
        opcode == OpCodes.Stind_I2 ||
        opcode == OpCodes.Stind_I4 ||
        opcode == OpCodes.Stind_I8 ||
        opcode == OpCodes.Stind_R4 ||
        opcode == OpCodes.Stind_R8 ||
        opcode == OpCodes.Stind_Ref ||
        opcode == OpCodes.Stobj ||
        opcode == OpCodes.Initobj;
    private static bool IsInternalStorage(MetadataValueOrigin? origin) =>
        origin is MetadataValueOrigin.Fresh or MetadataValueOrigin.Local;
    private static bool IsElementRead(OpCode opcode) =>
        opcode == OpCodes.Ldlen ||
        opcode == OpCodes.Ldelema ||
        opcode.Name?.StartsWith("ldelem", StringComparison.Ordinal) == true;
    private static bool IsIndirectRead(OpCode opcode) =>
        opcode == OpCodes.Ldobj ||
        opcode.Name?.StartsWith("ldind", StringComparison.Ordinal) == true;
    private static bool IsArithmeticExceptionOnly(OpCode opcode) =>
        opcode == OpCodes.Div ||
        opcode == OpCodes.Div_Un ||
        opcode == OpCodes.Rem ||
        opcode == OpCodes.Rem_Un ||
        opcode.Name?.IndexOf("ovf", StringComparison.Ordinal) >= 0;
    private static bool MayThrowImplicitly(OpCode opcode) =>
               opcode == OpCodes.Ldlen ||
               opcode == OpCodes.Ldelema || opcode.Name?.StartsWith("ldelem", StringComparison.Ordinal) == true ||
               opcode.Name?.StartsWith("ldind", StringComparison.Ordinal) == true;
    private static bool IsModeledNoEffectOpcode(OpCode opcode) {
        var name = opcode.Name ?? string.Empty;
        return opcode == OpCodes.Nop || opcode == OpCodes.Ret || opcode == OpCodes.Pop || opcode == OpCodes.Dup ||
               opcode == OpCodes.Isinst || opcode == OpCodes.Sizeof ||
               opcode == OpCodes.Ldftn || opcode == OpCodes.Ldvirtftn ||
               opcode == OpCodes.Add || opcode == OpCodes.Sub || opcode == OpCodes.Mul || opcode == OpCodes.Neg ||
               opcode == OpCodes.Not || opcode == OpCodes.And || opcode == OpCodes.Or || opcode == OpCodes.Xor ||
               opcode == OpCodes.Shl || opcode == OpCodes.Shr || opcode == OpCodes.Shr_Un || opcode == OpCodes.Ceq ||
               opcode == OpCodes.Cgt || opcode == OpCodes.Cgt_Un || opcode == OpCodes.Clt || opcode == OpCodes.Clt_Un ||
               opcode == OpCodes.Ldnull || opcode == OpCodes.Ldstr || opcode == OpCodes.Ldtoken ||
               name.StartsWith("ldarg", StringComparison.Ordinal) || name.StartsWith("ldloc", StringComparison.Ordinal) ||
               name.StartsWith("starg", StringComparison.Ordinal) || name.StartsWith("stloc", StringComparison.Ordinal) ||
               name.StartsWith("ldc", StringComparison.Ordinal) ||
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
                out var declaringType,
                out var typeArguments)) {
            result = default;
            return false;
        }
        using var declaringStream = File.OpenRead(declaringPath);
        using var declaringPe = new PEReader(declaringStream, PEStreamOptions.PrefetchMetadata);
        var declaringReader = declaringPe.GetMetadataReader();
        var wantedName = reader.GetString(member.Name);
        var genericContext = new StructuralGenericContext(typeArguments, []);
        var wantedSignature = member.DecodeMethodSignature(new StructuralTypeProvider(), genericContext);
        foreach (var methodHandle in declaringReader.GetTypeDefinition(declaringType).GetMethods()) {
            var definition = declaringReader.GetMethodDefinition(methodHandle);
            if (!string.Equals(declaringReader.GetString(definition.Name), wantedName, StringComparison.Ordinal) ||
                !SignaturesMatch(
                    wantedSignature,
                    definition.DecodeSignature(
                        new StructuralTypeProvider(),
                        genericContext)))
                continue;
            result = new MethodLocation(declaringPath, methodHandle);
            return true;
        }
        result = default;
        return false;
    }
    private static bool TryDecodeMethodSignature(
        MetadataReader reader,
        Handle candidate,
        out MethodSignature<StructuralDecodedType> signature) {
        if (candidate.Kind == HandleKind.MethodSpecification) {
            var specification = reader.GetMethodSpecification((MethodSpecificationHandle)candidate);
            return TryDecodeMethodSignature(reader, specification.Method, out signature);
        }
        if (candidate.Kind == HandleKind.MethodDefinition) {
            signature = reader.GetMethodDefinition((MethodDefinitionHandle)candidate)
                .DecodeSignature(new StructuralTypeProvider(), null);
            return true;
        }
        if (candidate.Kind == HandleKind.MemberReference) {
            signature = reader.GetMemberReference((MemberReferenceHandle)candidate)
                .DecodeMethodSignature(new StructuralTypeProvider(), null);
            return true;
        }
        signature = default;
        return false;
    }
    private static bool IsVoid(StructuralDecodedType type) =>
        string.Equals(type.Key, "named:System.Void", StringComparison.Ordinal);
    private static bool IsRuntimeDelegateConstructor(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle) {
        var method = reader.GetMethodDefinition(methodHandle);
        if (!string.Equals(reader.GetString(method.Name), ".ctor", StringComparison.Ordinal))
            return false;
        var declaringType = reader.GetTypeDefinition(method.GetDeclaringType());
        return IsNamedType(reader, declaringType.BaseType, "System", "MulticastDelegate");
    }
    private static bool IsNamedType(
        MetadataReader reader,
        EntityHandle handle,
        string @namespace,
        string name) {
        StringHandle namespaceHandle;
        StringHandle nameHandle;
        if (handle.Kind == HandleKind.TypeDefinition) {
            var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            namespaceHandle = type.Namespace;
            nameHandle = type.Name;
        }
        else if (handle.Kind == HandleKind.TypeReference) {
            var type = reader.GetTypeReference((TypeReferenceHandle)handle);
            namespaceHandle = type.Namespace;
            nameHandle = type.Name;
        }
        else {
            return false;
        }
        return string.Equals(reader.GetString(namespaceHandle), @namespace, StringComparison.Ordinal) &&
               string.Equals(reader.GetString(nameHandle), name, StringComparison.Ordinal);
    }
    private static MetadataValueOrigin MergeOrigins(
        MetadataValueOrigin? current,
        MetadataValueOrigin next) =>
        current == null || current == next ? next : MetadataValueOrigin.Unknown;
    private bool TryResolveContainingType(
        string currentPath,
        MetadataReader reader,
        EntityHandle parent,
        out string declaringPath,
        out TypeDefinitionHandle declaringType,
        out ImmutableArray<StructuralDecodedType> typeArguments) {
        if (parent.Kind == HandleKind.TypeDefinition) {
            declaringPath = currentPath;
            declaringType = (TypeDefinitionHandle)parent;
            typeArguments = [];
            return true;
        }
        if (parent.Kind == HandleKind.TypeSpecification) {
            var decoded = reader.GetTypeSpecification((TypeSpecificationHandle)parent)
                .DecodeSignature(new DeclaringTypeProvider(), null);
            if (decoded.Definition.IsNil)
                return FailContainingType(out declaringPath, out declaringType, out typeArguments);
            if (!TryResolveContainingType(
                    currentPath,
                    reader,
                    decoded.Definition,
                    out declaringPath,
                    out declaringType,
                    out _))
                return FailContainingType(out declaringPath, out declaringType, out typeArguments);
            typeArguments = decoded.TypeArguments;
            return true;
        }
        if (parent.Kind != HandleKind.TypeReference) {
            return FailContainingType(out declaringPath, out declaringType, out typeArguments);
        }
        var referenceHandle = (TypeReferenceHandle)parent;
        var reference = reader.GetTypeReference(referenceHandle);
        if (!TryResolveAssemblyPath(reader, reference.ResolutionScope, currentPath, out declaringPath)) {
            declaringType = default;
            typeArguments = [];
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
            typeArguments = [];
            return true;
        }
        declaringType = default;
        typeArguments = [];
        return false;
    }
    private static bool FailContainingType(
        out string declaringPath,
        out TypeDefinitionHandle declaringType,
        out ImmutableArray<StructuralDecodedType> typeArguments) {
        declaringPath = string.Empty;
        declaringType = default;
        typeArguments = [];
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
        if (size == 1) operand = bytes[offset];
        else if (size == 2) operand = BitConverter.ToUInt16(bytes, offset);
        else if (size == 4) operand = BitConverter.ToInt32(bytes, offset);
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
    private readonly record struct DecodedDeclaringType(
        EntityHandle Definition,
        StructuralDecodedType StructuralType,
        ImmutableArray<StructuralDecodedType> TypeArguments);
    private sealed class DeclaringTypeProvider : ISignatureTypeProvider<DecodedDeclaringType, object?> {
        private static readonly StructuralTypeProvider Structural = new();
        public DecodedDeclaringType GetArrayType(DecodedDeclaringType elementType, ArrayShape shape) =>
            Type(Structural.GetArrayType(elementType.StructuralType, shape));
        public DecodedDeclaringType GetByReferenceType(DecodedDeclaringType elementType) =>
            Type(Structural.GetByReferenceType(elementType.StructuralType));
        public DecodedDeclaringType GetFunctionPointerType(MethodSignature<DecodedDeclaringType> signature) =>
            Type(new StructuralDecodedType("unsupported:function-pointer"));
        public DecodedDeclaringType GetGenericInstantiation(
            DecodedDeclaringType genericType,
            ImmutableArray<DecodedDeclaringType> typeArguments) {
            ImmutableArray<StructuralDecodedType> arguments =
                [.. typeArguments.Select(static argument => argument.StructuralType)];
            return new(
                genericType.Definition,
                Structural.GetGenericInstantiation(genericType.StructuralType, arguments),
                arguments);
        }
        public DecodedDeclaringType GetGenericMethodParameter(object? genericContext, int index) =>
            Type(Structural.GetGenericMethodParameter(genericContext, index));
        public DecodedDeclaringType GetGenericTypeParameter(object? genericContext, int index) =>
            Type(Structural.GetGenericTypeParameter(genericContext, index));
        public DecodedDeclaringType GetModifiedType(
            DecodedDeclaringType modifier,
            DecodedDeclaringType unmodifiedType,
            bool isRequired) =>
            Type(Structural.GetModifiedType(
                modifier.StructuralType,
                unmodifiedType.StructuralType,
                isRequired));
        public DecodedDeclaringType GetPinnedType(DecodedDeclaringType elementType) =>
            Type(Structural.GetPinnedType(elementType.StructuralType));
        public DecodedDeclaringType GetPointerType(DecodedDeclaringType elementType) =>
            Type(Structural.GetPointerType(elementType.StructuralType));
        public DecodedDeclaringType GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            Type(Structural.GetPrimitiveType(typeCode));
        public DecodedDeclaringType GetSZArrayType(DecodedDeclaringType elementType) =>
            Type(Structural.GetSZArrayType(elementType.StructuralType));
        public DecodedDeclaringType GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind) =>
            new(
                handle,
                Structural.GetTypeFromDefinition(reader, handle, rawTypeKind),
                []);
        public DecodedDeclaringType GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind) =>
            new(
                handle,
                Structural.GetTypeFromReference(reader, handle, rawTypeKind),
                []);
        public DecodedDeclaringType GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        private static DecodedDeclaringType Type(StructuralDecodedType type) => new(default, type, []);
    }
    private enum MetadataValueOrigin { Scalar, Receiver, Argument, Fresh, Local, Static, Unknown }
    private readonly record struct MethodAnalysisKey(MethodLocation Location, string Context);
    private readonly record struct MetadataCallContext(
        MetadataValueOrigin Receiver,
        ImmutableArray<MetadataValueOrigin> Arguments) {
        internal string Key => ((int)Receiver).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                               string.Join(",", Arguments.Select(static origin =>
                                   ((int)origin).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        internal static MetadataCallContext Root(bool isStatic, int parameterCount) => new(
            isStatic ? MetadataValueOrigin.Unknown : MetadataValueOrigin.Receiver,
            [.. Enumerable.Repeat(MetadataValueOrigin.Argument, parameterCount)]);
    }
    private sealed class MetadataProvenanceState {
        private readonly bool _isStatic;
        private readonly MetadataCallContext _context;
        private readonly List<MetadataValueOrigin> _stack = [];
        private readonly Dictionary<int, MetadataValueOrigin> _arguments = [];
        private readonly Dictionary<int, MetadataValueOrigin> _locals = [];
        private bool _hasControlFlowBranch;
        internal MetadataProvenanceState(bool isStatic, MetadataCallContext context) {
            _isStatic = isStatic;
            _context = context;
        }
        internal MetadataCallContext ObserveInvocation(
            int parameterCount,
            bool hasReceiver,
            bool createsFreshReceiver) {
            var arguments = new MetadataValueOrigin[parameterCount];
            for (var index = parameterCount - 1; index >= 0; index--) arguments[index] = Pop();
            var receiver = createsFreshReceiver
                ? MetadataValueOrigin.Fresh
                : hasReceiver
                    ? Pop()
                    : MetadataValueOrigin.Unknown;
            if (createsFreshReceiver) Push(MetadataValueOrigin.Fresh);
            return new MetadataCallContext(receiver, [.. arguments]);
        }
        internal void PushInvocationReturn(MetadataValueOrigin origin) => Push(origin);
        internal MetadataValueOrigin ObserveReturn() {
            var origin = Pop();
            ResetStack();
            return origin;
        }
        internal MetadataValueOrigin? Observe(OpCode opcode, int operand) {
            if (TryArgumentIndex(opcode, operand, out var argumentIndex)) {
                Push(ArgumentOrigin(argumentIndex));
                return null;
            }
            if (TryArgumentAddressIndex(opcode, operand, out argumentIndex)) {
                Push(ArgumentOrigin(argumentIndex));
                return null;
            }
            if (TryArgumentStoreIndex(opcode, operand, out argumentIndex)) {
                var origin = Pop();
                _arguments[argumentIndex] = _hasControlFlowBranch
                    ? MetadataValueOrigin.Unknown
                    : origin;
                return null;
            }
            if (TryLocalLoadIndex(opcode, operand, out var localIndex)) {
                Push(_locals.TryGetValue(localIndex, out var local) ? local : MetadataValueOrigin.Unknown);
                return null;
            }
            if (TryLocalStoreIndex(opcode, operand, out localIndex)) {
                var origin = Pop();
                _locals[localIndex] = _hasControlFlowBranch
                    ? MetadataValueOrigin.Unknown
                    : origin;
                return null;
            }
            if (TryLocalAddressIndex(opcode, operand, out localIndex)) {
                Push(MetadataValueOrigin.Local);
                return null;
            }
            if (opcode == OpCodes.Dup) {
                Push(Peek());
                return null;
            }
            if (opcode == OpCodes.Pop) {
                Pop();
                return null;
            }
            if (opcode == OpCodes.Newarr) {
                Pop();
                Push(MetadataValueOrigin.Fresh);
                return null;
            }
            if (opcode == OpCodes.Newobj) {
                ResetStack();
                Push(MetadataValueOrigin.Fresh);
                return null;
            }
            if (opcode == OpCodes.Box) {
                Pop();
                Push(MetadataValueOrigin.Fresh);
                return null;
            }
            if (IsElementWrite(opcode)) {
                Pop();
                Pop();
                return Pop();
            }
            if (opcode == OpCodes.Initobj) {
                return Pop();
            }
            if (IsIndirectWrite(opcode)) {
                Pop();
                return Pop();
            }
            if (opcode == OpCodes.Stfld) {
                Pop();
                return Pop();
            }
            if (opcode == OpCodes.Ldflda) {
                var receiver = Pop();
                Push(receiver);
                return receiver;
            }
            if (opcode == OpCodes.Ldfld) {
                var receiver = Pop();
                Push(MetadataValueOrigin.Unknown);
                return receiver;
            }
            if (opcode == OpCodes.Ldelema) {
                Pop();
                var array = Pop();
                Push(array);
                return array;
            }
            if (opcode.Name?.StartsWith("ldelem", StringComparison.Ordinal) == true) {
                Pop();
                var array = Pop();
                Push(MetadataValueOrigin.Unknown);
                return array;
            }
            if (opcode == OpCodes.Ldlen) {
                var array = Pop();
                Push(MetadataValueOrigin.Scalar);
                return array;
            }
            if (IsIndirectRead(opcode)) {
                var address = Pop();
                Push(MetadataValueOrigin.Unknown);
                return address;
            }
            if (opcode == OpCodes.Stsfld) {
                Pop();
                return MetadataValueOrigin.Static;
            }
            if (opcode == OpCodes.Ldsflda) {
                Push(MetadataValueOrigin.Static);
                return null;
            }
            if (opcode == OpCodes.Ldsfld) {
                Push(MetadataValueOrigin.Static);
                return null;
            }
            if (opcode.Name?.StartsWith("ldc", StringComparison.Ordinal) == true) {
                Push(MetadataValueOrigin.Scalar);
                return null;
            }
            if (opcode == OpCodes.Sizeof) {
                Push(MetadataValueOrigin.Scalar);
                return null;
            }
            if (opcode == OpCodes.Ldftn) {
                Push(MetadataValueOrigin.Scalar);
                return null;
            }
            if (opcode == OpCodes.Ldvirtftn) {
                Pop();
                Push(MetadataValueOrigin.Scalar);
                return null;
            }
            if (opcode == OpCodes.Ldnull || opcode == OpCodes.Ldstr || opcode == OpCodes.Ldtoken) {
                Push(MetadataValueOrigin.Scalar);
                return null;
            }
            if (IsBinaryValueOperation(opcode)) {
                Pop();
                Pop();
                Push(MetadataValueOrigin.Scalar);
                return null;
            }
            if (IsUnaryValueOperation(opcode)) {
                Pop();
                Push(MetadataValueOrigin.Scalar);
                return null;
            }
            if (opcode == OpCodes.Castclass || opcode == OpCodes.Isinst) {
                var origin = Pop();
                Push(origin);
                return null;
            }
            if (opcode == OpCodes.Unbox) {
                var origin = Pop();
                Push(origin);
                return null;
            }
            if (opcode == OpCodes.Unbox_Any) {
                Pop();
                Push(MetadataValueOrigin.Unknown);
                return null;
            }
            if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt || opcode == OpCodes.Calli) {
                ResetStack();
                Push(MetadataValueOrigin.Unknown);
                return null;
            }
            if (opcode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch) {
                _hasControlFlowBranch = true;
                ResetStack();
                foreach (var index in _arguments.Keys.ToArray())
                    _arguments[index] = MetadataValueOrigin.Unknown;
                foreach (var index in _locals.Keys.ToArray()) _locals[index] = MetadataValueOrigin.Unknown;
                return null;
            }
            if (opcode == OpCodes.Ret || opcode == OpCodes.Throw || opcode == OpCodes.Rethrow) {
                ResetStack();
                return null;
            }
            return null;
        }
        private MetadataValueOrigin ArgumentOrigin(int ilIndex) {
            if (_arguments.TryGetValue(ilIndex, out var assigned)) return assigned;
            if (!_isStatic && ilIndex == 0) return _context.Receiver;
            var parameterIndex = _isStatic ? ilIndex : ilIndex - 1;
            return parameterIndex >= 0 && parameterIndex < _context.Arguments.Length
                ? _context.Arguments[parameterIndex]
                : MetadataValueOrigin.Unknown;
        }
        private MetadataValueOrigin Pop() {
            if (_stack.Count == 0) return MetadataValueOrigin.Unknown;
            var index = _stack.Count - 1;
            var value = _stack[index];
            _stack.RemoveAt(index);
            return value;
        }
        private MetadataValueOrigin Peek() =>
            _stack.Count == 0 ? MetadataValueOrigin.Unknown : _stack[_stack.Count - 1];
        private void Push(MetadataValueOrigin value) => _stack.Add(value);
        private void ResetStack() => _stack.Clear();
        private static bool TryArgumentIndex(OpCode opcode, int operand, out int index) =>
            TryVariableIndex(
                opcode,
                operand,
                OpCodes.Ldarg_0,
                OpCodes.Ldarg_1,
                OpCodes.Ldarg_2,
                OpCodes.Ldarg_3,
                OpCodes.Ldarg_S,
                OpCodes.Ldarg,
                out index);
        private static bool TryArgumentAddressIndex(OpCode opcode, int operand, out int index) =>
            TryVariableIndex(
                opcode,
                operand,
                default,
                default,
                default,
                default,
                OpCodes.Ldarga_S,
                OpCodes.Ldarga,
                out index);
        private static bool TryArgumentStoreIndex(OpCode opcode, int operand, out int index) =>
            TryVariableIndex(
                opcode,
                operand,
                default,
                default,
                default,
                default,
                OpCodes.Starg_S,
                OpCodes.Starg,
                out index);
        private static bool TryLocalLoadIndex(OpCode opcode, int operand, out int index) =>
            TryVariableIndex(
                opcode,
                operand,
                OpCodes.Ldloc_0,
                OpCodes.Ldloc_1,
                OpCodes.Ldloc_2,
                OpCodes.Ldloc_3,
                OpCodes.Ldloc_S,
                OpCodes.Ldloc,
                out index);
        private static bool TryLocalStoreIndex(OpCode opcode, int operand, out int index) =>
            TryVariableIndex(
                opcode,
                operand,
                OpCodes.Stloc_0,
                OpCodes.Stloc_1,
                OpCodes.Stloc_2,
                OpCodes.Stloc_3,
                OpCodes.Stloc_S,
                OpCodes.Stloc,
                out index);
        private static bool TryLocalAddressIndex(OpCode opcode, int operand, out int index) =>
            TryVariableIndex(
                opcode,
                operand,
                default,
                default,
                default,
                default,
                OpCodes.Ldloca_S,
                OpCodes.Ldloca,
                out index);
        private static bool TryVariableIndex(
            OpCode opcode,
            int operand,
            OpCode zero,
            OpCode one,
            OpCode two,
            OpCode three,
            OpCode shortForm,
            OpCode longForm,
            out int index) {
            if (opcode == zero && zero.Size != 0) index = 0;
            else if (opcode == one && one.Size != 0) index = 1;
            else if (opcode == two && two.Size != 0) index = 2;
            else if (opcode == three && three.Size != 0) index = 3;
            else if (opcode == shortForm || opcode == longForm) index = operand;
            else {
                index = -1;
                return false;
            }
            return true;
        }
        private static bool IsBinaryValueOperation(OpCode opcode) =>
            opcode == OpCodes.Add || opcode == OpCodes.Sub || opcode == OpCodes.Mul ||
            opcode == OpCodes.Add_Ovf || opcode == OpCodes.Add_Ovf_Un ||
            opcode == OpCodes.Sub_Ovf || opcode == OpCodes.Sub_Ovf_Un ||
            opcode == OpCodes.Mul_Ovf || opcode == OpCodes.Mul_Ovf_Un ||
            opcode == OpCodes.Div || opcode == OpCodes.Div_Un || opcode == OpCodes.Rem ||
            opcode == OpCodes.Rem_Un || opcode == OpCodes.And || opcode == OpCodes.Or ||
            opcode == OpCodes.Xor || opcode == OpCodes.Shl || opcode == OpCodes.Shr ||
            opcode == OpCodes.Shr_Un || opcode == OpCodes.Ceq || opcode == OpCodes.Cgt ||
            opcode == OpCodes.Cgt_Un || opcode == OpCodes.Clt || opcode == OpCodes.Clt_Un;
        private static bool IsUnaryValueOperation(OpCode opcode) =>
            opcode == OpCodes.Neg || opcode == OpCodes.Not ||
            opcode.Name?.StartsWith("conv", StringComparison.Ordinal) == true;
    }
}
