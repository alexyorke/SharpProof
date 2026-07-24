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
    private static readonly ImmutableDictionary<short, MetadataOpcodeModel> OpcodeModels = BuildOpcodeModels();
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
        void ApplyAccess(
            MetadataValueOrigin? origin,
            bool write,
            bool staticAllowed,
            string unknownReason,
            SharpProofEffect unknownEffects) {
            var knownEffect = origin switch {
                MetadataValueOrigin.Argument => write
                    ? SharpProofEffect.WritesArgumentState
                    : SharpProofEffect.ReadsArgumentState,
                MetadataValueOrigin.Receiver => write
                    ? SharpProofEffect.WritesReceiverState
                    : SharpProofEffect.ReadsReceiverState,
                MetadataValueOrigin.Static when staticAllowed => write
                    ? SharpProofEffect.WritesStaticState
                    : SharpProofEffect.ReadsStaticState,
                MetadataValueOrigin.Fresh when write => SharpProofEffect.WritesFreshOwnedState,
                _ => SharpProofEffect.None
            };
            if (knownEffect != SharpProofEffect.None)
                effects |= knownEffect;
            else if (!IsInternalStorage(origin))
                MarkUnknown(unknownReason, unknownEffects, true);
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
                    context,
                    [.. definitionSignature.ParameterTypes.Select(
                        static type => type.IsValueType && !type.IsByRef)]);
                void VisitDeclaringInitializer(MethodLocation member, bool requireStaticMember) {
                    if (TryFindDeclaringTypeInitializer(member, requireStaticMember, out var initializer) &&
                        !active.Any(key => key.Location == initializer))
                        _ = Visit(initializer, depth + 1);
                }
                var bytes = body.GetILBytes() ?? [];
                for (var offset = 0; offset < bytes.Length;) {
                    if (++instructionCount > MaxInstructions) {
                        MarkUnknown("metadata_instruction_budget_exhausted", SharpProofEffect.BudgetExhaustion);
                        return methodReturn;
                    }
                    if (!TryRead(bytes, ref offset, out var instruction, out var operand)) {
                        MarkUnknown("malformed_il", SharpProofEffect.UnsupportedOperation);
                        return methodReturn;
                    }
                    MetadataCallContext? invocationContext = null;
                    var invocationReturnsValue = false;
                    MetadataValueOrigin? accessOrigin;
                    if (instruction.Effect is MetadataEffectAction.Call or MetadataEffectAction.Construct &&
                        TryDecodeMethodSignature(
                            reader,
                            MetadataTokens.Handle(operand),
                            out var invocationSignature)) {
                        invocationContext = provenance.ObserveInvocation(
                            invocationSignature.ParameterTypes.Length,
                            instruction.Effect != MetadataEffectAction.Construct &&
                            invocationSignature.Header.IsInstance,
                            instruction.Effect == MetadataEffectAction.Construct);
                        invocationReturnsValue = instruction.Effect != MetadataEffectAction.Construct &&
                                                 !IsVoid(invocationSignature.ReturnType);
                        accessOrigin = null;
                    }
                    else if (instruction.Code == OpCodes.Ret) {
                        if (!returnsVoid)
                            observedReturn = MergeOrigins(observedReturn, provenance.ObserveReturn());
                        else
                            provenance.ObserveReturn();
                        accessOrigin = null;
                    }
                    else {
                        accessOrigin = provenance.Observe(instruction, operand);
                    }
                    if (instruction.Effect is MetadataEffectAction.ReadStatic or MetadataEffectAction.WriteStatic) {
                        var field = MetadataTokens.Handle(operand);
                        if (!TryResolveFieldDeclaringType(
                                location.Path,
                                reader,
                                field,
                                out var fieldPath,
                                out var fieldType)) {
                            MarkUnknown("metadata_static_initializer_unresolved", exceptionBoundary: true);
                        }
                        else if (TryFindTypeInitializer(fieldPath, fieldType, out var initializer) &&
                                 !active.Any(key => key.Location == initializer)) {
                            _ = Visit(initializer, depth + 1);
                        }
                    }
                    switch (instruction.Effect) {
                        case MetadataEffectAction.None:
                            break;
                        case MetadataEffectAction.Synchronize:
                            effects |= SharpProofEffect.Synchronizes;
                            break;
                        case MetadataEffectAction.Allocate:
                            effects |= SharpProofEffect.Allocates;
                            break;
                        case MetadataEffectAction.Construct:
                            var constructor = MetadataTokens.Handle(operand);
                            if (TryResolveMethod(location.Path, reader, constructor, out var constructorLocation)) {
                                VisitDeclaringInitializer(constructorLocation, requireStaticMember: false);
                                if (!IsValueTypeConstructor(constructorLocation))
                                    effects |= SharpProofEffect.Allocates;
                                _ = Visit(constructorLocation, depth + 1, invocationContext);
                            }
                            else {
                                effects |= SharpProofEffect.Allocates;
                                MarkUnknown("metadata_constructor_unresolved", exceptionBoundary: true);
                            }
                            break;
                        case MetadataEffectAction.Throw:
                            effects |= SharpProofEffect.Throws;
                            if (!exceptions.Contains("System.Exception", StringComparer.Ordinal))
                                exceptions.Add("System.Exception");
                            break;
                        case MetadataEffectAction.WriteStatic:
                            effects |= SharpProofEffect.WritesStaticState;
                            break;
                        case MetadataEffectAction.ReadStatic:
                            effects |= SharpProofEffect.ReadsStaticState;
                            break;
                        case MetadataEffectAction.WriteField:
                            ApplyAccess(
                                accessOrigin,
                                write: true,
                                staticAllowed: false,
                                "metadata_field_write_origin_unknown",
                                SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesReceiverState);
                            hasUnknownExceptionBoundary |= !IsInternalStorage(accessOrigin);
                            break;
                        case MetadataEffectAction.ReadField:
                        case MetadataEffectAction.ReadElement:
                        case MetadataEffectAction.ReadIndirect:
                            var elementRead = instruction.Effect == MetadataEffectAction.ReadElement;
                            ApplyAccess(
                                accessOrigin,
                                write: false,
                                staticAllowed: true,
                                instruction.Effect switch {
                                    MetadataEffectAction.ReadField => "metadata_field_read_origin_unknown",
                                    MetadataEffectAction.ReadElement => "metadata_element_read_origin_unknown",
                                    _ => "metadata_indirect_read_origin_unknown"
                                },
                                SharpProofEffect.ReadsArgumentState | SharpProofEffect.ReadsReceiverState);
                            hasUnknownExceptionBoundary |= elementRead || !IsInternalStorage(accessOrigin);
                            break;
                        case MetadataEffectAction.Call:
                            effects |= SharpProofEffect.DirectCall;
                            var virtualCall = instruction.Code == OpCodes.Callvirt;
                            if (virtualCall)
                                MarkUnknown(
                                    "metadata_virtual_dispatch_unresolved",
                                    SharpProofEffect.DispatchUncertainty,
                                    true);
                            var called = MetadataTokens.Handle(operand);
                            var resolved = TryResolveMethod(
                                location.Path,
                                reader,
                                called,
                                out var calledLocation);
                            if (!virtualCall && resolved) {
                                VisitDeclaringInitializer(calledLocation, requireStaticMember: true);
                                var calledReturn = Visit(calledLocation, depth + 1, invocationContext);
                                if (invocationReturnsValue)
                                    provenance.PushInvocationReturn(calledReturn);
                            }
                            else {
                                if (invocationReturnsValue)
                                    provenance.PushInvocationReturn(MetadataValueOrigin.Unknown);
                                if (!resolved || !virtualCall)
                                    MarkUnknown("metadata_external_call_unresolved", exceptionBoundary: true);
                            }
                            break;
                        case MetadataEffectAction.WriteElement:
                        case MetadataEffectAction.WriteIndirect:
                            ApplyAccess(
                                accessOrigin,
                                write: true,
                                staticAllowed: true,
                                "metadata_indirect_write_origin_unknown",
                                SharpProofEffect.WritesArgumentState | SharpProofEffect.UnsupportedOperation);
                            hasUnknownExceptionBoundary |=
                                instruction.Effect == MetadataEffectAction.WriteElement ||
                                !IsInternalStorage(accessOrigin);
                            break;
                        case MetadataEffectAction.ExceptionBoundary:
                            hasUnknownExceptionBoundary = true;
                            break;
                        default:
                            MarkUnknown("metadata_opcode_unsupported", SharpProofEffect.UnsupportedOperation, true);
                            break;
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
        var rootLocation = new MethodLocation(path, root);
        if (TryFindRootTypeInitializer(rootLocation, out var rootInitializer))
            _ = Visit(rootInitializer, 0);
        _ = Visit(rootLocation, 0, CreateRootCallContext(rootLocation));
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
    private static bool IsInternalStorage(MetadataValueOrigin? origin) =>
        origin is MetadataValueOrigin.Fresh or MetadataValueOrigin.Local;
    private static ImmutableDictionary<short, MetadataOpcodeModel> BuildOpcodeModels() =>
        OpCodesByValue.Values.ToImmutableDictionary(
            static opcode => opcode.Value,
            static opcode => CreateOpcodeModel(opcode));
    private static MetadataOpcodeModel CreateOpcodeModel(OpCode opcode) {
        var name = opcode.Name ?? string.Empty;
        var variable = name switch {
            _ when name.StartsWith("ldarga", StringComparison.Ordinal) => MetadataVariableAction.AddressArgument,
            _ when name.StartsWith("ldarg", StringComparison.Ordinal) => MetadataVariableAction.LoadArgument,
            _ when name.StartsWith("starg", StringComparison.Ordinal) => MetadataVariableAction.StoreArgument,
            _ when name.StartsWith("ldloca", StringComparison.Ordinal) => MetadataVariableAction.AddressLocal,
            _ when name.StartsWith("ldloc", StringComparison.Ordinal) => MetadataVariableAction.LoadLocal,
            _ when name.StartsWith("stloc", StringComparison.Ordinal) => MetadataVariableAction.StoreLocal,
            _ => MetadataVariableAction.None
        };
        var effect = ClassifyEffect(opcode, name);
        return new MetadataOpcodeModel(
            opcode,
            effect,
            ClassifyStack(opcode, name, effect, variable),
            variable,
            name.Length > 1 &&
            name[name.Length - 2] == '.' &&
            name[name.Length - 1] is >= '0' and <= '3'
                ? name[name.Length - 1] - '0'
                : -1);
    }
    private static MetadataEffectAction ClassifyEffect(OpCode opcode, string name) {
        if (opcode == OpCodes.Volatile) return MetadataEffectAction.Synchronize;
        if (opcode == OpCodes.Newarr || opcode == OpCodes.Box) return MetadataEffectAction.Allocate;
        if (opcode == OpCodes.Newobj) return MetadataEffectAction.Construct;
        if (opcode == OpCodes.Throw || opcode == OpCodes.Rethrow) return MetadataEffectAction.Throw;
        if (opcode == OpCodes.Stsfld) return MetadataEffectAction.WriteStatic;
        if (opcode == OpCodes.Ldsfld || opcode == OpCodes.Ldsflda) return MetadataEffectAction.ReadStatic;
        if (opcode == OpCodes.Stfld) return MetadataEffectAction.WriteField;
        if (opcode == OpCodes.Ldfld || opcode == OpCodes.Ldflda) return MetadataEffectAction.ReadField;
        if (opcode == OpCodes.Ldlen ||
            opcode == OpCodes.Ldelema ||
            name.StartsWith("ldelem", StringComparison.Ordinal))
            return MetadataEffectAction.ReadElement;
        if (opcode == OpCodes.Ldobj || name.StartsWith("ldind", StringComparison.Ordinal))
            return MetadataEffectAction.ReadIndirect;
        if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt) return MetadataEffectAction.Call;
        if (name.StartsWith("stelem", StringComparison.Ordinal)) return MetadataEffectAction.WriteElement;
        if (opcode == OpCodes.Stobj ||
            opcode == OpCodes.Initobj ||
            opcode == OpCodes.Cpblk ||
            opcode == OpCodes.Initblk ||
            name.StartsWith("stind", StringComparison.Ordinal))
            return MetadataEffectAction.WriteIndirect;
        if (opcode == OpCodes.Div ||
            opcode == OpCodes.Div_Un ||
            opcode == OpCodes.Rem ||
            opcode == OpCodes.Rem_Un ||
            name.IndexOf("ovf", StringComparison.Ordinal) >= 0 ||
            opcode == OpCodes.Castclass ||
            opcode == OpCodes.Unbox ||
            opcode == OpCodes.Unbox_Any ||
            opcode == OpCodes.Ldvirtftn)
            return MetadataEffectAction.ExceptionBoundary;
        return IsNoEffectOpcode(opcode, name)
            ? MetadataEffectAction.None
            : MetadataEffectAction.Unsupported;
    }
    private static bool IsNoEffectOpcode(OpCode opcode, string name) =>
        opcode == OpCodes.Nop || opcode == OpCodes.Ret || opcode == OpCodes.Pop || opcode == OpCodes.Dup ||
        opcode == OpCodes.Isinst || opcode == OpCodes.Sizeof || opcode == OpCodes.Ldftn ||
        opcode == OpCodes.Add || opcode == OpCodes.Sub || opcode == OpCodes.Mul || opcode == OpCodes.Neg ||
        opcode == OpCodes.Not || opcode == OpCodes.And || opcode == OpCodes.Or || opcode == OpCodes.Xor ||
        opcode == OpCodes.Shl || opcode == OpCodes.Shr || opcode == OpCodes.Shr_Un || opcode == OpCodes.Ceq ||
        opcode == OpCodes.Cgt || opcode == OpCodes.Cgt_Un || opcode == OpCodes.Clt || opcode == OpCodes.Clt_Un ||
        opcode == OpCodes.Ldnull || opcode == OpCodes.Ldstr || opcode == OpCodes.Ldtoken ||
        name.StartsWith("ldarg", StringComparison.Ordinal) || name.StartsWith("ldloc", StringComparison.Ordinal) ||
        name.StartsWith("starg", StringComparison.Ordinal) || name.StartsWith("stloc", StringComparison.Ordinal) ||
        name.StartsWith("ldc", StringComparison.Ordinal) || opcode == OpCodes.Switch ||
        name.StartsWith("br", StringComparison.Ordinal) || name.StartsWith("leave", StringComparison.Ordinal) ||
        name.StartsWith("conv", StringComparison.Ordinal) || name.StartsWith("readonly", StringComparison.Ordinal) ||
        name.StartsWith("constrained", StringComparison.Ordinal) || name.StartsWith("tail", StringComparison.Ordinal) ||
        name.StartsWith("unaligned", StringComparison.Ordinal);
    private static MetadataStackRule ClassifyStack(
        OpCode opcode,
        string name,
        MetadataEffectAction effect,
        MetadataVariableAction variable) {
        if (variable != MetadataVariableAction.None) return default;
        if (opcode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
            return new MetadataStackRule(Branch: true);
        if (effect == MetadataEffectAction.Allocate)
            return new MetadataStackRule(1, MetadataStackResult.Fresh);
        if (effect == MetadataEffectAction.Construct)
            return new MetadataStackRule(Push: MetadataStackResult.Fresh, Reset: true);
        if (effect == MetadataEffectAction.Throw) return new MetadataStackRule(Reset: true);
        if (effect == MetadataEffectAction.WriteStatic) return new MetadataStackRule(1);
        if (effect == MetadataEffectAction.ReadStatic)
            return new MetadataStackRule(Push: MetadataStackResult.Static);
        if (effect == MetadataEffectAction.WriteField)
            return new MetadataStackRule(2, OriginIndex: 1, ReportsOrigin: true);
        if (effect == MetadataEffectAction.ReadField)
            return new MetadataStackRule(
                1,
                opcode == OpCodes.Ldflda ? MetadataStackResult.Origin : MetadataStackResult.Unknown,
                ReportsOrigin: true);
        if (effect == MetadataEffectAction.ReadElement)
            return opcode == OpCodes.Ldlen
                ? new MetadataStackRule(1, MetadataStackResult.Scalar, ReportsOrigin: true)
                : new MetadataStackRule(
                    2,
                    opcode == OpCodes.Ldelema ? MetadataStackResult.Origin : MetadataStackResult.Unknown,
                    1,
                    true);
        if (effect == MetadataEffectAction.ReadIndirect)
            return new MetadataStackRule(1, MetadataStackResult.Unknown, ReportsOrigin: true);
        if (effect == MetadataEffectAction.Call)
            return new MetadataStackRule(Push: MetadataStackResult.Unknown, Reset: true);
        if (effect == MetadataEffectAction.WriteElement)
            return new MetadataStackRule(3, OriginIndex: 2, ReportsOrigin: true);
        if (effect == MetadataEffectAction.WriteIndirect) {
            if (opcode == OpCodes.Initobj)
                return new MetadataStackRule(1, ReportsOrigin: true);
            return name.StartsWith("stind", StringComparison.Ordinal) || opcode == OpCodes.Stobj
                ? new MetadataStackRule(2, OriginIndex: 1, ReportsOrigin: true)
                : default;
        }
        if (opcode == OpCodes.Calli)
            return new MetadataStackRule(Push: MetadataStackResult.Unknown, Reset: true);
        if (opcode == OpCodes.Dup)
            return new MetadataStackRule(Push: MetadataStackResult.Peek);
        if (opcode == OpCodes.Isinst || opcode == OpCodes.Castclass || opcode == OpCodes.Unbox)
            return new MetadataStackRule(1, MetadataStackResult.Origin);
        if (opcode == OpCodes.Unbox_Any)
            return new MetadataStackRule(1, MetadataStackResult.Unknown);
        var pops = FixedPopCount(opcode.StackBehaviourPop);
        return opcode.StackBehaviourPush == StackBehaviour.Push0
            ? new MetadataStackRule(pops)
            : new MetadataStackRule(pops, MetadataStackResult.Scalar);
    }
    private static int FixedPopCount(StackBehaviour behavior) => behavior switch {
        StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
        StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or
            StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or
            StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
        StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_pop1 or
            StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or
            StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or
            StackBehaviour.Popref_popi_popref => 3,
        _ => 0
    };
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
    private static bool IsValueTypeConstructor(MethodLocation location) {
        using var stream = File.OpenRead(location.Path);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
        var reader = pe.GetMetadataReader();
        var method = reader.GetMethodDefinition(location.Handle);
        if (!string.Equals(reader.GetString(method.Name), ".ctor", StringComparison.Ordinal))
            return false;
        var declaringType = reader.GetTypeDefinition(method.GetDeclaringType());
        return IsNamedType(reader, declaringType.BaseType, "System", "ValueType");
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
    private bool TryResolveFieldDeclaringType(
        string currentPath,
        MetadataReader reader,
        Handle field,
        out string declaringPath,
        out TypeDefinitionHandle declaringType) {
        if (field.Kind == HandleKind.FieldDefinition) {
            declaringPath = currentPath;
            declaringType = reader.GetFieldDefinition((FieldDefinitionHandle)field).GetDeclaringType();
            return true;
        }
        if (field.Kind == HandleKind.MemberReference) {
            var member = reader.GetMemberReference((MemberReferenceHandle)field);
            return TryResolveContainingType(
                currentPath,
                reader,
                member.Parent,
                out declaringPath,
                out declaringType,
                out _);
        }
        declaringPath = string.Empty;
        declaringType = default;
        return false;
    }
    private static bool TryFindTypeInitializer(
        string path,
        TypeDefinitionHandle typeHandle,
        out MethodLocation initializer) {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
        var reader = pe.GetMetadataReader();
        foreach (var methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods()) {
            var method = reader.GetMethodDefinition(methodHandle);
            if (!string.Equals(reader.GetString(method.Name), ".cctor", StringComparison.Ordinal))
                continue;
            initializer = new MethodLocation(path, methodHandle);
            return true;
        }
        initializer = default;
        return false;
    }
    private static bool TryFindDeclaringTypeInitializer(
        MethodLocation memberLocation,
        bool requireStaticMember,
        out MethodLocation initializer) {
        using var stream = File.OpenRead(memberLocation.Path);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
        var reader = pe.GetMetadataReader();
        var member = reader.GetMethodDefinition(memberLocation.Handle);
        if (requireStaticMember && (member.Attributes & MethodAttributes.Static) == 0) {
            initializer = default;
            return false;
        }
        foreach (var methodHandle in reader.GetTypeDefinition(member.GetDeclaringType()).GetMethods()) {
            var method = reader.GetMethodDefinition(methodHandle);
            if (!string.Equals(reader.GetString(method.Name), ".cctor", StringComparison.Ordinal))
                continue;
            initializer = new MethodLocation(memberLocation.Path, methodHandle);
            return true;
        }
        initializer = default;
        return false;
    }
    private static bool TryFindRootTypeInitializer(
        MethodLocation rootLocation,
        out MethodLocation initializer) {
        using var stream = File.OpenRead(rootLocation.Path);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
        var reader = pe.GetMetadataReader();
        var root = reader.GetMethodDefinition(rootLocation.Handle);
        var name = reader.GetString(root.Name);
        var triggersInitialization =
            ((root.Attributes & MethodAttributes.Static) != 0 &&
             !string.Equals(name, ".cctor", StringComparison.Ordinal)) ||
            string.Equals(name, ".ctor", StringComparison.Ordinal);
        if (!triggersInitialization) {
            initializer = default;
            return false;
        }
        foreach (var methodHandle in reader.GetTypeDefinition(root.GetDeclaringType()).GetMethods()) {
            var method = reader.GetMethodDefinition(methodHandle);
            if (!string.Equals(reader.GetString(method.Name), ".cctor", StringComparison.Ordinal))
                continue;
            initializer = new MethodLocation(rootLocation.Path, methodHandle);
            return true;
        }
        initializer = default;
        return false;
    }
    private static MetadataCallContext? CreateRootCallContext(MethodLocation rootLocation) {
        using var stream = File.OpenRead(rootLocation.Path);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchMetadata);
        var reader = pe.GetMetadataReader();
        var root = reader.GetMethodDefinition(rootLocation.Handle);
        if (!string.Equals(reader.GetString(root.Name), ".ctor", StringComparison.Ordinal))
            return null;
        var signature = root.DecodeSignature(new StructuralTypeProvider(), null);
        return new MetadataCallContext(
            MetadataValueOrigin.Fresh,
            [.. Enumerable.Repeat(MetadataValueOrigin.Argument, signature.ParameterTypes.Length)]);
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
    private static bool TryRead(
        byte[] bytes,
        ref int offset,
        out MetadataOpcodeModel instruction,
        out int operand) {
        instruction = default;
        operand = 0;
        if (offset >= bytes.Length) return false;
        short value = bytes[offset++] == 0xFE
            ? offset < bytes.Length ? (short)(0xFE00 | bytes[offset++]) : (short)-1
            : (short)bytes[offset - 1];
        if (!OpcodeModels.TryGetValue(value, out instruction)) return false;
        var size = OperandSize(instruction.Code.OperandType, bytes, offset);
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
    private enum MetadataEffectAction {
        Unsupported,
        None,
        Synchronize,
        Allocate,
        Construct,
        Throw,
        WriteStatic,
        ReadStatic,
        WriteField,
        ReadField,
        ReadElement,
        ReadIndirect,
        Call,
        WriteElement,
        WriteIndirect,
        ExceptionBoundary
    }
    private enum MetadataStackResult { None, Scalar, Fresh, Static, Unknown, Origin, Peek }
    private enum MetadataVariableAction {
        None,
        LoadArgument,
        AddressArgument,
        StoreArgument,
        LoadLocal,
        StoreLocal,
        AddressLocal
    }
    private readonly record struct MetadataStackRule(
        int Pops = 0,
        MetadataStackResult Push = MetadataStackResult.None,
        int OriginIndex = 0,
        bool ReportsOrigin = false,
        bool Reset = false,
        bool Branch = false);
    private readonly record struct MetadataOpcodeModel(
        OpCode Code,
        MetadataEffectAction Effect,
        MetadataStackRule Stack,
        MetadataVariableAction Variable = MetadataVariableAction.None,
        int FixedVariableIndex = -1);
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
        private readonly ImmutableArray<bool> _copiedValueParameters;
        private readonly List<MetadataValueOrigin> _stack = [];
        private readonly Dictionary<int, MetadataValueOrigin> _arguments = [];
        private readonly Dictionary<int, MetadataValueOrigin> _locals = [];
        private bool _hasControlFlowBranch;
        internal MetadataProvenanceState(
            bool isStatic,
            MetadataCallContext context,
            ImmutableArray<bool> copiedValueParameters) {
            _isStatic = isStatic;
            _context = context;
            _copiedValueParameters = copiedValueParameters;
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
        internal MetadataValueOrigin? Observe(MetadataOpcodeModel instruction, int operand) {
            if (instruction.Variable != MetadataVariableAction.None) {
                ObserveVariable(instruction, operand);
                return null;
            }
            var rule = instruction.Stack;
            if (rule.Branch) {
                ObserveBranch();
                return null;
            }
            if (rule.Reset) ResetStack();
            var origin = MetadataValueOrigin.Unknown;
            for (var index = 0; index < rule.Pops; index++) {
                var popped = Pop();
                if (index == rule.OriginIndex) origin = popped;
            }
            if (rule.Push != MetadataStackResult.None) {
                Push(rule.Push switch {
                    MetadataStackResult.Scalar => MetadataValueOrigin.Scalar,
                    MetadataStackResult.Fresh => MetadataValueOrigin.Fresh,
                    MetadataStackResult.Static => MetadataValueOrigin.Static,
                    MetadataStackResult.Origin => origin,
                    MetadataStackResult.Peek => Peek(),
                    _ => MetadataValueOrigin.Unknown
                });
            }
            return rule.ReportsOrigin ? origin : null;
        }
        private void ObserveVariable(MetadataOpcodeModel instruction, int operand) {
            var index = instruction.FixedVariableIndex >= 0
                ? instruction.FixedVariableIndex
                : operand;
            switch (instruction.Variable) {
                case MetadataVariableAction.LoadArgument:
                    Push(ArgumentOrigin(index));
                    break;
                case MetadataVariableAction.AddressArgument:
                case MetadataVariableAction.AddressLocal:
                    Push(MetadataValueOrigin.Local);
                    break;
                case MetadataVariableAction.StoreArgument:
                    _arguments[index] = StoredOrigin();
                    break;
                case MetadataVariableAction.LoadLocal:
                    Push(_locals.TryGetValue(index, out var local)
                        ? local
                        : MetadataValueOrigin.Unknown);
                    break;
                case MetadataVariableAction.StoreLocal:
                    _locals[index] = StoredOrigin();
                    break;
            }
        }
        private MetadataValueOrigin StoredOrigin() {
            var origin = Pop();
            return _hasControlFlowBranch ? MetadataValueOrigin.Unknown : origin;
        }
        private void ObserveBranch() {
            _hasControlFlowBranch = true;
            ResetStack();
            foreach (var index in _arguments.Keys.ToArray())
                _arguments[index] = MetadataValueOrigin.Unknown;
            foreach (var index in _locals.Keys.ToArray())
                _locals[index] = MetadataValueOrigin.Unknown;
        }
        private MetadataValueOrigin ArgumentOrigin(int ilIndex) {
            if (_arguments.TryGetValue(ilIndex, out var assigned)) return assigned;
            if (!_isStatic && ilIndex == 0) return _context.Receiver;
            var parameterIndex = _isStatic ? ilIndex : ilIndex - 1;
            if (parameterIndex >= 0 &&
                parameterIndex < _copiedValueParameters.Length &&
                _copiedValueParameters[parameterIndex])
                return MetadataValueOrigin.Local;
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
    }
}
