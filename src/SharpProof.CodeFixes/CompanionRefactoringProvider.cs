using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace SharpProof.CodeFixes;

[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(CompanionRefactoringProvider)), Shared]
public sealed class CompanionRefactoringProvider : CodeRefactoringProvider
{
    public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var declaration = root?.FindNode(context.Span).FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (declaration == null)
        { return; }
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model?.GetDeclaredSymbol(declaration, context.CancellationToken) is not INamedTypeSymbol target ||
            !CompanionScaffolding.IsSupported(target) ||
            target.ContainingNamespace.GetTypeMembers(target.Name + "Contracts").Length != 0 ||
            !CompanionScaffolding.FindCompanions(target, model.Compilation, context.CancellationToken).IsEmpty ||
            model.Compilation.GetTypeByMetadataName("SharpProof.Attributes.ContractForAttribute")?.ContainingAssembly.Name != "SharpProof.Attributes")
        {
            return;
        }
        var annotationsEnabled = (model.GetNullableContext(declaration.SpanStart) & NullableContext.AnnotationsEnabled) != 0;
        if (!await CompanionScaffolding.HasNullableContextAsync(target, context.Document.Project, annotationsEnabled, context.CancellationToken).ConfigureAwait(false))
        { return; }
        MemberDeclarationSyntax companion = CompanionScaffolding.Companion(target);
        if (!target.ContainingNamespace.IsGlobalNamespace)
        {
            companion = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(CompanionScaffolding.NamespaceName(target.ContainingNamespace)))
                .AddMembers(companion);
        }
        var compilation = SyntaxFactory.CompilationUnit().AddMembers(companion)
            .WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(annotationsEnabled ? "#nullable enable\n" : "#nullable disable\n"))
            .NormalizeWhitespace()
            .WithAdditionalAnnotations(Formatter.Annotation);
        var name = target.Name + "Contracts.cs";
        var directory = context.Document.FilePath is { } sourcePath ? System.IO.Path.GetDirectoryName(sourcePath) : null;
        var path = directory == null ? null : System.IO.Path.Combine(directory, name);
        if (path != null && context.Document.Project.Documents.Any(document =>
            string.Equals((document ?? throw new ArgumentNullException(nameof(document))).FilePath, path, StringComparison.OrdinalIgnoreCase)))
        { return; }
        var generatedDocument = context.Document.Project.AddDocument(name, compilation.GetText(), context.Document.Folders, path);
        var tree = await generatedDocument.GetSyntaxTreeAsync(context.CancellationToken).ConfigureAwait(false);
        var generatedCompilation = await generatedDocument.Project.GetCompilationAsync(context.CancellationToken).ConfigureAwait(false);
        if (tree == null || generatedCompilation == null || generatedCompilation.GetDiagnostics(context.CancellationToken).Any(diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Severity == DiagnosticSeverity.Error && diagnostic.Location.SourceTree == tree))
        { return; }
        context.RegisterRefactoring(CodeAction.Create("Generate SharpProof contract companion", async cancellationToken =>
        {
            var document = await Formatter.FormatAsync(generatedDocument, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document;
        }, nameof(CompanionRefactoringProvider)));
    }
}
