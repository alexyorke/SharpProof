using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.Analyzer;

internal static partial class CommonBugAnalyzer
{
    internal static void AnalyzeSyntaxTree(
        SyntaxTreeAnalysisContext context,
        AnalyzerSession session)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (var directive in root.DescendantTrivia(descendIntoTrivia: true)
                     .Select(static trivia => trivia.GetStructure()))
            switch (directive)
            {
                case PragmaWarningDirectiveTriviaSyntax pragma
                    when pragma.IsActive &&
                         pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword) &&
                         pragma.ErrorCodes.Count == 0:
                    Report(
                        context,
                        session,
                        SharpProofDiagnostics.SuppressionWithoutJustificationRule,
                        pragma.GetLocation(),
                        "broad_pragma_suppression",
                        pragma.ToString());
                    break;
                case NullableDirectiveTriviaSyntax nullable
                    when nullable.IsActive && nullable.SettingToken.IsKind(SyntaxKind.DisableKeyword):
                    Report(
                        context,
                        session,
                        SharpProofDiagnostics.NullableAnalysisDisabledRule,
                        nullable.SettingToken.GetLocation(),
                        "nullable_analysis_disabled");
                    break;
            }
    }

    internal static void AnalyzeSuppressionAttribute(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        if (context.Node is not AttributeSyntax attribute ||
            context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not
                IMethodSymbol constructor ||
            constructor.ContainingType.ToDisplayString() is not
                ("System.Diagnostics.CodeAnalysis.SuppressMessageAttribute" or
                "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute"))
            return;

        var justification = attribute.ArgumentList?.Arguments.FirstOrDefault(argument =>
            string.Equals(argument.NameEquals?.Name.Identifier.ValueText, "Justification", StringComparison.Ordinal));
        if (justification != null &&
            context.SemanticModel.GetConstantValue(justification.Expression, context.CancellationToken) is
                { HasValue: true, Value: string value } &&
            !string.IsNullOrWhiteSpace(value))
            return;

        Report(
            context,
            session,
            SharpProofDiagnostics.SuppressionWithoutJustificationRule,
            attribute.GetLocation(),
            "attribute_suppression_without_justification",
            attribute.Name.ToString());
    }
}
