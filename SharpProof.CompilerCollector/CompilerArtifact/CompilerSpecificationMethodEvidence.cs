namespace SharpProof.CompilerArtifact;

// Compiler binding evidence projected once from the selected audited catalog.
internal sealed record CompilerSpecificationMethodEvidence(
    string DocumentationCommentId,
    ImmutableArray<ApiSpecAssemblyIdentity> Assemblies,
    ImmutableArray<IrTypeKind> ParameterTypes,
    IrTypeKind ResultType, SpecTermDeclaration Result,
    string EvidenceSha256 = "", string EvidenceIdentity = "");
