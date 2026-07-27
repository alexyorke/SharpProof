using System.Text.Json;
namespace SharpProof.CompilerArtifact;
#pragma warning disable IDE0055 // Compact evidence DTOs preserve the fixed production-size ceiling.

internal sealed class CompilerCompilationSnapshot {
    public string ProjectDirectory { get; set; } = string.Empty; public string AssemblyName { get; set; } = string.Empty;
    public string AssemblyIdentity { get; set; } = string.Empty; public string TargetFramework { get; set; } = string.Empty;
    public string CompilerVersion { get; set; } = string.Empty; public string CompilerMvid { get; set; } = string.Empty;
    public string CSharpCompilerVersion { get; set; } = string.Empty; public string CSharpCompilerMvid { get; set; } = string.Empty;
    public CompilerCompilationOptionsSnapshot Options { get; set; } = new();
    public CompilerSyntaxTreeSnapshot[] SyntaxTrees { get; set; } = []; public CompilerReferenceSnapshot[] References { get; set; } = [];
    public CompilerAdditionalFileSnapshot[] AdditionalFiles { get; set; } = [];
}
internal sealed class CompilerCompilationOptionsSnapshot {
    public string OutputKind { get; set; } = string.Empty; public string OptimizationLevel { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty; public string NullableContext { get; set; } = string.Empty;
    public string MetadataImportOptions { get; set; } = string.Empty;
    public bool CheckOverflow { get; set; } public bool AllowUnsafe { get; set; } public bool Deterministic { get; set; }
    public bool ReferencesSupersedeLowerVersions { get; set; }
    public string AssemblyIdentityComparer { get; set; } = string.Empty; public string[] Usings { get; set; } = [];
    public string ResolverPolicy { get; set; } = string.Empty;
}
internal sealed class CompilerSyntaxTreeSnapshot {
    public string Path { get; set; } = string.Empty; public string Sha256 { get; set; } = string.Empty;
    public string LanguageVersion { get; set; } = string.Empty; public string DocumentationMode { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string[] PreprocessorSymbols { get; set; } = []; public CompilerFeatureSnapshot[] Features { get; set; } = [];
}
internal sealed class CompilerReferenceSnapshot {
    public string Path { get; set; } = string.Empty; public string Kind { get; set; } = string.Empty;
    public bool EmbedInteropTypes { get; set; } public string[] Aliases { get; set; } = [];
    public string Identity { get; set; } = string.Empty; public string Sha256 { get; set; } = string.Empty;
}
internal sealed class CompilerAdditionalFileSnapshot {
    public string Path { get; set; } = string.Empty; public string Sha256 { get; set; } = string.Empty;
}
internal sealed class CompilerFeatureSnapshot {
    public string Key { get; set; } = string.Empty; public string Value { get; set; } = string.Empty;
}

internal static class CompilationFingerprint {
    internal static string ComputeSha256(CompilerCompilationSnapshot snapshot) {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        using var hash = new CanonicalHashWriter();
        hash.Add("SharpProof.CompilerCompilationSnapshot", 4, JsonSerializer.Serialize(snapshot, WorkerProtocolJson.Options));
        return hash.Finish();
    }
    internal static void ValidateShape(CompilerCompilationSnapshot snapshot) {
        if (snapshot == null || !Path.IsPathRooted(snapshot.ProjectDirectory) || string.IsNullOrWhiteSpace(snapshot.AssemblyName) ||
            string.IsNullOrWhiteSpace(snapshot.AssemblyIdentity) || string.IsNullOrWhiteSpace(snapshot.TargetFramework) ||
            string.IsNullOrWhiteSpace(snapshot.CompilerVersion) || !Guid.TryParseExact(snapshot.CompilerMvid, "D", out _) ||
            string.IsNullOrWhiteSpace(snapshot.CSharpCompilerVersion) || !Guid.TryParseExact(snapshot.CSharpCompilerMvid, "D", out _) ||
            !ValidOptions(snapshot.Options) || snapshot.SyntaxTrees == null || snapshot.SyntaxTrees.Any(static tree => !ValidTree(tree)) ||
            snapshot.References == null || snapshot.References.Any(static reference => !ValidReference(reference)) ||
            !ValidAdditionalFiles(snapshot.AdditionalFiles))
            throw new JsonException("The compiler compilation evidence is invalid.");
    }
    private static bool ValidFeatures(CompilerFeatureSnapshot[]? values) => values != null && values.All(
        static value => value != null && !string.IsNullOrWhiteSpace(value.Key) && value.Value != null);
    private static bool ValidOptions(CompilerCompilationOptionsSnapshot? value) => value != null &&
        IsOneOf(value.OutputKind, "ConsoleApplication", "WindowsApplication", "DynamicallyLinkedLibrary", "NetModule",
            "WindowsRuntimeMetadata", "WindowsRuntimeApplication") &&
        IsOneOf(value.OptimizationLevel, "Debug", "Release") &&
        IsOneOf(value.Platform, "AnyCpu", "AnyCpu32BitPreferred", "Arm", "Arm64", "Itanium", "X64", "X86") &&
        IsOneOf(value.NullableContext, "Disable", "Warnings", "Annotations", "Enable") &&
        IsOneOf(value.MetadataImportOptions, "Public", "Internal", "All") && !value.ReferencesSupersedeLowerVersions &&
        IsOneOf(value.AssemblyIdentityComparer, "Default", "Desktop") && value.Usings != null &&
        value.Usings.All(static item => !string.IsNullOrWhiteSpace(item)) && value.ResolverPolicy == "EvidenceOnly";
    private static bool ValidTree(CompilerSyntaxTreeSnapshot? value) => value != null && WorkerProtocolJson.IsSha256(value.Sha256) &&
        !string.IsNullOrWhiteSpace(value.LanguageVersion) && IsOneOf(value.DocumentationMode, "None", "Parse", "Diagnose") &&
        IsOneOf(value.Kind, "Regular", "Script") && value.PreprocessorSymbols != null &&
        value.PreprocessorSymbols.All(static item => !string.IsNullOrWhiteSpace(item)) && ValidFeatures(value.Features);
    private static bool ValidReference(CompilerReferenceSnapshot? value) => value != null && Path.IsPathRooted(value.Path) &&
        IsOneOf(value.Kind, "Assembly", "Module") && value.Aliases != null &&
        value.Aliases.All(static item => !string.IsNullOrWhiteSpace(item)) &&
        !string.IsNullOrWhiteSpace(value.Identity) && WorkerProtocolJson.IsSha256(value.Sha256);
    private static bool ValidAdditionalFiles(CompilerAdditionalFileSnapshot[]? values) {
        if (values == null) return false;
        var paths = new HashSet<string>(StringComparer.Ordinal); CompilerAdditionalFileSnapshot? previous = null;
        foreach (var value in values) { if (!ValidAdditionalFile(value) || !paths.Add(value!.Path) ||
            previous != null && Compare(previous, value) >= 0) return false; previous = value; }
        return true; }
    private static bool ValidAdditionalFile(CompilerAdditionalFileSnapshot? value) {
        if (value == null || !Path.IsPathRooted(value.Path) || !WorkerProtocolJson.IsSha256(value.Sha256)) return false;
        try { return Path.GetFullPath(value.Path).Replace('\\', '/') == value.Path; }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException) { return false; } }
    private static int Compare(CompilerAdditionalFileSnapshot left, CompilerAdditionalFileSnapshot right) {
        var path = StringComparer.Ordinal.Compare(left.Path, right.Path);
        return path != 0 ? path : StringComparer.Ordinal.Compare(left.Sha256, right.Sha256); }
    private static bool IsOneOf(string value, params string[] expected) =>
        expected.Contains(value, StringComparer.Ordinal);
}
