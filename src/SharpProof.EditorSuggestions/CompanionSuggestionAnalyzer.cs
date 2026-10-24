using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.EditorSuggestions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompanionSuggestionAnalyzer : DiagnosticAnalyzer
{
    internal const string DiagnosticId = CompanionSuggestionContract.DiagnosticId;
    internal const string FixKindProperty = CompanionSuggestionContract.FixKindProperty;
    internal const string FixKind = CompanionSuggestionContract.FixKind;
    internal const string PackageMarker = CompanionSuggestionContract.PackageMarker;

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Generate SharpProof contract companion",
        "Generate a SharpProof contract companion for '{0}'",
        "SharpProof.Editor",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Creates an editable companion with matching signatures and throwing placeholders. Add contract clauses explicitly.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        global::SharpProof.Internal.CodeFixInputValidation.Require(context, nameof(context));
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static startContext =>
        {
            global::SharpProof.Internal.CodeFixInputValidation.Require(startContext, nameof(startContext));
            if (!startContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(PackageMarker, out var enabled) ||
                !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            { return; }
            var compilation = startContext.Compilation;
            var contractFor = compilation.GetTypeByMetadataName("SharpProof.Attributes.ContractForAttribute");
            if (contractFor?.ContainingAssembly.Identity.Name != "SharpProof.Attributes")
            { return; }
            var existingTargets = CompanionEligibility.ExistingTargets(compilation, startContext.CancellationToken);
            startContext.RegisterSyntaxNodeAction(nodeContext =>
            {
                global::SharpProof.Internal.CodeFixInputValidation.Require(nodeContext, nameof(nodeContext));
                var declaration = (TypeDeclarationSyntax)nodeContext.Node;
                if (declaration.ContainsDiagnostics ||
                    nodeContext.SemanticModel.GetDeclaredSymbol(declaration, nodeContext.CancellationToken) is not INamedTypeSymbol target ||
                    !CompanionEligibility.IsSupported(target) || existingTargets.Contains(target.OriginalDefinition) ||
                    target.GetAttributes().Any(attribute =>
                        SymbolEqualityComparer.Default.Equals((attribute ?? throw new ArgumentNullException(nameof(attribute))).AttributeClass, contractFor)) ||
                    target.ContainingNamespace.GetTypeMembers(target.Name + "Contracts").Length != 0 ||
                    target.DeclaringSyntaxReferences.IsEmpty ||
                    target.DeclaringSyntaxReferences[0].SyntaxTree != declaration.SyntaxTree ||
                    target.DeclaringSyntaxReferences[0].Span != declaration.Span ||
                    !target.GetMembers().OfType<IMethodSymbol>().Any(static method =>
                        (method ?? throw new ArgumentNullException(nameof(method))) is { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false }) ||
                    !CompanionEligibility.HasNullableContext(target, nodeContext.SemanticModel, declaration))
                { return; }
                nodeContext.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(),
                    properties: ImmutableDictionary<string, string?>.Empty.Add(FixKindProperty, FixKind), messageArgs: [target.Name]));
            }, SyntaxKind.ClassDeclaration, SyntaxKind.InterfaceDeclaration, SyntaxKind.RecordDeclaration);
        });
    }
}
