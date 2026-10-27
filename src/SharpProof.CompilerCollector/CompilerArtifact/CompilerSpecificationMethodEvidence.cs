namespace SharpProof.CompilerCollector;

// Compiler binding evidence projected once from the selected audited catalog.
internal sealed record CompilerSpecificationMethodEvidence(
    string DocumentationCommentId,
    ImmutableArray<ApiSpecAssemblyIdentity> Assemblies,
    ImmutableArray<IrTypeKind> ParameterTypes,
    IrTypeKind ResultType, SpecTermDeclaration Result,
    string EvidenceSha256 = "", string EvidenceIdentity = "")
{

    public string DocumentationCommentId { get; init; } = DocumentationCommentId ?? throw new System.ArgumentNullException(nameof(DocumentationCommentId));
    public ImmutableArray<ApiSpecAssemblyIdentity> Assemblies { get; init; } = Assemblies.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Assemblies)) : Assemblies;
    public ImmutableArray<IrTypeKind> ParameterTypes { get; init; } = ParameterTypes.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(ParameterTypes)) : ParameterTypes;
    public SpecTermDeclaration Result { get; init; } = Result ?? throw new System.ArgumentNullException(nameof(Result));
    public string EvidenceSha256 { get; init; } = EvidenceSha256 ?? throw new System.ArgumentNullException(nameof(EvidenceSha256));
    public string EvidenceIdentity { get; init; } = EvidenceIdentity ?? throw new System.ArgumentNullException(nameof(EvidenceIdentity));
}
