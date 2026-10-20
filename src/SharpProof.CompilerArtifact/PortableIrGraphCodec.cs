using System.Collections.Immutable;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static partial class PortableIrGraphCodec
{
    internal const int MaximumGraphDepth = 256;
    private static readonly ImmutableArray<IrOpaquePurity> OpaquePurities =
        PortableIrWireCatalog.OpaquePurities;
    private static readonly ImmutableArray<IrUnaryOperator> UnaryOperators =
        PortableIrWireCatalog.UnaryOperators;
    private static readonly ImmutableArray<IrBinaryOperator> BinaryOperators =
        PortableIrWireCatalog.BinaryOperators;
    private static readonly ImmutableArray<IrHavocKind> HavocKinds =
        PortableIrWireCatalog.HavocKinds;
    internal static bool HasCompleteWireEnumCatalogs =>
        ImmutableArray.Create(
            IsComplete(OpaquePurities),
            IsComplete(UnaryOperators),
            IsComplete(BinaryOperators),
            IsComplete(HavocKinds),
            IsComplete(PortableIrWireCatalog.HavocOrigins),
            IsComplete(PortableIrWireCatalog.ExecutionSemantics),
            IsComplete(PortableIrWireCatalog.ExceptionKinds)
        ).All(static complete => complete);

    internal static bool HasCompleteSlotCatalogs =>
        IsCompleteSlots(PortableIrSlotCatalog.Terms, typeof(IrTermKind)) &&
        IsCompleteSlots(PortableIrSlotCatalog.Locations, typeof(IrLocationKind)) &&
        IsCompleteSlots(
            PortableIrSlotCatalog.Instructions,
            typeof(IrInstructionKind));

    private static bool IsComplete<T>(ImmutableArray<T> values) where T : struct, Enum
    {
        return values.SequenceEqual(Enum.GetValues(typeof(T)).Cast<T>());
    }

    private static bool IsCompleteSlots(
        ImmutableArray<PortableIrSlotMapping> mappings,
        Type enumType)
    {
        enumType = RepositoryParameterGuard.NotNull(enumType, nameof(enumType));
        RepositoryParameterGuard.RequireInitialized(mappings.IsDefault, nameof(mappings), "Array must be initialized.");
        return mappings.Select(static mapping => mapping.Kind)
            .SequenceEqual(Enum.GetNames(enumType), StringComparer.Ordinal);
    }

    internal static EncodedPortableIrGraph Encode(
        IrProgram program,
        ImmutableArray<IrTerm> roots,
        CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.RequireInitialized(roots.IsDefault, nameof(roots), "Array must be initialized.");
        program = ArgumentNullGuard.NotNull(program, nameof(program));

        return Encode(program.Factory, program, roots, variables: null, cancellationToken: cancellationToken);
    }

    internal static EncodedPortableIrGraph Encode(
        IrFactory factory, IrProgram? program, ImmutableArray<IrTerm> roots,
        ImmutableArray<IrVarId>? variables,
        CancellationToken cancellationToken)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.RequireInitialized(roots.IsDefault, nameof(roots), "Array must be initialized.");
        return Encode(factory, program, roots, variables, [], cancellationToken);
    }

    internal static EncodedPortableIrGraph Encode(
        IrFactory factory, IrProgram? program, ImmutableArray<IrTerm> roots,
        ImmutableArray<IrVarId>? variables, ImmutableArray<OperationId> operations,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.RequireInitialized(roots.IsDefault, nameof(roots), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(operations.IsDefault, nameof(operations), "Array must be initialized.");

        RepositoryParameterGuard.RequireInitialized(variables is { IsDefault: true }, nameof(variables), "Array must be initialized.");

        if (program != null && !ReferenceEquals(factory, program.Factory))
        {
            throw new ArgumentException("The program belongs to a different IR factory.", nameof(program));
        }

        return new Encoder(
            factory,
            program,
            roots,
            variables ?? [],
            operations,
            cancellationToken).Encode();
    }

    private static EncodedPortableIrGraph EncodeGraph(
        IrFactory factory,
        IrProgram? program,
        ImmutableArray<IrTerm> roots,
        ImmutableArray<IrVarId> variables,
        ImmutableArray<OperationId> operations,
        CancellationToken cancellationToken)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        RepositoryParameterGuard.RequireInitialized(operations.IsDefault, nameof(operations), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(roots.IsDefault, nameof(roots), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(variables.IsDefault, nameof(variables), "Array must be initialized.");
        return new Encoder(
            factory,
            program,
            roots,
            variables,
            operations,
            cancellationToken).Encode(includeInstructionIndices: false);
    }

    internal static DecodedPortableIrGraph Decode(
        PortableIrGraph graph,
        ImmutableArray<int>? externalVariableIndices,
        CancellationToken cancellationToken)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        return Decode(graph, externalVariableIndices, [], cancellationToken);
    }

    internal static DecodedPortableIrGraph Decode(
        PortableIrGraph graph, ImmutableArray<int>? externalVariableIndices,
        ImmutableArray<int> externalOperationIndices, CancellationToken cancellationToken)
    {
        graph = ArgumentNullGuard.NotNull(graph, nameof(graph));
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var decoded = new Decoder(graph, cancellationToken).Decode();
            RequireCanonicalEncoderImage(
                graph,
                decoded,
                externalVariableIndices ?? [],
                externalOperationIndices,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return decoded;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NullReferenceException)
        {
            throw Bad("The portable IR graph is malformed.", exception);
        }
    }

    private static void RequireCanonicalEncoderImage(
        PortableIrGraph graph,
        DecodedPortableIrGraph decoded,
        ImmutableArray<int> externalVariableIndices,
        ImmutableArray<int> externalOperationIndices,
        CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.RequireInitialized(externalOperationIndices.IsDefault, nameof(externalOperationIndices), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(externalVariableIndices.IsDefault, nameof(externalVariableIndices), "Array must be initialized.");
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        var previous = -1;
        var externalVariables = ImmutableArray.CreateBuilder<IrVarId>();
        foreach (var index in externalVariableIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(
                index >= 0 && index < decoded.Variables.Length && index > previous,
                "Portable IR external variable metadata is not canonical.");
            previous = index;
            externalVariables.Add(decoded.Variables[index]);
        }

        previous = -1;
        var externalOperations = ImmutableArray.CreateBuilder<OperationId>();
        foreach (var index in externalOperationIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(index >= 0 && index < decoded.Operations.Length && index > previous,
                "Portable IR external operation metadata is not canonical.");
            previous = index;
            externalOperations.Add(decoded.Operations[index]);
        }
        var canonical = EncodeGraph(
            decoded.Factory,
            decoded.Program,
            decoded.Roots,
            externalVariables.ToImmutable(),
            externalOperations.ToImmutable(),
            cancellationToken).Graph;
        canonical = canonical with
        {
            Members = canonical.Members.Select(member =>
            {
                member = RepositoryParameterGuard.NotNull(member, nameof(member));
                cancellationToken.ThrowIfCancellationRequested();
                return member with { DocumentationCommentId = CallDocumentationCommentId(member.Name) };
            }).ToImmutableArray()
        };
        var actual = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            graph,
            WorkerProtocolJson.SharedOptions).ToImmutableArray();
        cancellationToken.ThrowIfCancellationRequested();
        var expected = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            canonical,
            WorkerProtocolJson.SharedOptions).ToImmutableArray();
        cancellationToken.ThrowIfCancellationRequested();
        Require(
            actual.SequenceEqual(expected),
            "Portable IR metadata is not the canonical encoder image.");
    }

    private static string? CallDocumentationCommentId(string name)
    {
        const string prefix = "call:";
        const string delimiter = "::";
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var delimiterIndex = name.IndexOf(
            delimiter,
            prefix.Length,
            StringComparison.Ordinal);
        if (delimiterIndex <= prefix.Length)
        {
            return null;
        }

        var documentationCommentId = name.Substring(
            delimiterIndex + delimiter.Length);
        var displaySuffix = documentationCommentId.IndexOf('~');
        if (displaySuffix >= 0)
        {
            documentationCommentId = documentationCommentId.Substring(0, displaySuffix);
        }
        return documentationCommentId.StartsWith("M:", StringComparison.Ordinal) &&
            documentationCommentId.Length > 2
            ? documentationCommentId
            : null;
    }

    private static InvalidDataException Bad(string message, Exception? inner = null)
    {
        message = RepositoryParameterGuard.NotNull(message, nameof(message));
        return new(message, inner);
    }

    private static void Require(bool condition, string message)
    {
        message = RepositoryParameterGuard.NotNull(message, nameof(message));
        if (!condition)
        {
            throw Bad(message);
        }
    }

    private static PortableIrSlotMapping RequireCanonicalSlotMapping<TEnum>(
        PortableIrSlotMapping mapping,
        TEnum kind,
        int slotCount)
        where TEnum : struct, Enum
    {
        Require(mapping.Kind != null, $"Portable IR {kind} slots are not declared.");
        Require(!mapping.Slots.IsDefault, $"Portable IR {kind} slots are not declared.");
        Require(
            mapping.Slots!.Length == slotCount,
            $"Portable IR {kind} slots have an invalid shape.");
        return mapping;
    }

    private static void RequireCanonicalSlot(
        string kind,
        string role,
        int value)
    {
        Require(
            role != "unused" || value == -1,
            $"Portable IR {kind} slots are not canonical.");
    }

    private static void RequireCanonicalSlot(
        string kind,
        string role,
        long value)
    {
        Require(
            role != "unused" || value == 0L,
            $"Portable IR {kind} slots are not canonical.");
    }

    private static void RequireCanonicalSlot(
        string kind,
        string role,
        string? value)
    {
        Require(
            role != "unused" || value == null,
            $"Portable IR {kind} slots are not canonical.");
    }

    private static void RequireCanonicalSlot(
        string kind,
        string role,
        ImmutableArray<int> value)
    {
        Require(
            role != "unused" &&
            (role != "empty" || value.Length == 0),
            $"Portable IR {kind} slots are not canonical.");
    }

    private static void RequireCanonicalSlot(
        string kind,
        string role,
        PortableIrLocation? value)
    {
        Require(
            role != "unused" || value == null,
            $"Portable IR {kind} slots are not canonical.");
    }

    private static void RequireCanonicalTermSlots(PortableIrTerm row)
    {
        var mapping = RequireCanonicalSlotMapping(
            PortableIrGraphCodecProjections.TermSlotMapping(row.Kind),
            row.Kind,
            7);
        var kind = row.Kind.ToString();
        RequireCanonicalSlot(kind, mapping.Slots![0], row.A);
        RequireCanonicalSlot(kind, mapping.Slots[1], row.B);
        RequireCanonicalSlot(kind, mapping.Slots[2], row.C);
        RequireCanonicalSlot(kind, mapping.Slots[3], row.D);
        RequireCanonicalSlot(kind, mapping.Slots[4], row.Number);
        RequireCanonicalSlot(kind, mapping.Slots[5], row.Text);
        RequireCanonicalSlot(kind, mapping.Slots[6], row.Items);
        Require(row.Kind == IrTermKind.Integer || row.Bits == 0,
            "Only integer terms may carry raw integer bits.");
    }

    private static void RequireCanonicalInstructionSlots(
        PortableIrInstruction row)
    {
        var mapping = RequireCanonicalSlotMapping(
            PortableIrGraphCodecProjections.InstructionSlotMapping(row.Kind),
            row.Kind,
            6);
        var kind = row.Kind.ToString();
        RequireCanonicalSlot(kind, mapping.Slots![0], row.A);
        RequireCanonicalSlot(kind, mapping.Slots[1], row.B);
        RequireCanonicalSlot(kind, mapping.Slots[2], row.C);
        RequireCanonicalSlot(kind, mapping.Slots[3], -1);
        RequireCanonicalSlot(kind, mapping.Slots[4], row.Items);
        RequireCanonicalSlot(kind, mapping.Slots[5], row.Location);
        Require(row.Kind == IrInstructionKind.Havoc || row.Origin == IrHavocOrigin.Approximation,
            "Only havoc instructions may carry an origin.");
    }

    private static void RequireCanonicalLocationSlots(
        PortableIrLocation row)
    {
        var mapping = RequireCanonicalSlotMapping(
            PortableIrGraphCodecProjections.LocationSlotMapping(row.Kind),
            row.Kind,
            5);
        var kind = row.Kind.ToString();
        RequireCanonicalSlot(kind, mapping.Slots![0], row.A);
        RequireCanonicalSlot(kind, mapping.Slots[1], row.B);
        RequireCanonicalSlot(kind, mapping.Slots[2], -1);
        RequireCanonicalSlot(kind, mapping.Slots[3], -1);
        RequireCanonicalSlot(kind, mapping.Slots[4], row.Items);
    }

    private static int Wire<T>(T value /* SPINT001: all values valid - generic values are forwarded or serialized, including null when the type permits it */, ImmutableArray<T> values) where T : struct, Enum
    {

        RepositoryParameterGuard.RequireInitialized(values.IsDefault, nameof(values), "Array must be initialized.");
        var index = values.IndexOf(value);
        return index >= 0 ? index : throw Bad("Portable IR contains an unknown enum value.");
    }

    private static T Wire<T>(int value, ImmutableArray<T> values) where T : struct, Enum
    {
        return value >= 0 && value < values.Length
            ? values[value]
            : throw Bad("Portable IR contains an unknown enum value.");
    }

    private static ImmutableDictionary<T, int> Dense<T>(IEnumerable<T> values) where T : notnull
    {
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        var result = ImmutableDictionary.CreateBuilder<T, int>();
        foreach (var value in values)
        {
            if (result.ContainsKey(value))
            { throw new ArgumentException("An item with the same key has already been added. Key: " + value); }
            result.Add(value, result.Count);
        }

        return result.ToImmutable();
    }

    private sealed class EncodingTable<TSource, TRow>(
        Func<TSource, int, TRow> encode) where TSource : notnull
    {
        private readonly ImmutableDictionary<TSource, int>.Builder _indices = encode is null ? throw new System.ArgumentNullException(nameof(encode)) : ImmutableDictionary.CreateBuilder<TSource, int>();
        private readonly ImmutableArray<TRow>.Builder _rows = ImmutableArray.CreateBuilder<TRow>();

        internal ImmutableDictionary<TSource, int> Indices => _indices.ToImmutable();
        internal ImmutableArray<TRow> Rows => _rows.ToImmutable();

        internal bool TryGetValue(TSource source, out int index)
        {
            source = RepositoryParameterGuard.NotNull(source, nameof(source));
            return _indices.TryGetValue(source, out index);
        }

        internal int Add(TSource source)
        {
            if (_indices.TryGetValue(source, out var existing))
            {
                return existing;
            }

            var index = _rows.Count;
            _indices.Add(source, index);
            _rows.Add(default!);
            var row = encode(source, index);
            _rows[index] = row;
            return index;
        }
    }

    private sealed partial class Encoder
    {
        private readonly IrFactory _factory;
        private readonly IrProgram? _program;
        private readonly ImmutableArray<IrTerm> _roots;
        private readonly ImmutableArray<IrVarId> _extraVariables;
        private readonly ImmutableArray<OperationId> _extraOperations;
        private readonly CancellationToken _cancellationToken;
        private readonly EncodingTable<IrTypeId, PortableIrType> _types;
        private readonly EncodingTable<IrIdentityId, int> _identities;
        private readonly EncodingTable<IrVarId, PortableIrVariable> _variables;
        private readonly EncodingTable<IrMemberId, PortableIrMember> _members;
        private readonly EncodingTable<OperationId, PortableIrOperation> _operations;
        private readonly EncodingTable<IrId, PortableIrTerm> _terms;
        private ImmutableDictionary<IrId, int> _termDepths = ImmutableDictionary<IrId, int>.Empty;
        private ImmutableArray<IrBasicBlock> _blocks = [];
        private ImmutableDictionary<IrBlockId, int> _blockIndices = ImmutableDictionary<IrBlockId, int>.Empty;
        private ImmutableDictionary<IrInstructionId, int> _instructionIndices = ImmutableDictionary<IrInstructionId, int>.Empty;

        internal Encoder(
            IrFactory factory,
            IrProgram? program,
            ImmutableArray<IrTerm> roots,
            ImmutableArray<IrVarId> extraVariables,
            ImmutableArray<OperationId> extraOperations,
            CancellationToken cancellationToken)
        {
            RepositoryParameterGuard.RequireInitialized(extraOperations.IsDefault, nameof(extraOperations), "Array must be initialized.");
            RepositoryParameterGuard.RequireInitialized(extraVariables.IsDefault, nameof(extraVariables), "Array must be initialized.");
            factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
            RepositoryParameterGuard.RequireInitialized(roots.IsDefault, nameof(roots), "Array must be initialized.");
            (_factory, _program, _roots, _extraVariables, _cancellationToken) =
                (factory, program, roots, extraVariables, cancellationToken);
            _extraOperations = extraOperations;
            _types = new((id, _) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (TypeRow(id)));
            _identities = new(static (_, index) => index);
            _variables = new((id, _) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (VariableRow(id)));
            _members = new((id, _) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (MemberRow(id)));
            _operations = new((id, _) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (OperationRow(id)));
            _terms = new((id, _) => id.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(id)) : (TermRow(id)));
        }

        internal EncodedPortableIrGraph Encode(bool includeInstructionIndices = true)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            TypeIndex(_factory.BooleanType);
            TypeIndex(_factory.IntegerType);
            TypeIndex(_factory.StringType);
            TypeIndex(_factory.ObjectType);
            var roots = _roots.Select(TermIndex).ToImmutableArray();
            foreach (var variable in _extraVariables)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                VariableIndex(variable);
            }
            foreach (var operation in _extraOperations)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                OperationIndex(operation);
            }

            if (_program == null)
            {
                _blocks = [];
            }
            else
            {
                _blocks = [.. _program.Blocks.OrderBy(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Id.Value))];
            }
            _blockIndices = Dense(_blocks.Select(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Id)));
            if (includeInstructionIndices)
            {
                _instructionIndices = Dense(_blocks
                    .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                    .Select(static instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Id)));
            }
            var blocks = _blocks.Select(BlockRow).ToImmutableArray();
            _cancellationToken.ThrowIfCancellationRequested();
            var graph = new PortableIrGraph
            {
                Semantics = _factory.Semantics,
                HasProgram = _program != null,
                Types = _types.Rows,
                Identities = _identities.Rows,
                Variables = _variables.Rows,
                Members = _members.Rows,
                Operations = _operations.Rows,
                Terms = _terms.Rows,
                Blocks = blocks,
                Entry = _program == null ? -1 : BlockIndex(_program.Entry),
                Roots = roots
            };
            return new EncodedPortableIrGraph(
                graph, _variables.Indices, _instructionIndices, _operations.Indices);
        }

        private PortableIrTerm TermRow(IrId id)
        {
            RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
            _cancellationToken.ThrowIfCancellationRequested();
            var term = _factory.GetTerm(id);
            return PortableIrGraphCodecProjections.EncodeTerm(
                term,
                _factory.GetString,
                VariableIndex,
                TermIndex,
                MemberIndex,
                OperationIndex,
                OptionalTermIndex,
                TermIndices,
                value => Wire(value, OpaquePurities),
                value => Wire(value, UnaryOperators),
                value => Wire(value, BinaryOperators),
                TermRow);
        }

        private PortableIrTerm TermRow(
            IrTerm term, int a = -1, int b = -1, int c = -1, int d = -1,
            long number = 0, string? text = null, ImmutableArray<int>? items = null)
        {
            term = RepositoryParameterGuard.NotNull(term, nameof(term));
            return new(term.Kind, TypeIndex(term.Type), a, b, c, d, number, text) { Items = items ?? [] };
        }

        private PortableIrLocation LocationRow(IrLocation location)
        {
            location = RepositoryParameterGuard.NotNull(location, nameof(location));
            return PortableIrGraphCodecProjections.EncodeLocation(
                location,
                MemberIndex,
                OptionalTermIndex,
                TermIndex,
                TermIndices,
                (value, a, b, items) => value is null ? throw new System.ArgumentNullException(nameof(value)) : (new(
                    value.Kind, TypeIndex(value.Type), a, b)
                {
                    Items = items ?? []
                }));
        }

        private PortableIrBlock BlockRow(IrBasicBlock block)
        {
            block = RepositoryParameterGuard.NotNull(block, nameof(block));
            _cancellationToken.ThrowIfCancellationRequested();
            return new(
                block.Name.HasValue ? _factory.GetString(block.Name.Value) : null)
            {
                Instructions = block.Instructions.Select(InstructionRow).ToImmutableArray()
            };
        }

        private PortableIrInstruction InstructionRow(IrInstruction instruction)
        {
            instruction = RepositoryParameterGuard.NotNull(instruction, nameof(instruction));
            _cancellationToken.ThrowIfCancellationRequested();
            return PortableIrGraphCodecProjections.EncodeInstruction(
                instruction,
                TypeIndex,
                OperationIndex,
                VariableIndex,
                TermIndex,
                value => value.HasValue ? VariableIndex(value.Value) : -1,
                MemberIndex,
                OptionalTermIndex,
                value => Wire(value, HavocKinds),
                BlockIndex,
                TermIndices,
                values => values is null ? throw new System.ArgumentNullException(nameof(values)) : ([.. values.Select(VariableIndex).OrderBy(static index => index)]),
                LocationRow,
                InstructionRow);
        }

        private PortableIrInstruction InstructionRow(
            IrInstruction instruction, int operation, int a = -1, int b = -1, int c = -1,
            ImmutableArray<int>? items = null, PortableIrLocation? location = null)
        {
            instruction = RepositoryParameterGuard.NotNull(instruction, nameof(instruction));
            return new(
                instruction.Kind, operation,
                a, b, c, location: location)
            {
                Items = items ?? [],
                Origin = instruction is IrHavocInstruction havoc ? havoc.Origin : IrHavocOrigin.Approximation
            };
        }

        private int TypeIndex(IrTypeId id)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_types.TryGetValue(id, out var existing))
            {
                return existing;
            }

            var depth = 0;
            for (var current = id; ;)
            {
                depth++;
                if (depth > MaximumGraphDepth)
                {
                    throw Bad("Portable IR type depth exceeds the supported limit.");
                }
                var info = _factory.GetTypeInfo(current);
                if (info.Kind != IrTypeKind.Sequence ||
                    !info.ElementType.HasValue)
                {
                    break;
                }
                current = info.ElementType.Value;
            }
            return _types.Add(id);
        }

        private int VariableIndex(IrVarId id)
        {
            RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
            return _variables.Add(id);
        }

        private int MemberIndex(IrMemberId id)
        {
            RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
            return _members.Add(id);
        }

        private int OperationIndex(OperationId id)
        {
            RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
            return _operations.Add(id);
        }

        private int TermIndex(IrTerm term)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _factory.EnsureTerm(term, nameof(term));
            if (_terms.TryGetValue(term.Id, out var existing))
            {
                return existing;
            }
            var depth = IrTermAnalysis.GetDepth(term, _termDepths);
            _termDepths = depth.Memo;
            if (depth.Value > MaximumGraphDepth)
            {
                throw Bad("Portable IR term depth exceeds the supported limit.");
            }
            return _terms.Add(term.Id);
        }

        private int OptionalTermIndex(IrTerm? term)
        {
            return term == null ? -1 : TermIndex(term);
        }

        private ImmutableArray<int> TermIndices(IEnumerable<IrTerm> terms)
        {
            terms = RepositoryParameterGuard.NotNull(terms, nameof(terms));
            return [.. terms.Select(TermIndex)];
        }

        private int BlockIndex(IrBlockId id)
        {
            return _blockIndices.TryGetValue(id, out var index)
                    ? index : throw Bad("The IR graph references missing metadata.");
        }
    }

    private sealed class Decoder(
        PortableIrGraph _graph,
        CancellationToken _cancellationToken)
    {
        private readonly IrFactory _factory = new((_graph ?? throw new System.ArgumentNullException(nameof(_graph))).Semantics);
        private readonly ImmutableHashSet<IrMemberId>.Builder _distinctMembers = ImmutableHashSet.CreateBuilder<IrMemberId>();
        private readonly ImmutableHashSet<IrId>.Builder _distinctTerms = ImmutableHashSet.CreateBuilder<IrId>();
        private readonly ImmutableList<IrTypeId>.Builder _types = ImmutableList.CreateBuilder<IrTypeId>();
        private ImmutableArray<IrIdentityId> _identities = [];
        private ImmutableArray<IrVarId> _variables = [];
        private ImmutableArray<IrMemberId> _members = [];
        private ImmutableArray<OperationId> _operations = [];
        private readonly ImmutableList<IrTerm?>.Builder _terms = ImmutableList.CreateBuilder<IrTerm?>();
        private readonly ImmutableList<byte>.Builder _typeState = ImmutableList.CreateBuilder<byte>();
        private readonly ImmutableList<byte>.Builder _termState = ImmutableList.CreateBuilder<byte>();

        internal DecodedPortableIrGraph Decode()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            RequireGraphShape();
            DecodeTypes();
            DecodeIdentities();
            _variables = DecodeRows(_graph.Variables, "variable",
                row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (_factory.CreateVariable(row.Name, Type(row.Type))));
            _members = DecodeRows(_graph.Members, "member", DecodeMember);
            _operations = DecodeRows(_graph.Operations, "operation",
                DecodeOperation);
            _terms.Clear();
            _terms.AddRange(Enumerable.Repeat<IrTerm?>(null, _graph.Terms.Length));
            _termState.Clear();
            _termState.AddRange(Enumerable.Repeat((byte)0, _terms.Count));
            for (var index = 0; index < _terms.Count; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                DecodeTerm(index);
            }

            ImmutableArray<IrTerm> roots = [.. _graph.Roots.Select(Term)];
            var (program, instructions) = DecodeProgram();
            _cancellationToken.ThrowIfCancellationRequested();
            return new DecodedPortableIrGraph(
                _factory, program, roots, _variables, instructions, _operations);
        }

        private void RequireGraphShape()
        {
            Require(!_graph.Types.IsDefault && !_graph.Identities.IsDefault &&
                !_graph.Variables.IsDefault && !_graph.Members.IsDefault &&
                !_graph.Operations.IsDefault && !_graph.Terms.IsDefault &&
                !_graph.Blocks.IsDefault && !_graph.Roots.IsDefault,
                "Portable IR arrays cannot be null.");
            Require(
                _graph.HasProgram == (_graph.Blocks.Length != 0) &&
                (_graph.HasProgram || _graph.Entry == -1),
                "The portable IR program shape is invalid.");
        }

        private void DecodeTypes()
        {
            Require(_graph.Types.Length >= 4, "Portable IR is missing built-in types.");
            _types.Clear();
            _types.AddRange(Enumerable.Repeat(default(IrTypeId), _graph.Types.Length));
            _typeState.Clear();
            _typeState.AddRange(Enumerable.Repeat((byte)0, _types.Count));
            var builtIns = ImmutableArray.Create(
                (_factory.BooleanType, IrTypeKind.Boolean, "bool"),
                (_factory.IntegerType, IrTypeKind.Integer, "int"),
                (_factory.StringType, IrTypeKind.String, "string"),
                (_factory.ObjectType, IrTypeKind.Reference, "object"));
            for (var index = 0; index < builtIns.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var (id, kind, name) = builtIns[index];
                var row = Required(_graph.Types[index], "type row");
                Require(
                    row.Kind == kind && row.Name == name && row.Element == -1 && !row.ClosedSealedReference && row.ClosedClassBase == null && !row.InterfaceReference && row.ImplementedInterfaces is null &&
                    row.Width == _factory.GetTypeInfo(id).Width && row.Signed == _factory.GetTypeInfo(id).Signed,
                    "Portable IR built-in type metadata is invalid.");
                _types[index] = id;
                _typeState[index] = 2;
            }
            for (var index = builtIns.Length; index < _types.Count; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                DecodeType(index);
            }
        }

        private IrTypeId DecodeType(int index, int depth = 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Check(index, _types.Count, "type");
            if (_typeState[index] == 2)
            {
                return _types[index];
            }

            Require(_typeState[index] != 1, "Portable IR type metadata contains a cycle.");
            Require(depth < MaximumGraphDepth, "Portable IR type depth exceeds the supported limit.");
            _typeState[index] = 1;
            var row = Required(_graph.Types[index], "type row");
            Require(!string.IsNullOrWhiteSpace(row.Name), "Portable IR type metadata is invalid.");
            Require(!row.ClosedSealedReference || _graph.Semantics == IrExecutionSemantics.Total && row.Kind == IrTypeKind.Reference,
                "Only Total nominal reference types may carry closed sealed certificates.");
            Require(row.Element >= -1, "Portable IR type metadata is invalid.");
            var decodedType = row.Kind switch
            {
                IrTypeKind.Integer when row.Element == -1 && row.Width is 8 or 16 or 32 or 64 =>
                    _factory.GetOrCreateIntegerType(row.Width, row.Signed),
                IrTypeKind.Reference when row.Element == -1 =>
                    _factory.GetOrCreateReferenceType(_factory.CreateIdentity(), row.Name),
                IrTypeKind.Sequence => _factory.GetOrCreateSequenceType(
                    _factory.CreateIdentity(), DecodeType(row.Element, depth + 1), row.Name),
                _ => throw Bad("Portable IR contains a non-canonical scalar type.")
            };
            _types[index] = decodedType;
            var info = _factory.GetTypeInfo(_types[index]);
            if (row.ClosedSealedReference)
            { _factory.RegisterClosedSealedReferenceType(_types[index]); }
            if (row.InterfaceReference)
            {
                Require(_graph.Semantics == IrExecutionSemantics.Total && row.Kind == IrTypeKind.Reference && !row.ClosedSealedReference && row.ClosedClassBase == null,
                    "Only Total nominal interfaces may carry interface certificates.");
                _factory.RegisterInterfaceReferenceType(_types[index]);
            }
            if (row.ClosedClassBase.HasValue)
            {
                Require(_graph.Semantics == IrExecutionSemantics.Total && row.Kind == IrTypeKind.Reference && row.ClosedClassBase >= -1,
                    "Only Total nominal reference types may carry class certificates.");
                var parent = row.ClosedClassBase == -1 ? (IrTypeId?)null : DecodeType(row.ClosedClassBase.Value, depth + 1);
                Require(!parent.HasValue || _factory.TryGetClosedClassReferenceBase(parent.Value, out _),
                    "A class certificate requires a certified base.");
                _factory.RegisterClosedClassReferenceType(_types[index], parent);
            }
            if (row.ImplementedInterfaces is { } implementedInterfaces)
            {
                Require(_graph.Semantics == IrExecutionSemantics.Total && row.Kind == IrTypeKind.Reference && !implementedInterfaces.IsDefault && implementedInterfaces.Length is > 0 and <= 128,
                    "Implemented interface metadata requires a bounded nominal reference type.");
                var previous = -1;
                foreach (var implemented in implementedInterfaces)
                {
                    Require(implemented > previous, "Implemented interfaces must be ordered distinct type indices.");
                    var interfaceType = DecodeType(implemented, depth + 1);
                    Require(_factory.IsInterfaceReferenceType(interfaceType), "Implemented interfaces require certified interface types.");
                    _factory.RegisterImplementedReferenceInterface(_types[index], interfaceType);
                    previous = implemented;
                }
            }
            Require(
                info.Kind == row.Kind &&
                info.Width == row.Width && info.Signed == row.Signed &&
                (info.Kind != IrTypeKind.Integer || _factory.GetString(info.Name) == row.Name) &&
                info.ElementType == (row.Element == -1 ? null : _types[row.Element]),
                "Portable IR type metadata is inconsistent.");
            _typeState[index] = 2;
            return _types[index];
        }

        private void DecodeIdentities()
        {
            var identities = ImmutableArray.CreateBuilder<IrIdentityId>(_graph.Identities.Length);
            for (var index = 0; index < _graph.Identities.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                Require(
                    _graph.Identities[index] == index,
                    "Portable IR identities are not canonical.");
                identities.Add(_factory.CreateIdentity());
            }
            _identities = identities.MoveToImmutable();
        }

        private IrMemberId DecodeMember(PortableIrMember row)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var name = row.Name ?? throw Bad("Portable IR member metadata is invalid.");
            Require(!row.ParameterTypes.IsDefault, "Portable IR member parameters cannot be null.");
            var member = _factory.GetOrCreateMemberSnapshot(
                Item(row.Identity, _identities, "identity"),
                Type(row.DeclaringType), name,
                Type(row.ReturnType), row.IsStatic, row.ParameterTypes.Select(Type).ToImmutableArray());
            Require(!_distinctMembers.Contains(member), "Portable IR member equality partitions collapse.");
            _distinctMembers.Add(member);
            return member;
        }

        private IrTerm DecodeTerm(int index, int depth = 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Check(index, _terms.Count, "term");
            if (_termState[index] == 2)
            {
                return _terms[index]!;
            }

            Require(_termState[index] != 1, "Portable IR terms contain a cycle.");
            Require(depth < MaximumGraphDepth, "Portable IR term depth exceeds the supported limit.");
            _termState[index] = 1;
            var row = Required(_graph.Terms[index], "term row");
            Require(!row.Items.IsDefault, "Portable IR term metadata is invalid.");
            RequireCanonicalTermSlots(row);
            var term = PortableIrGraphCodecProjections.DecodeTerm(
                row,
                _factory,
                depth,
                Type,
                DecodeTerm,
                OptionalTerm,
                Variable,
                Member,
                Operation,
                (indices, currentDepth) =>
                    indices.IsDefault ? throw Bad("Portable IR projection indices cannot be null.") : ([.. indices.Select(selectedIndex => DecodeTerm(selectedIndex, currentDepth + 1))]),
                value => Wire(value, OpaquePurities),
                value => Wire(value, UnaryOperators),
                value => Wire(value, BinaryOperators),
                static () => Bad("Portable IR term metadata is invalid."));
            Require(
                term.Kind == row.Kind &&
                term.Type == Type(row.Type) &&
                !_distinctTerms.Contains(term.Id),
                "Portable IR term equality or type metadata is inconsistent.");
            _distinctTerms.Add(term.Id);
            _terms[index] = term;
            _termState[index] = 2;
            return term;
        }

        private (IrProgram? Program, ImmutableArray<IrInstruction> Instructions) DecodeProgram()
        {
            if (!_graph.HasProgram)
            {
                return (null, []);
            }

            var builder = new IrProgramBuilder(_factory);
            var blockBuilder = ImmutableArray.CreateBuilder<IrBlockId>();
            for (var index = 0; index < _graph.Blocks.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var row = Required(_graph.Blocks[index], "block row");
                Require(!row.Instructions.IsDefault, "Portable IR instruction arrays cannot be null.");
                RequireCanonicalOptionalText(row.Name, "block name");
                blockBuilder.Add(builder.CreateBlock(row.Name));
            }
            var blocks = blockBuilder.ToImmutable();
            builder.SetEntry(Block(_graph.Entry, blocks));
            var instructions = ImmutableArray.CreateBuilder<IrInstruction>();
            for (var index = 0; index < _graph.Blocks.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                foreach (var row in _graph.Blocks[index].Instructions)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    instructions.Add(Instruction(builder, blocks[index], blocks, row));
                }
            }

            return (builder.Build(), instructions.ToImmutable());
        }

        private IrInstruction Instruction(
            IrProgramBuilder builder, IrBlockId block, ImmutableArray<IrBlockId> blocks,
            PortableIrInstruction? row)
        {
            RepositoryParameterGuard.RequireInitialized(blocks.IsDefault, nameof(blocks), "Array must be initialized.");
            RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
            builder = RepositoryParameterGuard.NotNull(builder, nameof(builder));
            _cancellationToken.ThrowIfCancellationRequested();
            row = Required(row, "instruction row");
            Require(!row.Items.IsDefault, "Portable IR instruction metadata is invalid.");
            RequireCanonicalInstructionSlots(row);
            if (row.Kind == IrInstructionKind.Havoc)
            {
                RequireCanonicalVariableIndices(row.Items!);
            }
            return PortableIrGraphCodecProjections.DecodeInstruction(
                builder,
                block,
                row,
                Type,
                Operation,
                Variable,
                Member,
                index => OptionalTerm(index),
                Term,
                OptionalVariable,
                value => Wire(value, HavocKinds),
                index => Block(index, blocks),
                value => Location(builder, value),
                indices => indices.IsDefault ? throw Bad("Portable IR projection indices cannot be null.") : ([.. indices.Select(Term)]),
                indices => indices.IsDefault ? throw Bad("Portable IR projection indices cannot be null.") : ([.. indices.Select(Variable)]),
                static () => Bad("Portable IR instruction metadata is invalid."));
        }

        private IrLocation Location(IrProgramBuilder builder, PortableIrLocation? row)
        {
            builder = RepositoryParameterGuard.NotNull(builder, nameof(builder));
            _cancellationToken.ThrowIfCancellationRequested();
            row = Required(row, "location row");
            Require(!row.Items.IsDefault, "Portable IR location metadata is invalid.");
            RequireCanonicalLocationSlots(row);
            var location = PortableIrGraphCodecProjections.DecodeLocation(
                builder,
                row,
                Member,
                index => OptionalTerm(index),
                Term,
                indices => indices.IsDefault ? throw Bad("Portable IR projection indices cannot be null.") : ([.. indices.Select(Term)]));
            Require(
                location.Type == Type(row.Type),
                "Portable IR location type metadata is inconsistent.");
            return location;
        }

        private ImmutableArray<TResult> DecodeRows<TRow, TResult>(
            ImmutableArray<TRow> rows,
            string kind,
            Func<TRow, TResult> decode) where TRow : class
        {
            decode = RepositoryParameterGuard.NotNull(decode, nameof(decode));
            var result = ImmutableArray.CreateBuilder<TResult>();
            for (var index = 0; index < rows.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                result.Add(decode(Required(rows[index], $"{kind} row")));
            }

            return result.ToImmutable();
        }

        private OperationId DecodeOperation(PortableIrOperation row)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            RequireCanonicalOptionalText(row.Description, "operation description");
            return _factory.CreateOperation(row.Description, row.SourceSpan is { } span
                ? new IrSourceSpan(span.Document, span.Start, span.Length, span.Line, span.Column) : null);
        }

        private static void RequireCanonicalOptionalText(string? value, string kind)
        {
            Require(
                value == null || !string.IsNullOrWhiteSpace(value),
                $"Portable IR {kind} cannot be whitespace.");
        }

        private void RequireCanonicalVariableIndices(ImmutableArray<int> indices)
        {
            if (indices.IsDefault)
            { throw new ArgumentException("Array must be initialized.", nameof(indices)); }
            var previous = -1;
            foreach (var index in indices)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                Check(index, _variables.Length, "variable");
                Require(
                    index > previous,
                    "Portable IR havoc variables are not canonical.");
                previous = index;
            }
        }

        private static T Required<T>(T? value, string kind) where T : class
        {
            kind = RepositoryParameterGuard.NotNull(kind, nameof(kind));
            return value ?? throw Bad($"Portable IR {kind}s cannot be null.");
        }

        private IrTypeId Type(int index)
        {
            Check(index, _types.Count, "type");
            return DecodeType(index);
        }

        private IrVarId Variable(int index)
        {
            return Item(index, _variables, "variable");
        }

        private IrMemberId Member(int index)
        {
            return Item(index, _members, "member");
        }

        private OperationId Operation(int index)
        {
            return Item(index, _operations, "operation");
        }

        private IrTerm Term(int index)
        {
            return DecodeTerm(index);
        }

        private IrTerm? OptionalTerm(int index, int depth = 0)
        {
            Require(index >= -1, "Portable IR contains an invalid optional term index.");
            return index == -1 ? null : DecodeTerm(index, depth + 1);
        }

        private IrVarId? OptionalVariable(int index)
        {
            Require(index >= -1, "Portable IR contains an invalid optional variable index.");
            return index == -1 ? null : Variable(index);
        }

        private static IrBlockId Block(int index, ImmutableArray<IrBlockId> blocks)
        {
            RepositoryParameterGuard.RequireInitialized(blocks.IsDefault, nameof(blocks), "Array must be initialized.");
            return Item(index, blocks, "block");
        }

        private static T Item<T>(int index, ImmutableArray<T> values, string kind)
        {
            RepositoryParameterGuard.RequireInitialized(values.IsDefault, nameof(values), "Array must be initialized.");
            Check(index, values.Length, kind);
            return values[index];
        }

        private static void Check(int index, int length, string kind)
        {
            Require(
                index >= 0 && index < length,
                $"Portable IR references an invalid {kind} index.");
        }
    }
}
