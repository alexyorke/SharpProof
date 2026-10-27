using System.Collections.Immutable;
using SharpProof.Ir;

namespace SharpProof.CompilerArtifact;

// Row encodings for terms, locations and instructions. Each kind uses a fixed
// subset of the generic row slots; every other slot must hold its default so
// that one IR graph has exactly one wire encoding.
internal static partial class PortableIrGraphCodec
{
    [Flags]
    private enum Slots
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4,
        D = 8,
        Number = 16,
        Text = 32,
        Items = 64,
        Location = 128
    }

    private static Slots TermSlots(IrTermKind kind)
    {
        return kind switch
        {
            IrTermKind.Boolean or IrTermKind.Variable or IrTermKind.Cast or IrTermKind.Length => Slots.A,
            IrTermKind.Integer => Slots.Number,
            IrTermKind.String => Slots.Text,
            IrTermKind.Null or IrTermKind.EmptyArray => Slots.None,
            IrTermKind.Opaque => Slots.A | Slots.B | Slots.C | Slots.D | Slots.Items,
            IrTermKind.Unary or IrTermKind.SequenceAccess => Slots.A | Slots.B,
            IrTermKind.Binary or IrTermKind.Conditional => Slots.A | Slots.B | Slots.C,
            _ => throw Bad($"Portable IR {kind} slots are not declared.")
        };
    }

    private static Slots LocationSlots(IrLocationKind kind)
    {
        return kind switch
        {
            IrLocationKind.Member => Slots.A | Slots.B | Slots.Items,
            IrLocationKind.Sequence => Slots.A | Slots.B,
            _ => throw Bad($"Portable IR {kind} slots are not declared.")
        };
    }

    private static Slots InstructionSlots(IrInstructionKind kind)
    {
        return kind switch
        {
            IrInstructionKind.Assign => Slots.A | Slots.B,
            IrInstructionKind.Load or IrInstructionKind.Store => Slots.A | Slots.Location,
            IrInstructionKind.Call or IrInstructionKind.Allocate => Slots.A | Slots.B | Slots.C | Slots.Items,
            IrInstructionKind.Assume or IrInstructionKind.Assert or IrInstructionKind.Goto or
                IrInstructionKind.Return or IrInstructionKind.Lock => Slots.A,
            IrInstructionKind.Havoc => Slots.A | Slots.Items,
            IrInstructionKind.Branch => Slots.A | Slots.B | Slots.C,
            IrInstructionKind.Throw => Slots.A | Slots.B,
            IrInstructionKind.ExceptionalExit => Slots.None,
            IrInstructionKind.Write => Slots.A | Slots.B | Slots.C | Slots.Items | Slots.Location,
            _ => throw Bad($"Portable IR {kind} slots are not declared.")
        };
    }

    private static void RequireCanonicalSlots(
        object kind, Slots used, int a, int b, int c, int d, long number, string? text,
        ImmutableArray<int> items, PortableIrLocation? location)
    {
        kind = RepositoryParameterGuard.NotNull(kind, nameof(kind));
        RepositoryParameterGuard.RequireInitialized(items.IsDefault, nameof(items), "Array must be initialized.");
        Require(
            ((used & Slots.A) != 0 || a == -1) &&
            ((used & Slots.B) != 0 || b == -1) &&
            ((used & Slots.C) != 0 || c == -1) &&
            ((used & Slots.D) != 0 || d == -1) &&
            ((used & Slots.Number) != 0 || number == 0) &&
            ((used & Slots.Text) != 0 || text == null) &&
            ((used & Slots.Items) != 0 || items.Length == 0) &&
            ((used & Slots.Location) != 0 || location == null),
            $"Portable IR {kind} slots are not canonical.");
    }

    private static void RequireCanonicalTermSlots(PortableIrTerm row)
    {
        row = RepositoryParameterGuard.NotNull(row, nameof(row));
        RequireCanonicalSlots(row.Kind, TermSlots(row.Kind),
            row.A, row.B, row.C, row.D, row.Number, row.Text, row.Items, null);
        Require(row.Kind == IrTermKind.Integer || row.Bits == 0,
            "Only integer terms may carry raw integer bits.");
    }

    private static void RequireCanonicalInstructionSlots(PortableIrInstruction row)
    {
        row = RepositoryParameterGuard.NotNull(row, nameof(row));
        RequireCanonicalSlots(row.Kind, InstructionSlots(row.Kind),
            row.A, row.B, row.C, -1, 0, null, row.Items, row.Location);
        Require(row.Kind == IrInstructionKind.Havoc || row.Origin == IrHavocOrigin.Approximation,
            "Only havoc instructions may carry an origin.");
    }

    private static void RequireCanonicalLocationSlots(PortableIrLocation row)
    {
        row = RepositoryParameterGuard.NotNull(row, nameof(row));
        RequireCanonicalSlots(row.Kind, LocationSlots(row.Kind),
            row.A, row.B, -1, -1, 0, null, row.Items, null);
    }

    private sealed partial class Encoder
    {
        private PortableIrType TypeRow(IrTypeId id)
        {
            RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
            var value = _factory.GetTypeInfo(id);
            return new(
                value.Kind,
                _factory.GetString(value.Name),
                value.ElementType.HasValue ? TypeIndex(value.ElementType.Value) : -1,
                value.Width, value.Signed)
            {
                ClosedSealedReference = _factory.IsClosedSealedReferenceType(id),
                ClosedClassBase = _factory.TryGetClosedClassReferenceBase(id, out var parent) ? parent.HasValue ? TypeIndex(parent.Value) : -1 : null,
                InterfaceReference = _factory.IsInterfaceReferenceType(id),
                DelegateReference = _factory.IsDelegateReferenceType(id),
                ClosureFrameFields = _factory.TryGetClosureFrameType(id, out var captures) ? captures : null,
                ObjectStorageFields = _factory.TryGetObjectStorageType(id, out var storage) ? storage : null,
                ImplementedInterfaces = _factory.GetImplementedReferenceInterfaces(id) is { Length: > 0 } interfaces ? interfaces.Select(TypeIndex).OrderBy(index => index).ToImmutableArray() : null
            };
        }

        private PortableIrVariable VariableRow(IrVarId id)
        {
            RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
            var value = _factory.GetVariableInfo(id);
            return new(
                _factory.GetString(value.Name),
                TypeIndex(value.Type));
        }

        private PortableIrMember MemberRow(IrMemberId id)
        {
            RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
            var value = _factory.GetMemberInfo(id);
            return new(
                _identities.Add(value.Identity),
                TypeIndex(value.DeclaringType),
                _factory.GetString(value.Name),
                TypeIndex(value.ReturnType),
                value.IsStatic)
            { ParameterTypes = value.ParameterTypes.Select(TypeIndex).ToImmutableArray() };
        }

        private PortableIrOperation OperationRow(OperationId id)
        {
            RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
            var value = _factory.GetOperationInfo(id);
            return new(
                value.Description.HasValue ? _factory.GetString(value.Description.Value) : null)
            {
                SourceSpan = value.SourceSpan is { } span ? new PortableIrSourceSpan
                {
                    Document = span.Document,
                    Start = span.Start,
                    Length = span.Length,
                    Line = span.Line,
                    Column = span.Column
                } : null
            };
        }

        // Argument order fixes the order in which referenced rows are numbered.
        private PortableIrTerm TermRow(IrId id)
        {
            RepositoryParameterGuard.ThrowIfInvalid(id.IsDefault, nameof(id));
            _cancellationToken.ThrowIfCancellationRequested();
            var term = _factory.GetTerm(id);
            switch (term.Kind)
            {
                case IrTermKind.Boolean:
                    return TermRow(term, ((IrBooleanTerm)term).Value ? 1 : 0);
                case IrTermKind.Integer:
                    var integer = (IrIntegerTerm)term;
                    return TermRow(term, number: integer.Width == 0 ? integer.Value : 0) with
                    {
                        Bits = integer.Width == 0 ? 0 : integer.Bits
                    };
                case IrTermKind.String:
                    return TermRow(term, text: _factory.GetString(((IrStringTerm)term).Value));
                case IrTermKind.Null:
                case IrTermKind.EmptyArray:
                    return TermRow(term);
                case IrTermKind.Variable:
                    return TermRow(term, VariableIndex(((IrVariableTerm)term).Variable));
                case IrTermKind.Opaque:
                    var opaque = (IrOpaqueTerm)term;
                    return TermRow(
                        term,
                        MemberIndex(opaque.Member),
                        OptionalTermIndex(opaque.Receiver),
                        Wire(opaque.Purity, OpaquePurities),
                        opaque.Purity == IrOpaquePurity.Pure ? -1 : OperationIndex(opaque.Operation),
                        items: TermIndices(opaque.Arguments));
                case IrTermKind.Unary:
                    var unary = (IrUnaryTerm)term;
                    return TermRow(term, Wire(unary.Operator, UnaryOperators), TermIndex(unary.Operand));
                case IrTermKind.Binary:
                    var binary = (IrBinaryTerm)term;
                    return TermRow(term, Wire(binary.Operator, BinaryOperators),
                        TermIndex(binary.Left), TermIndex(binary.Right));
                case IrTermKind.Conditional:
                    var conditional = (IrConditionalTerm)term;
                    return TermRow(term, TermIndex(conditional.Condition),
                        TermIndex(conditional.WhenTrue), TermIndex(conditional.WhenFalse));
                case IrTermKind.Cast:
                    return TermRow(term, TermIndex(((IrCastTerm)term).Operand));
                case IrTermKind.Length:
                    return TermRow(term, TermIndex(((IrLengthTerm)term).Value));
                case IrTermKind.SequenceAccess:
                    var access = (IrSequenceAccessTerm)term;
                    return TermRow(term, TermIndex(access.Sequence), TermIndex(access.Index));
                default:
                    throw Bad($"Portable IR {term.Kind} slots are not declared.");
            }
        }

        private PortableIrLocation LocationRow(IrLocation location)
        {
            location = RepositoryParameterGuard.NotNull(location, nameof(location));
            switch (location)
            {
                case IrMemberLocation member:
                    var memberIndex = MemberIndex(member.Member);
                    var receiver = OptionalTermIndex(member.Receiver);
                    var arguments = TermIndices(member.Arguments);
                    return new(location.Kind, TypeIndex(location.Type), memberIndex, receiver) { Items = arguments };
                case IrSequenceLocation sequence:
                    var sequenceIndex = TermIndex(sequence.Sequence);
                    var index = TermIndex(sequence.Index);
                    return new(location.Kind, TypeIndex(location.Type), sequenceIndex, index);
                default:
                    throw Bad($"Portable IR {location.Kind} slots are not declared.");
            }
        }

        private PortableIrInstruction InstructionRow(IrInstruction instruction)
        {
            instruction = RepositoryParameterGuard.NotNull(instruction, nameof(instruction));
            _cancellationToken.ThrowIfCancellationRequested();
            var operation = OperationIndex(instruction.Operation);
            switch (instruction)
            {
                case IrAssignInstruction assign:
                    return InstructionRow(instruction, operation, VariableIndex(assign.Target), TermIndex(assign.Value));
                case IrLoadInstruction load:
                    return InstructionRow(instruction, operation, VariableIndex(load.Target),
                        location: LocationRow(load.Location));
                case IrStoreInstruction store:
                    return InstructionRow(instruction, operation, TermIndex(store.Value),
                        location: LocationRow(store.Location));
                case IrCallInstruction call:
                    return InstructionRow(instruction, operation, OptionalVariableIndex(call.Target),
                        MemberIndex(call.Member), OptionalTermIndex(call.Receiver), TermIndices(call.Arguments));
                case IrAssumeInstruction assume:
                    return InstructionRow(instruction, operation, TermIndex(assume.Condition));
                case IrAssertInstruction assertion:
                    return InstructionRow(instruction, operation, TermIndex(assertion.Condition));
                case IrHavocInstruction havoc:
                    return InstructionRow(instruction, operation, Wire(havoc.HavocKind, HavocKinds),
                        items: [.. havoc.Variables.Select(VariableIndex).OrderBy(static index => index)]);
                case IrBranchInstruction branch:
                    return InstructionRow(instruction, operation, TermIndex(branch.Condition),
                        BlockIndex(branch.WhenTrue), BlockIndex(branch.WhenFalse));
                case IrGotoInstruction jump:
                    return InstructionRow(instruction, operation, BlockIndex(jump.Target));
                case IrReturnInstruction exit:
                    return InstructionRow(instruction, operation, OptionalTermIndex(exit.Value));
                case IrThrowInstruction thrown:
                    return InstructionRow(instruction, operation, (int)thrown.ExceptionKind, BlockIndex(thrown.Target));
                case IrAllocationInstruction allocation:
                    return InstructionRow(instruction, operation, TypeIndex(allocation.AllocatedType),
                        OptionalVariableIndex(allocation.Target), OptionalTermIndex(allocation.Length),
                        TermIndices(allocation.InitialValues));
                case IrWriteInstruction write:
                    var target = write.Field == null ? OptionalTermIndex(write.Target) : -1;
                    var writeIndex = write.Field == null ? OptionalTermIndex(write.Index) : -1;
                    ImmutableArray<int>? values = write.Value == null
                        ? null
                        : TermIndices(write.Length is { } length ? [write.Value, length] : [write.Value]);
                    var field = write.Field is { } member
                        ? LocationRow(new IrMemberLocation(write.Value!.Type, member, write.Target,
                            write.Index is { } key ? [key] : []))
                        : null;
                    return InstructionRow(instruction, operation, (int)write.Region, target, writeIndex, values, field);
                case IrLockInstruction synchronization:
                    return InstructionRow(instruction, operation, TermIndex(synchronization.Receiver));
                default:
                    return instruction.Kind == IrInstructionKind.ExceptionalExit
                        ? InstructionRow(instruction, operation)
                        : throw Bad($"Portable IR {instruction.Kind} slots are not declared.");
            }
        }

        private int OptionalVariableIndex(IrVarId? id)
        {
            return id.HasValue ? VariableIndex(id.Value) : -1;
        }
    }

    private sealed partial class Decoder
    {
        private IrTerm DecodeTermRow(PortableIrTerm row, int depth)
        {
            row = RepositoryParameterGuard.NotNull(row, nameof(row));
            switch (row.Kind)
            {
                case IrTermKind.Boolean:
                    RequireTerm(row.A is 0 or 1);
                    return _factory.Boolean(row.A == 1);
                case IrTermKind.Integer:
                    if (_factory.GetTypeInfo(Type(row.Type)).Width == 0)
                    {
                        RequireTerm(row.Bits == 0);
                        return _factory.Integer(row.Number);
                    }
                    RequireTerm(row.Number == 0);
                    return _factory.IntegerBits(Type(row.Type), row.Bits);
                case IrTermKind.String:
                    RequireTerm(row.Text != null);
                    return _factory.String(row.Text!);
                case IrTermKind.Null:
                    return _factory.Null(Type(row.Type));
                case IrTermKind.EmptyArray:
                    return _factory.EmptyArray(Type(row.Type));
                case IrTermKind.Variable:
                    return _factory.Variable(Variable(row.A));
                case IrTermKind.Opaque:
                    var purity = Wire(row.C, OpaquePurities);
                    var receiver = OptionalTerm(row.B, depth);
                    var arguments = Terms(row.Items, depth + 1);
                    return purity switch
                    {
                        IrOpaquePurity.Pure when row.D == -1 =>
                            _factory.PureOpaqueSnapshot(Member(row.A), receiver, arguments),
                        IrOpaquePurity.Impure =>
                            _factory.ImpureOpaqueSnapshot(Operation(row.D), Member(row.A), receiver, arguments),
                        _ => throw Bad("Portable IR term metadata is invalid.")
                    };
                case IrTermKind.Unary:
                    return _factory.Unary(Wire(row.A, UnaryOperators), DecodeTerm(row.B, depth + 1));
                case IrTermKind.Binary:
                    return _factory.RewriteBinary(Wire(row.A, BinaryOperators),
                        DecodeTerm(row.B, depth + 1), DecodeTerm(row.C, depth + 1));
                case IrTermKind.Conditional:
                    return _factory.Conditional(DecodeTerm(row.A, depth + 1),
                        DecodeTerm(row.B, depth + 1), DecodeTerm(row.C, depth + 1));
                case IrTermKind.Cast:
                    return _factory.Cast(Type(row.Type), DecodeTerm(row.A, depth + 1));
                case IrTermKind.Length:
                    return _factory.Length(DecodeTerm(row.A, depth + 1));
                case IrTermKind.SequenceAccess:
                    return _factory.SequenceAccess(DecodeTerm(row.A, depth + 1), DecodeTerm(row.B, depth + 1));
                default:
                    throw Bad($"Portable IR {row.Kind} slots are not declared.");
            }
        }

        private static void RequireTerm(bool condition)
        {
            Require(condition, "Portable IR term metadata is invalid.");
        }

        private ImmutableArray<IrTerm> Terms(ImmutableArray<int> indices, int depth)
        {
            Require(!indices.IsDefault, "Portable IR projection indices cannot be null.");
            return [.. indices.Select(index => DecodeTerm(index, depth))];
        }

        private IrLocation DecodeLocationRow(IrProgramBuilder builder, PortableIrLocation row)
        {
            builder = RepositoryParameterGuard.NotNull(builder, nameof(builder));
            row = RepositoryParameterGuard.NotNull(row, nameof(row));
            return row.Kind switch
            {
                IrLocationKind.Member => builder.MemberLocationSnapshot(
                    Member(row.A), OptionalTerm(row.B), Terms(row.Items, 0)),
                IrLocationKind.Sequence => builder.SequenceLocation(Term(row.A), Term(row.B)),
                _ => throw Bad($"Portable IR {row.Kind} slots are not declared.")
            };
        }

        private IrInstruction DecodeInstructionRow(
            IrProgramBuilder builder, IrBlockId block, ImmutableArray<IrBlockId> blocks, PortableIrInstruction row)
        {
            builder = RepositoryParameterGuard.NotNull(builder, nameof(builder));
            RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
            RepositoryParameterGuard.RequireInitialized(blocks.IsDefault, nameof(blocks), "Array must be initialized.");
            row = RepositoryParameterGuard.NotNull(row, nameof(row));
            switch (row.Kind)
            {
                case IrInstructionKind.Assign:
                    return builder.Assign(block, Operation(row.Operation), Variable(row.A), Term(row.B));
                case IrInstructionKind.Load:
                    return builder.Load(block, Operation(row.Operation), Variable(row.A), Location(builder, row.Location));
                case IrInstructionKind.Store:
                    return builder.Store(block, Operation(row.Operation), Location(builder, row.Location), Term(row.A));
                case IrInstructionKind.Call:
                    return builder.CallSnapshot(block, Operation(row.Operation), OptionalVariable(row.A),
                        Member(row.B), OptionalTerm(row.C), Terms(row.Items, 0));
                case IrInstructionKind.Assume:
                    return builder.Assume(block, Operation(row.Operation), Term(row.A));
                case IrInstructionKind.Assert:
                    return builder.Assert(block, Operation(row.Operation), Term(row.A));
                case IrInstructionKind.Havoc:
                    return builder.HavocSnapshot(block, Operation(row.Operation), Wire(row.A, HavocKinds),
                        row.Origin, [.. row.Items.Select(Variable)]);
                case IrInstructionKind.Branch:
                    return builder.Branch(block, Operation(row.Operation), Term(row.A),
                        Block(row.B, blocks), Block(row.C, blocks));
                case IrInstructionKind.Goto:
                    return builder.Goto(block, Operation(row.Operation), Block(row.A, blocks));
                case IrInstructionKind.Return:
                    return builder.Return(block, Operation(row.Operation), OptionalTerm(row.A));
                case IrInstructionKind.Throw:
                    return builder.Throw(block, Operation(row.Operation), (IrExceptionKind)row.A, Block(row.B, blocks));
                case IrInstructionKind.ExceptionalExit:
                    return builder.ExceptionalExit(block, Operation(row.Operation));
                case IrInstructionKind.Allocate:
                    return builder.Allocate(block, Operation(row.Operation), Type(row.A), OptionalVariable(row.B),
                        OptionalTerm(row.C), Terms(row.Items, 0));
                case IrInstructionKind.Lock:
                    return builder.Lock(block, Operation(row.Operation), Term(row.A));
                case IrInstructionKind.Write:
                    return DecodeWrite(builder, block, row);
                default:
                    throw Bad($"Portable IR {row.Kind} slots are not declared.");
            }
        }

        // A write is a field store (location), an element store (target and
        // index) or a bare region write, and nothing else.
        private IrWriteInstruction DecodeWrite(IrProgramBuilder builder, IrBlockId block, PortableIrInstruction row)
        {
            builder = RepositoryParameterGuard.NotNull(builder, nameof(builder));
            RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
            row = RepositoryParameterGuard.NotNull(row, nameof(row));
            if (row.Location != null)
            {
                return row.B < 0 && row.C < 0 && row.Items.Length == 1 &&
                    Location(builder, row.Location) is IrMemberLocation { Receiver: { } receiver, Arguments.Length: <= 1 } field
                    ? builder.FieldStore(block, Operation(row.Operation), (IrWriteRegion)row.A, receiver, field.Member,
                        Term(row.Items[0]), field.Arguments.IsEmpty ? null : field.Arguments[0])
                    : throw InvalidWrite();
            }
            if (OptionalTerm(row.B) is { } stored)
            {
                return row.A == (int)IrWriteRegion.Element && row.Items.Length is 1 or 2 && OptionalTerm(row.C) is { } index
                    ? builder.ElementStore(block, Operation(row.Operation), stored, index, Term(row.Items[0]),
                        row.Items.Length == 2 ? Term(row.Items[1]) : null)
                    : throw InvalidWrite();
            }
            return row.C < 0 && row.Items.Length == 0
                ? builder.Write(block, Operation(row.Operation), (IrWriteRegion)row.A)
                : throw InvalidWrite();
        }

        private static InvalidDataException InvalidWrite()
        {
            return Bad("Portable IR instruction metadata is invalid.");
        }
    }
}
