using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HmInferenceAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new("HM0001", "Consider a generic signature", "Body admits '{0}'. Review as an API change.", "Design", DiagnosticSeverity.Info, true, description: "Experimental HM-style inference for a supported C# subset. Candidate signature and selected calls are compiler checked; API compatibility is not guaranteed.");
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
        {
            throw new global::System.ArgumentNullException(nameof(context));
        }

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetDiagnostics(start.CancellationToken).Any(d =>
            {
                if (d is null)
                {
                    throw new global::System.ArgumentNullException(nameof(d));
                }

                return Inference.IsCompilationError(d);
            }))
            {
                return;
            }

            start.RegisterSyntaxNodeAction((SyntaxNodeAnalysisContext /* SPINT001: all values valid - Roslyn supplies initialized method declaration contexts for this registered callback. */ node) =>
            {
                var method = (MethodDeclarationSyntax)node.Node;
                var result = Inference.Analyze(method, node.SemanticModel, node.CancellationToken);
                if (result.Signature is not null)
                {
                    var properties = ImmutableDictionary<string, string?>.Empty;
                    if (result.RecursiveGroup is { } group)
                    {
                        properties = properties.Add(RecursiveGroupInfo.DiagnosticAnchorProperty,
                            group.AnchorStart.ToString(global::System.Globalization.CultureInfo.InvariantCulture))
                            .Add(RecursiveGroupInfo.DiagnosticCountProperty,
                                group.Signatures.Count.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
                        for (var i = 0; i < group.Signatures.Count; i++)
                        { properties = properties.Add(RecursiveGroupInfo.DiagnosticSignaturePrefix + i, group.Signatures[i]); }
                    }
                    node.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation(), properties,
                        result.Signature));
                }
            }, SyntaxKind.MethodDeclaration);
        });
    }
}
