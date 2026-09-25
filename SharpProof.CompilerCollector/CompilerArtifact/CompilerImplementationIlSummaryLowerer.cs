using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// This lowerer runs only in the build-time compiler collector.
#pragma warning disable RS1035 // Exact implementation evidence requires reading the final PE image.
namespace SharpProof.CompilerArtifact;

internal delegate bool TryResolveCompilerSummary(
    IMethodSymbol method,
    IrMemberId member,
    CancellationToken cancellationToken,
    out IrRelationalSummary? summary);

internal enum CompilerImplementationIlAbstentionReason
{
    None = 0,
    NotCandidate = 1,
    ReferenceAssembly = 2,
    ReferenceUnavailable = 3,
    MetadataMismatch = 4,
    MethodBodyUnavailable = 5,
    UnsupportedIl = 6,
    SummaryUnsupportedBody = 7,
    SummaryInvalidSignature = 8,
    SummaryMissingDependency = 9,
    SummaryResourceLimit = 10,
    SummaryConstructionFailed = 11,
    InvalidImage = 12,
    UnresolvedCallTarget = 13,
    MissingCallSummary = 14,
    UnsupportedOpcode = 15,
    InadmissibleCallTarget = 16,
    CrossModuleCallTarget = 17
}

internal static class CompilerImplementationIlSummaryLowerer
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

    private const int MaximumIlBytes = 65536;
    private const int MaximumStack = 128;
    private static readonly ImmutableDictionary<ILOpCode, IlOperandSize>
        OperandSizes = new Dictionary<ILOpCode, IlOperandSize>
        {
            [ILOpCode.Ldarg_s] = IlOperandSize.Byte,
            [ILOpCode.Ldloc_s] = IlOperandSize.Byte,
            [ILOpCode.Stloc_s] = IlOperandSize.Byte,
            [ILOpCode.Ldarg] = IlOperandSize.UInt16,
            [ILOpCode.Ldloc] = IlOperandSize.UInt16,
            [ILOpCode.Stloc] = IlOperandSize.UInt16,
            [ILOpCode.Ldc_i4_s] = IlOperandSize.SByte,
            [ILOpCode.Br_s] = IlOperandSize.SByte,
            [ILOpCode.Brfalse_s] = IlOperandSize.SByte,
            [ILOpCode.Brtrue_s] = IlOperandSize.SByte,
            [ILOpCode.Beq_s] = IlOperandSize.SByte,
            [ILOpCode.Bge_s] = IlOperandSize.SByte,
            [ILOpCode.Bgt_s] = IlOperandSize.SByte,
            [ILOpCode.Ble_s] = IlOperandSize.SByte,
            [ILOpCode.Blt_s] = IlOperandSize.SByte,
            [ILOpCode.Bne_un_s] = IlOperandSize.SByte,
            [ILOpCode.Ldc_i4] = IlOperandSize.Int32,
            [ILOpCode.Call] = IlOperandSize.Int32,
            [ILOpCode.Br] = IlOperandSize.Int32,
            [ILOpCode.Brfalse] = IlOperandSize.Int32,
            [ILOpCode.Brtrue] = IlOperandSize.Int32,
            [ILOpCode.Beq] = IlOperandSize.Int32,
            [ILOpCode.Bge] = IlOperandSize.Int32,
            [ILOpCode.Bgt] = IlOperandSize.Int32,
            [ILOpCode.Ble] = IlOperandSize.Int32,
            [ILOpCode.Blt] = IlOperandSize.Int32,
            [ILOpCode.Bne_un] = IlOperandSize.Int32,
            [ILOpCode.Ldc_i8] = IlOperandSize.Int64,
            [ILOpCode.Nop] = IlOperandSize.None,
            [ILOpCode.Ldarg_0] = IlOperandSize.None,
            [ILOpCode.Ldarg_1] = IlOperandSize.None,
            [ILOpCode.Ldarg_2] = IlOperandSize.None,
            [ILOpCode.Ldarg_3] = IlOperandSize.None,
            [ILOpCode.Ldloc_0] = IlOperandSize.None,
            [ILOpCode.Ldloc_1] = IlOperandSize.None,
            [ILOpCode.Ldloc_2] = IlOperandSize.None,
            [ILOpCode.Ldloc_3] = IlOperandSize.None,
            [ILOpCode.Stloc_0] = IlOperandSize.None,
            [ILOpCode.Stloc_1] = IlOperandSize.None,
            [ILOpCode.Stloc_2] = IlOperandSize.None,
            [ILOpCode.Stloc_3] = IlOperandSize.None,
            [ILOpCode.Ldc_i4_m1] = IlOperandSize.None,
            [ILOpCode.Ldc_i4_0] = IlOperandSize.None,
            [ILOpCode.Ldc_i4_1] = IlOperandSize.None,
            [ILOpCode.Ldc_i4_2] = IlOperandSize.None,
            [ILOpCode.Ldc_i4_3] = IlOperandSize.None,
            [ILOpCode.Ldc_i4_4] = IlOperandSize.None,
            [ILOpCode.Ldc_i4_5] = IlOperandSize.None,
            [ILOpCode.Ldc_i4_6] = IlOperandSize.None,
            [ILOpCode.Ldc_i4_7] = IlOperandSize.None,
            [ILOpCode.Ldc_i4_8] = IlOperandSize.None,
            [ILOpCode.Dup] = IlOperandSize.None,
            [ILOpCode.Pop] = IlOperandSize.None,
            [ILOpCode.Ret] = IlOperandSize.None,
            [ILOpCode.Add] = IlOperandSize.None,
            [ILOpCode.Sub] = IlOperandSize.None,
            [ILOpCode.Mul] = IlOperandSize.None,
            [ILOpCode.Div] = IlOperandSize.None,
            [ILOpCode.Rem] = IlOperandSize.None,
            [ILOpCode.And] = IlOperandSize.None,
            [ILOpCode.Or] = IlOperandSize.None,
            [ILOpCode.Xor] = IlOperandSize.None,
            [ILOpCode.Neg] = IlOperandSize.None,
            [ILOpCode.Ceq] = IlOperandSize.None,
            [ILOpCode.Cgt] = IlOperandSize.None,
            [ILOpCode.Clt] = IlOperandSize.None,
            [ILOpCode.Add_ovf] = IlOperandSize.None,
            [ILOpCode.Sub_ovf] = IlOperandSize.None,
            [ILOpCode.Mul_ovf] = IlOperandSize.None
        }.ToImmutableDictionary();

    internal static bool IsCandidate(
        CSharpCompilation compilation,
        IMethodSymbol method)
    {
        method = SemanticClaimIdentity.NormalizeCandidate(method)
            .OriginalDefinition;
        return IsNormalizedCandidate(compilation, method);
    }

    private static bool IsNormalizedCandidate(
        CSharpCompilation compilation,
        IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Ordinary &&
            method.IsStatic &&
            !method.IsAbstract &&
            !method.IsExtern &&
            !method.IsVirtual &&
            method.TypeParameters.IsEmpty &&
            method.ContainingType.TypeParameters.IsEmpty &&
            !SymbolEqualityComparer.Default.Equals(
                method.ContainingAssembly,
                compilation.Assembly) &&
            method.Parameters.All(static parameter =>
                parameter.RefKind == RefKind.None &&
                ScalarType.TryCreate(parameter.Type, out _)) &&
            !method.ReturnsByRef &&
            !method.ReturnsByRefReadonly &&
            ScalarType.TryCreate(method.ReturnType, out _);
    }

    internal static bool TryBuild(
        CSharpCompilation compilation,
        IrFactory factory,
        IMethodSymbol method,
        IrMemberId member,
        Func<IMethodSymbol, bool> isKnownPure,
        TryResolveCompilerSummary resolveSummary,
        CancellationToken cancellationToken,
        out IrRelationalSummary? summary,
        out CompilerImplementationIlAbstentionReason reason)
    {
        return TryBuild(
            compilation,
            new MetadataResolutionContext(compilation),
            factory,
            method,
            member,
            isKnownPure,
            resolveSummary,
            cancellationToken,
            out summary,
            out reason);
    }

    internal static bool TryBuild(
        CSharpCompilation compilation,
        MetadataResolutionContext metadataResolution,
        IrFactory factory,
        IMethodSymbol method,
        IrMemberId member,
        Func<IMethodSymbol, bool> isKnownPure,
        TryResolveCompilerSummary resolveSummary,
        CancellationToken cancellationToken,
        out IrRelationalSummary? summary,
        out CompilerImplementationIlAbstentionReason reason)
    {
        summary = null;
        reason = CompilerImplementationIlAbstentionReason.None;
        method = SemanticClaimIdentity.NormalizeCandidate(method)
            .OriginalDefinition;
        if (!IsNormalizedCandidate(compilation, method))
        {
            reason = CompilerImplementationIlAbstentionReason.NotCandidate;
            return false;
        }

        if (IsReferenceAssembly(method.ContainingAssembly))
        {
            reason = CompilerImplementationIlAbstentionReason.ReferenceAssembly;
            return false;
        }

        if (!metadataResolution.TryFindReference(
                method.ContainingAssembly.Identity,
                method.ContainingModule.Name,
                cancellationToken,
                out var reference,
                out var module,
                out var modulePath))
        {
            reason = CompilerImplementationIlAbstentionReason.ReferenceUnavailable;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stream = new FileStream(
                modulePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var image = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!image.HasMetadata)
            {
                reason = CompilerImplementationIlAbstentionReason.MetadataMismatch;
                return false;
            }

            var reader = image.GetMetadataReader();
            var backingReader = module.GetMetadataReader();
            if (!CompilerCompilationCapture.MetadataEquals(
                    backingReader,
                    reader))
            {
                reason = CompilerImplementationIlAbstentionReason.MetadataMismatch;
                return false;
            }

            if (metadataResolution.Resolve(reference)
                    is not IAssemblySymbol metadataAssembly)
            {
                reason = CompilerImplementationIlAbstentionReason.MetadataMismatch;
                return false;
            }

            if (!TryGetMethodDefinition(
                    reader,
                    method.MetadataToken,
                    out var methodHandle,
                    out var definition) ||
                definition.RelativeVirtualAddress == 0 ||
                !HasManagedIlBody(definition))
            {
                reason = CompilerImplementationIlAbstentionReason.MethodBodyUnavailable;
                return false;
            }

            var body = image.GetMethodBody(
                definition.RelativeVirtualAddress);
            if (body.Size <= 0 ||
                body.Size > MaximumIlBytes ||
                body.MaxStack < 0 ||
                body.MaxStack > MaximumStack ||
                !body.ExceptionRegions.IsEmpty)
            {
                reason = CompilerImplementationIlAbstentionReason.UnsupportedIl;
                return false;
            }

            var memberInfo = factory.GetMemberInfo(member);
            if (!memberInfo.IsStatic ||
                memberInfo.ParameterTypes.Length !=
                method.Parameters.Length)
            {
                reason = CompilerImplementationIlAbstentionReason.NotCandidate;
                return false;
            }

            var parameters = memberInfo.ParameterTypes
                .Select((type, ordinal) => factory.CreateVariable(
                    "il-summary:parameter:" + ordinal.ToString(
                        CultureInfo.InvariantCulture),
                    type))
                .ToImmutableArray();
            var result = factory.CreateVariable(
                "il-summary:result",
                memberInfo.ReturnType);
            var mapper = new RoslynOperationLowerer(
                factory,
                isKnownPure);
            var translator = new Translator(
                compilation,
                factory,
                reader,
                metadataAssembly,
                method,
                body,
                parameters,
                mapper,
                resolveSummary,
                cancellationToken);
            var translated = translator.Translate();
            if (translated == null)
            {
                reason = translator.FailureReason;
                return false;
            }

            var signature = CompilerSummarySignature.Create(
                member,
                parameters,
                result,
                new IrSummaryProvenance(
                    IrSummaryOrigin.ImplementationIl,
                    CompilerCompilationCapture.Hash(stream, cancellationToken),
                    evidenceCallIdentity: method.GetDocumentationCommentId() ?? string.Empty));
            var built = IrRelationalSummaryBuilder.Build(
                translated.Program,
                signature,
                parameters.ToImmutableDictionary(
                    static parameter => parameter,
                    parameter => (IrTerm)factory.Variable(parameter)),
                translated.Calls,
                mayThrow: translated.MayThrow);
            summary = built.Summary;
            if (!built.IsSuccess)
            {
                reason = built.Reason switch
                {
                    IrSummaryAbstentionReason.UnsupportedBody =>
                        CompilerImplementationIlAbstentionReason.SummaryUnsupportedBody,
                    IrSummaryAbstentionReason.InvalidSignature =>
                        CompilerImplementationIlAbstentionReason.SummaryInvalidSignature,
                    IrSummaryAbstentionReason.MissingDependency =>
                        CompilerImplementationIlAbstentionReason.SummaryMissingDependency,
                    IrSummaryAbstentionReason.ResourceLimit or
                    IrSummaryAbstentionReason.ExpressionDepth =>
                        CompilerImplementationIlAbstentionReason.SummaryResourceLimit,
                    _ => CompilerImplementationIlAbstentionReason.SummaryConstructionFailed
                };
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is
            BadImageFormatException or
            IOException or
            InvalidDataException or
            ArgumentException)
        {
            reason = CompilerImplementationIlAbstentionReason.InvalidImage;
            return false;
        }
    }

    private static bool IsReferenceAssembly(IAssemblySymbol assembly)
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

    private static bool HasNamespace(
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

    private static bool TryGetMethodDefinition(
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

    private static bool HasManagedIlBody(MethodDefinition definition)
    {
        return (definition.Attributes & MethodAttributes.Abstract) == 0 &&
            (definition.Attributes & MethodAttributes.PinvokeImpl) == 0 &&
            (definition.ImplAttributes & MethodImplAttributes.CodeTypeMask) ==
                MethodImplAttributes.IL &&
            (definition.ImplAttributes & MethodImplAttributes.ManagedMask) ==
                MethodImplAttributes.Managed;
    }

    private sealed class Translator
    {
        private readonly CSharpCompilation _compilation;
        private readonly IrFactory _factory;
        private readonly MetadataReader _reader;
        private readonly IAssemblySymbol _metadataAssembly;
        private readonly IMethodSymbol _method;
        private readonly MethodBodyBlock _body;
        private readonly ImmutableArray<IrVarId> _parameters;
        private readonly ImmutableArray<ScalarType> _parameterTypes;
        private readonly RoslynOperationLowerer _mapper;
        private readonly TryResolveCompilerSummary _resolveSummary;
        private readonly CancellationToken _cancellationToken;
        private readonly ImmutableDictionary<
            IrInstructionId,
            IrRelationalSummary>.Builder _calls =
                ImmutableDictionary.CreateBuilder<
                    IrInstructionId,
                    IrRelationalSummary>();
        private readonly Dictionary<MethodDefinitionHandle, IMethodSymbol?>
            _resolvedMethods = new();
        private bool _mayThrow;

        private readonly record struct BlockContext(
            IrBlockId Block,
            Dictionary<int, IrBlockId> Blocks,
            ImmutableArray<IrVarId> Locals,
            ImmutableArray<ScalarType> LocalTypes,
            Stack<IlValue> Stack,
            IrProgramBuilder Builder);

        internal CompilerImplementationIlAbstentionReason FailureReason
        {
            get;
            private set;
        } = CompilerImplementationIlAbstentionReason.UnsupportedIl;

        internal Translator(
            CSharpCompilation compilation,
            IrFactory factory,
            MetadataReader reader,
            IAssemblySymbol metadataAssembly,
            IMethodSymbol method,
            MethodBodyBlock body,
            ImmutableArray<IrVarId> parameters,
            RoslynOperationLowerer mapper,
            TryResolveCompilerSummary resolveSummary,
            CancellationToken cancellationToken)
        {
            _compilation = compilation;
            _factory = factory;
            _reader = reader;
            _metadataAssembly = metadataAssembly;
            _method = method;
            _body = body;
            _parameters = parameters;
            _parameterTypes = method.Parameters
                .Select(static parameter =>
                {
                    ScalarType.TryCreate(parameter.Type, out var type);
                    return type;
                })
                .ToImmutableArray();
            _mapper = mapper;
            _resolveSummary = resolveSummary;
            _cancellationToken = cancellationToken;
        }

        internal Translation? Translate()
        {
            if (!TryDecode(out var instructions) ||
                !TryDecodeLocals(out var localTypes))
            {
                return null;
            }

            var leaders = new SortedSet<int> { 0 };
            foreach (var instruction in instructions)
            {
                if (instruction.IsBranch)
                {
                    leaders.Add(instruction.BranchTarget);
                    if (instruction.IsConditional &&
                        instruction.NextOffset < _body.GetILContent().Length)
                    {
                        leaders.Add(instruction.NextOffset);
                    }
                }
                else if (instruction.OpCode == ILOpCode.Ret &&
                    instruction.NextOffset < _body.GetILContent().Length)
                {
                    leaders.Add(instruction.NextOffset);
                }
            }

            var instructionIndexes = instructions
                .Select((instruction, index) =>
                    new KeyValuePair<int, int>(instruction.Offset, index))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value);
            if (leaders.Any(leader => !instructionIndexes.ContainsKey(leader)))
            {
                return null;
            }

            var builder = new IrProgramBuilder(_factory);
            var leaderArray = leaders.ToArray();
            var blocks = leaderArray.ToDictionary(
                static offset => offset,
                offset => builder.CreateBlock(
                    "il:" + offset.ToString(
                        CultureInfo.InvariantCulture)));
            builder.SetEntry(blocks[0]);
            var locals = localTypes.Select((type, index) =>
                _factory.CreateVariable(
                    "il:local:" + index.ToString(
                        CultureInfo.InvariantCulture),
                    type.IrType)).ToImmutableArray();
            if (locals.Length != 0 && !_body.LocalVariablesInitialized)
            {
                return null;
            }

            // Instructions are already decoded in offset order.  Keep an
            // offset-to-index map so each basic block can walk its contiguous
            // slice once instead of filtering the complete instruction list.
            for (var blockIndex = 0;
                 blockIndex < leaderArray.Length;
                 blockIndex++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var start = leaderArray[blockIndex];
                var end = blockIndex + 1 < leaderArray.Length
                    ? leaderArray[blockIndex + 1]
                    : _body.GetILContent().Length;
                var block = blocks[start];
                var stack = new Stack<IlValue>();
                if (start == 0)
                {
                    if (locals.Length != 0)
                    {
                        var initializationOperation = Operation(start);
                        for (var index = 0; index < locals.Length; index++)
                        {
                            builder.Assign(
                                block,
                                initializationOperation,
                                locals[index],
                                DefaultValue(localTypes[index]));
                        }
                    }
                }

                var terminated = false;
                var instructionIndex = instructionIndexes[start];
                var context = new BlockContext(
                    block,
                    blocks,
                    locals,
                    localTypes,
                    stack,
                    builder);
                for (; instructionIndex < instructions.Length &&
                       instructions[instructionIndex].Offset < end;
                     instructionIndex++)
                {
                    var instruction = instructions[instructionIndex];
                    if (!ExecuteInstruction(
                            instruction,
                            in context,
                            out terminated))
                    {
                        return null;
                    }

                    if (context.Stack.Count > _body.MaxStack)
                    {
                        return null;
                    }

                    if (terminated)
                    {
                        break;
                    }
                }

                if (!terminated)
                {
                    if (stack.Count != 0 ||
                        blockIndex + 1 >= leaderArray.Length)
                    {
                        return null;
                    }

                    builder.Goto(
                        block,
                        Operation(end),
                        blocks[leaderArray[blockIndex + 1]]);
                }
            }

            IrProgram program;
            try
            {
                program = builder.Build();
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            return new Translation(
                program,
                _calls.ToImmutable(),
                _mayThrow);
        }

        private bool ExecuteInstruction(
            DecodedInstruction instruction,
            in BlockContext context,
            out bool terminated)
        {
            terminated = false;
            switch (instruction.OpCode)
            {
                case ILOpCode.Nop:
                    return true;
                case ILOpCode.Ldarg_0:
                case ILOpCode.Ldarg_1:
                case ILOpCode.Ldarg_2:
                case ILOpCode.Ldarg_3:
                    return PushArgument(
                        (int)instruction.OpCode -
                        (int)ILOpCode.Ldarg_0,
                        context.Stack);
                case ILOpCode.Ldarg_s:
                case ILOpCode.Ldarg:
                    return PushArgument(
                        checked((int)instruction.Operand),
                        context.Stack);
                case ILOpCode.Ldloc_0:
                case ILOpCode.Ldloc_1:
                case ILOpCode.Ldloc_2:
                case ILOpCode.Ldloc_3:
                    return PushLocal(
                        (int)instruction.OpCode -
                        (int)ILOpCode.Ldloc_0,
                        in context);
                case ILOpCode.Ldloc_s:
                case ILOpCode.Ldloc:
                    return PushLocal(
                        checked((int)instruction.Operand),
                        in context);
                case ILOpCode.Stloc_0:
                case ILOpCode.Stloc_1:
                case ILOpCode.Stloc_2:
                case ILOpCode.Stloc_3:
                    return StoreLocal(
                        (int)instruction.OpCode -
                        (int)ILOpCode.Stloc_0,
                        instruction,
                        in context);
                case ILOpCode.Stloc_s:
                case ILOpCode.Stloc:
                    return StoreLocal(
                        checked((int)instruction.Operand),
                        instruction,
                        in context);
                case ILOpCode.Ldc_i4_m1:
                    context.Stack.Push(Integer(-1));
                    return true;
                case >= ILOpCode.Ldc_i4_0 and <= ILOpCode.Ldc_i4_8:
                    context.Stack.Push(Integer(
                        (int)instruction.OpCode -
                        (int)ILOpCode.Ldc_i4_0));
                    return true;
                case ILOpCode.Ldc_i4_s:
                case ILOpCode.Ldc_i4:
                    context.Stack.Push(Integer(
                        checked((int)instruction.Operand)));
                    return true;
                case ILOpCode.Ldc_i8:
                    context.Stack.Push(new IlValue(
                        _factory.Integer(instruction.Operand),
                        SpecialType.System_Int64));
                    return true;
                case ILOpCode.Dup:
                    if (!TryPeek(context.Stack, out var duplicated))
                    {
                        return false;
                    }

                    context.Stack.Push(duplicated);
                    return true;
                case ILOpCode.Pop:
                    return TryPop(context.Stack, out _);
                case ILOpCode.Ceq:
                case ILOpCode.Cgt:
                case ILOpCode.Clt:
                    return Compare(instruction.OpCode, context.Stack);
                case ILOpCode.Add:
                case ILOpCode.Sub:
                case ILOpCode.Mul:
                case ILOpCode.Add_ovf:
                case ILOpCode.Sub_ovf:
                case ILOpCode.Mul_ovf:
                case ILOpCode.Div:
                case ILOpCode.Rem:
                    return Arithmetic(
                        instruction,
                        in context);
                case ILOpCode.And:
                case ILOpCode.Or:
                case ILOpCode.Xor:
                    return BooleanBinary(
                        instruction.OpCode,
                        context.Stack);
                case ILOpCode.Neg:
                    return Negate(context.Stack);
                case ILOpCode.Call:
                    return Call(
                        instruction,
                        in context);
                case ILOpCode.Br:
                case ILOpCode.Br_s:
                    if (context.Stack.Count != 0)
                    {
                        return false;
                    }

                    context.Builder.Goto(
                        context.Block,
                        Operation(instruction.Offset),
                        context.Blocks[instruction.BranchTarget]);
                    terminated = true;
                    return true;
                case ILOpCode.Brtrue:
                case ILOpCode.Brtrue_s:
                case ILOpCode.Brfalse:
                case ILOpCode.Brfalse_s:
                    if (!TryPop(context.Stack, out var branchValue) ||
                        !TryBoolean(branchValue, out var branchCondition) ||
                        context.Stack.Count != 0 ||
                        !context.Blocks.TryGetValue(
                            instruction.NextOffset,
                            out var fallthrough))
                    {
                        return false;
                    }

                    var whenTrue = instruction.OpCode is
                        ILOpCode.Brtrue or ILOpCode.Brtrue_s
                            ? context.Blocks[instruction.BranchTarget]
                            : fallthrough;
                    var whenFalse = instruction.OpCode is
                        ILOpCode.Brtrue or ILOpCode.Brtrue_s
                            ? fallthrough
                            : context.Blocks[instruction.BranchTarget];
                    context.Builder.Branch(
                        context.Block,
                        Operation(instruction.Offset),
                        branchCondition,
                        whenTrue,
                        whenFalse);
                    terminated = true;
                    return true;
                case ILOpCode.Beq:
                case ILOpCode.Beq_s:
                case ILOpCode.Bne_un:
                case ILOpCode.Bne_un_s:
                case ILOpCode.Bge:
                case ILOpCode.Bge_s:
                case ILOpCode.Bgt:
                case ILOpCode.Bgt_s:
                case ILOpCode.Ble:
                case ILOpCode.Ble_s:
                case ILOpCode.Blt:
                case ILOpCode.Blt_s:
                    if (!BranchComparison(
                            instruction,
                            context.Stack,
                            out var comparison) ||
                        context.Stack.Count != 0 ||
                        !context.Blocks.TryGetValue(
                            instruction.NextOffset,
                            out var comparisonFallthrough))
                    {
                        return false;
                    }

                    context.Builder.Branch(
                        context.Block,
                        Operation(instruction.Offset),
                        comparison,
                        context.Blocks[instruction.BranchTarget],
                        comparisonFallthrough);
                    terminated = true;
                    return true;
                case ILOpCode.Ret:
                    if (!TryPop(context.Stack, out var returned) ||
                        !TryCoerce(
                            returned,
                            _method.ReturnType.SpecialType,
                            out returned) ||
                        context.Stack.Count != 0)
                    {
                        return false;
                    }

                    context.Builder.Return(
                        context.Block,
                        Operation(instruction.Offset),
                        returned.Term);
                    terminated = true;
                    return true;
                default:
                    FailureReason =
                        CompilerImplementationIlAbstentionReason.UnsupportedOpcode;
                    return false;
            }
        }

        private bool TryDecode(
            out ImmutableArray<DecodedInstruction> instructions)
        {
            var result = ImmutableArray.CreateBuilder<
                DecodedInstruction>();
            var reader = _body.GetILReader();
            while (reader.RemainingBytes != 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var offset = reader.Offset;
                var opCode = ReadOpCode(ref reader);
                if (!OperandSizes.TryGetValue(opCode, out var operandSize))
                {
                    instructions = [];
                    return false;
                }

                var operand = operandSize switch
                {
                    IlOperandSize.None => 0,
                    IlOperandSize.Byte => reader.ReadByte(),
                    IlOperandSize.UInt16 => reader.ReadUInt16(),
                    IlOperandSize.SByte => reader.ReadSByte(),
                    IlOperandSize.Int32 => reader.ReadInt32(),
                    IlOperandSize.Int64 => reader.ReadInt64(),
                    _ => throw new InvalidOperationException(
                        "Unknown IL operand size.")
                };

                var nextOffset = reader.Offset;
                var branchTarget = 0;
                if (opCode.IsBranch())
                {
                    try
                    {
                        branchTarget = checked(nextOffset + (int)operand);
                    }
                    catch (OverflowException)
                    {
                        instructions = [];
                        return false;
                    }

                    if (branchTarget < 0 || branchTarget >= reader.Length)
                    {
                        instructions = [];
                        return false;
                    }
                }

                var instruction = new DecodedInstruction(
                    offset,
                    opCode,
                    operand,
                    nextOffset,
                    branchTarget);

                result.Add(instruction);
            }

            instructions = result.ToImmutable();
            return !instructions.IsEmpty;
        }

        private bool TryDecodeLocals(
            out ImmutableArray<ScalarType> locals)
        {
            if (_body.LocalSignature.IsNil)
            {
                locals = [];
                return true;
            }

            var signature = _reader.GetStandaloneSignature(
                _body.LocalSignature);
            var signatureReader = _reader.GetBlobReader(
                signature.Signature);
            if (signatureReader.ReadSignatureHeader().Kind !=
                SignatureKind.LocalVariables)
            {
                locals = [];
                return false;
            }

            if (signatureReader.ReadCompressedInteger() >
                IrRelationalSummaryBuildLimits.Default.MaximumInstructions)
            {
                FailureReason = CompilerImplementationIlAbstentionReason
                    .SummaryResourceLimit;
                locals = [];
                return false;
            }

            locals = signature.DecodeLocalSignature(
                new ScalarSignatureTypeProvider(_factory),
                genericContext: null);
            return locals.All(static type => type.IsValid);
        }

        private bool Call(
            DecodedInstruction instruction,
            in BlockContext context)
        {
            var handle = MetadataTokens.Handle(
                checked((int)instruction.Operand));
            if (handle.Kind != HandleKind.MethodDefinition)
            {
                return false;
            }

            var target = ResolveMethod(
                (MethodDefinitionHandle)handle);
            if (target == null)
            {
                FailureReason =
                    CompilerImplementationIlAbstentionReason.UnresolvedCallTarget;
                return false;
            }

            if (!IsCandidate(_compilation, target))
            {
                FailureReason =
                    CompilerImplementationIlAbstentionReason.InadmissibleCallTarget;
                return false;
            }

            if (target.ContainingModule.Name !=
                _method.ContainingModule.Name)
            {
                FailureReason =
                    CompilerImplementationIlAbstentionReason.CrossModuleCallTarget;
                return false;
            }

            var terms = new IrTerm[target.Parameters.Length];
            for (var index = terms.Length - 1; index >= 0; index--)
            {
                if (!TryPop(context.Stack, out var argument) ||
                    !TryCoerce(
                        argument,
                        target.Parameters[index].Type.SpecialType,
                        out argument))
                {
                    return false;
                }

                terms[index] = argument.Term;
            }

            IrTerm? receiver = null;
            var member = _mapper.GetMember(
                target,
                ref receiver,
                "call:",
                target.ReturnType,
                terms);
            if (!_resolveSummary(
                    target,
                    member,
                    _cancellationToken,
                    out var dependency) ||
                dependency == null)
            {
                FailureReason =
                    CompilerImplementationIlAbstentionReason.MissingCallSummary;
                return false;
            }

            var result = _factory.CreateVariable(
                "il:call:" + instruction.Offset.ToString(
                    CultureInfo.InvariantCulture),
                _mapper.GetTypeId(target.ReturnType));
            var call = context.Builder.Call(
                context.Block,
                Operation(instruction.Offset),
                result,
                member,
                null,
                terms);
            _calls.Add(call.Id, dependency);
            context.Stack.Push(new IlValue(
                _factory.Variable(result),
                target.ReturnType.SpecialType));
            _mayThrow |= dependency.Effects ==
                IrSummaryEffect.MayThrow;
            return true;
        }

        private IMethodSymbol? ResolveMethod(
            MethodDefinitionHandle handle)
        {
            if (_resolvedMethods.TryGetValue(handle, out var cached))
            {
                return cached;
            }

            var definition = _reader.GetMethodDefinition(handle);
            var typeName = GetMetadataTypeName(
                definition.GetDeclaringType());
            var type = _metadataAssembly.GetTypeByMetadataName(
                typeName);
            if (type == null)
            {
                _resolvedMethods.Add(handle, null);
                return null;
            }

            var token = MetadataTokens.GetToken(handle);
            var name = _reader.GetString(definition.Name);
            var resolved = type.GetMembers(name)
                .OfType<IMethodSymbol>()
                .SingleOrDefault(candidate =>
                    candidate.MetadataToken == token &&
                    candidate.ContainingModule.Name ==
                    _method.ContainingModule.Name);
            _resolvedMethods.Add(handle, resolved);
            return resolved;
        }

        private string GetMetadataTypeName(
            TypeDefinitionHandle handle)
        {
            var definition = _reader.GetTypeDefinition(handle);
            var name = _reader.GetString(definition.Name);
            var declaring = definition.GetDeclaringType();
            if (!declaring.IsNil)
            {
                return GetMetadataTypeName(declaring) + "+" + name;
            }

            var @namespace = _reader.GetString(definition.Namespace);
            return @namespace.Length == 0
                ? name
                : @namespace + "." + name;
        }

        private bool Arithmetic(
            DecodedInstruction instruction,
            in BlockContext context)
        {
            if (!TryPop(context.Stack, out var right) ||
                !TryPop(context.Stack, out var left) ||
                left.SpecialType != right.SpecialType ||
                left.Term.Type != _factory.IntegerType)
            {
                return false;
            }

            var @operator = instruction.OpCode switch
            {
                ILOpCode.Add or ILOpCode.Add_ovf =>
                    IrBinaryOperator.Add,
                ILOpCode.Sub or ILOpCode.Sub_ovf =>
                    IrBinaryOperator.Subtract,
                ILOpCode.Mul or ILOpCode.Mul_ovf =>
                    IrBinaryOperator.Multiply,
                ILOpCode.Div => IrBinaryOperator.Divide,
                ILOpCode.Rem => IrBinaryOperator.Remainder,
                _ => (IrBinaryOperator)(-1)
            };
            var raw = _factory.Binary(
                @operator,
                left.Term,
                right.Term);
            var overflowChecked = instruction.OpCode is
                ILOpCode.Add_ovf or
                ILOpCode.Sub_ovf or
                ILOpCode.Mul_ovf;
            if (overflowChecked || instruction.OpCode is
                    ILOpCode.Div or ILOpCode.Rem)
            {
                if (!CSharpScalarSemantics.TryGetIrIntegerRange(
                        left.SpecialType,
                        out var minimum,
                        out var maximum))
                {
                    return false;
                }

                if (instruction.OpCode == ILOpCode.Rem)
                {
                    var quotient = _factory.Binary(
                        IrBinaryOperator.Divide,
                        left.Term,
                        right.Term);
                    context.Builder.Assume(
                        context.Block,
                        Operation(instruction.Offset),
                        InRange(quotient, minimum, maximum));
                }

                context.Builder.Assume(
                    context.Block,
                    Operation(instruction.Offset),
                    InRange(raw, minimum, maximum));
                _mayThrow = true;
                context.Stack.Push(new IlValue(raw, left.SpecialType));
                return true;
            }

            if (left.SpecialType != SpecialType.System_Int32)
            {
                return false;
            }

            context.Stack.Push(new IlValue(
                WrapInt32(raw),
                SpecialType.System_Int32));
            return true;
        }

        private bool BooleanBinary(
            ILOpCode opCode,
            Stack<IlValue> stack)
        {
            if (!TryPop(stack, out var right) ||
                !TryPop(stack, out var left) ||
                left.SpecialType != SpecialType.System_Boolean ||
                right.SpecialType != SpecialType.System_Boolean)
            {
                return false;
            }

            var term = opCode switch
            {
                ILOpCode.And => _factory.Binary(
                    IrBinaryOperator.AndAlso,
                    left.Term,
                    right.Term),
                ILOpCode.Or => _factory.Binary(
                    IrBinaryOperator.OrElse,
                    left.Term,
                    right.Term),
                ILOpCode.Xor => _factory.Binary(
                    IrBinaryOperator.NotEqual,
                    left.Term,
                    right.Term),
                _ => null
            };
            if (term == null)
            {
                return false;
            }

            stack.Push(new IlValue(
                term,
                SpecialType.System_Boolean));
            return true;
        }

        private bool Negate(Stack<IlValue> stack)
        {
            if (!TryPop(stack, out var operand) ||
                operand.SpecialType != SpecialType.System_Int32)
            {
                return false;
            }

            stack.Push(new IlValue(
                WrapInt32(_factory.Unary(
                    IrUnaryOperator.Negate,
                    operand.Term)),
                SpecialType.System_Int32));
            return true;
        }

        private bool Compare(
            ILOpCode opCode,
            Stack<IlValue> stack)
        {
            if (!TryPop(stack, out var right) ||
                !TryPop(stack, out var left) ||
                !TryComparison(
                    opCode,
                    left,
                    right,
                    out var comparison))
            {
                return false;
            }

            stack.Push(new IlValue(
                comparison,
                SpecialType.System_Boolean));
            return true;
        }

        private bool BranchComparison(
            DecodedInstruction instruction,
            Stack<IlValue> stack,
            out IrTerm comparison)
        {
            if (!TryPop(stack, out var right) ||
                !TryPop(stack, out var left))
            {
                comparison = null!;
                return false;
            }

            var opCode = instruction.OpCode switch
            {
                ILOpCode.Beq or ILOpCode.Beq_s => ILOpCode.Ceq,
                ILOpCode.Bne_un or ILOpCode.Bne_un_s =>
                    ILOpCode.Bne_un,
                ILOpCode.Bge or ILOpCode.Bge_s => ILOpCode.Bge,
                ILOpCode.Bgt or ILOpCode.Bgt_s => ILOpCode.Cgt,
                ILOpCode.Ble or ILOpCode.Ble_s => ILOpCode.Ble,
                ILOpCode.Blt or ILOpCode.Blt_s => ILOpCode.Clt,
                _ => unchecked((ILOpCode)(-1))
            };
            return TryComparison(
                opCode,
                left,
                right,
                out comparison);
        }

        private bool TryComparison(
            ILOpCode opCode,
            IlValue left,
            IlValue right,
            out IrTerm comparison)
        {
            if (TryBooleanIntegerEquality(
                    left,
                    right,
                    out var booleanEquality))
            {
                comparison = opCode switch
                {
                    ILOpCode.Ceq => booleanEquality,
                    ILOpCode.Bne_un => _factory.Unary(
                        IrUnaryOperator.Not,
                        booleanEquality),
                    _ => null!
                };
                return comparison != null;
            }

            if (left.SpecialType != right.SpecialType ||
                left.Term.Type != right.Term.Type)
            {
                comparison = null!;
                return false;
            }

            var @operator = opCode switch
            {
                ILOpCode.Ceq => IrBinaryOperator.Equal,
                ILOpCode.Bne_un => IrBinaryOperator.NotEqual,
                ILOpCode.Cgt => IrBinaryOperator.GreaterThan,
                ILOpCode.Clt => IrBinaryOperator.LessThan,
                ILOpCode.Bge => IrBinaryOperator.GreaterThanOrEqual,
                ILOpCode.Ble => IrBinaryOperator.LessThanOrEqual,
                _ => (IrBinaryOperator)(-1)
            };
            if (left.Term.Type == _factory.BooleanType &&
                @operator is not (
                    IrBinaryOperator.Equal or
                    IrBinaryOperator.NotEqual))
            {
                comparison = null!;
                return false;
            }

            comparison = _factory.Binary(
                @operator,
                left.Term,
                right.Term);
            return true;
        }

        private bool TryBooleanIntegerEquality(
            IlValue left,
            IlValue right,
            out IrTerm equality)
        {
            if (left.SpecialType == SpecialType.System_Boolean &&
                TryBooleanLiteral(right, out var rightBoolean))
            {
                equality = rightBoolean
                    ? left.Term
                    : _factory.Unary(
                        IrUnaryOperator.Not,
                        left.Term);
                return true;
            }

            if (right.SpecialType == SpecialType.System_Boolean &&
                TryBooleanLiteral(left, out var leftBoolean))
            {
                equality = leftBoolean
                    ? right.Term
                    : _factory.Unary(
                        IrUnaryOperator.Not,
                        right.Term);
                return true;
            }

            equality = null!;
            return false;
        }

        private bool PushArgument(
            int index,
            Stack<IlValue> stack)
        {
            if (index < 0 || index >= _parameters.Length ||
                index >= _parameterTypes.Length ||
                !_parameterTypes[index].IsValid)
            {
                return false;
            }

            stack.Push(new IlValue(
                _factory.Variable(_parameters[index]),
                _parameterTypes[index].SpecialType));
            return true;
        }

        private bool PushLocal(
            int index,
            in BlockContext context)
        {
            if (index < 0 || index >= context.Locals.Length)
            {
                return false;
            }

            context.Stack.Push(new IlValue(
                _factory.Variable(context.Locals[index]),
                context.LocalTypes[index].SpecialType));
            return true;
        }

        private bool StoreLocal(
            int index,
            DecodedInstruction instruction,
            in BlockContext context)
        {
            if (index < 0 || index >= context.Locals.Length ||
                !TryPop(context.Stack, out var value) ||
                !TryCoerce(
                    value,
                    context.LocalTypes[index].SpecialType,
                    out value))
            {
                return false;
            }

            context.Builder.Assign(
                context.Block,
                Operation(instruction.Offset),
                context.Locals[index],
                value.Term);
            return true;
        }

        private bool TryBoolean(
            IlValue value,
            out IrTerm condition)
        {
            if (value.SpecialType == SpecialType.System_Boolean)
            {
                condition = value.Term;
                return true;
            }

            if (value.Term.Type == _factory.IntegerType)
            {
                condition = _factory.Binary(
                    IrBinaryOperator.NotEqual,
                    value.Term,
                    _factory.Integer(0));
                return true;
            }

            condition = null!;
            return false;
        }

        private bool TryCoerce(
            IlValue value,
            SpecialType target,
            out IlValue coerced)
        {
            if (value.SpecialType == target)
            {
                coerced = value;
                return true;
            }

            if (target == SpecialType.System_Boolean &&
                TryBooleanLiteral(value, out var boolean))
            {
                coerced = new IlValue(
                    _factory.Boolean(boolean),
                    SpecialType.System_Boolean);
                return true;
            }

            coerced = default;
            return false;
        }

        private static bool TryBooleanLiteral(
            IlValue value,
            out bool boolean)
        {
            if (value.SpecialType == SpecialType.System_Int32 &&
                value.Term is IrIntegerTerm integer &&
                integer.Value is 0 or 1)
            {
                boolean = integer.Value == 1;
                return true;
            }

            boolean = false;
            return false;
        }

        private IrTerm WrapInt32(IrTerm value)
        {
            const long modulus = 4294967296;
            var modulusTerm = _factory.Integer(modulus);
            var remainder = _factory.Binary(
                IrBinaryOperator.Remainder,
                value,
                modulusTerm);
            var unsigned = _factory.Conditional(
                _factory.Binary(
                    IrBinaryOperator.LessThan,
                    remainder,
                    _factory.Integer(0)),
                _factory.Binary(
                    IrBinaryOperator.Add,
                    remainder,
                    modulusTerm),
                remainder);
            return _factory.Conditional(
                _factory.Binary(
                    IrBinaryOperator.GreaterThan,
                    unsigned,
                    _factory.Integer(int.MaxValue)),
                _factory.Binary(
                    IrBinaryOperator.Subtract,
                    unsigned,
                    modulusTerm),
                unsigned);
        }

        private IrTerm InRange(
            IrTerm value,
            long minimum,
            long maximum)
        {
            return _factory.Binary(
                IrBinaryOperator.AndAlso,
                _factory.Binary(
                    IrBinaryOperator.GreaterThanOrEqual,
                    value,
                    _factory.Integer(minimum)),
                _factory.Binary(
                    IrBinaryOperator.LessThanOrEqual,
                    value,
                    _factory.Integer(maximum)));
        }

        private IrTerm DefaultValue(ScalarType type)
        {
            return type.SpecialType == SpecialType.System_Boolean
                ? _factory.Boolean(false)
                : _factory.Integer(0);
        }

        private IlValue Integer(int value)
        {
            return new IlValue(
                _factory.Integer(value),
                SpecialType.System_Int32);
        }

        private OperationId Operation(int offset)
        {
            return _factory.CreateOperation(
                "implementation-il:" + offset.ToString(
                    CultureInfo.InvariantCulture));
        }

        private static ILOpCode ReadOpCode(ref BlobReader reader)
        {
            var first = reader.ReadByte();
            var value = first == 0xfe
                ? 0xfe00 | reader.ReadByte()
                : first;
            return (ILOpCode)value;
        }

        private static bool TryPop(
            Stack<IlValue> stack,
            out IlValue value)
        {
            if (stack.Count == 0)
            {
                value = default;
                return false;
            }

            value = stack.Pop();
            return true;
        }

        private static bool TryPeek(
            Stack<IlValue> stack,
            out IlValue value)
        {
            if (stack.Count == 0)
            {
                value = default;
                return false;
            }

            value = stack.Peek();
            return true;
        }
    }

    private enum IlOperandSize
    {
        None,
        Byte,
        UInt16,
        SByte,
        Int32,
        Int64
    }

    private sealed class ScalarSignatureTypeProvider(
        IrFactory factory) :
        ISignatureTypeProvider<ScalarType, object?>
    {
        public ScalarType GetPrimitiveType(
            PrimitiveTypeCode typeCode)
        {
            return typeCode switch
            {
                PrimitiveTypeCode.Boolean => new ScalarType(
                    factory.BooleanType,
                    SpecialType.System_Boolean),
                PrimitiveTypeCode.Int32 => new ScalarType(
                    factory.IntegerType,
                    SpecialType.System_Int32),
                PrimitiveTypeCode.Int64 => new ScalarType(
                    factory.IntegerType,
                    SpecialType.System_Int64),
                _ => default
            };
        }

        public ScalarType GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            return default;
        }

        public ScalarType GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            return default;
        }

        public ScalarType GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            return default;
        }

        public ScalarType GetSZArrayType(ScalarType elementType)
        {
            return default;
        }

        public ScalarType GetPointerType(ScalarType elementType)
        {
            return default;
        }

        public ScalarType GetByReferenceType(ScalarType elementType)
        {
            return default;
        }

        public ScalarType GetPinnedType(ScalarType elementType)
        {
            return default;
        }

        public ScalarType GetModifiedType(
            ScalarType modifier,
            ScalarType unmodifiedType,
            bool isRequired)
        {
            return default;
        }

        public ScalarType GetArrayType(
            ScalarType elementType,
            ArrayShape shape)
        {
            return default;
        }

        public ScalarType GetGenericInstantiation(
            ScalarType genericType,
            ImmutableArray<ScalarType> typeArguments)
        {
            return default;
        }

        public ScalarType GetGenericMethodParameter(
            object? genericContext,
            int index)
        {
            return default;
        }

        public ScalarType GetGenericTypeParameter(
            object? genericContext,
            int index)
        {
            return default;
        }

        public ScalarType GetFunctionPointerType(
            MethodSignature<ScalarType> signature)
        {
            return default;
        }
    }

    private readonly record struct ScalarType(
        IrTypeId IrType,
        SpecialType SpecialType)
    {
        internal bool IsValid => SpecialType != SpecialType.None;

        internal static bool TryCreate(
            ITypeSymbol type,
            out ScalarType scalar)
        {
            scalar = type.SpecialType switch
            {
                SpecialType.System_Boolean => new ScalarType(
                    default,
                    SpecialType.System_Boolean),
                SpecialType.System_Int32 => new ScalarType(
                    default,
                    SpecialType.System_Int32),
                SpecialType.System_Int64 => new ScalarType(
                    default,
                    SpecialType.System_Int64),
                _ => default
            };
            return scalar.IsValid;
        }
    }

    private readonly record struct IlValue(
        IrTerm Term,
        SpecialType SpecialType);

    private readonly record struct DecodedInstruction(
        int Offset,
        ILOpCode OpCode,
        long Operand,
        int NextOffset,
        int BranchTarget)
    {
        internal bool IsBranch => OpCode.IsBranch();

        internal bool IsConditional => OpCode is not (
            ILOpCode.Br or ILOpCode.Br_s);
    }

    private sealed record Translation(
        IrProgram Program,
        ImmutableDictionary<IrInstructionId, IrRelationalSummary> Calls,
        bool MayThrow);
}
