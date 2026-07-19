namespace SharpProof.Symbolic;

internal sealed class SymbolicSourceCompilationProfile(
    LanguageVersion languageVersion = LanguageVersion.Preview,
    IEnumerable<string>? preprocessorSymbols = null,
    NullableContextOptions nullableContext = NullableContextOptions.Disable,
    bool allowUnsafe = false,
    DocumentationMode documentationMode = DocumentationMode.Parse,
    Platform platform = Platform.AnyCpu,
    OptimizationLevel optimizationLevel = OptimizationLevel.Debug,
    string? assemblyName = null)
{
    public static readonly SymbolicSourceCompilationProfile Default = new();

    public LanguageVersion LanguageVersion { get; } = ValidateDefinedEnum(languageVersion, nameof(languageVersion));

    public ImmutableArray<string> PreprocessorSymbols { get; } = NormalizePreprocessorSymbols(preprocessorSymbols);

    public NullableContextOptions NullableContext { get; } =
        ValidateDefinedEnum(nullableContext, nameof(nullableContext));

    public bool AllowUnsafe { get; } = allowUnsafe;

    public DocumentationMode DocumentationMode { get; } =
        ValidateDefinedEnum(documentationMode, nameof(documentationMode));

    public Platform Platform { get; } = ValidateDefinedEnum(platform, nameof(platform));

    public OptimizationLevel OptimizationLevel { get; } =
        ValidateDefinedEnum(optimizationLevel, nameof(optimizationLevel));

    public string? AssemblyName { get; } = NormalizeAssemblyName(assemblyName);

    private static ImmutableArray<string> NormalizePreprocessorSymbols(IEnumerable<string>? symbols)
    {
        if (symbols == null) return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Preprocessor symbols cannot contain empty entries.", nameof(symbols));

            var normalized = symbol.Trim();
            if (!SyntaxFacts.IsValidIdentifier(normalized))
                throw new ArgumentException(
                    "Preprocessor symbol '" + normalized + "' is not a valid C# identifier.",
                    nameof(symbols));

            if (seen.Add(normalized)) builder.Add(normalized);
        }

        return builder.ToImmutable();
    }

    private static string? NormalizeAssemblyName(string? assemblyName)
    {
        if (assemblyName == null) return null;

        if (string.IsNullOrWhiteSpace(assemblyName))
            throw new ArgumentException("Assembly name cannot be empty.", nameof(assemblyName));

        return assemblyName.Trim();
    }

    private static TEnum ValidateDefinedEnum<TEnum>(TEnum value, string parameterName)
        where TEnum : struct
    {
        if (!Enum.IsDefined(typeof(TEnum), value))
            throw new ArgumentOutOfRangeException(parameterName, value, "Value is not defined.");
        return value;
    }
}
