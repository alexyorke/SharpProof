namespace SharpProof.CompilerArtifact;
#pragma warning disable IDE0055 // Compact codec preserves the fixed production-size ceiling.
internal static class PortableIrGraphCodec {
    internal static EncodedPortableIrGraph Encode(IrProgram program, IReadOnlyList<IrTerm> roots) {
        if (program == null) throw new ArgumentNullException(nameof(program)); return Encode(program.Factory, program, roots); }
    internal static EncodedPortableIrGraph Encode(IrFactory factory, IrProgram? program, IReadOnlyList<IrTerm> roots,
        IReadOnlyList<IrVarId>? variables = null) {
        if (factory == null) throw new ArgumentNullException(nameof(factory)); if (roots == null) throw new ArgumentNullException(nameof(roots));
        if (program != null && !ReferenceEquals(factory, program.Factory)) throw new ArgumentException("The program belongs to a different IR factory.", nameof(program));
        return new Encoder(factory, program, roots, variables ?? []).Encode(); }
    internal static DecodedPortableIrGraph Decode(PortableIrGraph graph) {
        if (graph == null) throw new ArgumentNullException(nameof(graph)); try { return new Decoder(graph).Decode(); }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NullReferenceException) {
            throw Bad("The portable IR graph is malformed.", exception); } }
    private static InvalidDataException Bad(string message, Exception? inner = null) => new(message, inner);
    private sealed class Encoder {
        private readonly IrFactory _factory; private readonly IrProgram? _program; private readonly IReadOnlyList<IrTerm> _roots;
        private readonly IReadOnlyList<IrVarId> _extraVariables;
        private readonly HashSet<IrTypeId> _types = []; private readonly HashSet<IrIdentityId> _identities = []; private readonly HashSet<IrVarId> _variables = [];
        private readonly HashSet<IrMemberId> _members = []; private readonly HashSet<OperationId> _operations = []; private readonly HashSet<IrId> _terms = [];
        internal Encoder(IrFactory factory, IrProgram? program, IReadOnlyList<IrTerm> roots, IReadOnlyList<IrVarId> variables) =>
            (_factory, _program, _roots, _extraVariables) = (factory, program, roots, variables);
        internal EncodedPortableIrGraph Encode() {
            AddType(_factory.BooleanType); AddType(_factory.IntegerType); AddType(_factory.StringType); AddType(_factory.ObjectType);
            foreach (var root in _roots) AddTerm(root);
            foreach (var variable in _extraVariables) AddVariable(variable);
            if (_program != null) foreach (var block in _program.Blocks) foreach (var instruction in block.Instructions) AddInstruction(instruction);
            IrTypeId[] types = [.. _types.OrderBy(static id => id.Value)]; IrIdentityId[] identities = [.. _identities.OrderBy(static id => id.Value)];
            IrVarId[] variables = [.. _variables.OrderBy(static id => id.Value)]; IrMemberId[] members = [.. _members.OrderBy(static id => id.Value)];
            OperationId[] operations = [.. _operations.OrderBy(static id => id.Value)]; IrId[] terms = [.. _terms.OrderBy(static id => id.Value)];
            IrBasicBlock[] blocks = _program == null ? [] : [.. _program.Blocks.OrderBy(static block => block.Id.Value)]; IrInstruction[] instructions = [.. blocks.SelectMany(static block => block.Instructions)];
            var tm = Dense(types); var im = Dense(identities); var vm = Dense(variables); var mm = Dense(members);
            var om = Dense(operations); var em = Dense(terms); var bm = Dense([.. blocks.Select(static block => block.Id)]); var nm = Dense([.. instructions.Select(static instruction => instruction.Id)]);
            var graph = new PortableIrGraph {
                HasProgram = _program != null, Types = [.. types.Select(id => TypeRow(id, tm))],
                Identities = [.. Enumerable.Range(0, identities.Length)], Variables = [.. variables.Select(id => VariableRow(id, tm))],
                Members = [.. members.Select(id => MemberRow(id, im, tm))], Operations = [.. operations.Select(OperationRow)],
                Terms = [.. terms.Select(id => TermRow(id, tm, vm, mm, om, em))], Blocks = [.. blocks.Select(block => BlockRow(block, vm, mm, om, em, bm, tm))],
                Entry = _program == null ? -1 : At(bm, _program.Entry), Roots = [.. _roots.Select(root => At(em, root.Id))]
            };
            return new EncodedPortableIrGraph(graph, vm, nm); }
        private static Dictionary<T, int> Dense<T>(IReadOnlyList<T> values) where T : notnull {
            var result = new Dictionary<T, int>(values.Count); for (var index = 0; index < values.Count; index++) result.Add(values[index], index); return result; }
        private static int At<T>(IReadOnlyDictionary<T, int> map, T key) where T : notnull =>
            map.TryGetValue(key, out var value) ? value : throw Bad("The IR graph references missing metadata.");
        private void AddType(IrTypeId id) {
            var info = _factory.GetTypeInfo(id); if (!_types.Add(id)) return; if (info.ElementType.HasValue) AddType(info.ElementType.Value); }
        private void AddVariable(IrVarId id) { var info = _factory.GetVariableInfo(id); if (_variables.Add(id)) AddType(info.Type); }
        private void AddMember(IrMemberId id) {
            var info = _factory.GetMemberInfo(id); if (!_members.Add(id)) return;
            _identities.Add(info.Identity); AddType(info.DeclaringType); AddType(info.ReturnType); foreach (var type in info.ParameterTypes) AddType(type); }
        private void AddOperation(OperationId id) { _factory.GetOperationInfo(id); _operations.Add(id); }
        private void AddTerm(IrTerm? term) {
            if (term == null) return; _factory.EnsureTerm(term, nameof(term)); if (!_terms.Add(term.Id)) return; AddType(term.Type);
            switch (term) {
                case IrVariableTerm value: AddVariable(value.Variable); break;
                case IrOpaqueTerm value:
                    AddMember(value.Member); if (value.Purity == IrOpaquePurity.Impure) AddOperation(value.Operation); AddTerm(value.Receiver); foreach (var item in value.Arguments) AddTerm(item); break;
                case IrUnaryTerm value: AddTerm(value.Operand); break; case IrBinaryTerm value: AddTerm(value.Left); AddTerm(value.Right); break;
                case IrConditionalTerm value: AddTerm(value.Condition); AddTerm(value.WhenTrue); AddTerm(value.WhenFalse); break;
                case IrCastTerm value: AddTerm(value.Operand); break; case IrLengthTerm value: AddTerm(value.Value); break;
                case IrSequenceAccessTerm value: AddTerm(value.Sequence); AddTerm(value.Index); break; case IrBooleanTerm or IrIntegerTerm or IrStringTerm or IrNullTerm: break;
                default: throw Bad("Unknown IR term kind.");
            } }
        private void AddLocation(IrLocation location) {
            AddType(location.Type);
            switch (location) {
                case IrMemberLocation value: AddMember(value.Member); AddTerm(value.Receiver); foreach (var item in value.Arguments) AddTerm(item); break;
                case IrSequenceLocation value: AddTerm(value.Sequence); AddTerm(value.Index); break;
                default: throw Bad("Unknown IR location kind.");
            } }
        private void AddInstruction(IrInstruction instruction) {
            AddOperation(instruction.Operation);
            switch (instruction) {
                case IrAssignInstruction value: AddVariable(value.Target); AddTerm(value.Value); break; case IrLoadInstruction value: AddVariable(value.Target); AddLocation(value.Location); break;
                case IrStoreInstruction value: AddLocation(value.Location); AddTerm(value.Value); break;
                case IrCallInstruction value: if (value.Target.HasValue) AddVariable(value.Target.Value); AddMember(value.Member); AddTerm(value.Receiver);
                    foreach (var item in value.Arguments) AddTerm(item); break;
                case IrAssumeInstruction value: AddTerm(value.Condition); break; case IrAssertInstruction value: AddTerm(value.Condition); break;
                case IrHavocInstruction value: foreach (var item in value.Variables) AddVariable(item); break;
                case IrBranchInstruction value: AddTerm(value.Condition); break; case IrGotoInstruction: break;
                case IrReturnInstruction value: AddTerm(value.Value); break;
                default: throw Bad("Unknown IR instruction kind.");
            } }
        private PortableIrType TypeRow(IrTypeId id, IReadOnlyDictionary<IrTypeId, int> types) {
            var value = _factory.GetTypeInfo(id); return new() { Kind = value.Kind, Name = _factory.GetString(value.Name),
                Element = value.ElementType.HasValue ? At(types, value.ElementType.Value) : -1 }; }
        private PortableIrVariable VariableRow(IrVarId id, IReadOnlyDictionary<IrTypeId, int> types) {
            var value = _factory.GetVariableInfo(id); return new() { Name = _factory.GetString(value.Name), Type = At(types, value.Type) }; }
        private PortableIrMember MemberRow(IrMemberId id, IReadOnlyDictionary<IrIdentityId, int> identities, IReadOnlyDictionary<IrTypeId, int> types) {
            var value = _factory.GetMemberInfo(id); return new() { Identity = At(identities, value.Identity), DeclaringType = At(types, value.DeclaringType),
                Name = _factory.GetString(value.Name), ReturnType = At(types, value.ReturnType), IsStatic = value.IsStatic,
                ParameterTypes = [.. value.ParameterTypes.Select(type => At(types, type))] }; }
        private PortableIrOperation OperationRow(OperationId id) {
            var value = _factory.GetOperationInfo(id); return new() {
                Description = value.Description.HasValue ? _factory.GetString(value.Description.Value) : null }; }
        private PortableIrTerm TermRow(IrId id, IReadOnlyDictionary<IrTypeId, int> types, IReadOnlyDictionary<IrVarId, int> variables,
            IReadOnlyDictionary<IrMemberId, int> members, IReadOnlyDictionary<OperationId, int> operations, IReadOnlyDictionary<IrId, int> terms) {
            var term = _factory.GetTerm(id); var row = new PortableIrTerm { Kind = term.Kind, Type = At(types, term.Type) };
            switch (term) {
                case IrBooleanTerm x: row.A = x.Value ? 1 : 0; break; case IrIntegerTerm x: row.Number = x.Value; break;
                case IrStringTerm x: row.Text = _factory.GetString(x.Value); break; case IrNullTerm: break;
                case IrVariableTerm x: row.A = At(variables, x.Variable); break;
                case IrOpaqueTerm x:
                    row.A = At(members, x.Member); row.B = x.Receiver == null ? -1 : At(terms, x.Receiver.Id);
                    row.C = (int)x.Purity; row.D = x.Purity == IrOpaquePurity.Pure ? -1 : At(operations, x.Operation); row.Items = [.. x.Arguments.Select(item => At(terms, item.Id))]; break;
                case IrUnaryTerm x: row.A = (int)x.Operator; row.B = At(terms, x.Operand.Id); break;
                case IrBinaryTerm x: row.A = (int)x.Operator; row.B = At(terms, x.Left.Id); row.C = At(terms, x.Right.Id); break;
                case IrConditionalTerm x: row.A = At(terms, x.Condition.Id); row.B = At(terms, x.WhenTrue.Id); row.C = At(terms, x.WhenFalse.Id); break;
                case IrCastTerm x: row.A = At(terms, x.Operand.Id); break; case IrLengthTerm x: row.A = At(terms, x.Value.Id); break;
                case IrSequenceAccessTerm x: row.A = At(terms, x.Sequence.Id); row.B = At(terms, x.Index.Id); break;
                default: throw Bad("Unknown IR term kind.");
            }
            return row; }
        private static PortableIrLocation LocationRow(IrLocation location, IReadOnlyDictionary<IrTypeId, int> types,
            IReadOnlyDictionary<IrMemberId, int> members, IReadOnlyDictionary<IrId, int> terms) {
            var row = new PortableIrLocation { Kind = location.Kind, Type = At(types, location.Type) };
            switch (location) {
                case IrMemberLocation x:
                    row.A = At(members, x.Member); row.B = x.Receiver == null ? -1 : At(terms, x.Receiver.Id); row.Items = [.. x.Arguments.Select(item => At(terms, item.Id))]; break;
                case IrSequenceLocation x: row.A = At(terms, x.Sequence.Id); row.B = At(terms, x.Index.Id); break;
                default: throw Bad("Unknown IR location kind.");
            }
            return row; }
        private PortableIrBlock BlockRow(IrBasicBlock block, IReadOnlyDictionary<IrVarId, int> variables,
            IReadOnlyDictionary<IrMemberId, int> members, IReadOnlyDictionary<OperationId, int> operations,
            IReadOnlyDictionary<IrId, int> terms, IReadOnlyDictionary<IrBlockId, int> blocks, IReadOnlyDictionary<IrTypeId, int> types) => new() {
                Name = block.Name.HasValue ? _factory.GetString(block.Name.Value) : null,
                Instructions = [.. block.Instructions.Select(value => InstructionRow(value, variables, members, operations, terms, blocks, types))] };
        private static PortableIrInstruction InstructionRow(IrInstruction instruction, IReadOnlyDictionary<IrVarId, int> variables,
            IReadOnlyDictionary<IrMemberId, int> members, IReadOnlyDictionary<OperationId, int> operations,
            IReadOnlyDictionary<IrId, int> terms, IReadOnlyDictionary<IrBlockId, int> blocks, IReadOnlyDictionary<IrTypeId, int> types) {
            var row = new PortableIrInstruction { Kind = instruction.Kind, Operation = At(operations, instruction.Operation) };
            switch (instruction) {
                case IrAssignInstruction x: row.A = At(variables, x.Target); row.B = At(terms, x.Value.Id); break;
                case IrLoadInstruction x: row.A = At(variables, x.Target); row.Location = LocationRow(x.Location, types, members, terms); break;
                case IrStoreInstruction x: row.A = At(terms, x.Value.Id); row.Location = LocationRow(x.Location, types, members, terms); break;
                case IrCallInstruction x:
                    row.A = x.Target.HasValue ? At(variables, x.Target.Value) : -1; row.B = At(members, x.Member); row.C = x.Receiver == null ? -1 : At(terms, x.Receiver.Id); row.Items = [.. x.Arguments.Select(item => At(terms, item.Id))]; break;
                case IrAssumeInstruction x: row.A = At(terms, x.Condition.Id); break; case IrAssertInstruction x: row.A = At(terms, x.Condition.Id); break;
                case IrHavocInstruction x: row.A = (int)x.HavocKind; row.Items = [.. x.Variables.Select(item => At(variables, item))]; break;
                case IrBranchInstruction x: row.A = At(terms, x.Condition.Id); row.B = At(blocks, x.WhenTrue); row.C = At(blocks, x.WhenFalse); break;
                case IrGotoInstruction x: row.A = At(blocks, x.Target); break;
                case IrReturnInstruction x: row.A = x.Value == null ? -1 : At(terms, x.Value.Id); break;
                default: throw Bad("Unknown IR instruction kind.");
            }
            return row; }
    }

    private sealed class Decoder {
        private const int MaximumDecodeDepth = 256;
        private readonly PortableIrGraph _graph; private readonly IrFactory _factory = new();
        private IrTypeId[] _types = []; private IrIdentityId[] _identities = []; private IrVarId[] _variables = []; private IrMemberId[] _members = [];
        private OperationId[] _operations = []; private IrTerm?[] _terms = []; private byte[] _typeState = []; private byte[] _termState = [];
        private int _typeDepth; private int _termDepth;
        private readonly HashSet<IrMemberId> _distinctMembers = []; private readonly HashSet<IrId> _distinctTerms = [];
        internal Decoder(PortableIrGraph graph) => _graph = graph;
        internal DecodedPortableIrGraph Decode() {
            RequireArrays(); DecodeTypes(); DecodeIdentities(); DecodeVariables(); DecodeMembers(); DecodeOperations();
            _terms = new IrTerm?[_graph.Terms.Length]; _termState = new byte[_terms.Length]; for (var index = 0; index < _terms.Length; index++) DecodeTerm(index);
            IrTerm[] roots = [.. _graph.Roots.Select(Term)]; var (program, blocks, instructions) = DecodeProgram();
            return new(_factory, program, roots, _variables, blocks, instructions); }
        private void RequireArrays() {
            if (_graph.Types == null || _graph.Identities == null || _graph.Variables == null || _graph.Members == null ||
                _graph.Operations == null || _graph.Terms == null || _graph.Blocks == null || _graph.Roots == null)
                throw Bad("Portable IR arrays cannot be null.");
            if (_graph.HasProgram != (_graph.Blocks.Length != 0) || !_graph.HasProgram && _graph.Entry != -1) throw Bad("The portable IR program shape is invalid."); }
        private void DecodeTypes() {
            if (_graph.Types.Length < 4) throw Bad("Portable IR is missing built-in types."); _types = new IrTypeId[_graph.Types.Length]; _typeState = new byte[_types.Length];
            BuiltIn(0, _factory.BooleanType, IrTypeKind.Boolean, "bool"); BuiltIn(1, _factory.IntegerType, IrTypeKind.Integer, "int");
            BuiltIn(2, _factory.StringType, IrTypeKind.String, "string"); BuiltIn(3, _factory.ObjectType, IrTypeKind.Reference, "object");
            for (var index = 4; index < _types.Length; index++) DecodeType(index); }
        private void BuiltIn(int index, IrTypeId id, IrTypeKind kind, string name) {
            var row = _graph.Types[index] ?? throw Bad("Portable IR type rows cannot be null."); if (row.Kind != kind || row.Name != name || row.Element != -1) throw Bad("Portable IR built-in type metadata is invalid.");
            _types[index] = id; _typeState[index] = 2; }
        private IrTypeId DecodeType(int index) {
            Check(index, _types.Length, "type"); if (_typeState[index] == 2) return _types[index];
            if (_typeState[index] == 1) throw Bad("Portable IR type metadata contains a cycle.");
            Enter(ref _typeDepth, "type"); _typeState[index] = 1;
            try {
                var row = _graph.Types[index] ?? throw Bad("Portable IR type rows cannot be null."); if (string.IsNullOrWhiteSpace(row.Name) || !Enum.IsDefined(typeof(IrTypeKind), row.Kind)) throw Bad("Portable IR type metadata is invalid.");
                _types[index] = row.Kind switch {
                    IrTypeKind.Reference when row.Element == -1 => _factory.GetOrCreateReferenceType(_factory.CreateIdentity(), row.Name),
                    IrTypeKind.Sequence => _factory.GetOrCreateSequenceType(_factory.CreateIdentity(), DecodeType(row.Element), row.Name),
                    _ => throw Bad("Portable IR contains a non-canonical scalar type.")
                };
                var info = _factory.GetTypeInfo(_types[index]); if (info.Kind != row.Kind || info.ElementType != (row.Element < 0 ? null : _types[row.Element])) throw Bad("Portable IR type metadata is inconsistent.");
                _typeState[index] = 2; return _types[index];
            } finally { _typeDepth--; } }
        private void DecodeIdentities() {
            _identities = new IrIdentityId[_graph.Identities.Length]; for (var index = 0; index < _identities.Length; index++) {
                if (_graph.Identities[index] != index) throw Bad("Portable IR identities are not canonical.");
                _identities[index] = _factory.CreateIdentity(); } }
        private void DecodeVariables() {
            _variables = new IrVarId[_graph.Variables.Length]; for (var index = 0; index < _variables.Length; index++) {
                var row = _graph.Variables[index] ?? throw Bad("Portable IR variable rows cannot be null.");
                _variables[index] = _factory.CreateVariable(row.Name, Type(row.Type)); } }
        private void DecodeMembers() {
            _members = new IrMemberId[_graph.Members.Length]; for (var index = 0; index < _members.Length; index++) {
                var row = _graph.Members[index] ?? throw Bad("Portable IR member rows cannot be null."); if (row.ParameterTypes == null) throw Bad("Portable IR member parameters cannot be null.");
                _members[index] = _factory.GetOrCreateMember(Identity(row.Identity), Type(row.DeclaringType), row.Name,
                    Type(row.ReturnType), row.IsStatic, [.. row.ParameterTypes.Select(Type)]);
                if (!_distinctMembers.Add(_members[index])) throw Bad("Portable IR member equality partitions collapse.");
            } }
        private void DecodeOperations() {
            _operations = new OperationId[_graph.Operations.Length]; for (var index = 0; index < _operations.Length; index++)
                _operations[index] = _factory.CreateOperation((_graph.Operations[index] ?? throw Bad("Portable IR operation rows cannot be null.")).Description); }
        private IrTerm DecodeTerm(int index) {
            Check(index, _terms.Length, "term"); if (_termState[index] == 2) return _terms[index]!;
            if (_termState[index] == 1) throw Bad("Portable IR terms contain a cycle.");
            Enter(ref _termDepth, "term"); _termState[index] = 1;
            try {
                var row = _graph.Terms[index] ?? throw Bad("Portable IR term rows cannot be null."); if (row.Items == null || !Enum.IsDefined(typeof(IrTermKind), row.Kind)) throw Bad("Portable IR term metadata is invalid.");
                var term = row.Kind switch {
                    IrTermKind.Boolean when row.A is 0 or 1 => _factory.Boolean(row.A == 1), IrTermKind.Integer => _factory.Integer(row.Number),
                    IrTermKind.String when row.Text != null => _factory.String(row.Text), IrTermKind.Null => _factory.Null(Type(row.Type)),
                    IrTermKind.Variable => _factory.Variable(Variable(row.A)),
                    IrTermKind.Opaque => Opaque(row), IrTermKind.Unary => _factory.Unary(EnumValue<IrUnaryOperator>(row.A), DecodeTerm(row.B)),
                    IrTermKind.Binary => _factory.Binary(EnumValue<IrBinaryOperator>(row.A), DecodeTerm(row.B), DecodeTerm(row.C)),
                    IrTermKind.Conditional => _factory.Conditional(DecodeTerm(row.A), DecodeTerm(row.B), DecodeTerm(row.C)),
                    IrTermKind.Cast => _factory.Cast(Type(row.Type), DecodeTerm(row.A)), IrTermKind.Length => _factory.Length(DecodeTerm(row.A)),
                    IrTermKind.SequenceAccess => _factory.SequenceAccess(DecodeTerm(row.A), DecodeTerm(row.B)),
                    _ => throw Bad("Portable IR term metadata is invalid.")
                };
                if (term.Kind != row.Kind || term.Type != Type(row.Type) || !_distinctTerms.Add(term.Id)) throw Bad("Portable IR term equality or type metadata is inconsistent.");
                _terms[index] = term; _termState[index] = 2; return term;
            } finally { _termDepth--; } }
        private IrOpaqueTerm Opaque(PortableIrTerm row) {
            var purity = EnumValue<IrOpaquePurity>(row.C); var receiver = row.B < 0 ? null : DecodeTerm(row.B);
            IrTerm[] arguments = [.. row.Items.Select(DecodeTerm)]; return purity switch {
                IrOpaquePurity.Pure when row.D == -1 => _factory.PureOpaque(Member(row.A), receiver, arguments),
                IrOpaquePurity.Impure => _factory.ImpureOpaque(Operation(row.D), Member(row.A), receiver, arguments),
                _ => throw Bad("Portable IR opaque metadata is invalid.")
            }; }
        private (IrProgram? Program, IrBlockId[] Blocks, IrInstruction[] Instructions) DecodeProgram() {
            if (!_graph.HasProgram) return (null, [], []);
            var builder = new IrProgramBuilder(_factory); var blocks = new IrBlockId[_graph.Blocks.Length];
            for (var index = 0; index < blocks.Length; index++) {
                var row = _graph.Blocks[index] ?? throw Bad("Portable IR block rows cannot be null.");
                if (row.Instructions == null) throw Bad("Portable IR instruction arrays cannot be null."); blocks[index] = builder.CreateBlock(row.Name); }
            builder.SetEntry(Block(_graph.Entry, blocks)); var instructions = new List<IrInstruction>();
            for (var index = 0; index < blocks.Length; index++) foreach (var row in _graph.Blocks[index].Instructions)
                instructions.Add(Instruction(builder, blocks[index], blocks, row));
            return (builder.Build(), blocks, [.. instructions]); }
        private IrInstruction Instruction(IrProgramBuilder builder, IrBlockId block, IrBlockId[] blocks, PortableIrInstruction? row) {
            if (row == null || row.Items == null || !Enum.IsDefined(typeof(IrInstructionKind), row.Kind)) throw Bad("Portable IR instruction metadata is invalid.");
            var operation = Operation(row.Operation); return row.Kind switch {
                IrInstructionKind.Assign => builder.Assign(block, operation, Variable(row.A), Term(row.B)),
                IrInstructionKind.Load => builder.Load(block, operation, Variable(row.A), Location(builder, row.Location)),
                IrInstructionKind.Store => builder.Store(block, operation, Location(builder, row.Location), Term(row.A)),
                IrInstructionKind.Call => builder.Call(block, operation, row.A < 0 ? null : Variable(row.A), Member(row.B),
                    row.C < 0 ? null : Term(row.C), [.. row.Items.Select(Term)]),
                IrInstructionKind.Assume => builder.Assume(block, operation, Term(row.A)), IrInstructionKind.Assert => builder.Assert(block, operation, Term(row.A)),
                IrInstructionKind.Havoc => builder.Havoc(block, operation, EnumValue<IrHavocKind>(row.A), [.. row.Items.Select(Variable)]),
                IrInstructionKind.Branch => builder.Branch(block, operation, Term(row.A), Block(row.B, blocks), Block(row.C, blocks)),
                IrInstructionKind.Goto => builder.Goto(block, operation, Block(row.A, blocks)),
                IrInstructionKind.Return => builder.Return(block, operation, row.A < 0 ? null : Term(row.A)),
                _ => throw Bad("Portable IR instruction metadata is invalid.")
            }; }
        private IrLocation Location(IrProgramBuilder builder, PortableIrLocation? row) {
            if (row == null || row.Items == null || !Enum.IsDefined(typeof(IrLocationKind), row.Kind)) throw Bad("Portable IR location metadata is invalid.");
            IrLocation location = row.Kind switch {
                IrLocationKind.Member => builder.MemberLocation(Member(row.A), row.B < 0 ? null : Term(row.B), [.. row.Items.Select(Term)]),
                IrLocationKind.Sequence => builder.SequenceLocation(Term(row.A), Term(row.B)),
                _ => throw Bad("Portable IR location metadata is invalid.")
            };
            if (location.Type != Type(row.Type)) throw Bad("Portable IR location type metadata is inconsistent."); return location; }
        private IrTypeId Type(int index) { Check(index, _types.Length, "type"); return DecodeType(index); } private IrIdentityId Identity(int index) { Check(index, _identities.Length, "identity"); return _identities[index]; }
        private IrVarId Variable(int index) { Check(index, _variables.Length, "variable"); return _variables[index]; } private IrMemberId Member(int index) { Check(index, _members.Length, "member"); return _members[index]; }
        private OperationId Operation(int index) { Check(index, _operations.Length, "operation"); return _operations[index]; } private IrTerm Term(int index) => DecodeTerm(index);
        private static IrBlockId Block(int index, IrBlockId[] blocks) { Check(index, blocks.Length, "block"); return blocks[index]; }
        private static T EnumValue<T>(int value) where T : struct {
            if (!Enum.IsDefined(typeof(T), value)) throw Bad("Portable IR contains an unknown enum value.");
            return (T)Enum.ToObject(typeof(T), value); }
        private static void Enter(ref int depth, string kind) {
            if (++depth <= MaximumDecodeDepth) return; depth--; throw Bad($"Portable IR {kind} depth exceeds the supported limit."); }
        private static void Check(int index, int length, string kind) { if (index < 0 || index >= length) throw Bad($"Portable IR references an invalid {kind} index."); }
    }
}
