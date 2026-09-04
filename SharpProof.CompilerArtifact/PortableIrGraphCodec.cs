using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

internal static partial class PortableIrGraphCodec
{
    internal const int MaximumGraphDepth = 256;
    private static readonly IrOpaquePurity[] OpaquePurities =
        PortableIrWireCatalog.OpaquePurities;
    private static readonly IrUnaryOperator[] UnaryOperators =
        PortableIrWireCatalog.UnaryOperators;
    private static readonly IrBinaryOperator[] BinaryOperators =
        PortableIrWireCatalog.BinaryOperators;
    private static readonly IrHavocKind[] HavocKinds =
        PortableIrWireCatalog.HavocKinds;

    internal static bool HasCompleteWireEnumCatalogs =>
        new[] {
            IsComplete(OpaquePurities),
            IsComplete(UnaryOperators),
            IsComplete(BinaryOperators),
            IsComplete(HavocKinds)
        }.All(static complete => complete);

    internal static bool HasCompleteSlotCatalogs =>
        IsCompleteSlots(PortableIrSlotCatalog.Terms, typeof(IrTermKind)) &&
        IsCompleteSlots(PortableIrSlotCatalog.Locations, typeof(IrLocationKind)) &&
        IsCompleteSlots(
            PortableIrSlotCatalog.Instructions,
            typeof(IrInstructionKind));

    private static bool IsComplete<T>(T[] values) where T : struct, Enum
    {
        return values.SequenceEqual(Enum.GetValues(typeof(T)).Cast<T>());
    }

    private static bool IsCompleteSlots(
        IReadOnlyList<PortableIrSlotMapping> mappings,
        Type enumType)
    {
        return mappings.Select(static mapping => mapping.Kind)
            .SequenceEqual(Enum.GetNames(enumType), StringComparer.Ordinal);
    }

