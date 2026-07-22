using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
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
    private static MethodEffects AnalyzeBody(string path, MethodDefinitionHandle root) {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
        var reader = pe.GetMetadataReader();
        var active = new HashSet<int>();
        var analyzed = new HashSet<int>();
        var effects = SharpProofEffect.None;
        var unknowns = ImmutableArray.CreateBuilder<SharpProofUnknownReason>();
        var exceptions = ImmutableArray.CreateBuilder<string>();
        var hasUnknownExceptionBoundary = false;
        var instructionCount = 0;
        void Visit(MethodDefinitionHandle handle, int depth) {
            var token = MetadataTokens.GetToken(handle);
            if (depth > MaxDepth || analyzed.Count >= MaxMethods) {
                effects |= SharpProofEffect.Unknown | SharpProofEffect.BudgetExhaustion;
                unknowns.Add(Reason("metadata_budget_exhausted"));
                return;
            }
            if (analyzed.Contains(token)) return;
            if (!active.Add(token)) {
                effects |= SharpProofEffect.Unknown;
                unknowns.Add(Reason("metadata_recursive_cycle"));
                return;
            }
            try {
                var definition = reader.GetMethodDefinition(handle);
                if ((definition.Attributes & MethodAttributes.PinvokeImpl) != 0) {
                    effects |= SharpProofEffect.UsesNativeCode | SharpProofEffect.Unknown;
                    hasUnknownExceptionBoundary = true;
                    unknowns.Add(Reason("metadata_native_exception_boundary"));
                    return;
                }
                if (definition.RelativeVirtualAddress == 0) {
                    effects |= SharpProofEffect.Unknown;
                    hasUnknownExceptionBoundary = true;
                    unknowns.Add(Reason("metadata_body_unavailable"));
                    return;
                }
                var body = pe.GetMethodBody(definition.RelativeVirtualAddress);
                if (body.ExceptionRegions.Length != 0) {
                    effects |= SharpProofEffect.Unknown | SharpProofEffect.UnsupportedOperation;
                    hasUnknownExceptionBoundary = true;
                    unknowns.Add(Reason("metadata_exception_regions_unsupported"));
                }
                var bytes = body.GetILBytes() ?? [];
                for (var offset = 0; offset < bytes.Length;) {
                    if (++instructionCount > MaxInstructions) {
                        effects |= SharpProofEffect.Unknown | SharpProofEffect.BudgetExhaustion;
                        unknowns.Add(Reason("metadata_instruction_budget_exhausted"));
                        return;
                    }
                    if (!TryRead(bytes, ref offset, out var opcode, out var operand)) {
                        effects |= SharpProofEffect.Unknown | SharpProofEffect.UnsupportedOperation;
                        unknowns.Add(Reason("malformed_il"));
                        return;
                    }
                    if (opcode == OpCodes.Newobj || opcode == OpCodes.Newarr || opcode == OpCodes.Box) {
                        effects |= SharpProofEffect.Allocates;
                        if (opcode == OpCodes.Newobj) {
                            var constructor = MetadataTokens.Handle(operand);
                            if (TryResolveMethod(reader, constructor, out var constructorDefinition))
                                Visit(constructorDefinition, depth + 1);
                            else {
                                effects |= SharpProofEffect.Unknown;
                                hasUnknownExceptionBoundary = true;
                                unknowns.Add(Reason("metadata_constructor_unresolved"));
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
                            effects |= SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown;
                            hasUnknownExceptionBoundary = true;
                            unknowns.Add(Reason("metadata_virtual_dispatch_unresolved"));
                        }
                        var called = MetadataTokens.Handle(operand);
                        if (opcode == OpCodes.Call && TryResolveMethod(reader, called, out var calledDefinition))
                            Visit(calledDefinition, depth + 1);
                        else if (opcode == OpCodes.Call || !TryResolveMethod(reader, called, out _)) {
                            effects |= SharpProofEffect.Unknown;
                            hasUnknownExceptionBoundary = true;
                            unknowns.Add(Reason("metadata_external_call_unresolved"));
                        }
                    }
                    else if (IsIndirectOrElementWrite(opcode)) {
                        effects |= SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown |
                                   SharpProofEffect.UnsupportedOperation;
                        hasUnknownExceptionBoundary = true;
                        unknowns.Add(Reason("metadata_indirect_write_origin_unknown"));
                    }
                    else if (MayThrowImplicitly(opcode)) {
                        effects |= SharpProofEffect.Unknown;
                        hasUnknownExceptionBoundary = true;
                        unknowns.Add(Reason("metadata_implicit_exception"));
                    }
                    else if (!IsModeledNoEffectOpcode(opcode)) {
                        effects |= SharpProofEffect.Unknown | SharpProofEffect.UnsupportedOperation;
                        hasUnknownExceptionBoundary = true;
                        unknowns.Add(Reason("metadata_opcode_unsupported"));
                    }
                }
            }
            finally {
                active.Remove(token);
                analyzed.Add(token);
            }
        }
        Visit(root, 0);
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
    private static bool TryResolveMethod(
        MetadataReader reader,
        Handle candidate,
        out MethodDefinitionHandle result) {
        if (candidate.Kind == HandleKind.MethodDefinition) {
            result = (MethodDefinitionHandle)candidate;
            return true;
        }
        if (candidate.Kind == HandleKind.MethodSpecification) {
            var specification = reader.GetMethodSpecification((MethodSpecificationHandle)candidate);
            return TryResolveMethod(reader, specification.Method, out result);
        }
        if (candidate.Kind != HandleKind.MemberReference) {
            result = default;
            return false;
        }
        var member = reader.GetMemberReference((MemberReferenceHandle)candidate);
        if (member.Parent.Kind != HandleKind.TypeDefinition) {
            result = default;
            return false;
        }
        var wantedName = reader.GetString(member.Name);
        var wantedSignature = reader.GetBlobBytes(member.Signature);
        foreach (var methodHandle in reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent).GetMethods()) {
            var definition = reader.GetMethodDefinition(methodHandle);
            if (!string.Equals(reader.GetString(definition.Name), wantedName, StringComparison.Ordinal) ||
                !reader.GetBlobBytes(definition.Signature).AsSpan().SequenceEqual(wantedSignature))
                continue;
            result = methodHandle;
            return true;
        }
        result = default;
        return false;
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
}
