using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Symbolic;

public sealed class SymbolicSourceCompilationProfile
{
    public static readonly SymbolicSourceCompilationProfile Default = new();

    public SymbolicSourceCompilationProfile(
        LanguageVersion languageVersion = LanguageVersion.Preview,
        IEnumerable<string>? preprocessorSymbols = null,
        NullableContextOptions nullableContext = NullableContextOptions.Disable,
        bool allowUnsafe = false,
        DocumentationMode documentationMode = DocumentationMode.Parse,
        Platform platform = Platform.AnyCpu,
        OptimizationLevel optimizationLevel = OptimizationLevel.Debug,
        string? assemblyName = null)
    {
        ValidateDefinedEnum(languageVersion, nameof(languageVersion));
        ValidateDefinedEnum(nullableContext, nameof(nullableContext));
        ValidateDefinedEnum(documentationMode, nameof(documentationMode));
        ValidateDefinedEnum(platform, nameof(platform));
        ValidateDefinedEnum(optimizationLevel, nameof(optimizationLevel));

        LanguageVersion = languageVersion;
        PreprocessorSymbols = NormalizePreprocessorSymbols(preprocessorSymbols);
        NullableContext = nullableContext;
        AllowUnsafe = allowUnsafe;
        DocumentationMode = documentationMode;
        Platform = platform;
        OptimizationLevel = optimizationLevel;
        AssemblyName = NormalizeAssemblyName(assemblyName);
    }

    public LanguageVersion LanguageVersion { get; }

    public ImmutableArray<string> PreprocessorSymbols { get; }

    public NullableContextOptions NullableContext { get; }

    public bool AllowUnsafe { get; }

    public DocumentationMode DocumentationMode { get; }

    public Platform Platform { get; }

    public OptimizationLevel OptimizationLevel { get; }

    public string? AssemblyName { get; }

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

    private static void ValidateDefinedEnum<TEnum>(TEnum value, string parameterName)
        where TEnum : struct
    {
        if (!Enum.IsDefined(typeof(TEnum), value))
            throw new ArgumentOutOfRangeException(parameterName, value, "Value is not defined.");
    }
}
