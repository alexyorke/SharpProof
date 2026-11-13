namespace SharpProof.Ir;

internal enum IrTermKind
{
    Boolean = 0,
    Integer = 1,
    String = 2,
    Null = 3,
    Variable = 4,
    Opaque = 5,
    Unary = 6,
    Binary = 7,
    Conditional = 8,
    Cast = 9,
    Length = 10,
    SequenceAccess = 11,
    EmptyArray = 12
}

internal enum IrOpaquePurity
{
    Pure = 0,
    Impure = 1
}
