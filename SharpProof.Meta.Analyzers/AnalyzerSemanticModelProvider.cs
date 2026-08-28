using Microsoft.CodeAnalysis;

namespace SharpProof.Meta.Analyzers;

/// <summary>
/// The meta-analyzer must inspect declarations that are reached indirectly
/// from an operation. This is its single, explicitly audited semantic-model
/// access point; product analyzers use the frontend host adapter instead.
/// </summary>
internal static class AnalyzerSemanticModelProvider
{
    internal static SemanticModel GetSemanticModel(
        Compilation compilation,
        SyntaxTree tree)
    {
#pragma warning disable RS0030, RS1030 // Audited meta-analyzer inspection boundary.
        return compilation.GetSemanticModel(tree, ignoreAccessibility: false);
#pragma warning restore RS0030, RS1030
    }
}