    internal static EncodedPortableIrGraph Encode(
        IrProgram program,
        IReadOnlyList<IrTerm> roots,
        CancellationToken cancellationToken = default)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));

        return Encode(program.Factory, program, roots, cancellationToken: cancellationToken);
    }

    internal static EncodedPortableIrGraph Encode(
        IrFactory factory, IrProgram? program, IReadOnlyList<IrTerm> roots,
        IReadOnlyList<IrVarId>? variables = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        roots = ArgumentNullGuard.NotNull(roots, nameof(roots));

        if (program != null && !ReferenceEquals(factory, program.Factory))
        {
            throw new ArgumentException("The program belongs to a different IR factory.", nameof(program));
        }

        return new Encoder(
            factory,
            program,
            roots,
            variables ?? [],
            cancellationToken).Encode();
    }

    internal static DecodedPortableIrGraph Decode(
        PortableIrGraph graph,
        IReadOnlyList<int>? externalVariableIndices = null,
        CancellationToken cancellationToken = default)
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
        IReadOnlyList<int> externalVariableIndices,
        CancellationToken cancellationToken)
    {
        var previous = -1;
        var externalVariables = new List<IrVarId>(externalVariableIndices.Count);
        foreach (var index in externalVariableIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Require(
                index >= 0 && index < decoded.Variables.Count && index > previous,
                "Portable IR external variable metadata is not canonical.");
            previous = index;
            externalVariables.Add(decoded.Variables[index]);
        }

        var canonical = Encode(
            decoded.Factory,
            decoded.Program,
            decoded.Roots,
            externalVariables,
            cancellationToken).Graph;
        foreach (var member in canonical.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            member.DocumentationCommentId =
                CallDocumentationCommentId(member.Name);
        }
        var actual = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            graph,
            WorkerProtocolJson.SharedOptions);
        cancellationToken.ThrowIfCancellationRequested();
        var expected = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            canonical,
            WorkerProtocolJson.SharedOptions);
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
        return new(message, inner);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw Bad(message);
        }
    }

    private static PortableIrSlotMapping RequireCanonicalSlotMapping<TEnum>(
        IReadOnlyList<PortableIrSlotMapping> catalog,
        TEnum kind,
        int slotCount)
        where TEnum : struct, Enum
    {
        var mapping = catalog.FirstOrDefault(candidate =>
            string.Equals(candidate.Kind, kind.ToString(), StringComparison.Ordinal));
        Require(mapping.Kind != null, $"Portable IR {kind} slots are not declared.");
        Require(mapping.Slots != null, $"Portable IR {kind} slots are not declared.");
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
        int[] value)
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
            PortableIrSlotCatalog.Terms,
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
    }

    private static void RequireCanonicalInstructionSlots(
        PortableIrInstruction row)
    {
        var mapping = RequireCanonicalSlotMapping(
            PortableIrSlotCatalog.Instructions,
            row.Kind,
            6);
        var kind = row.Kind.ToString();
        RequireCanonicalSlot(kind, mapping.Slots![0], row.A);
        RequireCanonicalSlot(kind, mapping.Slots[1], row.B);
        RequireCanonicalSlot(kind, mapping.Slots[2], row.C);
        RequireCanonicalSlot(kind, mapping.Slots[3], -1);
        RequireCanonicalSlot(kind, mapping.Slots[4], row.Items);
        RequireCanonicalSlot(kind, mapping.Slots[5], row.Location);
    }

    private static void RequireCanonicalLocationSlots(
        PortableIrLocation row)
    {
        var mapping = RequireCanonicalSlotMapping(
            PortableIrSlotCatalog.Locations,
            row.Kind,
            5);
        var kind = row.Kind.ToString();
        RequireCanonicalSlot(kind, mapping.Slots![0], row.A);
        RequireCanonicalSlot(kind, mapping.Slots[1], row.B);
        RequireCanonicalSlot(kind, mapping.Slots[2], -1);
        RequireCanonicalSlot(kind, mapping.Slots[3], -1);
        RequireCanonicalSlot(kind, mapping.Slots[4], row.Items);
    }

    private static int Wire<T>(T value, T[] values) where T : struct, Enum
    {
        var index = Array.IndexOf(values, value);
        return index >= 0 ? index : throw Bad("Portable IR contains an unknown enum value.");
    }

    private static T Wire<T>(int value, T[] values) where T : struct, Enum
    {
        return value >= 0 && value < values.Length
            ? values[value]
            : throw Bad("Portable IR contains an unknown enum value.");
    }

    private static Dictionary<T, int> Dense<T>(IEnumerable<T> values) where T : notnull
    {
        var result = new Dictionary<T, int>();
        foreach (var value in values)
        {
            result.Add(value, result.Count);
        }

        return result;
    }

    private sealed class EncodingTable<TSource, TRow>(
        Func<TSource, int, TRow> encode) where TSource : notnull
    {
        private readonly Dictionary<TSource, int> _indices = [];
        private readonly List<TRow> _rows = [];

        internal IReadOnlyDictionary<TSource, int> Indices => _indices;
        internal TRow[] Rows => [.. _rows];

        internal int Add(TSource source)
        {
            if (_indices.TryGetValue(source, out var existing))
            {
                return existing;
            }

            var index = _rows.Count;
            _indices.Add(source, index);
            _rows.Add(default!);
            _rows[index] = encode(source, index);
            return index;
        }
    }

    private sealed partial class Encoder
    {
        private readonly IrFactory _factory;
        private readonly IrProgram? _program;
        private readonly IReadOnlyList<IrTerm> _roots;
        private readonly IReadOnlyList<IrVarId> _extraVariables;
        private readonly CancellationToken _cancellationToken;
        private readonly EncodingTable<IrTypeId, PortableIrType> _types;
        private readonly EncodingTable<IrIdentityId, int> _identities;
        private readonly EncodingTable<IrVarId, PortableIrVariable> _variables;
        private readonly EncodingTable<IrMemberId, PortableIrMember> _members;
        private readonly EncodingTable<OperationId, PortableIrOperation> _operations;
        private readonly EncodingTable<IrId, PortableIrTerm> _terms;
        private readonly Dictionary<IrId, int> _termDepths = [];
        private IrBasicBlock[] _blocks = [];
        private Dictionary<IrBlockId, int> _blockIndices = [];
        private Dictionary<IrInstructionId, int> _instructionIndices = [];

        internal Encoder(
            IrFactory factory,
            IrProgram? program,
            IReadOnlyList<IrTerm> roots,
            IReadOnlyList<IrVarId> extraVariables,
            CancellationToken cancellationToken)
        {
            (_factory, _program, _roots, _extraVariables, _cancellationToken) =
                (factory, program, roots, extraVariables, cancellationToken);
            _types = new((id, _) => TypeRow(id));
            _identities = new(static (_, index) => index);
            _variables = new((id, _) => VariableRow(id));
            _members = new((id, _) => MemberRow(id));
            _operations = new((id, _) => OperationRow(id));
            _terms = new((id, _) => TermRow(id));
        }

        internal EncodedPortableIrGraph Encode()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            TypeIndex(_factory.BooleanType);
            TypeIndex(_factory.IntegerType);
            TypeIndex(_factory.StringType);
            TypeIndex(_factory.ObjectType);
            var roots = _roots.Select(TermIndex).ToArray();
            foreach (var variable in _extraVariables)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                VariableIndex(variable);
            }

            if (_program == null)
            {
                _blocks = [];
            }
            else
            {
                var programBlocks = _program.Blocks;
                var builderOrder = true;
                for (var index = 0; index < programBlocks.Length; index++)
                {
                    if (programBlocks[index].Id.Value != index)
                    {
                        builderOrder = false;
                        break;
                    }
                }

                _blocks = builderOrder
                    ? [.. programBlocks]
                    : [.. programBlocks.OrderBy(
                        static block => block.Id.Value)];
            }
            _blockIndices = Dense(_blocks.Select(static block => block.Id));
            _instructionIndices = Dense(_blocks
                .SelectMany(static block => block.Instructions)
                .Select(static instruction => instruction.Id));
            var blocks = _blocks.Select(BlockRow).ToArray();
            _cancellationToken.ThrowIfCancellationRequested();
            var graph = new PortableIrGraph
            {
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
                graph, _variables.Indices, _instructionIndices);
        }

        private PortableIrTerm TermRow(IrId id)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var term = _factory.GetTerm(id);
            return PortableIrGraphCodecProjections.EncodeTerm(
                term,
                TypeIndex,
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
                TermRow,
                static () => Bad("Unknown IR term kind."));
        }

        private PortableIrTerm TermRow(
            IrTerm term, int a = -1, int b = -1, int c = -1, int d = -1,
            long number = 0, string? text = null, int[]? items = null)
        {
            return new(term.Kind, TypeIndex(term.Type), a, b, c, d, number, text, items);
        }

        private PortableIrLocation LocationRow(IrLocation location)
        {
            return PortableIrGraphCodecProjections.EncodeLocation(
                location,
                TypeIndex,
                MemberIndex,
                OptionalTermIndex,
                TermIndex,
                TermIndices,
                (value, a, b, items) => new(
                    value.Kind, TypeIndex(value.Type), a, b, items),
                static () => Bad("Unknown IR location kind."));
        }

        private PortableIrBlock BlockRow(IrBasicBlock block)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return new(
                block.Name.HasValue ? _factory.GetString(block.Name.Value) : null,
                [.. block.Instructions.Select(InstructionRow)]);
        }

        private PortableIrInstruction InstructionRow(IrInstruction instruction)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            return PortableIrGraphCodecProjections.EncodeInstruction(
                instruction,
                OperationIndex,
                VariableIndex,
                TermIndex,
                value => value.HasValue ? VariableIndex(value.Value) : -1,
                MemberIndex,
                OptionalTermIndex,
                value => Wire(value, HavocKinds),
                BlockIndex,
                TermIndices,
                values => [.. values.Select(VariableIndex).OrderBy(static index => index)],
                LocationRow,
                InstructionRow,
                static () => Bad("Unknown IR instruction kind."));
        }

        private PortableIrInstruction InstructionRow(
            IrInstruction instruction, int operation, int a = -1, int b = -1, int c = -1,
            int[]? items = null, PortableIrLocation? location = null)
        {
            return new(
                instruction.Kind, operation,
                a, b, c, items, location);
        }

        private int TypeIndex(IrTypeId id)
        {
            _cancellationToken.ThrowIfCancellationRequested();
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
            return _variables.Add(id);
        }

        private int MemberIndex(IrMemberId id)
        {
            return _members.Add(id);
        }

        private int OperationIndex(OperationId id)
        {
            return _operations.Add(id);
        }

        private int TermIndex(IrTerm term)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            _factory.EnsureTerm(term, nameof(term));
            if (_terms.Indices.TryGetValue(term.Id, out var existing))
            {
                return existing;
            }
            if (IrTermAnalysis.GetDepth(term, _termDepths) > MaximumGraphDepth)
            {
                throw Bad("Portable IR term depth exceeds the supported limit.");
            }
            return _terms.Add(term.Id);
        }

        private int OptionalTermIndex(IrTerm? term)
        {
            return term == null ? -1 : TermIndex(term);
        }

        private int[] TermIndices(IEnumerable<IrTerm> terms)
        {
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
        private readonly IrFactory _factory = new();
        private readonly HashSet<IrMemberId> _distinctMembers = [];
        private readonly HashSet<IrId> _distinctTerms = [];
        private IrTypeId[] _types = [];
        private IrIdentityId[] _identities = [];
        private IrVarId[] _variables = [];
        private IrMemberId[] _members = [];
        private OperationId[] _operations = [];
        private IrTerm?[] _terms = [];
        private byte[] _typeState = [];
        private byte[] _termState = [];

        internal DecodedPortableIrGraph Decode()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            RequireGraphShape();
            DecodeTypes();
            DecodeIdentities();
            _variables = DecodeRows(_graph.Variables, "variable",
                row => _factory.CreateVariable(row.Name, Type(row.Type)));
            _members = DecodeRows(_graph.Members, "member", DecodeMember);
            _operations = DecodeRows(_graph.Operations, "operation",
                DecodeOperation);
            _terms = new IrTerm?[_graph.Terms.Length];
            _termState = new byte[_terms.Length];
            for (var index = 0; index < _terms.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                DecodeTerm(index);
            }

            IrTerm[] roots = [.. _graph.Roots.Select(Term)];
            var (program, blocks, instructions) = DecodeProgram();
            _cancellationToken.ThrowIfCancellationRequested();
            return new DecodedPortableIrGraph(
                _factory, program, roots, _variables, blocks, instructions);
        }

        private void RequireGraphShape()
        {
            object?[] arrays = [
                _graph.Types, _graph.Identities, _graph.Variables, _graph.Members,
                _graph.Operations, _graph.Terms, _graph.Blocks, _graph.Roots
            ];
            Require(arrays.All(static value => value != null), "Portable IR arrays cannot be null.");
            Require(
                _graph.HasProgram == (_graph.Blocks.Length != 0) &&
                (_graph.HasProgram || _graph.Entry == -1),
                "The portable IR program shape is invalid.");
        }

        private void DecodeTypes()
        {
            Require(_graph.Types.Length >= 4, "Portable IR is missing built-in types.");
            _types = new IrTypeId[_graph.Types.Length];
            _typeState = new byte[_types.Length];
            var builtIns = new[] {
                (_factory.BooleanType, IrTypeKind.Boolean, "bool"),
                (_factory.IntegerType, IrTypeKind.Integer, "int"),
                (_factory.StringType, IrTypeKind.String, "string"),
                (_factory.ObjectType, IrTypeKind.Reference, "object")
            };
            for (var index = 0; index < builtIns.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var (id, kind, name) = builtIns[index];
                var row = Required(_graph.Types[index], "type row");
                Require(
                    row.Kind == kind && row.Name == name && row.Element == -1,
                    "Portable IR built-in type metadata is invalid.");
                _types[index] = id;
                _typeState[index] = 2;
            }
            for (var index = builtIns.Length; index < _types.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                DecodeType(index);
            }
        }

        private IrTypeId DecodeType(int index, int depth = 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Check(index, _types.Length, "type");
            if (_typeState[index] == 2)
            {
                return _types[index];
            }

            Require(_typeState[index] != 1, "Portable IR type metadata contains a cycle.");
            Require(depth < MaximumGraphDepth, "Portable IR type depth exceeds the supported limit.");
            _typeState[index] = 1;
            var row = Required(_graph.Types[index], "type row");
            Require(!string.IsNullOrWhiteSpace(row.Name), "Portable IR type metadata is invalid.");
            Require(row.Element >= -1, "Portable IR type metadata is invalid.");
            _types[index] = row.Kind switch
            {
                IrTypeKind.Reference when row.Element == -1 =>
                    _factory.GetOrCreateReferenceType(_factory.CreateIdentity(), row.Name),
                IrTypeKind.Sequence => _factory.GetOrCreateSequenceType(
                    _factory.CreateIdentity(), DecodeType(row.Element, depth + 1), row.Name),
                _ => throw Bad("Portable IR contains a non-canonical scalar type.")
            };
            var info = _factory.GetTypeInfo(_types[index]);
            Require(
                info.Kind == row.Kind &&
                info.ElementType == (row.Element == -1 ? null : _types[row.Element]),
                "Portable IR type metadata is inconsistent.");
            _typeState[index] = 2;
            return _types[index];
        }

        private void DecodeIdentities()
        {
            _identities = new IrIdentityId[_graph.Identities.Length];
            for (var index = 0; index < _identities.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                Require(
                    _graph.Identities[index] == index,
                    "Portable IR identities are not canonical.");
                _identities[index] = _factory.CreateIdentity();
            }
        }

        private IrMemberId DecodeMember(PortableIrMember row)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Require(row.ParameterTypes != null, "Portable IR member parameters cannot be null.");
            var member = _factory.GetOrCreateMember(
                Identity(row.Identity), Type(row.DeclaringType), row.Name,
                Type(row.ReturnType), row.IsStatic, [.. row.ParameterTypes.Select(Type)]);
            Require(_distinctMembers.Add(member), "Portable IR member equality partitions collapse.");
            return member;
        }

        private IrTerm DecodeTerm(int index, int depth = 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Check(index, _terms.Length, "term");
            if (_termState[index] == 2)
            {
                return _terms[index]!;
            }

            Require(_termState[index] != 1, "Portable IR terms contain a cycle.");
            Require(depth < MaximumGraphDepth, "Portable IR term depth exceeds the supported limit.");
            _termState[index] = 1;
            var row = Required(_graph.Terms[index], "term row");
            Require(row.Items != null, "Portable IR term metadata is invalid.");
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
                TermsAtDepth,
                value => Wire(value, OpaquePurities),
                value => Wire(value, UnaryOperators),
                value => Wire(value, BinaryOperators),
                static () => Bad("Portable IR term metadata is invalid."));
            Require(
                term.Kind == row.Kind &&
                term.Type == Type(row.Type) &&
                _distinctTerms.Add(term.Id),
                "Portable IR term equality or type metadata is inconsistent.");
            _terms[index] = term;
            _termState[index] = 2;
            return term;
        }

        private IrTerm[] TermsAtDepth(int[] indices, int depth)
        {
            return [.. indices.Select(index => DecodeTerm(index, depth + 1))];
        }

        private (IrProgram? Program, IrBlockId[] Blocks, IrInstruction[] Instructions) DecodeProgram()
        {
            if (!_graph.HasProgram)
            {
                return (null, [], []);
            }

            var builder = new IrProgramBuilder(_factory);
            var blocks = new IrBlockId[_graph.Blocks.Length];
            for (var index = 0; index < blocks.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var row = Required(_graph.Blocks[index], "block row");
                Require(row.Instructions != null, "Portable IR instruction arrays cannot be null.");
                RequireCanonicalOptionalText(row.Name, "block name");
                blocks[index] = builder.CreateBlock(row.Name);
            }
            builder.SetEntry(Block(_graph.Entry, blocks));
            var instructions = new List<IrInstruction>();
            for (var index = 0; index < blocks.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                foreach (var row in _graph.Blocks[index].Instructions)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    instructions.Add(Instruction(builder, blocks[index], blocks, row));
                }
            }

            return (builder.Build(), blocks, [.. instructions]);
        }

        private IrInstruction Instruction(
            IrProgramBuilder builder, IrBlockId block, IrBlockId[] blocks,
            PortableIrInstruction? row)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            row = Required(row, "instruction row");
            Require(row.Items != null, "Portable IR instruction metadata is invalid.");
            RequireCanonicalInstructionSlots(row);
            if (row.Kind == IrInstructionKind.Havoc)
            {
                RequireCanonicalVariableIndices(row.Items!);
            }
            return PortableIrGraphCodecProjections.DecodeInstruction(
                builder,
                block,
                row,
                Operation,
                Variable,
                Member,
                index => OptionalTerm(index),
                Term,
                OptionalVariable,
                value => Wire(value, HavocKinds),
                index => Block(index, blocks),
                value => Location(builder, value),
                indices => [.. indices.Select(Term)],
                indices => [.. indices.Select(Variable)],
                static () => Bad("Portable IR instruction metadata is invalid."));
        }

        private IrLocation Location(IrProgramBuilder builder, PortableIrLocation? row)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            row = Required(row, "location row");
            Require(row.Items != null, "Portable IR location metadata is invalid.");
            RequireCanonicalLocationSlots(row);
            var location = PortableIrGraphCodecProjections.DecodeLocation(
                builder,
                row,
                Member,
                index => OptionalTerm(index),
                Term,
                indices => [.. indices.Select(Term)],
                static () => Bad("Portable IR location metadata is invalid."));
            Require(
                location.Type == Type(row.Type),
                "Portable IR location type metadata is inconsistent.");
            return location;
        }

        private TResult[] DecodeRows<TRow, TResult>(
            TRow?[] rows,
            string kind,
            Func<TRow, TResult> decode) where TRow : class
        {
            var result = new TResult[rows.Length];
            for (var index = 0; index < result.Length; index++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                result[index] = decode(Required(rows[index], $"{kind} row"));
            }

            return result;
        }

        private OperationId DecodeOperation(PortableIrOperation row)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            RequireCanonicalOptionalText(row.Description, "operation description");
            return _factory.CreateOperation(row.Description);
        }

        private static void RequireCanonicalOptionalText(string? value, string kind)
        {
            Require(
                value == null || !string.IsNullOrWhiteSpace(value),
                $"Portable IR {kind} cannot be whitespace.");
        }

        private void RequireCanonicalVariableIndices(int[] indices)
        {
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
            return value ?? throw Bad($"Portable IR {kind}s cannot be null.");
        }

        private IrTypeId Type(int index)
        {
            Check(index, _types.Length, "type");
            return DecodeType(index);
        }

        private IrIdentityId Identity(int index)
        {
            return Item(index, _identities, "identity");
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

        private static IrBlockId Block(int index, IrBlockId[] blocks)
        {
            return Item(index, blocks, "block");
        }

        private static T Item<T>(int index, T[] values, string kind)
        {
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
