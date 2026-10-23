using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace SharpProof.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingCompanionMemberCodeFixProvider)), Shared]
public sealed class MissingCompanionMemberCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["SPCF0004"];
    public override FixAllProvider? GetFixAllProvider()
    {
        return null;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var contextDeclaration = root?.FindNode(context.Span).FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (contextDeclaration == null)
        { return; }
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(contextDeclaration, context.CancellationToken) is not INamedTypeSymbol contextType)
        { return; }
        // Missing-member diagnostics normally point to the target method, even
        // when its companion is declared in a different document.
        var target = (CompanionScaffolding.ContractTarget(contextType, model.Compilation) ?? contextType).OriginalDefinition;
        var companions = CompanionScaffolding.FindCompanions(target, model.Compilation, context.CancellationToken);
        if (companions.Length != 1)
        { return; }
        var companion = companions[0];
        var contractTarget = CompanionScaffolding.ContractTarget(companion, model.Compilation);
        if (contractTarget == null || !companion.IsStatic || companion.ContainingType != null || companion.IsFileLocal ||
            contractTarget.IsGenericType && !contractTarget.IsUnboundGenericType ||
            !CompanionScaffolding.IsSupported(target) || target.TypeParameters.Length != companion.TypeParameters.Length)
        { return; }
        for (var index = 0; index < target.TypeParameters.Length; index++)
        {
            if (target.TypeParameters[index].Name != companion.TypeParameters[index].Name)
            { return; }
        }
        var generator = SyntaxGenerator.GetGenerator(context.Document);
        var targetShape = (TypeDeclarationSyntax)generator.Declaration(target);
        var companionShape = (TypeDeclarationSyntax)generator.Declaration(companion);
        if (SyntaxFactory.List(targetShape.ConstraintClauses).ToFullString() != SyntaxFactory.List(companionShape.ConstraintClauses).ToFullString())
        { return; }
        var missing = CompanionScaffolding.MissingByName(companion, target);
        if (missing.IsEmpty)
        { return; }
        var reference = companion.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference == null || await reference.GetSyntaxAsync(context.CancellationToken).ConfigureAwait(false) is not ClassDeclarationSyntax declaration ||
            context.Document.Project.Solution.GetDocument(reference.SyntaxTree) is not { } document)
        { return; }
        var companionModel = await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var annotationsEnabled = (companionModel!.GetNullableContext(declaration.CloseBraceToken.SpanStart) & NullableContext.AnnotationsEnabled) != 0;
        if (!await CompanionScaffolding.HasNullableContextAsync(target, context.Document.Project, annotationsEnabled, context.CancellationToken).ConfigureAwait(false))
        { return; }
        var companionRoot = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var updated = declaration.AddMembers(missing.Select(static method => (MemberDeclarationSyntax)CompanionScaffolding.Method(method ?? throw new ArgumentNullException(nameof(method)))).ToArray());
        var updatedRoot = companionRoot!.ReplaceNode(declaration, updated);
        var changedDocument = document.WithSyntaxRoot(updatedRoot);
        var tree = await changedDocument.GetSyntaxTreeAsync(context.CancellationToken).ConfigureAwait(false);
        var changedCompilation = await changedDocument.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false);
        if (tree == null || changedCompilation == null || changedCompilation.GetDiagnostics(context.CancellationToken).Any(diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Severity == DiagnosticSeverity.Error && diagnostic.Location.SourceTree == tree))
        { return; }
        context.RegisterCodeFix(CodeAction.Create("Add missing SharpProof companion members", async cancellationToken =>
        {
            var changed = await Formatter.FormatAsync(changedDocument, Formatter.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
            return changed.Project.Solution;
        }, nameof(MissingCompanionMemberCodeFixProvider)), context.Diagnostics);
    }
}
