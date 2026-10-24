#if SHARPPROOF_EDITOR_SUGGESTIONS
namespace SharpProof.EditorSuggestions;
#else
namespace SharpProof.CodeFixes;
#endif

internal static class CompanionSuggestionContract
{
    internal const string DiagnosticId = "SPED0001";
    internal const string FixKindProperty = "SharpProof.FixKind";
    internal const string FixKind = "GenerateCompanion";
    internal const string PackageMarker = "build_property.SharpProofEditorSuggestions";
}
