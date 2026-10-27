#nullable enable
namespace SharpProof.Specs;

internal sealed record RelationalSpecPackCatalog(
    ImmutableDictionary<string, RelationalSpecPack> Packs,
    int Version, string EvidenceSha256)
{
    public ImmutableDictionary<string, RelationalSpecPack> Packs { get; init; } = Packs ?? throw new ArgumentNullException(nameof(Packs));
    public string EvidenceSha256 { get; init; } = EvidenceSha256 ?? throw new ArgumentNullException(nameof(EvidenceSha256));
}

internal sealed record RelationalSpecPack(
    string Id, string Version,
    ImmutableArray<RelationalSpecPackMethod> Methods)
{
    public string Id { get; init; } = Id ?? throw new ArgumentNullException(nameof(Id));
    public string Version { get; init; } = Version ?? throw new ArgumentNullException(nameof(Version));
    public ImmutableArray<RelationalSpecPackMethod> Methods { get; init; } = Methods.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Methods)) : Methods;
}

internal sealed record RelationalSpecPackMethod(
    string DocumentationCommentId,
    ImmutableArray<ApiSpecAssemblyIdentity> Assemblies,
    ImmutableArray<IrTypeKind> ParameterTypes,
    IrTypeKind ResultType, SpecTermDeclaration Result,
    string EvidenceSha256 = "", string EvidenceIdentity = "")
{
    public string DocumentationCommentId { get; init; } = DocumentationCommentId ?? throw new ArgumentNullException(nameof(DocumentationCommentId));
    public ImmutableArray<ApiSpecAssemblyIdentity> Assemblies { get; init; } = Assemblies.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Assemblies)) : Assemblies;
    public ImmutableArray<IrTypeKind> ParameterTypes { get; init; } = ParameterTypes.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(ParameterTypes)) : ParameterTypes;
    public SpecTermDeclaration Result { get; init; } = Result ?? throw new ArgumentNullException(nameof(Result));
    public string EvidenceSha256 { get; init; } = EvidenceSha256 ?? throw new ArgumentNullException(nameof(EvidenceSha256));
    public string EvidenceIdentity { get; init; } = EvidenceIdentity ?? throw new ArgumentNullException(nameof(EvidenceIdentity));
}

internal static class RelationalSpecPackCatalogData
{
    internal static RelationalSpecPackCatalog Catalog { get; } = Create();

    private static RelationalSpecPackCatalog Create()
    {
        var packs = ImmutableDictionary.CreateBuilder<string, RelationalSpecPack>(
            StringComparer.Ordinal);
        packs.Add(
            "dotnet.scalar",
            new RelationalSpecPack(
                "dotnet.scalar",
                "1",
                [
                    new RelationalSpecPackMethod(
                        "M:System.Math.Max(System.Int32,System.Int32)",
                        [new ApiSpecAssemblyIdentity("System.Private.CoreLib", "7cec85d7bea7798e"), new ApiSpecAssemblyIdentity("System.Runtime", "b03f5f7f11d50a3a"), new ApiSpecAssemblyIdentity("mscorlib", "b77a5c561934e089")],
                        [IrTypeKind.Integer, IrTypeKind.Integer],
                        IrTypeKind.Integer,
                        new SpecConditionalDeclaration(new SpecBinaryDeclaration(IrBinaryOperator.GreaterThanOrEqual, new SpecVariableDeclaration(SpecVariableRole.Parameter, 0, IrTypeKind.Integer), new SpecVariableDeclaration(SpecVariableRole.Parameter, 1, IrTypeKind.Integer), IrTypeKind.Boolean), new SpecVariableDeclaration(SpecVariableRole.Parameter, 0, IrTypeKind.Integer), new SpecVariableDeclaration(SpecVariableRole.Parameter, 1, IrTypeKind.Integer), IrTypeKind.Integer)),
                ]));
        return new RelationalSpecPackCatalog(
            packs.ToImmutable(),
            2,
            "53be86bbf2cd6b4c093caf96e308220809cbaa7cb39e0d475f84d34df5f6a178");
    }
}
