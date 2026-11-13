namespace SharpProof.Ir;

internal interface IIrIdentifierTag
{
    string Prefix { get; }
}

internal readonly record struct IrIdentityTag : IIrIdentifierTag
{
    public string Prefix => "identity";
}

internal readonly record struct IrTermTag : IIrIdentifierTag
{
    public string Prefix => "ir";
}

internal readonly record struct IrVariableTag : IIrIdentifierTag
{
    public string Prefix => "v";
}

internal readonly record struct IrTypeTag : IIrIdentifierTag
{
    public string Prefix => "t";
}

internal readonly record struct IrMemberTag : IIrIdentifierTag
{
    public string Prefix => "m";
}

internal readonly record struct IrStringTag : IIrIdentifierTag
{
    public string Prefix => "s";
}

internal readonly record struct IrOperationTag : IIrIdentifierTag
{
    public string Prefix => "op";
}

internal readonly record struct IrBlockTag : IIrIdentifierTag
{
    public string Prefix => "b";
}

internal readonly record struct IrInstructionTag : IIrIdentifierTag
{
    public string Prefix => "i";
}
