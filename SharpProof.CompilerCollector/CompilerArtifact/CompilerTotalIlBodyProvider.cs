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
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => opcode.Value);
    private readonly CompilerMetadataResolution.MetadataResolutionContext _resolution = new(compilation);
    private readonly Dictionary<string, byte[]> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TotalIlBody?> _bodies = new(StringComparer.Ordinal);
    private CompilerReferenceSnapshot[]? _capturedReferences = references;

    internal TotalIlBody? Resolve(IMethodSymbol method, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Candidate(method) || SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly))
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

    private static bool Candidate(IMethodSymbol method)
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
            var read = 0;
            while (read != bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = stream.Read(bytes, read, Math.Min(81920, bytes.Length - read));
                if (count == 0)
                { return null; }
                read += count;
            }
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
            ResolveMethod(reader, assembly, handle, method.ContainingModule.Name) is not { } resolved ||
            !SameSignature(method, resolved) || !NoInitialization(pe, reader, definition.GetDeclaringType()))
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
            locals = signature.DecodeLocalSignature(new ScalarSignatureProvider(), genericContext: null);
            if (locals.Any(type => !TotalIlStack.Supported(type)))
            { return null; }
        }
        var instructions = Decode(body, reader, assembly, method.ContainingModule.Name, cancellationToken);
        return instructions.IsDefault ? null : new(method, authority.Sha256, authority.Name, locals,
            body.LocalVariablesInitialized, body.MaxStack, instructions);
    }

    private static bool SameSignature(IMethodSymbol expected, IMethodSymbol actual)
    {
        return Candidate(actual) && expected.MetadataToken == actual.MetadataToken &&
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

    private static ImmutableArray<TotalIlInstruction> Decode(MethodBodyBlock body, MetadataReader metadata,
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

    private static IMethodSymbol? ResolveMethod(MetadataReader reader, IAssemblySymbol assembly, MethodDefinitionHandle handle, string module)
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

    private sealed class ScalarSignatureProvider : ISignatureTypeProvider<SpecialType, object?>
    {
        public SpecialType GetPrimitiveType(PrimitiveTypeCode code)
        {
            return code switch
            {
                PrimitiveTypeCode.Boolean => SpecialType.System_Boolean,
                PrimitiveTypeCode.Char => SpecialType.System_Char,
                PrimitiveTypeCode.SByte => SpecialType.System_SByte,
                PrimitiveTypeCode.Byte => SpecialType.System_Byte,
                PrimitiveTypeCode.Int16 => SpecialType.System_Int16,
                PrimitiveTypeCode.UInt16 => SpecialType.System_UInt16,
                PrimitiveTypeCode.Int32 => SpecialType.System_Int32,
                PrimitiveTypeCode.UInt32 => SpecialType.System_UInt32,
                PrimitiveTypeCode.Int64 => SpecialType.System_Int64,
                PrimitiveTypeCode.UInt64 => SpecialType.System_UInt64,
                PrimitiveTypeCode.Object => SpecialType.System_Object,
                PrimitiveTypeCode.String => SpecialType.System_String,
                _ => SpecialType.None
            };
        }
        public SpecialType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        { return SpecialType.None; }
        public SpecialType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        { return SpecialType.None; }
        public SpecialType GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        { return SpecialType.None; }
        public SpecialType GetSZArrayType(SpecialType elementType)
        { return SpecialType.None; }
        public SpecialType GetPointerType(SpecialType elementType)
        { return SpecialType.None; }
        public SpecialType GetByReferenceType(SpecialType elementType)
        { return SpecialType.None; }
        public SpecialType GetPinnedType(SpecialType elementType)
        { return SpecialType.None; }
        public SpecialType GetModifiedType(SpecialType modifier, SpecialType unmodifiedType, bool isRequired)
        { return SpecialType.None; }
        public SpecialType GetArrayType(SpecialType elementType, ArrayShape shape)
        { return SpecialType.None; }
        public SpecialType GetGenericInstantiation(SpecialType genericType, ImmutableArray<SpecialType> typeArguments)
        { return SpecialType.None; }
        public SpecialType GetGenericMethodParameter(object? genericContext, int index)
        { return SpecialType.None; }
        public SpecialType GetGenericTypeParameter(object? genericContext, int index)
        { return SpecialType.None; }
        public SpecialType GetFunctionPointerType(MethodSignature<SpecialType> signature)
        { return SpecialType.None; }
    }
}
