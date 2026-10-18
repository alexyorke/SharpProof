using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HmInferenceAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new("HM0001", "Consider a generic signature",
        "Body admits '{0}'. Review as an API change", "Design", DiagnosticSeverity.Info, true,
        description: "Experimental HM-style inference for a supported C# subset. Candidate signature and selected calls are compiler checked; API compatibility is not guaranteed.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetDiagnostics(start.CancellationToken).Any(d => d.Severity == DiagnosticSeverity.Error)) return;
            start.RegisterSyntaxNodeAction(node =>
            {
                var method = (MethodDeclarationSyntax)node.Node;
                var result = Inference.Analyze(method, node.SemanticModel, node.CancellationToken);
                if (result.Signature is not null)
                    node.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation(), result.Signature));
            }, SyntaxKind.MethodDeclaration);
        });
    }
}
