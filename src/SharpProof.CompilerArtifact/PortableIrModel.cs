#nullable enable

using System.Collections.Immutable;
using SharpProof.Ir;

namespace SharpProof.CompilerArtifact;

internal sealed record PortableIrGraph
{
    [System.Text.Json.Serialization.JsonRequired]
    public IrExecutionSemantics Semantics { get; init; }
    public bool HasProgram { get; init; }
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<PortableIrType> Types { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<int> Identities { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<PortableIrVariable> Variables { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<PortableIrMember> Members { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<PortableIrOperation> Operations { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<PortableIrTerm> Terms { get; init; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<PortableIrBlock> Blocks { get; init; } = [];
    public int Entry { get; init; } = -1;
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<int> Roots { get; init; } = [];

}

internal sealed record PortableIrType
{
    public IrTypeKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Element { get; init; } = -1;
    [System.Text.Json.Serialization.JsonRequired]
    public int Width { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public bool Signed { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ClosedSealedReference { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? ClosedClassBase { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool InterfaceReference { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<int>? ImplementedInterfaces { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool DelegateReference { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<ResourceStorageKind>? ClosureFrameFields { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<ResourceStorageKind>? ObjectStorageFields { get; init; }

    [System.Text.Json.Serialization.JsonConstructor]
    public PortableIrType() { }

    public PortableIrType(
    IrTypeKind kind = default,
    string? name = null,
    int element = -1,
    int width = 0,
    bool signed = false
) : this()
    {
        Kind = kind;
        Name = name ?? string.Empty;
        Element = element;
        Width = width;
        Signed = signed;
    }

}

internal sealed record PortableIrVariable
{
    public string Name { get; init; } = string.Empty;
    public int Type { get; init; } = -1;

    [System.Text.Json.Serialization.JsonConstructor]
    public PortableIrVariable() { }

    public PortableIrVariable(
    string? name = null,
    int type = -1
) : this()
    {
        Name = name ?? string.Empty;
        Type = type;
    }

}

internal sealed record PortableIrMember
{
    public int Identity { get; init; } = -1;
    public int DeclaringType { get; init; } = -1;
    public string Name { get; init; } = string.Empty;
    public int ReturnType { get; init; } = -1;
    public bool IsStatic { get; init; }
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<int> ParameterTypes { get; init; } = [];
    public string? DocumentationCommentId { get; init; }

    [System.Text.Json.Serialization.JsonConstructor]
    public PortableIrMember() { }

    public PortableIrMember(
    int identity = -1,
    int declaringType = -1,
    string? name = null,
    int returnType = -1,
    bool isStatic = false,
    ImmutableArray<int> parameterTypes /* SPINT001: all values valid - default is normalized to initialized empty metadata */ = default
) : this()
    {
        Identity = identity;
        DeclaringType = declaringType;
        Name = name ?? string.Empty;
        ReturnType = returnType;
        IsStatic = isStatic;
        ParameterTypes = parameterTypes.IsDefault ? [] : parameterTypes;
    }

}

internal sealed record PortableIrOperation
{
    public string? Description { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public PortableIrSourceSpan? SourceSpan { get; init; }

    [System.Text.Json.Serialization.JsonConstructor]
    public PortableIrOperation() { }

    public PortableIrOperation(
    string? description = null
) : this()
    {
        Description = description;
    }

}

internal sealed record PortableIrSourceSpan
{
    [System.Text.Json.Serialization.JsonRequired]
    public string Document { get; init; } = string.Empty;
    [System.Text.Json.Serialization.JsonRequired]
    public int Start { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public int Length { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }

}

internal sealed record PortableIrTerm
{
    public IrTermKind Kind { get; init; }
    public int Type { get; init; } = -1;
    public int A { get; init; } = -1;
    public int B { get; init; } = -1;
    public int C { get; init; } = -1;
    public int D { get; init; } = -1;
    public long Number { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public ulong Bits { get; init; }
    public string? Text { get; init; }
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<int> Items { get; init; } = [];

    [System.Text.Json.Serialization.JsonConstructor]
    public PortableIrTerm() { }

    public PortableIrTerm(
    IrTermKind kind = default,
    int type = -1,
    int a = -1,
    int b = -1,
    int c = -1,
    int d = -1,
    long number = 0,
    string? text = null,
    ImmutableArray<int> items /* SPINT001: all values valid - default is normalized to initialized empty metadata */ = default
) : this()
    {
        Kind = kind;
        Type = type;
        A = a;
        B = b;
        C = c;
        D = d;
        Number = number;
        Text = text;
        Items = items.IsDefault ? [] : items;
    }

}

internal sealed record PortableIrLocation
{
    public IrLocationKind Kind { get; init; }
    public int Type { get; init; } = -1;
    public int A { get; init; } = -1;
    public int B { get; init; } = -1;
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<int> Items { get; init; } = [];

    [System.Text.Json.Serialization.JsonConstructor]
    public PortableIrLocation() { }

    public PortableIrLocation(
    IrLocationKind kind = default,
    int type = -1,
    int a = -1,
    int b = -1,
    ImmutableArray<int> items /* SPINT001: all values valid - default is normalized to initialized empty metadata */ = default
) : this()
    {
        Kind = kind;
        Type = type;
        A = a;
        B = b;
        Items = items.IsDefault ? [] : items;
    }

}

internal sealed record PortableIrInstruction
{
    public IrInstructionKind Kind { get; init; }
    public int Operation { get; init; } = -1;
    public int A { get; init; } = -1;
    public int B { get; init; } = -1;
    public int C { get; init; } = -1;
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<int> Items { get; init; } = [];
    public PortableIrLocation? Location { get; init; }
    [System.Text.Json.Serialization.JsonRequired]
    public IrHavocOrigin Origin { get; init; } = IrHavocOrigin.Approximation;

    [System.Text.Json.Serialization.JsonConstructor]
    public PortableIrInstruction() { }

    public PortableIrInstruction(
    IrInstructionKind kind = default,
    int operation = -1,
    int a = -1,
    int b = -1,
    int c = -1,
    ImmutableArray<int> items /* SPINT001: all values valid - default is normalized to initialized empty metadata */ = default,
    PortableIrLocation? location = null
) : this()
    {
        Kind = kind;
        Operation = operation;
        A = a;
        B = b;
        C = c;
        Items = items.IsDefault ? [] : items;
        Location = location;
    }

}

internal sealed record PortableIrBlock
{
    public string? Name { get; init; }
    [System.Text.Json.Serialization.JsonConverter(typeof(SharpProof.Worker.Protocol.ProtocolArrayJsonConverter))]
    public ImmutableArray<PortableIrInstruction> Instructions { get; init; } = [];

    [System.Text.Json.Serialization.JsonConstructor]
    public PortableIrBlock() { }

    public PortableIrBlock(
    string? name = null,
    ImmutableArray<PortableIrInstruction> instructions /* SPINT001: all values valid - default is normalized to initialized empty metadata */ = default
) : this()
    {
        Name = name;
        Instructions = instructions.IsDefault ? [] : instructions;
    }

}

internal sealed class DecodedPortableIrGraph(
    IrFactory factory,
    IrProgram? program,
    ImmutableArray<IrTerm> roots,
    ImmutableArray<IrVarId> variables,
    ImmutableArray<IrInstruction> instructions,
    ImmutableArray<OperationId> operations
)
{
    internal IrFactory Factory { get; } = factory ?? throw new ArgumentNullException(nameof(factory));
    internal IrProgram? Program { get; } = program;
    internal ImmutableArray<IrTerm> Roots { get; } = roots.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(roots)) : roots;
    internal ImmutableArray<IrVarId> Variables { get; } = variables.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(variables)) : variables;
    internal ImmutableArray<IrInstruction> Instructions { get; } = instructions.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(instructions)) : instructions;
    internal ImmutableArray<OperationId> Operations { get; } = operations.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(operations)) : operations;
}

internal sealed class EncodedPortableIrGraph(
    PortableIrGraph graph,
    ImmutableDictionary<IrVarId, int> variableIndices,
    ImmutableDictionary<IrInstructionId, int> instructionIndices,
    ImmutableDictionary<OperationId, int> operationIndices
)
{
    internal PortableIrGraph Graph { get; } = graph ?? throw new ArgumentNullException(nameof(graph));
    internal ImmutableDictionary<IrVarId, int> VariableIndices { get; } = variableIndices ?? throw new ArgumentNullException(nameof(variableIndices));
    internal ImmutableDictionary<IrInstructionId, int> InstructionIndices { get; } = instructionIndices ?? throw new ArgumentNullException(nameof(instructionIndices));
    internal ImmutableDictionary<OperationId, int> OperationIndices { get; } = operationIndices ?? throw new ArgumentNullException(nameof(operationIndices));
}

internal static class PortableIrWireCatalog
{
    internal static readonly ImmutableArray<IrOpaquePurity> OpaquePurities = [
        IrOpaquePurity.Pure,
        IrOpaquePurity.Impure
    ];
    internal static readonly ImmutableArray<IrUnaryOperator> UnaryOperators = [
        IrUnaryOperator.Not,
        IrUnaryOperator.Negate
    ];
    internal static readonly ImmutableArray<IrBinaryOperator> BinaryOperators = [
        IrBinaryOperator.Add,
        IrBinaryOperator.Subtract,
        IrBinaryOperator.Multiply,
        IrBinaryOperator.Divide,
        IrBinaryOperator.Remainder,
        IrBinaryOperator.AndAlso,
        IrBinaryOperator.OrElse,
        IrBinaryOperator.Equal,
        IrBinaryOperator.NotEqual,
        IrBinaryOperator.LessThan,
        IrBinaryOperator.LessThanOrEqual,
        IrBinaryOperator.GreaterThan,
        IrBinaryOperator.GreaterThanOrEqual,
        IrBinaryOperator.StringConcat,
        IrBinaryOperator.StringEquals,
        IrBinaryOperator.BitwiseAnd,
        IrBinaryOperator.ShiftLeft,
        IrBinaryOperator.ShiftRight,
        IrBinaryOperator.BitwiseOr,
        IrBinaryOperator.BitwiseXor
    ];
    internal static readonly ImmutableArray<IrHavocKind> HavocKinds = [
        IrHavocKind.Variables,
        IrHavocKind.Memory,
        IrHavocKind.VariablesAndMemory
    ];
    internal static readonly ImmutableArray<IrHavocOrigin> HavocOrigins = [
        IrHavocOrigin.Input, IrHavocOrigin.SpecResult, IrHavocOrigin.Approximation
    ];
    internal static readonly ImmutableArray<IrExecutionSemantics> ExecutionSemantics = [
        IrExecutionSemantics.Legacy, IrExecutionSemantics.Total
    ];
    internal static readonly ImmutableArray<IrExceptionKind> ExceptionKinds = [
        IrExceptionKind.DivideByZero, IrExceptionKind.Overflow, IrExceptionKind.NullReference,
        IrExceptionKind.IndexOutOfRange, IrExceptionKind.InvalidCast, IrExceptionKind.Argument,
        IrExceptionKind.Unknown, IrExceptionKind.Explicit, IrExceptionKind.ArgumentOutOfRange
    ];
}
