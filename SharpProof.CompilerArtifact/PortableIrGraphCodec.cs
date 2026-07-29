namespace SharpProof.CompilerArtifact;

internal static partial class PortableIrGraphCodec {

    internal static EncodedPortableIrGraph Encode(IrProgram program, IReadOnlyList<IrTerm> roots) {
        if (program == null)
            throw new ArgumentNullException(nameof(program));
        return Encode(program.Factory, program, roots);
    }

    internal static EncodedPortableIrGraph Encode(
        IrFactory factory, IrProgram? program, IReadOnlyList<IrTerm> roots,
        IReadOnlyList<IrVarId>? variables = null) {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));
        if (roots == null)
            throw new ArgumentNullException(nameof(roots));
        if (program != null && !ReferenceEquals(factory, program.Factory))
            throw new ArgumentException("The program belongs to a different IR factory.", nameof(program));
        return new Encoder(factory, program, roots, variables ?? []).Encode();
    }

    internal static DecodedPortableIrGraph Decode(PortableIrGraph graph) {
        if (graph == null)
            throw new ArgumentNullException(nameof(graph));
        try {
            return new Decoder(graph).Decode();
        }
        catch (InvalidDataException) {
            throw;
        }
        catch (Exception exception) when (exception is
            ArgumentException or InvalidOperationException or NullReferenceException) {
            throw Bad("The portable IR graph is malformed.", exception);
        }
    }

    private static InvalidDataException Bad(string message, Exception? inner = null) => new(message, inner);

    private static void Require(bool condition, string message) {
        if (!condition)
            throw Bad(message);
    }

    private static int Wire<T>(T value, T[] values) where T : struct, Enum {
        var index = Array.IndexOf(values, value);
        return index >= 0 ? index : throw Bad("Portable IR contains an unknown enum value.");
    }

    private static T Wire<T>(int value, T[] values) where T : struct, Enum =>
        value >= 0 && value < values.Length
            ? values[value]
            : throw Bad("Portable IR contains an unknown enum value.");

    private static Dictionary<T, int> Dense<T>(IEnumerable<T> values) where T : notnull {
        var result = new Dictionary<T, int>();
        foreach (var value in values)
            result.Add(value, result.Count);
        return result;
    }

    private sealed class EncodingTable<TSource, TRow>(
        Func<TSource, int, TRow> encode) where TSource : notnull {
        private readonly Dictionary<TSource, int> _indices = [];
        private readonly List<TRow> _rows = [];

        internal IReadOnlyDictionary<TSource, int> Indices => _indices;
        internal TRow[] Rows => [.. _rows];

        internal int Add(TSource source) {
            if (_indices.TryGetValue(source, out var existing))
                return existing;
            var index = _rows.Count;
            _indices.Add(source, index);
            _rows.Add(default!);
            _rows[index] = encode(source, index);
            return index;
        }
    }

    private sealed class Encoder {
        private readonly IrFactory _factory;
        private readonly IrProgram? _program;
        private readonly IReadOnlyList<IrTerm> _roots;
        private readonly IReadOnlyList<IrVarId> _extraVariables;
        private readonly EncodingTable<IrTypeId, PortableIrType> _types;
        private readonly EncodingTable<IrIdentityId, int> _identities;
        private readonly EncodingTable<IrVarId, PortableIrVariable> _variables;
        private readonly EncodingTable<IrMemberId, PortableIrMember> _members;
        private readonly EncodingTable<OperationId, PortableIrOperation> _operations;
        private readonly EncodingTable<IrId, PortableIrTerm> _terms;
        private IrBasicBlock[] _blocks = [];
        private Dictionary<IrBlockId, int> _blockIndices = [];
        private Dictionary<IrInstructionId, int> _instructionIndices = [];

        internal Encoder(
            IrFactory factory,
            IrProgram? program,
            IReadOnlyList<IrTerm> roots,
            IReadOnlyList<IrVarId> extraVariables) {
            (_factory, _program, _roots, _extraVariables) =
                (factory, program, roots, extraVariables);
            _types = new((id, _) => TypeRow(id));
            _identities = new(static (_, index) => index);
            _variables = new((id, _) => VariableRow(id));
            _members = new((id, _) => MemberRow(id));
            _operations = new((id, _) => OperationRow(id));
            _terms = new((id, _) => TermRow(id));
        }

        internal EncodedPortableIrGraph Encode() {
            TypeIndex(_factory.BooleanType);
            TypeIndex(_factory.IntegerType);
            TypeIndex(_factory.StringType);
            TypeIndex(_factory.ObjectType);
            var roots = _roots.Select(TermIndex).ToArray();
            foreach (var variable in _extraVariables)
                VariableIndex(variable);
            _blocks = _program == null ? [] :
                [.. _program.Blocks.OrderBy(static block => block.Id.Value)];
            _blockIndices = Dense(_blocks.Select(static block => block.Id));
            _instructionIndices = Dense(_blocks
                .SelectMany(static block => block.Instructions)
                .Select(static instruction => instruction.Id));
            var blocks = _blocks.Select(BlockRow).ToArray();
            var graph = new PortableIrGraph {
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

        private PortableIrType TypeRow(IrTypeId id) {
            var value = _factory.GetTypeInfo(id);
            return new(
                value.Kind,
                _factory.GetString(value.Name),
                value.ElementType.HasValue ? TypeIndex(value.ElementType.Value) : -1);
        }

        private PortableIrVariable VariableRow(IrVarId id) {
            var value = _factory.GetVariableInfo(id);
            return new(_factory.GetString(value.Name), TypeIndex(value.Type));
        }

        private PortableIrMember MemberRow(IrMemberId id) {
            var value = _factory.GetMemberInfo(id);
            return new(
                _identities.Add(value.Identity),
                TypeIndex(value.DeclaringType),
                _factory.GetString(value.Name),
                TypeIndex(value.ReturnType),
                value.IsStatic,
                [.. value.ParameterTypes.Select(TypeIndex)]);
        }

        private PortableIrOperation OperationRow(OperationId id) {
            var value = _factory.GetOperationInfo(id);
            return new(value.Description.HasValue ?
                _factory.GetString(value.Description.Value) : null);
        }

        private PortableIrTerm TermRow(IrId id) {
            var term = _factory.GetTerm(id);
            return term switch {
                IrBooleanTerm value => TermRow(term, a: value.Value ? 1 : 0),
                IrIntegerTerm value => TermRow(term, number: value.Value),
                IrStringTerm value => TermRow(term, text: _factory.GetString(value.Value)),
                IrNullTerm => TermRow(term),
                IrVariableTerm value => TermRow(term, a: VariableIndex(value.Variable)),
                IrOpaqueTerm value => TermRow(
                    term, MemberIndex(value.Member), OptionalTermIndex(value.Receiver), Wire(value.Purity, OpaquePurities),
                    d: value.Purity == IrOpaquePurity.Pure ? -1 : OperationIndex(value.Operation),
                    items: TermIndices(value.Arguments)),
                IrUnaryTerm value => TermRow(
                    term, Wire(value.Operator, UnaryOperators), TermIndex(value.Operand)),
                IrBinaryTerm value => TermRow(
                    term, Wire(value.Operator, BinaryOperators), TermIndex(value.Left), TermIndex(value.Right)),
                IrConditionalTerm value => TermRow(
                    term, TermIndex(value.Condition), TermIndex(value.WhenTrue), TermIndex(value.WhenFalse)),
                IrCastTerm value => TermRow(term, a: TermIndex(value.Operand)),
                IrLengthTerm value => TermRow(term, a: TermIndex(value.Value)),
                IrSequenceAccessTerm value => TermRow(term, TermIndex(value.Sequence), TermIndex(value.Index)),
                _ => throw Bad("Unknown IR term kind.")
            };
        }

        private PortableIrTerm TermRow(
            IrTerm term, int a = -1, int b = -1, int c = -1, int d = -1,
            long number = 0, string? text = null, int[]? items = null) =>
            new(term.Kind, TypeIndex(term.Type), a, b, c, d, number, text, items);

        private PortableIrLocation LocationRow(IrLocation location) =>
            location switch {
                IrMemberLocation value => new(
                    value.Kind, TypeIndex(value.Type), MemberIndex(value.Member),
                    OptionalTermIndex(value.Receiver), TermIndices(value.Arguments)),
                IrSequenceLocation value => new(value.Kind, TypeIndex(value.Type), TermIndex(value.Sequence), TermIndex(value.Index)),
                _ => throw Bad("Unknown IR location kind.")
            };

        private PortableIrBlock BlockRow(IrBasicBlock block) =>
            new(
                block.Name.HasValue ? _factory.GetString(block.Name.Value) : null,
                [.. block.Instructions.Select(InstructionRow)]);

        private PortableIrInstruction InstructionRow(IrInstruction instruction) =>
            instruction switch {
                IrAssignInstruction value => InstructionRow(
                    instruction, a: VariableIndex(value.Target), b: TermIndex(value.Value)),
                IrLoadInstruction value => InstructionRow(
                    instruction, a: VariableIndex(value.Target), location: LocationRow(value.Location)),
                IrStoreInstruction value => InstructionRow(
                    instruction, a: TermIndex(value.Value), location: LocationRow(value.Location)),
                IrCallInstruction value => InstructionRow(
                    instruction, a: value.Target.HasValue ? VariableIndex(value.Target.Value) : -1,
                    b: MemberIndex(value.Member), c: OptionalTermIndex(value.Receiver),
                    items: TermIndices(value.Arguments)),
                IrAssumeInstruction value => InstructionRow(instruction, a: TermIndex(value.Condition)),
                IrAssertInstruction value => InstructionRow(instruction, a: TermIndex(value.Condition)),
                IrHavocInstruction value => InstructionRow(
                    instruction, a: Wire(value.HavocKind, HavocKinds),
                    items: [.. value.Variables.Select(VariableIndex)]),
                IrBranchInstruction value => InstructionRow(
                    instruction, a: TermIndex(value.Condition),
                    b: BlockIndex(value.WhenTrue), c: BlockIndex(value.WhenFalse)),
                IrGotoInstruction value => InstructionRow(instruction, a: BlockIndex(value.Target)),
                IrReturnInstruction value => InstructionRow(instruction, a: OptionalTermIndex(value.Value)),
                _ => throw Bad("Unknown IR instruction kind.")
            };

        private PortableIrInstruction InstructionRow(
            IrInstruction instruction, int a = -1, int b = -1, int c = -1,
            int[]? items = null, PortableIrLocation? location = null) =>
            new(
                instruction.Kind, OperationIndex(instruction.Operation),
                a, b, c, items, location);

        private int TypeIndex(IrTypeId id) => _types.Add(id);
        private int VariableIndex(IrVarId id) => _variables.Add(id);
        private int MemberIndex(IrMemberId id) => _members.Add(id);
        private int OperationIndex(OperationId id) => _operations.Add(id);

        private int TermIndex(IrTerm term) {
            _factory.EnsureTerm(term, nameof(term));
            return _terms.Add(term.Id);
        }

        private int OptionalTermIndex(IrTerm? term) => term == null ? -1 : TermIndex(term);
        private int[] TermIndices(IEnumerable<IrTerm> terms) => [.. terms.Select(TermIndex)];
        private int BlockIndex(IrBlockId id) => _blockIndices.TryGetValue(id, out var index)
            ? index : throw Bad("The IR graph references missing metadata.");
    }

    private sealed class Decoder(PortableIrGraph _graph) {
        private const int MaximumDecodeDepth = 256;
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

        internal DecodedPortableIrGraph Decode() {
            RequireGraphShape();
            DecodeTypes();
            DecodeIdentities();
            _variables = DecodeRows(_graph.Variables, "variable",
                row => _factory.CreateVariable(row.Name, Type(row.Type)));
            _members = DecodeRows(_graph.Members, "member", DecodeMember);
            _operations = DecodeRows(_graph.Operations, "operation",
                row => _factory.CreateOperation(row.Description));
            _terms = new IrTerm?[_graph.Terms.Length];
            _termState = new byte[_terms.Length];
            for (var index = 0; index < _terms.Length; index++)
                DecodeTerm(index);
            IrTerm[] roots = [.. _graph.Roots.Select(Term)];
            var (program, blocks, instructions) = DecodeProgram();
            return new DecodedPortableIrGraph(
                _factory, program, roots, _variables, blocks, instructions);
        }

        private void RequireGraphShape() {
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

        private void DecodeTypes() {
            Require(_graph.Types.Length >= 4, "Portable IR is missing built-in types.");
            _types = new IrTypeId[_graph.Types.Length];
            _typeState = new byte[_types.Length];
            var builtIns = new[] {
                (_factory.BooleanType, IrTypeKind.Boolean, "bool"),
                (_factory.IntegerType, IrTypeKind.Integer, "int"),
                (_factory.StringType, IrTypeKind.String, "string"),
                (_factory.ObjectType, IrTypeKind.Reference, "object")
            };
            for (var index = 0; index < builtIns.Length; index++) {
                var (id, kind, name) = builtIns[index];
                var row = Required(_graph.Types[index], "type row");
                Require(
                    row.Kind == kind && row.Name == name && row.Element == -1,
                    "Portable IR built-in type metadata is invalid.");
                _types[index] = id;
                _typeState[index] = 2;
            }
            for (var index = builtIns.Length; index < _types.Length; index++)
                DecodeType(index);
        }

        private IrTypeId DecodeType(int index, int depth = 0) {
            Check(index, _types.Length, "type");
            if (_typeState[index] == 2)
                return _types[index];
            Require(_typeState[index] != 1, "Portable IR type metadata contains a cycle.");
            Require(depth < MaximumDecodeDepth, "Portable IR type depth exceeds the supported limit.");
            _typeState[index] = 1;
            var row = Required(_graph.Types[index], "type row");
            Require(!string.IsNullOrWhiteSpace(row.Name), "Portable IR type metadata is invalid.");
            _types[index] = row.Kind switch {
                IrTypeKind.Reference when row.Element == -1 =>
                    _factory.GetOrCreateReferenceType(_factory.CreateIdentity(), row.Name),
                IrTypeKind.Sequence => _factory.GetOrCreateSequenceType(
                    _factory.CreateIdentity(), DecodeType(row.Element, depth + 1), row.Name),
                _ => throw Bad("Portable IR contains a non-canonical scalar type.")
            };
            var info = _factory.GetTypeInfo(_types[index]);
            Require(
                info.Kind == row.Kind &&
                info.ElementType == (row.Element < 0 ? null : _types[row.Element]),
                "Portable IR type metadata is inconsistent.");
            _typeState[index] = 2;
            return _types[index];
        }

        private void DecodeIdentities() {
            _identities = new IrIdentityId[_graph.Identities.Length];
            for (var index = 0; index < _identities.Length; index++) {
                Require(
                    _graph.Identities[index] == index,
                    "Portable IR identities are not canonical.");
                _identities[index] = _factory.CreateIdentity();
            }
        }

        private IrMemberId DecodeMember(PortableIrMember row) {
            Require(row.ParameterTypes != null, "Portable IR member parameters cannot be null.");
            var member = _factory.GetOrCreateMember(
                Identity(row.Identity), Type(row.DeclaringType), row.Name,
                Type(row.ReturnType), row.IsStatic, [.. row.ParameterTypes.Select(Type)]);
            Require(_distinctMembers.Add(member), "Portable IR member equality partitions collapse.");
            return member;
        }

        private IrTerm DecodeTerm(int index, int depth = 0) {
            Check(index, _terms.Length, "term");
            if (_termState[index] == 2)
                return _terms[index]!;
            Require(_termState[index] != 1, "Portable IR terms contain a cycle.");
            Require(depth < MaximumDecodeDepth, "Portable IR term depth exceeds the supported limit.");
            _termState[index] = 1;
            var row = Required(_graph.Terms[index], "term row");
            Require(row.Items != null, "Portable IR term metadata is invalid.");
            var term = row.Kind switch {
                IrTermKind.Boolean when row.A is 0 or 1 => _factory.Boolean(row.A == 1),
                IrTermKind.Integer => _factory.Integer(row.Number),
                IrTermKind.String when row.Text != null => _factory.String(row.Text),
                IrTermKind.Null => _factory.Null(Type(row.Type)),
                IrTermKind.Variable => _factory.Variable(Variable(row.A)),
                IrTermKind.Opaque => Opaque(row, depth),
                IrTermKind.Unary => _factory.Unary(Wire(row.A, UnaryOperators), DecodeTerm(row.B, depth + 1)),
                IrTermKind.Binary => _factory.Binary(
                    Wire(row.A, BinaryOperators), DecodeTerm(row.B, depth + 1), DecodeTerm(row.C, depth + 1)),
                IrTermKind.Conditional => _factory.Conditional(
                    DecodeTerm(row.A, depth + 1), DecodeTerm(row.B, depth + 1), DecodeTerm(row.C, depth + 1)),
                IrTermKind.Cast => _factory.Cast(Type(row.Type), DecodeTerm(row.A, depth + 1)),
                IrTermKind.Length => _factory.Length(DecodeTerm(row.A, depth + 1)),
                IrTermKind.SequenceAccess => _factory.SequenceAccess(
                    DecodeTerm(row.A, depth + 1), DecodeTerm(row.B, depth + 1)),
                _ => throw Bad("Portable IR term metadata is invalid.")
            };
            Require(
                term.Kind == row.Kind &&
                term.Type == Type(row.Type) &&
                _distinctTerms.Add(term.Id),
                "Portable IR term equality or type metadata is inconsistent.");
            _terms[index] = term;
            _termState[index] = 2;
            return term;
        }

        private IrOpaqueTerm Opaque(PortableIrTerm row, int depth) {
            var purity = Wire(row.C, OpaquePurities);
            var receiver = row.B < 0 ? null : DecodeTerm(row.B, depth + 1);
            IrTerm[] arguments = [.. row.Items.Select(index => DecodeTerm(index, depth + 1))];
            return purity switch {
                IrOpaquePurity.Pure when row.D == -1 =>
                    _factory.PureOpaque(Member(row.A), receiver, arguments),
                IrOpaquePurity.Impure => _factory.ImpureOpaque(Operation(row.D), Member(row.A), receiver, arguments),
                _ => throw Bad("Portable IR opaque metadata is invalid.")
            };
        }

        private (IrProgram? Program, IrBlockId[] Blocks, IrInstruction[] Instructions) DecodeProgram() {
            if (!_graph.HasProgram)
                return (null, [], []);
            var builder = new IrProgramBuilder(_factory);
            var blocks = new IrBlockId[_graph.Blocks.Length];
            for (var index = 0; index < blocks.Length; index++) {
                var row = Required(_graph.Blocks[index], "block row");
                Require(row.Instructions != null, "Portable IR instruction arrays cannot be null.");
                blocks[index] = builder.CreateBlock(row.Name);
            }
            builder.SetEntry(Block(_graph.Entry, blocks));
            var instructions = new List<IrInstruction>();
            for (var index = 0; index < blocks.Length; index++)
                foreach (var row in _graph.Blocks[index].Instructions)
                    instructions.Add(Instruction(builder, blocks[index], blocks, row));
            return (builder.Build(), blocks, [.. instructions]);
        }

        private IrInstruction Instruction(
            IrProgramBuilder builder, IrBlockId block, IrBlockId[] blocks,
            PortableIrInstruction? row) {
            row = Required(row, "instruction row");
            Require(row.Items != null, "Portable IR instruction metadata is invalid.");
            var operation = Operation(row.Operation);
            return row.Kind switch {
                IrInstructionKind.Assign => builder.Assign(block, operation, Variable(row.A), Term(row.B)),
                IrInstructionKind.Load => builder.Load(block, operation, Variable(row.A), Location(builder, row.Location)),
                IrInstructionKind.Store => builder.Store(block, operation, Location(builder, row.Location), Term(row.A)),
                IrInstructionKind.Call => builder.Call(
                    block, operation, row.A < 0 ? null : Variable(row.A),
                    Member(row.B), row.C < 0 ? null : Term(row.C),
                    [.. row.Items.Select(Term)]),
                IrInstructionKind.Assume => builder.Assume(block, operation, Term(row.A)),
                IrInstructionKind.Assert => builder.Assert(block, operation, Term(row.A)),
                IrInstructionKind.Havoc => builder.Havoc(
                    block, operation, Wire(row.A, HavocKinds), [.. row.Items.Select(Variable)]),
                IrInstructionKind.Branch => builder.Branch(
                    block, operation, Term(row.A),
                    Block(row.B, blocks), Block(row.C, blocks)),
                IrInstructionKind.Goto => builder.Goto(block, operation, Block(row.A, blocks)),
                IrInstructionKind.Return => builder.Return(block, operation, row.A < 0 ? null : Term(row.A)),
                _ => throw Bad("Portable IR instruction metadata is invalid.")
            };
        }

        private IrLocation Location(IrProgramBuilder builder, PortableIrLocation? row) {
            row = Required(row, "location row");
            Require(row.Items != null, "Portable IR location metadata is invalid.");
            IrLocation location = row.Kind switch {
                IrLocationKind.Member => builder.MemberLocation(
                    Member(row.A), row.B < 0 ? null : Term(row.B),
                    [.. row.Items.Select(Term)]),
                IrLocationKind.Sequence => builder.SequenceLocation(Term(row.A), Term(row.B)),
                _ => throw Bad("Portable IR location metadata is invalid.")
            };
            Require(
                location.Type == Type(row.Type),
                "Portable IR location type metadata is inconsistent.");
            return location;
        }

        private static TResult[] DecodeRows<TRow, TResult>(TRow?[] rows, string kind, Func<TRow, TResult> decode) where TRow : class {
            var result = new TResult[rows.Length];
            for (var index = 0; index < result.Length; index++)
                result[index] = decode(Required(rows[index], $"{kind} row"));
            return result;
        }

        private static T Required<T>(T? value, string kind) where T : class =>
            value ?? throw Bad($"Portable IR {kind}s cannot be null.");

        private IrTypeId Type(int index) {
            Check(index, _types.Length, "type");
            return DecodeType(index);
        }

        private IrIdentityId Identity(int index) => Item(index, _identities, "identity");
        private IrVarId Variable(int index) => Item(index, _variables, "variable");
        private IrMemberId Member(int index) => Item(index, _members, "member");
        private OperationId Operation(int index) => Item(index, _operations, "operation");
        private IrTerm Term(int index) => DecodeTerm(index);
        private static IrBlockId Block(int index, IrBlockId[] blocks) => Item(index, blocks, "block");

        private static T Item<T>(int index, T[] values, string kind) {
            Check(index, values.Length, kind);
            return values[index];
        }

        private static void Check(int index, int length, string kind) =>
            Require(
                index >= 0 && index < length,
                $"Portable IR references an invalid {kind} index.");
    }
}
