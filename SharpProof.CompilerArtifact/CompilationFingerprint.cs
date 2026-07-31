using System.Text.Json;

namespace SharpProof.CompilerArtifact;

internal static class CompilationFingerprint
{
    private const string RuntimeContractEvaluationSymbol =
        "SHARPPROOF_CONTRACTS";

    internal static string ComputeSha256(CompilerCompilationSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        using var hash = new CanonicalHashWriter();
        hash.Add(
            "SharpProof.CompilerCompilationSnapshot",
            6,
            JsonSerializer.Serialize(snapshot, WorkerProtocolJson.Options));
        return hash.Finish();
    }

    internal static void ValidateShape(CompilerCompilationSnapshot snapshot)
    {
        if (!ValidSnapshot(snapshot))
        {
            throw new JsonException("The compiler compilation evidence is invalid.");
        }
    }

    private static bool ValidSnapshot(CompilerCompilationSnapshot? value)
    {
        return value != null &&
        Path.IsPathRooted(value.ProjectDirectory) &&
        HasText(value.AssemblyName) &&
        HasText(value.AssemblyIdentity) &&
        HasText(value.TargetFramework) &&
        HasText(value.CompilerVersion) &&
        Guid.TryParseExact(value.CompilerMvid, "D", out _) &&
        HasText(value.CSharpCompilerVersion) &&
        Guid.TryParseExact(value.CSharpCompilerMvid, "D", out _) &&
        ValidOptions(value.Options) &&
        All(value.SyntaxTrees, ValidTree) &&
        All(value.References, ValidReference) &&
        ValidAdditionalFiles(value.AdditionalFiles);
    }

    private static bool ValidOptions(CompilerCompilationOptionsSnapshot? value)
    {
        return value != null &&
        Enum.IsDefined(typeof(CompilerOutputKind), value.OutputKind) &&
        Enum.IsDefined(typeof(CompilerOptimizationLevel), value.OptimizationLevel) &&
        Enum.IsDefined(typeof(CompilerPlatform), value.Platform) &&
        Enum.IsDefined(typeof(CompilerNullableContext), value.NullableContext) &&
        Enum.IsDefined(typeof(CompilerMetadataImportOptions), value.MetadataImportOptions) &&
        !value.ReferencesSupersedeLowerVersions &&
        Enum.IsDefined(typeof(CompilerAssemblyIdentityComparer), value.AssemblyIdentityComparer) &&
        All(value.Usings, HasText) &&
        value.ResolverPolicy == CompilerResolverPolicy.EvidenceOnly;
    }

    private static bool ValidTree(CompilerSyntaxTreeSnapshot? value)
    {
        return value != null &&
        WorkerProtocolJson.IsSha256(value.Sha256) &&
        value.TextLength >= 0 &&
        HasText(value.LanguageVersion) &&
        value.DocumentationMode is "None" or "Parse" or "Diagnose" &&
        value.Kind is "Regular" or "Script" &&
        All(value.PreprocessorSymbols, HasText) &&
        All(value.EffectivePreprocessorSymbols, HasText) &&
        !value.EffectivePreprocessorSymbols.Contains(
            RuntimeContractEvaluationSymbol,
            StringComparer.Ordinal) &&
        All(value.Features, ValidFeature);
    }

    private static bool ValidFeature(CompilerFeatureSnapshot? value)
    {
        return value != null && HasText(value.Key) && value.Value != null;
    }

    private static bool ValidReference(CompilerReferenceSnapshot? value)
    {
        return value != null &&
        Path.IsPathRooted(value.Path) &&
        value.Kind is "Assembly" or "Module" &&
        All(value.Aliases, HasText) &&
        HasText(value.Identity) &&
        WorkerProtocolJson.IsSha256(value.Sha256);
    }

    private static bool ValidAdditionalFiles(CompilerAdditionalFileSnapshot[]? values)
    {
        return values != null &&
        All(values, ValidAdditionalFile) &&
        values.Select(static value => value.Path).Distinct(StringComparer.Ordinal).Count() == values.Length &&
        values.Zip(values.Skip(1), static (left, right) => Compare(left, right) < 0).All(static ordered => ordered);
    }

    private static bool ValidAdditionalFile(CompilerAdditionalFileSnapshot? value)
    {
        if (value == null ||
            !Path.IsPathRooted(value.Path) ||
            !WorkerProtocolJson.IsSha256(value.Sha256))
        {
            return false;
        }

        try
        {
            return Path.GetFullPath(value.Path).Replace('\\', '/') == value.Path;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static int Compare(
        CompilerAdditionalFileSnapshot left,
        CompilerAdditionalFileSnapshot right)
    {
        var path = StringComparer.Ordinal.Compare(left.Path, right.Path);
        return path != 0 ? path : StringComparer.Ordinal.Compare(left.Sha256, right.Sha256);
    }

    private static bool All<T>(T[]? values, Func<T, bool> predicate)
    {
        return values != null && values.All(predicate);
    }

    private static bool HasText(string value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }
}
