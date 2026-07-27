namespace SharpProof.CompilerArtifact;
#pragma warning disable IDE0055 // Compact artifact DTOs preserve the fixed production-size ceiling.
internal sealed class PortableIrGraph { public bool HasProgram { get; set; }
    public PortableIrType[] Types { get; set; } = []; public int[] Identities { get; set; } = [];
    public PortableIrVariable[] Variables { get; set; } = []; public PortableIrMember[] Members { get; set; } = []; public PortableIrOperation[] Operations { get; set; } = []; public PortableIrTerm[] Terms { get; set; } = [];
    public PortableIrBlock[] Blocks { get; set; } = []; public int Entry { get; set; } = -1; public int[] Roots { get; set; } = []; }
internal sealed class PortableIrType { public IrTypeKind Kind { get; set; } public string Name { get; set; } = string.Empty; public int Element { get; set; } = -1; }
internal sealed class PortableIrVariable { public string Name { get; set; } = string.Empty; public int Type { get; set; } = -1; }
internal sealed class PortableIrMember { public int Identity { get; set; } = -1;
    public int DeclaringType { get; set; } = -1; public string Name { get; set; } = string.Empty;
    public int ReturnType { get; set; } = -1; public bool IsStatic { get; set; } public int[] ParameterTypes { get; set; } = [];
    public string? DocumentationCommentId { get; set; } }
internal sealed class PortableIrOperation { public string? Description { get; set; } }
internal sealed class PortableIrTerm { public IrTermKind Kind { get; set; }
    public int Type { get; set; } = -1;
    public int A { get; set; } = -1; public int B { get; set; } = -1; public int C { get; set; } = -1; public int D { get; set; } = -1;
    public long Number { get; set; } public string? Text { get; set; } public int[] Items { get; set; } = []; }
internal sealed class PortableIrLocation { public IrLocationKind Kind { get; set; } public int Type { get; set; } = -1;
    public int A { get; set; } = -1; public int B { get; set; } = -1; public int[] Items { get; set; } = []; }
internal sealed class PortableIrInstruction {
    public IrInstructionKind Kind { get; set; } public int Operation { get; set; } = -1;
    public int A { get; set; } = -1; public int B { get; set; } = -1; public int C { get; set; } = -1;
    public int[] Items { get; set; } = []; public PortableIrLocation? Location { get; set; } }
internal sealed class PortableIrBlock { public string? Name { get; set; } public PortableIrInstruction[] Instructions { get; set; } = []; }
internal sealed class DecodedPortableIrGraph(IrFactory factory, IrProgram? program, IrTerm[] roots,
    IrVarId[] variables, IrBlockId[] blocks, IrInstruction[] instructions) {
    internal IrFactory Factory { get; } = factory; internal IrProgram? Program { get; } = program;
    internal IReadOnlyList<IrTerm> Roots { get; } = roots;
    internal IReadOnlyList<IrVarId> Variables { get; } = variables; internal IReadOnlyList<IrBlockId> Blocks { get; } = blocks; internal IReadOnlyList<IrInstruction> Instructions { get; } = instructions; }
internal sealed class EncodedPortableIrGraph(PortableIrGraph graph, IReadOnlyDictionary<IrVarId, int> variableIndices,
    IReadOnlyDictionary<IrInstructionId, int> instructionIndices) {
    internal PortableIrGraph Graph { get; } = graph; internal IReadOnlyDictionary<IrVarId, int> VariableIndices { get; } = variableIndices;
    internal IReadOnlyDictionary<IrInstructionId, int> InstructionIndices { get; } = instructionIndices; }
