namespace SharpProof.CompilerProbe.TestAsset;

/// <summary>
/// Names shared by the package-backed final-compilation probe fixtures.
/// </summary>
public static class CompilerProbeContract
{
    /// <summary>The probe JSON schema name.</summary>
    public const string SchemaName = "SharpProof.CompilerProbe";

    /// <summary>The probe JSON schema version.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The MSBuild property carrying the analyzer output path.</summary>
    public const string OutputPathPropertyName = "SharpProofProbeOutputPath";

    /// <summary>The analyzer-config key for <see cref="OutputPathPropertyName"/>.</summary>
    public const string OutputPathOptionKey =
        "build_property.SharpProofProbeOutputPath";

    /// <summary>The MSBuild property consumed by the fixture generator.</summary>
    public const string GlobalValuePropertyName = "SharpProofProbeGlobalValue";

    /// <summary>The analyzer-config key for <see cref="GlobalValuePropertyName"/>.</summary>
    public const string GlobalValueOptionKey =
        "build_property.SharpProofProbeGlobalValue";

    /// <summary>The AdditionalFile name consumed by the fixture generator.</summary>
    public const string AdditionalFileName = "SharpProofProbeInput.txt";

    /// <summary>The AdditionalFile metadata consumed by the fixture generator.</summary>
    public const string AdditionalFileMetadataName = "SharpProofProbeMetadata";

    /// <summary>The analyzer-config key for <see cref="AdditionalFileMetadataName"/>.</summary>
    public const string AdditionalFileMetadataOptionKey =
        "build_metadata.AdditionalFiles.SharpProofProbeMetadata";

    /// <summary>The generated global-using syntax-tree hint name.</summary>
    public const string GlobalUsingsHintName =
        "SharpProofProbe.GlobalUsings.g.cs";

    /// <summary>The generated contract syntax-tree hint name.</summary>
    public const string ContractHintName = "SharpProofProbe.Contract.g.cs";

    /// <summary>The metadata name of the generated fixture type.</summary>
    public const string GeneratedTypeMetadataName =
        "SharpProof.CompilerProbe.Generated.ProbeGenerated";

    /// <summary>The name of the generated method containing a contract.</summary>
    public const string GeneratedMethodName = "Verify";

    /// <summary>The analyzer diagnostic emitted only when probe output fails.</summary>
    public const string FailureDiagnosticId = "SPPROBE001";

    /// <summary>Gets the absolute path of the fixture analyzer/generator assembly.</summary>
    public static string AssemblyPath => typeof(CompilerProbeContract).Assembly.Location;
}
